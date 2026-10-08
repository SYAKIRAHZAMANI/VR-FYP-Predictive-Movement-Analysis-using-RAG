using UnityEngine;
using TMPro; // Required for TextMeshPro UI

public class BasketDetector : MonoBehaviour
{
    [Header("Detection Zones")]
    [Tooltip("The trigger placed slightly ABOVE the rim")]
    [SerializeField] private Collider topTrigger;

    [Tooltip("The trigger placed INSIDE the net")]
    [SerializeField] private Collider bottomTrigger;

    [Header("Detection Settings")]
    [SerializeField] private string ballTag = "Ball";
    [SerializeField] private float maxTimeBetweenTriggers = 1.5f;

    [Header("Scoreboard UI")]
    [Tooltip("Drag your TextMeshPro component here to show the score")]
    [SerializeField] private TextMeshProUGUI scoreText;
   
    public static System.Action OnPhysicalMadeShot;

    private bool passedTop = false;
    private float topTriggerTime = 0f;

    public int TotalMakes { get; private set; }

    private void Start()
    {
        // Set initial score display when the game starts
        UpdateScoreboard();

    }

    private void Update()
    {
        // Reset the top-pass state if the ball took too long to reach the bottom trigger
        if (passedTop && (Time.time - topTriggerTime > maxTimeBetweenTriggers))
        {
            passedTop = false;
            Debug.Log("[BasketDetector] Shot sequence timed out.");
        }
    }

    public void ReportTriggerEnter(Collider trigger, Collider otherBall)
    {
        if (!otherBall.CompareTag(ballTag)) return;

        if (trigger == topTrigger)
        {
            passedTop = true;
            topTriggerTime = Time.time;
        }
        else if (trigger == bottomTrigger)
        {
            if (passedTop)
            {
                passedTop = false;
                TotalMakes++;

                Debug.Log($"[BasketDetector] SWISH! MADE SHOT #{TotalMakes}");

                // Update the floating screen!
                UpdateScoreboard();
                OnPhysicalMadeShot?.Invoke();

                // Report back to the TrajectoryLogger so the CSV is tagged "Made"
                var logger = otherBall.GetComponent<TrajectoryLogger>();
                if (logger != null)
                {
                    logger.RecordOutcome("Made");
                }
            }
        }
    }

    private void UpdateScoreboard()
    {
        if (scoreText != null)
        {
            scoreText.text = $"SCORE\n{TotalMakes}";
        }
    }
}