using UnityEngine;
using System.Collections.Generic;

public class CollisionController : MonoBehaviour
{
    [SerializeField] private TherapyChatController chatController;

    private async void OnCollisionEnter(Collision collision)
    {
        EntityIdentity identity = collision.gameObject.GetComponent<EntityIdentity>();
        if (identity == null)
        {
            return; // not a tracked entity (e.g., a wall) - ignore for now
        }

        string entityType = collision.gameObject.tag.ToLower();
        string entityId = identity.GetOrAssignId();

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
        {
            if (sensitivity?.entity_type == entityType)
            {
                return new EmotionState
                {
                    entity_id = entityId,
                    entity_type = entityType,
                    emotion = sensitivity.emotion,
                    strength = sensitivity.strength
                };
            }
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