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
///   - UnclaimTile removes from BOTH sets (a mined cell that isn't claimed
///     makes no sense) and fires both events as needed.
///   - Save format unchanged from Phase 1 (still v2). Loading a Phase 1 save
///     where claimed == mined works correctly because both lists are present.
///
/// INFLUENCE FIELD REWORK — SESSION 1 (growth model replaced)
///   - Random passive expansion removed. Free growth now lives in the floor's
///     InfluenceField: a per-floor cost-distance field claims the cheapest
///     frontier cells within the core's level-based reach (ambient creep plus
///     a level-up surge).
///   - ShrinkInfluenceAroundCore replaced by UnclaimTilesBatch. Breach recede
///     is driven by InfluenceField: reach is suppressed on first breach and
///     recovers across the instability window while the creep regrows the edge.
///   - New ClaimableTiles read-only view for allocation-free frontier scans.
///
/// INFLUENCE FIELD REWORK — SESSION 3 (ring visuals replaced)
///   - The gold claimable-ring tiles and their resistance tints are retired.
///     The claimable SET is unchanged — creep, channel, and gating still use
///     it — but the frontier is now drawn by InfluenceRingRenderer's boundary
///     shader. claimableTilemap remains solely as the floor's WorldToCell /
///     CellToWorld coordinate service and never receives tiles.
/// </summary>
[DefaultExecutionOrder(0)]
public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [Tooltip("Coordinate service only (WorldToCell / CellToWorld). Never receives tiles.")]
    [SerializeField] private Tilemap claimableTilemap;

    [Header("Starter Area")]
    [Tooltip("Radius (cells) of the blob-shaped starter room. ~3 ≈ a 6-wide room.")]
    [SerializeField, Min(1f)] private float starterRoomRadius = 3f;

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

    /// Fires when minedTiles.Count changes. HUD subscribers use this.</summary>
    public event Action<int> OnTileCountChanged;

    /// Fires when claimedTiles.Count changes.</summary>
    public event Action<int> OnClaimedTileCountChanged;

    /// Fires per cell newly added to minedTiles.</summary>
    public event Action<Vector3Int> OnTileMined;

    /// Fires per cell newly claimed. Live claims only; save-restore claims are silent.</summary>
    public event Action<Vector3Int> OnTileClaimed;

    /// Fires whenever a cell enters the claimable ring.</summary>
    public event Action<Vector3Int> OnTileBecameClaimable;

    // ── Internal ──────────────────────────────────────────────────

    private DungeonTerrain terrain;
    private float lastMineRefusalBark = -999f;   // throttles the cannot-dig-there bark

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

    // Bedrock border ring (unminable rim): bedrock is never added to the claimable
    // set, so it can never be claimed and therefore never mined. Cells carved by
    // the entrance cave are exempt — the tunnel through the rim is claimable
    // ground (claim cost applies, no dig cost) so influence can reach the mouth.
    private bool IsBedrock(Vector3Int cell)
    {
        if (MyFloor == null || MyFloor.TerrainTypeMap == null) return false;
        if (!MyFloor.TerrainTypeMap.IsBedrock(cell)) return false;
        var features = MyFloor.FeatureGenerator;
        if (features != null && features.IsEntranceCave(cell)) return false;
        return true;
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
            // is consistent with Floor 2+. Free growth is driven by the floor's
            // InfluenceField component.
            ClaimStarterArea(terrain.CoreCell);
        }
    }

    public void InjectTerrain(DungeonTerrain t) => terrain = t;

    // ── Bootstrap (Floor 2+) ──────────────────────────────────────

    /// <summary>
    /// PHASE 4 — Bootstraps a floor's starter area: a blob-shaped room (a noisy disc
    /// of radius starterRoomRadius) around centerCell.
    ///
    /// Every cell in the blob is claimed AND mined, so the room reads as open floor
    /// with an organic edge rather than a 3×3 box. The shape is seeded by centerCell
    /// so it is deterministic — floor 0 re-runs this on every scene load, and a stable
    /// seed keeps its room from churning across save cycles.
    /// </summary>
    public void ClaimStarterArea(Vector3Int centerCell)
    {
        // Build a blob-shaped starter room (a noisy disc) instead of a 3×3 square.
        // Seeded by centerCell so the shape is deterministic — floor 0 re-runs this on
        // every scene load, and a stable seed stops its room churning across saves.
        var rng = new System.Random(centerCell.GetHashCode());
        float r = starterRoomRadius;                 // ~3 → roughly a 6-wide room
        float inner = Mathf.Max(1f, r - 1.25f);      // always-open core
        int span = Mathf.CeilToInt(r);

        var roomCells = new List<Vector3Int>();
        for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            {
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > r) continue;
                if (dist > inner)
                {
                    // Ragged edge: keep-probability falls from 1 at the core to ~0 past r.
                    float keep = Mathf.InverseLerp(r + 0.5f, inner, dist);
                    if (rng.NextDouble() > keep) continue;
                }
                roomCells.Add(centerCell + new Vector3Int(dx, dy, 0));
            }
        if (!roomCells.Contains(centerCell)) roomCells.Add(centerCell);

        // Mine + reveal the whole blob — the open room is the full ~6-wide cavern.
        foreach (var pos in roomCells)
        {
            minedTiles.Add(pos);
            terrain?.RevealTile(pos);
        }

        // Reveal the 1-cell wall border too, so the cavern's wall caps sit on
        // revealed ground. Fog left under a cap shows through its transparent
        // edges as a dark rim (mirrors RevealWithBorder for features).
        foreach (var pos in roomCells)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        terrain?.RevealTile(new Vector3Int(pos.x + dx, pos.y + dy, pos.z));

        // CLAIM only the central 3×3 — owned territory stays small on every floor.
        // The rest of the cavern is open but unclaimed; the player claims outward into it.
        var claimCells = new List<Vector3Int>
        {
            centerCell,
            centerCell + new Vector3Int(-1, -1, 0), centerCell + new Vector3Int(0, -1, 0), centerCell + new Vector3Int(1, -1, 0),
            centerCell + new Vector3Int(-1,  0, 0),                                          centerCell + new Vector3Int(1,  0, 0),
            centerCell + new Vector3Int(-1,  1, 0), centerCell + new Vector3Int(0,  1, 0), centerCell + new Vector3Int(1,  1, 0),
        };
        foreach (var pos in claimCells)
        {
            if (claimedTiles.Contains(pos)) continue;
            claimedTiles.Add(pos);
            everClaimed.Add(pos);
            claimableTiles.Remove(pos);
            terrain?.RevealTile(pos);
        }

        // Expand the claimable ring around the claimed 3×3.
        foreach (var pos in claimCells)
        {
            foreach (var dir in Neighbours)
            {
                Vector3Int neighbour = pos + dir;
                if (claimedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;
                if (IsBedrock(neighbour)) continue;

                claimableTiles.Add(neighbour);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }

        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Claim (Phase 2: claim-only, no mining) ────────────────────

    /// <summary>Cells that have EVER been claimed. Additive only -- a breach
    /// recede removes ownership but never removes membership here. DungeonShadow
    /// keys its void lighting off this set, which is what stops receded rock from
    /// dropping to unlit black. See the "breach never darkens" note in canon 12A.</summary>
    private readonly HashSet<Vector3Int> everClaimed = new HashSet<Vector3Int>();

    /// <summary>True if this cell is or ever was inside the influence field.</summary>
    public bool WasEverClaimed(Vector3Int pos) => everClaimed.Contains(pos);

    /// <summary>Every cell ever claimed. Read by DungeonShadow's rimless sweep.</summary>
    public IReadOnlyCollection<Vector3Int> EverClaimedTiles => everClaimed;

    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (claimedTiles.Contains(pos)) return;
        if (terrain != null && !terrain.IsWithinBounds(pos)) return;
        if (!silent && IsBedrock(pos)) return;   // bedrock rim: unclaimable

        // Chamber gate — uncleared chambers cannot be claimed.
        // silent: true bypasses for save-restore.
        if (!silent && Features != null)
        {
            if (Features.IsCellInUnclearedChamber(pos)) return;
        }

        claimedTiles.Add(pos);
        everClaimed.Add(pos);   // permanent: a breach takes ownership, never light
        // PHASE 2 — no longer adds to minedTiles. Mining is a separate action.

        claimableTiles.Remove(pos);
        terrain?.RevealTile(pos);                                   // claimed cells are visible

        // Expand the claimable ring.
        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = pos + dir;
            if (claimedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;
            if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;
            if (IsBedrock(neighbour)) continue;

            claimableTiles.Add(neighbour);
            OnTileBecameClaimable?.Invoke(neighbour);
        }

        if (!silent)
        {
            // DungeonCore.ownedTileCount tracks CLAIMED count after Phase 2.
            // Mana regen formula (baseRegen + ownedTileCount * perTile) scales
            // with claimed, per P3-Q1.
            DungeonCore.Instance?.AddClaimedTiles(1);
            OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);

            // Material pattern discovery -- the first live claim of each
            // terrain type teaches its pattern. Save-restore claims pass
            // silent and never reach this.
            if (MyFloor != null && MyFloor.TerrainTypeMap != null)
                PatternDiscovery.NotifyTerrainClaimed(
                    MyFloor.TerrainTypeMap.GetTerrainAt(pos),
                    CellToWorld(pos), MyFloor.FloorIndex);

            OnTileClaimed?.Invoke(pos);
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
                if (Features != null && Features.IsRiver(pos + dir)) { hasAdjacentMined = true; break; }
            }
            if (!hasAdjacentMined)
            {
                // A silent no-op here once cost a whole session: claimed stone
                // LOOKS diggable, but a mine-click that does not touch already
                // carved ground does nothing -- and every later click down the
                // same chain fails too, leaving a tunnel that is tinted but never
                // dug. Say so once, throttled, so the break is visible when it
                // happens rather than surfacing later as entities that will not move.
                if (Time.unscaledTime - lastMineRefusalBark > 8f)
                {
                    lastMineRefusalBark = Time.unscaledTime;
                    WispCompanion.Instance?.SpeakLine(
                        "The stone will not yield there -- dig from ground we have already carved.");
                }
                return;
            }
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
    /// <summary>Registers pre-existing open ground as walkable (mined) but unclaimed.
    /// Bedrock is filtered out: a river's dry banks can fall inside the rim, and
    /// without this guard those cells became pre-mined walkable floor punching a
    /// second, unintended entrance straight through the sealed border. The claim-side
    /// IsBedrock guard could never catch it because pre-mined ground never needs
    /// claiming. Entrance-cave cells are exempt inside IsBedrock, so the real tunnel
    /// through the rim still registers.</summary>
    public void MarkNaturalFloor(IEnumerable<Vector3Int> cells)
    {
        if (cells == null) return;
        bool any = false;
        foreach (var cell in cells)
        {
            if (IsBedrock(cell)) continue;   // never open a hole in the rim
            if (minedTiles.Add(cell)) any = true;
        }
        if (any) OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    // ── Unclaim / Shrink ──────────────────────────────────────────

    public void UnclaimTile(Vector3Int pos)
    {
        if (!claimedTiles.Contains(pos)) return;

        bool wasMined = minedTiles.Contains(pos);

        claimedTiles.Remove(pos);
        if (wasMined) minedTiles.Remove(pos);
        // Revealed ground stays revealed: unclaiming strips ownership only, it
        // never re-fogs what the player (or the terrain generator) has shown.

        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveClaimedTiles(1);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);
        if (wasMined) OnTileCountChanged?.Invoke(minedTiles.Count);
    }

    /// <summary>
    /// Unclaims a batch of cells in one pass: removes each from claimedTiles,
    /// then rebuilds the claimable ring and
    /// fires the count event once. Used by InfluenceField's breach recede — far
    /// cheaper than per-cell UnclaimTile, which rebuilds the ring every call.
    /// Recede shrinks ownership only: a dug tunnel persists, so mined cells
    /// keep their mined state and stay revealed.
    /// </summary>
    public void UnclaimTilesBatch(IReadOnlyCollection<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return;

        int removed = 0;
        foreach (Vector3Int cell in cells)
        {
            if (!claimedTiles.Remove(cell)) continue;
            removed++;
            // A breach pulls back INFLUENCE ONLY -- it never re-fogs. What the
            // core has once seen it keeps; losing ground costs ownership, not
            // knowledge.
        }

        if (removed == 0) return;

        RebuildClaimableSet();
        DungeonCore.Instance?.RemoveClaimedTiles(removed);
        OnClaimedTileCountChanged?.Invoke(claimedTiles.Count);

        Debug.Log($"[TileInfluenceManager] Recede unclaimed {removed} cell(s); mined tunnels preserved.");
    }

    // ── Bounds ────────────────────────────────────────────────────────

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
                if (IsBedrock(neighbour)) continue;

                claimableTiles.Add(neighbour);
                OnTileBecameClaimable?.Invoke(neighbour);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private void RebuildClaimableSet()
    {
        claimableTiles.Clear();

        foreach (Vector3Int owned in claimedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (claimedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (terrain != null && !terrain.IsWithinBounds(neighbour)) continue;
                if (IsBedrock(neighbour)) continue;

                claimableTiles.Add(neighbour);
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

        // A south-facing wall's draped face is two cells tall and covers the two open
        // cells directly south of it (upper slice at wall+S, lower slice at wall+2S).
        // Both read as wall, so neither is walkable:
        //   - a solid cell directly north      -> pos is the face's UPPER slice.
        //   - one open cell north, then solid   -> pos is the face's LOWER slice.
        // Cells beyond the floor disc are open air, not rock: nothing drapes from
        // the surface, so they are never a face source. This mirrors
        // CaveWallClassifier.IsSolid so walkability and visuals agree at the breach.
        if (DrapesFrom(pos + Vector3Int.up)) return true;
        return DrapesFrom(pos + new Vector3Int(0, 2, 0));
    }

    /// <summary>True if a face would drape from this cell: unmined rock inside the
    /// floor disc. Out-of-disc cells are open surface and never drape.</summary>
    private bool DrapesFrom(Vector3Int cell)
    {
        if (minedTiles.Contains(cell)) return false;
        var terrain = MyFloor != null ? MyFloor.Terrain : null;
        return terrain == null || terrain.IsWithinBounds(cell);
    }
    public bool IsTileClaimable(Vector3Int pos) => claimableTiles.Contains(pos);

    public IReadOnlyCollection<Vector3Int> ClaimedTiles => claimedTiles;
    public IReadOnlyCollection<Vector3Int> MinedTiles => minedTiles;
    public IReadOnlyCollection<Vector3Int> ClaimableTiles => claimableTiles;
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
            everClaimedTiles = everClaimed.Select(SerializableVector3Int.From).ToList(),
            ownedTiles = new List<SerializableVector3Int>(),
        };
    }

    public void LoadSaveData(TileInfluenceSaveData data)
    {
        claimedTiles.Clear();
        minedTiles.Clear();
        everClaimed.Clear();
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
        //
        // REVEAL them as well, with the one-cell wall border. Claimed cells come
        // back visible because the silent ClaimTile above still calls
        // RevealTile; mined-and-unclaimed ground had nothing doing that, so
        // every reload re-fogged the starter cavern's outer blob and any dug
        // ground the player had not also claimed. The wall caps around it are
        // still painted -- the renderer frames anything 8-adjacent to mined
        // floor -- so they sat under fog, invisible. It measured clean when the
        // site pass checked it by hand because that was a FRESH generation; the
        // fault only exists after a load. This mirrors ClaimStarterArea, which
        // reveals its blob AND a border for exactly this reason.
        if (data?.minedTiles != null)
        {
            foreach (var tile in data.minedTiles)
            {
                var cell = tile.ToVector3Int();
                minedTiles.Add(cell);
                terrain?.RevealTile(cell);
            }
            foreach (var cell in minedTiles)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0)
                            terrain?.RevealTile(new Vector3Int(cell.x + dx, cell.y + dy, cell.z));
        }

        // Ever-claimed is additive history, not current state, so it is saved in
        // its own list. Saves written before this field existed have none: fall
        // back to the restored claim set, which is exactly the pre-breach truth
        // for any dungeon that has not yet been breached.
        if (data?.everClaimedTiles != null && data.everClaimedTiles.Count > 0)
        {
            foreach (var tile in data.everClaimedTiles)
                everClaimed.Add(tile.ToVector3Int());
        }
        else
        {
            foreach (var cell in claimedTiles) everClaimed.Add(cell);
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

    /// <summary>Cells ever inside the influence field. Superset of claimedTiles --
    /// a breach recede shrinks claimedTiles but never this. Absent in older saves;
    /// LoadSaveData falls back to claimedTiles when it is null or empty.</summary>
    public List<SerializableVector3Int> everClaimedTiles;
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