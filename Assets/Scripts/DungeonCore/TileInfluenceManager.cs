using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor tile ownership manager.
///
/// DAY 31 PART 1
///   - OnTileBecameClaimable event fires for every cell newly added to the
///     claimable ring (player claim, passive expansion, starter area, save load).
///   - ClaimTile rejects river cells when called non-silently.
///   - Passive expansion skips river cells.
///   - GetClaimableTilesSnapshot exposes a defensive copy for catch-up scans.
///
/// DAY 31 PART 2
///   - ClaimTile also rejects cells in an uncleared chamber. silent: true
///     bypasses for save-restore.
///   - Passive expansion skips uncleared-chamber cells too.
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

    /// <summary>DAY 31 — Fires whenever a cell enters the claimable ring.</summary>
    public event Action<Vector3Int> OnTileBecameClaimable;

    // ── Internal ──────────────────────────────────────────────────
    private DungeonTerrain terrain;
    private Coroutine passiveExpansionCoroutine;

    // DAY 31 — Resolved lazily so this works regardless of Awake order.
    private TerrainFeatureGenerator featureGenerator;
    private TerrainFeatureGenerator Features
    {
        get
        {
            if (featureGenerator == null)
            {
                var root = GetComponentInParent<FloorRoot>();
                featureGenerator = root != null ? root.FeatureGenerator : null;
            }
            return featureGenerator;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
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

            ownedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
        }

        foreach (var offset in offsets)
        {
            Vector3Int pos = centerCell + offset;
            foreach (var dir in Neighbours)
            {
                Vector3Int neighbour = pos + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }

        StartPassiveExpansion();
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    // ── Claiming ──────────────────────────────────────────────────

    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (ownedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // DAY 31 PART 1 — rivers cannot be claimed via normal mining.
        // DAY 31 PART 2 — uncleared chamber cells likewise blocked.
        // silent: true bypasses both gates so save-restore can re-apply
        // any cells that legitimately ended up claimed in a prior session.
        if (!silent && Features != null)
        {
            if (Features.IsRiver(pos)) return;
            if (Features.IsCellInUnclearedChamber(pos)) return;
        }

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
            OnTileBecameClaimable?.Invoke(neighbour);
        }

        if (!silent)
        {
            DungeonCore.Instance?.AddOwnedTiles(1);
            OnTileCountChanged?.Invoke(ownedTiles.Count);
        }
    }

    public void UnclaimTile(Vector3Int pos)
    {
        if (!ownedTiles.Contains(pos)) return;

        ownedTiles.Remove(pos);
        terrain?.RefogTile(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveOwnedTiles(1);
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

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

            // DAY 31 — passive expansion never absorbs rivers or uncleared chambers.
            int index = UnityEngine.Random.Range(0, claimableTiles.Count);
            Vector3Int target = claimableTiles.ElementAt(index);
            if (Features != null)
            {
                if (Features.IsRiver(target)) continue;
                if (Features.IsCellInUnclearedChamber(target)) continue;
            }
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
                OnTileBecameClaimable?.Invoke(neighbour);
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
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }
    }

    public Vector3Int WorldToCell(Vector3 worldPos) => claimableTilemap.WorldToCell(worldPos);
    public Vector3 CellToWorld(Vector3Int cell) => claimableTilemap.GetCellCenterWorld(cell);
    public bool IsTileOwned(Vector3Int pos) => ownedTiles.Contains(pos);
    public bool IsTileClaimable(Vector3Int pos) => claimableTiles.Contains(pos);

    public IReadOnlyCollection<Vector3Int> OwnedTiles => ownedTiles;
    public int OwnedTileCount => ownedTiles.Count;

    public List<Vector3Int> GetClaimableTilesSnapshot() => new List<Vector3Int>(claimableTiles);

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