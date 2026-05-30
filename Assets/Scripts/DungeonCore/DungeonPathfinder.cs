using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BFS pathfinding through owned dungeon tiles.
/// Returns a list of world-space positions (cell centres) from start to goal.
/// Returns an empty list if no path exists.
/// </summary>
public static class DungeonPathfinder
{
    private static readonly Vector3Int[] Directions =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    /// <param name="startWorld">World position of the mover.</param>
    /// <param name="goalWorld">World position of the destination.</param>
    public static List<Vector3> FindPath(Vector3 startWorld, Vector3 goalWorld)
    {
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return new List<Vector3>();

        Vector3Int start = influence.WorldToCell(startWorld);
        Vector3Int goal = influence.WorldToCell(goalWorld);

        if (start == goal) return new List<Vector3>();

        // NEW: pull flagged cells once per pathfind call (cheap — cached in TrapRegistry).
        var blocked = TrapRegistry.Instance != null
            ? TrapRegistry.Instance.GetFlaggedCells()
            : null;

        var frontier = new Queue<Vector3Int>();
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>();

        frontier.Enqueue(start);
        cameFrom[start] = start;

        while (frontier.Count > 0)
        {
            Vector3Int current = frontier.Dequeue();

            if (current == goal)
                return ReconstructPath(cameFrom, start, goal, influence);

            foreach (var dir in Directions)
            {
                Vector3Int next = current + dir;
                if (cameFrom.ContainsKey(next)) continue;

                // NEW: skip flagged-trap cells unless the goal IS that cell.
                if (blocked != null && blocked.Contains(next) && next != goal) continue;

                if (!influence.IsTileOwned(next) && next != goal) continue;

                frontier.Enqueue(next);
                cameFrom[next] = current;
            }
        }

        return new List<Vector3>();
    }


    private static List<Vector3> ReconstructPath(
        Dictionary<Vector3Int, Vector3Int> cameFrom,
        Vector3Int start, Vector3Int goal,
        TileInfluenceManager influence)
    {
        var cells = new List<Vector3Int>();
        Vector3Int current = goal;

        while (current != start)
        {
            cells.Add(current);
            current = cameFrom[current];
        }

        cells.Reverse();

        var worldPath = new List<Vector3>(cells.Count);
        foreach (var cell in cells)
            worldPath.Add(influence.CellToWorld(cell));

        return worldPath;
    }
}
