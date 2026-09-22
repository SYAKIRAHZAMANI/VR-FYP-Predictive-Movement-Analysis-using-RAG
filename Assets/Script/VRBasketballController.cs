using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class VRBasketballController : MonoBehaviour
{
    [Header("True Physical Throw Tuning")]
    [Tooltip("Boosts your physical arm speed so you can easily reach a 10ft hoop. (1.3 to 1.8 is ideal)")]
    [Range(1.0f, 3.0f)]
    public float throwPowerMultiplier = 1.5f;

    [Tooltip("Slight upward assist to simulate natural jump shot arc")]
    [Range(0.0f, 2.0f)]
    public float upwardArcAssist = 0.5f;

    [Header("Respawn Settings")]
    public float respawnDelay = 5.0f;
    public Transform customSpawnPoint;

    [Header("References")]
    public TrajectoryLogger trajectoryLogger;

    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private Transform activeControllerTransform;
    private bool isHeld = false;

    // Rolling velocity buffer to catch your PEAK arm speed right before release
    private Queue<Vector3> positionHistory = new Queue<Vector3>();
    private Queue<float> timeHistory = new Queue<float>();
    private const int HistoryFrames = 8; 

    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;
    private Coroutine respawnCoroutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        if (trajectoryLogger == null)
            trajectoryLogger = GetComponent<TrajectoryLogger>();

        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;

        // Disable XRI's sluggish default throw so OUR physical script takes full control!
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
        // Grab the transform of whatever physical controller grabbed the ball (Left or Right!)
        activeControllerTransform = args.interactorObject.transform;

        positionHistory.Clear();
        timeHistory.Clear();

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        trajectoryLogger?.BeginRecording();
        Debug.Log("[VR Basketball] Grabbed with physical controller!");
    }

    private void Update()
    {
        // Continuously record your real physical hand movement
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
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;

        // 1. Calculate the TRUE physical velocity of your real arm swing
        Vector3 physicalVelocity = CalculateReleaseVelocity();

        // 2. Apply your athletic basketball power multiplier
        Vector3 finalVelocity = physicalVelocity * throwPowerMultiplier;
        finalVelocity.y += upwardArcAssist; 

        // 3. Launch the ball with real physics!
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = finalVelocity;

        // 4. Inject natural basketball backspin based on your shot direction!
        Vector3 spinAxis = Vector3.Cross(Vector3.up, finalVelocity).normalized;
        rb.angularVelocity = spinAxis * (finalVelocity.magnitude * 1.5f);

        Debug.Log($"[VR Basketball] Physical Shot Launched! Speed: {finalVelocity.magnitude:F2} m/s");

        // 5. Log data and start respawn timer
        trajectoryLogger?.StopRecordingAndSave();
        respawnCoroutine = StartCoroutine(RespawnAfterDelay());
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

        // Change in distance over time = real velocity!
        return (newestPos - oldestPos) / timeDelta;
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

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

        Debug.Log("[VR Basketball] Ball respawned!");
    }
}