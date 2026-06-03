using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BFS pathfinding through owned dungeon tiles.
///
/// CHANGES FROM PRE-DAY-27
///   - Primary overload now takes a FloorRoot so pathfinding always uses
///     the correct floor's TileInfluenceManager and TrapRegistry.
///   - Legacy parameterless overload kept but logs an error — callers
///     should be updated to pass a FloorRoot.
/// </summary>
public static class DungeonPathfinder
{
    private static readonly Vector3Int[] Directions =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Primary overload (floor-aware) ────────────────────────────

    /// <param name="floor">The floor the mover currently lives on.</param>
    /// <param name="startWorld">World position of the mover.</param>
    /// <param name="goalWorld">World position of the destination.</param>
    public static List<Vector3> FindPath(FloorRoot floor, Vector3 startWorld, Vector3 goalWorld)
    {
        if (floor == null)
        {
            Debug.LogError("[DungeonPathfinder] FloorRoot is null.");
            return new List<Vector3>();
        }

        var influence = floor.TileInfluence;
        var trapReg = floor.TrapRegistry;

        if (influence == null)
        {
            Debug.LogError($"[DungeonPathfinder] Floor {floor.FloorIndex} has no TileInfluenceManager.");
            return new List<Vector3>();
        }

        return RunBFS(influence, trapReg, startWorld, goalWorld);
    }

    // ── Legacy overload (Floor 1 / non-floor-aware callers) ───────

    /// <summary>
    /// Uses FloorManager.ActiveFloor's influence and trap registry.
    /// Prefer the FloorRoot overload wherever the caller knows its floor.
    /// </summary>
    public static List<Vector3> FindPath(Vector3 startWorld, Vector3 goalWorld)
    {
        var activeFloor = FloorManager.Instance?.ActiveFloor;
        if (activeFloor == null)
        {
            Debug.LogError("[DungeonPathfinder] No active floor — cannot pathfind.");
            return new List<Vector3>();
        }
        return FindPath(activeFloor, startWorld, goalWorld);
    }

    // ── BFS core ─────────────────────────────────────────────────

    private static List<Vector3> RunBFS(
        TileInfluenceManager influence,
        TrapRegistry trapReg,
        Vector3 startWorld,
        Vector3 goalWorld)
    {
        Vector3Int start = influence.WorldToCell(startWorld);
        Vector3Int goal = influence.WorldToCell(goalWorld);

        if (start == goal) return new List<Vector3>();

        var blocked = trapReg?.GetFlaggedCells();

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