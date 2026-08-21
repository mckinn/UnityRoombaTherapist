using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the procedural mesh for a single dirt patch: a small cluster of
/// randomly-sized, randomly-offset, overlapping flat circles, combined into
/// one mesh so the whole patch is a single draw call rather than one
/// GameObject per circle. Purely visual - knows nothing about trigger
/// detection or removal (see DirtController for that), matching the
/// project's existing split between geometry/data classes and the
/// behavior that consumes them (e.g. CoverageGrid vs CoveragePlanner).
///
/// Deliberately flat (all circle vertices at the same local Y, offset only
/// by config.YOffset) rather than a 3D clump - see Requirements.md, "The
/// Arena", which describes dirt as planar.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DirtVisual : MonoBehaviour
{
    [SerializeField] private DirtConfig config;

    /// <summary>
    /// The generated cluster's measured footprint radius (furthest any
    /// circle's outer edge reaches from the patch's local origin).
    /// </summary>
    public float FootprintRadius { get; private set; }

    /// <summary>
    /// FootprintRadius plus config's collider padding - what
    /// DirtController actually uses to size the trigger collider. Exposed
    /// here so DirtController never needs its own reference to DirtConfig;
    /// it only ever asks DirtVisual "how big should my collider be".
    /// </summary>
    public float ColliderRadius { get; private set; }

    /// <summary>
    /// Assigns the config to use. Needed for runtime-constructed patches
    /// (see DirtScatterer), where there's no Inspector to drag a reference
    /// into before Generate() is called.
    /// </summary>
    public void SetConfig(DirtConfig newConfig)
    {
        config = newConfig;
    }

    /// <summary>
    /// Generates the mesh now. Called explicitly by DirtController.Awake()
    /// rather than relying on this script's own Awake() - keeps generation
    /// order deterministic regardless of Unity's (unspecified) ordering
    /// between components on the same GameObject.
    /// </summary>
    public void Generate()
    {
        if (config == null)
        {
            Debug.LogError("DirtVisual: no DirtConfig assigned - cannot generate.");
            return;
        }

        int circleCount = Random.Range(config.MinCircles, config.MaxCircles + 1);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        float footprintRadius = 0f;

        for (int i = 0; i < circleCount; i++)
        {
            float radius = Random.Range(config.MinCircleRadius, config.MaxCircleRadius);
            Vector2 offset2D = Random.insideUnitCircle * config.MaxCircleOffset;
            Vector3 center = new Vector3(offset2D.x, config.YOffset, offset2D.y);

            AppendCircle(vertices, triangles, center, radius, config.CircleSegments);

            float reach = offset2D.magnitude + radius;
            if (reach > footprintRadius)
            {
                footprintRadius = reach;
            }
        }

        FootprintRadius = footprintRadius;
        ColliderRadius = footprintRadius + config.ColliderPadding;

        Mesh mesh = new Mesh { name = "DirtPatchMesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (config.DirtMaterial != null)
        {
            meshRenderer.sharedMaterial = config.DirtMaterial;
        }
        else
        {
            Debug.LogWarning("DirtVisual: no DirtMaterial assigned in DirtConfig - patch will use Unity's default material.");
        }
    }

    /// <summary>
    /// Appends one triangle-fan circle (center vertex + ring of segment
    /// vertices) to the given shared vertex/triangle lists, offsetting
    /// indices correctly for whatever's already been added.
    ///
    /// WINDING ORDER CAVEAT: this generates triangles in the order
    /// (center, current, next) walking counter-clockwise around the ring
    /// as measured in the XZ plane. Unity's front-face convention can go
    /// either way depending on viewing direction here - if the circles
    /// render invisible (or only visible from below) once you see this in
    /// the Editor, swap the "current" and "next" arguments in the two
    /// triangles.Add calls below. This is a well-known, quick-to-fix Unity
    /// gotcha, not a sign of a deeper problem.
    /// </summary>
    private static void AppendCircle(List<Vector3> vertices, List<int> triangles, Vector3 center, float radius, int segments)
    {
        int centerIndex = vertices.Count;
        vertices.Add(center);

        for (int s = 0; s < segments; s++)
        {
            float angle = 2f * Mathf.PI * s / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            vertices.Add(point);
        }

        for (int s = 0; s < segments; s++)
        {
            int current = centerIndex + 1 + s;
            int next = centerIndex + 1 + (s + 1) % segments;
            triangles.Add(centerIndex);
            triangles.Add(next);
            triangles.Add(current);
        }
    }
}