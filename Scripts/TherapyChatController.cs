using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class TherapyChatController : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Transform contentTransform;
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private GameObject messageRowLeftPrefab;
    [SerializeField] private GameObject messageRowRightPrefab;

    private const string baseUrl = "http://localhost:8000";

    void Start()
    {
        inputField.onSubmit.AddListener(OnInputSubmitted);
    }

    void OnDestroy()
    {
        inputField.onSubmit.RemoveListener(OnInputSubmitted);
    }

    private async void OnInputSubmitted(string text)
    {
        SpawnRow(messageRowRightPrefab, text);
        Debug.Log($"[Chat R] {text}");
        inputField.text = "";

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

            SessionManager.Instance.UpdateState(response.pad, response.entity_sensitivities);

            // SpawnRow(messageRowLeftPrefab, response.dialog);
            DisplayLeftMessage(response.dialog);
            DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);

        }
    }

    private void SpawnRow(GameObject rowPrefab, string message)
    {
        GameObject rowInstance = Instantiate(rowPrefab, contentTransform);

        TextMeshProUGUI bubbleText = rowInstance.GetComponentInChildren<TextMeshProUGUI>();
        bubbleText.text = message;

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
        { "ambivalence", Color.gray },
        { "curiosity", Color.yellow },
        { "joy", Color.green },
        { "disgust", new Color(0.5f, 0f, 0.5f) },
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
                string message = $"[{current.entity_type} -> {current.emotion} ({current.strength:F1})]";
                Color color = emotionColors.TryGetValue(current.emotion, out Color c) ? c : Color.white;
                DisplayLeftMessage(message, color);
            }
        }
    }
}
