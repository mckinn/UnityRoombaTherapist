using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure data structure for the arena floor coverage grid. No Unity
/// lifecycle, no MonoBehaviour - a CoverageGrid can be constructed, queried,
/// and mutated entirely in isolation, since it holds nothing but grid
/// geometry and cell states.
///
/// Coordinates: cells are indexed by Vector2Int over the arena's X/Z plane
/// (Y is not part of the grid - the Roomba's normal operation is
/// plane-constrained; see Requirements.md, "The Arena"). The origin is the
/// arena's world-space minimum corner (min X, min Z), so cell (0,0) is the
/// grid's corner nearest that minimum - not necessarily anything
/// gameplay-meaningful on its own.
///
/// This class knows nothing about BFS, movement, or collision. See
/// Planning.md, "Coverage Algorithm" (step 3) for the search algorithm that
/// will consume this grid, and (step 1, this file's own step) for the
/// Unity-side glue that builds it from actual arena geometry. Kept
/// deliberately separate so the search algorithm - and eventually this
/// grid's own internal representation - can be changed without touching
/// this file or its callers.
/// </summary>
public class CoverageGrid
{
    public enum CellState
    {
        Unknown,
        Visited,
        Blocked
    }

    private readonly CellState[,] cells;
    private readonly Vector3 worldOrigin; // world-space min corner; X and Z are used, Y is carried but not meaningful to the grid itself

    public float CellSize { get; }
    public int WidthInCells { get; }
    public int DepthInCells { get; }

    /// <summary>
    /// Builds a grid covering [worldOrigin, worldOrigin + (widthInCells, 0, depthInCells) * cellSize].
    /// Callers (ArenaCoverageController) are responsible for computing
    /// worldOrigin and the cell counts from actual arena geometry - this
    /// constructor only allocates and initializes every cell to Unknown.
    /// </summary>
    public CoverageGrid(Vector3 worldOrigin, float cellSize, int widthInCells, int depthInCells)
    {
        this.worldOrigin = worldOrigin;
        CellSize = cellSize;
        WidthInCells = widthInCells;
        DepthInCells = depthInCells;

        // CellState.Unknown is the enum's zero value, so `new CellState[,]`
        // already initializes every cell correctly without an explicit
        // loop. Called out here because relying on "default == Unknown" is
        // an easy thing to silently break later if this enum's declared
        // order is ever reshuffled - if that happens, this comment (and the
        // enum's ordering) needs re-checking together.
        cells = new CellState[widthInCells, depthInCells];
    }

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < WidthInCells && cell.y >= 0 && cell.y < DepthInCells;
    }

    /// <summary>
    /// Anything outside the grid's bounds is treated as Blocked - the arena
    /// wall is the reason those cells don't exist, so "impassable" is the
    /// correct answer for them, not a special "out of bounds" case that
    /// every caller would otherwise need to check for separately.
    /// </summary>
    public CellState GetState(Vector2Int cell)
    {
        if (!IsInBounds(cell))
        {
            return CellState.Blocked;
        }
        return cells[cell.x, cell.y];
    }

    public void MarkVisited(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return;

        // Blocked is a stronger, collision-verified fact than Visited and
        // should never be downgraded by a later visit.
        if (cells[cell.x, cell.y] == CellState.Blocked) return;

        cells[cell.x, cell.y] = CellState.Visited;
    }

    public void MarkBlocked(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return;
        cells[cell.x, cell.y] = CellState.Blocked;
    }

    /// <summary>
    /// Converts a world-space position (Y ignored) to the cell containing it.
    /// </summary>
    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - worldOrigin.x) / CellSize);
        int z = Mathf.FloorToInt((worldPosition.z - worldOrigin.z) / CellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// World-space center point of the given cell, at the given Y height.
    /// The grid itself has no opinion on Y - callers supply whatever height
    /// they want (typically the Roomba's current Y position).
    /// </summary>
    public Vector3 CellToWorldCenter(Vector2Int cell, float y)
    {
        float worldX = worldOrigin.x + (cell.x + 0.5f) * CellSize;
        float worldZ = worldOrigin.z + (cell.y + 0.5f) * CellSize;
        return new Vector3(worldX, y, worldZ);
    }

    /// <summary>
    /// 4-connected neighbors within bounds. Exposed here rather than inside
    /// the (not-yet-written) search algorithm because adjacency is a
    /// grid-geometry fact, not a search fact, and belongs with the data it
    /// describes. Switching to 8-connected (diagonals) later is a one-line
    /// change to the offsets array below - noted as a deliberately deferred,
    /// low-stakes tuning choice, not a design decision (see Planning.md).
    /// </summary>
    public IEnumerable<Vector2Int> GetNeighbors(Vector2Int cell)
    {
        Vector2Int[] offsets =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        foreach (Vector2Int offset in offsets)
        {
            Vector2Int neighbor = cell + offset;
            if (IsInBounds(neighbor))
            {
                yield return neighbor;
            }
        }
    }
}
