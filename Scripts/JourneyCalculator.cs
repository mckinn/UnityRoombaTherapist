using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the active set of Journeys (see ActiveJourney) AND is the single
/// authority for what drives PlayerController.Move() each frame - either the
/// weighted blend of all active, unresolved Journeys, or manual keyboard
/// input when the active set is empty. Never both in the same frame.
///
/// PlayerController no longer reads input itself - it's a pure motor. That
/// centralization is deliberate: two independent FixedUpdate methods each
/// deciding to call Move() would reintroduce the same class of
/// ordering-dependent fragility we hit (and fixed) with collision handling.
///
/// Multi-entity handling: weight_i = |d_i - D_i| (how far each entity's
/// current distance is from its own target distance). The blended direction
/// is the weight-averaged sum of each entity's individual direction. An
/// entity that resolves (weight -> 0) drops out of the sum automatically -
/// active-set membership and blend weighting are the same mechanism, not
/// two independently-maintained ones. If weights on opposing entities
/// happen to cancel out, the Roomba nets to a stall - that's an accepted,
/// legitimate outcome (a Roomba caught between fear and curiosity), not a
/// bug to special-case around.
/// </summary>
public class JourneyCalculator : MonoBehaviour
{
    [Tooltip("The CollisionController that owns the physical OnCollisionEnter event.")]
    [SerializeField] private CollisionController collisionController;

    [Tooltip("The motor this script drives via Move().")]
    [SerializeField] private PlayerController playerController;

    [Tooltip("The per-emotion L/H distance bounds asset.")]
    [SerializeField] private EmotionMovementConfig movementConfig;

    [Header("Debug")]
    [Tooltip("Testing aid: when true, keyboard input is added on top of the blended Journey movement instead of being ignored. Lets you mock the effect of an additional simultaneous movement source (e.g. a future Behavior pattern) before Step 6 actually produces one. Leave false for normal play - this deliberately reintroduces the authority conflict the single-caller design otherwise prevents.")]
    [SerializeField] private bool allowManualNudgeDuringJourney = false;

    private readonly Dictionary<string, ActiveJourney> activeJourneys = new Dictionary<string, ActiveJourney>();

    public IReadOnlyDictionary<string, ActiveJourney> ActiveJourneys => activeJourneys;

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

        string entityId = identity.GetOrAssignId();
        float destinationDistance = movementConfig.ComputeDestinationDistance(sensitivity.emotion, sensitivity.strength);

        if (activeJourneys.TryGetValue(entityId, out ActiveJourney journey))
        {
            // Re-trigger: refresh everything, including un-resolving it if
            // a fresh collision means it's back in play.
            journey.Emotion = sensitivity.emotion;
            journey.Strength = sensitivity.strength;
            journey.DestinationDistance = destinationDistance;
            journey.LastKnownPosition = contactPoint;
            journey.Resolved = false;
        }
        else
        {
            journey = new ActiveJourney
            {
                Entity = identity,
                EntityType = entityType,
                Emotion = sensitivity.emotion,
                Strength = sensitivity.strength,
                DestinationDistance = destinationDistance,
                LastKnownPosition = contactPoint,
                Resolved = false
            };
            activeJourneys[entityId] = journey;
        }

