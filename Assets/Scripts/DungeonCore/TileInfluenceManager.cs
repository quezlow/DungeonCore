using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor tile ownership manager.
///
/// [DAY 31 — DAY 32 history retained; see prior commits]
///
/// INFLUENCE/MINING DECOUPLING — PHASE 1 (data model split)
/// INFLUENCE/MINING DECOUPLING — PHASE 2 (claim and mine decouple)
///
/// PHASE 5 / A / A.1 (claim-coupled stone painting — SUPERSEDED)
///   - Historical: stone painting was tied to claim/claimable status, which
///     forced the autotile RuleTile to disambiguate "ring vs mined vs
///     exterior". This created a steady stream of boundary edge cases that
///     no amount of RuleTile authoring could fully cover, because the autotile
///     can only see "same tile or not" — it has no concept of why a cell is
///     empty. Phase A.1 papered over this by painting stone under the ring,
///     but ring corners and floor-radius boundaries kept misbehaving.
///
/// PHASE B (universal stone — current)
///   - Stone is painted on EVERY cell that is:
///       (a) within the floor radius (i.e., the natural floor was painted),
///       (b) NOT mined,
///       (c) NOT a feature (river / chamber / core cavern).
///     Claim/claimable/anything-else status does NOT affect stone painting.
///   - The autotile gets a consistent world: stone or not-stone. No ambiguity.
///     Edge rules fire correctly at the natural floor boundary (the radius
///     edge) and at mined cavities, with no special handling for the claim ring.
///   - Mining is the only runtime operation that clears stone.
///   - The claim ring becomes a pure interaction indicator. It overlays
///     claimable cells (sort 40) and tells the player "you can act here next."
///     It is data-decoupled from stone painting.
///   - Visibility is handled by fog. Fog has moved to sort 50 (above
///     everything below). Cells that are not "revealed" (not claimed and not
///     in the claim ring) show fog. The player only sees what they have
///     claimed plus the 1-cell claim ring.
///   - Cells entering the claim ring are revealed (RevealTile). Cells leaving
///     the ring without being claimed are refogged. Cells claimed and later
///     unclaimed are refogged.
///   - Discovered features (chambers, rivers, cavern) remain unfogged after
///     discovery via TerrainFeatureGenerator's existing unfog logic.
///
///   Visual stack (Default sorting layer):
///        0 FloorLayer        — natural / feature floor everywhere in radius.
///       20 ClaimedStoneLayer — RT_ClaimedStone everywhere unmined non-feature.
///       30 ShadowLayer       — reserved for next chat.
///       40 ClaimableLayer    — gold ring on claimable cells.
///       50 FogLayer          — covers unrevealed cells. MOVED from sort 10.
/// </summary>
[DefaultExecutionOrder(0)]
public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap claimableTilemap;

    [Header("Visual State Layers (PHASE B)")]
    [Tooltip("Sort 20. Painted with claimedStoneTile on every cell that is " +
             "within the floor radius AND not mined AND not a feature. Stone " +
             "painting is NOT tied to claim status anymore — fog (sort 50) " +
             "is what hides unrevealed stone from the player.")]
    [SerializeField] private Tilemap claimedStoneTilemap;

    [Tooltip("RESERVED for next chat (entity shadows at sort 30). Optional.")]
    [SerializeField] private Tilemap shadowTilemap;

    [Tooltip("PHASE B — Reference to DungeonTerrain's floorTilemap. Used by " +
             "PaintAllStone to enumerate the in-radius cell set. Wire to the " +
             "same Tilemap as DungeonTerrain.floorTilemap and " +
             "TerrainFeatureGenerator.floorTilemap.")]
    [SerializeField] private Tilemap floorTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Tooltip("PHASE A/B — RuleTile asset for stone (RT_ClaimedStone). Painted " +
             "on every unmined non-feature cell in the floor radius.")]
    [SerializeField] private TileBase claimedStoneTile;

    [Header("Settings")]
    [SerializeField] private float passiveExpansionInterval = 30f;

    [Header("Starter Area")]
    [Tooltip("PHASE 4 — Per-cell probability that each of the 8 surrounding " +
             "starter cells starts as mined floor (vs unmined stone). The core " +
             "cell is always mined. At least 1 of the 8 is guaranteed mined " +
             "for connectivity, even if the roll says otherwise.")]
    [Range(0f, 1f)]
    [SerializeField] private float starterMinedChance = 0.7f;

    // ── State ─────────────────────────────────────────────────────

    private readonly HashSet<Vector3Int> claimedTiles = new();
    private readonly HashSet<Vector3Int> minedTiles = new();
    private readonly HashSet<Vector3Int> claimableTiles = new();

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    private bool isLoading;

    // ── Events ────────────────────────────────────────────────────

    public event Action<int> OnTileCountChanged;
    public event Action<int> OnClaimedTileCountChanged;
    public event Action<Vector3Int> OnTileMined;
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

        // [PHASE B] Paint stone on every unmined non-feature in-radius cell.
        // Runs before ClaimStarterArea so the starter area's mining can clear stone.
        PaintAllStone();

        var root = GetComponentInParent<FloorRoot>();
        if (root != null && root.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null || terrain == null)
            {
                Debug.LogError("[TileInfluenceManager] Missing DungeonCore or DungeonTerrain (Floor 1).");
                return;
            }
            ClaimStarterArea(terrain.CoreCell);
        }
    }

    public void InjectTerrain(DungeonTerrain t) => terrain = t;

    // ── Bootstrap (Floor 2+) ──────────────────────────────────────

    /// <summary>
    /// PHASE 4 / B — Bootstraps a floor's starter area: a 3×3 around centerCell.
    ///
    /// PHASE B notes:
    ///   - Stone is already painted on every unmined cell by PaintAllStone()
    ///     in Start. Steps 2 and 5 do NOT need PaintClaimedStone calls.
    ///   - Steps 1, 3, 4 mine cells, which clears stone (ClearClaimedStone).
    ///   - RevealTile is called for every claimed cell (so they're visible).
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
        ClearClaimedStone(corePos); // [PHASE B] Mining clears the stone painted by PaintAllStone.

        // Step 2 — Claim all 8 surrounding cells.
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (claimedTiles.Contains(pos)) continue;

            claimedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
            // [PHASE B] No PaintClaimedStone — already painted by PaintAllStone.
        }

        // Step 3 — Random mining of surrounding cells.
        int surroundingMinedCount = 0;
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (UnityEngine.Random.value < starterMinedChance)
            {
                minedTiles.Add(pos);
                ClearClaimedStone(pos); // [PHASE B] Mining clears stone.
                surroundingMinedCount++;
            }
        }

        // Step 4 — Connectivity guarantee.
        if (surroundingMinedCount == 0)
        {
            int forcedIndex = UnityEngine.Random.Range(0, surroundingOffsets.Length);
            Vector3Int forcedPos = centerCell + surroundingOffsets[forcedIndex];
            minedTiles.Add(forcedPos);
            ClearClaimedStone(forcedPos); // [PHASE B]
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
                PaintClaimableTile(neighbour); // [PHASE B] Reveals the cell so the ring shows above fog.
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }

        StartPassiveExpansion();
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Claim ─────────────────────────────────────────────────────

    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (claimedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        if (!silent && Features != null)
        {
            if (Features.IsCellInUnclearedChamber(pos)) return;
        }

        claimedTiles.Add(pos);
        claimableTiles.Remove(pos);
        terrain?.RevealTile(pos);
        claimableTilemap.SetTile(pos, null);
        // [PHASE B] No PaintClaimedStone — stone is already painted on this cell
        //           by PaintAllStone (provided !mined && !feature, which holds here).

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
            DungeonCore.Instance?.AddClaimedTiles(1);
            OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        }
    }

    // ── Mine ──────────────────────────────────────────────────────

    /// <summary>
    /// PHASE B — Mining is the ONLY runtime operation that clears stone.
    /// Rivers reject silently (water is claimable but not mineable). Cavern
    /// and chamber cells let mining proceed for data consistency, but the
    /// ClearClaimedStone call is a no-op there because PaintAllStone never
    /// painted stone on feature cells.
    /// </summary>
    public void MineTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;
        if (minedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        if (Features != null && Features.IsRiver(pos)) return;

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
        ClearClaimedStone(pos);

        OnTileMined?.Invoke(pos);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    public void ClaimAndMineTile(Vector3Int pos, bool silent = false)
    {
        ClaimTile(pos, silent);
        MineTile(pos);
    }

    // ── Unclaim / Shrink ──────────────────────────────────────────

    /// <summary>
    /// PHASE B — Unclaim removes the claim and refogs the cell. It does NOT
    /// clear stone — stone is independent of claim status. The cell stays
    /// stone-painted (if not mined); fog (sort 50) hides it from the player.
    /// </summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        claimedTiles.Remove(pos);
        terrain?.RefogTile(pos);
        // [PHASE B] No ClearClaimedStone — fog hides the stone.

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
    }

    /// <summary>
    /// PHASE B — Breach shrink removes claims and refogs cells. Stone is
    /// untouched (fog hides what should be hidden). Mined cells stay mined
    /// permanently (legacy Phase A behavior preserved).
    /// </summary>
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

        foreach (var cell in toRemove)
        {
            claimedTiles.Remove(cell);
            terrain?.RefogTile(cell);
            // [PHASE B] No ClearClaimedStone.
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(toRemove.Count);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} claimed (mined and stone preserved).");
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

            ClaimTile(target);
        }
    }

    /// <summary>
    /// PHASE B — When the floor bounds expand, repaint stone over the larger
    /// area so newly-in-radius cells become stone (if applicable).
    /// </summary>
    public void OnBoundsExpanded()
    {
        PaintAllStone();

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

    /// <summary>
    /// PHASE B — Paints the claim ring tile AND reveals the cell so the ring
    /// is visible above the fog (which sits at sort 50, above the ring's 40).
    /// Without RevealTile, fog would hide the ring on cells that haven't been
    /// claimed yet.
    /// </summary>
    private void PaintClaimableTile(Vector3Int cell)
    {
        claimableTilemap.SetTile(cell, claimableTile);
        terrain?.RevealTile(cell);

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

    /// <summary>
    /// PHASE B — When the claim ring rebuilds, any cell that was claimable but
    /// is no longer (and is not claimed) gets refogged. The player should only
    /// see what they currently have claim influence over — losing the ring
    /// means losing visibility.
    /// </summary>
    private void RebuildClaimableSet()
    {
        // Refog cells leaving the ring (unless still claimed).
        foreach (var oldClaimable in claimableTiles)
        {
            if (!claimedTiles.Contains(oldClaimable))
                terrain?.RefogTile(oldClaimable);
        }

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

    // ── PHASE B — Stone Painting ──────────────────────────────────

    /// <summary>
    /// PHASE B — Paints stone on every cell that:
    ///   - has the natural floor painted (i.e., is in the floor radius),
    ///   - is not in minedTiles,
    ///   - is not a river / chamber / core cavern feature.
    /// Called once at Start, on save load (via RepaintAllStateLayers), and on
    /// floor bounds expansion. Clears and repaints the entire stone tilemap.
    /// </summary>
    public void PaintAllStone()
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.ClearAllTiles();

        if (claimedStoneTilemap == null || claimedStoneTile == null) return;
        if (floorTilemap == null)
        {
            Debug.LogWarning("[TileInfluenceManager] floorTilemap unwired — " +
                             "PaintAllStone cannot enumerate in-bounds cells. " +
                             "Assign DungeonTerrain's floorTilemap to the " +
                             "Floor Tilemap field in the Inspector.");
            return;
        }

        var features = Features;

        // Iterate every cell within the floor tilemap's bounds. The floor tilemap
        // was painted by DungeonTerrain.PaintTerrain, so any cell with a non-null
        // tile is inside the floor radius.
        foreach (var cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (floorTilemap.GetTile(cell) == null) continue;

            if (minedTiles.Contains(cell)) continue;

            if (features != null)
            {
                if (features.IsRiver(cell)) continue;
                if (features.IsChamber(cell)) continue;
                if (features.IsCoreCavern(cell)) continue;
            }

            claimedStoneTilemap.SetTile(cell, claimedStoneTile);
        }
    }

    /// <summary>
    /// PHASE B — Clears the stone overlay for a cell. Called only by mining
    /// (and equivalent flows in ClaimStarterArea). SetTile(null) triggers a
    /// RuleTile refresh on adjacent stone cells so their wall-edge variants
    /// re-render with the new "this neighbour is empty" boundary.
    /// </summary>
    private void ClearClaimedStone(Vector3Int cell)
    {
        if (claimedStoneTilemap == null) return;
        claimedStoneTilemap.SetTile(cell, null);
    }

    /// <summary>
    /// PHASE B — Re-derives the stone layer from the current minedTiles and
    /// floor radius. Called from LoadSaveData. Equivalent to PaintAllStone now
    /// that stone is decoupled from claim status.
    /// </summary>
    public void RepaintAllStateLayers() => PaintAllStone();

    // ── Public Reads ──────────────────────────────────────────────

    public Vector3Int WorldToCell(Vector3 worldPos) => claimableTilemap.WorldToCell(worldPos);
    public Vector3 CellToWorld(Vector3Int cell) => claimableTilemap.GetCellCenterWorld(cell);

    public bool IsTileClaimed(Vector3Int pos) => claimedTiles.Contains(pos);
    public bool IsTileMined(Vector3Int pos) => minedTiles.Contains(pos);
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

    /// <summary>
    /// PHASE B — Restores data and calls PaintAllStone (via RepaintAllStateLayers)
    /// to rebuild the stone overlay from minedTiles + features.
    /// </summary>
    public void LoadSaveData(TileInfluenceSaveData data)
    {
        isLoading = true;
        try
        {
            claimedTiles.Clear();
            minedTiles.Clear();
            claimableTiles.Clear();
            claimableTilemap.ClearAllTiles();
            if (claimedStoneTilemap != null) claimedStoneTilemap.ClearAllTiles();

            if (data?.claimedTiles != null)
            {
                foreach (var tile in data.claimedTiles)
                    ClaimTile(tile.ToVector3Int(), silent: true);
            }

            if (data?.minedTiles != null)
            {
                foreach (var tile in data.minedTiles)
                    minedTiles.Add(tile.ToVector3Int());
            }
        }
        finally
        {
            isLoading = false;
        }

        // [PHASE B] Rebuild stone from scratch — every unmined non-feature
        // in-radius cell becomes stone.
        RepaintAllStateLayers();

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