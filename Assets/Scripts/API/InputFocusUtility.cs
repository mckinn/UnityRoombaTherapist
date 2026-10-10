using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shared helper for "is the player currently typing into a UI text field,
/// as opposed to controlling the Roomba" - used to suppress keyboard input
/// that would otherwise leak through, since Unity's new Input System reads
/// raw keyboard state regardless of UI focus (unlike the legacy Input
/// Manager).
///
/// Promoted out of PlayerController's own private IsTextFieldFocused
/// (added 2026-09-17 for the Space/Jump-while-typing fix) into this shared
/// static utility on 2026-09-18, when JourneyCalculator's arrow-key reading
/// needed the exact same check (Pause_Redesign_Implementation_Plan.md
/// section 5.6) - two independently-maintained copies of the same
/// EventSystem lookup seemed more likely to drift out of sync later than
/// one small shared file was worth avoiding now.
/// </summary>
public static class InputFocusUtility
{
    /// <summary>
    /// True if the currently EventSystem-selected object is a text input
    /// field that's actively focused for editing - checked generically via
    /// TMP_InputField rather than a direct reference to any specific input
    /// field, so this stays correct if more text fields are ever added to
    /// the UI.
    /// </summary>
    public static bool IsTextFieldFocused()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null)
        {
            return false;
        }

        TMP_InputField inputField = selected.GetComponent<TMP_InputField>();
        return inputField != null && inputField.isFocused;
    }
}
