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
/// Subscribes to CollisionController.OnEntityCollision rather than
/// declaring its own OnCollisionEnter - there is exactly one physics entry
/// point for "collided with a tracked entity" (CollisionController), and
/// everything downstream, including this script, reacts to that single
/// event instead of each independently hooking the same physical concept.
/// </summary>
public class JourneyCalculator : MonoBehaviour
{
    [Tooltip("The CollisionController that owns the physical OnCollisionEnter event.")]
    [SerializeField] private CollisionController collisionController;

    [Tooltip("The per-emotion L/H distance bounds asset.")]
    [SerializeField] private EmotionMovementConfig movementConfig;

    private void OnEnable()
    {
        if (collisionController != null)
        {
            collisionController.OnEntityCollision += HandleEntityCollision;
        }
        else
        {
            Debug.LogWarning("JourneyCalculator has no CollisionController assigned - it will never receive collision events.");
        }
    }

    private void OnDisable()
    {
        if (collisionController != null)
        {
            collisionController.OnEntityCollision -= HandleEntityCollision;
        }
    }

    private void HandleEntityCollision(EntityIdentity identity, string entityType, Vector3 contactPoint)
    {
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