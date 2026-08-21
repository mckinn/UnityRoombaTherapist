using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Scatters a target number of dirt patches across valid floor space.
/// Self-contained: builds each patch as a fresh GameObject at runtime
/// (MeshFilter, MeshRenderer, DirtVisual, BoxCollider, DirtController all
/// added via AddComponent) rather than requiring a hand-built prefab - one
/// less manual Editor step, and DirtController's own Awake() handles its
/// setup identically whether it was placed via a prefab or built this way.
///
/// ScatterDirt() is public and safe to call more than once - each call
/// clears any patches left over from a previous call before placing fresh
/// ones, and OnScatterComplete fires again with the new count. This is
/// what would let a future "reset for another run" feature just call
/// ScatterDirt() again rather than needing its own placement logic - not
/// implemented yet, but the door is left open cheaply. Start() calls it
/// once automatically at scene load.
///
/// OnScatterComplete fires from Start(), not Awake() - see
/// DirtProgressReporter, the current subscriber, for why that split
/// matters (Awake-before-Start is a real Unity guarantee; Awake-before-
/// another-component's-Awake is not).
///
/// Placement validity deliberately does NOT query CoverageGrid /
/// ArenaCoverageController - see the "last open design point" discussion
/// in this project's history: the grid only knows a cell is occupied AFTER
/// the Roomba discovers it by collision, so it can't tell scatter
/// placement about furniture that hasn't been bumped into yet. A direct
/// collider overlap check (below) is the only thing that actually knows
/// that at setup time, and this also avoids any script-execution-order
/// dependency between DirtScatterer and ArenaCoverageController.
///
/// Occupancy checking uses tags, not physics layers - consistent with how
/// the rest of this project distinguishes entities ("Player", "Floor",
/// entity-type tags), rather than introducing a new, separately-configured
/// LayerMask that would need its own manual Editor setup.
/// </summary>
public class DirtScatterer : MonoBehaviour
{
    [Tooltip("Root of the arena's static geometry - same reference ArenaCoverageController uses, reused here for the Floor-tag validity check and for computing the arena's overall bounds.")]
    [SerializeField] private Transform floorsAndWalls;

    [SerializeField] private DirtConfig dirtConfig;

    [Header("Scatter Settings")]
    [Tooltip("How many dirt patches to attempt placing.")]
    [SerializeField] private int targetDirtCount = 30;

    [Tooltip("Maximum attempts per patch before giving up on placing it. Prevents an infinite loop if the arena is small/cluttered enough that valid points become hard to find.")]
    [SerializeField] private int maxAttemptsPerPatch = 20;

    [Tooltip("Radius used for the overlap check against nearby colliders when validating a candidate point. Anything found within this radius that isn't tagged with one of occupancyIgnoreTags counts as an obstruction (furniture, walls, or an already-placed dirt patch).")]
    [SerializeField] private float overlapCheckRadius = 0.15f;

    [Tooltip("Tags that never count as an obstruction during the occupancy check, even though they're not necessarily walkable floor. 'Floor' is here because standing on the floor is expected everywhere. Add tags for purely structural/non-interactive objects here too (e.g. a backing plate underneath the visible floor tiles) - those shouldn't block placement, but also shouldn't be tagged 'Floor' itself, since that tag is separately used to bound the room's actual walkable shape (see FloorGeometryUtility) and giving an oversized structural object that tag would incorrectly widen that boundary.")]
    [SerializeField] private string[] occupancyIgnoreTags = { FloorGeometryUtility.FloorTag };

    [Tooltip("Small height above the detected floor surface at which candidate points are sampled and occupancy is checked - not exactly at the surface itself, to comfortably clear the floor collider's own thin slab and actually reach into the space where furniture/obstructions would be.")]
    [SerializeField] private float sampleHeightAboveFloor = 0.05f;

    [Tooltip("Small height above the detected floor surface at which dirt patches are actually placed (distinct from sampleHeightAboveFloor, which only affects the occupancy check, not final placement). Needed because some Floor-tagged surfaces sit slightly above the base floor height this system computes (e.g. a thin rug) - a pragmatic fixed offset rather than computing true per-point floor height. If a taller thin surface is added later and dirt starts disappearing under it again, this value needs raising, or the underlying computation needs to become per-point instead of arena-wide.")]
    [SerializeField] private float placementHeightOffset = 0.01f;

    private bool loggedDiagnosticDetail = false;
    private readonly List<GameObject> spawnedPatches = new List<GameObject>();

