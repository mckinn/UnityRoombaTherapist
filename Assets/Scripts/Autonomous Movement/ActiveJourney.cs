using UnityEngine;

/// <summary>
/// One tracked entity instance's active pursuit of its emotional target
/// distance (D). Lives in JourneyCalculator's active set only while
/// unresolved - once the Roomba's distance to this entity lands within the
/// epsilon deadband around D, it's marked Resolved and excluded from the
/// weighted blend, until a fresh collision with this same entity re-opens
/// it.
///
/// LastKnownPosition is the contact point from the triggering collision,
/// not the entity's Transform.position - consistent with the "no eyes,
/// collision-only" model: the Roomba only ever knows where it touched
/// something, not that object's abstract origin.
///
/// This same structure is the natural home for the future proximity-memory
/// feature ("I remember there's a couch over there") once that's in scope -
/// position is already being tracked here for a narrower, current reason
/// (computing distance right now). Extending its lifetime beyond
/// resolution, later, is additive - not a second implementation.
/// </summary>
public class ActiveJourney
{
    public EntityIdentity Entity;
    public string EntityType;
    public string Emotion;
    public float Strength;              // V, from EntitySensitivities at the time of the triggering collision
    public float DestinationDistance;   // D = L + V * (H - L)
    public Vector3 LastKnownPosition;   // contact point, refreshed on each new collision with this entity
    public bool Resolved;               // set by the per-frame distance check Step 2 will add
    public int ResolvedSequence;        // stamped when Resolved flips true - used to pick the most recently settled emotion when several are resolved simultaneously (Step 6, stable-state Behavior selection)

    // Stuck recovery (see JourneyCalculator's per-journey stuck detection
    // and JourneyStuckRecovery). All four reset on a fresh collision with
    // this entity, whether that's this journey's creation or a re-trigger
    // of an existing one - a new collision restarts the pursuit, including
    // any in-progress recovery episode.
    public Vector3? RedirectTarget;     // set once stuck-recovery has chosen an alternate point on the D-radius circle to approach from; null = pure radial pursuit (the default, unmodified behavior)
    public Vector3 StuckTrackingBaseline; // position snapshot used to detect lack of net progress toward the current target (radial or redirect)
    public float StuckTimer;            // seconds of no net progress against StuckTrackingBaseline
    public object RecoveryState;        // opaque to ActiveJourney - owned and interpreted only by whatever recovery strategy is currently in use (JourneyStuckRecovery today)
}