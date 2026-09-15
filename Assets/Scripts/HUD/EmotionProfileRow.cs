using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View component for one row of EmotionProfileTable - a single entity_type's
/// current emotion, drawn as an entity_type label, an emotion label, and a
/// colored intensity bar. Uses Image.fillAmount (Image Type = Filled) for
/// the bar rather than manually resizing a RectTransform - simpler and
/// avoids anchor/pivot edge cases. Pure view: holds no state of its own
/// between calls to Set().
///
/// Expected prefab layout (build this in the Editor, same pattern as
/// EmotionIntensityRing's indicatorPrefab): a row GameObject with this
/// component attached, an entityLabel TMP_Text, an emotionLabel TMP_Text,
/// and a barFillImage - an Image with Image Type set to Filled, Fill Method
/// Horizontal, sitting on top of a plain background/track Image (the track
/// itself needs no script reference; it's just a static background image
/// behind barFillImage).
/// </summary>
public class EmotionProfileRow : MonoBehaviour
{
    [SerializeField] private TMP_Text entityLabel;
    [SerializeField] private TMP_Text emotionLabel;

    [Tooltip("Image Type must be Filled / Fill Method Horizontal in the Inspector - fillAmount (0-1) is how this component draws intensity, not the RectTransform's width.")]
    [SerializeField] private Image barFillImage;

    public void Set(string entityType, string emotion, float strength, Color color)
    {
        if (entityLabel != null)
        {
            entityLabel.text = entityType;
        }

        if (emotionLabel != null)
        {
            emotionLabel.text = emotion;
        }

        if (barFillImage != null)
        {
            barFillImage.color = color;
            barFillImage.fillAmount = Mathf.Clamp01(strength);
        }
    }
}
