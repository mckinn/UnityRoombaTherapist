using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;

public class TherapyChatController : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Transform contentTransform;
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private GameObject messageRowLeftPrefab;
    [SerializeField] private GameObject messageRowRightPrefab;

    [Tooltip("Resets on typing, message send, and response received - see ResumeTimer's own header comment for why this is the sole intended caller of ResetTimer().")]
    [SerializeField] private ResumeTimer resumeTimer;

    [Tooltip("Entry trigger 3 of the Managed Pause design (see Planning.md) - a dialog exchange in progress is its own reason to pause, independent of settle-detection, arena-entry, or should_pause. Typing calls Pause() directly here.")]
    [SerializeField] private PauseController pauseController;

    private const string baseUrl = "http://localhost:8000";

    void Start()
    {
        inputField.onSubmit.AddListener(OnInputSubmitted);
        inputField.onValueChanged.AddListener(OnTyping);
    }

    void OnDestroy()
    {
        inputField.onSubmit.RemoveListener(OnInputSubmitted);
        inputField.onValueChanged.RemoveListener(OnTyping);
    }

    private void OnTyping(string text)
    {
        // Entry trigger 3: dialog in progress is its own reason to be
        // paused, not just something that extends a pause already caused
        // by something else. Pause() is idempotent - safe to call on every
        // keystroke rather than needing to detect "typing just started".
        pauseController?.Pause();
        resumeTimer?.ResetTimer();
    }

    private async void OnInputSubmitted(string text)
    {
        SpawnRow(messageRowRightPrefab, text);
        Debug.Log($"[Chat R] {text}");
        inputField.text = "";
        resumeTimer?.ResetTimer();

        await SendTherapyMessage(text);
    }

    private async Awaitable SendTherapyMessage(string message)
    {
        Dictionary<string, EntitySensitivity> beforeSensitivities = SnapshotSensitivities();
        TherapyMessageRequest requestBody = new TherapyMessageRequest
        {
            session_id = SessionManager.Instance.SessionId,
            message = message
        };
        string jsonBody = JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest request = new UnityWebRequest($"{baseUrl}/therapy/message", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Therapy message failed: {request.error} | {request.downloadHandler.text}");
                return;
            }

            OrchestratorResponse response = JsonConvert.DeserializeObject<OrchestratorResponse>(request.downloadHandler.text);
            resumeTimer?.ResetTimer();

            SessionManager.Instance.UpdateState(response.pad, response.entity_sensitivities, response.should_pause);

            // SpawnRow(messageRowLeftPrefab, response.dialog);
            DisplayLeftMessage(response.dialog);
            // DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);

        }
    }
    /// <summary>
    ///  one of two methods used to post messaging information in the message log.   This is due for some consolidation.
    /// </summary>
    /// <param name="rowPrefab"></param>
    /// <param name="message"></param>
    private void SpawnRow(GameObject rowPrefab, string message)
    {
        GameObject rowInstance = Instantiate(rowPrefab, contentTransform);

        TextMeshProUGUI bubbleText = rowInstance.GetComponentInChildren<TextMeshProUGUI>();
        bubbleText.text = message;
        bubbleText.fontSize = 20;

        RectTransform bubbleRect = bubbleText.transform.parent.GetComponent<RectTransform>();
        RectTransform rowRect = rowInstance.GetComponent<RectTransform>();
        RectTransform contentRect = contentTransform.GetComponent<RectTransform>();

        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        chatScrollRect.verticalNormalizedPosition = 0f;
    }
    public void DisplayLeftMessage(string message, Color? textColor = null)
    {
        GameObject rowInstance = Instantiate(messageRowLeftPrefab, contentTransform);
        TextMeshProUGUI bubbleText = rowInstance.GetComponentInChildren<TextMeshProUGUI>();
        bubbleText.text = message;
        bubbleText.fontSize = 20;
        Debug.Log($"[Chat L] {message}");

        if (textColor.HasValue)
        {
            bubbleText.color = textColor.Value;
        }

        RectTransform bubbleRect = bubbleText.transform.parent.GetComponent<RectTransform>();
        RectTransform rowRect = rowInstance.GetComponent<RectTransform>();
        RectTransform contentRect = contentTransform.GetComponent<RectTransform>();

        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private readonly Dictionary<string, Color> emotionColors = new Dictionary<string, Color>
    {
        { "fear", Color.red },
        { "ambivalence", Color.white },
        { "curiosity", Color.yellow },
        { "joy", Color.green },
        { "disgust", Color.purple},
        { "none", Color.white }
    };

    public Dictionary<string, EntitySensitivity> SnapshotSensitivities()
    {
        Dictionary<string, EntitySensitivity> snapshot = new Dictionary<string, EntitySensitivity>();
        foreach (EntitySensitivity s in SessionManager.Instance.EntitySensitivities)
        {
            snapshot[s.entity_type] = s;
        }
        return snapshot;
    }

    public void DisplaySensitivityChanges(Dictionary<string, EntitySensitivity> before, List<EntitySensitivity> after)
    {
        foreach (EntitySensitivity current in after)
        {
            bool changed = !before.TryGetValue(current.entity_type, out EntitySensitivity previous)
                || previous.emotion != current.emotion
                || previous.strength != current.strength;

            if (changed)
            {
                string message = $"[{current.entity_type} -> {current.emotion} ({current.strength:F2})]";
                Debug.Log($"DisplaySensitivityChanges {message}");
                Color color = emotionColors.TryGetValue(current.emotion, out Color c) ? c : Color.white;
                DisplayLeftMessage(message, color);
            }
        }
    }
}