    /// <summary>
    /// How many patches are placed as of the most recent ScatterDirt() call.
    /// </summary>
    public int PlacedCount { get; private set; }

    /// <summary>
    /// Fired at the end of every ScatterDirt() call (including the initial
    /// automatic one from Start()), with the new PlacedCount. Subscribe in
    /// your own Awake() - see class summary for why that ordering matters.
    /// </summary>
    public event Action<int> OnScatterComplete;

    private void Start()
    {
        ScatterDirt();
    }

    /// <summary>
    /// Clears any patches left over from a previous call, then places up to
    /// targetDirtCount fresh ones and fires OnScatterComplete. Safe to call
    /// more than once.
    /// </summary>
    public void ScatterDirt()
    {
        if (floorsAndWalls == null || dirtConfig == null)
        {
            Debug.LogError("DirtScatterer: floorsAndWalls or dirtConfig not assigned - cannot scatter.");
            return;
        }

        ClearExistingPatches();

        Bounds arenaBounds = ComputeArenaBounds();
        LogFloorTagDiagnostics();
        float floorSurfaceY = ComputeFloorSurfaceY();

        for (int i = 0; i < targetDirtCount; i++)
        {
            if (TryFindValidPoint(arenaBounds, floorSurfaceY, out Vector3 point))
            {
                SpawnDirtPatch(point);
                PlacedCount++;
            }
        }

        Debug.Log($"DirtScatterer: placed {PlacedCount} of {targetDirtCount} requested dirt patches.");
        OnScatterComplete?.Invoke(PlacedCount);
    }

    private void ClearExistingPatches()
    {
        foreach (GameObject patch in spawnedPatches)
        {
            if (patch != null)
            {
                Destroy(patch);
            }
        }
        spawnedPatches.Clear();
        PlacedCount = 0;
    }

    /// <summary>
    /// The real floor's top surface height, derived from the Floor-tagged
    /// colliders' own bounds rather than from the arena's overall combined
    /// bounds. This matters when other geometry (e.g. a structural backing
    /// plate sitting below the visible floor) drags the overall minimum Y
    /// well below where the floor - and anything actually resting on it -
    /// really is. Assumes a roughly flat floor across the room; if a future
    /// arena has floor pieces at meaningfully different heights, this would
    /// need to become per-point rather than one arena-wide value.
    /// </summary>
    private float ComputeFloorSurfaceY()
    {
        Collider[] colliders = floorsAndWalls.GetComponentsInChildren<Collider>();
        float maxY = float.NegativeInfinity;
        Collider maxCollider = null;

        foreach (Collider collider in colliders)
        {
            if (!collider.CompareTag(FloorGeometryUtility.FloorTag))
            {
                continue;
            }
            if (collider.bounds.max.y > maxY)
            {
                maxY = collider.bounds.max.y;
                maxCollider = collider;
            }
        }

        if (maxCollider == null)
        {
            Debug.LogWarning("DirtScatterer: could not compute floor surface height (no Floor-tagged colliders) - falling back to arena bounds minimum, which may be inaccurate.");
            return ComputeArenaBounds().min.y;
        }

        Debug.Log($"DirtScatterer: floor surface Y = {maxY:F3}, determined by collider '{maxCollider.gameObject.name}'. If this doesn't match the real floor height, that GameObject is likely mistagged 'Floor'.");
        return maxY;
    }

    private Bounds ComputeArenaBounds()
    {
        Collider[] colliders = floorsAndWalls.GetComponentsInChildren<Collider>();
        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
        {
            bounds.Encapsulate(colliders[i].bounds);
        }
        return bounds;
    }

    /// <summary>
    /// One-time startup diagnostic: how many colliders exist under
    /// floorsAndWalls in total, and how many of those are actually tagged
    /// "Floor"? If the second number is 0, every scatter candidate will
    /// silently fail FloorGeometryUtility.IsPointOnFloor - this makes that
    /// failure mode visible instead of manifesting only as "placed 0 of N"
    /// with no further explanation.
    /// </summary>
    private void LogFloorTagDiagnostics()
    {
        Collider[] colliders = floorsAndWalls.GetComponentsInChildren<Collider>();
        int floorTagged = 0;
        foreach (Collider collider in colliders)
        {
            if (collider.CompareTag(FloorGeometryUtility.FloorTag))
            {
                floorTagged++;
            }
        }

        if (floorTagged == 0)
        {
            Debug.LogError($"DirtScatterer: found {colliders.Length} total colliders under floorsAndWalls, but NONE are tagged \"{FloorGeometryUtility.FloorTag}\". Every scatter attempt will fail the floor-validity check. Verify the tag is applied directly to the GameObjects that hold the Collider components, not to a parent.");
        }
        else
        {
            Debug.Log($"DirtScatterer: found {floorTagged} Floor-tagged collider(s) out of {colliders.Length} total under floorsAndWalls.");
        }
    }

