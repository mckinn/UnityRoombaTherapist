using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View component for one row of GameScoreTable - a fixed named measure
/// (OVERALL, Dirt Removed, Time Remaining, Damage Done, Emotions, PAD,
/// Therapist Need) plus a colored bar, per
/// GameScoreTable_Implementation_Plan.md section 7.4. Structurally parallel
/// to EmotionProfileRow (Image.fillAmount for the bar - simpler and avoids
/// anchor/pivot edge cases, same reasoning that component used), but the
/// label is set once at creation rather than every update: unlike
/// EmotionProfileTable's dynamic per-entity_type rows, GameScoreTable's 7
/// rows are fixed and never change identity, so there's no reason to
/// re-set the label on every score refresh.
///
/// Expected prefab layout (matches the JIRA's own construction spec): a
/// row GameObject with this component attached, a Panel containing a
/// Measure TMP_Text and a BarTrack hosting a BarFill image - an Image with
/// Image Type set to Filled, Fill Method Horizontal, sitting on top of a
/// plain background/track image (the track itself needs no script
/// reference, same as EmotionProfileRow's barFillImage/track split).
///
/// Pure view: holds no state of its own between calls to SetLabel()/SetScore().
/// </summary>
public class GameScoreRow : MonoBehaviour
{
    [SerializeField] private TMP_Text measureLabel;

    [Tooltip("Image Type must be Filled / Fill Method Horizontal in the Inspector - fillAmount (0-1) is how this component draws the score, not the RectTransform's width.")]
    [SerializeField] private Image barFillImage;

    /// <summary>
    /// Sets this row's fixed name (e.g. "Dirt Removed"). Called once, at
    /// creation, by GameScoreTable - never re-called on later score
    /// refreshes, since a row's identity never changes after it's built.
    /// </summary>
    public void SetLabel(string label)
    {
        if (measureLabel != null)
        {
            measureLabel.text = label;
        }
    }

    /// <summary>
    /// Refreshes this row's bar to the given 0-1 score - length (fillAmount)
    /// and color both come from this single number, via
    /// GameScoreColorUtility, per Plan section 3.3. Called every update by
    /// GameScoreTable, for every row including OVERALL.
    /// </summary>
    public void SetScore(float score01)
    {
        if (barFillImage == null)
        {
            return;
        }

        float clamped = Mathf.Clamp01(score01);
        barFillImage.fillAmount = clamped;
        barFillImage.color = GameScoreColorUtility.ScoreToColor(clamped);
    }
}
