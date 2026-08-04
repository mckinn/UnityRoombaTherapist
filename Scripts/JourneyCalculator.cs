using UnityEngine;

/// <summary>
/// Step 1 of the autonomous-movement work: on collision with a tracked
/// entity, look up its EntitySensitivity (which emotion, and V/strength),
/// feed it into EmotionMovementConfig to get the Journey destination
/// distance (D), and log it.
///
/// No movement yet, deliberately - this step exists purely to verify that V
/// and D flow correctly end-to-end before Transform/Rigidbody are touched
/// at all. Direction, actual translation, and Arousal-driven speed are all
/// later steps.
///
/// This is a separate component from CollisionController on purpose.
/// CollisionController's job is reporting events upstream to the
/// Orchestrator/LLM - an async "tell the therapist what happened" concern.
/// This script's job is the Unity-local ANS response - an immediate
/// "decide what the body does" concern (see Architecture Decision Summary
/// #2: Unity executes movement immediately, without waiting on the
/// Orchestrator). Both react to the same physical collision but serve
/// different layers of the architecture, so they're kept apart rather than
/// merged into one component that does both jobs.
/// </summary>
public class JourneyCalculator : MonoBehaviour
{
    [Tooltip("The per-emotion L/H distance bounds asset.")]
    [SerializeField] private EmotionMovementConfig movementConfig;

    private void OnCollisionEnter(Collision collision)
    {
        EntityIdentity identity = collision.gameObject.GetComponent<EntityIdentity>();

        if (identity == null)
        {
            return; // not a tracked entity (e.g. a wall)
        }

        Debug.Log($"JourneyCalculator: entering OnCollisionEnter - {collision.gameObject.tag.ToLower()}");

        string entityType = collision.gameObject.tag.ToLower();
        EntitySensitivity sensitivity = SessionManager.Instance.GetSensitivity(entityType);

        if (sensitivity == null)
        {
            // Expected on genuinely first contact with an entity_type the
            // Orchestrator hasn't seeded yet - not a bug.
            Debug.Log($"JourneyCalculator: no sensitivity known yet for entity_type '{entityType}'.");
            return;
        }

        float destinationDistance = movementConfig.ComputeDestinationDistance(sensitivity.emotion, sensitivity.strength);

        Debug.Log($"JourneyCalculator: entity_type={entityType}, emotion={sensitivity.emotion}, " +
                  $"V={sensitivity.strength:F2} => D={destinationDistance:F2}");
    }
}