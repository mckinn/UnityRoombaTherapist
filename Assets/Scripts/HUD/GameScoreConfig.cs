using UnityEngine;

/// <summary>
/// All tunable inputs for the GameScoreTable HUD widget (composite win/loss
/// indicator) - see GameScoreTable_Implementation_Plan.md in the Docs repo
/// for the full design and the formulas each of these feeds. Every value
/// here is deliberately Inspector-tunable, per the explicit target that this
/// story isn't "done" until the whole scoring mix can be retuned by
/// play-testing rather than by editing code.
///
/// Live-tunable in the Inspector, including while the game is running -
/// same convention as EmotionColorConfig/EmotionMovementConfig/DirtConfig.
///
/// Default values below are starting points, not final answers:
/// - The six weights are seeded directly from the JIRA worked-example
///   sheet's "% contrib" column (20/20/20/10/10/20), which already sums to
///   100% - a sane starting mix, expected to change with play-testing.
/// - entitySensitivityHighRange/padLowRange/padHighRange are the sheet's own
///   example range values (4, and -3..3 respectively).
/// - totalAllowedSeconds and targetWordsForFiftyPercent have no real-world
///   basis yet (no level structure exists to time against, and no play-test
///   data exists for typical therapy-dialog length) - both are arbitrary
///   round-number placeholders, flagged as such below.
///
/// Deliberately holds no color/gradient configuration - the score-to-color
/// mapping (GameScoreColorUtility) is a single computed formula with no
/// tunable inputs, per Steve's explicit call for simplicity there.
/// </summary>
[CreateAssetMenu(fileName = "GameScoreConfig", menuName = "RoombaTherapist/Game Score Config")]
public class GameScoreConfig : ScriptableObject
{
    [Header("Contributor Weights (% of OVERALL - expected, not enforced, to sum to 1.0)")]

    [Tooltip("Weight of the Dirt Removed row in the OVERALL weighted average. Sheet example: 20%.")]
    [SerializeField, Range(0f, 1f)] private float dirtWeight = 0.20f;
    public float DirtWeight => dirtWeight;

    [Tooltip("Weight of the Time Remaining row in the OVERALL weighted average. Sheet example: 20%.")]
    [SerializeField, Range(0f, 1f)] private float timeWeight = 0.20f;
    public float TimeWeight => timeWeight;

    [Tooltip("Weight of the Damage Done row in the OVERALL weighted average. Kept configurable even though this story's input is a hardcoded stub (see GameScoreTable) - the weight still matters for how OVERALL is balanced today. Sheet example: 20%.")]
    [SerializeField, Range(0f, 1f)] private float damageWeight = 0.20f;
    public float DamageWeight => damageWeight;

    [Tooltip("Weight of the Emotions (entity sensitivities) row in the OVERALL weighted average. Sheet example: 10%.")]
    [SerializeField, Range(0f, 1f)] private float emotionsWeight = 0.10f;
    public float EmotionsWeight => emotionsWeight;

    [Tooltip("Weight of the PAD row in the OVERALL weighted average. Sheet example: 10%.")]
    [SerializeField, Range(0f, 1f)] private float padWeight = 0.10f;
    public float PadWeight => padWeight;

    [Tooltip("Weight of the Therapist Need row in the OVERALL weighted average. Sheet example: 20%.")]
    [SerializeField, Range(0f, 1f)] private float therapistWeight = 0.20f;
    public float TherapistWeight => therapistWeight;

    [Header("Emotions (Entity Sensitivities) Range")]

    [Tooltip("Worst-case sum of raw, non-ambivalence EntitySensitivity strengths - the denominator for the Emotions score (lower raw sum is better; 0 is the 'zen' ideal). Sheet example: 4.")]
    [SerializeField] private float entitySensitivityHighRange = 4f;
    public float EntitySensitivityHighRange => entitySensitivityHighRange;

    [Header("PAD Range")]

