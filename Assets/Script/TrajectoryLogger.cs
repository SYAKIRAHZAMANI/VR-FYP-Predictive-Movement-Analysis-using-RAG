using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records a rich per-frame motor-learning dataset for a thrown object
/// (position, quaternion rotation, linear velocity, linear acceleration,
/// angular velocity, and the HMD/head reference frame), from
/// BeginRecording() to StopRecordingAndSave().
///
/// ALL THROWS IN A SESSION ARE APPENDED TO ONE SINGLE CSV FILE, distinguished
/// by a trial_id column, with a per-row "outcome" column (Made/Miss). This
/// avoids generating one file per throw and keeps a full session's data
/// (both makes and misses) together for training.
///
/// Velocity and angular velocity are computed from position/rotation deltas
/// WHILE HELD (since a kinematic Rigidbody always reports zero physics
/// velocity), and switch to real Rigidbody physics once thrown - so these
/// columns are never spuriously zero during the important "wind-up" swing.
/// </summary>
public class TrajectoryLogger : MonoBehaviour
{
    [Header("What to record")]
    [Tooltip("Transform to log as the 'hand'. Defaults to this object (the ball) if left empty.")]
    [SerializeField] private Transform trackedTransform;

    [Tooltip("The HMD/head transform (e.g. Main Camera under XR Origin), used to log a " +
             "body-relative reference frame each sample. ASSIGN THIS EXPLICITLY in the " +
             "Inspector - Camera.main fallback only works if your camera is tagged MainCamera.")]
    [SerializeField] private Transform headTransform;

    [Header("Sampling")]
    [SerializeField] private float sampleInterval = 0.02f;

    [Tooltip("Minimum number of samples required to save a trial. Filters out " +
             "accidental brief taps/grabs that aren't real throw attempts.")]
    [SerializeField] private int minimumSamplesToSave = 10; // ~0.2s at 50Hz

    [Tooltip("If RecordOutcome() isn't called within this many seconds after release, " +
             "the trial is auto-labeled 'Miss' and saved anyway.")]
    [SerializeField] private float outcomeTimeoutSeconds = 3f;

    [Header("Output")]
    [Tooltip("Folder name inside the project's Assets folder where the session CSV is saved.")]
    [SerializeField] private string outputFolderName = "TrajectoryData";

    [Header("Session Metadata")]
    [SerializeField] private string participantId = "dev_test";
    [SerializeField] private int trialNumber = 0;

    [Tooltip("Session ends once this many SUCCESSFUL (made) shots have been recorded. " +
             "Set to 0 for unlimited.")]
    [SerializeField] private int targetSuccessfulShots = 10;

    [SerializeField] private int successfulShotCount = 0;

    [Tooltip("Safety cap on TOTAL attempts (made + missed). Set to 0 for unlimited.")]
    [SerializeField] private int maxTotalAttempts = 50;

    public bool SessionComplete =>
        (targetSuccessfulShots > 0 && successfulShotCount >= targetSuccessfulShots) ||
        (maxTotalAttempts > 0 && trialNumber >= maxTotalAttempts);

    public int SuccessfulShotCount => successfulShotCount;
    public int TrialNumber => trialNumber;

    /// <summary>
    /// Fires as soon as a throw's samples are ready (right after
    /// StopRecordingAndSave passes the minimum-samples check), independent
    /// of BasketDetector's physics-based outcome. Subscribe to this from
    /// AICoachClient to send the trial to the AI backend for prediction
    /// without waiting for RecordOutcome()/timeout.
    /// </summary>
    public event Action<List<PredictionSample>> OnThrowReadyForPrediction;

    /// <summary>
    /// Public, JSON-serializable sample shape matching exactly the field
    /// names model_inference.py's FEATURE_COLS expects on the backend.
    /// </summary>
    [Serializable]
    public class PredictionSample
    {
        public float hand_x, hand_y, hand_z;
        public float hand_rot_x, hand_rot_y, hand_rot_z, hand_rot_w;
        public float linear_vx, linear_vy, linear_vz;
        public float linear_ax, linear_ay, linear_az;
        public float angular_vx, angular_vy, angular_vz;
    }

    private Rigidbody trackedRigidbody;
    private Coroutine recordingRoutine;
    private Coroutine outcomeTimeoutRoutine;

    private struct Sample
    {
        public float time;
        public float x, y, z;
        public float rotX, rotY, rotZ, rotW;
        public float velX, velY, velZ;
        public float accX, accY, accZ;
        public float angVelX, angVelY, angVelZ;
        public float headX, headY, headZ;
        public float headRotX, headRotY, headRotZ, headRotW;
    }

    private readonly List<Sample> currentTrajectory = new List<Sample>();
    private float recordStartTime;

    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private Vector3 previousVelocity;
    private bool havePreviousSample;

    private List<Sample> pendingTrajectory = null;
    private int pendingTrialNumber;

    private string sessionFilePath = null;

