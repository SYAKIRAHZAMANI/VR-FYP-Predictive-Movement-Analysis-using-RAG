using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Sends each completed throw's kinematic samples to the local FastAPI/RAG
/// backend for real-time LSTM prediction + coaching feedback, independent
/// of BasketDetector's physics-based outcome (see TrajectoryLogger's
/// OnThrowReadyForPrediction event for why: this matches the report's
/// System Architecture, where Unity streams telemetry out and the backend
/// predicts outcome from kinematics alone).
///
/// Attach this to the same GameObject as TrajectoryLogger (the ball).
/// </summary>
[RequireComponent(typeof(TrajectoryLogger))]
public class AICoachClient : MonoBehaviour
{
    [Header("Backend Connection")]
    [Tooltip("Base URL of the local FastAPI server. Change if not running on the same machine.")]
    [SerializeField] private string backendUrl = "http://127.0.0.1:8000/predict_and_coach";

    [Tooltip("Max time to wait for a backend response before giving up.")]
    [SerializeField] private float requestTimeoutSeconds = 15f;

    private TrajectoryLogger trajectoryLogger;

    // --- JSON-serializable request/response shapes (must match main_api.py) ---

    [Serializable]
    private class SamplesPayload
    {
        public List<TrajectoryLogger.PredictionSample> samples;
    }

    [Serializable]
    public class CoachResponse
    {
        public string outcome;
        public float probability_made;
        public string error_type;
        public string feedback;
    }

    /// <summary>
    /// Fired when a response is successfully received, so other scripts
    /// (Ghost Hand, spatial audio, UI) can react without needing to know
    /// anything about the HTTP/JSON details.
    /// </summary>
    public event Action<CoachResponse> OnCoachResponse;

    private void Awake()
    {
        trajectoryLogger = GetComponent<TrajectoryLogger>();
    }

    private void OnEnable()
    {
        trajectoryLogger.OnThrowReadyForPrediction += HandleThrowReady;
    }

    private void OnDisable()
    {
        trajectoryLogger.OnThrowReadyForPrediction -= HandleThrowReady;
    }

    private void HandleThrowReady(List<TrajectoryLogger.PredictionSample> samples)
    {
        StartCoroutine(SendToBackend(samples));
    }

    private IEnumerator SendToBackend(List<TrajectoryLogger.PredictionSample> samples)
    {
        var payload = new SamplesPayload { samples = samples };
        string json = JsonUtility.ToJson(payload);

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(backendUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.CeilToInt(requestTimeoutSeconds);

            Debug.Log($"[AICoachClient] Sending {samples.Count} samples to AI backend...");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AICoachClient] Request failed: {request.error}. " +
                                  "Is the backend running (uvicorn main_api:app --reload)?");
                yield break;
            }

            string responseText = request.downloadHandler.text;
            CoachResponse response;

            try
            {
                response = JsonUtility.FromJson<CoachResponse>(responseText);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AICoachClient] Failed to parse response: {e.Message}\nRaw: {responseText}");
                yield break;
            }

            Debug.Log($"[AICoachClient] Prediction: {response.outcome} " +
                      $"(confidence: {response.probability_made:F2}) | " +
                      $"Error type: {response.error_type ?? "none"} | " +
                      $"Feedback: {response.feedback}");

            OnCoachResponse?.Invoke(response);
        }
    }
}