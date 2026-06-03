using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor trap registry. No longer a singleton.
/// Access via FloorRoot.TrapRegistry.
///
/// CHANGES FROM PRE-DAY-27
///   - Static Instance removed.
///   - GetTrapsWithinRadius now takes a TileInfluenceManager parameter
///     instead of using TileInfluenceManager.Instance.
/// </summary>
[DefaultExecutionOrder(-10)]
public class TrapRegistry : MonoBehaviour
{
    public static TrapRegistry Instance { get; private set; }

    private readonly Dictionary<Vector3Int, TrapBase> trapsByCell = new();
    private readonly HashSet<Vector3Int> flaggedCellsCache = new();
    private bool flaggedCacheDirty = true;

    public event Action OnFlaggedTrapsChanged;

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

    public HashSet<Vector3Int> GetFlaggedCells()
    {
        if (flaggedCacheDirty)
        {
            flaggedCellsCache.Clear();
            foreach (var kvp in trapsByCell)
            {
                if (kvp.Value == null) continue;
                if (!kvp.Value.IsFlagged) continue;
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
    /// All traps within world-space radius. Requires the floor's
    /// TileInfluenceManager for cell→world conversion.
    /// </summary>
    public IEnumerable<TrapBase> GetTrapsWithinRadius(
        Vector3 worldPos, float radius, TileInfluenceManager influence)
    {
        if (influence == null) yield break;

        float radiusSq = radius * radius;
        foreach (var kvp in trapsByCell)
        {
            if (kvp.Value == null) continue;
            Vector3 trapWorld = influence.CellToWorld(kvp.Key);
            float dx = trapWorld.x - worldPos.x;
            float dy = trapWorld.y - worldPos.y;
            if (dx * dx + dy * dy <= radiusSq) yield return kvp.Value;
        }
    }
}