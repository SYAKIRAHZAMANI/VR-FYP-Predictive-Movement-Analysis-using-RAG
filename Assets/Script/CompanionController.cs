using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class CompanionController : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI feedbackText;
    public Transform canvasTransform;

    [Header("References")]
    public AICoachClient aiClient;

    public GhostHandAnimator ghostHand;

    [Header("Typewriter Effect")]
    public float typingSpeed = 0.03f;

    [Header("Voice Settings")]
    public bool enableVoice = true;
    [SerializeField] private string backendAudioBaseUrl = "http://127.0.0.1:8000/audio/";

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        feedbackText.text = "Waspy is ready to coach!";

        if (aiClient != null)
        {
            aiClient.OnCoachResponse += HandleCoachResponse;
        }
        else
        {
            Debug.LogError("[CompanionController] Please assign the BasketBall in the Inspector!");
        }
    }

    private void Update()
    {
        if (Camera.main != null && canvasTransform != null)
        {
            Vector3 directionToPlayer = canvasTransform.position - Camera.main.transform.position;
            canvasTransform.rotation = Quaternion.LookRotation(directionToPlayer);
        }
    }

    private void HandleCoachResponse(AICoachClient.CoachResponse response)
    {
        StopAllCoroutines();

        string displayMessage = "";
        string audioFileName = "";

        if (response.outcome == "Made")
        {
            displayMessage = "Nice shot!\n\n" + response.feedback;
            audioFileName = "Made.mp3";
        }
       else
        {
            // Just show the natural language coaching tip! No raw error labels.
            displayMessage = response.feedback; 
            audioFileName = $"{response.error_type}.mp3"; // Keep this so the audio still finds the right file
        }

        if (ghostHand != null) ghostHand.ShowPerfectThrow();

        // 1. Visual Typewriter
        StartCoroutine(TypeText(displayMessage));

        // 2. Play realistic Neural Voice from Waspy
        if (enableVoice)
        {
            StartCoroutine(PlayVoiceAudio(backendAudioBaseUrl + audioFileName));
        }
    }

    private IEnumerator TypeText(string fullText)
    {
        feedbackText.text = "";
        foreach (char c in fullText)
        {
            feedbackText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
    }

    private IEnumerator PlayVoiceAudio(string url)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                audioSource.Stop();
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                Debug.LogWarning("[Waspy Voice] Failed to fetch audio: " + www.error);
            }
        }
    }

    private void OnDestroy()
    {
        if (aiClient != null)
        {
            aiClient.OnCoachResponse -= HandleCoachResponse;
        }
    }
}