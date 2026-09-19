using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Paused gate - the single source of truth for whether the Roomba is
/// currently paused. As of 2026-09-18 (Pause_Redesign_Implementation_
/// Plan.md) this supersedes the original Planning.md "Managed Pause"
/// design (five entry triggers, one timed resume condition): there is no
/// more auto-pause of any kind in this design. The only two things that
/// can ever call
/// Pause()/Resume() are an LLM pause_directive (see SessionManager.
/// UpdateState) and the debug key below - nothing settles, nothing times
/// out, nothing gets trapped into a pause on its own.
///
/// Historical note, pending a separate change: JourneyCalculator's own
/// FixedUpdate still checks IsPaused first and skips ALL movement
/// (including an active Journey) while true, exactly as it always has.
/// That's being restructured separately so Paused only gates autonomous
/// Clean/Map exploration - an already-active emotion/LLM-driven Journey
/// will no longer be interruptable by a pause once that lands. Until it
/// does, this file's own behavior (when IsPaused becomes true or false) is
/// already fully correct and independent of that pending change.
/// </summary>
public class PauseController : MonoBehaviour
{
    [Tooltip("Starts the Roomba Paused when the scene loads - a Unity-local decision, not an LLM one (see Pause_Redesign_Implementation_Plan.md section 1.1). Defaults to true so testing already reflects the intended starting behavior; toggle off in the Inspector for faster iteration if you want the Roomba moving immediately.")]
    [SerializeField] private bool startPaused = true;

    [Tooltip("Enable for manual pause/resume testing in the editor and dev builds; disable to hide this override entirely in a built game.")]
    [SerializeField] private bool debugToggleKeyEnabled = true;

    [Tooltip("Debug-only: manually toggles Paused for testing, independent of any LLM directive. Defaults to Escape (changed 2026-09-17, was P then briefly Home) - a physical, non-printable key present on both Mac and PC keyboards without a Fn combo (Home was awkward on Mac laptops), and never something typed into a text field.")]
    [SerializeField] private Key debugToggleKey = Key.Escape;

    public bool IsPaused { get; private set; }

    private void Awake()
    {
        IsPaused = startPaused;
        if (IsPaused)
        {
            Debug.Log("PauseController: starting Paused.");
        }
    }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        Debug.Log("PauseController: Paused.");
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        Debug.Log("PauseController: Resumed.");
    }

    private void Update()
    {
        if (debugToggleKeyEnabled && Keyboard.current != null && Keyboard.current[debugToggleKey].wasPressedThisFrame)
        {
            if (IsPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
    }
}
