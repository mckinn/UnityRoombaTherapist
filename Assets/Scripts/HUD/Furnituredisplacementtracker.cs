using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks furniture displacement for the Damage Done score - see
/// Furniture_Displacement_Implementation_Plan.md (ROOMBA-80) for the full
/// design and the reasoning behind every decision below. Self-contained
/// singleton, same shape as DirtProgressReporter/EntityRegistry:
/// GameScoreTable reads CurrentDestructionSeverity01 directly via
/// Instance, with no Inspector wiring required on GameScoreTable's side.
///
/// Candidate objects are the eligible children of furnitureRoot (Plan
/// 3.1/3.2) - anything with both a non-kinematic Rigidbody and a
/// Collider. This single filter is deliberately the ONLY eligibility
/// test: it already excludes the rugs (confirmed to have neither
/// component) and both landmarks - the rug and the exit door - since
/// landmarks can't be pushed around by design, with no separate
/// tag-based exclusion list to maintain alongside it.
///
/// "Collided" here is the discovery gate, not "has moved" - each
/// eligible candidate gets its own FurnitureCollisionListener added at
/// Start(), so an object pushed indirectly (Roomba hits table, table
/// hits chair) is caught by its own OnCollisionEnter the moment the
/// table touches it, with no dependency anywhere in this class on the
/// Roomba being one of the two colliding bodies (Plan 3.3).
///
/// P1 (first-collision position) is captured the instant a candidate's
/// Collided flag first flips true (Plan 3.5). PLast is refreshed on a
/// repeating timer, not per-collision and not per-frame (Plan 3.6) -
/// deliberate, since motion from a collision continues and settles after
/// the triggering event, and the ticket this implements explicitly does
/// not require per-game-loop-iteration accuracy. Only already-collided
/// candidates are polled; nothing moves without first registering a
/// collision in this physics setup, so polling anything else would never
/// find a change worth catching.
///
/// No noise-floor epsilon and no separate "Displaced" boolean exist
/// anywhere in this class (Plan 3.8) - every collided candidate simply
/// contributes its (possibly tiny) distance to the average below, and
/// CurrentDestructionSeverity01's curve suppresses negligible
/// contributions on its own.
/// </summary>
public class FurnitureDisplacementTracker : MonoBehaviour
{
    private static FurnitureDisplacementTracker instance;
    public static FurnitureDisplacementTracker Instance => instance;

    [Tooltip("Root GameObject whose direct children are the candidate furniture - '03_living_room/Furniture' per the JIRA. Wired explicitly rather than found by path string, same convention as SessionManager.landmarkIdentity.")]
    [SerializeField] private Transform furnitureRoot;

    [Tooltip("How often (seconds) already-collided candidates' positions are re-polled to catch their settled resting spot. Deliberately not every frame and not tied to collision timing - see this class's own header comment.")]
    [SerializeField] private float pollIntervalSeconds = 1f;

    [Tooltip("The 's' in the Hill-function severity curve (d^n / (d^n + s^n)) - the average per-object displacement distance at which destruction severity reads exactly 0.5. No sane universal default exists; this needs a real value measured against the actual living-room scene (how far a shoved chair or table travels before settling). Exposed here specifically so it can be retuned by play-testing if the metric reads too sensitive or too forgiving in either direction - Furniture_Displacement_Implementation_Plan.md section 2.")]
    [SerializeField] private float midpointDistance = 1f;

    [Tooltip("The 'n' in the Hill-function severity curve - controls how sharply the curve transitions around midpointDistance. Must be greater than 1 for small displacements to be genuinely downplayed (at n=1 the curve has nonzero slope at zero, so tiny movements would NOT be suppressed - see the plan doc's discussion of this). 2 is a reasonable starting point; raise it if the transition feels too gradual once you can play-test against a real scene.")]
    [SerializeField] private float steepness = 2f;

    private class TrackedEntry
    {
        public Transform Transform;
        public bool Collided;
        public Vector3 P1;
        public Vector3 PLast;
    }

