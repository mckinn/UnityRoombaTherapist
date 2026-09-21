using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks aggregate dirt-cleanup progress and reports it to the
/// Orchestrator as a distinct event_type ("dirt_progress") on the existing
/// /arena/event endpoint - a text-based percentage summary, not a
/// per-instance EmotionState (see Planning.md, "Dirt Cleanup and
/// Reporting"). Per-instance emotional reaction to individual dirt contact
/// is a separate, not-yet-scheduled piece of work - this class only
/// handles the aggregate progress side.
///
/// Reporting cadence: shrinking-threshold pattern. After each report, the
/// next trigger point is roughly half of what's been collected since the
/// last report - naturally decaying frequency rather than a flat interval.
/// A minimum gap (minimumReportGap) prevents reporting on every single
/// piece as remaining count gets small. No separate time-based rate limit
/// is applied here (unlike CollisionController's arena-event limiter) -
/// this threshold mechanism already serves that purpose for this event
/// type by design, so a second, independent limiter would be redundant.
///
/// Reachability correction: presumedTotal starts at however many patches
/// DirtScatterer actually placed (all assumed reachable). The one
/// correction happens exactly once, on CleanMapController.OnCoverageComplete
/// - anything never collected by that point is provably unreachable and
/// gets subtracted from presumedTotal in a single adjustment, alongside an
/// unconditional final report (independent of the threshold math, so a
/// real completion is never silently skipped even if the shrinking
/// threshold had already dropped below the minimum gap).
///
/// Payload is deliberately minimal: percent_complete and is_complete only -
/// no raw collected/remaining counts. Considered and dropped as unnecessary
/// detail for now (see project history); easy to add back later without a
/// breaking contract change if a future phrasing need (e.g.
/// small-remaining-count-specific narration) actually calls for it.
/// </summary>
public class DirtProgressReporter : MonoBehaviour
{
    private static DirtProgressReporter instance;
    public static DirtProgressReporter Instance => instance;

    [Tooltip("Supplies the initial dirt count once scattering completes.")]
    [SerializeField] private DirtScatterer dirtScatterer;

    [Tooltip("Used to detect coverage-complete for the one-time reachability correction and unconditional final report.")]
    [SerializeField] private CleanMapController cleanMapController;

    [Tooltip("Displays the LLM's response dialog/sensitivity changes, same as CollisionController does for collision events.")]
    [SerializeField] private TherapyChatController chatController;

    [Tooltip("Minimum number of newly-collected pieces required before a threshold report can fire, even if the halving math would suggest fewer.")]
    [SerializeField] private int minimumReportGap = 1;

    private int presumedTotal;
    private int collectedCount;
    private int collectedSinceLastReport;
    private int nextReportThreshold;
    private bool finalReportSent;

    /// <summary>
    /// Current dirt-cleanup fraction (0-1), for GameScoreTable's Dirt
    /// Removed row (see GameScoreTable_Implementation_Plan.md section 4.1) -
    /// added 2026-09-21 since nothing previously exposed this outside the
    /// threshold-report mechanism above. The presumedTotal &lt;= 0 fallback
    /// deliberately mirrors SendProgressReportAsync's own convention just
    /// below (percentComplete = 100f in that same case), so this doesn't
    /// introduce a second, inconsistent answer for the same edge case.
    /// </summary>
    public float PercentCollected01 =>
        presumedTotal > 0 ? Mathf.Clamp01((float)collectedCount / presumedTotal) : 1f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        // Subscribed here, not Start() - DirtScatterer fires OnScatterComplete
        // from its own Start(), and Awake-before-Start is a real Unity
        // guarantee (Awake-before-another-component's-Awake is not). This
        // guarantees the subscription is active before the event can ever
        // fire, regardless of component/GameObject ordering.
        if (dirtScatterer != null)
        {
            dirtScatterer.OnScatterComplete += HandleScatterComplete;
        }

