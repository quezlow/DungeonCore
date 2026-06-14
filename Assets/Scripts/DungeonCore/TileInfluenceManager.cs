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
///     gating happens upstream in DungeonBuildController.
///   - Passive expansion still skips rivers.
///
/// INFLUENCE/MINING DECOUPLING — PHASES 1, 2, 5, 6 (see prior phase notes).
///
/// INFLUENCE/MINING DECOUPLING — PHASE 7 (north walls + river paint fix)
///   - New tilemap: northWallTilemap, sort order 35 (between MinedFloorLayer
///     30 and ClaimableLayer 40). New tile asset: northWallTile (RT_NorthWall
///     RuleTile, rotation disabled, connects east/west only).
///   - Painting rule: a cell c gets a wall iff c is in minedTiles AND
///     (c + Vector3Int.up) is NOT in minedTiles. The wall sprite represents
///     the south face of the stone wall NORTH of c, drawn over c's mined floor.
///   - New helpers: ShouldHaveNorthWall, RefreshNorthWallAt.
///   - Paint hooks in MineTile (refresh self + south neighbour), end of
///     ClaimStarterArea (refresh all 9 starter cells now that mining is final),
///     and RepaintAllStateLayers (single-pass iterate minedTiles, paint where
///     ShouldHaveNorthWall is true).
///   - UnclaimTile and ShrinkInfluenceAroundCore do NOT touch the wall layer —
///     mined state is preserved post-Phase-6, so wall configuration is too.
///
///   - BONUS RIVER FIX (found while testing Phase 6): when a river cell is
///     claimed, PaintClaimedStone was painting stone over the river's blue
///     floor. Now PaintClaimedStone, RepaintAllStateLayers, and MineTile all
///     skip cells where Features.IsRiver returns true. Rivers stay visually
///     blue when claimed; mining is explicitly rejected on rivers (they're
///     not stone-to-be-dug). Bridges / drainage are a future-phase concern.
///
/// INFLUENCE/MINING DECOUPLING — PHASE 8 (pre-revealed core cavern)
///   - The core cavern + tunnels (Floor 0 only) are pre-revealed at game
///     start as MINED-BUT-UNCLAIMED floor — a valid post-Phase-6 data state
///     elevated to a starting state. Player still begins with influence on
///     the 9-cell starter area only; the dark cavern gives visual context
///     for where they woke up.
///   - New SerializeField: revealedDarkMinedColor. Mined-floor cells render
///     with this tint when NOT in claimedTiles; with Color.white when claimed.
///     PaintMinedFloor and RepaintAllStateLayers apply the rule.
///   - New helper: RefreshMinedFloorTintAt. Hooked into ClaimTile (flip dark
///     → bright on claim), UnclaimTile (flip bright → dark on unclaim), and
///     ShrinkInfluenceAroundCore (same as Unclaim, per affected cell).
///   - New public API: PreRevealMinedCells(IEnumerable&lt;Vector3Int&gt;).
///     Adds cells to minedTiles, paints them, refreshes adjacent stone tints
///     and north walls. Idempotent. Defensive river skip (cavern construction
///     reserves cavern cells from river overlap, but PaintMinedFloor mirrors
///     PaintClaimedStone's river skip for future-proofing).
///   - Q7 FIX: UnclaimTile and ShrinkInfluenceAroundCore now skip
///     terrain.RefogTile for cells that belong to FeatureType.CoreCavern.
///     The cavern is permanently visible by design — losing influence over a
///     cavern cell shouldn't re-hide it.
///
///   - Visual stack (Default sorting layer, current spacing):
///        0 FloorLayer        — base brown / river floor, painted by DungeonTerrain
///       10 FogLayer
///       20 ClaimedStoneLayer
///       30 MinedFloorLayer   — Phase 8: tint = white if claimed, revealedDarkMinedColor otherwise
///       35 NorthWallLayer    — Phase 7
///       40 ClaimableLayer    — gold ring
///       50 RoomHighlightTilemap
///   - Save format unchanged from Phase 2 (still v2). Phase 8 state is fully
///     derived from claimedTiles + minedTiles, both already persisted.
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

    [Header("North Walls (PHASE 7)")]
    [Tooltip("PHASE 7 — Sort order 35. Painted with northWallTile on every " +
             "cell in minedTiles whose north neighbour is NOT in minedTiles. " +
             "Renders above mined floor but below characters / monsters / " +
             "furniture. No TilemapCollider2D required.")]
    [SerializeField] private Tilemap northWallTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Tooltip("PHASE 5 — RuleTile asset for claimed-not-mined stone. Painted " +
             "into claimedStoneTilemap.")]
    [SerializeField] private TileBase claimedStoneTile;

    [Tooltip("PHASE 5 — RuleTile asset for mined floor. Painted into " +
             "minedFloorTilemap.")]
    [SerializeField] private TileBase minedFloorTile;

    [Tooltip("PHASE 7 — RuleTile asset for the north wall (RT_NorthWall). " +
             "Rotation disabled. Connection rules check east/west neighbours " +
             "only against other RT_NorthWall tiles. Four cases: isolated, " +
             "left-end, right-end, middle-of-run.")]
    [SerializeField] private TileBase northWallTile;

    [Header("Stone Tinting (PHASE 6)")]
    [Tooltip("PHASE 6 — Tint applied to claimed-stone cells with NO mined " +
             "cardinal neighbour. Stone cells WITH a mined cardinal neighbour " +
             "render bright (untinted white = the RuleTile's natural color). " +
             "Default ~(0.55, 0.55, 0.55, 1) = mid gray.")]
    [SerializeField] private Color darkStoneColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Mined Floor Tinting (PHASE 8)")]
    [Tooltip("PHASE 8 — Tint applied to mined-floor cells that are NOT in " +
             "claimedTiles (pre-revealed cavern, breached-then-unclaimed, etc). " +
             "Cells in claimedTiles render with Color.white (the RuleTile's " +
             "natural color). Default ~(0.45, 0.45, 0.5, 1) = cool dark grey. " +
             "Pick a value that reads distinctly from darkStoneColor so the " +
             "player can tell unclaimed mined floor from unclaimed stone.")]
    [SerializeField] private Color revealedDarkMinedColor = new Color(0.45f, 0.45f, 0.5f, 1f);

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
    // unclaimed via breach shrink or other influence loss).
    // PHASE 8 — Also includes pre-revealed cavern + tunnel cells at game start.
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
    /// PHASE 4 — Bootstraps a floor's starter area: a 3×3 around centerCell.
    /// PHASE 5 — Paints stone + mined-floor layers as cells are added.
    /// PHASE 6 — End-of-method stone tint refresh over all 9 cells.
    /// PHASE 7 — End-of-method north-wall refresh over all 9 cells (mining
    /// in Steps 1, 3, 4 bypasses MineTile's hook).
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
        PaintMinedFloor(corePos);                                    // PHASE 5 (+ Phase 8 tint = white, cell is claimed)

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
                PaintMinedFloor(pos);                                // PHASE 5 (+ Phase 8 tint = white, cell is claimed)
                surroundingMinedCount++;
            }
        }

        // Step 4 — Connectivity guarantee.
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

        // PHASE 6 — Refresh stone tints after mining is final.
        foreach (var pos in allCells)
            RefreshStoneTintAt(pos);

        // PHASE 7 — Refresh walls after mining is final.
        foreach (var pos in allCells)
            RefreshNorthWallAt(pos);

        StartPassiveExpansion();
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Claim (Phase 2: claim-only, no mining) ────────────────────

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
        PaintClaimedStone(pos);                                      // PHASE 5 (+ Phase 6 tint, Phase 7 river skip)

        // PHASE 8 — If this cell was pre-revealed (or previously mined and
        // since unclaimed), flip its mined-floor tint from dark to bright.
        RefreshMinedFloorTintAt(pos);

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
            DungeonCore.Instance?.AddClaimedTiles(1);
            OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        }
    }

    // ── Mine (Phase 2: new action) ────────────────────────────────

    /// <summary>
    /// PHASE 2 — Digs a claimed cell into walkable floor.
    /// PHASE 5 — Paints the mined-floor layer.
    /// PHASE 6 — Refreshes stone tint on the 4 cardinal neighbours.
    /// PHASE 7 — Refreshes north wall on self (may have just gained one) and
    /// on the south neighbour (may have just lost its wall — its north
    /// neighbour, pos, is now mined). Also explicitly rejects river cells.
    /// </summary>
    public void MineTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;
        if (minedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // PHASE 7 — Rivers aren't mineable.
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
        PaintMinedFloor(pos);                                        // PHASE 5 (+ Phase 8 tint = white, cell is claimed)

        // PHASE 6 — Brighten any adjacent claimed-stone cells.
        foreach (var dir in Neighbours)
            RefreshStoneTintAt(pos + dir);

        // PHASE 7 — Walls.
        RefreshNorthWallAt(pos);
        RefreshNorthWallAt(pos + Vector3Int.down);

        OnTileMined?.Invoke(pos);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    public void ClaimAndMineTile(Vector3Int pos, bool silent = false)
    {
        ClaimTile(pos, silent);
        MineTile(pos);
    }

    // ── PHASE 8 — Pre-Reveal API ──────────────────────────────────

    /// <summary>
    /// PHASE 8 — Adds the given cells to minedTiles WITHOUT claiming them.
    /// Used to seed the core cavern + tunnels as mined-but-unclaimed at game
    /// start. Cells receive the dark mined-floor tint (revealedDarkMinedColor)
    /// because they're not in claimedTiles. Adjacent claimed-stone tints and
    /// north walls are refreshed at the end of the batch so cavern boundaries
    /// render correctly.
    ///
    /// Idempotent: cells already in minedTiles are skipped. Cells outside the
    /// floor bounds or that are rivers are skipped defensively.
    ///
    /// Does NOT fire OnTileMined per cell (those cells weren't "mined" by
    /// player action). Fires OnTileCountChanged once at the end if anything
    /// changed.
    /// </summary>
    public void PreRevealMinedCells(IEnumerable<Vector3Int> cells)
    {
        if (cells == null) return;

        // Materialise so we can iterate twice (paint pass + wall refresh).
        var toReveal = new List<Vector3Int>();
        foreach (var cell in cells)
        {
            if (terrain != null && !terrain.IsWithinBounds(cell)) continue;
            if (Features != null && Features.IsRiver(cell)) continue;   // defensive
            if (minedTiles.Add(cell))
                toReveal.Add(cell);
        }

        if (toReveal.Count == 0)
        {
            Debug.Log("[TileInfluenceManager] PreRevealMinedCells: nothing new to add.");
            return;
        }

        // Paint pass — PaintMinedFloor applies dark tint since cells aren't claimed.
        foreach (var cell in toReveal)
            PaintMinedFloor(cell);

        // Stone tint refresh — adjacent claimed-stone cells may have gained
        // a mined neighbour and should brighten. The cavern is centred on the
        // starter 3×3, so this covers starter cells flanking newly-mined
        // cavern cells.
        foreach (var cell in toReveal)
            foreach (var dir in Neighbours)
                RefreshStoneTintAt(cell + dir);

        // Wall refresh — refresh each revealed cell (may have just gained a
        // north wall) AND its south neighbour (its north wall may have just
        // disappeared because the cell to its north is now mined).
        foreach (var cell in toReveal)
        {
            RefreshNorthWallAt(cell);
            RefreshNorthWallAt(cell + Vector3Int.down);
        }

        OnTileCountChanged?.Invoke(minedTiles.Count);
        Debug.Log($"[TileInfluenceManager] PreRevealMinedCells: added {toReveal.Count} " +
                  $"mined-but-unclaimed cells (cavern/tunnel pre-reveal).");
    }

    // ── Unclaim / Shrink ──────────────────────────────────────────

    /// <summary>
    /// PHASE 6 — Removes only the claim. Mined state and any painted wall
    /// are preserved. Wall configuration is fully determined by minedTiles,
    /// which doesn't change here, so no wall refresh is needed either.
    /// PHASE 8 — Mined-floor tint flips bright → dark on unclaim. Core cavern
    /// cells are NOT refogged (the cavern stays permanently visible).
    /// </summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        claimedTiles.Remove(pos);

        // PHASE 8 — Skip refog for pre-revealed cavern cells. The player
        // should retain visual knowledge of the cavern even after losing
        // influence over it.
        if (Features == null || !Features.IsCoreCavern(pos))
            terrain?.RefogTile(pos);

        ClearClaimedStoneAt(pos);

        // PHASE 8 — Flip mined-floor tint back to dark if this cell is still mined.
        RefreshMinedFloorTintAt(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
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

        foreach (var cell in toRemove)
        {
            claimedTiles.Remove(cell);

            // PHASE 8 — Skip refog for pre-revealed cavern cells.
            if (Features == null || !Features.IsCoreCavern(cell))
                terrain?.RefogTile(cell);

            ClearClaimedStoneAt(cell);

            // PHASE 8 — Flip mined-floor tint back to dark if still mined.
            RefreshMinedFloorTintAt(cell);
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(toRemove.Count);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} claimed " +
                  $"(minedTiles + walls preserved per Phase 6/7).");
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

    // ── PHASE 5 / 6 / 7 / 8 — Visual State Layer Helpers ──────────

    /// <summary>
    /// PHASE 5 — Paints the claimed-stone tile.
    /// PHASE 6 — Applies bright (Color.white) or dark (darkStoneColor) tint
    /// based on IsAdjacentToMined.
    /// PHASE 7 — River cells are skipped entirely. Rivers stay visually blue.
    /// </summary>
    private void PaintClaimedStone(Vector3Int cell)
    {
        if (isLoading) return;
        if (claimedStoneTilemap == null || claimedStoneTile == null) return;
        if (Features != null && Features.IsRiver(cell)) return;     // PHASE 7

        claimedStoneTilemap.SetTile(cell, claimedStoneTile);

        Color tint = IsAdjacentToMined(cell) ? Color.white : darkStoneColor;
        claimedStoneTilemap.SetTileFlags(cell, TileFlags.None);
        claimedStoneTilemap.SetColor(cell, tint);
    }

    /// <summary>
    /// PHASE 5 — Paints the mined-floor tile.
    /// PHASE 8 — Applies bright (Color.white) tint if claimed, dark
    /// (revealedDarkMinedColor) tint otherwise. Defensive river skip
    /// (rivers stay visually blue regardless of claim/mined state).
    /// </summary>
    private void PaintMinedFloor(Vector3Int cell)
    {
        if (isLoading) return;
        if (minedFloorTilemap == null || minedFloorTile == null) return;
        if (Features != null && Features.IsRiver(cell)) return;     // PHASE 8 (defensive)

        minedFloorTilemap.SetTile(cell, minedFloorTile);

        Color tint = claimedTiles.Contains(cell) ? Color.white : revealedDarkMinedColor;
        minedFloorTilemap.SetTileFlags(cell, TileFlags.None);
        minedFloorTilemap.SetColor(cell, tint);
    }

    /// <summary>
    /// PHASE 6 — Clears ONLY the claimed-stone layer. Mined-floor and wall
    /// layers are left alone because mined state is preserved through unclaim.
    /// </summary>
    private void ClearClaimedStoneAt(Vector3Int cell)
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.SetTile(cell, null);
    }

    private bool IsAdjacentToMined(Vector3Int cell)
    {
        foreach (var dir in Neighbours)
            if (minedTiles.Contains(cell + dir)) return true;
        return false;
    }

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
    /// PHASE 8 — Re-applies the mined-floor tint based on current claim state.
    /// Bails if the cell isn't mined or has no painted tile. Safe to call on
    /// any cell — handles the no-op cases.
    /// </summary>
    private void RefreshMinedFloorTintAt(Vector3Int cell)
    {
        if (isLoading) return;
        if (minedFloorTilemap == null || minedFloorTile == null) return;
        if (!minedTiles.Contains(cell)) return;
        if (!minedFloorTilemap.HasTile(cell)) return;
        if (Features != null && Features.IsRiver(cell)) return;     // defensive

        Color tint = claimedTiles.Contains(cell) ? Color.white : revealedDarkMinedColor;
        minedFloorTilemap.SetTileFlags(cell, TileFlags.None);
        minedFloorTilemap.SetColor(cell, tint);
    }

    /// <summary>
    /// PHASE 7 — A cell c needs a north-wall tile iff c is mined AND the cell
    /// directly north of c (c + Vector3Int.up) is NOT mined. The wall sprite
    /// represents the south face of the stone wall north of c.
    /// </summary>
    private bool ShouldHaveNorthWall(Vector3Int cell)
    {
        return minedTiles.Contains(cell) && !minedTiles.Contains(cell + Vector3Int.up);
    }

    /// <summary>
    /// PHASE 7 — Re-applies the north-wall tile (or clears it) for the given
    /// cell. Safe to call on any cell — handles the no-op case where the cell
    /// isn't mined. Bails during bulk load (RepaintAllStateLayers handles that).
    /// </summary>
    private void RefreshNorthWallAt(Vector3Int cell)
    {
        if (isLoading) return;
        if (northWallTilemap == null || northWallTile == null) return;

        if (ShouldHaveNorthWall(cell))
            northWallTilemap.SetTile(cell, northWallTile);
        else
            northWallTilemap.SetTile(cell, null);
    }

    /// <summary>
    /// Bulk repaint of stone, mined-floor, and wall layers from current
    /// claimedTiles / minedTiles. Called once at the end of LoadSaveData.
    ///
    /// PHASE 6 — Stone iteration applies the two-zone tint per cell.
    ///           River cells are skipped (stays blue).
    /// PHASE 7 — Wall layer cleared and rebuilt via a single iteration over
    /// minedTiles, painting where ShouldHaveNorthWall is true.
    /// PHASE 8 — Mined-floor iteration applies bright/dark tint based on
    /// claimedTiles membership; rivers skipped defensively.
    /// </summary>
    public void RepaintAllStateLayers()
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.ClearAllTiles();
        if (minedFloorTilemap != null) minedFloorTilemap.ClearAllTiles();
        if (northWallTilemap != null) northWallTilemap.ClearAllTiles();

        if (claimedStoneTilemap != null && claimedStoneTile != null)
        {
            foreach (var cell in claimedTiles)
            {
                // PHASE 7 — Skip rivers; they keep their blue floor tile.
                if (Features != null && Features.IsRiver(cell)) continue;

                claimedStoneTilemap.SetTile(cell, claimedStoneTile);

                Color tint = IsAdjacentToMined(cell) ? Color.white : darkStoneColor;
                claimedStoneTilemap.SetTileFlags(cell, TileFlags.None);
                claimedStoneTilemap.SetColor(cell, tint);
            }
        }

        if (minedFloorTilemap != null && minedFloorTile != null)
        {
            foreach (var cell in minedTiles)
            {
                // PHASE 8 — Defensive river skip.
                if (Features != null && Features.IsRiver(cell)) continue;

                minedFloorTilemap.SetTile(cell, minedFloorTile);

                // PHASE 8 — Bright if claimed, dark (revealedDarkMinedColor) otherwise.
                Color tint = claimedTiles.Contains(cell) ? Color.white : revealedDarkMinedColor;
                minedFloorTilemap.SetTileFlags(cell, TileFlags.None);
                minedFloorTilemap.SetColor(cell, tint);
            }
        }

        // PHASE 7 — Walls. One pass over minedTiles.
        if (northWallTilemap != null && northWallTile != null)
        {
            foreach (var cell in minedTiles)
            {
                if (ShouldHaveNorthWall(cell))
                    northWallTilemap.SetTile(cell, northWallTile);
            }
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
            if (northWallTilemap != null) northWallTilemap.ClearAllTiles();

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
    /// PHASE 6 — No longer a strict subset of claimedTiles.
    /// PHASE 8 — Also includes pre-revealed cavern + tunnel cells.
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