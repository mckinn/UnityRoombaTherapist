using UnityEngine;

/// <summary>
/// Drives Clean/Map movement: on each tick it's consulted, marks the
/// Roomba's current cell Visited and asks the coverage planner for a fresh
/// direction toward the nearest reachable Unknown cell. Exposes only a
/// direction for JourneyCalculator to drive with via PlayerController.Move()
/// - this class never calls Move() itself, preserving JourneyCalculator's
/// single-authority dispatch (see its own header comment). Lowest priority
/// in that dispatch: only consulted when nothing else (Journey, keyboard,
/// Behavior pattern motion) wants to move the Roomba this frame.
///
/// BFS target *selection* is still stateless between ticks - no target is
/// cached purely for continuity's sake, and every consultation recomputes
/// fresh from wherever the Roomba currently is (see Planning.md, "Coverage
/// Algorithm"). What this class DOES now track, added after real-world
/// testing surfaced a gap: how long the Roomba has been pursuing the same
/// target without making progress, purely to detect "stuck" (see
/// CheckStuckAndMaybeBlock). That's bookkeeping about outcomes, not a
/// cached plan - it doesn't change what GetNextStep is asked for, only
/// what happens when the answer turns out to be unreachable in practice.
///
/// MarkVisited is idempotent (CoverageGrid never downgrades an
/// already-Visited or Blocked cell), so calling it unconditionally every
/// tick this is consulted is safe and needs no arrival-distance check of
/// its own.
///
/// Blocked-cell marking on collision is step 8 (Journey-to-mapping
/// integration), not this step - nothing currently tells the planner
/// "furniture is here" from a real collision. Until step 8 lands, the
/// Roomba could re-attempt a path through a colliding obstacle more than
/// once (the stuck-detector below only catches targets that are
/// unreachable without ever resolving a Journey at all, e.g. untracked
/// geometry with no EntityIdentity - a real collision still correctly
/// hands off to Journey pursuit first, per JourneyCalculator's priority
/// order, before Clean/Map would ever get a chance to notice anything).
/// </summary>
public class CleanMapController : MonoBehaviour
{
    [Tooltip("Supplies the CoverageGrid this planner operates on.")]
    [SerializeField] private ArenaCoverageController arenaCoverageController;

    [Header("Stuck Detection")]
    [Tooltip("If the Roomba makes less than Stuck Progress Threshold of net movement for this many seconds while pursuing the same target cell, that cell is marked Blocked and the planner replans immediately. Guards against obstacles the Floor-tag check alone can't catch - e.g. a cell whose exact center still registers as valid floor even though most of the cell is physically obstructed by untracked geometry.")]
    [SerializeField] private float stuckTimeThreshold = 1.5f;

    [Tooltip("Minimum net distance (world units) the Roomba must move, relative to where stuck-tracking started, to NOT be considered stuck.")]
    [SerializeField] private float stuckProgressThreshold = 0.05f;

    private CoveragePlanner planner;
    private bool loggedCompletion = false;

    private Vector2Int? stuckTrackingTargetCell;
    private Vector3 stuckTrackingBaselinePosition;
    private float stuckTimer;

    private void Start()
    {
        if (arenaCoverageController == null || arenaCoverageController.Grid == null)
        {
            Debug.LogError("CleanMapController: no CoverageGrid available (ArenaCoverageController missing or its grid failed to build) - Clean/Map will never produce a direction.");
            return;
        }

        planner = new CoveragePlanner(arenaCoverageController.Grid);
    }

    /// <summary>
    /// Returns a world-space direction toward the nearest reachable Unknown
    /// cell, or null if coverage is complete (or the planner isn't ready).
    /// Marks the Roomba's current cell Visited as a side effect of being
    /// called - only call this when Clean/Map is actually meant to be
    /// driving this tick, not just to "check" without committing to it.
    /// </summary>
    public Vector3? GetSteeringDirection()
    {
        if (planner == null)
        {
            return null;
        }

        CoverageGrid grid = arenaCoverageController.Grid;
        Vector2Int currentCell = grid.WorldToCell(transform.position);

        grid.MarkVisited(currentCell);

        Vector2Int? nextStep = planner.GetNextStep(currentCell);

        if (nextStep == null)
        {
            ResetStuckTracking();
            LogCompletionOnce();
            return null;
        }

        if (CheckStuckAndMaybeBlock(nextStep.Value))
        {
            // Just gave up on that target and blocked it - replan
            // immediately in the same tick rather than steering toward a
            // cell we already know is unreachable for one more frame.
            nextStep = planner.GetNextStep(currentCell);
            if (nextStep == null)
            {
                ResetStuckTracking();
                LogCompletionOnce();
                return null;
            }
        }

        Vector3 targetWorldCenter = grid.CellToWorldCenter(nextStep.Value, transform.position.y);
        Vector3 direction = targetWorldCenter - transform.position;
        direction.y = 0f;

        return direction;
    }

    /// <summary>
    /// Tracks how long the Roomba has gone without meaningful net movement
    /// while pursuing the SAME target cell. Returns true (and marks the
    /// target Blocked) once stuck long enough - the caller is expected to
    /// replan immediately when this happens. Resets automatically on real
    /// progress, or whenever the target cell itself changes (a fresh
    /// target starts a fresh clock, rather than inheriting a stall that
    /// happened while pursuing something else).
    /// </summary>
    private bool CheckStuckAndMaybeBlock(Vector2Int targetCell)
    {
        if (stuckTrackingTargetCell == null || targetCell != stuckTrackingTargetCell.Value)
        {
            stuckTrackingTargetCell = targetCell;
            stuckTrackingBaselinePosition = transform.position;
            stuckTimer = 0f;
            return false;
        }

        stuckTimer += Time.fixedDeltaTime;

        float netProgress = Vector3.Distance(transform.position, stuckTrackingBaselinePosition);
        if (netProgress >= stuckProgressThreshold)
        {
            stuckTrackingBaselinePosition = transform.position;
            stuckTimer = 0f;
            return false;
        }

        if (stuckTimer >= stuckTimeThreshold)
        {
            Debug.LogWarning($"CleanMapController: stuck pursuing cell {targetCell} for {stuckTimer:F1}s with no net progress - marking it Blocked and replanning.");
            arenaCoverageController.Grid.MarkBlocked(targetCell);
            ResetStuckTracking();
            return true;
        }

        return false;
    }

    private void ResetStuckTracking()
    {
        stuckTrackingTargetCell = null;
        stuckTimer = 0f;
    }

    private void LogCompletionOnce()
    {
        if (!loggedCompletion)
        {
            loggedCompletion = true;
            Debug.Log("CleanMapController: coverage complete - no reachable Unknown cell remains.");
        }
    }
}
