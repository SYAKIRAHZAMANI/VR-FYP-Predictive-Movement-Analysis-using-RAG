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
    private bool physicalShotScored = false;
    private float timeSinceLastMake = 0f;
    private bool isTyping = false; // NEW: Prevent text overlap

    private void Awake() { audioSource = GetComponent<AudioSource>(); }
    private void OnEnable() { BasketDetector.OnPhysicalMadeShot += HandlePhysicalMadeShot; }
    private void OnDisable() { BasketDetector.OnPhysicalMadeShot -= HandlePhysicalMadeShot; }

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

        timeSinceLastMake += Time.deltaTime;
        if (timeSinceLastMake > 180f && !isTyping) 
        {
            StartCoroutine(TypeText("Don't give up! Aim for the center."));
            timeSinceLastMake = 0;
        }
    }

    public void ResetEncouragementTimer() { timeSinceLastMake = 0f; }
    private void HandlePhysicalMadeShot() { physicalShotScored = true; }

    private void HandleCoachResponse(AICoachClient.CoachResponse response)
    {
        StopAllCoroutines();
        StartCoroutine(DeliverFeedbackAfterFlight(response));
    }

    private IEnumerator DeliverFeedbackAfterFlight(AICoachClient.CoachResponse response)
    {
        yield return new WaitForSeconds(ballFlightDuration);

        if (useBaselineStaticMode)
        {
            StartCoroutine(TypeText(physicalShotScored ? "Nice." : "Missed. Try again."));
        }
        else if (physicalShotScored)
        {
            StartCoroutine(TypeText("SWISH! Beautiful shot!\nKeep that rhythm."));
            if (enableVoice) StartCoroutine(PlayVoiceAudio(backendAudioBaseUrl + "Made.mp3"));
        }
        else
        {
            StartCoroutine(TypeText(response.feedback));
            if (ghostHand != null) ghostHand.ShowPerfectThrow();
            if (enableVoice) StartCoroutine(PlayVoiceAudio(backendAudioBaseUrl + $"{response.error_type}.mp3"));
        }
        physicalShotScored = false; // Reset for next throw
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
}