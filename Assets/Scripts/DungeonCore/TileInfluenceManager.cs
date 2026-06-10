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
///   - GetClaimableTilesSnapshot exposes a defensive copy for catch-up scans.
///
/// DAY 31 PART 2
///   - ClaimTile rejects cells in an uncleared chamber. silent: true
///     bypasses for save-restore.
///   - Passive expansion skips uncleared-chamber cells.
///
/// DAY 32 — TERRAIN RESISTANCE
///   - River gate removed from ClaimTile: rivers are now claimable. Cost
///     gating happens upstream in DungeonBuildController where the player
///     pays mana before calling ClaimTile.
///   - Passive expansion still skips rivers (deliberately expensive, player-
///     decision territory). Passive expansion is now probabilistic based on
///     terrain resistance: each tick a random claimable cell is selected,
///     then claimed with probability 1/resistance. Granite (4×) thus takes
///     ~4× longer in expectation.
///   - Each newly-painted claimable tile is tinted via
///     FloorRoot.GetClaimableRingTint so terrain resistance reads visually.
///   - RepaintClaimableTiles() re-applies tints to all claimable cells;
///     called by TerrainTypeMap.GenerateNew so Floor 0's early-painted
///     starter ring picks up the correct colours once terrain exists.
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

    // DAY 32 — Floor lookup for terrain queries; cached.
    private FloorRoot myFloor;
    private FloorRoot MyFloor
    {
        get
        {
            if (myFloor == null)
                myFloor = GetComponentInParent<FloorRoot>();
            return myFloor;
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
                PaintClaimableTile(neighbour);                      // DAY 32
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

        // DAY 31 PART 2 — uncleared chamber cells blocked.
        // DAY 32 — river gate removed; rivers are claimable at high cost paid
        //          upstream by DungeonBuildController. Chamber gate stays.
        // silent: true bypasses for save-restore.
        if (!silent && Features != null)
        {
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
            PaintClaimableTile(neighbour);                          // DAY 32
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

            // DAY 32 — probabilistic claim based on terrain resistance.
            //          Dirt (1×) always claims when picked.
            //          Granite (4×) claims with ~25% probability per pick.
            float resistance = 1f;
            var floor = MyFloor;
            if (floor != null) resistance = floor.GetClaimCostMultiplier(target);
            if (resistance > 1f && UnityEngine.Random.value > 1f / resistance) continue;

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
                PaintClaimableTile(neighbour);                       // DAY 32
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>DAY 32 — Paints a claimable tile with terrain-aware tint.</summary>
    private void PaintClaimableTile(Vector3Int cell)
    {
        claimableTilemap.SetTile(cell, claimableTile);

        var floor = MyFloor;
        if (floor == null) return;

        Color tint = floor.GetClaimableRingTint(cell);
        claimableTilemap.SetTileFlags(cell, TileFlags.None);
        claimableTilemap.SetColor(cell, tint);
    }

    /// <summary>DAY 32 — Re-applies tints to every current claimable cell.
    ///         Called by TerrainTypeMap.GenerateNew so Floor 0's first-painted
    ///         ring picks up its colours once terrain generation completes.</summary>
    public void RepaintClaimableTiles()
    {
        foreach (var cell in claimableTiles)
            PaintClaimableTile(cell);
    }

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
                PaintClaimableTile(neighbour);                       // DAY 32
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

[Serializable]
public struct SerializableVector3
{
    public float x, y, z;
    public static SerializableVector3 From(Vector3 v) => new() { x = v.x, y = v.y, z = v.z };
    public Vector3 ToVector3() => new(x, y, z);
}