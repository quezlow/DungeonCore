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
///     terrain resistance.
///   - Each newly-painted claimable tile is tinted via
///     FloorRoot.GetClaimableRingTint so terrain resistance reads visually.
///
/// INFLUENCE/MINING DECOUPLING — PHASE 1 (data model split, NO behavior change)
///   - Internal field 'ownedTiles' renamed to 'claimedTiles'. A new 'minedTiles'
///     set was added. Compat shims kept for IsTileOwned / OwnedTiles / OwnedTileCount.
///
/// INFLUENCE/MINING DECOUPLING — PHASE 2 (behavior change — claim and mine decouple)
///   - ClaimTile no longer adds to minedTiles. It only adds to claimedTiles
///     and calls terrain.RevealTile (claimed cells are visible). The claimable
///     ring still expands from claimedTiles. DungeonCore.AddOwnedTiles(1) is
///     still called on claim — this means DungeonCore.ownedTileCount tracks
///     CLAIMED count after Phase 2, and mana regen scales with claimed (per
///     design).
///   - NEW: MineTile(pos) requires the cell to be in claimedTiles and 4-adjacent
///     to an existing mined cell (with a bypass for the floor's core cell so
///     the very first mine has somewhere to start). Adds the cell to minedTiles
///     and fires OnTileMined.
///   - NEW: ClaimAndMineTile(pos) helper — does both in one call. Used by the
///     Floor 0 bootstrap and any other callsite that wants the pre-Phase-2
///     combined behavior.
///   - NEW events: OnTileMined(Vector3Int), OnClaimedTileCountChanged(int).
///     The existing OnTileCountChanged event continues to report MINED count
///     for HUD compatibility.
///   - UnclaimTile and ShrinkInfluenceAroundCore remove from BOTH sets (a mined
///     cell that isn't claimed makes no sense) and fire both events as needed.
///   - Save format unchanged from Phase 1 (still v2). Loading a Phase 1 save
///     where claimed == mined works correctly because both lists are present.
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

    [Header("Starter Area")]
    [Tooltip("PHASE 4 — Per-cell probability that each of the 8 surrounding " +
         "starter cells starts as mined floor (vs claimed stone). Core " +
         "cell is always mined regardless. At least 1 of the 8 is guaranteed " +
         "mined for connectivity, even if the roll says otherwise.")]
    [Range(0f, 1f)]
    [SerializeField] private float starterMinedChance = 0.7f;

    // ── State ─────────────────────────────────────────────────────

    // Cells inside dungeon influence (visible, can interact, contributes to mana, mineable).
    private readonly HashSet<Vector3Int> claimedTiles = new();

    // Cells dug out (walkable, buildable, pathable). Strict subset of claimedTiles in Phase 2+.
    private readonly HashSet<Vector3Int> minedTiles = new();

    // The 1-cell ring around claimedTiles — next candidates for claim.
    private readonly HashSet<Vector3Int> claimableTiles = new();

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Events ────────────────────────────────────────────────────

    /// <summary>Fires when minedTiles.Count changes. HUD subscribers use this.</summary>
    public event Action<int> OnTileCountChanged;

    /// <summary>PHASE 2 — Fires when claimedTiles.Count changes.</summary>
    public event Action<int> OnClaimedTileCountChanged;

    /// <summary>PHASE 2 — Fires per cell newly added to minedTiles.</summary>
    public event Action<Vector3Int> OnTileMined;

    /// <summary>DAY 31 — Fires whenever a cell enters the claimable ring.</summary>
    public event Action<Vector3Int> OnTileBecameClaimable;

    // ── Internal ──────────────────────────────────────────────────

    private DungeonTerrain terrain;
    private Coroutine passiveExpansionCoroutine;

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

    private FloorRoot myFloor;
    private FloorRoot MyFloor
    {
        get
        {
            if (myFloor == null) myFloor = GetComponentInParent<FloorRoot>();
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
            Debug.LogWarning($"[TileInfluenceManager] No DungeonTerrain assigned on {gameObject.name}. " +
                             $"Wire it via FloorRoot.");

        var root = GetComponentInParent<FloorRoot>();
        if (root != null && root.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null || terrain == null)
            {
                Debug.LogError("[TileInfluenceManager] Missing DungeonCore or DungeonTerrain (Floor 1).");
                return;
            }
            // PHASE 4 — Floor 0 uses the unified ClaimStarterArea path so the
            // starter pattern (3×3 with random mining on the 8 surrounding cells)
            // is consistent with Floor 2+. ClaimStarterArea calls StartPassiveExpansion
            // internally.
            ClaimStarterArea(terrain.CoreCell);
        }
    }

    public void InjectTerrain(DungeonTerrain t) => terrain = t;

    // ── Bootstrap (Floor 2+) ──────────────────────────────────────

    /// <summary>
    /// PHASE 4 — Bootstraps a floor's starter area: a 3×3 around centerCell.
    ///
    /// All 9 cells become claimed. The core cell is always mined. Each of the 8
    /// surrounding cells is independently mined with probability
    /// starterMinedChance. If the random roll produces zero mined surrounding
    /// cells, one is forced mined for connectivity.
    ///
    /// Cells that end up claimed-but-not-mined start the game as "claimed stone"
    /// — visible to the player, contributing to mana regen, mineable later.
    /// </summary>
    public void ClaimStarterArea(Vector3Int centerCell)
    {
        Vector3Int corePos = centerCell;

        var surroundingOffsets = new[]
        {
        new Vector3Int(-1,-1,0), new Vector3Int(0,-1,0), new Vector3Int(1,-1,0),
        new Vector3Int(-1, 0,0),                          new Vector3Int(1, 0,0),
        new Vector3Int(-1, 1,0), new Vector3Int(0, 1,0), new Vector3Int(1, 1,0),
    };

        // Step 1 — Claim the core cell. Always mined.
        if (!claimedTiles.Contains(corePos))
        {
            claimedTiles.Add(corePos);
            claimableTiles.Remove(corePos);
            terrain?.RevealTile(corePos);
            claimableTilemap.SetTile(corePos, null);
        }
        minedTiles.Add(corePos);

        // Step 2 — Claim all 8 surrounding cells. Mining decided in Step 3.
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (claimedTiles.Contains(pos)) continue;

            claimedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
        }

        // Step 3 — Random mining of surrounding cells.
        int surroundingMinedCount = 0;
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (UnityEngine.Random.value < starterMinedChance)
            {
                minedTiles.Add(pos);
                surroundingMinedCount++;
            }
        }

        // Step 4 — Connectivity guarantee. If zero surrounding cells were mined,
        // force one at random.
        if (surroundingMinedCount == 0)
        {
            int forcedIndex = UnityEngine.Random.Range(0, surroundingOffsets.Length);
            Vector3Int forcedPos = centerCell + surroundingOffsets[forcedIndex];
            minedTiles.Add(forcedPos);
            Debug.Log($"[TileInfluenceManager] Starter area: 0/8 surrounding rolled " +
                      $"mined — forcing {forcedPos} for connectivity.");
        }

        // Step 5 — Expand the claimable ring around all 9 claimed cells.
        var allCells = new List<Vector3Int>(surroundingOffsets.Length + 1);
        allCells.Add(corePos);
        foreach (var offset in surroundingOffsets)
            allCells.Add(centerCell + offset);

        foreach (var pos in allCells)
        {
            foreach (var dir in Neighbours)
            {
                Vector3Int neighbour = pos + dir;
                if (claimedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                PaintClaimableTile(neighbour);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }

        StartPassiveExpansion();
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Claim (Phase 2: claim-only, no mining) ────────────────────

    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (claimedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // Chamber gate — uncleared chambers cannot be claimed.
        // silent: true bypasses for save-restore.
        if (!silent && Features != null)
        {
            if (Features.IsCellInUnclearedChamber(pos)) return;
        }

        claimedTiles.Add(pos);
        // PHASE 2 — no longer adds to minedTiles. Mining is a separate action.

        claimableTiles.Remove(pos);
        terrain?.RevealTile(pos);                                   // claimed cells are visible
        claimableTilemap.SetTile(pos, null);

        // Expand the claimable ring.
        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = pos + dir;
            if (claimedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;
            if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

            claimableTiles.Add(neighbour);
            PaintClaimableTile(neighbour);
            OnTileBecameClaimable?.Invoke(neighbour);
        }

        if (!silent)
        {
            // DungeonCore.ownedTileCount tracks CLAIMED count after Phase 2.
            // Mana regen formula (baseRegen + ownedTileCount * perTile) scales
            // with claimed, per P3-Q1.
            DungeonCore.Instance?.AddClaimedTiles(1);
            OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        }
    }

    // ── Mine (Phase 2: new action) ────────────────────────────────

    /// <summary>
    /// PHASE 2 — Digs a claimed cell into walkable floor. Requires:
    ///   - The cell to already be in claimedTiles.
    ///   - The cell not already in minedTiles.
    ///   - The cell to be 4-adjacent to an existing mined cell, OR the floor's
    ///     core cell (so the very first mine has somewhere to start).
    /// Does NOT call DungeonCore.AddOwnedTiles — that's tracked at claim time.
    /// </summary>
    public void MineTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;
        if (minedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // Adjacency check — must be next to existing mined area, with a bypass
        // for the floor's core cell.
        bool isCoreCell = (terrain != null && pos == terrain.CoreCell);
        if (!isCoreCell)
        {
            bool hasAdjacentMined = false;
            foreach (var dir in Neighbours)
            {
                if (minedTiles.Contains(pos + dir)) { hasAdjacentMined = true; break; }
            }
            if (!hasAdjacentMined) return;
        }

        minedTiles.Add(pos);
        // No RevealTile needed — cell was already revealed at claim time.
        // No claimableTilemap update — mining doesn't change the ring.

        OnTileMined?.Invoke(pos);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    /// <summary>
    /// PHASE 2 — Convenience helper for callsites that want the pre-Phase-2
    /// combined behavior. Calls ClaimTile then MineTile. Used by the Floor 0
    /// bootstrap.
    /// </summary>
    public void ClaimAndMineTile(Vector3Int pos, bool silent = false)
    {
        ClaimTile(pos, silent);
        MineTile(pos);
    }

    /// <summary>
    /// Marks a batch of cells as natural open floor — walkable (mined) — WITHOUT
    /// claiming them. They stay outside the influence ring until the player claims
    /// into them. Used by terrain generation for the pre-revealed core cavern +
    /// tunnels (runs on both fresh generation and save-load). Does not fire
    /// OnTileMined (these are not player digs) and does not expand the claimable
    /// ring; fires OnTileCountChanged once if anything changed so the wall
    /// renderer rebuilds.
    /// </summary>
    public void MarkNaturalFloor(IEnumerable<Vector3Int> cells)
    {
        if (cells == null) return;
        bool any = false;
        foreach (var cell in cells)
            if (minedTiles.Add(cell)) any = true;
        if (any) OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Unclaim / Shrink ──────────────────────────────────────────

    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        bool wasMined = minedTiles.Contains(pos);

        claimedTiles.Remove(pos);
        if (wasMined) minedTiles.Remove(pos);
        terrain?.RefogTile(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        if (wasMined) OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    public void ShrinkInfluenceAroundCore(Vector3Int coreCell, float radius)
    {
        int cellRadius = Mathf.CeilToInt(radius);
        var toRemove = new List<Vector3Int>();

        foreach (var cell in claimedTiles)
        {
            if (cell == coreCell) continue;
            int dx = Mathf.Abs(cell.x - coreCell.x);
            int dy = Mathf.Abs(cell.y - coreCell.y);
            if (dx <= cellRadius && dy <= cellRadius)
                toRemove.Add(cell);
        }

        if (toRemove.Count == 0) return;

        int minedRemoved = 0;
        foreach (var cell in toRemove)
        {
            claimedTiles.Remove(cell);
            if (minedTiles.Remove(cell)) minedRemoved++;
            terrain?.RefogTile(cell);
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(toRemove.Count);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        if (minedRemoved > 0) OnTileCountChanged?.Invoke(minedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} claimed ({minedRemoved} mined).");
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
            if (Features != null)
            {
                if (Features.IsRiver(target)) continue;
                if (Features.IsCellInUnclearedChamber(target)) continue;
            }

            float resistance = 1f;
            var floor = MyFloor;
            if (floor != null) resistance = floor.GetClaimCostMultiplier(target);
            if (resistance > 1f && UnityEngine.Random.value > 1f / resistance) continue;

            // PHASE 2 — passive expansion only claims, never mines.
            // ClaimTile is already claim-only after Phase 2.
            ClaimTile(target);
        }
    }

    public void OnBoundsExpanded()
    {
        foreach (Vector3Int owned in claimedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (claimedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                PaintClaimableTile(neighbour);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private void PaintClaimableTile(Vector3Int cell)
    {
        claimableTilemap.SetTile(cell, claimableTile);

        var floor = MyFloor;
        if (floor == null) return;

        Color tint = floor.GetClaimableRingTint(cell);
        claimableTilemap.SetTileFlags(cell, TileFlags.None);
        claimableTilemap.SetColor(cell, tint);
    }

    public void RepaintClaimableTiles()
    {
        foreach (var cell in claimableTiles)
            PaintClaimableTile(cell);
    }

    private void RebuildClaimableSet()
    {
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        foreach (Vector3Int owned in claimedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (claimedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                PaintClaimableTile(neighbour);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }
    }

    // ── Public Reads ──────────────────────────────────────────────

    public Vector3Int WorldToCell(Vector3 worldPos) => claimableTilemap.WorldToCell(worldPos);
    public Vector3 CellToWorld(Vector3Int cell) => claimableTilemap.GetCellCenterWorld(cell);

    public bool IsTileClaimed(Vector3Int pos) => claimedTiles.Contains(pos);
    public bool IsTileMined(Vector3Int pos) => minedTiles.Contains(pos);

    /// <summary>
    /// True when this mined floor cell sits under the BOTTOM slice of a north
    /// wall's draped face — open floor immediately north, solid rock two cells
    /// north. The bottom wall sprite lands here, so the cell reads as wall, not
    /// floor: entities must not stand on it or target it.
    /// </summary>
    public bool IsUnderOverhang(Vector3Int pos)
    {
        if (!minedTiles.Contains(pos)) return false;
        return minedTiles.Contains(pos + Vector3Int.up)
            && !minedTiles.Contains(pos + new Vector3Int(0, 2, 0));
    }
    public bool IsTileClaimable(Vector3Int pos) => claimableTiles.Contains(pos);

    public IReadOnlyCollection<Vector3Int> ClaimedTiles => claimedTiles;
    public IReadOnlyCollection<Vector3Int> MinedTiles => minedTiles;
    public int ClaimedTileCount => claimedTiles.Count;
    public int MinedTileCount => minedTiles.Count;

    [Obsolete("Phase 1 compat. Use IsTileMined or IsTileClaimed depending on intent.")]
    public bool IsTileOwned(Vector3Int pos) => IsTileMined(pos);

    [Obsolete("Phase 1 compat. Use MinedTiles or ClaimedTiles depending on intent.")]
    public IReadOnlyCollection<Vector3Int> OwnedTiles => minedTiles;

    [Obsolete("Phase 1 compat. Use MinedTileCount or ClaimedTileCount depending on intent.")]
    public int OwnedTileCount => minedTiles.Count;

    public List<Vector3Int> GetClaimableTilesSnapshot() => new List<Vector3Int>(claimableTiles);

    // ── Save / Load ───────────────────────────────────────────────

    public TileInfluenceSaveData GetSaveData()
    {
        return new TileInfluenceSaveData
        {
            claimedTiles = claimedTiles.Select(SerializableVector3Int.From).ToList(),
            minedTiles = minedTiles.Select(SerializableVector3Int.From).ToList(),
            ownedTiles = new List<SerializableVector3Int>(),
        };
    }

    public void LoadSaveData(TileInfluenceSaveData data)
    {
        claimedTiles.Clear();
        minedTiles.Clear();
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        // Restore claimed cells via silent ClaimTile (sets fog, ring, etc.).
        // ClaimTile is claim-only in Phase 2, so this populates claimedTiles only.
        if (data?.claimedTiles != null)
        {
            foreach (var tile in data.claimedTiles)
                ClaimTile(tile.ToVector3Int(), silent: true);
        }

        // PHASE 2 — Restore mined cells directly. No event firing needed; the
        // OnTileCountChanged below is the bulk update.
        if (data?.minedTiles != null)
        {
            foreach (var tile in data.minedTiles)
                minedTiles.Add(tile.ToVector3Int());
        }

        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }
}

// ── Save Data ─────────────────────────────────────────────────────

[Serializable]
public class TileInfluenceSaveData
{
    /// <summary>LEGACY — Used only by v1→v2 migration. Empty in v2+ saves.</summary>
    public List<SerializableVector3Int> ownedTiles;

    /// <summary>Cells inside dungeon influence.</summary>
    public List<SerializableVector3Int> claimedTiles;

    /// <summary>Cells dug out / walkable / buildable. Subset of claimedTiles.</summary>
    public List<SerializableVector3Int> minedTiles;
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