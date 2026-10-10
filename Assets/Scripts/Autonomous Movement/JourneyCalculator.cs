using System;
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

    [Header("Managed Pause")]
    [Tooltip("The Paused gate - see PauseController's own header comment and Pause_Redesign_Implementation_Plan.md. As of 2026-09-18, Paused gates ONLY the autonomous Clean/Map fallback (see FixedUpdate/DriveCleanMap below) - an active, unresolved Journey (emotion- or LLM-driven) keeps evaluating and moving regardless of IsPaused. There is no local auto-pause trigger of any kind here any more; IsPaused only ever changes via an LLM pause_directive or the debug key, both handled entirely inside PauseController/SessionManager.")]
    [SerializeField] private PauseController pauseController;

    [Header("Freeze (Freeze_And_Stop_Implementation_Plan.md)")]
    [Tooltip("The Freeze latch - a separate gate from Paused above, not a stronger version of it. IsFrozen gates DriveCleanMap (both call sites, same as isPaused does) AND keyboard input (both call sites) - unlike Paused, which never touches keyboard. An already-active, unresolved Journey still keeps moving regardless of IsFrozen, exactly as it does regardless of IsPaused, so it runs to completion rather than cutting off mid-motion; what IsFrozen actually prevents is any NEW or re-activated Journey - see HandleEntityCollision and HandleLLMDirective below, both of which no-op while frozen.")]
    [SerializeField] private FreezeController freezeController;

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

    [Tooltip("Whether Arrow Key navigation is allowed at all.")]
    [SerializeField] private bool wasdAllowed = true;

    [Tooltip("Minimum seconds between blend-diagnostic log lines (see ComputeBlendedJourneyDirection) - throttled since it would otherwise log every FixedUpdate tick while 2+ journeys are simultaneously active.")]
    [SerializeField] private float blendDiagnosticLogIntervalSeconds = 0.5f;

    private float lastBlendDiagnosticLogTime = float.NegativeInfinity;

    [Header("Clean/Map (Step 3)")]
    [Tooltip("Drives autonomous coverage movement when nothing else (Journey, keyboard) wants to move the Roomba this frame. Lowest priority in the dispatch - see DriveCleanMap and CleanMapController's own header comment.")]
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

    // entity_id -> Time.time of the last movement_directive processed for
    // it - see the duplicate-dispatch check in HandleLLMDirective, added
    // 2026-09-14 after playtests showed apparently-duplicate log lines
    // that were too fast to confirm visually against the game view.
    private readonly Dictionary<string, float> lastDirectiveProcessedTime = new Dictionary<string, float>();

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

    /// <summary>
    /// Fired synchronously on every physical collision with a tracked
    /// entity - see CollisionController.OnEntityCollision. Refreshes
    /// (or creates) the ActiveJourney for this entity_id: emotion,
    /// strength, DestinationDistance and LastKnownPosition always reflect
    /// the most recent physical contact, and Resolved is cleared since a
    /// fresh collision means the entity is back in play.
    ///
    /// Deliberately does NOT reset RedirectTarget/RecoveryState/
    /// StuckTrackingBaseline/StuckTimer on a re-trigger - fixed 2026-09-12,
    /// the same bug and the same fix as HandleLLMDirective's (see that
    /// method's doc comment for the full rationale). A physically wedged
    /// Roomba against a multi-collider entity (e.g. several colliders
    /// making up one table) can fire OnCollisionEnter far more often than
    /// the 1.5s stuck-time threshold - every fresh re-trigger was wiping
    /// the stuck clock and any in-progress recovery episode before either
    /// could ever run uninterrupted, so a Roomba bouncing rapidly between
    /// two contact points never registered as "stuck" even though it was
    /// making zero net progress. As with HandleLLMDirective, a repeated
    /// collision with an entity already being pursued is not new
    /// navigational information the way a *fresh* encounter is - only the
    /// fields that genuinely change (Emotion/Strength/DestinationDistance/
    /// LastKnownPosition/Resolved) are refreshed; stuck-recovery
    /// bookkeeping runs uninterrupted across repeated collisions, exactly
    /// as it does across repeated FixedUpdate ticks with none at all.
    ///
    /// Extended 2026-09-25 (Freeze_And_Stop_Implementation_Plan.md): no-ops
    /// entirely while frozen, before even checking sensitivity - a fresh
    /// collision during a Freeze must not create or re-activate a Journey,
    /// which is the actual mechanism behind "should just not restart
    /// again." In practice this rarely fires once frozen anyway, since
    /// movement is also gated in FixedUpdate by then - but something else
    /// moving into a stationary Roomba (as happened with the door's own
    /// animation once) is exactly the scenario this guards against.
    /// </summary>
    private void HandleEntityCollision(EntityIdentity identity, string entityType, Vector3 contactPoint)
    {
        if (freezeController != null && freezeController.IsFrozen)
        {
            Debug.Log($"JourneyCalculator: collision with entity_type '{entityType}' ignored - frozen.");
            return;
        }

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
            // Re-trigger: refresh what a fresh collision actually tells us
            // (emotion/strength/distance/contact point), including
            // un-resolving it if a fresh collision means it's back in
            // play. Stuck-recovery bookkeeping is deliberately left alone
            // - see this method's doc comment above.
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
                Resolved = false,
                RedirectTarget = null,
                RecoveryState = null,
                StuckTrackingBaseline = transform.position,
                StuckTimer = 0f
            };
            activeJourneys[entityId] = journey;
        }

        // contactPoint is logged here (2026-09-12, diagnostic addition) so a
        // multi-collider entity's LastKnownPosition jumping between
        // distinct physical contact points - suspected cause of
        // unproductive oscillation when trapped among several colliders on
        // one entity - is directly visible in the log, rather than only
        // inferable from downstream symptoms.
        Debug.Log($"JourneyCalculator: entity_id={entityId}, entity_type={entityType}, emotion={journey.Emotion}, " +
                  $"V={journey.Strength:F2} => D={journey.DestinationDistance:F2}, epsilon={movementConfig.ComputeEpsilon():F3}, " +
                  $"contactPoint={contactPoint}");
    }

    /// <summary>
    /// Applies an LLM-issued movement_directive - see
    /// Movement_Concurrency_Plan.md section 4, items 1 (D-computation) and
    /// 3 (entity resolution: this only ever receives an already-resolved
    /// entity_id, never a name). Called from SessionManager.UpdateState
    /// whenever an Orchestrator response carries a non-null
    /// movement_directive.
    ///
    /// D is computed ONCE here, as a snapshot at directive-processing time
    /// - not re-derived every frame - using the exact same distance metric
    /// EvaluateJourney uses: planar (y=0) distance from the Roomba's
    /// current position to the journey's LastKnownPosition. D = d_current
    /// * (1 - percent/100) for "closer", D = d_current * (1 + percent/100)
    /// for "further". percent is already clamped to [0, 100] server-side
    /// (llm.py) but is clamped again here defensively, since an
    /// out-of-range value feeding this formula directly could otherwise
    /// produce a negative destination distance - a physically meaningless
    /// target, not just a cosmetic issue.
    ///
    /// No-ops (with a warning, not an error) if target_entity_id has no
    /// corresponding ActiveJourney yet. This is the same "not a bug"
    /// defensive stance as HandleEntityCollision's own null-sensitivity
    /// early return above: the roster (server-side, in the Orchestrator)
    /// and activeJourneys (here) are populated by two independently-timed
    /// triggers on the same physical collision - the roster updates
    /// unconditionally, server-side, within the very request that reports
    /// the collision, while an activeJourneys entry is only created once a
    /// sensitivity is already known *locally*, at the moment of physical
    /// contact (see CollisionController: the local OnEntityCollision fires
    /// before the network round trip that would have taught this client
    /// about a brand-new entity_type). So an entity can be on the roster
    /// (touched once, sensitivity unknown at the time) without ever having
    /// a local journey - not just in the same frame it was first touched,
    /// but for as long as it isn't touched again after its sensitivity
    /// becomes known. Dropping the directive is the correct, safe response
    /// to that gap, not a workaround for a bug.
    ///
    /// Deliberately does NOT reset RedirectTarget/RecoveryState/
    /// StuckTrackingBaseline/StuckTimer the way HandleEntityCollision does
    /// on a fresh physical collision - found and fixed 2026-09-11 after
    /// play-testing a trapped Roomba: repeated movement_directives for the
    /// same entity (which the arena-event rate limit means can arrive
    /// roughly every 2 seconds) were wiping the stuck-recovery clock and
    /// any in-progress angular-stepping episode just before or right
    /// around the point journeyStuckTimeThreshold (1.5s) would otherwise
    /// have crossed it - so JourneyStuckRecovery's redirect (and
    /// eventually the universal stuck-abandonment) never got an
    /// uninterrupted run, and the Roomba kept re-computing the exact same
    /// blocked radial "away from contact point" direction forever. Unlike
    /// a fresh collision - which really is new information ("I found this
    /// again") - a repeated directive for an entity the Roomba is already
    /// pursuing is often the OPPOSITE of new information: it's the LLM
    /// noticing the Roomba still hasn't gotten anywhere. Only
    /// DestinationDistance (what the directive actually changes) and
    /// Resolved (since the new D may or may not already be satisfied) are
    /// touched here; stuck-recovery bookkeeping is left to run
    /// uninterrupted across repeated directives, exactly as it does across
    /// repeated FixedUpdate ticks with no directive at all.
    /// </summary>
    public void HandleLLMDirective(MovementDirective directive)
    {
        if (directive == null)
        {
            return;
        }

        // Extended 2026-09-25 (Freeze_And_Stop_Implementation_Plan.md): a
        // movement_directive can re-activate (un-resolve) an already-
        // existing Journey just as effectively as a fresh collision can -
        // see this method's own DestinationDistance/Resolved assignment
        // below - so it needs the same no-op-while-frozen guard
        // HandleEntityCollision has, for the same "should not restart
        // again" reason.
        if (freezeController != null && freezeController.IsFrozen)
        {
            Debug.Log($"JourneyCalculator: movement_directive for entity_id={directive.target_entity_id} ignored - frozen.");
            return;
        }

        if (!activeJourneys.TryGetValue(directive.target_entity_id, out ActiveJourney journey))
        {
            Debug.LogWarning($"JourneyCalculator: movement_directive targets entity_id={directive.target_entity_id}, " +
                              $"which has no active journey yet - dropping directive.");
            return;
        }

        // Duplicate-dispatch check (added 2026-09-14): a genuinely
        // duplicate call for the SAME entity_id inside the exact same
        // physics tick would compute an identical d_current (transform.
        // position hasn't moved between them), which is what playtesting
        // surfaced as suspicious back-to-back log lines too fast to watch
        // for visually. Scoped to (entity_id, Time.time) rather than a
        // bare Time.time comparison - two DIFFERENT entities' directives
        // legitimately landing in the same tick is not a bug and
        // shouldn't be flagged as one. LogError, not throw - see this
        // method's own reasoning for why a real exception here is riskier
        // than the bug it would be catching.
        if (lastDirectiveProcessedTime.TryGetValue(directive.target_entity_id, out float lastProcessedTime)
            && lastProcessedTime == Time.time)
        {
            Debug.LogError($"JourneyCalculator: movement_directive for entity_id={directive.target_entity_id} " +
                            $"was processed twice in the same physics tick (Time.time={Time.time:F3}) - " +
                            $"likely duplicate dispatch, not a coincidence.");
        }
        lastDirectiveProcessedTime[directive.target_entity_id] = Time.time;

        Vector3 roombaPos = transform.position;
        roombaPos.y = 0f;
        Vector3 entityPos = journey.LastKnownPosition;
        entityPos.y = 0f;

        float dCurrent = Vector3.Distance(roombaPos, entityPos);
        float fraction = Mathf.Clamp(directive.percent, 0f, 100f) / 100f;
        float newDestinationDistance = directive.direction == "closer"
            ? dCurrent * (1f - fraction)
            : dCurrent * (1f + fraction);

        journey.DestinationDistance = newDestinationDistance;
        journey.Resolved = false;

        // entityPos is logged here (2026-09-12, diagnostic addition) so it
        // can be directly compared against the contactPoint values in
        // HandleEntityCollision's log for the same entity_id - if
        // entityPos is jumping between calls, this directive's "further"
        // is being measured from a different anchor than the previous one
        // was, which is the suspected cause of oscillation when trapped
        // among a multi-collider entity's several contact points.
        Debug.Log($"JourneyCalculator: movement_directive entity_id={directive.target_entity_id}, " +
                  $"direction={directive.direction}, percent={directive.percent:F0} => " +
                  $"d_current={dCurrent:F2}, new D={newDestinationDistance:F2}, entityPos={entityPos}");
    }

    /// <summary>
    /// Directly creates a permanent ActiveJourney for a designated
    /// "landmark" entity - added 2026-09-14 after several trapped-Roomba
    /// playtests (see Movement_Concurrency_Plan.md's directionality
    /// discussion) showed movement_directive only working as an escape
    /// mechanism when the roster happened to already contain some OTHER
    /// entity for the LLM to reference; a single-entity trap with an
    /// otherwise-empty roster left it with no alternative at all. Called
    /// once, from SessionManager right after session start (see
    /// SessionManager.landmarkIdentity) - NOT through the normal
    /// HandleEntityCollision path, which deliberately bails out early for
    /// an entity with no known or "none" sensitivity (correctly so, for a
    /// REAL first contact - see that method's own comments). This
    /// journey's purpose isn't emotional pursuit, it's guaranteed
    /// availability, so it's created directly here instead.
    ///
    /// Starts Resolved = true (zero blend weight, no pull on its own) -
    /// it's inert until a movement_directive actually targets it, at
    /// which point HandleLLMDirective un-resolves it exactly like any
    /// other journey. Idempotent: a second call for an entity_id already
    /// tracked (e.g. if this entity later collides for real) is a no-op,
    /// so calling this more than once, or the entity later being touched
    /// through the ordinary collision path, is safe either way.
    /// </summary>
    public void SeedLandmarkJourney(EntityIdentity identity, string entityType, Vector3 position)
    {
        if (identity == null)
        {
            Debug.LogWarning("JourneyCalculator: SeedLandmarkJourney called with a null identity - ignoring.");
            return;
        }

        string entityId = identity.GetOrAssignId();

        if (activeJourneys.ContainsKey(entityId))
        {
            return;
        }

        float destinationDistance = movementConfig.ComputeDestinationDistance("destination", 0f);

        activeJourneys[entityId] = new ActiveJourney
        {
            Entity = identity,
            EntityType = entityType,
            Emotion = "destination",
            Strength = 0f,
            DestinationDistance = destinationDistance,
            LastKnownPosition = position,
            Resolved = true,
            RedirectTarget = null,
            RecoveryState = null,
            StuckTrackingBaseline = transform.position,
            StuckTimer = 0f
        };

        Debug.Log($"JourneyCalculator: seeded landmark journey entity_id={entityId}, entity_type={entityType}, " +
                  $"position={position}, D={destinationDistance:F2} (inert until a movement_directive targets it).");
    }

    /// <summary>
    /// Updated 2026-09-18 (Pause_Redesign_Implementation_Plan.md section
    /// 5.5): Paused no longer gates this whole method. It previously sat as
    /// a top-of-method early return that froze EVERYTHING - Journey
    /// evaluation (including stuck-timers), Clean/Map, all of it - which is
    /// exactly the "Journeys interrupted by pause" behavior the redesign
    /// eliminates. Journeys (emotion- or LLM-driven) are not interruptable
    /// by Paused any more: ComputeBlendedJourneyDirection, EvaluateJourney,
    /// and CheckJourneyStuckAndMaybeRedirect all keep running and the
    /// blended Move() call still happens, paused or not. isPaused gates
    /// exactly one thing - DriveCleanMap, at both call sites below - which
    /// is what makes autonomous exploration (and therefore incidental dirt
    /// collection via Clean/Map) actually stop during a pause. Keyboard is
    /// unconditional with respect to Paused specifically - see the next
    /// paragraph for isFrozen, which is a different story.
    ///
    /// There is also no more settle-detection call here at all (see
    /// PauseController's header comment) - a Journey settling into
    /// StableExpressionJourney never itself changes IsPaused. What happens
    /// next when everything resolves is ordinary dispatch priority: Clean/
    /// Map picks up if there's coverage left and nothing else claims the
    /// frame, or the Roomba simply has nothing to move toward if
    /// nothing_to_do is true - neither case touches pause state.
    ///
    /// Extended 2026-09-25 (Freeze_And_Stop_Implementation_Plan.md):
    /// isFrozen gates DriveCleanMap at both call sites, same as isPaused -
    /// AND keyboard input at both its call sites too, unlike isPaused,
    /// per Steve's call that manual/arrow navigation should also freeze
    /// ("this is an opportunity for discussion and observation, not
    /// further gameplay"). Exactly like Paused, an already-active,
    /// unresolved Journey's blended Move() call (below) is NOT gated by
    /// isFrozen - it keeps running to completion rather than cutting off
    /// mid-motion. What Freeze actually prevents starting or restarting is
    /// enforced upstream of this method entirely, in HandleEntityCollision
    /// and HandleLLMDirective.
    /// </summary>
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

        bool isPaused = pauseController != null && pauseController.IsPaused;
        bool isFrozen = freezeController != null && freezeController.IsFrozen;

        Vector3 blendedDirection = ComputeBlendedJourneyDirection();

        if (StableExpressionJourney != null)
        {
            // Fully stable - nothing currently unresolved. Keyboard
            // immediately interrupts idle expression (lower priority than
            // the player actually wanting to drive). Behavior pattern
            // motion (BehaviorController) was retired 2026-09-11 - see
            // Movement_Concurrency_Plan.md section 4 item 4 - so Clean/Map
            // is now the only thing that can claim a stable-expression
            // frame the player isn't driving.
            Vector3 keyboardOverride = ReadKeyboardDirection();
            bool playerIsDriving = keyboardOverride.sqrMagnitude > 0f;

            if (playerIsDriving)
            {
                // isFrozen here means the Roomba simply sits still this
                // frame rather than falling through to DriveCleanMap -
                // manual navigation freezes too (Freeze_And_Stop_
                // Implementation_Plan.md), it doesn't hand off to autonomy.
                if (!isFrozen)
                {
                    playerController.Move(keyboardOverride, closingSpeed);
                }
            }
            else if (!isPaused && !isFrozen)
            {
                // Paused/Frozen both gate this autonomous-exploration
                // fallback - a resolved/settled state with nothing else
                // claiming the frame.
                DriveCleanMap(closingSpeed);
            }

            return;
        }

        if (blendedDirection != Vector3.zero)
        {
            // Not gated by isPaused - an active Journey (emotion- or
            // LLM-driven) keeps progressing regardless of Paused. This is
            // the "Journeys are not interruptable by pause" requirement.
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
                // isFrozen: same reasoning as the StableExpressionJourney
                // branch above - manual navigation freezes too, and doesn't
                // fall through to DriveCleanMap below when it's suppressed.
                if (!isFrozen)
                {
                    playerController.Move(keyboardInput, closingSpeed);
                }
            }
            else if (blendedDirection == Vector3.zero && !isPaused && !isFrozen)
            {
                // True fresh idle (no active journey at all, not even a
                // debug-nudge situation), no keyboard input, and not
                // paused or frozen. Explicit blendedDirection check, not
                // just "we're inside this block" - allowManualNudgeDuringJourney
                // could put us here while a real journey IS active, and
                // Clean/Map must never contend with that. The isPaused/
                // isFrozen checks are the same gates as the
                // StableExpressionJourney branch above - see this method's
                // own doc comment.
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

        // Diagnostic snapshot (2026-09-13, throttled - see
        // blendDiagnosticLogIntervalSeconds). Added after a playtest where
        // a Roomba trapped under one entity broke free only once OTHER,
        // unrelated journeys (a chair, a different table) had large
        // outstanding movement_directive-driven errors - the existing
        // per-event logs (collision, movement_directive) show each
        // journey's own state in isolation, but not which journey actually
        // dominated the resulting blended direction when several compete.
        // Only worth logging (and allocating) when 2+ journeys are
        // simultaneously in the blend - a single active journey's
        // direction/weight is already fully visible from its own
        // collision/directive log line.
        bool shouldLogBlend = Time.time - lastBlendDiagnosticLogTime >= blendDiagnosticLogIntervalSeconds;
        List<string> blendDiagnostics = shouldLogBlend ? new List<string>() : null;

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

            blendDiagnostics?.Add($"{kvp.Key}: D={journey.DestinationDistance:F2}, weight={weight:F2}, direction={direction}");
        }

        if (shouldLogBlend && blendDiagnostics.Count > 1)
        {
            lastBlendDiagnosticLogTime = Time.time;
            Debug.Log($"JourneyCalculator: blend of {blendDiagnostics.Count} active journeys - {string.Join(" | ", blendDiagnostics)}");
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

        if (journey.Resolved)
        {
            // CheckJourneyStuckAndMaybeRedirect can now abandon the journey
            // (Resolved = true) mid-call - see JourneyStuckRecovery's
            // stuck-recovery cap and Movement_Concurrency_Plan.md section 4
            // item 5. Mirrors the epsilon-deadband early return above: an
            // abandoned journey must not fall through and compute a
            // direction/weight for this same frame.
            return (Vector3.zero, 0f);
        }

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
    ///
    /// If stuckRecovery reports a full circle of redirect angles already
    /// tried without resolving (see JourneyStuckRecovery's cap), the
    /// journey is abandoned instead: marked Resolved exactly like a normal
    /// in-epsilon resolution, so it drops out of the blend cleanly rather
    /// than the Roomba locking against a wall forever - universal
    /// stuck-abandonment, Movement_Concurrency_Plan.md section 4 item 5. A
    /// fresh collision with this same entity re-opens it as usual.
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
            Vector3? nextTarget = stuckRecovery.GetNextRecoveryTarget(
                journey, currentDirection, entityPos, journey.DestinationDistance, journeyRecoveryStepDegrees);

            journey.StuckTrackingBaseline = roombaPos;
            journey.StuckTimer = 0f;

            if (!nextTarget.HasValue)
            {
                journey.Resolved = true;
                journey.ResolvedSequence = ++resolutionSequenceCounter;
                journey.RedirectTarget = null;
                journey.RecoveryState = null;

                Debug.LogWarning($"JourneyCalculator: journey for entity_id={journey.Entity?.GetOrAssignId()} " +
                                  $"exhausted stuck-recovery (tried a full circle of redirect angles) - abandoning as unresolvable. " +
                                  $"entityPos={entityPos}");
                return;
            }

            journey.RedirectTarget = nextTarget;

            // entityPos is logged here (2026-09-12, diagnostic addition),
            // same rationale as the movement_directive and
            // HandleEntityCollision logs - lets a stuck-recovery step be
            // correlated against exactly which anchor point it was
            // computed from.
            Debug.LogWarning($"JourneyCalculator: journey for entity_id={journey.Entity?.GetOrAssignId()} " +
                              $"stuck for {journeyStuckTimeThreshold:F1}s - stepping recovery angle, new target {journey.RedirectTarget.Value}. " +
                              $"entityPos={entityPos}");
        }
    }

    /// <summary>
    /// Reads the four arrow keys as a planar direction. Returns
    /// Vector3.zero unconditionally while a UI text field (e.g. the
    /// therapist chat input) has keyboard focus - added 2026-09-18
    /// (Pause_Redesign_Implementation_Plan.md section 5.6) because Unity's
    /// new Input System reads raw keyboard state regardless of UI focus, so
    /// arrow keys typed into the chat box were otherwise leaking through as
    /// movement input. Unlike PlayerController's analogous Jump suppression,
    /// there's no toggle here to restore the old behavior - arrow-key input
    /// while typing has no legitimate use, so the check is unconditional.
    /// </summary>
    private Vector3 ReadKeyboardDirection()
    {
        Vector3 moveInput = Vector3.zero;

        if (InputFocusUtility.IsTextFieldFocused() || !wasdAllowed)
        {
            return moveInput;
        }

        if (Keyboard.current.upArrowKey.isPressed) moveInput.z += 1f;
        if (Keyboard.current.downArrowKey.isPressed) moveInput.z -= 1f;
        if (Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
        if (Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;

        return moveInput;
    }
}