using UnityEngine;

/// <summary>
/// Root HUD widget for the composite win/loss indicator - see
/// GameScoreTable_Implementation_Plan.md for the full design. Structurally
/// parallel to EmotionProfileTable (a row prefab instantiated under a
/// container), but deliberately NOT the same rebuild-on-change pattern
/// (Plan section 2.3): EmotionProfileTable renders a variable-length list
/// that gets destroyed and rebuilt whenever the underlying data changes.
/// This table's 7 rows (OVERALL + 6 named contributors) are fixed and never
/// change identity, so they're instantiated once in Awake() and only their
/// scores are refreshed afterward - a plain numeric update, not a rebuild.
///
/// Recomputes every row every Update(), not on any discrete event - unlike
/// EmotionProfileTable (which only needs to react when EntitySensitivities
/// changes), Time Remaining changes continuously with no triggering event
/// at all, so this table has to poll regardless of what the other five
/// contributors are doing.
///
/// 100% local to Unity, per Plan section 3.1 - reads SessionManager and
/// DirtProgressReporter's existing singletons directly, plus two plain
/// (non-singleton) component references this story added
/// (GameLevelTimer, and TherapyChatController for its new
/// TotalWordsSpoken). No Orchestrator/LLM involvement.
///
/// Extended 2026-09-25 (Freeze_And_Stop_Implementation_Plan.md): on the
/// first Update() after freezeController.IsFrozen goes true, every
/// contributor's current value is captured once into a snapshot. From then
/// on, each row independently either holds that snapshot or keeps
/// recomputing live, per its own GameScoreConfig toggle (FreezeDirtScore
/// etc.) - deliberately per-row, not a single "is the whole table frozen"
/// switch, because three of the six contributors (Emotions/PAD/Therapist
/// Need) are dialog-driven, not movement-driven, and Freezing does not
/// stop Therapist dialog, so those three CAN keep changing through a
/// Freeze if their toggle is set to false. This is also why the freeze
/// isn't implemented by stopping GameLevelTimer/SessionManager/
/// TherapyChatController themselves - none of those three sources should
/// be told to stop, only whether GameScoreTable keeps reading them live.
/// OVERALL has no toggle of its own: it's always the live weighted sum of
/// whatever the six row values currently are, frozen or not.
/// </summary>
public class GameScoreTable : MonoBehaviour
{
    [Tooltip("Row template - must have a GameScoreRow component. Instantiated once per row, in fixed order, parented under rowContainer.")]
    [SerializeField] private GameScoreRow rowPrefab;

    [Tooltip("Parent all rows are instantiated under - give it a Vertical Layout Group so rows stack automatically, same as EmotionProfileTable's rowContainer.")]
    [SerializeField] private RectTransform rowContainer;

    [Tooltip("Supplies every weight and range used below, plus the six FreezeXScore toggles - see GameScoreConfig.")]
    [SerializeField] private GameScoreConfig config;

    [Tooltip("Supplies RemainingFraction01 for the Time Remaining row. No singleton on this component (unlike SessionManager/DirtProgressReporter), so it must be wired here explicitly.")]
    [SerializeField] private GameLevelTimer gameLevelTimer;

    [Tooltip("Supplies TotalWordsSpoken for the Therapist Need row. No singleton on this component either - same reason as gameLevelTimer above.")]
    [SerializeField] private TherapyChatController chatController;

    [Tooltip("Supplies IsFrozen - see Freeze_And_Stop_Implementation_Plan.md. No singleton on this component either, same reason as gameLevelTimer/chatController above. Leave unassigned to disable freeze-snapshot behavior entirely (every row then always computes live, as if never frozen).")]
    [SerializeField] private FreezeController freezeController;

    public float overallScore;

    private float dirtScoreSnapshot;
    private float timeScoreSnapshot;
    private float damageScoreSnapshot;
    private float emotionsScoreSnapshot;
    private float padScoreSnapshot;
    private float therapistScoreSnapshot;
    private bool hasCapturedFreezeSnapshot;

    private static readonly string[] RowLabels =
    {
        "OVERALL", "Dirt Removed", "Time Remaining", "Damage Done", "Emotions", "PAD", "Therapist Need"
    };

