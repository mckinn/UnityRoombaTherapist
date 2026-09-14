using UnityEngine;

/// <summary>
/// Step 1 of the mapping/coverage work (see Planning.md): builds a
/// CoverageGrid sized to the arena's actual floor extent, computed from the
/// Colliders under the Floors_and_walls hierarchy. No tag is required - the
/// walls are plain untagged BoxColliders (not IsTrigger), so this reads
/// their bounds directly via a hierarchy reference rather than any
/// tag-based lookup. That also means an unrelated untagged object
/// elsewhere in the scene can't accidentally get swept into the bounds
/// calculation, since only children of the assigned Transform are
/// considered.
///
/// Deliberately does NOT yet drive movement or listen for collisions - that
/// integration is steps 3 and 7 of the plan, respectively. Keeping this
/// class narrow means step 1 can be verified in isolation (correct grid
/// dimensions, correct world/cell math) before anything else depends on it
/// behaving correctly.
/// </summary>
public class ArenaCoverageController : MonoBehaviour
{
    private static ArenaCoverageController instance;
    public static ArenaCoverageController Instance => instance;

    [Tooltip("Root of the arena's static geometry (floor and walls). All direct and indirect children's Colliders are encapsulated to compute the arena's floor extent.")]
    [SerializeField] private Transform floorsAndWalls;

    [Tooltip("Cell size and visited-heuristic configuration. Create via Assets > Create > RoombaTherapist > Coverage Grid Config if no asset exists yet.")]
    [SerializeField] private CoverageGridConfig config;

    [Tooltip("Draws the computed grid (Unknown/Visited/Blocked per cell) in the Scene view. A debug aid for verifying step 1 - not gameplay-visible, and safe to leave on or off.")]
    [SerializeField] private bool drawDebugGizmos = true;

    /// <summary>
    /// The built grid. Null until Awake() has run and only if construction
    /// succeeded - callers should treat null as "not ready / failed to
    /// build" rather than assuming it's always populated.
    /// </summary>
    public CoverageGrid Grid { get; private set; }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        BuildGrid();
    }

    private void BuildGrid()
    {
        if (floorsAndWalls == null)
        {
            Debug.LogError("ArenaCoverageController: floorsAndWalls is not assigned - cannot compute arena bounds.");
            return;
        }

        if (config == null)
        {
            Debug.LogError("ArenaCoverageController: config is not assigned - cannot determine cell size.");
            return;
        }

        Collider[] colliders = floorsAndWalls.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            Debug.LogError("ArenaCoverageController: no Colliders found under floorsAndWalls - cannot compute arena bounds.");
            return;
        }

        Bounds arenaBounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
        {
            arenaBounds.Encapsulate(colliders[i].bounds);
        }

        float cellSize = config.CellSize;
        int widthInCells = Mathf.CeilToInt(arenaBounds.size.x / cellSize);
        int depthInCells = Mathf.CeilToInt(arenaBounds.size.z / cellSize);

        Grid = new CoverageGrid(arenaBounds.min, cellSize, widthInCells, depthInCells);

        MarkOffFloorCellsBlocked();

        Debug.Log($"ArenaCoverageController: built {widthInCells} x {depthInCells} cell grid " +
                  $"(cellSize={cellSize:F4}) from arena bounds {arenaBounds.size.x:F2} x {arenaBounds.size.z:F2} " +
                  $"world units, origin {arenaBounds.min}.");
    }

    /// <summary>
    /// Marks any cell whose center doesn't actually fall on real Floor-
    /// tagged geometry as Blocked at construction time - see Planning.md,
    /// "Cells outside the walls". The grid's rectangular extent is the
    /// bounding box of everything under floorsAndWalls (walls included),
    /// but the room itself can be irregularly shaped, so not every cell in
    /// that rectangle is real floor. Reuses FloorGeometryUtility.
    /// IsPointOnFloor - the same check DirtScatterer already uses - so
    /// both systems agree on what counts as floor rather than maintaining
    /// two separate notions of it.
    ///
    /// This directly matters for Clean/Map (step 3): without it, the
    /// coverage planner could select a "nearest Unknown" target that's
    /// actually outside the room (or inside wall thickness), and the
    /// Roomba would push against the wall trying to reach it, with no
    /// collision event ever firing to correct course (walls have no
    /// EntityIdentity and so never raise CollisionController's collision
    /// event, by design). Marking these cells Blocked up front means BFS
    /// never selects them as a target in the first place.
    /// </summary>
    private void MarkOffFloorCellsBlocked()
    {
        int blockedCount = 0;

        for (int x = 0; x < Grid.WidthInCells; x++)
        {
            for (int z = 0; z < Grid.DepthInCells; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                Vector3 worldCenter = Grid.CellToWorldCenter(cell, 0f); // Y is ignored by IsPointOnFloor

                if (!FloorGeometryUtility.IsPointOnFloor(worldCenter, floorsAndWalls))
                {
                    Grid.MarkBlocked(cell);
                    blockedCount++;
                }
            }
        }

        Debug.Log($"ArenaCoverageController: marked {blockedCount} off-floor cell(s) Blocked out of " +
                  $"{Grid.WidthInCells * Grid.DepthInCells} total.");
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos || Grid == null)
        {
            return;
        }

        Vector3 gizmoSize = new Vector3(Grid.CellSize, 0.01f, Grid.CellSize);

        for (int x = 0; x < Grid.WidthInCells; x++)
        {
            for (int z = 0; z < Grid.DepthInCells; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                Vector3 center = Grid.CellToWorldCenter(cell, transform.position.y);

                switch (Grid.GetState(cell))
                {
                    case CoverageGrid.CellState.Visited:
                        Gizmos.color = Color.green;
                        break;
                    case CoverageGrid.CellState.Blocked:
                        Gizmos.color = Color.red;
                        break;
                    default:
                        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
                        break;
                }

                Gizmos.DrawWireCube(center, gizmoSize);
            }
        }
    }
}
