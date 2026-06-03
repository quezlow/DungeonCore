using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor tile ownership manager. No longer a singleton.
/// Access via FloorRoot.TileInfluence or FloorManager.Instance.ActiveFloor.TileInfluence.
///
/// CHANGES FROM PRE-DAY-27
///   - Static Instance removed. All external callers go through FloorRoot.
///   - DungeonTerrain reference fetched from sibling FloorRoot, not via singleton.
///   - ClaimStarterArea() added for bootstrapping newly created floors.
/// </summary>
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
    private DungeonTerrain terrain;
    private Coroutine passiveExpansionCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    { 
        Instance = this;
    }

    private void Start()
    {
        // Terrain is injected by FloorRoot.InjectDependencies().
        // If it's still null here, fall back to GetComponentInParent as a courtesy.
        if (terrain == null)
        {
            var floorRoot = GetComponentInParent<FloorRoot>();
            if (floorRoot != null) terrain = floorRoot.Terrain;
        }

        if (terrain == null)
            Debug.LogWarning($"[TileInfluenceManager] No DungeonTerrain assigned on {gameObject.name}. Wire it via FloorRoot.");

        var root = GetComponentInParent<FloorRoot>();
        if (root != null && root.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null || terrain == null) { Debug.LogError("[TileInfluenceManager] Missing DungeonCore or DungeonTerrain (Floor 1)."); return; }
            ClaimTile(terrain.CoreCell);
            StartPassiveExpansion();
        }
    }

    public void InjectTerrain(DungeonTerrain t) => terrain = t;

    // ── Bootstrap (Floor 2+) ──────────────────────────────────────

    /// <summary>
    /// Seeds a small starter area around centerCell.
    /// Called by FloorRoot.Bootstrap() for newly created floors.
    /// </summary>
    public void ClaimStarterArea(Vector3Int centerCell)
    {
        var offsets = new[]
        {
        new Vector3Int(-1,-1,0), new Vector3Int(0,-1,0), new Vector3Int(1,-1,0),
        new Vector3Int(-1, 0,0), new Vector3Int(0, 0,0), new Vector3Int(1, 0,0),
        new Vector3Int(-1, 1,0), new Vector3Int(0, 1,0), new Vector3Int(1, 1,0),
    };

        foreach (var offset in offsets)
        {
            Vector3Int pos = centerCell + offset;
            if (ownedTiles.Contains(pos)) continue;

            // Skip bounds check here — terrain was just generated at this centre,
            // so these cells are guaranteed valid.
            ownedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
        }

        // Build the claimable ring around the starter area.
        foreach (var offset in offsets)
        {
            Vector3Int pos = centerCell + offset;
            foreach (var dir in new[] { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right })
            {
                Vector3Int neighbour = pos + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }

        StartPassiveExpansion();
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    // ── Claiming ──────────────────────────────────────────────────

    /// <summary>Claims a tile, updates neighbours, notifies DungeonCore.</summary>
    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (ownedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        ownedTiles.Add(pos);
        claimableTiles.Remove(pos);

        terrain?.RevealTile(pos);
        claimableTilemap.SetTile(pos, null);

        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = pos + dir;
            if (ownedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;
            if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

            claimableTiles.Add(neighbour);
            claimableTilemap.SetTile(neighbour, claimableTile);
        }

        if (!silent)
        {
            DungeonCore.Instance?.AddOwnedTiles(1);
            OnTileCountChanged?.Invoke(ownedTiles.Count);
        }
    }

    /// <summary>Removes a tile from ownership (e.g. Destroyer consequence).</summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!ownedTiles.Contains(pos)) return;

        ownedTiles.Remove(pos);
        terrain?.RefogTile(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveOwnedTiles(1);
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    /// <summary>
    /// Called on first core breach. Removes owned tiles within radius of the
    /// core cell. The core tile itself is always preserved.
    /// </summary>
    public void ShrinkInfluenceAroundCore(Vector3Int coreCell, float radius)
    {
        int cellRadius = Mathf.CeilToInt(radius);
        var toRemove = new List<Vector3Int>();

        foreach (var cell in ownedTiles)
        {
            if (cell == coreCell) continue;
            int dx = Mathf.Abs(cell.x - coreCell.x);
            int dy = Mathf.Abs(cell.y - coreCell.y);
            if (dx <= cellRadius && dy <= cellRadius)
                toRemove.Add(cell);
        }

        if (toRemove.Count == 0) return;

        foreach (var cell in toRemove)
        {
            ownedTiles.Remove(cell);
            terrain?.RefogTile(cell);
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveOwnedTiles(toRemove.Count);
        OnTileCountChanged?.Invoke(ownedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} tiles.");
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

    public void OnBoundsExpanded()
    {
        foreach (Vector3Int owned in ownedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

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
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    public Vector3Int WorldToCell(Vector3 worldPos) => claimableTilemap.WorldToCell(worldPos);
    public Vector3 CellToWorld(Vector3Int cell) => claimableTilemap.GetCellCenterWorld(cell);
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