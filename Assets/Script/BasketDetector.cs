using UnityEngine;
using TMPro;

public class BasketDetector : MonoBehaviour
{
    [Header("Detection Zones")]
    [SerializeField] private Collider topTrigger;
    [SerializeField] private Collider bottomTrigger;

    [Header("Detection Settings")]
    [SerializeField] private string ballTag = "Ball";
    [SerializeField] private float maxTimeBetweenTriggers = 1.5f;

    [Header("Scoreboard UI")]
    [SerializeField] private TextMeshProUGUI scoreText;
   
    public static System.Action OnPhysicalMadeShot;

    private bool passedTop = false;
    private float topTriggerTime = 0f;
    private float lastScoreTime = 0f; // NEW: Prevent double scoring

    public int TotalMakes { get; private set; }

    private void Start() { UpdateScoreboard(); }

    private void Update()
    {
        if (passedTop && (Time.time - topTriggerTime > maxTimeBetweenTriggers))
        {
            passedTop = false;
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
        else if (trigger == bottomTrigger && passedTop)
        {
            // NEW: Added 1.5s cooldown
            if (Time.time - lastScoreTime > 1.5f)
            {
                passedTop = false;
                lastScoreTime = Time.time;
                TotalMakes++;
                UpdateScoreboard();
                
                // Trigger Waspy's success logic
                OnPhysicalMadeShot?.Invoke();
                
                // Reset Waspy's boredom timer
                var coach = FindObjectOfType<CompanionController>();
                if (coach) coach.ResetEncouragementTimer();

                var logger = otherBall.GetComponent<TrajectoryLogger>();
                if (logger != null) logger.RecordOutcome("Made");
            }
        }
    }

    private void UpdateScoreboard()
    {
        if (scoreText != null) scoreText.text = $"SCORE\n{TotalMakes}";
    }
}