using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BFS-based nearest-unknown-cell coverage planner. See Planning.md,
/// "Coverage Algorithm", for the full design rationale (why BFS, why
/// per-step movement rather than straight-line-to-target, the chair
/// worked example, and the halting-condition reasoning).
///
/// Deliberately separate from CoverageGrid: this is the only
/// algorithm-specific logic in the coverage system. Swapping the coverage
/// strategy later (serpentine, weighted, etc.) means replacing this
/// class's internals - CoverageGrid, and everything that calls
/// GetNextStep, is unaffected.
/// </summary>
public class CoveragePlanner
{
    private readonly CoverageGrid grid;

    public CoveragePlanner(CoverageGrid grid)
    {
        this.grid = grid;
    }

    /// <summary>
    /// Returns the single adjacent cell to move into next, or null if no
    /// Unknown cell is reachable from currentCell (coverage complete, or
    /// this pocket of the arena is fully enclosed by Blocked cells).
    ///
    /// Recomputes a fresh BFS every call - intentionally stateless between
    /// calls (see CleanMapController for why this matters in practice: it
    /// means an interruption, like a real Journey taking over and later
    /// resolving, needs no special "resume" handling anywhere - the next
    /// call just searches fresh from wherever the Roomba now is).
    /// </summary>
    public Vector2Int? GetNextStep(Vector2Int currentCell)
    {
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int?> cameFrom = new Dictionary<Vector2Int, Vector2Int?>();

        frontier.Enqueue(currentCell);
        cameFrom[currentCell] = null;

        Vector2Int? nearestUnknown = null;

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();

            if (grid.GetState(cell) == CoverageGrid.CellState.Unknown && cell != currentCell)
            {
                nearestUnknown = cell;
                break; // BFS visits in hop-count order, so the first Unknown found is nearest.
            }

            foreach (Vector2Int neighbor in grid.GetNeighbors(cell))
            {
                if (cameFrom.ContainsKey(neighbor))
                {
                    continue;
                }
                if (grid.GetState(neighbor) == CoverageGrid.CellState.Blocked)
                {
                    continue;
                }

                cameFrom[neighbor] = cell;
                frontier.Enqueue(neighbor);
            }
        }

        if (nearestUnknown == null)
        {
            return null;
        }

        // Walk backward from the target to find the single first step away
        // from currentCell - this is what makes GetNextStep return "next
        // cell", not "final destination". Movement follows this path one
        // cell at a time; see Planning.md's chair example for why that
        // matters (a straight line to the destination would misattribute
        // an obstacle anywhere along the way to the destination itself).
        Vector2Int step = nearestUnknown.Value;
        while (cameFrom[step] != currentCell)
        {
            step = cameFrom[step].Value;
        }
        return step;
    }
}
