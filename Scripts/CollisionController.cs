using System;
using System.Collections.Generic;
using UnityEngine;

public class CollisionController : MonoBehaviour
{
    [SerializeField] private TherapyChatController chatController;

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

        // Fire the local event first and synchronously - the ANS-layer
        // reaction (JourneyCalculator, eventually actual movement) must not
        // wait on the Orchestrator round trip that follows.
        OnEntityCollision?.Invoke(identity, entityType, contactPoint);

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

        chatController.DisplayLeftMessage(response.dialog);
        chatController.DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);
    }

    private EmotionState BuildEmotionState(string entityId, string entityType)
    {
        EntitySensitivity sensitivity = SessionManager.Instance.GetSensitivity(entityType);

        if (sensitivity != null)
        {
            return new EmotionState
            {
                entity_id = entityId,
                entity_type = entityType,
                emotion = sensitivity.emotion,
                strength = sensitivity.strength
            };
        }

        return new EmotionState
        {
            entity_id = entityId,
            entity_type = entityType,
            emotion = "ambivalence",
            strength = 0f
        };
    }
}