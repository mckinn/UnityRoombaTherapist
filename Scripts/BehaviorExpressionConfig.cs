using UnityEngine;

/// <summary>
/// Behavior-expression tuning constants: how the Roomba's baseline PAD state
/// scales the visible size/speed of its Behavior patterns (Step 6 builds the
/// actual patterns; this step only establishes the numbers feeding them).
///
/// Deliberately a separate asset from EmotionMovementConfig. Journey
/// movement (Arousal -> Closing Speed, per-emotion L/H) and Behavior
/// expression (Pleasure -> Pattern Speed, Dominance -> Pattern Size) are
/// different concerns even though both consume PAD.
///
/// PAD-only, not entity-specific, per design: the baseline emotional state
/// governs HOW a Behavior pattern is expressed (how big, how fast)
/// regardless of which entity triggered it. "Loudest wins" (Step 6, reusing
/// JourneyCalculator's weight_i) decides WHICH pattern displays; this
/// decides how big/fast whichever one is currently showing.
/// </summary>
[CreateAssetMenu(fileName = "BehaviorExpressionConfig", menuName = "RoombaTherapist/Behavior Expression Config")]
public class BehaviorExpressionConfig : ScriptableObject
{
    [Header("Pleasure -> Pattern Speed")]
    [Tooltip("Pattern playback speed at the most positive (happy) end of Pleasure.")]
    [SerializeField] private float minPatternSpeed = 0.5f;

    [Tooltip("Pattern playback speed at the most negative (anxious) end of Pleasure.")]
    [SerializeField] private float maxPatternSpeed = 2.0f;

    /// <summary>
    /// Maps Pleasure (-1..1) to a pattern speed. Confirmed direction: slow
    /// for happy (high Pleasure), fast for anxious (low/negative Pleasure) -
    /// the inverse of the naive "high input -> high output" instinct, so
    /// this deliberately lerps in the reversed order.
    /// </summary>
    public float ComputePatternSpeed(float pleasure)
    {
        float normalized = Mathf.Clamp01((pleasure + 1f) / 2f); // -1..1 -> 0..1, 1 = most pleasant
        return Mathf.Lerp(maxPatternSpeed, minPatternSpeed, normalized); // inverted on purpose
    }

    [Header("Dominance -> Pattern Size")]
    [Tooltip("Pattern size at the most negative (submissive/timid) end of Dominance.")]
    [SerializeField] private float minPatternSize = 0.5f;

    [Tooltip("Pattern size at the most positive (assertive/dominant) end of Dominance.")]
    [SerializeField] private float maxPatternSize = 2.0f;

    /// <summary>
    /// Maps Dominance (-1..1) to a pattern size. Direction is a stated
    /// assumption (high Dominance -> large pattern), not confirmed doctrine -
    /// flip minPatternSize/maxPatternSize if it plays backwards.
    /// </summary>
    public float ComputePatternSize(float dominance)
    {
        float normalized = Mathf.Clamp01((dominance + 1f) / 2f); // -1..1 -> 0..1, 1 = most dominant
        return Mathf.Lerp(minPatternSize, maxPatternSize, normalized);
    }
}