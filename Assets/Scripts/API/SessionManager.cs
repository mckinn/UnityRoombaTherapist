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

    [Tooltip("Receives pause_directive from any LLM-backed response (arena event, dirt progress, or therapy dialog) - see Pause_Redesign_Implementation_Plan.md.")]
    [SerializeField] private PauseController pauseController;

    [Tooltip("Receives movement_directive from any LLM-backed response - see Movement_Concurrency_Plan.md section 4, item 1. Must be wired in the Inspector; a [SerializeField] default here would be silently shadowed by whatever the scene/prefab has serialized for this slot if it's left unassigned, same gotcha already hit elsewhere in this project.")]
    [SerializeField] private JourneyCalculator journeyCalculator;

    [Header("Landmark Seeding (2026-09-14)")]
    [Tooltip("An always-known reference point the Roomba can move closer to or further from from the very first turn, even before it has organically collided with anything else - added after playtests showed a single-entity trap with an otherwise-empty roster gives the LLM no alternative direction at all. Currently the rug the Roomba starts on top of; deliberately swappable for a charging dock later without any code change beyond re-wiring this field. Must have an EntityIdentity component and a tag matching landmarkEntityType (case-insensitive) - same manual-Inspector-wiring gotcha as journeyCalculator above. Leave unassigned to disable landmark seeding entirely.")]
    [SerializeField] private EntityIdentity landmarkIdentity;

    [Tooltip("entity_type reported for landmarkIdentity above - must match its GameObject's tag (case-insensitive), the same convention CollisionController uses for real collisions. A mismatch would create a second, inconsistent roster/sensitivity entry if this entity is ever collided with for real later.")]
    [SerializeField] private string landmarkEntityType = "rug";

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
    /// call all funnel through here. pauseDirective and movementDirective
    /// both default so this stays source-compatible with any call site
    /// that predates them (there are none currently, but no reason to
    /// force every future caller to pass every field explicitly either).
    ///
    /// Updated 2026-09-18 (Pause_Redesign_Implementation_Plan.md): replaces
    /// the old one-directional should_pause bool. pauseDirective is now a
    /// genuine tri-state - "pause" calls Pause(), "resume" calls Resume(),
    /// and null (no opinion this turn) leaves pause state exactly as it is.
    /// This is the first time Resume() is reachable from an LLM response at
    /// all; previously the only ways to resume were ResumeTimer's
    /// quiet-period timeout or the debug key.
    ///
    /// movementDirective is handed straight to JourneyCalculator - see
    /// Movement_Concurrency_Plan.md section 4, and JourneyCalculator.
    /// HandleLLMDirective's own doc comment for what happens when it's
    /// non-null.
    /// </summary>
    public void UpdateState(PADState pad, List<EntitySensitivity> sensitivities, string pauseDirective = null, MovementDirective movementDirective = null)
    {
        CurrentPad = pad;
        EntitySensitivities = sensitivities;
        OnStateUpdated?.Invoke();
        Debug.Log($"SessionManager UpdateState - PAD: {JsonConvert.SerializeObject(CurrentPad)}");
        Debug.Log($"SessionManager UpdateState - Evaluating pause_directive: {pauseDirective ?? "(none)"}");

        if (pauseController != null)
        {
            switch (pauseDirective)
            {
                case "pause":
                    Debug.Log($"SessionManager UpdateState - Pausing");
                    pauseController.Pause();
                    break;
                case "resume":
                    Debug.Log($"SessionManager UpdateState - Resuming");
                    pauseController.Resume();
                    break;
                    // null (or any unrecognized value): no opinion this turn -
                    // leave IsPaused exactly as it is.
            }
        }

        if (movementDirective != null)
        {
            if (journeyCalculator != null)
            {
                journeyCalculator.HandleLLMDirective(movementDirective);
            }
            else
            {
                Debug.LogWarning("SessionManager UpdateState - received a movement_directive but no JourneyCalculator is assigned; dropping it.");
            }
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

        await SeedLandmarkIfConfigured();
    }

    /// <summary>
    /// Seeds landmarkIdentity (the rug) as a permanent, always-available
    /// Journey immediately after session start - see landmarkIdentity's own
    /// tooltip and JourneyCalculator.SeedLandmarkJourney's doc comment for
    /// why. No-ops if landmarkIdentity isn't wired up - landmark seeding is
    /// opt-in, not required. The actual seeding work is in SeedLandmark
    /// below, shared with any other landmark seeded later, mid-session
    /// (e.g. the egress door - see DoorOpener and
    /// Egress_Door_Implementation_Plan.md).
    /// </summary>
    private async Awaitable SeedLandmarkIfConfigured()
    {
        if (landmarkIdentity == null)
        {
            return;
        }

        await SeedLandmark(landmarkIdentity, landmarkEntityType);
    }

    /// <summary>
    /// Registers identity as a permanent, always-available Journey/
    /// movement_directive destination. Two steps, mirroring the split
    /// between local (Unity) and server (Orchestrator) state everywhere
    /// else in this codebase: SeedLandmarkJourney creates the local
    /// ActiveJourney directly, then this method reports one ordinary
    /// synthetic "collision" event so the Orchestrator's entity_roster
    /// picks it up under the SAME entity_id, through the existing
    /// /arena/event pipeline unchanged - no new server-side code needed.
    /// entity_roster.record_collision has no strength/emotion gate, and a
    /// single ambivalence/0 event won't cross any event_aggregation
    /// threshold (see DEFAULT_ROLLUP_THRESHOLDS), so this costs zero LLM
    /// calls.
    ///
    /// Extracted 2026-09-23 (Egress_Door_Implementation_Plan.md) out of
    /// what was originally SeedLandmarkIfConfigured's whole body, so a
    /// second landmark - the egress door, seeded later, mid-session, once
    /// the dirt threshold is reached - goes through the exact same path
    /// the rug already used at startup, rather than a second, drifting
    /// copy of this logic. Safe to call at any time during play, not just
    /// at startup: JourneyCalculator.SeedLandmarkJourney itself no-ops if
    /// this entity_id has already been seeded.
    /// </summary>
    public async Awaitable SeedLandmark(EntityIdentity identity, string entityType)
    {
        if (identity == null)
        {
            return;
        }

        if (journeyCalculator == null)
        {
            Debug.LogWarning($"SessionManager: SeedLandmark called for entity_type '{entityType}' but journeyCalculator is not assigned - cannot seed a landmark Journey.");
            return;
        }

        string landmarkId = identity.GetOrAssignId();
        journeyCalculator.SeedLandmarkJourney(identity, entityType, identity.transform.position);

        await SendArenaEvent("collision", new List<EmotionState>
        {
            new EmotionState
            {
                entity_id = landmarkId,
                entity_type = entityType,
                emotion = "ambivalence",
                strength = 0f
            }
        });
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
            UpdateState(response.pad, response.entity_sensitivities, response.pause_directive, response.movement_directive);
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
            UpdateState(response.pad, response.entity_sensitivities, response.pause_directive, response.movement_directive);
            return response;
        }
    }
}
