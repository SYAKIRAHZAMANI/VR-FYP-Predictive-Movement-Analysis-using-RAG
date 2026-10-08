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

    [Header("Pilot Study Settings (A/B Testing)")]
    public bool useBaselineStaticMode = false;

    [Header("Timing")]
    [Tooltip("Seconds the ball takes to fly through the air to the hoop before Waspy speaks")]
    public float ballFlightDuration = 1.8f;

    [Header("Animation & Voice Settings")]
    public float typingSpeed = 0.03f;
    public bool enableVoice = true;
    [SerializeField] private string backendAudioBaseUrl = "http://127.0.0.1:8000/audio/";

    private AudioSource audioSource;
    private bool physicalShotScored = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        BasketDetector.OnPhysicalMadeShot += HandlePhysicalMadeShot;
    }

    private void OnDisable()
    {
        BasketDetector.OnPhysicalMadeShot -= HandlePhysicalMadeShot;
    }

    private void Start()
    {
        feedbackText.text = "Waspy is ready!";
        if (aiClient != null) aiClient.OnCoachResponse += HandleCoachResponse;
    }

    private void Update()
    {
        if (Camera.main != null && canvasTransform != null)
        {
            Vector3 directionToPlayer = canvasTransform.position - Camera.main.transform.position;
            canvasTransform.rotation = Quaternion.LookRotation(directionToPlayer);
        }
    }

    private void HandlePhysicalMadeShot()
    {
        physicalShotScored = true;
    }

    private void HandleCoachResponse(AICoachClient.CoachResponse response)
    {
        // Stop any active feedback and reset the made-shot tracker for this new throw
        StopAllCoroutines();
        physicalShotScored = false;

        // WAIT for the ball to actually reach the hoop before judging the player!
        StartCoroutine(DeliverFeedbackAfterFlight(response));
    }

    private IEnumerator DeliverFeedbackAfterFlight(AICoachClient.CoachResponse response)
    {
        // 1. Wait while the ball flies through the air
        yield return new WaitForSeconds(ballFlightDuration);

        // --- GROUP A: DUMB STATIC MODE ---
        if (useBaselineStaticMode)
        {
            if (physicalShotScored)
                StartCoroutine(TypeText("Nice."));
            else
                StartCoroutine(TypeText("Missed. Try again."));
            yield break;
        }

        // --- GROUP B: FULL INTELLIGENT AI MODE ---
        string displayMessage = "";
        string audioFileName = "";

        // If the ball PHYSICALLY went in:
        if (physicalShotScored)
        {
            displayMessage = "SWISH! Beautiful shot!\nKeep that rhythm.";
            audioFileName = "Made.mp3";
        }
        else
        {
            // If it physically missed, deliver the targeted RAG coaching tip:
            displayMessage = response.feedback;
            audioFileName = $"{response.error_type}.mp3";

            // Trigger the Ghost Hand!
            if (ghostHand != null) ghostHand.ShowPerfectThrow();
        }

        StartCoroutine(TypeText(displayMessage));

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
        }
    }

    private void OnDestroy()
    {
        if (aiClient != null) aiClient.OnCoachResponse -= HandleCoachResponse;
    }
}