    private bool TryFindValidPoint(Bounds arenaBounds, float floorSurfaceY, out Vector3 result)
    {
        int floorCheckFailures = 0;
        int occupancyFailures = 0;
        float sampleY = floorSurfaceY + sampleHeightAboveFloor;

        for (int attempt = 0; attempt < maxAttemptsPerPatch; attempt++)
        {
            float x = Random.Range(arenaBounds.min.x, arenaBounds.max.x);
            float z = Random.Range(arenaBounds.min.z, arenaBounds.max.z);
            Vector3 candidate = new Vector3(x, sampleY, z);

            if (!FloorGeometryUtility.IsPointOnFloor(candidate, floorsAndWalls))
            {
                floorCheckFailures++;
                continue;
            }

            if (IsPositionOccupied(candidate))
            {
                occupancyFailures++;
                continue;
            }

            result = new Vector3(x, floorSurfaceY + placementHeightOffset, z);
            return true;
        }

        if (!loggedDiagnosticDetail)
        {
            loggedDiagnosticDetail = true;
            Debug.LogWarning($"DirtScatterer: first full placement failure - floor surface Y = {floorSurfaceY:F3}, sampled candidate.y = {sampleY:F3}. Of {maxAttemptsPerPatch} attempts: {floorCheckFailures} failed the floor check, {occupancyFailures} failed the occupancy check.");

            if (occupancyFailures > 0)
            {
                // Re-run one candidate purely to report exactly what it hit.
                float x = Random.Range(arenaBounds.min.x, arenaBounds.max.x);
                float z = Random.Range(arenaBounds.min.z, arenaBounds.max.z);
                Vector3 probe = new Vector3(x, sampleY, z);
                Collider[] hits = Physics.OverlapSphere(probe, overlapCheckRadius);
                string hitList = hits.Length == 0 ? "(none)" : string.Join(", ", System.Array.ConvertAll(hits, h => $"{h.gameObject.name} [tag={h.tag}]"));
                Debug.LogWarning($"DirtScatterer: probe at {probe} (may or may not itself be the failing point, but same y) found colliders: {hitList}");
            }
        }

        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Anything nearby that isn't tagged with one of occupancyIgnoreTags
    /// counts as an obstruction - standing on the floor itself is expected
    /// for every valid point, and purely structural/non-interactive objects
    /// (see occupancyIgnoreTags) shouldn't block placement either, so both
    /// are deliberately excluded rather than causing every candidate near
    /// them to fail. Walls are untagged (see project history) and so
    /// correctly fall through to "occupied" without needing special-casing.
    /// Already-placed dirt patches' own trigger colliders are picked up by
    /// OverlapSphere too, which gives a natural minimum spacing between
    /// patches as a side effect, not something explicitly coded for.
    /// </summary>
    private bool IsPositionOccupied(Vector3 candidate)
    {
        Collider[] hits = Physics.OverlapSphere(candidate, overlapCheckRadius);
        foreach (Collider hit in hits)
        {
            bool ignored = false;
            foreach (string tag in occupancyIgnoreTags)
            {
                if (hit.CompareTag(tag))
                {
                    ignored = true;
                    break;
                }
            }
            if (ignored)
            {
                continue;
            }
            return true;
        }
        return false;
    }

    private void SpawnDirtPatch(Vector3 position)
    {
        GameObject patch = new GameObject("DirtPatch");
        patch.transform.position = position;
        patch.transform.SetParent(transform);
        spawnedPatches.Add(patch);

        patch.AddComponent<MeshFilter>();
        patch.AddComponent<MeshRenderer>();

        DirtVisual visual = patch.AddComponent<DirtVisual>();
        visual.SetConfig(dirtConfig);

        patch.AddComponent<BoxCollider>();

        // DirtController is added last, deliberately - its Awake() reads
        // DirtVisual and BoxCollider and expects both to already be present
        // and (for DirtVisual) already configured. AddComponent triggers
        // Awake() synchronously, so ordering here is what guarantees
        // Generate() runs with a valid config rather than a null one.
        patch.AddComponent<DirtController>();
    }
}