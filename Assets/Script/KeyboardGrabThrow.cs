// ============================================================================
// DEMO-ONLY SCRIPT - TEMPORARY INPUT METHOD FOR XR DEVICE SIMULATOR
// Simulates a realistic basketball shot arc and push-force using the
// XR Simulator hand alignment and mouse speed.
//
// UPDATED: Now supports Left Hand (Q), Right Hand (E), or Both Hands (Q+E).
// ============================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class KeyboardGrabThrow : MonoBehaviour
{
    [Header("Grab Settings (Left/Right/Both)")]
    [Tooltip("Key to hold/release with LEFT hand")]
    [SerializeField] private KeyCode leftGrabKey = KeyCode.Q;
    [Tooltip("Key to hold/release with RIGHT hand")]
    [SerializeField] private KeyCode rightGrabKey = KeyCode.E;

    [SerializeField] private Transform leftHandAnchor;  // Assign Left Controller
    [SerializeField] private Transform rightHandAnchor; // Assign Right Controller
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 0.1f, 0.2f);

    [Header("Basketball Physics Tuning")]
    [SerializeField] private float throwPowerMultiplier = 2.2f;
    [SerializeField] private float minThrowSpeed = 7.5f;
    [SerializeField] private float maxThrowSpeed = 13.0f;
    [Range(0.2f, 1.8f)]
    [SerializeField] private float upwardArcBias = 1.0f;
    [SerializeField] private float angularVelocityMultiplier = 1.0f;

    [Header("Data Collection: Manual Power Tiers (hold while releasing)")]
    [SerializeField] private KeyCode weakThrowKey = KeyCode.Alpha1;
    [SerializeField] private float weakThrowSpeed = 5.0f;

    [SerializeField] private KeyCode normalThrowKey = KeyCode.Alpha2;
    [SerializeField] private float normalThrowSpeed = 8.5f;

    [SerializeField] private KeyCode hardThrowKey = KeyCode.Alpha3;
    [SerializeField] private float hardThrowSpeed = 11.0f;

    [SerializeField] private KeyCode jerkyThrowKey = KeyCode.Alpha4;
    [SerializeField] private float jerkyAngularNoise = 15f;

    [Header("Data Collection: Manual Aim Bias (hold while releasing)")]
    [SerializeField] private KeyCode aimLeftKey = KeyCode.A;
    [SerializeField] private KeyCode aimRightKey = KeyCode.D;
    [SerializeField] private float aimBiasStrength = 0.35f;

    [Header("Data Logging")]
    [SerializeField] private TrajectoryLogger trajectoryLogger;

    private Rigidbody rb;
    private bool isLeftHeld = false;
    private bool isRightHeld = false;
    private bool IsHeld => isLeftHeld || isRightHeld;

    // Tracking variables for speed calculations
    private Vector3 previousBallPosition;
    private Quaternion previousRotation;
    private Vector3 smoothedLinearVelocity;
    private Vector3 smoothedAngularVelocity;
    private Vector3 currentAimDirection;
    private Vector3 currentRightDirection;

    // State tracking to prevent velocity spikes when switching hands
    private int previousGrabState = 0; // 0=None, 1=Left, 2=Right, 3=Both

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (trajectoryLogger == null)
            trajectoryLogger = GetComponent<TrajectoryLogger>();
    }

    private void Update()
    {
        bool wasHeld = IsHeld;

        // Check Input
        if (Input.GetKeyDown(leftGrabKey)) isLeftHeld = true;
        if (Input.GetKeyUp(leftGrabKey)) isLeftHeld = false;

        if (Input.GetKeyDown(rightGrabKey)) isRightHeld = true;
        if (Input.GetKeyUp(rightGrabKey)) isRightHeld = false;

        // Handle Grab/Release Transitions
        if (!wasHeld && IsHeld)
        {
            Grab();
        }
        else if (wasHeld && !IsHeld)
        {
            Release();
        }

        if (IsHeld)
        {
            FollowHandAndTrackVelocity();
        }
    }

    private void Grab()
    {
        if (leftHandAnchor == null || rightHandAnchor == null)
        {
            Debug.LogWarning("[KeyboardGrabThrow] Please assign BOTH Left and Right Hand Anchors in the Inspector.");
            return;
        }

        if (trajectoryLogger != null && trajectoryLogger.SessionComplete) return;

        rb.useGravity = false;
        rb.isKinematic = true;

        smoothedLinearVelocity = Vector3.zero;
        smoothedAngularVelocity = Vector3.zero;
        previousGrabState = GetCurrentGrabState();

        trajectoryLogger?.BeginRecording();
        Debug.Log("[KeyboardGrabThrow] Ball grabbed. Release Q and/or E to throw.");
    }

    private void FollowHandAndTrackVelocity()
    {
        Vector3 targetPos = Vector3.zero;
        Quaternion targetRot = Quaternion.identity;

        int currentGrabState = GetCurrentGrabState();

        // Calculate Target Position & Rotation based on active hands
        if (currentGrabState == 1) // Left Only
        {
            targetPos = leftHandAnchor.position + leftHandAnchor.TransformDirection(holdOffset);
            targetRot = leftHandAnchor.rotation;
            currentAimDirection = leftHandAnchor.forward;
            currentRightDirection = leftHandAnchor.right;
        }
        else if (currentGrabState == 2) // Right Only
        {
            targetPos = rightHandAnchor.position + rightHandAnchor.TransformDirection(holdOffset);
            targetRot = rightHandAnchor.rotation;
            currentAimDirection = rightHandAnchor.forward;
            currentRightDirection = rightHandAnchor.right;
        }
        else if (currentGrabState == 3) // Both Hands
        {
            Vector3 lPos = leftHandAnchor.position + leftHandAnchor.TransformDirection(holdOffset);
            Vector3 rPos = rightHandAnchor.position + rightHandAnchor.TransformDirection(holdOffset);

            targetPos = Vector3.Lerp(lPos, rPos, 0.5f);
            targetRot = Quaternion.Slerp(leftHandAnchor.rotation, rightHandAnchor.rotation, 0.5f);

            currentAimDirection = Vector3.Lerp(leftHandAnchor.forward, rightHandAnchor.forward, 0.5f).normalized;
            currentRightDirection = Vector3.Lerp(leftHandAnchor.right, rightHandAnchor.right, 0.5f).normalized;
        }

        // Apply Transform
        transform.position = targetPos;
        transform.rotation = targetRot;

        // If we just changed hand states (e.g., 1 hand to 2 hands), skip velocity math for this frame to prevent spikes
        if (currentGrabState != previousGrabState)
        {
            previousBallPosition = targetPos;
            previousRotation = targetRot;
            previousGrabState = currentGrabState;
            return;
        }

        // Measure real speed of movement
        Vector3 rawLinearVelocity = (targetPos - previousBallPosition) / Time.deltaTime;

        // Track rotation (wrist spin)
        Quaternion deltaRot = targetRot * Quaternion.Inverse(previousRotation);
        deltaRot.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);
        if (angleInDegrees > 180f) angleInDegrees -= 360f;
        Vector3 rawAngularVelocity = rotationAxis * (angleInDegrees * Mathf.Deg2Rad / Time.deltaTime);

        // Smooth to avoid simulator spikes
        smoothedLinearVelocity = Vector3.Lerp(smoothedLinearVelocity, rawLinearVelocity, 10f * Time.deltaTime);
        smoothedAngularVelocity = Vector3.Lerp(smoothedAngularVelocity, rawAngularVelocity, 10f * Time.deltaTime);

        previousBallPosition = targetPos;
        previousRotation = targetRot;
    }

    private void Release()
    {
        rb.isKinematic = false;
        rb.useGravity = true;

        float finalSpeed;
        bool usedManualTier = true;

        if (Input.GetKey(weakThrowKey)) finalSpeed = weakThrowSpeed;
        else if (Input.GetKey(normalThrowKey)) finalSpeed = normalThrowSpeed;
        else if (Input.GetKey(hardThrowKey)) finalSpeed = hardThrowSpeed;
        else if (Input.GetKey(jerkyThrowKey)) finalSpeed = normalThrowSpeed;
        else
        {
            usedManualTier = false;
            float swipeSpeed = smoothedLinearVelocity.magnitude;
            finalSpeed = Mathf.Clamp(swipeSpeed * throwPowerMultiplier, minThrowSpeed, maxThrowSpeed);
        }

        float aimBias = 0f;
        if (Input.GetKey(aimLeftKey)) aimBias -= aimBiasStrength;
        if (Input.GetKey(aimRightKey)) aimBias += aimBiasStrength;

        Vector3 throwDirection = (currentAimDirection + Vector3.up * upwardArcBias + currentRightDirection * aimBias).normalized;

        Vector3 finalAngularVelocity;
        if (Input.GetKey(jerkyThrowKey))
        {
            finalAngularVelocity = new Vector3(
                Random.Range(-jerkyAngularNoise, jerkyAngularNoise),
                Random.Range(-jerkyAngularNoise, jerkyAngularNoise),
                Random.Range(-jerkyAngularNoise, jerkyAngularNoise));
        }
        else
        {
            finalAngularVelocity = smoothedAngularVelocity * angularVelocityMultiplier;
        }

        rb.velocity = throwDirection * finalSpeed;
        rb.angularVelocity = finalAngularVelocity;

        trajectoryLogger?.StopRecordingAndSave();
        previousGrabState = 0; // Reset

        Debug.Log($"[KeyboardGrabThrow] Shot Released! Speed: {finalSpeed:F2} m/s " +
                  $"({(usedManualTier ? "manual tier" : "swipe-derived")}), " +
                  $"AimBias: {aimBias:F2}");
    }

    // Helper to get integer state of hands
    private int GetCurrentGrabState()
    {
        if (isLeftHeld && isRightHeld) return 3;
        if (isRightHeld) return 2;
        if (isLeftHeld) return 1;
        return 0;
    }
}