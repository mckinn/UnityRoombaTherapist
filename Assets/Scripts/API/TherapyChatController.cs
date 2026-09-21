using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    // ResumeTimer and the typing-triggers-pause behavior (formerly Managed
    // Pause's "entry trigger 3: dialog in progress") were both removed
    // 2026-09-18 (Pause_Redesign_Implementation_Plan.md) - there is no more
    // auto-pause of any kind in this design; pause/resume are governed
    // entirely by the LLM's pause_directive (see SessionManager.UpdateState)
    // and the debug key (PauseController). With chat-box focus preserved
    // after sending a message, typing alone isn't enough of a distraction
    // to need its own pause - confirmed acceptable to revisit later if
    // playtesting says otherwise.

    private const string baseUrl = "http://localhost:8000";

    /// <summary>
    /// Cumulative word count across every message the Therapist (player) has
    /// submitted this session - feeds GameScoreTable's Therapist Need row
    /// (see GameScoreTable_Implementation_Plan.md section 4.6). Added
    /// 2026-09-21; nothing tracked this before. Lives here rather than on
    /// SessionManager since this is the only place a submitted message is
    /// already being handled, and nothing else needs the count.
    /// </summary>
    public int TotalWordsSpoken { get; private set; }

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
        TotalWordsSpoken += CountWords(text);
        inputField.text = "";

        inputField.ActivateInputField();
        EventSystem.current.SetSelectedGameObject(inputField.gameObject);

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

            SessionManager.Instance.UpdateState(response.pad, response.entity_sensitivities, response.pause_directive, response.movement_directive);

            // SpawnRow(messageRowLeftPrefab, response.dialog);
            DisplayLeftMessage(response.dialog);
            // DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);

        }
    }
    /// <summary>
    /// Splits on any whitespace (spaces, tabs, newlines) and drops empty
    /// entries, so extra spacing or a blank/whitespace-only submission never
    /// inflates the count - an empty string correctly counts as 0 words.
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        return text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    ///  one of two methods used to post messaging information in the message log.   This is due for some consolidation.
    /// </summary>
    /// <param name="rowPrefab"></param>
    /// <param name="message"></param>
    private void SpawnRow(GameObject rowPrefab, string message)
    {
        // [TODO] - consolidate SpawnRow and DisplayLeftMessage
        //
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
