using UnityEngine;

/// <summary>
/// Shared floor-validity check: is a given world-space point actually
/// sitting on real floor geometry, tagged "Floor"? Checks against each
/// individual Floor-tagged collider's own bounds (not one merged bounding
/// box), so an irregularly-shaped room (e.g. an L-shape assembled from
/// multiple floor pieces) is handled correctly - a point only counts as
/// valid if it falls inside at least one piece's own bounds.
///
/// Factored out as a static utility (rather than living inside
/// ArenaCoverageController or DirtScatterer) so both can share it without
/// duplicating the Floor-tag lookup and bounds-containment logic. See
/// Planning.md, "Cells outside the walls", for the original design
/// reasoning - this is the first actual implementation of that design.
/// ArenaCoverageController's own irregular-shape grid exclusion (still
/// pending) is expected to call this same method once it's built, rather
/// than re-deriving its own version.
///
/// Accuracy note carried over from the original design discussion: this is
/// precise for box/plane-shaped floor pieces. Non-rectangular floor meshes
/// could be mismarked slightly at their edges, since bounds-containment
/// approximates with an axis-aligned box rather than checking actual mesh
/// geometry.
/// </summary>
public static class FloorGeometryUtility
{
    public const string FloorTag = "Floor";

    /// <summary>
    /// Only X/Z are checked - floor pieces can have arbitrary thickness,
    /// and a candidate point's Y is not meaningful to "is this over the
    /// floor".
    /// </summary>
    public static bool IsPointOnFloor(Vector3 worldPoint, Transform floorsAndWalls)
    {
        if (floorsAndWalls == null)
        {
            return false;
        }

        Collider[] colliders = floorsAndWalls.GetComponentsInChildren<Collider>();
        foreach (Collider collider in colliders)
        {
            if (!collider.CompareTag(FloorTag))
            {
                continue;
            }

            Bounds bounds = collider.bounds;
            if (worldPoint.x >= bounds.min.x && worldPoint.x <= bounds.max.x &&
                worldPoint.z >= bounds.min.z && worldPoint.z <= bounds.max.z)
            {
                return true;
            }
        }

        return false;
    }
}
