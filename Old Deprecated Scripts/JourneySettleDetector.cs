using UnityEngine;

/// <summary>
/// Entry trigger 1 of the Managed Pause design (see Planning.md, "Managed
/// Pause") - detects the moment all active Journeys have just resolved and
/// calls PauseController.Pause().
///
/// Called synchronously from JourneyCalculator's own FixedUpdate, at the
/// exact point StableExpressionJourney is evaluated - not independently
/// ticked. This is deliberate, not incidental: an independently-ticked
/// observer polling StableExpressionJourney from the outside would race
/// JourneyCalculator's own Clean/Map fallback (Unity doesn't guarantee
/// ordering between two different components' FixedUpdate calls), and
/// could lag a full tick behind. Calling this synchronously, from inside
/// JourneyCalculator's own dispatch, closes that gap - though not
/// perfectly: the pause-gate check itself only happens at the very top of
/// FixedUpdate, which has already passed by the time this fires mid-
/// function, so Clean/Map can still drive once more in the same tick the
/// transition occurs, with the gate fully engaging the tick after. That's
/// one physics step (~1/50th of a second) - accepted as imperceptible
/// rather than engineered away.
///
/// Deliberately triggers on the null -> non-null TRANSITION itself, not a
/// sustained velocity-below-threshold window, even though Planning.md
/// originally described this as a "velocity/rate-of-change threshold". In
/// this implementation that distinction doesn't end up mattering:
/// PlayerController.Move() is purely kinematic (Rigidbody.MovePosition, no
/// AddForce/momentum), so there's no coasting to wait out - and more
/// fundamentally, waiting for velocity to settle on its own wouldn't work
/// here at all, since Clean/Map picks up the instant a Journey resolves if
/// nothing else claims the frame first. Pausing ON the transition is what
/// CAUSES settling; it isn't something settling produces independently for
/// this to observe afterward.
///
/// No longer checks whether the player is actively driving via keyboard at
/// the moment of transition (an earlier version did, specifically to avoid
/// yanking control away from an engaged player). That guard is no longer
/// needed: JourneyCalculator's pause gate itself now always lets keyboard
/// input through regardless of Paused state, so triggering Pause() here
/// can no longer interrupt anything the player is currently doing - it
/// only prevents autonomous movement from resuming once they let go.
/// </summary>
public class JourneySettleDetector : MonoBehaviour
{
    [SerializeField] private PauseController pauseController;

    private bool wasStable;

    /// <summary>
    /// Call once per FixedUpdate, from JourneyCalculator, with whether
    /// StableExpressionJourney is currently non-null.
    /// </summary>
    public void CheckSettled(bool isStable)
    {
        if (isStable && !wasStable)
        {
            if (pauseController != null)
            {
                pauseController.Pause();
            }
        }

        wasStable = isStable;
    }
}
