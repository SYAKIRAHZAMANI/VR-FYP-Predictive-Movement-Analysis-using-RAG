using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;                 // Required for XRI events
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // Required for XRGrabInteractable in XRI 3.x+


/// <summary>
/// PRODUCTION driver for real VR hardware. Hooks XRGrabInteractable's
/// selectEntered/selectExited events to the same TrajectoryLogger methods
/// used by the keyboard demo driver — ensuring identical CSV output format
/// regardless of input method.
///
/// To switch from the keyboard demo to real controllers:
///   1. Remove KeyboardGrabThrow.cs from the ball.
///   2. Make sure XRGrabInteractable is enabled on the ball.
///   3. Add this script (XRGrabTrajectoryDriver) to the ball.
/// That's it — TrajectoryLogger and BasketDetector need no changes.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class XRGrabTrajectoryDriver : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private TrajectoryLogger trajectoryLogger;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        trajectoryLogger = GetComponent<TrajectoryLogger>();

        if (trajectoryLogger == null)
        {
            Debug.LogWarning("[XRGrabTrajectoryDriver] No TrajectoryLogger found on this object.");
        }
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
        trajectoryLogger?.BeginRecording();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        trajectoryLogger?.StopRecordingAndSave();
    }
}