        if (cleanMapController != null)
        {
            cleanMapController.OnCoverageComplete += HandleCoverageComplete;
        }
    }

    private void OnDestroy()
    {
        if (dirtScatterer != null)
        {
            dirtScatterer.OnScatterComplete -= HandleScatterComplete;
        }
        if (cleanMapController != null)
        {
            cleanMapController.OnCoverageComplete -= HandleCoverageComplete;
        }
    }

    /// <summary>
    /// Fires every time DirtScatterer completes a scatter pass - including
    /// a hypothetical future re-scatter for a fresh run, not just the
    /// initial one. Resets every counter to match, so a re-scatter is
    /// handled correctly for free rather than needing its own reset logic.
    /// </summary>
    private void HandleScatterComplete(int placedCount)
    {
        presumedTotal = placedCount;
        collectedCount = 0;
        collectedSinceLastReport = 0;
        finalReportSent = false;

        if (presumedTotal <= 0)
        {
            Debug.LogWarning("DirtProgressReporter: DirtScatterer reported 0 patches placed - progress reporting will be a no-op until the next scatter.");
        }

        RecomputeNextThreshold();
    }

    /// <summary>
    /// Called by DirtController when a piece of dirt is actually removed.
    /// </summary>
    public void ReportCollected()
    {

        if (presumedTotal <= 0 || finalReportSent)
        {
            return;
        }

        collectedCount++;
        collectedSinceLastReport++;
        Debug.Log($"Dirt progress: collected: {collectedCount} since last count {collectedSinceLastReport}, threshold {nextReportThreshold}");

        if (collectedSinceLastReport >= nextReportThreshold)
        {
            collectedSinceLastReport = 0;
            RecomputeNextThreshold();
            _ = SendProgressReportAsync(isComplete: false);
        }
    }

    private void HandleCoverageComplete()
    {
        if (finalReportSent || presumedTotal <= 0)
        {
            return;
        }

        // Reachability correction: anything never collected by the time
        // coverage is exhausted was never reachable - the presumed total
        // was optimistic, this is where it gets corrected, once.
        presumedTotal = collectedCount;
        finalReportSent = true;

        // Reads cleanMapController's own IsCoverageComplete rather than a
        // hardcoded true - this method only ever runs because that flag
        // just became true, but reading it directly makes it one source of
        // truth instead of two things that happen to agree by
        // construction (see Planning.md, "Managed Pause", on nothing_to_do
        // sharing this same underlying fact).
        _ = SendProgressReportAsync(isComplete: cleanMapController.IsCoverageComplete);
    }

    private void RecomputeNextThreshold()
    {
        int remaining = Mathf.Max(0, presumedTotal - collectedCount);
        nextReportThreshold = Mathf.Max(minimumReportGap, remaining / 2);
    }

    private async Awaitable SendProgressReportAsync(bool isComplete)
    {
        if (SessionManager.Instance == null)
        {
            Debug.LogWarning("DirtProgressReporter: no SessionManager - cannot send progress report.");
            return;
        }

        // Reuses PercentCollected01 rather than recomputing the same ratio
        // here - added 2026-09-21 alongside that property so this method's
        // percentComplete and GameScoreTable's Dirt Removed row are
        // provably reading the same number, not two independently
        // maintained copies of the same formula.
        float percentComplete = PercentCollected01 * 100f;

        Dictionary<string, EntitySensitivity> beforeSensitivities =
            chatController != null ? chatController.SnapshotSensitivities() : null;

        Debug.Log($"DirtProgressReporter: {percentComplete}, {isComplete}");
        OrchestratorResponse response = await SessionManager.Instance.SendDirtProgress(percentComplete, isComplete);

        if (response == null || chatController == null)
        {
            return;
        }

        chatController.DisplayLeftMessage(response.dialog);

        // if (beforeSensitivities != null)
        // {
        //     chatController.DisplaySensitivityChanges(beforeSensitivities, response.entity_sensitivities);
        // }
    }
}
