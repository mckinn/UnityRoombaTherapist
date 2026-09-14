using UnityEngine;

/// <summary>
/// Reflects the emotion from the most recent collision as a color -
/// continuous from that contact, persisting through manual movement and
/// Journey pursuit alike, until the next contact changes it. Entirely
/// decoupled from JourneyCalculator's movement/resolution state on
/// purpose: subscribes directly to CollisionController.OnEntityCollision
/// as an independent second listener (the same event JourneyCalculator
/// uses), so there's no shared state and no authority conflict to manage -
/// color literally cannot be affected by what Journey/keyboard are doing.
///
/// A prior procedural-motion expression approach (BehaviorController,
/// removed 2026-09-11 - see Movement_Concurrency_Plan.md section 4 item 4)
/// aimed at the same underlying goal - this is a parallel, simpler
/// replacement, unaffected by that removal: making the Roomba's current
/// feeling legible to the player at a glance.
///
/// ApplyColor() is intentionally a stub right now - the actual mechanism
/// (MaterialPropertyBlock + the correct shader color property name) is
/// render-pipeline-dependent (_Color for Built-in, _BaseColor for URP) and
/// pending confirmation of which pipeline this project uses.
/// </summary>
public class EmotionColorIndicator : MonoBehaviour
{
    [Tooltip("The CollisionController that owns the physical OnCollisionEnter event.")]
    [SerializeField] private CollisionController collisionController;

    [Tooltip("The per-emotion color asset.")]
    [SerializeField] private EmotionColorConfig colorConfig;

    [Tooltip("The renderer this indicator paints - e.g. a small unlit indicator object parented above the Roomba, not necessarily the Roomba's own body.")]
    [SerializeField] private Renderer indicatorRenderer;

    [Tooltip("Read-only observation of JourneyCalculator's active set - used only to detect when nothing is pending anymore, to revert to the default color. This is the one piece of coupling to Journey state this script otherwise deliberately avoids.")]
    [SerializeField] private JourneyCalculator journeyCalculator;

    private bool hadPendingJourneys;

    private Color currentColor;
    private Color transitionStartColor;
    private Color targetColor;
    private float transitionElapsed;
    private bool transitioning;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        currentColor = colorConfig.DefaultColor;
        targetColor = currentColor;
        propertyBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        ApplyColor(currentColor);
    }

    private void OnEnable()
    {
        if (collisionController != null)
        {
            collisionController.OnEntityCollision += HandleEntityCollision;
        }
        else
        {
            Debug.LogWarning("EmotionColorIndicator has no CollisionController assigned - it will never receive collision events.");
        }
    }

    private void OnDisable()
    {
        if (collisionController != null)
        {
            collisionController.OnEntityCollision -= HandleEntityCollision;
        }
    }

    private void HandleEntityCollision(EntityIdentity identity, string entityType, Vector3 contactPoint)
    {
        EntitySensitivity sensitivity = SessionManager.Instance.GetSensitivity(entityType);

        if (sensitivity == null || sensitivity.emotion == "none")
        {
            // Same filter JourneyCalculator uses: no formed opinion yet -
            // keep showing whatever the Roomba last legitimately felt,
            // rather than overwriting it with a "none" color.
            return;
        }

        BeginTransitionTo(colorConfig.GetColor(sensitivity.emotion));
    }

    private void BeginTransitionTo(Color newTargetColor)
    {
        // Capture wherever the indicator currently is - including
        // mid-transition - as the new start point, so a fast re-trigger
        // doesn't cause a visible jump back to the previous target first.
        transitionStartColor = currentColor;
        targetColor = newTargetColor;
        transitionElapsed = 0f;
        transitioning = true;
    }

    private void Update()
    {
        // Edge-detect "just became fully idle" - revert to default only on
        // the transition FROM having something pending TO having nothing,
        // not continuously every frame while idle (which would keep
        // restarting the transition and it would never finish).
        bool hasPendingJourneys = false;
        if (journeyCalculator != null)
        {
            foreach (System.Collections.Generic.KeyValuePair<string, ActiveJourney> kvp in journeyCalculator.ActiveJourneys)
            {
                if (!kvp.Value.Resolved)
                {
                    hasPendingJourneys = true;
                    break;
                }
            }
        }

        if (hadPendingJourneys && !hasPendingJourneys)
        {
            BeginTransitionTo(colorConfig.DefaultColor);
        }
        hadPendingJourneys = hasPendingJourneys;

        if (transitioning)
        {
            transitionElapsed += Time.deltaTime;
            float duration = colorConfig.TransitionDuration;
            float t = duration > 0f ? Mathf.Clamp01(transitionElapsed / duration) : 1f;

            currentColor = Color.Lerp(transitionStartColor, targetColor, t);

            if (t >= 1f)
            {
                transitioning = false;
            }
        }

        // Saturation and size track live PAD every frame, independent of
        // whether a hue transition is in progress - Pleasure/Dominance can
        // shift between collisions (e.g. via therapist chat), and the
        // indicator should reflect that continuously. Only the base hue
        // (currentColor above) is event-driven - a collision, or just
        // having gone idle.
        PADState pad = SessionManager.Instance != null ? SessionManager.Instance.CurrentPad : null;
        float pleasure = pad != null ? pad.pleasure : 0f;
        float dominance = pad != null ? pad.dominance : 0f;

        Color displayColor = colorConfig.ApplySaturation(currentColor, pleasure);
        ApplyColor(displayColor);

        if (indicatorRenderer != null)
        {
            float size = colorConfig.ComputeIndicatorSize(dominance);
            indicatorRenderer.transform.localScale = Vector3.one * size;
        }
    }

    /// <summary>
    /// Uses a MaterialPropertyBlock rather than mutating
    /// indicatorRenderer.material directly - the latter silently allocates
    /// a new material instance per object and leaks over time. _BaseColor
    /// is URP's color property name (both its Lit and Unlit shaders use
    /// it) - Built-in's Standard shader would need "_Color" instead, so
    /// this specifically assumes URP, per confirmation.
    /// </summary>
    private void ApplyColor(Color color)
    {
        if (indicatorRenderer == null)
        {
            return;
        }

        indicatorRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorPropertyId, color);
        indicatorRenderer.SetPropertyBlock(propertyBlock);
    }
}