    private GameScoreRow rowOverall;
    private GameScoreRow rowDirt;
    private GameScoreRow rowTime;
    private GameScoreRow rowDamage;
    private GameScoreRow rowEmotions;
    private GameScoreRow rowPad;
    private GameScoreRow rowTherapist;

    private void Awake()
    {
        if (rowPrefab == null || rowContainer == null)
        {
            Debug.LogWarning("GameScoreTable: rowPrefab or rowContainer not assigned - no rows will be built.");
            return;
        }

        GameScoreRow[] rows = new GameScoreRow[RowLabels.Length];
        for (int i = 0; i < RowLabels.Length; i++)
        {
            GameScoreRow row = Instantiate(rowPrefab, rowContainer);
            row.SetLabel(RowLabels[i]);
            rows[i] = row;
        }

        rowOverall = rows[0];
        rowDirt = rows[1];
        rowTime = rows[2];
        rowDamage = rows[3];
        rowEmotions = rows[4];
        rowPad = rows[5];
        rowTherapist = rows[6];
    }

    private void Update()
    {
        bool isFrozen = freezeController != null && freezeController.IsFrozen;

        // Captured exactly once, the first Update() after IsFrozen goes
        // true - not re-captured on every subsequent frozen frame. This is
        // the "state at issuance of Freeze" moment every FreezeXScore=true
        // row below will hold from now on.
        if (isFrozen && !hasCapturedFreezeSnapshot)
        {
            dirtScoreSnapshot = ComputeDirtScore();
            timeScoreSnapshot = ComputeTimeScore();
            damageScoreSnapshot = ComputeDamageScore();
            emotionsScoreSnapshot = ComputeEmotionsScore();
            padScoreSnapshot = ComputePadScore();
            therapistScoreSnapshot = ComputeTherapistScore();
            hasCapturedFreezeSnapshot = true;
            Debug.Log("GameScoreTable: Freeze snapshot captured.");
        }

        // Each row independently either holds its snapshot or keeps
        // recomputing live, per its own config toggle - see this class's
        // own doc comment for why this is per-row rather than one switch.
        // The live Compute*Score() call is skipped entirely (not just
        // ignored) whenever the snapshot is used, so a frozen, held row
        // has no dependency left on whatever it used to read from.
        float dirtScore = (isFrozen && config != null && config.FreezeDirtScore) ? dirtScoreSnapshot : ComputeDirtScore();
        float timeScore = (isFrozen && config != null && config.FreezeTimeScore) ? timeScoreSnapshot : ComputeTimeScore();
        float damageScore = (isFrozen && config != null && config.FreezeDamageScore) ? damageScoreSnapshot : ComputeDamageScore();
        float emotionsScore = (isFrozen && config != null && config.FreezeEmotionsScore) ? emotionsScoreSnapshot : ComputeEmotionsScore();
        float padScore = (isFrozen && config != null && config.FreezePadScore) ? padScoreSnapshot : ComputePadScore();
        float therapistScore = (isFrozen && config != null && config.FreezeTherapistScore) ? therapistScoreSnapshot : ComputeTherapistScore();

        // OVERALL has no freeze toggle of its own - always the live
        // weighted sum of whatever the six values above currently are,
        // whichever of them are frozen or live this frame.
        overallScore = ComputeOverallScore(dirtScore, timeScore, damageScore, emotionsScore, padScore, therapistScore);

        rowOverall?.SetScore(overallScore);
        rowDirt?.SetScore(dirtScore);
        rowTime?.SetScore(timeScore);
        rowDamage?.SetScore(damageScore);
        rowEmotions?.SetScore(emotionsScore);
        rowPad?.SetScore(padScore);
        rowTherapist?.SetScore(therapistScore);
    }

    /// <summary>
    /// Weighted average of the 6 contributor scores, per Plan section 4 -
    /// weights come from config and are expected (not enforced, per Plan
    /// section 3.10) to sum to 1.0. Falls back to 0 contributors weighted
    /// (i.e. 0f) if config is entirely missing, rather than guessing -
    /// an unassigned config is a setup mistake worth noticing on the HUD,
    /// unlike the per-contributor "missing data reads as best case"
    /// fallbacks used elsewhere in this class.
    /// </summary>
    private float ComputeOverallScore(float dirtScore, float timeScore, float damageScore, float emotionsScore, float padScore, float therapistScore)
    {
        if (config == null)
        {
            return 0f;
        }

        float weightedSum =
            dirtScore * config.DirtWeight +
            timeScore * config.TimeWeight +
            damageScore * config.DamageWeight +
            emotionsScore * config.EmotionsWeight +
            padScore * config.PadWeight +
            therapistScore * config.TherapistWeight;

        return Mathf.Clamp01(weightedSum);
    }