    private readonly List<TrackedEntry> tracked = new List<TrackedEntry>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void Start()
    {
        if (furnitureRoot == null)
        {
            Debug.LogWarning("FurnitureDisplacementTracker: furnitureRoot not assigned - no furniture will be tracked, Damage Done will read as pristine (1.0) for the whole session.");
            return;
        }

        foreach (Transform child in furnitureRoot)
        {
            if (!IsEligible(child))
            {
                continue;
            }

            tracked.Add(new TrackedEntry { Transform = child, Collided = false });

            FurnitureCollisionListener listener = child.gameObject.AddComponent<FurnitureCollisionListener>();
            listener.Initialize(this);
        }

        Debug.Log($"FurnitureDisplacementTracker: tracking {tracked.Count} eligible candidate(s) under '{furnitureRoot.name}'.");

        InvokeRepeating(nameof(PollSettledPositions), pollIntervalSeconds, pollIntervalSeconds);
    }

    /// <summary>
    /// Eligibility per Furniture_Displacement_Implementation_Plan.md
    /// section 3.2 - a non-kinematic Rigidbody AND a Collider, nothing
    /// else. Deliberately no tag check: this alone already excludes the
    /// rugs (no physics components at all) and both landmarks (immovable
    /// by design, so even a landmark carrying a Collider won't carry a
    /// non-kinematic Rigidbody).
    /// </summary>
    private static bool IsEligible(Transform candidate)
    {
        Rigidbody rb = candidate.GetComponent<Rigidbody>();
        Collider col = candidate.GetComponent<Collider>();
        return rb != null && !rb.isKinematic && col != null;
    }

    /// <summary>
    /// Called by a FurnitureCollisionListener on its own OnCollisionEnter.
    /// Only the FIRST call for a given transform has any effect - P1 is
    /// permanent once set (Plan 3.5). Safe to call repeatedly; later
    /// collisions on an already-collided object are no-ops here - PLast
    /// is maintained by the poll timer instead, not by further collision
    /// events (Plan 3.6).
    /// </summary>
    public void NotifyCollided(Transform candidate)
    {
        TrackedEntry entry = tracked.Find(e => e.Transform == candidate);
        if (entry == null || entry.Collided)
        {
            return;
        }

        entry.Collided = true;
        entry.P1 = candidate.position;
        entry.PLast = candidate.position;
        Debug.Log($"FurnitureDisplacementTracker: '{candidate.name}' collided for the first time at {entry.P1}.");
    }

    private void PollSettledPositions()
    {
        foreach (TrackedEntry entry in tracked)
        {
            if (!entry.Collided || entry.Transform == null)
            {
                continue;
            }

            entry.PLast = entry.Transform.position;
        }
    }

    /// <summary>
    /// Destruction severity per Furniture_Displacement_Implementation_Plan.md
    /// section 2 - 0 (pristine) to 1 (fully destroyed), never quite
    /// reaching 1 since the curve is asymptotic. Averages displacement
    /// over collided candidates only, per the plan's denominator decision
    /// (3.4) - an object nobody has touched contributes to neither the
    /// sum nor the count. Returns 0 outright if nothing has been collided
    /// with yet, matching the "missing data reads as best case"
    /// convention used throughout GameScoreTable.
    /// </summary>
    public float CurrentDestructionSeverity01
    {
        get
        {
            float sumDistance = 0f;
            int collidedCount = 0;

            foreach (TrackedEntry entry in tracked)
            {
                if (!entry.Collided)
                {
                    continue;
                }

                sumDistance += Vector3.Distance(entry.P1, entry.PLast);
                collidedCount++;
            }

            if (collidedCount == 0)
            {
                return 0f;
            }

            float averageDistance = sumDistance / collidedCount;
            float dPowN = Mathf.Pow(averageDistance, steepness);
            float sPowN = Mathf.Pow(midpointDistance, steepness);

            if (dPowN + sPowN <= 0f)
            {
                return 0f;
            }

            return dPowN / (dPowN + sPowN);
        }
    }
}