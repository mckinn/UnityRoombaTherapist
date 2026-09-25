using UnityEngine;

/// <summary>
/// Per-emotion movement-tuning constants: the static L/H boundary distances
/// used to compute a Journey's target distance from an entity:
///
///     D = L + V * (H - L)
///
/// where V is the emotion's strength (0..1) from EntitySensitivities.
///
/// One entry per emotion in the fixed vocabulary (fear, joy, disgust,
/// curiosity, ambivalence) - NOT per (emotion, entity_type) pair. Per-type
/// refinement is explicitly parked alongside Backlog #11 (entity_type
/// vocabulary isn't canonicalized yet, so per-type constants would be
/// unbounded and premature).
///
/// Defaults below are placeholders from the design doc's example table -
/// expect to retune all of them once something is actually moving on screen.
/// </summary>
[CreateAssetMenu(fileName = "EmotionMovementConfig", menuName = "RoombaTherapist/Emotion Movement Config")]
public class EmotionMovementConfig : ScriptableObject
{
    [System.Serializable]
    public class EmotionDistanceBounds
    {
        [Tooltip("Must match the ROOMBA_STATE_TOOL emotion enum exactly: fear, joy, disgust, curiosity, ambivalence.")]
        public string emotion;

        [Tooltip("Destination distance when this emotion's strength (V) is 0.")]
        public float L;

        [Tooltip("Destination distance when this emotion's strength (V) is 1.")]
        public float H;
    }

    [Tooltip("Live-tunable in the Inspector, including while the game is running.")]
    [SerializeField]
    private EmotionDistanceBounds[] bounds = new EmotionDistanceBounds[]
    {
        new EmotionDistanceBounds { emotion = "fear",        L = 3f, H = 10f },
        new EmotionDistanceBounds { emotion = "joy",         L = 5f, H = 2f  },
        new EmotionDistanceBounds { emotion = "disgust",     L = 3f, H = 3f  },
        new EmotionDistanceBounds { emotion = "curiosity",   L = 7f, H = 3f  },
        // Ambivalence's "+/- Random" component isn't implemented yet - using
        // a flat midpoint for now. Worth a separate discussion on how/where
        // randomization should apply before this one is trusted.
        new EmotionDistanceBounds { emotion = "ambivalence", L = 5f, H = 5f  },
        // the destination emotion is for things that are just destinations
        new EmotionDistanceBounds { emotion = "destination", L = 0f, H = 0f  },
    };

    /// <summary>
    /// Returns the L/H bounds for the given emotion, or null if it isn't
    /// configured. Shouldn't happen given the enum-constrained vocabulary
    /// upstream, but this reads live gameplay data, so it's handled rather
    /// than assumed.
    /// </summary>
    public EmotionDistanceBounds GetBounds(string emotion)
    {
        foreach (var b in bounds)
        {
            if (b.emotion == emotion)
            {
                return b;
            }
        }
        Debug.LogWarning($"EmotionMovementConfig: no L/H bounds configured for emotion '{emotion}'.");
        return null;
    }

    /// <summary>
    /// D = L + V * (H - L). Returns 0 if the emotion isn't configured.
    /// </summary>
    public float ComputeDestinationDistance(string emotion, float strength)
    {
        EmotionDistanceBounds b = GetBounds(emotion);
        if (b == null)
        {
            return 0f;
        }
        return b.L + strength * (b.H - b.L);
    }

    [Header("Deadband (epsilon)")]

    [Tooltip("Multiplier applied on top of the theoretical minimum epsilon, so the deadband isn't sized at the exact knife-edge of being skippable.")]
    [SerializeField] private float epsilonSafetyFactor = 1.5f;

    /// <summary>
    /// The deadband half-width: how close to D counts as "resolved". Derived
    /// from MaxClosingSpeed (the true ceiling, regardless of current Arousal)
    /// rather than set independently, so that tuning closing speed upward
    /// can't silently make the deadband skippable without also moving
    /// epsilon - the two are kept structurally coupled instead of relying on
    /// someone remembering to update both by hand.
    /// </summary>
    public float ComputeEpsilon()
    {
        return maxClosingSpeed * Time.fixedDeltaTime * epsilonSafetyFactor;
    }

    [Header("Arousal -> Closing Speed")]
    [Tooltip("Speed used at the calmest end of Arousal, and as the default whenever PAD state isn't known yet (e.g. before a session's first response has landed).")]
    [SerializeField] private float minClosingSpeed = 0.5f;

    [Tooltip("Speed used at the most aroused end of Arousal. This is also the ceiling ComputeEpsilon() is derived from - the true worst case, regardless of current PAD state.")]
    [SerializeField] private float maxClosingSpeed = 2.0f;

    [Tooltip("If true (assumed default): high Arousal => fast closing speed (maxClosingSpeed), low/negative Arousal => slow (minClosingSpeed). Flip if it plays backwards from what you'd expect.")]
    [SerializeField] private bool highArousalMeansFastClosing = true;

    /// <summary>
    /// Speed used at the calmest end of Arousal, and the value the null-PAD
    /// fallback should use directly (see JourneyCalculator) - not the
    /// midpoint, the actual lower bound.
    /// </summary>
    public float MinClosingSpeed => minClosingSpeed;

    public float MaxClosingSpeed => maxClosingSpeed;
    /// <summary>
    /// Maps current Arousal (-1..1) to a closing speed within
    /// [minClosingSpeed, maxClosingSpeed]. Used for BOTH Journey-driven
    /// movement and manual/keyboard movement - the Roomba's overall
    /// responsiveness is a function of its emotional arousal regardless of
    /// what's currently deciding its direction.
    /// </summary>
    public float ComputeClosingSpeed(float arousal)
    {
        float normalized = Mathf.Clamp01((arousal + 1f) / 2f); // -1..1 -> 0..1, 1 = most aroused
        if (!highArousalMeansFastClosing)
        {
            normalized = 1f - normalized;
        }
        return Mathf.Lerp(minClosingSpeed, maxClosingSpeed, normalized);
    }
}