    private void Awake()
    {
        if (trackedTransform == null)
        {
            trackedTransform = transform;
        }
        trackedRigidbody = trackedTransform.GetComponent<Rigidbody>();

        if (headTransform == null)
        {
            if (Camera.main != null)
            {
                headTransform = Camera.main.transform;
                Debug.LogWarning("[TrajectoryLogger] Head Transform was not assigned - falling back to " +
                                  "Camera.main. Assign it explicitly in the Inspector to be safe.");
            }
            else
            {
                Debug.LogWarning("[TrajectoryLogger] Head Transform is not assigned AND Camera.main is null " +
                                  "(your XR camera may not be tagged 'MainCamera'). head_x/y/z columns will " +
                                  "be logged as 0 until this is fixed. Drag your Main Camera into the " +
                                  "Head Transform field on this component.");
            }
        }
    }

    private void OnDisable()
    {
        if (recordingRoutine != null)
        {
            StopCoroutine(recordingRoutine);
            recordingRoutine = null;
            Debug.Log("[TrajectoryLogger] Recording interrupted (scene stopped or object disabled) - discarding, not saved.");
        }

        if (outcomeTimeoutRoutine != null)
        {
            StopCoroutine(outcomeTimeoutRoutine);
            outcomeTimeoutRoutine = null;
        }
    }

    public void SetParticipantId(string id)
    {
        participantId = id;
    }

    public void ResetSession(string newParticipantId = null)
    {
        if (!string.IsNullOrEmpty(newParticipantId))
        {
            participantId = newParticipantId;
        }
        trialNumber = 0;
        successfulShotCount = 0;
        pendingTrajectory = null;
        sessionFilePath = null;
        Debug.Log($"[TrajectoryLogger] Session reset. Participant: {participantId}");
    }

    public void BeginRecording()
    {
        if (SessionComplete)
        {
            Debug.Log($"[TrajectoryLogger] Session complete ({successfulShotCount}/{targetSuccessfulShots} makes, " +
                      $"{trialNumber}/{maxTotalAttempts} total attempts). Not recording further throws.");
            return;
        }

        currentTrajectory.Clear();
        recordStartTime = Time.time;
        havePreviousSample = false;

        if (recordingRoutine != null)
        {
            StopCoroutine(recordingRoutine);
        }
        recordingRoutine = StartCoroutine(RecordTrajectory());

        Debug.Log($"[TrajectoryLogger] Recording started on '{gameObject.name}'. " +
                  $"Makes so far: {successfulShotCount}/{targetSuccessfulShots}.");
    }

    public void StopRecordingAndSave()
    {
        if (recordingRoutine != null)
        {
            StopCoroutine(recordingRoutine);
            recordingRoutine = null;
        }

        Debug.Log($"[TrajectoryLogger] Recording stopped. Samples captured: {currentTrajectory.Count}");

        if (currentTrajectory.Count < minimumSamplesToSave)
        {
            Debug.LogWarning($"[TrajectoryLogger] Discarded - only {currentTrajectory.Count} samples " +
                              $"(minimum {minimumSamplesToSave} required). Not saved. Does not count toward attempts.");
            return;
        }

        pendingTrajectory = new List<Sample>(currentTrajectory);
        pendingTrialNumber = trialNumber;
        trialNumber++;

        if (OnThrowReadyForPrediction != null)
        {
            OnThrowReadyForPrediction.Invoke(ConvertToPredictionSamples(pendingTrajectory));
        }

        if (outcomeTimeoutRoutine != null)
        {
            StopCoroutine(outcomeTimeoutRoutine);
        }
        outcomeTimeoutRoutine = StartCoroutine(OutcomeTimeoutFallback());
    }

    private List<PredictionSample> ConvertToPredictionSamples(List<Sample> trajectory)
    {
        var result = new List<PredictionSample>(trajectory.Count);
        foreach (var s in trajectory)
        {
            result.Add(new PredictionSample
            {
                hand_x = s.x,
                hand_y = s.y,
                hand_z = s.z,
                hand_rot_x = s.rotX,
                hand_rot_y = s.rotY,
                hand_rot_z = s.rotZ,
                hand_rot_w = s.rotW,
                linear_vx = s.velX,
                linear_vy = s.velY,
                linear_vz = s.velZ,
                linear_ax = s.accX,
                linear_ay = s.accY,
                linear_az = s.accZ,
                angular_vx = s.angVelX,
                angular_vy = s.angVelY,
                angular_vz = s.angVelZ
            });
        }
        return result;
    }

    private IEnumerator OutcomeTimeoutFallback()
    {
        yield return new WaitForSeconds(outcomeTimeoutSeconds);

        if (pendingTrajectory != null)
        {
            Debug.Log($"[TrajectoryLogger] No outcome reported within {outcomeTimeoutSeconds}s - " +
                      "auto-labeling as 'Miss'.");
            FinalizeAndSave("Miss");
        }
    }

    public void RecordOutcome(string outcome)
    {
        if (outcomeTimeoutRoutine != null)
        {
            StopCoroutine(outcomeTimeoutRoutine);
            outcomeTimeoutRoutine = null;
        }

        if (outcome == "Made")
        {
            successfulShotCount++;
            Debug.Log($"[TrajectoryLogger] Make recorded. Progress: {successfulShotCount}/{targetSuccessfulShots}.");
        }

        FinalizeAndSave(outcome);

        if (outcome == "Made" && SessionComplete)
        {
            Debug.Log($"[TrajectoryLogger] Target reached - session complete at {successfulShotCount} makes " +
                      $"over {trialNumber} total attempts.");
        }
    }

