// ============================================================================
// DEMO-ONLY SCRIPT — TEMPORARY INPUT METHOD FOR XR DEVICE SIMULATOR
// Simulates a realistic basketball shot arc and push-force using the
// XR Simulator hand alignment and mouse speed.
//
// WHEN REAL VR HARDWARE IS AVAILABLE:
//   1. Remove this component from the BasketBall GameObject.
//   2. Re-enable XRGrabInteractable on the ball.
//   3. Add XRGrabTrajectoryDriver.cs instead — same TrajectoryLogger calls,
//      triggered by real selectEntered/selectExited events.
// No changes needed in TrajectoryLogger.cs or BasketDetector.cs.
// ============================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class KeyboardGrabThrow : MonoBehaviour
{
    [Header("Grab Settings")]
    [Tooltip("Key to hold/release the ball (e.g., G)")]
    [SerializeField] private KeyCode grabKey = KeyCode.G;
    [SerializeField] private Transform handAnchor; // Assign the Right Controller here
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 0.1f, 0.2f); // Holds it slightly higher/forward

    [Header("Basketball Physics Tuning")]
    [Tooltip("How much to boost your mouse speed to reach a 10-foot hoop. (7 to 10 m/s is ideal for basketball)")]
    [SerializeField] private float throwPowerMultiplier = 2.2f;

    [Tooltip("Min speed so the ball never drops straight down even on a slow release")]
    [SerializeField] private float minThrowSpeed = 7.5f;

    [Tooltip("Max speed cap to prevent the ball from launching into orbit")]
    [SerializeField] private float maxThrowSpeed = 13.0f;

    [Tooltip("Injects a guaranteed upward angle/loft to simulate the 'Push' and 'Follow-Through' arc. (0.8 - 1.2 is ideal)")]
    [Range(0.2f, 1.8f)]
    [SerializeField] private float upwardArcBias = 1.0f;

    [Tooltip("Multiplier for wrist rotation applied to the ball's spin")]
    [SerializeField] private float angularVelocityMultiplier = 1.0f;

    [Header("Data Logging")]
    [SerializeField] private TrajectoryLogger trajectoryLogger;

    private Rigidbody rb;
    private bool isHeld = false;

    // Tracking variables for speed calculations
    private Vector3 previousBallPosition;
    private Quaternion previousRotation;

    private Vector3 smoothedLinearVelocity;
    private Vector3 smoothedAngularVelocity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (trajectoryLogger == null)
        {
            trajectoryLogger = GetComponent<TrajectoryLogger>();
        }
    }

    private void Update()
    {
        // Detect Grab
        if (Input.GetKeyDown(grabKey) && !isHeld)
        {
            Grab();
        }
        // Detect Release
        else if (Input.GetKeyUp(grabKey) && isHeld)
        {
            Release();
        }

        if (isHeld)
        {
            FollowHandAndTrackVelocity();
        }
    }

    private void Grab()
    {
        if (handAnchor == null)
        {
            Debug.LogWarning("[KeyboardGrabThrow] No Hand Anchor assigned — cannot grab.");
            return;
        }

        if (trajectoryLogger != null && trajectoryLogger.SessionComplete)
        {
            Debug.Log("[KeyboardGrabThrow] Session complete — grab blocked.");
            return;
        }

        isHeld = true;
        rb.useGravity = false;
        rb.isKinematic = true;

        // Reset tracking so the first frame after grab doesn't produce a
        // huge fake velocity spike (comparing to wherever the ball was before).
        previousBallPosition = handAnchor.position + handAnchor.TransformDirection(holdOffset);
        previousRotation = handAnchor.rotation;
        smoothedLinearVelocity = Vector3.zero;
        smoothedAngularVelocity = Vector3.zero;

        trajectoryLogger?.BeginRecording();

        Debug.Log("[KeyboardGrabThrow] Ball grabbed.");
    }

    private void FollowHandAndTrackVelocity()
    {
        Vector3 targetPos = handAnchor.position + handAnchor.TransformDirection(holdOffset);

        // Measure real speed of movement
        Vector3 rawLinearVelocity = (targetPos - previousBallPosition) / Time.deltaTime;

        transform.position = targetPos;
        transform.rotation = handAnchor.rotation;

        // Track rotation (wrist spin)
        Quaternion deltaRot = handAnchor.rotation * Quaternion.Inverse(previousRotation);
        deltaRot.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);
        if (angleInDegrees > 180f) angleInDegrees -= 360f;
        Vector3 rawAngularVelocity = rotationAxis * (angleInDegrees * Mathf.Deg2Rad / Time.deltaTime);

        // Smooth to avoid simulator spikes
        smoothedLinearVelocity = Vector3.Lerp(smoothedLinearVelocity, rawLinearVelocity, 10f * Time.deltaTime);
        smoothedAngularVelocity = Vector3.Lerp(smoothedAngularVelocity, rawAngularVelocity, 10f * Time.deltaTime);

        previousBallPosition = targetPos;
        previousRotation = handAnchor.rotation;
    }

    private void Release()
    {
        isHeld = false;
        rb.isKinematic = false;
        rb.useGravity = true;

        // 1. Calculate the SPEED (power) based on your swipe
        float swipeSpeed = smoothedLinearVelocity.magnitude;
        float finalSpeed = Mathf.Clamp(swipeSpeed * throwPowerMultiplier, minThrowSpeed, maxThrowSpeed);

        // 2. Calculate the DIRECTION: Hand forward direction + Upward Arc Bias (The Basketball Push)
        Vector3 aimDirection = handAnchor.forward;
        Vector3 throwDirection = (aimDirection + Vector3.up * upwardArcBias).normalized;

        // 3. Apply the physical forces
        rb.velocity = throwDirection * finalSpeed;
        rb.angularVelocity = smoothedAngularVelocity * angularVelocityMultiplier;

        trajectoryLogger?.StopRecordingAndSave();

        Debug.Log($"[KeyboardGrabThrow] Shot Released! Speed: {finalSpeed:F2} m/s (Swipe was {swipeSpeed:F2}), Direction: {throwDirection}");
    }
}