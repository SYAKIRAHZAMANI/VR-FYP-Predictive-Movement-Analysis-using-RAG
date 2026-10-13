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

    [Header("Pilot Study Settings")]
    public bool useBaselineStaticMode = false;
    public float ballFlightDuration = 1.8f;
    public float typingSpeed = 0.03f;
    public bool enableVoice = true;
    [SerializeField] private string backendAudioBaseUrl = "http://127.0.0.1:8000/audio/";

    private AudioSource audioSource;
    private AudioClip madeClip = null;
    private bool physicalShotScored = false;
    private float timeSinceLastMake = 0f;
    private bool isTyping = false; 
    private int consecutiveMisses = 0;
    private Coroutine typingCoroutine = null;

    private void Awake() { audioSource = GetComponent<AudioSource>(); }
    private void OnEnable() { BasketDetector.OnPhysicalMadeShot += HandlePhysicalMadeShot; }
    private void OnDisable() { BasketDetector.OnPhysicalMadeShot -= HandlePhysicalMadeShot; }

    private void Start()
    {
        feedbackText.text = "Waspy is ready!";
        if (aiClient != null) aiClient.OnCoachResponse += HandleCoachResponse;
        if (enableVoice) StartCoroutine(PreloadMadeAudio());
    }

    private void Update()
    {
        if (Camera.main != null && canvasTransform != null)
        {
            Vector3 directionToPlayer = canvasTransform.position - Camera.main.transform.position;
            canvasTransform.rotation = Quaternion.LookRotation(directionToPlayer);
        }

        timeSinceLastMake += Time.deltaTime;
        if (timeSinceLastMake > 180f && !isTyping) 
        {
            ShowText("Don't give up! Aim for the center.");
            timeSinceLastMake = 0;
        }
    }

    public void ResetEncouragementTimer() { timeSinceLastMake = 0f; }
    private void HandlePhysicalMadeShot() { physicalShotScored = true; }

    private void HandleCoachResponse(AICoachClient.CoachResponse response)
    {
        // Deliver feedback after the ball completes its flight trajectory
        StartCoroutine(DeliverFeedbackAfterFlight(response));
    }

    private IEnumerator DeliverFeedbackAfterFlight(AICoachClient.CoachResponse response)
    {
        AudioClip preloadedErrorClip = null;
        UnityWebRequest audioReq = null;

        // Preload error audio in parallel during ball flight (eliminates speech delay)
        if (!useBaselineStaticMode && enableVoice && !string.IsNullOrEmpty(response.error_type))
        {
            string url = backendAudioBaseUrl + $"{response.error_type}.mp3";
            audioReq = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            audioReq.SendWebRequest();
        }

        yield return new WaitForSeconds(ballFlightDuration);

        // Complete audio download if still in transit
        if (audioReq != null)
        {
            if (!audioReq.isDone) yield return audioReq;
            if (audioReq.result == UnityWebRequest.Result.Success)
            {
                preloadedErrorClip = DownloadHandlerAudioClip.GetContent(audioReq);
            }
            audioReq.Dispose();
        }

        if (useBaselineStaticMode)
        {
            ShowText(physicalShotScored ? "Nice." : "Missed. Try again.");
        }
        else if (physicalShotScored)
        {
            consecutiveMisses = 0;
            ShowText("SWISH! Beautiful shot!\nKeep that rhythm.");
            if (enableVoice)
            {
                if (madeClip != null)
                {
                    audioSource.Stop();
                    audioSource.clip = madeClip;
                    audioSource.Play();
                }
                else
                {
                    StartCoroutine(PlayVoiceAudio(backendAudioBaseUrl + "Made.mp3"));
                }
            }
        }
        else
        {
            consecutiveMisses++;
            if (consecutiveMisses >= 2)
            {
                ShowText(response.feedback);
                if (ghostHand != null) ghostHand.ShowPerfectThrow();
                if (enableVoice && preloadedErrorClip != null)
                {
                    audioSource.Stop();
                    audioSource.clip = preloadedErrorClip;
                    audioSource.Play();
                }
            }
            else
            {
                ShowText("Missed. Stay focused.");
            }
        }

        physicalShotScored = false; // Reset for next throw
    }

    private void ShowText(string fullText)
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(fullText));
    }

    private IEnumerator TypeText(string fullText)
    {
        isTyping = true;
        feedbackText.text = "";
        foreach (char c in fullText)
        {
            feedbackText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
        isTyping = false;
    }

    private IEnumerator PreloadMadeAudio()
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(backendAudioBaseUrl + "Made.mp3", AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                madeClip = DownloadHandlerAudioClip.GetContent(www);
            }
        }
    }

    private IEnumerator PlayVoiceAudio(string url)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                audioSource.Stop();
                audioSource.clip = DownloadHandlerAudioClip.GetContent(www);
                audioSource.Play();
            }
        }
    }

    public void SetVoice(bool on)
    {
        enableVoice = on;
        if (!on && audioSource != null) audioSource.Stop();
        if (on && madeClip == null) StartCoroutine(PreloadMadeAudio());
    }
}