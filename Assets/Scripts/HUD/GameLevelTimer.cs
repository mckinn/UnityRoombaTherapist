using System;
using UnityEngine;

/// <summary>
/// Owns the elapsed-time clock for GameScoreTable's Time Remaining row (see
/// GameScoreTable_Implementation_Plan.md section 4.2). Single
/// responsibility: knows nothing about scoring, weights, or the HUD - same
/// single-authority-per-concern pattern used throughout this codebase (e.g.
/// CollisionController owning all collision events).
///
/// Starts automatically at game initialization (Awake()) rather than via
/// any explicit "level start" call - Plan section 3.12 - since no
/// level-lifecycle concept exists anywhere in this project yet. If a future
/// story introduces one, this is the component that would gain a
/// BeginLevel()-style reset.
///
/// Pause-independence falls out for free, not from any code here:
/// Time.time keeps advancing regardless of PauseController.IsPaused, since
/// this project's Managed Pause system never touches Time.timeScale - it
/// only gates whether JourneyCalculator drives autonomous Clean/Map
/// movement (see Pause_Redesign_Implementation_Plan.md). A plain
/// Time.time-based elapsed calculation is therefore already exactly what
/// "pause doesn't stop the clock" asks for.
///
/// Extended 2026-09-25 (Freeze_And_Stop_Implementation_Plan.md) with
/// OnTimeExpired - one of the two mechanical Freeze triggers, alongside
/// EgressExitDetector.OnExitReached. This component still only reports what
/// happened, the same single-responsibility stance as before: it doesn't
/// know FreezeController exists, doesn't gate or stop anything itself, and
/// keeps counting down exactly the same way regardless of whether anything
/// is listening.
/// </summary>
public class GameLevelTimer : MonoBehaviour
{
    [Tooltip("Supplies totalAllowedSeconds - the duration RemainingFraction01 decays from 1.0 to 0.0 over.")]
    [SerializeField] private GameScoreConfig config;

    /// <summary>Fires once, the first frame RemainingFraction01 reaches 0 - same one-shot pattern EgressExitDetector already uses for OnExitReached.</summary>
    public event Action OnTimeExpired;

    private float startTime;
    private bool timeExpiredFired;

    private void Awake()
    {
        startTime = Time.time;

        if (config == null)
        {
            Debug.LogWarning("GameLevelTimer: no GameScoreConfig assigned - RemainingFraction01 will report 1.0 (no time pressure) until one is wired up.");
        }
    }

    private void Update()
    {
        if (!timeExpiredFired && RemainingFraction01 <= 0f)
        {
            timeExpiredFired = true;
            Debug.Log("GameLevelTimer: time expired.");
            OnTimeExpired?.Invoke();
        }
    }

    /// <summary>
    /// 1.0 at game start, decaying linearly to 0.0 as config.TotalAllowedSeconds
    /// elapses, then holding at 0.0 rather than going negative. This
    /// component only reports the fraction (and, since 2026-09-25, the
    /// one-shot OnTimeExpired event above) - a misconfigured config falling
    /// back to 1.0 below simply means OnTimeExpired never fires, the same
    /// "missing data reads as best case" convention DirtProgressReporter
    /// already uses for its own presumedTotal &lt;= 0 edge case - so a setup
    /// mistake here doesn't quietly tank the whole OVERALL score, or force a
    /// Freeze that was never really earned, in a way that's hard to trace
    /// back to this component.
    /// </summary>
    public float RemainingFraction01
    {
        get
        {
            if (config == null || config.TotalAllowedSeconds <= 0f)
            {
                return 1f;
            }

            float elapsed = Time.time - startTime;
            return Mathf.Clamp01(1f - (elapsed / config.TotalAllowedSeconds));
        }
    }
}
