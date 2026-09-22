using UnityEngine;
using UnityEngine.XR;

public class SimpleKeyboardHand : MonoBehaviour
{
    public Animator handAnimator;
    public string parameterName = "Grip";
    
    [Header("Controller Selection")]
    [Tooltip("Check this if this script is on the Left Hand. Leave unchecked for Right Hand.")]
    public bool isLeftHand = false;

    [Range(0f, 1f)]
    public float gripTarget = 0.55f; 

    private InputDevice targetDevice;

    void Start()
    {
        InitializeDevice();
    }

    void InitializeDevice()
    {
        InputDeviceCharacteristics characteristics = isLeftHand ? 
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller : 
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller;

        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(characteristics, devices);

        if (devices.Count > 0)
        {
            targetDevice = devices[0];
        }
    }

    void Update()
    {
        if (handAnimator == null) return;

        if (!targetDevice.isValid)
        {
            InitializeDevice();
            return;
        }

        // Read physical Quest Pro Grip trigger (0.0 = open, 1.0 = fully squeezed)
        if (targetDevice.TryGetFeatureValue(CommonUsages.grip, out float gripValue))
        {
            // Scale it so it holds the ball naturally at your gripTarget
            float finalBlend = gripValue * gripTarget;
            handAnimator.SetFloat(parameterName, finalBlend);
        }
    }
}