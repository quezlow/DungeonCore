using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weighted pathfinding through owned dungeon tiles, with river fording.
///
/// DAY 27
///   Primary overload takes a FloorRoot so pathfinding always uses the correct
///   floor's TileInfluenceManager and TrapRegistry.
///
/// DAY 31 PART 1 — UPGRADED
///   BFS replaced with Dijkstra (weighted uniform-cost search). Default cell
///   cost is 1; river cells cost TerrainFeatureGenerator.RiverPathCost (default
///   5 — equivalent to "going around 4 normal tiles is preferred to crossing
///   1 river tile"). River cells are TRAVERSABLE EVEN WHEN NOT OWNED — that's
///   the fording mechanic. Chamber cells follow the normal owned/unowned rule;
///   Part 2 will add the wild-monster claim gate on chambers.
///
///   .NET 6's PriorityQueue is unavailable in Unity's .NET Standard 2.1
///   runtime, so we ship a minimal binary min-heap inline.
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
        var features = floor.FeatureGenerator;

        if (influence == null)
        {
            Debug.LogError($"[DungeonPathfinder] Floor {floor.FloorIndex} has no TileInfluenceManager.");
            return new List<Vector3>();
        }

        int riverCost = features != null ? features.RiverPathCost : 1;
        return RunDijkstra(influence, trapReg, features, riverCost, startWorld, goalWorld);
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

    // ── Dijkstra core ────────────────────────────────────────────

    private static List<Vector3> RunDijkstra(
        TileInfluenceManager influence,
        TrapRegistry trapReg,
        TerrainFeatureGenerator features,
        int riverCost,
        Vector3 startWorld,
        Vector3 goalWorld)
    {
        Vector3Int start = influence.WorldToCell(startWorld);
        Vector3Int goal = influence.WorldToCell(goalWorld);

        if (start == goal) return new List<Vector3>();

        var blocked = trapReg?.GetFlaggedCells();

        var heap = new MinHeap();
        var costSoFar = new Dictionary<Vector3Int, int>();
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>();

        heap.Push(0, start);
        costSoFar[start] = 0;
        cameFrom[start] = start;

        while (heap.Count > 0)
        {
            Vector3Int current = heap.Pop();

            if (current == goal)
                return ReconstructPath(cameFrom, start, goal, influence);

            foreach (var dir in Directions)
            {
                Vector3Int next = current + dir;

                if (blocked != null && blocked.Contains(next) && next != goal) continue;

                bool owned = influence.IsTileMined(next);
                bool isRiver = features != null && features.IsRiver(next);
                bool passable = owned || isRiver;

                if (!passable && next != goal) continue;

                int stepCost = isRiver ? riverCost : 1;
                int newCost = costSoFar[current] + stepCost;

                if (!costSoFar.TryGetValue(next, out int existingCost) || newCost < existingCost)
                {
                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    heap.Push(newCost, next);
                }
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

    // ── Binary Min-Heap (priority queue) ──────────────────────────
    //
    // .NET 6's System.Collections.Generic.PriorityQueue<TElement, TPriority>
    // is unavailable in Unity's .NET Standard 2.1 runtime, so we ship a minimal
    // implementation here. Stable for our use case (paths under a few thousand
    // nodes per call).

    private class MinHeap
    {
        private readonly List<(int priority, Vector3Int cell)> data = new();

        public int Count => data.Count;

        public void Push(int priority, Vector3Int cell)
        {
            data.Add((priority, cell));
            int i = data.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (data[parent].priority <= data[i].priority) break;
                (data[parent], data[i]) = (data[i], data[parent]);
                i = parent;
            }
        }

        public Vector3Int Pop()
        {
            var top = data[0].cell;
            int last = data.Count - 1;
            data[0] = data[last];
            data.RemoveAt(last);
            last--;
            int i = 0;
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left <= last && data[left].priority < data[smallest].priority) smallest = left;
                if (right <= last && data[right].priority < data[smallest].priority) smallest = right;
                if (smallest == i) break;
                (data[smallest], data[i]) = (data[i], data[smallest]);
                i = smallest;
            }
            return top;
        }
    }
}