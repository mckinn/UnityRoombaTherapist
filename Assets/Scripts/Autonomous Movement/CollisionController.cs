using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class CollisionController : MonoBehaviour
{
    [SerializeField] private TherapyChatController chatController;

    [Tooltip("Used only to report journey_started/journey_distance on the collision EmotionState (Narrative_Log_Stream_Plan.md section 6) - not required for collision reporting itself. If left unassigned, those two fields are simply omitted (left at their EmotionState defaults) rather than causing an error.")]
    [SerializeField] private JourneyCalculator journeyCalculator;

    [Header("Arena Event Rate Limiting")]
    [Tooltip("Minimum seconds between /arena/event POSTs to the Orchestrator. Each POST triggers a real LLM API call, which costs real money and time regardless of how fast collisions are physically happening - this caps that rate globally, independent of which entity triggered it. The local ANS reaction (OnEntityCollision -> Journey/Behavior) is NOT rate-limited by this; only the remote report is skipped.")]
    [SerializeField] private float minSecondsBetweenArenaEvents = 2.0f;

    private float lastArenaEventTime = float.NegativeInfinity;

    /// <summary>
    /// Fired synchronously, immediately on physical contact with a tracked
    /// entity - before any network round trip to the Orchestrator. This is
    /// the single source of truth for "the Roomba collided with entity X";
    /// other systems (e.g. JourneyCalculator) subscribe to this instead of
    /// declaring their own OnCollisionEnter, so there is exactly one physics
    /// entry point for this concept, not several independently maintained
    /// ones.
    /// </summary>
    public event Action<EntityIdentity, string, Vector3> OnEntityCollision;

    private void OnCollisionEnter(Collision collision)
    {
        EntityIdentity identity = collision.gameObject.GetComponent<EntityIdentity>();
        if (identity == null)
        {
            return; // not a tracked entity (e.g., a wall)
        }

        string entityType = collision.gameObject.tag.ToLower();
        string entityId = identity.GetOrAssignId();

        // Point of impact, not the entity's Transform.position - consistent
        // with "no eyes, collision-only": the Roomba only ever knows where
        // it touched something.
        Vector3 contactPoint = collision.GetContact(0).point;

        // Fire the local event first and synchronously, unconditionally -
        // the ANS-layer reaction must not wait on, or be limited by, the
        // Orchestrator round trip that follows.
        OnEntityCollision?.Invoke(identity, entityType, contactPoint);

        if (Time.time - lastArenaEventTime < minSecondsBetweenArenaEvents)
        {
            Debug.Log($"CollisionController: arena event rate-limited, skipping report for entity_type '{entityType}' " +
                      $"({Time.time - lastArenaEventTime:F2}s since last report, limit {minSecondsBetweenArenaEvents:F2}s).");
            return;
        }
        lastArenaEventTime = Time.time;

        _ = ReportEventAsync(entityId, entityType);
    }

    private async Awaitable ReportEventAsync(string entityId, string entityType)
    {
        EmotionState reportedState = BuildEmotionState(entityId, entityType);
        List<EmotionState> emotionStates = new List<EmotionState> { reportedState };

        Dictionary<string, EntitySensitivity> beforeSensitivities = chatController.SnapshotSensitivities();

        OrchestratorResponse response = await SessionManager.Instance.SendArenaEvent("collision", emotionStates);

        if (response == null)
        {
            return;
        }

        if (response.dialog.Length > 0) chatController.DisplayLeftMessage(response.dialog);
        // chatController.DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);
    }

    private EmotionState BuildEmotionState(string entityId, string entityType)
    {
        EntitySensitivity sensitivity = SessionManager.Instance.GetSensitivity(entityType);

        EmotionState state;
        if (sensitivity != null)
        {
            state = new EmotionState
            {
                entity_id = entityId,
                entity_type = entityType,
                emotion = sensitivity.emotion,
                strength = sensitivity.strength
            };
        }
        else
        {
            state = new EmotionState
            {
                entity_id = entityId,
                entity_type = entityType,
                emotion = "ambivalence",
                strength = 0f
            };
        }

        // This method is only ever called from ReportEventAsync, which is
        // only ever invoked with "collision" (line above, in
        // OnCollisionEnter) - so no event_type check is needed here; this
        // controller has no proximity path. By the time this runs,
        // OnEntityCollision has already fired synchronously (see
        // OnCollisionEnter) and JourneyCalculator.HandleEntityCollision -
        // its subscriber - has already created/refreshed activeJourneys for
        // this frame, so the lookup below sees this collision's own result,
        // not a stale one. journeyCalculator is optional (see its Tooltip);
        // if unassigned, or no journey exists for this entity (e.g.
        // sensitivity was null/"none", which JourneyCalculator itself
        // ignores), journey_started/journey_distance are simply left at
        // their EmotionState defaults (false/null).
        if (journeyCalculator != null &&
            journeyCalculator.ActiveJourneys.TryGetValue(entityId, out ActiveJourney journey))
        {
            state.journey_started = true;
            state.journey_distance = journey.DestinationDistance;
        }

        return state;
    }
}