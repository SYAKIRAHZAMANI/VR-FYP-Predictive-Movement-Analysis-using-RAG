using UnityEngine;

public class SimpleKeyboardHand : MonoBehaviour
{
    [Tooltip("Drag the pre-made hand's Animator here")]
    public Animator handAnimator;

    public string parameterName = "Grip";

    [Tooltip("1.0 is full fist. 0.5 is holding a ball.")]
    [Range(0f, 1f)]
    public float gripTarget = 0.55f;

    [Tooltip("Which key triggers THIS hand to close?")]
    public KeyCode triggerKey = KeyCode.Mouse1; // Default to Right Mouse Button

    void Update()
    {
        if (handAnimator == null) return;

        // Press the assigned key to close hand, let go to open fully
        if (Input.GetKey(triggerKey))
        {
            handAnimator.SetFloat(parameterName, gripTarget);
        }
        else
        {
            handAnimator.SetFloat(parameterName, 0.0f);
        }
    }
}