        Debug.Log($"JourneyCalculator: entity_id={entityId}, entity_type={entityType}, emotion={journey.Emotion}, " +
                  $"V={journey.Strength:F2} => D={journey.DestinationDistance:F2}, epsilon={movementConfig.ComputeEpsilon():F3}");
    }

    private void FixedUpdate()
    {
        // PADState is a class, not a struct - it can be null before a
        // session's first response lands. Defaulting to MinClosingSpeed
        // directly (the calmest-state speed), not a neutral midpoint - "we
        // don't know the emotional state yet" reads as "assume calm," not
        // "assume average."
        float closingSpeed = SessionManager.Instance.CurrentPad != null
            ? movementConfig.ComputeClosingSpeed(SessionManager.Instance.CurrentPad.arousal)
            : movementConfig.MinClosingSpeed;

        Vector3 blendedDirection = ComputeBlendedJourneyDirection();

        if (blendedDirection != Vector3.zero)
        {
            // Capped at MaxClosingSpeed by ComputeClosingSpeed itself - the
            // same ceiling epsilon was derived from, so this can't violate
            // the deadband guarantee regardless of PAD state or how many
            // entities are contributing to the blend.
            playerController.Move(blendedDirection, closingSpeed);
        }

        // Keyboard applies whenever there's no active journey (the normal
        // manual-fallback case), OR when one is active AND the debug nudge
        // toggle is explicitly on. It's never applied unconditionally
        // alongside an active journey - that's the single-authority
        // guarantee this class exists to enforce.
        if (allowManualNudgeDuringJourney || blendedDirection == Vector3.zero)
        {
            Vector3 keyboardInput = ReadKeyboardDirection();
            if (keyboardInput.sqrMagnitude > 0f)
            {
                // Arousal-driven too, at the same rate a Journey would use
                // right now - the Roomba's overall responsiveness is a
                // property of its emotional state, not of who's steering.
                playerController.Move(keyboardInput, closingSpeed);
            }
        }
    }

    /// <summary>
    /// Weighted blend across every active, unresolved Journey: each
    /// contributes its own direction, weighted by weight_i = |d_i - D_i|.
    /// Combined = normalize(sum(weight_i * direction_i)). Returns
    /// Vector3.zero if there's nothing unresolved to blend (empty active
    /// set), or if what's active happens to net to zero (opposing pulls
    /// cancelling - an accepted outcome, see class summary).
    /// </summary>
    private Vector3 ComputeBlendedJourneyDirection()
    {
        Vector3 weightedSum = Vector3.zero;
        float totalWeight = 0f;

        foreach (KeyValuePair<string, ActiveJourney> kvp in activeJourneys)
        {
            ActiveJourney journey = kvp.Value;
            if (journey.Resolved)
            {
                continue;
            }

            (Vector3 direction, float weight) = EvaluateJourney(journey);
            if (journey.Resolved)
            {
                // Resolved during this frame's evaluation - contributes
                // nothing, same as one that was already resolved.
                continue;
            }

            weightedSum += direction* weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0.0001f)
           {
                return Vector3.zero;
            }

        return weightedSum.normalized;
    }

    /// </summary>
    /// Computes distance d from the Roomba to the journey's last known
    /// (planar) position, checks it against the epsilon deadband around D,
    /// and marks the journey Resolved if within it. Returns the direction
    /// to move if not resolved (Vector3.zero if resolved).
    /// and marks the journey Resolved if within it. If not resolved,
    /// returns this entity's individual direction and its blend weight
    /// (|d - D|); returns (zero, 0) if resolved.
    ///
    /// Direction is polarity-agnostic by design: it always drives d toward
    /// D, whichever way that means moving. This works without classifying
    /// emotions as "avoid" vs. "approach" only because sticky resolution
    /// (this method) prevents an active journey from ever being evaluated
    /// far past D on the wrong side - see prior discussion on why epsilon
    /// must be derived from max closing speed for that guarantee to hold.
    /// Note that guarantee was established for a single active journey;
    /// with the blend, a fast-moving combination of several entities could
    /// still in principle overshoot a given entity's deadband and settle
    /// into oscillation rather than a clean resolve - accepted for now
    /// rather than solved.
    /// </summary>
    private (Vector3 direction, float weight) EvaluateJourney(ActiveJourney journey)
    {
        Vector3 roombaPos = transform.position;
        roombaPos.y = 0f;
        Vector3 entityPos = journey.LastKnownPosition;
        entityPos.y = 0f;

        Vector3 awayFromEntity = roombaPos - entityPos;
        float d = awayFromEntity.magnitude;
        float error = journey.DestinationDistance - d; // positive: too close, move away. negative: too far, move toward.
        float epsilon = movementConfig.ComputeEpsilon();

        if (Mathf.Abs(error) <= epsilon)
        {
            journey.Resolved = true;
            return ( Vector3.zero, 0f );
        }

        Vector3 directionAway = d > 0.0001f ? awayFromEntity / d : transform.forward;
        directionAway.y = 0f;
        directionAway.Normalize();

        Vector3 direction = directionAway * Mathf.Sign(error);
        float weight = Mathf.Abs(error);
        
        return (direction, weight);
    }

    private Vector3 ReadKeyboardDirection()
    {
        Vector3 moveInput = Vector3.zero;

        if (Keyboard.current.upArrowKey.isPressed) moveInput.z += 1f;
        if (Keyboard.current.downArrowKey.isPressed) moveInput.z -= 1f;
        if (Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
        if (Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;

        return moveInput;
    }
}