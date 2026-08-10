using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SessionManager : MonoBehaviour
{
    private static SessionManager instance;
    public static SessionManager Instance => instance;

    public string SessionId { get; private set; }
    public string RoombaName { get; private set; }
    public PADState CurrentPad { get; private set; }
    public List<EntitySensitivity> EntitySensitivities { get; private set; }
    public event Action OnStateUpdated;

    private const string baseUrl = "http://localhost:8000";

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    async void Start()
    {
        await StartSession("couchaphobe_01");
    }

    public void UpdateState(PADState pad, List<EntitySensitivity> sensitivities)
    {
        CurrentPad = pad;
        EntitySensitivities = sensitivities;
        OnStateUpdated?.Invoke();
        Debug.Log($"SessionManager UpdateState - PAD: {JsonConvert.SerializeObject(CurrentPad)}");
    }

    /// <summary>
    /// Looks up the current sensitivity for a given entity_type, or null if
    /// nothing is known yet (including the case where EntitySensitivities
    /// itself hasn't been populated yet). This is the single shared lookup
    /// against the single source of truth - both CollisionController
    /// (reporting events to the Orchestrator) and JourneyCalculator (local
    /// ANS movement) call this rather than each maintaining their own copy
    /// of the same search.
    /// </summary>
    public EntitySensitivity GetSensitivity(string entityType)
    {
        if (EntitySensitivities == null)
        {
            return null;
        }

        foreach (EntitySensitivity sensitivity in EntitySensitivities)
        {
            if (sensitivity.entity_type == entityType)
            {
                return sensitivity;
            }
        }

        return null;
    }

    public async Awaitable StartSession(string roombaId)
    {
        SessionStartRequest requestBody = new SessionStartRequest { roomba_id = roombaId };
        string jsonBody = JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest request = new UnityWebRequest($"{baseUrl}/session/start", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Session start failed: {request.error} | {request.downloadHandler.text}");
                return;
            }

            SessionStartResponse response = JsonConvert.DeserializeObject<SessionStartResponse>(request.downloadHandler.text);
            SessionId = response.session_id;
            RoombaName = response.roomba_name;
            CurrentPad = response.initial_pad;
            EntitySensitivities = response.entity_sensitivities;

            Debug.Log($"Session started: {SessionId}, Roomba: {RoombaName}, PAD: {JsonConvert.SerializeObject(CurrentPad)}");
        }
    }

    public async Awaitable<OrchestratorResponse> SendArenaEvent(string eventType, List<EmotionState> emotionStates)
    {
        ArenaEventRequest requestBody = new ArenaEventRequest
        {
            session_id = SessionId,
            event_type = eventType,
            emotion_states = emotionStates
        };
        string jsonBody = JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest request = new UnityWebRequest($"{baseUrl}/arena/event", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Arena event failed: {request.error} | {request.downloadHandler.text}");
                return null;
            }

            OrchestratorResponse response = JsonConvert.DeserializeObject<OrchestratorResponse>(request.downloadHandler.text);
            UpdateState(response.pad, response.entity_sensitivities);
            return response;
        }
    }
}