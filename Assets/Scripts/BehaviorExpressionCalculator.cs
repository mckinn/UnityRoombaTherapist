using UnityEngine;

/// <summary>
/// Step 5: numeric-only verification that Pleasure -> Pattern Speed and
/// Dominance -> Pattern Size compute sane values from live PAD state. No
/// actual Behavior patterns yet - Step 6 replaces this logging with real
/// trig-based pattern rendering, reusing these same computed numbers.
///
/// Polls SessionManager.CurrentPad in Update rather than subscribing to
/// SessionManager.OnStateUpdated. Subscribing in OnEnable would depend on
/// SessionManager's Awake (which sets the singleton Instance) having
/// already run - this project already hit one real bug from assuming
/// cross-object initialization ordering (the collision-handler fix earlier
/// this session). Polling sidesteps that entirely; this is a temporary
/// debug aid, not a performance-sensitive path.
/// </summary>
public class BehaviorExpressionCalculator : MonoBehaviour
{
    [Tooltip("The Pleasure/Dominance scaling constants asset.")]
    [SerializeField] private BehaviorExpressionConfig expressionConfig;

    private float lastLoggedPleasure = float.NaN;
    private float lastLoggedDominance = float.NaN;

    private void Update()
    {
        // PADState is a class, not a struct - can be null before a
        // session's first response lands.

        PADState pad = SessionManager.Instance != null ? SessionManager.Instance.CurrentPad : null;

        if (pad == null)
        {
            return;
        }

        bool changed = !Mathf.Approximately(pad.pleasure, lastLoggedPleasure)
                    || !Mathf.Approximately(pad.dominance, lastLoggedDominance);
        if (!changed)
        {
            return;
        }

        lastLoggedPleasure = pad.pleasure;
        lastLoggedDominance = pad.dominance;

        float patternSpeed = expressionConfig.ComputePatternSpeed(pad.pleasure);
        float patternSize = expressionConfig.ComputePatternSize(pad.dominance);

        Debug.Log($"BehaviorExpressionCalculator: pleasure={pad.pleasure:F2} => patternSpeed={patternSpeed:F2}, " +
                  $"dominance={pad.dominance:F2} => patternSize={patternSize:F2}");
    }
}