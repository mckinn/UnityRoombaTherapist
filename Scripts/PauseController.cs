using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Paused gate - see Planning.md, "Managed Pause", for the full
/// unified design (five entry triggers, one resume condition). This is
/// sub-phase 1 of that design: the gate mechanism itself, proven in
/// isolation with a single debug-only manual trigger standing in for all
/// five real entry triggers, which come in later sub-phases.
///
/// JourneyCalculator checks IsPaused as the very first thing in its own
/// FixedUpdate, before any other dispatch logic runs. This is what makes
/// Paused a true gate rather than just another movement-priority tier -
/// unlike Clean/Map, which sits at the BOTTOM of the dispatch and yields
/// to everything else, Paused sits ABOVE everything, including an active
/// Journey, and has to be able to interrupt one that's already running.
///
/// Pause()/Resume() are the real entry points real triggers will call in
/// later sub-phases (settle-detection, nothing_to_do, dialog activity,
/// arena-entry, Orchestrator should_pause). The debug key exists only to
/// exercise that same API surface for testing before any real trigger
/// exists - it calls the exact same methods a real trigger eventually
/// will, not a separate placeholder path to be replaced later.
/// </summary>
public class PauseController : MonoBehaviour
{
    [Tooltip("Starts the Roomba Paused when the scene loads - a temporary stand-in for the real 'entering the Arena' entry trigger (not yet built, see Planning.md). Defaults to true so testing already reflects the intended starting behavior; toggle off in the Inspector for faster iteration if you want the Roomba moving immediately.")]
    [SerializeField] private bool startPaused = true;

    [Tooltip("Debug-only: toggles Paused on/off for testing, before any real entry trigger exists. Not intended to remain active in normal play once real triggers are wired up in later sub-phases.")]
    [SerializeField] private Key debugToggleKey = Key.P;

    public bool IsPaused { get; private set; }

    private void Awake()
    {
        IsPaused = startPaused;
        if (IsPaused)
        {
            Debug.Log("PauseController: starting Paused (stand-in for the not-yet-built arena-entry trigger).");
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
        if (Keyboard.current != null && Keyboard.current[debugToggleKey].wasPressedThisFrame)
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