    private void FinalizeAndSave(string outcome)
    {
        if (pendingTrajectory == null)
        {
            return;
        }

        AppendTrajectoryToSessionCsv(pendingTrajectory, pendingTrialNumber, outcome);
        pendingTrajectory = null;
    }

    private IEnumerator RecordTrajectory()
    {
        var wait = new WaitForSeconds(sampleInterval);

        while (true)
        {
            Vector3 pos = trackedTransform.position;
            Quaternion rot = trackedTransform.rotation;

            Vector3 vel;
            Vector3 angVel;

            bool useRealPhysics = trackedRigidbody != null && !trackedRigidbody.isKinematic;

            if (useRealPhysics)
            {
                vel = trackedRigidbody.velocity;
                angVel = trackedRigidbody.angularVelocity;
            }
            else if (havePreviousSample)
            {
                vel = (pos - previousPosition) / sampleInterval;

                Quaternion deltaRot = rot * Quaternion.Inverse(previousRotation);
                deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (angleDeg > 180f) angleDeg -= 360f;
                angVel = axis * (angleDeg * Mathf.Deg2Rad / sampleInterval);
            }
            else
            {
                vel = Vector3.zero;
                angVel = Vector3.zero;
            }

            Vector3 acc = havePreviousSample ? (vel - previousVelocity) / sampleInterval : Vector3.zero;

            previousPosition = pos;
            previousRotation = rot;
            previousVelocity = vel;
            havePreviousSample = true;

            Vector3 headPos = Vector3.zero;
            Quaternion headRot = Quaternion.identity;
            if (headTransform != null)
            {
                headPos = headTransform.position;
                headRot = headTransform.rotation;
            }

            currentTrajectory.Add(new Sample
            {
                time = Time.time - recordStartTime,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                rotX = rot.x,
                rotY = rot.y,
                rotZ = rot.z,
                rotW = rot.w,
                velX = vel.x,
                velY = vel.y,
                velZ = vel.z,
                accX = acc.x,
                accY = acc.y,
                accZ = acc.z,
                angVelX = angVel.x,
                angVelY = angVel.y,
                angVelZ = angVel.z,
                headX = headPos.x,
                headY = headPos.y,
                headZ = headPos.z,
                headRotX = headRot.x,
                headRotY = headRot.y,
                headRotZ = headRot.z,
                headRotW = headRot.w
            });

            yield return wait;
        }
    }

    private void AppendTrajectoryToSessionCsv(List<Sample> trajectory, int forTrialNumber, string outcome)
    {
        if (trajectory == null || trajectory.Count == 0)
        {
            Debug.LogWarning("[TrajectoryLogger] No samples to save.");
            return;
        }

        string folderPath = Path.Combine(Application.dataPath, outputFolderName);
        Directory.CreateDirectory(folderPath);

        bool isNewFile = sessionFilePath == null;
        if (isNewFile)
        {
            string fileName = $"{participantId}_session_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
            sessionFilePath = Path.Combine(folderPath, fileName);
        }

        var sb = new StringBuilder();

        if (isNewFile)
        {
            sb.AppendLine(
                "trial_id,timestamp_s,hand_x,hand_y,hand_z," +
                "hand_rot_x,hand_rot_y,hand_rot_z,hand_rot_w," +
                "linear_vx,linear_vy,linear_vz," +
                "linear_ax,linear_ay,linear_az," +
                "angular_vx,angular_vy,angular_vz," +
                "head_x,head_y,head_z," +
                "head_rot_x,head_rot_y,head_rot_z,head_rot_w," +
                "outcome");
        }

        string trialId = $"{participantId}_{forTrialNumber:D3}";

        foreach (var s in trajectory)
        {
            sb.AppendLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1:F4},{2:F4},{3:F4},{4:F4}," +
                "{5:F5},{6:F5},{7:F5},{8:F5}," +
                "{9:F3},{10:F3},{11:F3}," +
                "{12:F3},{13:F3},{14:F3}," +
                "{15:F3},{16:F3},{17:F3}," +
                "{18:F4},{19:F4},{20:F4}," +
                "{21:F5},{22:F5},{23:F5},{24:F5}," +
                "{25}",
                trialId, s.time, s.x, s.y, s.z,
                s.rotX, s.rotY, s.rotZ, s.rotW,
                s.velX, s.velY, s.velZ,
                s.accX, s.accY, s.accZ,
                s.angVelX, s.angVelY, s.angVelZ,
                s.headX, s.headY, s.headZ,
                s.headRotX, s.headRotY, s.headRotZ, s.headRotW,
                outcome));
        }

        File.AppendAllText(sessionFilePath, sb.ToString());
        Debug.Log($"[TrajectoryLogger] Appended {trajectory.Count} samples (trial {trialId}, outcome: {outcome}) " +
                  $"to session file: {sessionFilePath}");
    }
}