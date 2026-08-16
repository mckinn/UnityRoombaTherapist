using UnityEngine;

/// <summary>
/// Step 6 (stationary): renders a Behavior pattern - a local oscillation
/// offset from a fixed anchor point - for the Roomba's currently-expressed
/// resolved journey (JourneyCalculator.StableExpressionJourney).
///
/// Deliberately has NO FixedUpdate of its own. ApplyPattern() is called
/// explicitly, once per physics tick, by JourneyCalculator.FixedUpdate,
/// which has already computed StableExpressionJourney that same frame. Two
/// independent FixedUpdate methods trying to agree on relative ordering is
/// exactly the class of fragility this project already hit once (and
/// fixed) with collision handling; same fix applied here proactively.
///
/// Anchor/phase handling while the player interrupts with the keyboard:
/// the pattern's own clock (phase) freezes rather than continuing in the
/// background, and the anchor is continuously re-solved each frame so that
/// anchor + offset(frozen phase) always equals the Roomba's actual current
/// position. That keeps the invariant true throughout the interruption, so
/// the moment the player releases the keys, the very next MoveTo() call
/// uses an anchor that's already correct - no snap back to a stale point.
///
/// Orientation: ComputePatternOffset returns a LOCAL offset (.x =
/// perpendicular-to-entity component, .z = toward/away-from-entity
/// component), not world space. ApplyPattern builds a per-entity basis
/// each call from the journey's stored contact point and rotates that
/// local offset into world space - so, e.g., Fear's shake (which only ever
/// used .x) automatically became "shake perpendicular to the line to the
/// feared object" with no change to its formula at all.
/// </summary>
public class BehaviorController : MonoBehaviour
{
    [Tooltip("The motor this script drives via MoveTo().")]
    [SerializeField] private PlayerController playerController;

    [Tooltip("The Pleasure/Dominance scaling constants asset (Step 5).")]
    [SerializeField] private BehaviorExpressionConfig expressionConfig;

    // Which journey the pattern is currently anchored to - used only to
    // detect when the expressed entity changes, so the pattern can restart
    // cleanly rather than jump discontinuously mid-cycle.
    private ActiveJourney currentJourney;

    // Where the pattern oscillates around. Captured fresh whenever
    // currentJourney changes; continuously re-solved while the player is
    // driving (see class summary).
    private Vector3 anchor;

    // Accumulated local phase, NOT Time.time - so a mid-display change in
    // patternSpeed (Pleasure shifting) changes pace smoothly rather than
    // causing a discontinuous jump in the curve. Frozen (not advanced)
    // while the player is driving.
    private float phase;

    /// <summary>
    /// Called once per FixedUpdate by JourneyCalculator whenever there's a
    /// resolved journey to express. Resets anchor/phase when the expressed
    /// journey's entity changes (including from "nothing" to "something").
    ///
    /// playerIsDriving: true on frames where JourneyCalculator moved the
    /// Roomba via keyboard instead of Behavior owning position. On those
    /// frames this method does NOT call MoveTo (JourneyCalculator already
    /// moved the Roomba this frame) - it only keeps phase/anchor state
    /// consistent so resuming afterward is seamless.
    /// </summary>
    public void ApplyPattern(ActiveJourney loudestJourney, bool playerIsDriving)
    {
        if (loudestJourney != currentJourney)
        {
            currentJourney = loudestJourney;
            anchor = playerController.transform.position;
            phase = 0f;
        }

        // PADState is a class, not a struct - can be null before a
        // session's first response lands. Falling back to neutral (0)
        // Pleasure/Dominance rather than skipping the pattern entirely -
        // unlike Journey movement, there's no "calmest state" analogue
        // that's obviously correct here, so neutral is the least-surprising
        // default for a visual that should still be showing something.
        PADState pad = SessionManager.Instance != null ? SessionManager.Instance.CurrentPad : null;
        float pleasure = pad != null ? pad.pleasure : 0f;
        float dominance = pad != null ? pad.dominance : 0f;

        float patternSpeed = expressionConfig.ComputePatternSpeed(pleasure);
        float patternSize = expressionConfig.ComputePatternSize(dominance);

        // Per-entity basis: "away" points from the entity's last known
        // contact point toward the anchor; "perpendicular" is 90 degrees
        // from that in the horizontal plane. Recomputed each call (cheap -
        // two stored Vector3s, one cross product) rather than cached.
        Vector3 entityPos = loudestJourney.LastKnownPosition;
        entityPos.y = 0f;
        Vector3 anchorFlat = anchor;
        anchorFlat.y = 0f;
        Vector3 awayFromEntity = anchorFlat - entityPos;
        if (awayFromEntity.sqrMagnitude < 0.0001f)
        {
            // Degenerate case: anchor coincides with the entity's contact
            // point. Fall back to a fixed reference direction rather than
            // normalizing a near-zero vector.
            awayFromEntity = Vector3.forward;
        }
        awayFromEntity.Normalize();
        Vector3 perpendicular = Vector3.Cross(Vector3.up, awayFromEntity).normalized;

        if (playerIsDriving)
        {
            // Pause the pattern's clock and re-solve the anchor against
            // wherever the player has actually driven the Roomba, using
            // the now-frozen offset (converted to world space). See class
            // summary for why this eliminates the resume-snap rather than
            // just reducing it.
            Vector3 frozenLocalOffset = ComputePatternOffset(loudestJourney.Emotion, phase, patternSize);
            Vector3 frozenWorldOffset = perpendicular * frozenLocalOffset.x + awayFromEntity * frozenLocalOffset.z;
            anchor = playerController.transform.position - frozenWorldOffset;
            return;
        }

        phase += patternSpeed * Time.fixedDeltaTime;

        Vector3 localOffset = ComputePatternOffset(loudestJourney.Emotion, phase, patternSize);
        Vector3 worldOffset = perpendicular * localOffset.x + awayFromEntity * localOffset.z;
        playerController.MoveTo(anchor + worldOffset);
    }

