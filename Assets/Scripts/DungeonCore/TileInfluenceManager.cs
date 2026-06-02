using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(0)]

public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap claimableTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Header("Settings")]
    [SerializeField] private float passiveExpansionInterval = 30f;

    // ── State ─────────────────────────────────────────────────────
    private readonly HashSet<Vector3Int> ownedTiles = new();
    private readonly HashSet<Vector3Int> claimableTiles = new();

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Events ────────────────────────────────────────────────────
    public event Action<int> OnTileCountChanged;

    // ── Internal ──────────────────────────────────────────────────
    private Camera mainCamera;
    private Coroutine passiveExpansionCoroutine;

    // ─────────────────────────────────────────────────────────────

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

    private void Start()
    {
        mainCamera = Camera.main;

        if (DungeonCore.Instance == null || DungeonTerrain.Instance == null)
        {
            Debug.LogError("TileInfluenceManager: Missing DungeonCore or DungeonTerrain instance.");
            return;
        }

        Vector3Int detectedCore = DungeonTerrain.Instance.CoreCell;
        ClaimTile(detectedCore);
        StartPassiveExpansion();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
    }

    // ── Claiming ──────────────────────────────────────────────────

    /// <summary>Claims a tile, updates neighbours, notifies DungeonCore.</summary>
    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (ownedTiles.Contains(pos)) return;
        if (!DungeonTerrain.Instance.IsWithinBounds(pos)) return;

        ownedTiles.Add(pos);
        claimableTiles.Remove(pos);

        DungeonTerrain.Instance.RevealTile(pos);
        claimableTilemap.SetTile(pos, null);

        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = pos + dir;
            if (ownedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;
            if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue;

            claimableTiles.Add(neighbour);
            claimableTilemap.SetTile(neighbour, claimableTile);
        }

        if (!silent)
        {
            DungeonCore.Instance?.AddOwnedTiles(1);
            OnTileCountChanged?.Invoke(ownedTiles.Count);
        }
    }

    /// <summary>
    /// Claims a tile without spending mana, notifying DungeonCore, or checking
    /// terrain bounds. Used by FloorManager's starter-area population and stair
    /// placement on newly-created floors that may not yet have a DungeonTerrain
    /// or active DungeonCore connection.
    /// </summary>
    public void ForceClaimTile(Vector3Int cell)
    {
        if (ownedTiles.Contains(cell)) return;

        ownedTiles.Add(cell);
        claimableTiles.Remove(cell);

        if (claimableTilemap != null)
            claimableTilemap.SetTile(cell, null);

        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = cell + dir;
            if (ownedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;

            claimableTiles.Add(neighbour);
            if (claimableTilemap != null)
                claimableTilemap.SetTile(neighbour, claimableTile);
        }

        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    /// <summary>Removes a tile from ownership (e.g. Destroyer consequence).</summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!ownedTiles.Contains(pos)) return;

        ownedTiles.Remove(pos);
        DungeonTerrain.Instance?.RefogTile(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveOwnedTiles(1);
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    /// <summary>
    /// Called on first core breach. Removes owned tiles within cellRadius of the
    /// core cell, simulating influence loss at the breach point.
    /// The core tile itself is always preserved.
    /// Rebuilds the claimable set once after all removals for efficiency.
    /// </summary>
    public void ShrinkInfluenceAroundCore(float radius)
    {
        if (DungeonCore.Instance == null) return;

        Vector3Int coreCell = WorldToCell(DungeonCore.Instance.transform.position);
        int cellRadius = Mathf.CeilToInt(radius);

        var toRemove = new List<Vector3Int>();

        foreach (var cell in ownedTiles)
        {
            // Never remove the core tile itself
            if (cell == coreCell) continue;

            int dx = Mathf.Abs(cell.x - coreCell.x);
            int dy = Mathf.Abs(cell.y - coreCell.y);

            if (dx <= cellRadius && dy <= cellRadius)
                toRemove.Add(cell);
        }

        if (toRemove.Count == 0) return;

        // Remove tiles and refog them — defer claimable rebuild to the end
        foreach (var cell in toRemove)
        {
            ownedTiles.Remove(cell);
            DungeonTerrain.Instance?.RefogTile(cell);
        }

        // Single rebuild pass after all removals
        RebuildClaimableSet();

        DungeonCore.Instance.RemoveOwnedTiles(toRemove.Count);
        OnTileCountChanged?.Invoke(ownedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} tiles around core.");
    }

    // ── Passive Expansion ─────────────────────────────────────────

    private void StartPassiveExpansion()
    {
        if (passiveExpansionCoroutine != null) StopCoroutine(passiveExpansionCoroutine);
        passiveExpansionCoroutine = StartCoroutine(PassiveExpansionRoutine());
    }

    private IEnumerator PassiveExpansionRoutine()
    {
        while (true)
        {
            float elapsed = 0f;
            while (elapsed < passiveExpansionInterval)
            {
                if (!PauseController.IsGamePaused)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (claimableTiles.Count == 0) continue;
            int index = UnityEngine.Random.Range(0, claimableTiles.Count);
            Vector3Int target = claimableTiles.ElementAt(index);
            ClaimTile(target);
        }
    }

    // ── Add OnBoundsExpanded() ────────────────────────────────────

    /// <summary>Rechecks claimable ring against new bounds after expansion.</summary>
    public void OnBoundsExpanded()
    {
        foreach (Vector3Int owned in ownedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>Rebuilds claimable set from scratch — used after unclaiming.</summary>
    private void RebuildClaimableSet()
    {
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        foreach (Vector3Int owned in ownedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    /// <summary>Converts a world position to a tilemap cell coordinate.</summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
        => claimableTilemap.WorldToCell(worldPos);

    /// <summary>Converts a tilemap cell to the world-space centre of that cell.</summary>
    public Vector3 CellToWorld(Vector3Int cell)
        => claimableTilemap.GetCellCenterWorld(cell);

    public bool IsTileOwned(Vector3Int pos) => ownedTiles.Contains(pos);
    public bool IsTileClaimable(Vector3Int pos) => claimableTiles.Contains(pos);
    public int OwnedTileCount => ownedTiles.Count;

    // ── Save / Load ───────────────────────────────────────────────

    public TileInfluenceSaveData GetSaveData()
    {
        return new TileInfluenceSaveData
        {
            ownedTiles = ownedTiles.Select(SerializableVector3Int.From).ToList()
        };
    }

    public void LoadSaveData(TileInfluenceSaveData data)
    {
        ownedTiles.Clear();
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        foreach (var tile in data.ownedTiles)
            ClaimTile(tile.ToVector3Int(), silent: true);

        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }
}

// ── Save Data ─────────────────────────────────────────────────────

[Serializable]
public class TileInfluenceSaveData
{
    public List<SerializableVector3Int> ownedTiles;
}

[Serializable]
public class SerializableVector3Int
{
    public int x, y, z;
    public Vector3Int ToVector3Int() => new Vector3Int(x, y, z);
    public static SerializableVector3Int From(Vector3Int v) => new() { x = v.x, y = v.y, z = v.z };
}