    /// <summary>
    /// Plan section 4.1 - reuses DirtProgressReporter's own PercentCollected01
    /// rather than recomputing the ratio, so this row and the Orchestrator
    /// report it sends are provably reading the same number.
    /// </summary>
    private float ComputeDirtScore()
    {
        return DirtProgressReporter.Instance != null ? DirtProgressReporter.Instance.PercentCollected01 : 1f;
    }

    /// <summary>Plan section 4.2 - straight pass-through of GameLevelTimer's own fraction.</summary>
    private float ComputeTimeScore()
    {
        return gameLevelTimer != null ? gameLevelTimer.RemainingFraction01 : 1f;
    }

    /// <summary>
    /// Plan section 4.3 - hardcoded stub for this story. No object registry
    /// or displacement detection exists anywhere in this project yet; a
    /// dedicated future story (Steve's) will replace this constant with a
    /// real (total - displacedCount) / total computation once that
    /// inventory exists. Deliberately not (total - 0) / total with a
    /// fabricated total, per Plan section 4.3's own reasoning.
    /// </summary>
    private float ComputeDamageScore()
    {
        return 1f;
    }

    /// <summary>
    /// Plan section 4.4 - sum of raw, non-ambivalence EntitySensitivity
    /// strengths, inverted (lower raw sum is better - 0 is the "zen" ideal).
    /// "none"/empty entries are skipped the same way EmotionProfileTable
    /// already does, and ambivalence is excluded per the JIRA's own framing
    /// ("Ambivalence does not count in the calculation").
    /// </summary>
    private float ComputeEmotionsScore()
    {
        if (config == null || SessionManager.Instance == null || SessionManager.Instance.EntitySensitivities == null)
        {
            return 1f;
        }

        float rawSum = 0f;
        foreach (EntitySensitivity sensitivity in SessionManager.Instance.EntitySensitivities)
        {
            if (string.IsNullOrEmpty(sensitivity.emotion) || sensitivity.emotion == "none" || sensitivity.emotion == "ambivalence")
            {
                continue;
            }
            rawSum += sensitivity.strength;
        }

        float high = config.EntitySensitivityHighRange;
        if (high <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp01((high - rawSum) / high);
    }

    /// <summary>
    /// Plan section 4.5 - sum of raw PAD (pleasure+arousal+dominance),
    /// NOT inverted - a maxed-out (happy/aroused/dominant) Roomba is the
    /// goal, so a higher raw sum scores higher. CurrentPad is nullable
    /// (defensive null-checking convention used throughout this codebase),
    /// guarded the same way EmotionProfileTable guards EntitySensitivities.
    /// </summary>
    private float ComputePadScore()
    {
        if (config == null || SessionManager.Instance == null || SessionManager.Instance.CurrentPad == null)
        {
            return 1f;
        }

        PADState pad = SessionManager.Instance.CurrentPad;
        float rawSum = pad.pleasure + pad.arousal + pad.dominance;
        float low = config.PadLowRange;
        float high = config.PadHighRange;
        if (high <= low)
        {
            return 1f;
        }

        return Mathf.Clamp01((rawSum - low) / (high - low));
    }

    /// <summary>
    /// Plan section 4.6 - the JIRA's own TANH formula, unchanged:
    /// score = Tanh((Ln(3)/2) * (targetWords / actualWordsSpoken)). Guards
    /// wordsSpoken == 0 explicitly (best case, no intervention yet) rather
    /// than letting the ratio divide by zero.
    /// </summary>
    private float ComputeTherapistScore()
    {
        if (config == null || chatController == null)
        {
            return 1f;
        }

        int wordsSpoken = chatController.TotalWordsSpoken;
        if (wordsSpoken <= 0)
        {
            return 1f;
        }

        float ratio = config.TargetWordsForFiftyPercent / wordsSpoken;
        double score = System.Math.Tanh((Mathf.Log(3f) / 2f) * ratio);
        return Mathf.Clamp01((float)score);
    }
}
