using UnityEngine;

/// <summary>
/// The Freeze latch - see Freeze_And_Stop_Implementation_Plan.md. Deliberately
/// a separate class from PauseController, not a variant or extension of it:
/// Pause is fully reversible and explicitly does not stop the level timer or
/// GameScoreTable (Pause_Redesign_Implementation_Plan.md - an active Journey
/// isn't even interruptable by it any more). Freeze is the opposite on both
/// counts - it stops all Roomba movement (gates JourneyCalculator) and holds
/// GameScoreTable's display - and it is a one-way latch: there is
/// deliberately no Unfreeze() here. The only way out of a Freeze is a Stop
/// (a full scene reload), which makes the question moot rather than
/// reversing it.
///
/// Three things can trigger a Freeze, all funneled through the single
/// Freeze() call below so IsFrozen only ever has one code path that sets it:
/// gameLevelTimer's OnTimeExpired and egressExitDetector's OnExitReached are
/// both subscribed to directly here, so neither of those components needs to
/// know Freeze exists - they just report what happened, the same generic
/// role EgressExitDetector was already designed for. The third trigger, the
/// Therapist-recommended/LLM-decided "freeze" pause_directive, is NOT wired
/// here - SessionManager.UpdateState already owns dispatching every other
/// LLM directive (Pause/Resume, movement_directive), so its new "freeze"
/// case calls Freeze() directly rather than this class reaching out to
/// SessionManager.
/// </summary>
public class FreezeController : MonoBehaviour
{
    [Tooltip("Fires OnTimeExpired once when the level timer runs out - one of the two mechanical Freeze triggers. Leave unassigned to disable this trigger.")]
    [SerializeField] private GameLevelTimer gameLevelTimer;

    [Tooltip("Fires OnExitReached once when the Roomba passes through the unlocked egress door - the other mechanical Freeze trigger. Leave unassigned to disable this trigger.")]
    [SerializeField] private EgressExitDetector egressExitDetector;

    public bool IsFrozen { get; private set; }

    private void OnEnable()
    {
        if (gameLevelTimer != null)
        {
            gameLevelTimer.OnTimeExpired += Freeze;
        }
        if (egressExitDetector != null)
        {
            egressExitDetector.OnExitReached += Freeze;
        }
    }

    private void OnDisable()
    {
        if (gameLevelTimer != null)
        {
            gameLevelTimer.OnTimeExpired -= Freeze;
        }
        if (egressExitDetector != null)
        {
            egressExitDetector.OnExitReached -= Freeze;
        }
    }

    /// <summary>
    /// One-way: no-ops (silently) if already frozen, so every trigger path -
    /// the two events above, or SessionManager's "freeze" case - can call
    /// this without its own guard. There is deliberately no Unfreeze(); see
    /// this class's own doc comment for why.
    /// </summary>
    public void Freeze()
    {
        if (IsFrozen)
        {
            return;
        }

        IsFrozen = true;
        Debug.Log("FreezeController: Frozen - Roomba movement stops and GameScoreTable holds, per each row's own freeze-behavior config.");
    }
}
