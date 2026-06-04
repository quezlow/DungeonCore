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
///   - Static Instance removed conceptually (still assigned for legacy callers).
///   - DungeonTerrain reference fetched from sibling FloorRoot, not via singleton.
///   - ClaimStarterArea() added for bootstrapping newly created floors.
///
/// DAY 31 PART 1
///   - New event OnTileBecameClaimable fires for every cell that enters the
///     claimable ring (player claim, passive expansion, starter area, save load).
///     Listeners (FeatureRevealController) should be idempotent.
///   - ClaimTile rejects river cells when called non-silently (player + passive
///     expansion paths). silent: true bypasses so save-restore can re-apply any
///     legitimately-claimed river cells from a future Day 33 absorption.
///   - PassiveExpansionRoutine never picks river cells (the random sample skips
///     them — the player must use mana-cost absorption in Day 33).
///   - GetClaimableTilesSnapshot() exposes a defensive copy for the
///     FeatureRevealController catch-up pass.
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

    /// <summary>DAY 31 — Fires whenever a cell enters the claimable ring. FeatureRevealController
    /// subscribes to this. Fires for all paths (player claim, passive expansion, starter area,
    /// save load) — listeners should be idempotent.</summary>
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

    /// <summary>Claims a tile, updates neighbours, notifies DungeonCore.</summary>
    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (ownedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // DAY 31 — Rivers cannot be claimed via normal mining. Day 33 adds
        // mana-cost absorption. silent: true bypasses the check so save-
        // restore can re-apply any river cells that legitimately ended up
        // claimed (e.g. via future Day 33 absorption persisted in a save).
        if (!silent && Features != null && Features.IsRiver(pos)) return;

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

            // DAY 31 — passive expansion never absorbs rivers (that requires
            // explicit player mana spend, Day 33). Skip if the random pick
            // lands on a river cell — next tick will try a different cell.
            int index = UnityEngine.Random.Range(0, claimableTiles.Count);
            Vector3Int target = claimableTiles.ElementAt(index);
            if (Features != null && Features.IsRiver(target)) continue;
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

    /// <summary>Read-only view of owned tiles for external consumers (e.g., DungeonBoundsUpdater).</summary>
    public IReadOnlyCollection<Vector3Int> OwnedTiles => ownedTiles;
    public int OwnedTileCount => ownedTiles.Count;

    /// <summary>DAY 31 — Returns a defensive copy of the current claimable set,
    /// safe to iterate while reveal logic mutates feature state. Used by
    /// FeatureRevealController.RunInitialCatchup().</summary>
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