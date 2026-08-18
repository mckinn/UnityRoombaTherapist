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

    [Header("Behavior (Step 6)")]
    [Tooltip("The Behavior pattern renderer this script hands control to once the Roomba is fully stable.")]
    [SerializeField] private BehaviorController behaviorController;

    [Tooltip("Master on/off switch for Behavior pattern motion. Disabled by default while the color-based expression prototype (EmotionColorIndicator) is being evaluated instead - the procedural motion patterns were found confusing rather than clarifying in play-testing. This is the single point that decides whether BehaviorController.ApplyPattern ever actually gets called; unlike unchecking the BehaviorController component's own enabled checkbox, this reliably stops it, since ApplyPattern is called directly rather than through a Unity magic method. All Behavior code/config is preserved and can be re-enabled here at any time.")]
    [SerializeField] private bool behaviorMotionEnabled = false;

    /// <summary>
    /// The resolved journey to currently express, or null if anything is
    /// still unresolved (mid-pursuit) or nothing has ever resolved yet.
    /// Behavior only ever runs once EVERYTHING tracked is resolved - the
    /// two states (pursuing vs. expressing) are mutually exclusive by
    /// construction, so there's no authority conflict to arbitrate: a fresh
    /// collision un-resolving an entity drops this back to null next frame,
    /// handing control straight back to Journey pursuit with no special
    /// interruption handling needed.
    ///
    /// Among several simultaneously-resolved entities, the most recently
    /// resolved one wins (ActiveJourney.ResolvedSequence) - weight_i can't
    /// rank them, since resolved entries are all ~0 error by definition.
    /// </summary>
    public ActiveJourney StableExpressionJourney { get; private set; }

    private int resolutionSequenceCounter = 0;

    [Header("Debug")]
    [Tooltip("Testing aid: when true, keyboard input is added on top of the blended Journey movement instead of being ignored. Lets you mock the effect of an additional simultaneous movement source before Step 7 actually produces one. Leave false for normal play - this deliberately reintroduces the authority conflict the single-caller design otherwise prevents.")]
    [SerializeField] private bool allowManualNudgeDuringJourney = false;

    [Header("Clean/Map (Step 3)")]
    [Tooltip("Drives autonomous coverage movement when nothing else (Journey, keyboard, Behavior pattern motion) wants to move the Roomba this frame. Lowest priority in the dispatch - see DriveCleanMap and CleanMapController's own header comment.")]
    [SerializeField] private CleanMapController cleanMapController;

    [Header("Journey Stuck Recovery")]
    [Tooltip("If a Journey makes less than Journey Stuck Progress Threshold of net movement for this many seconds, it's considered stuck and a recovery redirect is triggered (see JourneyStuckRecovery). Per-journey, not shared - multiple simultaneously active journeys are tracked independently.")]
    [SerializeField] private float journeyStuckTimeThreshold = 1.5f;

    [Tooltip("Minimum net distance (world units) a Journey must move, relative to where its stuck-tracking baseline was last set, to NOT be considered stuck.")]
    [SerializeField] private float journeyStuckProgressThreshold = 0.05f;

    [Tooltip("Angular step size (degrees) applied per stuck retry within the same recovery episode - see JourneyStuckRecovery's persistent-handedness stepping.")]
    [SerializeField] private float journeyRecoveryStepDegrees = 45f;

    private readonly JourneyStuckRecovery stuckRecovery = new JourneyStuckRecovery();

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

        if (sensitivity.emotion == "none")
        {
            // The Orchestrator has seeded this entity_type but hasn't
            // formed a real opinion about it yet (server-side seeding on
            // first-ever contact). Treated the same as "no sensitivity
            // known" - no reaction, not a reaction targeting distance
            // zero. Without this, ComputeDestinationDistance("none", ...)
            // returns 0, and the Roomba tries to close to exactly zero
            // distance from the entity forever - the erratic-movement bug.
            Debug.Log($"JourneyCalculator: entity_type '{entityType}' has no formed emotion yet ('none') - ignoring.");
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
            journey.RedirectTarget = null;
            journey.RecoveryState = null;
            journey.StuckTrackingBaseline = transform.position;
            journey.StuckTimer = 0f;
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
                Resolved = false,
                RedirectTarget = null,
                RecoveryState = null,
                StuckTrackingBaseline = transform.position,
                StuckTimer = 0f
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

        if (StableExpressionJourney != null)
        {
            // Fully stable - nothing currently unresolved. Keyboard
            // immediately interrupts idle expression (lower priority than
            // the player actually wanting to drive). BehaviorController is
            // ticked every frame regardless of which - it needs to know
            // playerIsDriving to keep its anchor synced while overridden,
            // so resuming afterward doesn't snap.
            Vector3 keyboardOverride = ReadKeyboardDirection();
            bool playerIsDriving = keyboardOverride.sqrMagnitude > 0f;

            if (playerIsDriving)
            {
                playerController.Move(keyboardOverride, closingSpeed);
            }

            if (behaviorMotionEnabled)
            {
                behaviorController.ApplyPattern(StableExpressionJourney, playerIsDriving);
            }
            else if (!playerIsDriving)
            {
                // Nothing else wants this frame: no Behavior pattern motion
                // active, and the player isn't overriding. Clean/Map takes
                // the fallback that used to just do nothing here.
                DriveCleanMap(closingSpeed);
            }

            return;
        }

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
            else if (blendedDirection == Vector3.zero)
            {
                // True fresh idle (no active journey at all, not even a
                // debug-nudge situation) and no keyboard input either.
                // Explicit blendedDirection check, not just "we're inside
                // this block" - allowManualNudgeDuringJourney could put us
                // here while a real journey IS active, and Clean/Map must
                // never contend with that.
                DriveCleanMap(closingSpeed);
            }
        }
    }

    /// <summary>
    /// Asks CleanMapController for a direction and drives it through the
    /// same single Move() primitive everything else uses, at the same
    /// Arousal-scaled speed. No-ops if cleanMapController isn't assigned or
    /// has nothing to offer (coverage complete, or not yet ready) - callers
    /// don't need to check either case themselves.
    /// </summary>
    private void DriveCleanMap(float closingSpeed)
    {
        if (cleanMapController == null)
        {
            return;
        }

        Vector3? direction = cleanMapController.GetSteeringDirection();
        if (direction.HasValue && direction.Value.sqrMagnitude > 0.0001f)
        {
            playerController.Move(direction.Value, closingSpeed);
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
        ActiveJourney mostRecentlyResolved = null;

        foreach (KeyValuePair<string, ActiveJourney> kvp in activeJourneys)
        {
            ActiveJourney journey = kvp.Value;

            if (journey.Resolved)
            {
                if (mostRecentlyResolved == null || journey.ResolvedSequence > mostRecentlyResolved.ResolvedSequence)
                {
                    mostRecentlyResolved = journey;
                }
                continue;
            }

            (Vector3 direction, float weight) = EvaluateJourney(journey);
            if (journey.Resolved)
            {
                // Just resolved during this frame's evaluation.
                if (mostRecentlyResolved == null || journey.ResolvedSequence > mostRecentlyResolved.ResolvedSequence)
                {
                    mostRecentlyResolved = journey;
                }
                continue;
            }

            weightedSum += direction * weight;
            totalWeight += weight;
        }

        // Only express once EVERYTHING tracked is resolved - see
        // StableExpressionJourney doc comment.
        StableExpressionJourney = (totalWeight <= 0.0001f) ? mostRecentlyResolved : null;

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
            journey.ResolvedSequence = ++resolutionSequenceCounter;
            journey.RedirectTarget = null;
            journey.RecoveryState = null;
            return (Vector3.zero, 0f);
        }

        Vector3 directionAway = d > 0.0001f ? awayFromEntity / d : transform.forward;
        directionAway.y = 0f;
        directionAway.Normalize();

        Vector3 radialDirection = directionAway * Mathf.Sign(error);

        CheckJourneyStuckAndMaybeRedirect(journey, roombaPos, entityPos, radialDirection);

        Vector3 direction;
        if (journey.RedirectTarget.HasValue)
        {
            Vector3 toRedirect = journey.RedirectTarget.Value - roombaPos;
            // Extremely unlikely (would mean sitting exactly on the
            // redirect point without having resolved the actual distance
            // goal) but guarded rather than risking a zero-direction Move
            // call - falls back to the plain radial direction for this
            // one tick only; RedirectTarget itself is left alone; a fresh
            // stuck check next tick will decide whether to advance it.
            direction = toRedirect.sqrMagnitude > 0.0001f ? toRedirect.normalized : radialDirection;
        }
        else
        {
            direction = radialDirection;
        }

        float weight = Mathf.Abs(error);

        return (direction, weight);
    }

    /// <summary>
    /// Per-journey stuck detection. Tracks net movement against a baseline
    /// snapshot; if too little progress accumulates for too long, asks
    /// stuckRecovery for the next recovery target and stores it on the
    /// journey. Resets the baseline/timer whenever real progress is made,
    /// or whenever a new recovery target is chosen - each attempt (radial
    /// or redirected) gets its own fresh progress window to prove itself
    /// in, rather than being judged against a stale baseline from before.
    /// </summary>
    private void CheckJourneyStuckAndMaybeRedirect(ActiveJourney journey, Vector3 roombaPos, Vector3 entityPos, Vector3 currentDirection)
    {
        journey.StuckTimer += Time.fixedDeltaTime;

        float netProgress = Vector3.Distance(roombaPos, journey.StuckTrackingBaseline);
        if (netProgress >= journeyStuckProgressThreshold)
        {
            journey.StuckTrackingBaseline = roombaPos;
            journey.StuckTimer = 0f;
            return;
        }

        if (journey.StuckTimer >= journeyStuckTimeThreshold)
        {
            journey.RedirectTarget = stuckRecovery.GetNextRecoveryTarget(
                journey, currentDirection, entityPos, journey.DestinationDistance, journeyRecoveryStepDegrees);

            journey.StuckTrackingBaseline = roombaPos;
            journey.StuckTimer = 0f;

            Debug.LogWarning($"JourneyCalculator: journey for entity_id={journey.Entity?.GetOrAssignId()} " +
                              $"stuck for {journeyStuckTimeThreshold:F1}s - stepping recovery angle, new target {journey.RedirectTarget.Value}.");
        }
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