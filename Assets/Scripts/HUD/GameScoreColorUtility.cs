using UnityEngine;

/// <summary>
/// Shared score-to-color mapping for GameScoreTable's rows (including
/// OVERALL) - see GameScoreTable_Implementation_Plan.md section 5. Every
/// row's bar length AND bar color come from the same single 0-1 score, so
/// there is exactly one function here, not a per-row special case.
///
/// Uses HSV hue interpolation rather than hand-picked color stops: red sits
/// at hue 0 and green at hue 1/3 (120 degrees of the 360-degree wheel), so
/// mapping score directly onto that short arc - never crossing into
/// blue/violet territory - gives a continuous red -> orange -> yellow ->
/// green sweep from one line of math, with no lookup table and nothing to
/// configure. Deliberately not exposed via GameScoreConfig or any other
/// ScriptableObject - there are no tunable inputs to hold.
/// </summary>
public static class GameScoreColorUtility
{
    /// <summary>
    /// Maps a 0-1 score to a color on the red(0.0)->green(1.0) arc, at full
    /// saturation and value so it stays vivid against the dark HUD
    /// background (matching the vivid, non-desaturated colors already used
    /// elsewhere in this project, e.g. TherapyChatController's
    /// emotionColors palette). Values outside [0, 1] are clamped rather than
    /// wrapping around the rest of the color wheel.
    /// </summary>
    public static Color ScoreToColor(float score01)
    {
        float hue = Mathf.Clamp01(score01) / 3f;
        return Color.HSVToRGB(hue, 1f, 1f);
    }
}