    /// <summary>
    /// Per-emotion LOCAL planar offset from the anchor - .x is the
    /// perpendicular-to-entity component, .z is the toward/away-from-entity
    /// component. ApplyPattern rotates this into world space; this function
    /// itself knows nothing about where the entity actually is.
    ///
    /// Fear, Disgust, Curiosity, and Ambivalence are all offset-from-anchor
    /// path patterns and share this one dispatch. Joy is deliberately NOT
    /// here - per design, Joy has no path at all (pure spin + rhythmic
    /// Jump()), so forcing it into this offset-based function would be the
    /// wrong shape for what it actually needs. It stays a logged no-op
    /// until that's built as its own piece of work.
    /// </summary>
    private Vector3 ComputePatternOffset(string emotion, float phase, float size)
    {
        switch (emotion)
        {
            case "fear":
                // Degenerate case, one axis: back-and-forth shake, purely
                // perpendicular - no toward/away component at all.
                return new Vector3(size * Mathf.Sin(phase), 0f, 0f);

            case "disgust":
                {
                    // Recoiling arc sweep: angle oscillates within a fixed
                    // half-width (a characteristic of "how far a flinch
                    // swings," independent of PAD), size sets the arc's
                    // radius. At phase = 0, angle = 0, offset = zero - same
                    // anchor-at-rest guarantee as the other patterns.
                    float angle = DisgustArcHalfAngleRadians * Mathf.Sin(phase);
                    return new Vector3(
                        size * Mathf.Sin(angle),
                        0f,
                        size * (1f - Mathf.Cos(angle)));
                }

            case "curiosity":
                // Wide ellipse - wider than tall so it reads as
                // investigative circling rather than a tight, anxious loop.
                return new Vector3(
                    size * CuriosityWideningFactor * Mathf.Cos(phase),
                    0f,
                    size * Mathf.Sin(phase));

            case "ambivalence":
                // Classic figure-eight Lissajous curve: frequency ratio
                // 1:2, zero phase offset between axes.
                return new Vector3(
                    size * Mathf.Sin(phase),
                    0f,
                    size * Mathf.Sin(2f * phase));

            default:
                // Debug.Log($"BehaviorController: no pattern implemented yet for emotion '{emotion}'.");
                return Vector3.zero;
        }
    }

    // Fixed shape characteristics, not PAD-driven - these define WHAT each
    // pattern looks like; patternSize (Dominance) and patternSpeed
    // (Pleasure) separately control how big/fast whichever one is showing.
    private const float DisgustArcHalfAngleRadians = 60f * Mathf.Deg2Rad;
    private const float CuriosityWideningFactor = 1.6f;
}