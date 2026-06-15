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
///
/// INFLUENCE/MINING DECOUPLING — PHASE 5 (visual state layers — SUPERSEDED by PHASE A)
///   - Historical: two state-layer tilemaps (claimedStoneTilemap +
///     minedFloorTilemap) were painted on every claim/mine. The MinedFloorLayer
///     occluded the stone overlay beneath. See PHASE A for the current model.
///
/// INFLUENCE/MINING DECOUPLING — PHASE A (inverted paint model)
///   - The floor tilemap is pre-painted EVERYWHERE inside the floor radius by
///     DungeonTerrain at gen time (natural / mined-look floor sprite). Feature
///     cells (rivers, cavern, chambers) are over-painted by
///     TerrainFeatureGenerator.PaintFeatureFloorTiles with their own floor
///     variants — into the SAME floor tilemap.
///   - The CLAIMED-NOT-MINED visual state is rendered by painting a STONE
///     OVERLAY into claimedStoneTilemap. The stone tile (RT_ClaimedStone) is
///     a RuleTile that bakes wall edges into its variants based on 4-cardinal
///     same-tile neighbour matching. There is no separate wall layer.
///   - Mining a cell REMOVES the stone overlay (SetTile(cell, null)) which
///     reveals the natural floor below. The dedicated MinedFloorLayer is
///     deleted entirely from the scene/prefab.
///   - PaintClaimedStone skips cells that are: already mined, a river, a
///     chamber, or part of the core cavern (those cells display the natural
///     or feature floor — never stone).
///   - MineTile rejects river cells silently (rivers can be claimed but not
///     dug). Cavern / chamber cells let mining proceed for data consistency
///     (visually a no-op since no stone overlay exists there to remove).
///   - UnclaimTile and ShrinkInfluenceAroundCore no longer remove cells from
///     minedTiles — a cell that was mined stays mined permanently. Intent:
///     re-claiming a previously-mined cell shows as natural floor immediately,
///     because PaintClaimedStone's mined-check skips painting stone on it.
///   - Save format unchanged (still v3). State fully derives from
///     claimedTiles + minedTiles + featureData.
///
///   Visual stack (Default sorting layer):
///        0 FloorLayer        — natural floor everywhere; feature cells
///                              over-painted by TerrainFeatureGenerator.
///       10 FogLayer          — covers unrevealed cells.
///       20 ClaimedStoneLayer — RT_ClaimedStone overlay on claimed-not-mined
///                              non-feature cells; walls baked into the RuleTile.
///       30 ShadowLayer       — RESERVED for next chat (entity shadows).
///       40 ClaimableLayer    — gold ring; stays on top.
/// </summary>
[DefaultExecutionOrder(0)]
public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap claimableTilemap;

    [Header("Visual State Layers (PHASE A)")]
    [Tooltip("Lower-sort layer (sort 20). Painted with claimedStoneTile on " +
             "cells that are claimed AND NOT mined AND NOT a feature " +
             "(river/chamber/cavern). Wall edges are baked into RT_ClaimedStone " +
             "via 4-cardinal neighbour matching — no separate wall layer.")]
    [SerializeField] private Tilemap claimedStoneTilemap;

    [Tooltip("RESERVED for next chat (entity shadows at sort 30). Wiring this " +
             "is optional in this phase; leave null until the shadow layer ships.")]
    [SerializeField] private Tilemap shadowTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Tooltip("PHASE A — RuleTile asset for claimed-not-mined stone " +
             "(RT_ClaimedStone). Bakes wall-edge variants into its rules based " +
             "on 4-cardinal neighbour matching. Painted into claimedStoneTilemap.")]
    [SerializeField] private TileBase claimedStoneTile;

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
    /// PHASE A — Paint hooks:
    ///   - Step 1 (core cell) skips PaintClaimedStone — the cell is mined below
    ///     and the natural floor pre-painted by DungeonTerrain is what shows.
    ///   - Step 2 paints stone via PaintClaimedStone for each surrounding cell.
    ///   - Step 3/4 mining of surrounding cells calls ClearClaimedStone to
    ///     remove the overlay painted in Step 2 (mining == stone removal).
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
            // [PHASE A] No stone overlay on the core cell — it's mined below
            //           and the natural floor (pre-painted by DungeonTerrain) is
            //           what should be visible at this cell.
        }
        minedTiles.Add(corePos);
        // [PHASE A] No PaintMinedFloor — natural floor is pre-painted.

        // Step 2 — Claim all 8 surrounding cells. Mining decided in Step 3.
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (claimedTiles.Contains(pos)) continue;

            claimedTiles.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
            claimableTilemap.SetTile(pos, null);
            PaintClaimedStone(pos);                                  // [PHASE A] paints if !mined && !feature
        }

        // Step 3 — Random mining of surrounding cells.
        int surroundingMinedCount = 0;
        foreach (var offset in surroundingOffsets)
        {
            Vector3Int pos = centerCell + offset;
            if (UnityEngine.Random.value < starterMinedChance)
            {
                minedTiles.Add(pos);
                ClearClaimedStone(pos);                              // [PHASE A] mining = stone removal
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
            ClearClaimedStone(forcedPos);                            // [PHASE A]
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
        PaintClaimedStone(pos);                                      // [PHASE A] skips if mined or feature

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
    /// PHASE A — Removes the stone overlay (revealing the natural floor below).
    /// Rivers reject silently — water is claimable but cannot be mined. Cavern
    /// and chamber cells let mining proceed; ClearClaimedStone is a no-op there
    /// because PaintClaimedStone never painted those cells (they're features).
    /// </summary>
    public void MineTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;
        if (minedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;

        // [PHASE A] Rivers reject silently — water is claimable but not mineable.
        if (Features != null && Features.IsRiver(pos)) return;

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
        ClearClaimedStone(pos);                                      // [PHASE A] reveal natural floor

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

    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        claimedTiles.Remove(pos);
        // [PHASE A] No longer removes from minedTiles — a cell that was mined
        //           stays mined permanently. Re-claiming later shows it as
        //           natural floor (no stone overlay) because PaintClaimedStone
        //           skips mined cells.
        terrain?.RefogTile(pos);
        ClearClaimedStone(pos);

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        // [PHASE A] No OnTileCountChanged fire — minedTiles wasn't modified.
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

        // [PHASE A] No longer removes from minedTiles — see UnclaimTile rationale.
        foreach (var cell in toRemove)
        {
            claimedTiles.Remove(cell);
            terrain?.RefogTile(cell);
            ClearClaimedStone(cell);
        }

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(toRemove.Count);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        // [PHASE A] No OnTileCountChanged fire — minedTiles wasn't modified.

        Debug.Log($"[TileInfluenceManager] Breach shrink removed {toRemove.Count} claimed (mined cells preserved).");
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

    // ── PHASE A — Visual State Layer Helpers ──────────────────────

    /// <summary>
    /// PHASE A — Paints the claimed-stone overlay on claimedStoneTilemap.
    /// Skips when ANY of:
    ///   - isLoading is true (LoadSaveData does a single bulk repaint instead)
    ///   - the tilemap or tile asset is unwired
    ///   - the cell is already mined (mining removed the overlay; re-claim
    ///     should not re-add it — test 4 in the Phase A test plan)
    ///   - the cell is a river / chamber / core-cavern feature (those cells
    ///     display the natural or feature floor, never stone)
    /// The RuleTile (RT_ClaimedStone) handles wall-edge variant selection
    /// automatically via 4-cardinal same-tile matching; SetTile triggers
    /// neighbour refresh so adjacent stone cells re-render their variants.
    /// </summary>
    private void PaintClaimedStone(Vector3Int cell)
    {
        if (isLoading) return;
        if (claimedStoneTilemap == null || claimedStoneTile == null) return;
        if (minedTiles.Contains(cell)) return;
        if (Features != null)
        {
            if (Features.IsRiver(cell)) return;
            if (Features.IsChamber(cell)) return;
            if (Features.IsCoreCavern(cell)) return;
        }
        claimedStoneTilemap.SetTile(cell, claimedStoneTile);
    }

    /// <summary>
    /// PHASE A — Removes the stone overlay for the given cell. SetTile(null)
    /// triggers a RuleTile refresh on adjacent stone cells so their wall-edge
    /// variants re-render with the new "this neighbour is empty" boundary.
    /// Safe to call regardless of whether the cell currently has an overlay.
    /// </summary>
    private void ClearClaimedStone(Vector3Int cell)
    {
        if (claimedStoneTilemap == null) return;
        claimedStoneTilemap.SetTile(cell, null);
    }

    /// <summary>
    /// PHASE A — Bulk repaint of the claimed-stone layer from current state.
    /// Single iteration over claimedTiles; paints stone only on cells that are
    /// claimed AND NOT mined AND NOT a feature. Called once at the end of
    /// LoadSaveData. Public so a debug hotkey can re-sync visuals if data and
    /// visuals ever drift.
    /// </summary>
    public void RepaintAllStateLayers()
    {
        if (claimedStoneTilemap != null) claimedStoneTilemap.ClearAllTiles();

        if (claimedStoneTilemap == null || claimedStoneTile == null) return;

        var features = Features;
        foreach (var cell in claimedTiles)
        {
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
    /// does a single bulk paint of the stone overlay.
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

            // Restore claimed cells via silent ClaimTile (sets fog, ring, etc.).
            // ClaimTile is claim-only in Phase 2, so this populates claimedTiles only.
            // PHASE A — Stone paint inside ClaimTile is suppressed by isLoading.
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

        // PHASE A — Single bulk repaint of the stone overlay from restored data.
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