using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Experimental: a ring of 5 small indicator spheres, one per emotion in
/// the fixed vocabulary, arranged evenly around the Roomba. Each sphere's
/// color is that emotion's configured color (EmotionColorConfig); its size
/// reflects that emotion's current aggregate intensity across every known
/// entity_type sharing it (SessionManager.EntitySensitivities) - NOT tied
/// to any single entity, and NOT the same thing as "last experienced
/// emotion" (EmotionColorIndicator's central sphere). This is a parallel
/// display, independent of that one and of JourneyCalculator's Resolved
/// tracking entirely - it only reads raw client-side sensitivities.
///
/// Intensity aggregation: MAX strength among all entity_types currently
/// assigned that emotion, not sum - "how strongly do I feel this, at its
/// most intense known trigger" rather than a cumulative total that keeps
/// growing as more entity_types are discovered. Worth revisiting if that
/// doesn't feel right once you're watching it.
///
/// Polls SessionManager.Instance every Update() rather than subscribing to
/// an event - same defensive reasoning the since-removed
/// BehaviorExpressionCalculator debug aid used (removed 2026-09-11 along
/// with BehaviorController - see Movement_Concurrency_Plan.md section 4
/// item 4): avoids depending on cross-object Awake/OnEnable ordering for
/// what's a non-performance-critical visual, not a physics-tied one.
/// </summary>
public class EmotionIntensityRing : MonoBehaviour
{
    private static readonly string[] Emotions = { "fear", "joy", "disgust", "curiosity", "ambivalence" };

    [Tooltip("The per-emotion color asset - used for each ring sphere's fixed hue.")]
    [SerializeField] private EmotionColorConfig colorConfig;

    [Tooltip("Prefab for one ring indicator sphere - same recipe as the main indicator: primitive Sphere, Sphere Collider removed, Unlit material.")]
    [SerializeField] private GameObject indicatorPrefab;

    [Tooltip("Distance from the Roomba's center to each ring sphere.")]
    [SerializeField] private float ringRadius = 1.0f;

    [Tooltip("Height above the Roomba the ring sits at.")]
    [SerializeField] private float ringHeight = 1.5f;

    [Tooltip("Sphere scale at zero intensity (0 hides it entirely) and at full (strength = 1.0) intensity.")]
    [SerializeField] private float minSize = 0f;
    [SerializeField] private float maxSize = 0.5f;

    private readonly Dictionary<string, Renderer> ringRenderers = new Dictionary<string, Renderer>();
    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        BuildRing();
    }

    private void BuildRing()
    {
        for (int i = 0; i < Emotions.Length; i++)
        {
            string emotion = Emotions[i];
            float angle = i * (360f / Emotions.Length) * Mathf.Deg2Rad;
            Vector3 localPos = new Vector3(Mathf.Cos(angle) * ringRadius, ringHeight, Mathf.Sin(angle) * ringRadius);

            GameObject indicator = Instantiate(indicatorPrefab, transform);
            indicator.name = $"RingIndicator_{emotion}";
            indicator.transform.localPosition = localPos;
            indicator.transform.localScale = Vector3.zero; // starts hidden until Update() sets a real size

            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"EmotionIntensityRing: indicatorPrefab has no Renderer - '{emotion}' won't be colorable.");
                continue;
            }

            ringRenderers[emotion] = renderer;
            ApplyColorToRenderer(renderer, colorConfig.GetColor(emotion));
        }
    }

    private void Update()
    {
        List<EntitySensitivity> sensitivities = SessionManager.Instance != null ? SessionManager.Instance.EntitySensitivities : null;

        foreach (string emotion in Emotions)
        {
            if (!ringRenderers.TryGetValue(emotion, out Renderer renderer))
            {
                continue;
            }

            float intensity = ComputeMaxIntensity(emotion, sensitivities);
            float size = Mathf.Lerp(minSize, maxSize, Mathf.Clamp01(intensity));
            renderer.transform.localScale = Vector3.one * size;
        }
    }

    private float ComputeMaxIntensity(string emotion, List<EntitySensitivity> sensitivities)
    {
        if (sensitivities == null)
        {
            return 0f;
        }

        float max = 0f;
        foreach (EntitySensitivity sensitivity in sensitivities)
        {
            if (sensitivity.emotion == emotion && sensitivity.strength > max)
            {
                max = sensitivity.strength;
            }
        }
        return max;
    }

    private void ApplyColorToRenderer(Renderer renderer, Color color)
    {
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorPropertyId, color);
        renderer.SetPropertyBlock(propertyBlock);
    }
}