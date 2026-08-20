using UnityEngine;

/// <summary>
/// The unified resume condition for the Paused state (see Planning.md,
/// "Managed Pause") - while Paused, counts down a quiet-period timer; any
/// dialog-exchange activity resets it; expiry calls
/// PauseController.Resume(). Entirely inert while not paused - the timer
/// only runs, and only matters, during an active pause.
///
/// Deliberately does NOT self-subscribe to any broad "a response arrived"
/// event (e.g. SessionManager.OnStateUpdated), even though that would be
/// simpler to wire. That event fires for every LLM-backed response -
/// arena events and dirt-progress reports alike, not just therapy dialog -
/// and resetting on all of them would extend a pause because of, say, an
/// automatic dirt-progress report landing while the player happens to be
/// manually driving through the pause (keyboard always works while paused
/// - see JourneyCalculator). "Every dialog exchange restarts the timer"
/// means therapist/Roomba dialog specifically, not every Orchestrator
/// response - so TherapyChatController is the sole caller of ResetTimer(),
/// at exactly the moments that are genuinely dialog: typing, message sent,
/// and response received.
/// </summary>
public class ResumeTimer : MonoBehaviour
{
    [SerializeField] private PauseController pauseController;

    [Tooltip("Seconds of no dialog activity, while paused, before autonomous movement resumes.")]
    [SerializeField] private float quietPeriodSeconds = 3f;

    private float timer;

    /// <summary>
    /// Call whenever something counts as dialog-exchange activity (typing,
    /// message sent, response received) - see TherapyChatController, the
    /// only intended caller.
    /// </summary>
    public void ResetTimer()
    {
        timer = 0f;
    }

    private void Update()
    {
        if (pauseController == null || !pauseController.IsPaused)
        {
            // Inert while not paused - don't accumulate time that would
            // otherwise cause an immediate resume the instant a future
            // pause begins.
            timer = 0f;
            return;
        }

        timer += Time.deltaTime;

        if (timer >= quietPeriodSeconds)
        {
            pauseController.Resume();
            timer = 0f;
        }
    }
}
