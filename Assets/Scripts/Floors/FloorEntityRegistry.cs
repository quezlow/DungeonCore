using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor index of dynamic entities. Access via FloorRoot.Entities.
///
/// PATTERN
///   - One instance per FloorRoot, wired via FloorRoot.entities SerializeField.
///   - Entities call Register(this) from their Initialise() or Start(), and
///     Unregister(this) from OnDestroy().
///   - Floor-traversing entities (DungeonAdventurer, future Avatar) unregister
///     from the old floor's registry and re-register on the new one as part
///     of their stair-traversal flow.
///
/// INHERITANCE INDEXING
///   Register walks the runtime type chain up to (but not including) MonoBehaviour.
///   So a SpikeTrap registers in BOTH the SpikeTrap bucket AND the TrapBase
///   bucket. Queries against base types return every subclass. Each query
///   reads exactly one bucket; instances never appear twice in a single query.
///
/// CELL INDEX
///   Entities implementing IFloorEntity also register in a Dictionary<Vector3Int, ...>
///   keyed by their OccupiedCell. Use GetAtCell<T> for cell lookups.
///
/// PERFORMANCE
///   Per-floor entity counts are small (10s, low 100s). All queries are linear
///   scans of the relevant bucket. No spatial hashing.
/// </summary>
[DefaultExecutionOrder(-10)]
public class FloorEntityRegistry : MonoBehaviour
{
    private static readonly Type StopAt = typeof(MonoBehaviour);

    private readonly Dictionary<Type, List<Component>> byType = new();
    private readonly Dictionary<Vector3Int, List<IFloorEntity>> byCell = new();

    private FloorRoot floor;
    public FloorRoot Floor => floor;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
            Debug.LogError($"[FloorEntityRegistry] No FloorRoot in parent of '{name}'.");
    }

    // ── Registration ──────────────────────────────────────────────

    public void Register(Component entity)
    {
        if (entity == null) return;

        for (var t = entity.GetType(); t != null && t != StopAt; t = t.BaseType)
        {
            if (!byType.TryGetValue(t, out var list))
                byType[t] = list = new List<Component>();
            if (!list.Contains(entity)) list.Add(entity);
        }

        if (entity is IFloorEntity ife)
        {
            var cell = ife.OccupiedCell;
            if (!byCell.TryGetValue(cell, out var cellList))
                byCell[cell] = cellList = new List<IFloorEntity>();
            if (!cellList.Contains(ife)) cellList.Add(ife);
        }
    }

    public void Unregister(Component entity)
    {
        if (entity == null) return;

        for (var t = entity.GetType(); t != null && t != StopAt; t = t.BaseType)
        {
            if (byType.TryGetValue(t, out var list))
                list.Remove(entity);
        }

        if (entity is IFloorEntity ife)
        {
            if (byCell.TryGetValue(ife.OccupiedCell, out var cellList))
            {
                cellList.Remove(ife);
                if (cellList.Count == 0) byCell.Remove(ife.OccupiedCell);
            }
        }
    }

    // ── Queries: enumerate ───────────────────────────────────────

    /// <summary>Snapshot list of all live entities of type T. Allocates a new list.</summary>
    public List<T> GetAll<T>() where T : Component
    {
        var result = new List<T>();
        FillAll(result);
        return result;
    }

    /// <summary>Zero-allocation fill of a caller-provided buffer.</summary>
    public int FillAll<T>(List<T> outBuf) where T : Component
    {
        outBuf.Clear();
        if (!byType.TryGetValue(typeof(T), out var list)) return 0;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c == null) continue; // destroyed-but-not-yet-unregistered safety
            outBuf.Add((T)c);
        }
        return outBuf.Count;
    }

    /// <summary>Live count of type T. Skips destroyed-but-not-yet-unregistered entries.</summary>
    public int Count<T>() where T : Component
    {
        if (!byType.TryGetValue(typeof(T), out var list)) return 0;
        int n = 0;
        for (int i = 0; i < list.Count; i++) if (list[i] != null) n++;
        return n;
    }

    // ── Queries: spatial ─────────────────────────────────────────

    /// <summary>Nearest T to pos within maxRadius (default infinite). Returns null if none.</summary>
    public T Nearest<T>(Vector3 pos, float maxRadius = float.MaxValue,
                       Predicate<T> filter = null) where T : Component
    {
        if (!byType.TryGetValue(typeof(T), out var list)) return null;
        T best = null;
        float bestDistSq = maxRadius * maxRadius;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i] as T;
            if (c == null) continue;
            if (filter != null && !filter(c)) continue;
            float d = ((Vector2)(c.transform.position - pos)).sqrMagnitude;
            if (d < bestDistSq) { bestDistSq = d; best = c; }
        }
        return best;
    }

    /// <summary>Fill outBuf with every T within radius of pos. Returns count.</summary>
    public int WithinRadius<T>(Vector3 pos, float radius, List<T> outBuf,
                              Predicate<T> filter = null) where T : Component
    {
        outBuf.Clear();
        if (!byType.TryGetValue(typeof(T), out var list)) return 0;
        float r2 = radius * radius;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i] as T;
            if (c == null) continue;
            if (filter != null && !filter(c)) continue;
            if (((Vector2)(c.transform.position - pos)).sqrMagnitude <= r2)
                outBuf.Add(c);
        }
        return outBuf.Count;
    }

    /// <summary>True if any T exists within radius of pos. Cheap early-out.</summary>
    public bool AnyWithinRadius<T>(Vector3 pos, float radius,
                                  Predicate<T> filter = null) where T : Component
    {
        if (!byType.TryGetValue(typeof(T), out var list)) return false;
        float r2 = radius * radius;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i] as T;
            if (c == null) continue;
            if (filter != null && !filter(c)) continue;
            if (((Vector2)(c.transform.position - pos)).sqrMagnitude <= r2)
                return true;
        }
        return false;
    }

    // ── Queries: by cell ─────────────────────────────────────────

    /// <summary>First T at the given cell, or null. Requires T : IFloorEntity.</summary>
    public T GetAtCell<T>(Vector3Int cell) where T : Component
    {
        if (!byCell.TryGetValue(cell, out var list)) return null;
        for (int i = 0; i < list.Count; i++)
            if (list[i] is T t) return t;
        return null;
    }

    /// <summary>Fill outBuf with every T at the given cell. Returns count.</summary>
    public int FillAtCell<T>(Vector3Int cell, List<T> outBuf) where T : Component
    {
        outBuf.Clear();
        if (!byCell.TryGetValue(cell, out var list)) return 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i] is T t) outBuf.Add(t);
        return outBuf.Count;
    }
}