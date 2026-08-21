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

    [Tooltip("Receives should_pause=true from any LLM-backed response (arena event, dirt progress, or therapy dialog) - see Planning.md, 'Managed Pause', 'Stop, mediated by the LLM'.")]
    [SerializeField] private PauseController pauseController;

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

    /// <summary>
    /// Shared choke point for every LLM-backed response - SendArenaEvent,
    /// SendDirtProgress, and TherapyChatController's own therapy/message
    /// call all funnel through here. shouldPause defaults to false so this
    /// stays source-compatible with any call site that predates the
    /// should_pause field (there are none currently, but no reason to force
    /// every future caller to pass it explicitly either). should_pause is
    /// one-directional: true calls Pause(); false does nothing - it is not
    /// a resume signal, resume stays governed entirely by the separate,
    /// unified resume condition regardless of what triggered the pause (see
    /// Planning.md, "Managed Pause").
    /// </summary>
    public void UpdateState(PADState pad, List<EntitySensitivity> sensitivities, bool shouldPause = false)
    {
        CurrentPad = pad;
        EntitySensitivities = sensitivities;
        OnStateUpdated?.Invoke();
        Debug.Log($"SessionManager UpdateState - PAD: {JsonConvert.SerializeObject(CurrentPad)}");
        Debug.Log($"SessionManager UpdateState - Evaluatng Pause: {shouldPause}");

        if (shouldPause && pauseController != null)
        {
            Debug.Log($"SessionManager UpdateState - Pausing");
            pauseController.Pause();
        }
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
            UpdateState(response.pad, response.entity_sensitivities, response.should_pause);
            return response;
        }
    }

    /// <summary>
    /// Aggregate dirt-cleanup progress report - a distinct event_type on the
    /// same /arena/event endpoint, not a per-instance EmotionState (see
    /// Planning.md, "Dirt Cleanup and Reporting", and DirtProgressReporter,
    /// which is the only caller). Kept as its own method rather than an
    /// overload of SendArenaEvent so existing collision-event call sites are
    /// untouched by this addition.
    /// </summary>
    public async Awaitable<OrchestratorResponse> SendDirtProgress(float percentComplete, bool isComplete)
    {
        ArenaEventRequest requestBody = new ArenaEventRequest
        {
            session_id = SessionId,
            event_type = "dirt_progress",
            emotion_states = new List<EmotionState>(),
            percent_complete = percentComplete,
            is_complete = isComplete
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
                Debug.LogError($"Dirt progress report failed: {request.error} | {request.downloadHandler.text}");
                return null;
            }

            OrchestratorResponse response = JsonConvert.DeserializeObject<OrchestratorResponse>(request.downloadHandler.text);
            UpdateState(response.pad, response.entity_sensitivities, response.should_pause);
            return response;
        }
    }
}