    [Tooltip("Worst-case sum of raw PAD (pleasure+arousal+dominance). Sheet example: -3, matching three axes each at -1.")]
    [SerializeField] private float padLowRange = -3f;
    public float PadLowRange => padLowRange;

    [Tooltip("Best-case sum of raw PAD (pleasure+arousal+dominance). Sheet example: 3, matching three axes each at +1.")]
    [SerializeField] private float padHighRange = 3f;
    public float PadHighRange => padHighRange;

    [Header("Time")]

    [Tooltip("Total seconds the Time Remaining score decays over, linearly, from 1.0 to 0.0 (see GameLevelTimer). Arbitrary placeholder (5 minutes) - no real level-length data exists yet to base this on.")]
    [SerializeField] private float totalAllowedSeconds = 300f;
    public float TotalAllowedSeconds => totalAllowedSeconds;

    [Header("Therapist Need")]

    [Tooltip("The word count at which the Therapist Need score is exactly 0.5 (the TANH formula's G7 term) - fewer words than this scores higher, more scores lower. Arbitrary placeholder - no play-test data exists yet for typical therapy-dialog length.")]
    [SerializeField] private float targetWordsForFiftyPercent = 150f;
    public float TargetWordsForFiftyPercent => targetWordsForFiftyPercent;

    [Header("Egress Door (Egress_Door_Implementation_Plan.md)")]

    [Tooltip("Dirt-collection fraction (0-1, see DirtProgressReporter.PercentCollected01) at which the egress door unlocks - checked by DoorOpener. Not otherwise tied to the six weights above.")]
    [SerializeField, Range(0f, 1f)] private float exitDirtThreshold01 = 0.8f;
    public float ExitDirtThreshold01 => exitDirtThreshold01;

    [Header("Freeze Behavior (Freeze_And_Stop_Implementation_Plan.md)")]

    [Tooltip("Read by GameScoreTable. True (the default) means the Dirt Removed row snapshots its value the instant a Freeze happens and holds it from then on - 'the state at issuance of Freeze, preserved for discussion.' False lets it keep calling ComputeDirtScore() every frame, live, straight through the Freeze - useful for experimenting with whether letting a contributor keep moving during the post-Freeze Therapist conversation (which is explicitly still happening) is a useful teaching signal.")]
    [SerializeField] private bool freezeDirtScore = true;
    public bool FreezeDirtScore => freezeDirtScore;

    [Tooltip("Same meaning as freezeDirtScore above, for the Time Remaining row (ComputeTimeScore()).")]
    [SerializeField] private bool freezeTimeScore = true;
    public bool FreezeTimeScore => freezeTimeScore;

    [Tooltip("Same meaning as freezeDirtScore above, for the Damage Done row (ComputeDamageScore()) - has no practical effect today since that row is a hardcoded stub, but kept for symmetry with the other five.")]
    [SerializeField] private bool freezeDamageScore = true;
    public bool FreezeDamageScore => freezeDamageScore;

    [Tooltip("Same meaning as freezeDirtScore above, for the Emotions row (ComputeEmotionsScore()). This row is dialog-driven, not movement-driven, so unlike Dirt this one can genuinely keep changing through a Freeze if set to false, since Freezing does not stop Therapist dialog.")]
    [SerializeField] private bool freezeEmotionsScore = true;
    public bool FreezeEmotionsScore => freezeEmotionsScore;

    [Tooltip("Same meaning as freezeDirtScore above, for the PAD row (ComputePadScore()). Dialog-driven like Emotions above - see that tooltip.")]
    [SerializeField] private bool freezePadScore = true;
    public bool FreezePadScore => freezePadScore;

    [Tooltip("Same meaning as freezeDirtScore above, for the Therapist Need row (ComputeTherapistScore()). Dialog-driven like Emotions above - see that tooltip.")]
    [SerializeField] private bool freezeTherapistScore = true;
    public bool FreezeTherapistScore => freezeTherapistScore;
}
