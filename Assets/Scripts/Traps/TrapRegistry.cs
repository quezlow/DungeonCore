using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor registry of placed traps. Queried by:
///   - DungeonAdventurer.FollowPath() to detect when an adventurer enters a trap cell
///   - DungeonPathfinder.FindPath() to route around flagged trap cells
///   - Rogue detection logic to enumerate nearby traps
///
/// EXECUTION ORDER
///   -10 — initialises before TrapBase.Awake/Initialise calls in the same frame.
///
/// MULTI-FLOOR
///   Each floor has its own TrapRegistry instance. The static Instance points
///   to whichever floor is currently active; singleton swap is handled via
///   OnEnable/OnDisable when floors toggle active.
/// </summary>
[DefaultExecutionOrder(-10)]
public class TrapRegistry : MonoBehaviour
{
    public static TrapRegistry Instance { get; private set; }

    private readonly Dictionary<Vector3Int, TrapBase> trapsByCell = new();

    /// <summary>Cached set of flagged cells, rebuilt only when flagged state changes.</summary>
    private readonly HashSet<Vector3Int> flaggedCellsCache = new();
    private bool flaggedCacheDirty = true;

    /// <summary>Fires when the set of flagged traps changes — listeners (e.g. pathfinders) can rebuild.</summary>
    public event Action OnFlaggedTrapsChanged;

    // ── Lifecycle ─────────────────────────────────────────────────

    // OnEnable/OnDisable handle singleton registration so it swaps correctly
    // when floors toggle active state (Day 27 multi-floor support).
    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    // ── Registration ──────────────────────────────────────────────

    public void Register(TrapBase trap)
    {
        if (trap == null) return;
        trapsByCell[trap.OccupiedCell] = trap;
    }

    public void Unregister(TrapBase trap)
    {
        if (trap == null) return;
        if (trapsByCell.TryGetValue(trap.OccupiedCell, out var existing) && existing == trap)
        {
            trapsByCell.Remove(trap.OccupiedCell);
            if (trap.IsFlagged) flaggedCacheDirty = true;
        }
    }

    public void NotifyFlaggedChanged()
    {
        flaggedCacheDirty = true;
        OnFlaggedTrapsChanged?.Invoke();
    }

    // ── Queries ───────────────────────────────────────────────────

    public TrapBase GetTrapAt(Vector3Int cell)
    {
        trapsByCell.TryGetValue(cell, out var trap);
        return trap;
    }

    /// <summary>
    /// All currently-flagged trap cells. Used by DungeonPathfinder to route
    /// adventurers around known dangers. Cached internally — cheap to call.
    /// </summary>
    public HashSet<Vector3Int> GetFlaggedCells()
    {
        if (flaggedCacheDirty)
        {
            flaggedCellsCache.Clear();
            foreach (var kvp in trapsByCell)
            {
                if (kvp.Value == null) continue;
                if (!kvp.Value.IsFlagged) continue;

                // Warning traps and pressure plates don't block pathfinding when flagged.
                if (kvp.Value.Definition != null &&
                    (kvp.Value.Definition.behaviour == TrapDefinition.TrapBehaviour.Warning ||
                     kvp.Value.Definition.behaviour == TrapDefinition.TrapBehaviour.PressurePlate))
                    continue;

                flaggedCellsCache.Add(kvp.Key);
            }
            flaggedCacheDirty = false;
        }
        return flaggedCellsCache;
    }

    /// <summary>
    /// All trap cells within radius of a world position, regardless of flagged state.
    /// Used by Rogue detection scans.
    /// </summary>
    public IEnumerable<TrapBase> GetTrapsWithinRadius(Vector3 worldPos, float radius)
    {
        if (TileInfluenceManager.Instance == null) yield break;

        float radiusSq = radius * radius;
        foreach (var kvp in trapsByCell)
        {
            if (kvp.Value == null) continue;
            Vector3 trapWorld = TileInfluenceManager.Instance.CellToWorld(kvp.Key);
            float dx = trapWorld.x - worldPos.x;
            float dy = trapWorld.y - worldPos.y;
            if (dx * dx + dy * dy <= radiusSq) yield return kvp.Value;
        }
    }
}