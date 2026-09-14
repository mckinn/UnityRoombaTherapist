using UnityEngine;

/// <summary>
/// Per-emotion colors for the simplified color-based expression prototype.
/// Same pattern as EmotionMovementConfig/BehaviorExpressionConfig: live-
/// tunable in the Inspector, including while the game is running.
/// </summary>
[CreateAssetMenu(fileName = "EmotionColorConfig", menuName = "RoombaTherapist/Emotion Color Config")]
public class EmotionColorConfig : ScriptableObject
{
    [System.Serializable]
    public class EmotionColorEntry
    {
        [Tooltip("Must match the ROOMBA_STATE_TOOL emotion enum exactly: fear, joy, disgust, curiosity, ambivalence.")]
        public string emotion;
        public Color color = Color.white;
    }

    [Tooltip("Live-tunable in the Inspector, including while the game is running. Starting values are arbitrary placeholders - retune by eye.")]
    [SerializeField]
    private EmotionColorEntry[] colors = new EmotionColorEntry[]
    {
        new EmotionColorEntry { emotion = "fear",        color = new Color(0.55f, 0.10f, 0.75f) },
        new EmotionColorEntry { emotion = "joy",         color = new Color(1.00f, 0.85f, 0.10f) },
        new EmotionColorEntry { emotion = "disgust",     color = new Color(0.35f, 0.65f, 0.20f) },
        new EmotionColorEntry { emotion = "curiosity",   color = new Color(0.15f, 0.60f, 1.00f) },
        new EmotionColorEntry { emotion = "ambivalence", color = new Color(0.60f, 0.60f, 0.60f) },
    };

    [Tooltip("Color shown before any collision has ever happened.")]
    [SerializeField] private Color defaultColor = Color.white;
    public Color DefaultColor => defaultColor;

    [Tooltip("Seconds for a smooth transition between colors.")]
    [SerializeField] private float transitionDuration = 0.5f;
    public float TransitionDuration => transitionDuration;

    /// <summary>
    /// Returns the configured color for the given emotion, or DefaultColor
    /// (with a warning) if it isn't configured. Shouldn't happen given the
    /// enum-constrained vocabulary upstream, but this reads live gameplay
    /// data, so it's handled rather than assumed - same pattern as
    /// EmotionMovementConfig.GetBounds.
    /// </summary>
    public Color GetColor(string emotion)
    {
        foreach (EmotionColorEntry entry in colors)
        {
            if (entry.emotion == emotion)
            {
                return entry.color;
            }
        }
        Debug.LogWarning($"EmotionColorConfig: no color configured for emotion '{emotion}'.");
        return defaultColor;
    }

    [Header("Pleasure -> Saturation")]
    [Tooltip("Saturation at the most negative (unhappy) end of Pleasure - 0 = fully desaturated/gray, 1 = fully vivid.")]
    [SerializeField, Range(0f, 1f)] private float minSaturation = 0.25f;

    [Tooltip("Saturation at the most positive (happy) end of Pleasure.")]
    [SerializeField, Range(0f, 1f)] private float maxSaturation = 1.0f;

    [Tooltip("If true (assumed default): high Pleasure => vivid/saturated, low/negative Pleasure => dull/gray. Flip if it plays backwards from what you'd expect.")]
    [SerializeField] private bool highPleasureMeansVivid = true;

    /// <summary>
    /// Applies a Pleasure-driven saturation to a base color, preserving its
    /// hue and value. baseColor is expected to be one of the configured
    /// per-emotion colors (or DefaultColor) - this doesn't change WHICH
    /// color is showing, only how vivid it is.
    /// </summary>
    public Color ApplySaturation(Color baseColor, float pleasure)
    {
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);

        if (s < 0.0001f)
        {
            // baseColor is achromatic (white/gray/black) - hue is
            // mathematically undefined for it, so there's nothing
            // meaningful to re-saturate. Reconstructing anyway would use
            // whatever arbitrary hue RGBToHSV happened to return for
            // achromatic input (0, i.e. red, in Unity) and tint toward
            // that instead of staying neutral - return unchanged instead.
            return baseColor;
        }

        float normalized = Mathf.Clamp01((pleasure + 1f) / 2f); // -1..1 -> 0..1, 1 = most pleasant
        if (!highPleasureMeansVivid)
        {
            normalized = 1f - normalized;
        }
        float targetSaturation = Mathf.Lerp(minSaturation, maxSaturation, normalized);

        return Color.HSVToRGB(h, targetSaturation, v);
    }

    [Header("Dominance -> Indicator Size")]
    [Tooltip("Indicator local scale at the most negative (submissive/timid) end of Dominance.")]
    [SerializeField] private float minSize = 0.2f;

    [Tooltip("Indicator local scale at the most positive (assertive/dominant) end of Dominance.")]
    [SerializeField] private float maxSize = 0.6f;

    [Tooltip("If true (assumed default): high Dominance => large indicator, low/negative => small. Matches the confirmed direction from the earlier Behavior-pattern work.")]
    [SerializeField] private bool highDominanceMeansLarge = true;

    /// <summary>
    /// Maps Dominance (-1..1) to the indicator's uniform local scale.
    /// Deliberately its own min/max range, historically separate from the
    /// since-removed BehaviorExpressionConfig (which used the same
    /// Dominance axis for a different purpose - oscillation-pattern size,
    /// not indicator scale; removed 2026-09-11 along with BehaviorController
    /// and BehaviorExpressionCalculator - see Movement_Concurrency_Plan.md
    /// section 4 item 4) - same underlying technique, intentionally not the
    /// same tunable numbers, since this color-based approach was always
    /// meant to stand fully independent of the motion-pattern approach.
    /// </summary>
    public float ComputeIndicatorSize(float dominance)
    {
        float normalized = Mathf.Clamp01((dominance + 1f) / 2f); // -1..1 -> 0..1, 1 = most dominant
        if (!highDominanceMeansLarge)
        {
            normalized = 1f - normalized;
        }
        return Mathf.Lerp(minSize, maxSize, normalized);
    }
}