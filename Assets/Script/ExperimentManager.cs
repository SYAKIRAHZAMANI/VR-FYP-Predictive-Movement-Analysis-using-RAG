using UnityEngine;
using UnityEngine.UI;

public class ExperimentManager : MonoBehaviour
{
    public CompanionController waspy;
    public Image muteButtonIcon;
    public Sprite voiceOnSprite;
    public Sprite voiceOffSprite;

    void Start()
    {
        UpdateIcon();
    }

    public void ToggleVoice()
    {
        waspy.SetVoice(!waspy.enableVoice);
        UpdateIcon();
    }

    private void UpdateIcon()
    {
        muteButtonIcon.sprite = waspy.enableVoice ? voiceOnSprite : voiceOffSprite;
    }
}