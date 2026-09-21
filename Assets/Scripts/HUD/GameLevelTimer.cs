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
/// </summary>
public class GameLevelTimer : MonoBehaviour
{
    [Tooltip("Supplies totalAllowedSeconds - the duration RemainingFraction01 decays from 1.0 to 0.0 over.")]
    [SerializeField] private GameScoreConfig config;

    private float startTime;

    private void Awake()
    {
        startTime = Time.time;

        if (config == null)
        {
            Debug.LogWarning("GameLevelTimer: no GameScoreConfig assigned - RemainingFraction01 will report 1.0 (no time pressure) until one is wired up.");
        }
    }

    /// <summary>
    /// 1.0 at game start, decaying linearly to 0.0 as config.TotalAllowedSeconds
    /// elapses, then holding at 0.0 rather than going negative. This
    /// component only reports the fraction - it has no opinion on what
    /// happens once it reaches zero (that's a future story's job, per Plan
    /// section 3.11's win/loss-consequence scope boundary).
    ///
    /// Falls back to 1.0 (best case, not worst case) if misconfigured -
    /// same "missing data reads as best case" convention DirtProgressReporter
    /// already uses for its own presumedTotal &lt;= 0 edge case - so a setup
    /// mistake here doesn't quietly tank the whole OVERALL score in a way
    /// that's hard to trace back to this component.
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
