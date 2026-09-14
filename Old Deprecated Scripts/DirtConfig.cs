using UnityEngine;

/// <summary>
/// Tunable configuration for how a single dirt patch is generated: the
/// flat, irregular cluster of overlapping circles that makes up its visual
/// mesh, and how much collider padding surrounds that cluster. Follows the
/// same ScriptableObject-config pattern as EmotionMovementConfig and
/// CoverageGridConfig.
///
/// Scatter-level settings (how many patches, arena-wide) intentionally
/// live on DirtScatterer itself rather than here - this config describes
/// what ONE dirt patch looks like, not how many exist.
///
/// The actual asset instance belongs in Configurators/, alongside the
/// project's other config assets - create it via
/// Assets > Create > RoombaTherapist > Dirt Config once this script has
/// compiled.
/// </summary>
[CreateAssetMenu(fileName = "DirtConfig", menuName = "RoombaTherapist/Dirt Config")]
public class DirtConfig : ScriptableObject
{
    [Header("Cluster Shape")]
    [Tooltip("Minimum number of overlapping circles per dirt patch.")]
    [SerializeField] private int minCircles = 4;

    [Tooltip("Maximum number of overlapping circles per dirt patch (inclusive).")]
    [SerializeField] private int maxCircles = 7;

    [Tooltip("Minimum radius of an individual circle, in world units.")]
    [SerializeField] private float minCircleRadius = 0.05f;

    [Tooltip("Maximum radius of an individual circle, in world units.")]
    [SerializeField] private float maxCircleRadius = 0.12f;

    [Tooltip("Maximum distance an individual circle's center can be offset from the patch's own local origin. Keep small relative to circle radius so circles overlap into a clustered blob rather than scattering apart into disconnected dots.")]
    [SerializeField] private float maxCircleOffset = 0.08f;

    [Tooltip("Number of triangle-fan segments per circle. Higher is smoother but costs more vertices - this is a small, low-detail shape seen mostly from above, so a low segment count is appropriate.")]
    [SerializeField] private int circleSegments = 8;

    [Header("Placement")]
    [Tooltip("Small vertical offset above the floor, to avoid Z-fighting with the floor plane while staying visually flat.")]
    [SerializeField] private float yOffset = 0.005f;

    [Header("Collider")]
    [Tooltip("Extra padding added to the visual cluster's measured footprint radius when sizing the trigger collider, so the trigger reliably covers the visible mesh rather than clipping it.")]
    [SerializeField] private float colliderPadding = 0.05f;

    [Header("Appearance")]
    [Tooltip("Shared material for all dirt patches. A single muted material is enough for v1 - no per-instance texture needed.")]
    [SerializeField] private Material dirtMaterial;

    public int MinCircles => minCircles;
    public int MaxCircles => maxCircles;
    public float MinCircleRadius => minCircleRadius;
    public float MaxCircleRadius => maxCircleRadius;
    public float MaxCircleOffset => maxCircleOffset;
    public int CircleSegments => circleSegments;
    public float YOffset => yOffset;
    public float ColliderPadding => colliderPadding;
    public Material DirtMaterial => dirtMaterial;
}
