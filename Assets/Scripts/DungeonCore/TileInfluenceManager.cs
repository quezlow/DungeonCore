using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
///   - Save format unchanged from Phase 1 (still v2). Loading a Phase 1 save
///     where claimed == mined works correctly because both lists are present.
///
/// INFLUENCE/MINING DECOUPLING — PHASE 5 (visual state layers)
///   - Two new tilemaps painted by this manager:
///       claimedStoneTilemap → claimedStoneTile (RT_ClaimedStone), painted on
///       every cell in claimedTiles. Stays painted when the cell is also mined.
///       minedFloorTilemap   → minedFloorTile   (RT_MinedFloor),  painted on
///       every cell in minedTiles. Sort order is set so it occludes the stone
///       layer beneath.
///   - Paint hooks live in: ClaimStarterArea, ClaimTile, MineTile, UnclaimTile,
///     ShrinkInfluenceAroundCore.
///   - LoadSaveData wraps in isLoading=true, suppresses per-cell paints, and
///     does a single RepaintAllStateLayers at the end.
///   - Visual stack (Default sorting layer, current spacing):
///        0 FloorLayer        — base brown, painted by DungeonTerrain
///       10 FogLayer
///       20 ClaimedStoneLayer
///       30 MinedFloorLayer
///       40 ClaimableLayer    — gold ring, stays on top of state layers
///       50 RoomHighlightTilemap
///   - Save format unchanged from Phase 2 (still v2).
///
/// INFLUENCE/MINING DECOUPLING — PHASE 6 (unclaim-preserves-mined + two-zone tint)
///   - BUG FIX: UnclaimTile and ShrinkInfluenceAroundCore no longer strip
///     entries from minedTiles. Mining is a physical act; losing influence
///     shouldn't unmake the hole. Both methods now only remove from
///     claimedTiles and clear the claimed-stone layer. The mined-floor tile
///     stays painted and is occluded by the returned fog. On re-claim, fog
///     lifts and the pre-existing mined-floor tile becomes visible again
///     with no re-paint needed in ClaimTile.
///   - Consequence: OnTileCountChanged (mined count) no longer fires from
///     unclaim/shrink — minedTiles.Count doesn't change. Only
///     OnClaimedTileCountChanged + DungeonCore.RemoveClaimedTiles still fire.
///   - RENAMED: ClearStateLayers → ClearClaimedStoneAt. Only touches the
///     stone layer now; honest name.
///   - TWO-ZONE STONE TINTING: claimed-stone cells with any mined cardinal
///     neighbour render bright (un-tinted = the RuleTile's natural color).
///     Stone cells with no mined cardinal neighbour render with the
///     configurable darkStoneColor. New helpers IsAdjacentToMined +
///     RefreshStoneTintAt. PaintClaimedStone applies the correct tint at
///     paint time. MineTile refreshes the tint on its 4 cardinal neighbours
///     after mining (they may have just gained mined-adjacent status).
///     RepaintAllStateLayers applies tints during bulk load. ClaimStarterArea
///     does one end-of-method refresh pass over the 9 starter cells so
///     diagonal stone cells get correct tints after Steps 3/4 finalise the
///     mining pattern. Unclaim/shrink do NOT refresh neighbours — since
///     minedTiles never shrinks, no cell's mined-adjacent status ever
///     decreases.
/// </summary>
[DefaultExecutionOrder(0)]
public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap claimableTilemap;

    [Header("Visual State Layers (PHASE 5)")]
    [Tooltip("Lower-sort layer. Painted with claimedStoneTile on every cell " +
             "in claimedTiles. Stays painted even when the cell is also mined " +
             "(the mined layer occludes it).")]
    [SerializeField] private Tilemap claimedStoneTilemap;

    [Tooltip("Higher-sort layer. Painted with minedFloorTile on every cell in " +
             "minedTiles.")]
    [SerializeField] private Tilemap minedFloorTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Tooltip("PHASE 5 — RuleTile asset for claimed-not-mined stone. Painted " +
             "into claimedStoneTilemap.")]
    [SerializeField] private TileBase claimedStoneTile;

    [Tooltip("PHASE 5 — RuleTile asset for mined floor. Painted into " +
             "minedFloorTilemap.")]
    [SerializeField] private TileBase minedFloorTile;

    [Header("Stone Tinting (PHASE 6)")]
    [Tooltip("PHASE 6 — Tint applied to claimed-stone cells with NO mined " +
             "cardinal neighbour (the outer 'visible-but-untouched' zone). " +
             "Stone cells WITH a mined cardinal neighbour render bright " +
             "(untinted white = the RuleTile's natural color). Tune in the " +
             "Inspector. Default ~(0.55, 0.55, 0.55, 1) = mid gray.")]
    [SerializeField] private Color darkStoneColor = new Color(0.55f, 0.55f, 0.55f, 1f);

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

    // Cells dug out (walkable, buildable, pathable).
    // PHASE 6 — No longer guaranteed to be a strict subset of claimedTiles.
    // A cell can be in minedTiles but not claimedTiles (was mined, then later
    // unclaimed via breach shrink or other influence loss). The mined-floor
    // tile stays painted and is occluded by fog while unclaimed.
    private readonly HashSet<Vector3Int> minedTiles = new();

    // The 1-cell ring around claimedTiles — next candidates for claim.
    private readonly HashSet<Vector3Int> claimableTiles = new();

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    /// <summary>
    /// PHASE 5 — When true, per-cell paint helpers bail out so LoadSaveData can
    /// do a single bulk RepaintAllStateLayers at the end instead of paying for
    /// per-cell paints during restore.
    /// </summary>
    private bool isLoading;

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
    ///
    /// PHASE 5 — Paints stone layer for every newly claimed cell and the mined
    /// layer for every newly mined cell.
    ///
    /// PHASE 6 — End-of-method tint refresh over all 9 starter cells. PaintClaimedStone
    /// applies a tint at paint time using IsAdjacentToMined, but Steps 3 and 4
    /// add cells to minedTiles AFTER the surrounding stone has been painted in
    /// Step 2. Without the refresh, diagonal stone cells whose cardinal
    /// neighbours mine in Step 3/4 would incorrectly stay dark until the player
    /// mined something nearby. RefreshStoneTintAt is a no-op on cells without
    /// stone painted (e.g. the mined-floor cells whose stone is occluded — the
    /// underlying stone tile IS still present and gets its tint correctly
    /// computed, just visually occluded).
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
            PaintClaimedStone(corePos);                              // PHASE 5
        }
        minedTiles.Add(corePos);
        PaintMinedFloor(corePos);                                    // PHASE 5

        // Step 2 — Claim all 8 surrounding cells. Mining decided in Step 3.
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (claimedTiles.Contains(pos)) continue;

            claimedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
            PaintClaimedStone(pos);                                  // PHASE 5
        }

        // Step 3 — Random mining of surrounding cells.
        int surroundingMinedCount = 0;
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (UnityEngine.Random.value < starterMinedChance)
            {
                minedTiles.Add(pos);
                PaintMinedFloor(pos);                                // PHASE 5
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
            PaintMinedFloor(forcedPos);                              // PHASE 5
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

        // PHASE 6 — Refresh tint on every starter cell now that all mining
        // decisions are final. Required for diagonal stone cells whose
        // cardinal-neighbour mined status was decided after Step 2 painted them.
        foreach (var pos in allCells)
            RefreshStoneTintAt(pos);

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
        // PHASE 6 — Re-claiming a cell that still has minedTiles entry (because
        // unclaim no longer scrubs that set) is fine: the mined-floor tile is
        // still painted on minedFloorTilemap and becomes visible again as soon
        // as RevealTile lifts the fog below. No re-paint of the mined layer
        // is needed here.

        claimableTiles.Remove(pos);
        terrain?.RevealTile(pos);                                   // claimed cells are visible
        claimableTilemap.SetTile(pos, null);
        PaintClaimedStone(pos);                                      // PHASE 5 (+ Phase 6 tint applied inside)

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
    ///
    /// PHASE 5 — Paints the mined-floor layer (occludes stone beneath).
    ///
    /// PHASE 6 — Refreshes stone tint on the 4 cardinal neighbours, since they
    /// may have just gained mined-adjacent status and should flip bright.
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
        PaintMinedFloor(pos);                                        // PHASE 5
        // No RevealTile needed — cell was already revealed at claim time.
        // No claimableTilemap update — mining doesn't change the ring.

        // PHASE 6 — Brighten any adjacent claimed-stone cells that just
        // gained a mined cardinal neighbour. RefreshStoneTintAt bails on
        // cells that aren't claimed-stone, so it's safe to fire on all 4.
        foreach (var dir in Neighbours)
            RefreshStoneTintAt(pos + dir);

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

    // ── Unclaim / Shrink ──────────────────────────────────────────

    /// <summary>
    /// PHASE 6 — Removes only the claim. The cell's minedTiles entry (if any)
    /// is preserved: mining is a physical act and shouldn't be undone by an
    /// influence change. The mined-floor tile stays painted on
    /// minedFloorTilemap and is occluded by fog (added back via RefogTile).
    /// On future re-claim, RevealTile lifts the fog and the mined-floor tile
    /// becomes visible again with no re-paint required.
    /// </summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        claimedTiles.Remove(pos);
        terrain?.RefogTile(pos);
        ClearClaimedStoneAt(pos);                                    // PHASE 6 (renamed)

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        // PHASE 6 — OnTileCountChanged intentionally NOT fired: minedTiles
        // unchanged. No mined-adjacency status decreases, so no neighbour
        // tint refresh either.
    }

    /// <summary>
    /// PHASE 6 — As with UnclaimTile, minedTiles entries are preserved.
    /// Removed cells re-fog and their stone clears, but the mined-floor
    /// tile stays painted underneath.
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
            ClearClaimedStoneAt(cell);                               // PHASE 6 (renamed)
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(toRemove.Count);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        // PHASE 6 — OnTileCountChanged intentionally NOT fired: minedTiles
        // unchanged. No mined-adjacency status decreases.

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} claimed " +
                  $"(minedTiles preserved per Phase 6).");
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

    // ── PHASE 5 — Visual State Layer Helpers ──────────────────────

    /// <summary>
    /// Paints the claimed-stone tile on the lower state layer. Bails when
    /// isLoading is true (LoadSaveData does a single bulk repaint instead).
    ///
    /// PHASE 6 — After SetTile, applies the bright-or-dark tint based on
    /// IsAdjacentToMined. Uses the same SetTileFlags(None) + SetColor pattern
    /// PaintClaimableTile uses.
    /// </summary>
    private void PaintClaimedStone(Vector3Int cell)
    {
        if (isLoading) return;
        if (claimedStoneTilemap == null || claimedStoneTile == null) return;

        claimedStoneTilemap.SetTile(cell, claimedStoneTile);

        Color tint = IsAdjacentToMined(cell) ? Color.white : darkStoneColor;
        claimedStoneTilemap.SetTileFlags(cell, TileFlags.None);
        claimedStoneTilemap.SetColor(cell, tint);
    }

    /// <summary>
    /// Paints the mined-floor tile on the upper state layer. Bails when
    /// isLoading is true (LoadSaveData does a single bulk repaint instead).
    /// </summary>
    private void PaintMinedFloor(Vector3Int cell)
    {
        if (isLoading) return;
        if (minedFloorTilemap == null || minedFloorTile == null) return;
        minedFloorTilemap.SetTile(cell, minedFloorTile);
    }

    /// <summary>
    /// PHASE 6 — Clears ONLY the claimed-stone layer for the given cell. The
    /// mined-floor layer is left alone: see UnclaimTile / ShrinkInfluenceAroundCore
    /// docstrings for rationale. SetTile(null) on an empty cell is a no-op.
    /// Previously named ClearStateLayers (which touched both layers).
    /// </summary>
    private void ClearClaimedStoneAt(Vector3Int cell)
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.SetTile(cell, null);
    }

    /// <summary>
    /// PHASE 6 — True if any of the 4 cardinal neighbours of <paramref name="cell"/>
    /// is in minedTiles. Cardinal only — no diagonals.
    /// </summary>
    private bool IsAdjacentToMined(Vector3Int cell)
    {
        foreach (var dir in Neighbours)
            if (minedTiles.Contains(cell + dir)) return true;
        return false;
    }

    /// <summary>
    /// PHASE 6 — Re-applies the bright-or-dark stone tint on <paramref name="cell"/>
    /// based on its current mined-adjacency. Safe to call on any cell: bails
    /// if the cell isn't claimed or doesn't have a stone tile painted (e.g.
    /// claimable-ring cells, out-of-bounds, cells whose stone has been cleared).
    /// </summary>
    private void RefreshStoneTintAt(Vector3Int cell)
    {
        if (claimedStoneTilemap == null) return;
        if (!claimedTiles.Contains(cell)) return;
        if (!claimedStoneTilemap.HasTile(cell)) return;

        Color tint = IsAdjacentToMined(cell) ? Color.white : darkStoneColor;
        claimedStoneTilemap.SetTileFlags(cell, TileFlags.None);
        claimedStoneTilemap.SetColor(cell, tint);
    }

    /// <summary>
    /// Bulk repaint of both state layers from current claimedTiles / minedTiles.
    /// Called once at the end of LoadSaveData. Public so a debug hotkey can
    /// re-sync visuals if data and visuals ever drift.
    ///
    /// PHASE 6 — Applies the correct two-zone tint to each stone tile based on
    /// IsAdjacentToMined. minedTiles is fully populated by the time this runs,
    /// so the computation is accurate.
    /// </summary>
    public void RepaintAllStateLayers()
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.ClearAllTiles();
        if (minedFloorTilemap != null) minedFloorTilemap.ClearAllTiles();

        if (claimedStoneTilemap != null && claimedStoneTile != null)
        {
            foreach (var cell in claimedTiles)
            {
                claimedStoneTilemap.SetTile(cell, claimedStoneTile);

                // PHASE 6 — Tint each painted stone tile.
                Color tint = IsAdjacentToMined(cell) ? Color.white : darkStoneColor;
                claimedStoneTilemap.SetTileFlags(cell, TileFlags.None);
                claimedStoneTilemap.SetColor(cell, tint);
            }
        }

        if (minedFloorTilemap != null && minedFloorTile != null)
        {
            foreach (var cell in minedTiles)
                minedFloorTilemap.SetTile(cell, minedFloorTile);
        }
    }

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
    /// PHASE 5 — Wraps the restore loop in isLoading=true so per-cell paint
    /// helpers bail out. After the data is restored, RepaintAllStateLayers
    /// does a single bulk paint of both state layers.
    ///
    /// PHASE 6 — RepaintAllStateLayers now also applies the two-zone stone
    /// tint per cell, so loaded games reproduce the visual state correctly.
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
            if (minedFloorTilemap != null) minedFloorTilemap.ClearAllTiles();

            // Restore claimed cells via silent ClaimTile (sets fog, ring, etc.).
            // ClaimTile is claim-only in Phase 2, so this populates claimedTiles only.
            // PHASE 5 — Stone paint inside ClaimTile is suppressed by isLoading.
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
        }
        finally
        {
            isLoading = false;
        }

        // PHASE 5 — Single bulk repaint of both state layers from restored data.
        // PHASE 6 — Applies two-zone tinting in the same pass.
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

    /// <summary>
    /// Cells dug out / walkable / buildable.
    /// PHASE 6 — No longer a strict subset of claimedTiles. May contain cells
    /// that were mined then later unclaimed.
    /// </summary>
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