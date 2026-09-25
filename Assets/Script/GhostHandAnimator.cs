using System.Collections;
using UnityEngine;

public class GhostHandAnimator : MonoBehaviour
{
    [Header("References")]
    public Transform playerCamera;

    [Tooltip("Drag your Hoop or BasketDetector here so the arc knows where to go!")]
    public Transform hoopTarget;

    public GameObject leftGhostMesh;
    public GameObject rightGhostMesh;

    [Header("Animation Settings")]
    public float throwMotionDuration = 0.5f; // Fast, realistic arm extension
    public float holdDuration = 6.0f;        // How long to keep the hologram on screen

    // Hand movement (short, realistic arm extension)
    private Vector3 startOffset = new Vector3(0.0f, -0.4f, 0.3f); // Chest level
    private Vector3 endOffset = new Vector3(0.0f, 0.2f, 0.7f);   // Arm extended

    private LineRenderer trajectoryLine;


      // Add references to the Ghost Hand Animators
    private Animator leftAnimator;
    private Animator rightAnimator;

    private void Awake()
    {
        trajectoryLine = gameObject.AddComponent<LineRenderer>();
        trajectoryLine.startWidth = 0.04f;
        trajectoryLine.endWidth = 0.04f;
        trajectoryLine.enabled = false;

        // Get the Animators from the ghost models
        if (leftGhostMesh != null) leftAnimator = leftGhostMesh.GetComponent<Animator>();
        if (rightGhostMesh != null) rightAnimator = rightGhostMesh.GetComponent<Animator>();

        HideHands();
    }
    
    public void ShowPerfectThrow()
    {
        StopAllCoroutines();
        StartCoroutine(AnimateThrowAndArc());
    }

    private IEnumerator AnimateThrowAndArc()
    {
        // 1. Setup Visibility
        int handState = KeyboardGrabThrow.LastThrowState;
        leftGhostMesh.SetActive(handState == 1 || handState == 3);
        rightGhostMesh.SetActive(handState == 2 || handState == 3);
        trajectoryLine.enabled = false;

        // Automatically borrow the cool glowing material from the hand to use on the line!
        if (rightGhostMesh.activeSelf)
            trajectoryLine.material = rightGhostMesh.GetComponentInChildren<SkinnedMeshRenderer>().material;
        else
            trajectoryLine.material = leftGhostMesh.GetComponentInChildren<SkinnedMeshRenderer>().material;

        // 2. Hand Animation Math (Relative to where player is looking)
        Vector3 startPos = playerCamera.position + playerCamera.TransformDirection(startOffset);
        Vector3 endPos = playerCamera.position + playerCamera.TransformDirection(endOffset);

        Quaternion startRot = playerCamera.rotation * Quaternion.Euler(-45, 0, 0); // Cocked back
        Quaternion endRot = playerCamera.rotation * Quaternion.Euler(45, 0, 0);    // Flicked forward (Gooseneck)

        float elapsed = 0f;

        // 3. Animate the hand doing the short, realistic throw
        while (elapsed < throwMotionDuration)
        {

         float t = elapsed / throwMotionDuration;
         float easeT = Mathf.SmoothStep(0, 1, t);

        transform.position = Vector3.Lerp(startPos, endPos, easeT);
        transform.rotation = Quaternion.Slerp(startRot, endRot, easeT);

        // --- SKELETAL ARTICULATION ---
        // Fingers start curled on the ball (0.6), and dynamically open at release (0.0)!
        float dynamicGrip = Mathf.Lerp(0.6f, 0.0f, t);
        if (leftAnimator != null) leftAnimator.SetFloat("Grip", dynamicGrip);
        if (rightAnimator != null) rightAnimator.SetFloat("Grip", dynamicGrip);

        elapsed += Time.deltaTime;
        yield return null;

        }



        // Snap to final follow-through position
        transform.position = endPos;
        transform.rotation = endRot;

        // 4. At the exact moment of release, draw the Ideal Ball Trajectory Arc!
        DrawIdealArc(endPos);

        // 5. Hold the pose and the arc so the player can study it
        yield return new WaitForSeconds(holdDuration);

        // 6. Vanish
        HideHands();
        trajectoryLine.enabled = false;
    }

    private void DrawIdealArc(Vector3 releasePoint)
    {
        if (hoopTarget == null) return;

        int resolution = 30;
        trajectoryLine.positionCount = resolution;

        Vector3 targetPoint = hoopTarget.position;

        // Create a mathematical high arc peak exactly halfway to the hoop
        Vector3 midPoint = Vector3.Lerp(releasePoint, targetPoint, 0.5f);
        midPoint.y += 2.0f; // Gives it a nice high basketball arc

        // Draw a smooth Quadratic Bezier Curve
        for (int i = 0; i < resolution; i++)
        {
            float t = i / (float)(resolution - 1);
            Vector3 m1 = Vector3.Lerp(releasePoint, midPoint, t);
            Vector3 m2 = Vector3.Lerp(midPoint, targetPoint, t);
            Vector3 p = Vector3.Lerp(m1, m2, t);
            trajectoryLine.SetPosition(i, p);
        }

        trajectoryLine.enabled = true;
    }

    private void HideHands()
    {
        leftGhostMesh.SetActive(false);
        rightGhostMesh.SetActive(false);
    }
}