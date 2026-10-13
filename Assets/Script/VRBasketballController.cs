using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class VRBasketballController : MonoBehaviour
{
    [Header("Throw Physics Tuning")]
    [Range(1.0f, 3.0f)] public float throwPowerMultiplier = 1.5f;
    [Range(0.0f, 2.0f)] public float upwardArcAssist = 0.5f;

    [Header("Accidental Drop Filter (Fix #1)")]
    [Tooltip("Throws below this speed (m/s) are treated as accidental drops, NOT shots!")]
    public float minimumThrowSpeedThreshold = 2.5f;

    [Header("Auto-Respawn System (Fix #3)")]
    [Tooltip("Seconds after a throw before the ball respawns")]
    public float throwRespawnDelay = 4.0f;
    [Tooltip("Seconds after dropping on the floor before auto-teleporting back")]
    public float idleFloorRespawnDelay = 3.0f;
    public Transform customSpawnPoint;

    [Header("References")]
    public TrajectoryLogger trajectoryLogger;

    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private Transform activeControllerTransform;
    private bool isHeld = false;

    // Rolling velocity buffer
    private Queue<Vector3> positionHistory = new Queue<Vector3>();
    private Queue<float> timeHistory = new Queue<float>();
    private const int HistoryFrames = 8;

    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;
    private Coroutine respawnCoroutine;
    private float floorTimer = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        if (trajectoryLogger == null)
            trajectoryLogger = GetComponent<TrajectoryLogger>();

        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;

        grabInteractable.throwOnDetach = false;
    }

    private void OnEnable()
    {
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    private void OnDisable()
    {
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        isHeld = true;
        floorTimer = 0f;
        activeControllerTransform = args.interactorObject.transform;

        positionHistory.Clear();
        timeHistory.Clear();

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        
        // This automatically picks whichever controller is interacting with the ball!
        // Whether it's Left or Right, this will link to it.
        activeControllerTransform = args.interactorObject.transform;


        // Only start recording kinematics
        trajectoryLogger?.BeginRecording();
    }

    private void Update()
    {
        // Track hand speed when held
        if (isHeld && activeControllerTransform != null)
        {
            positionHistory.Enqueue(activeControllerTransform.position);
            timeHistory.Enqueue(Time.time);

            if (positionHistory.Count > HistoryFrames)
            {
                positionHistory.Dequeue();
                timeHistory.Dequeue();
            }
        }
        // Fix #3: If the ball is loose on the floor or rolled away, auto-respawn it!
        else if (!isHeld)
        {
            // If ball is below waist level or moving very slowly on the court
            if (transform.position.y < (initialSpawnPosition.y - 0.3f) || rb.velocity.magnitude < 0.5f)
            {
                floorTimer += Time.deltaTime;
                if (floorTimer >= idleFloorRespawnDelay && respawnCoroutine == null)
                {
                    RespawnBall();
                }
            }
        }
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;
        Vector3 physicalVelocity = CalculateReleaseVelocity();
        Vector3 finalVelocity = (physicalVelocity * throwPowerMultiplier) + (Vector3.up * upwardArcAssist);

        // --- FIX #1: ACCIDENTAL DROP FILTER ---
        if (finalVelocity.magnitude < minimumThrowSpeedThreshold)
        {
            Debug.Log($"[VR Basketball] Ignored drop (Speed {finalVelocity.magnitude:F2} m/s < {minimumThrowSpeedThreshold} m/s). No AI triggered.");
            
            // Apply slight drop gravity, do NOT log to AI, do NOT trigger Waspy!
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = finalVelocity * 0.5f;

            // Start short ground timer so it returns if you dropped it
            floorTimer = 0f;
            return;
        }

        // --- REAL THROW DETECTED ---
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = finalVelocity;

        Vector3 spinAxis = Vector3.Cross(Vector3.up, finalVelocity).normalized;
        rb.angularVelocity = spinAxis * (finalVelocity.magnitude * 1.5f);

        Debug.Log($"[VR Basketball] Authentic Shot! Speed: {finalVelocity.magnitude:F2} m/s");

        // Send to AI & Waspy only on real shots
        trajectoryLogger?.StopRecordingAndSave();
        respawnCoroutine = StartCoroutine(RespawnCountdown(throwRespawnDelay));
    }

    private Vector3 CalculateReleaseVelocity()
    {
        if (positionHistory.Count < 2) return Vector3.zero;

        Vector3 oldestPos = positionHistory.Peek();
        float oldestTime = timeHistory.Peek();
        Vector3 newestPos = activeControllerTransform.position;
        float newestTime = Time.time;

        float timeDelta = newestTime - oldestTime;
        if (timeDelta <= 0.001f) return Vector3.zero;

        return (newestPos - oldestPos) / timeDelta;
    }

    private IEnumerator RespawnCountdown(float delay)
    {
        yield return new WaitForSeconds(delay);
        RespawnBall();
    }

    private void RespawnBall()
    {
        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        floorTimer = 0f;

        if (customSpawnPoint != null)
        {
            transform.position = customSpawnPoint.position;
            transform.rotation = customSpawnPoint.rotation;
        }
        else
        {
            transform.position = initialSpawnPosition;
            transform.rotation = initialSpawnRotation;
        }

        Debug.Log("[VR Basketball] Ball auto-returned to player!");
    }
}