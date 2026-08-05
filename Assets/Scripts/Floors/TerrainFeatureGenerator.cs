using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Why a floor laid no sites. Exists because "LastSitePlacement is null" has
/// four causes and the report collapsed all four into "(restored from save)" --
/// which was then printed for a floor on a brand new game, sending the
/// diagnosis in exactly the wrong direction.
///
/// Runtime only, never serialized. NotRun is the correct value for a floor that
/// came through the load path, and it is the DEFAULT so a floor that never
/// reached GenerateSites reads honestly rather than claiming a reason.
/// </summary>
public enum SitePlacementSkip
{
    NotRun = 0,
    Placed = 1,
    NoProfileAssigned = 2,
    NoFloor = 3,
    NoEntryForFloor = 4,
}

/// <summary>
/// DAY 30 — Per-floor procedural feature generator.
/// DAY 31 PART 1 — Reveal API, conditional debug paint, pathfinding/fording knobs.
/// DAY 31 PART 2 — Wild monster pool, OnChamberRevealed event, chamber helpers + cleared API.
/// </summary>
[DefaultExecutionOrder(50)]
public class TerrainFeatureGenerator : MonoBehaviour
{
    // ── Inspector — Chambers ──────────────────────────────────────

    [Header("Chambers")]
    [SerializeField] private int minChambers = 3;
    [SerializeField] private int maxChambers = 6;
    [Tooltip("Edge length (cells) of the CA bounding box for a chamber. The actual chamber will be smaller.")]
    [SerializeField] private int minChamberBoxSize = 8;
    [SerializeField] private int maxChamberBoxSize = 14;
    [Tooltip("Probability a non-edge cell starts as a wall in the CA seed.")]
    [Range(0f, 1f)]
    [SerializeField] private float caInitialWallChance = 0.45f;
    [SerializeField] private int caSmoothingIterations = 4;
    [Tooltip("Discard a chamber if its connected floor region has fewer than this many cells.")]
    [SerializeField] private int minChamberCellCount = 6;
    [Tooltip("Reject a chamber centre within this many tiles of another chamber's centre.")]
    [SerializeField] private int chamberSpacing = 10;
    [Tooltip("Floor radius the authored chamber count is calibrated against. " +
             "Deeper floors scale their count up from here.")]
    [SerializeField, Min(1)] private int chamberReferenceRadius = 150;
    [Tooltip("Hard ceiling on chambers per floor after scaling, so a future radius " +
             "bump cannot quietly turn a floor into a warren.")]
    [SerializeField, Min(1)] private int chamberCountCeiling = 30;
    [Tooltip("Keep chambers clear of the outer bedrock rim: chamber centres are drawn " +
             "from a disc this many cells smaller than the floor radius, so a chamber " +
             "never opens into the unminable border ring. Cover the max rim thickness " +
             "plus a chamber's half-extent.")]
    [SerializeField, Min(0)] private int chamberRimMargin = 10;

    [Tooltip("How far in from the disc edge a river runs bank-free, in cells. Where a " +
             "river crosses the bedrock rim its channel is water wall-to-wall: dry banks " +
             "there would render as cave wall (banks are RiverBank, not River, so IsSolid " +
             "treats them as rock) and stand between the cave river and the forest river. " +
             "Cover the thickest possible rim -- TerrainTypeMap's maxRingThickness, 6 by " +
             "default -- plus a cell of slack. The rim map is generated AFTER features, so " +
             "bedrock cannot be queried directly here.")]
    [SerializeField, Min(0)] private int riverBankRimMargin = 7;

    // ── Inspector — Rivers ────────────────────────────────────────

    [Header("Rivers")]
    [SerializeField] private int minRivers = 1;
    [SerializeField] private int maxRivers = 3;
    [SerializeField] private int minRiverControlPoints = 8;
    [SerializeField] private int maxRiverControlPoints = 20;
    [SerializeField] private int riverSegmentLength = 3;
    [SerializeField] private float riverMeanderDegrees = 35f;
    [SerializeField] private int minRiverWidth = 2;
    [SerializeField] private int maxRiverWidth = 5;
    [Tooltip("Floor-bank thickness eroded inward from each side of a river. The river " +
             "footprint never grows; banks eat into the water. Clamped per-river so a " +
             "water channel always survives, so narrow rivers get thinner banks or none.")]
    [SerializeField] private int minRiverBank = 1;
    [SerializeField] private int maxRiverBank = 3;

    // ── Inspector — Exclusion ─────────────────────────────────────

    [Header("Exclusion Zone")]
    [SerializeField] private int exclusionRadiusFromCenter = 8;

    // ── Inspector — Core Cavern ───────────────────────

    [Header("Core Cavern")]
    [Tooltip("Inner disc radius (cells). Every cell within this radius is part of the cavern.")]
    [SerializeField, Min(1)] private int cavernInnerRadius = 2;
    [Tooltip("Outer disc radius (cells). Cells between inner and outer are noisy — included with falloff probability.")]
    [SerializeField, Min(1)] private int cavernOuterRadius = 3;
    [Tooltip("Minimum total cavern cells. Topped up by adjacent expansion if the noisy disc undershoots.")]
    [SerializeField, Min(4)] private int cavernMinCells = 10;
    [Tooltip("Maximum total cavern cells. Trimmed from outside in if overshooting.")]
    [SerializeField, Min(4)] private int cavernMaxCells = 16;

    [Header("Core Cavern — Tunnels")]
    [Tooltip("Weights for tunnel count 1 / 2 / 3 respectively. Must sum to > 0; ratios are what matter.")]
    [SerializeField] private int tunnelWeight1 = 20;
    [SerializeField] private int tunnelWeight2 = 50;
    [SerializeField] private int tunnelWeight3 = 30;
    [Tooltip("Minimum angular separation between tunnels, in degrees.")]
    [SerializeField, Range(45f, 180f)] private float tunnelMinAngleSeparation = 90f;
    [SerializeField, Min(1)] private int tunnelMinLength = 4;
    [SerializeField, Min(1)] private int tunnelMaxLength = 8;
    [Tooltip("Probability per step that the tunnel drifts one cell perpendicular to its direction.")]
    [SerializeField, Range(0f, 1f)] private float tunnelWobbleChance = 0.3f;
    [Tooltip("Tunnel width in cells. The centreline is dilated to this width (>= 4 recommended).")]
    [SerializeField, Min(2)] private int tunnelWidth = 4;
    [Tooltip("Tunnel width at the far tip. The tunnel tapers from tunnelWidth (mouth) to this.")]
    [SerializeField, Min(1)] private int tunnelTipWidth = 2;

    // ── Inspector — Entrance Cave (Floor 0) ───────────────────────

    [Header("Entrance Cave (Floor 0)")]
    [Tooltip("Tunnel length in cells, carved inward from the disc edge. Keep this above " +
             "the bedrock rim's max thickness so an interior run always exists for the " +
             "player's influence to touch and trigger discovery.")]
    [SerializeField, Min(6)] private int entranceTunnelMinLength = 9;
    [SerializeField, Min(6)] private int entranceTunnelMaxLength = 13;
    [Tooltip("Tunnel width at the mouth, tapering toward the interior tip.")]
    [SerializeField, Min(2)] private int entranceMouthWidth = 3;
    [SerializeField, Min(1)] private int entranceTipWidth = 2;
    [Tooltip("Probability per step that the tunnel drifts one cell perpendicular to its direction.")]
    [Range(0f, 1f)]
    [SerializeField] private float entranceWobbleChance = 0.35f;
    [Tooltip("Offshoot chamberlets budding from the tunnel's interior half.")]
    [SerializeField, Range(0, 3)] private int entranceOffshootMin = 1;
    [SerializeField, Range(0, 3)] private int entranceOffshootMax = 2;
    [Tooltip("CA bounding-box edge length for each offshoot chamberlet.")]
    [SerializeField, Min(5)] private int entranceOffshootBoxSize = 7;
    [Tooltip("Roll the sealed resting-place pocket off the entrance tunnel (canon 34). " +
             "Rolled like an offshoot and then never carved: it stays stone until dug.")]
    [SerializeField] private bool entranceRestPocket = true;
    [Tooltip("How far in from the disc edge the resting pocket must sit, in cells. " +
             "Bedrock can be neither claimed nor mined, and a body sealed in unminable " +
             "stone is a body nobody ever finds -- but the rim map is generated AFTER " +
             "features, so bedrock cannot be queried here. Cover the thickest possible " +
             "rim (TerrainTypeMap's maxRingThickness, 6 by default) plus a cell of slack, " +
             "exactly as riverBankRimMargin does.")]
    [SerializeField, Min(0)] private int entranceRestRimMargin = 7;
    [Tooltip("How far the channel runs OUT past the disc edge onto the apron, in cells. " +
             "Straight along the road bearing; parties spawn at its outer end.")]
    [SerializeField, Min(1)] private int entranceApronRun = 4;

    // ── Inspector — Pathfinding & Fording ─────────────────────────

    [Header("Pathfinding")]
    [SerializeField, Min(1)] private int riverPathCost = 5;

    [Header("Fording")]
    [Range(0.05f, 1f)]
    [SerializeField] private float fordingSpeedMultiplier = 0.5f;

    // ── Inspector — Wild Monsters (DAY 31 PART 2) ─────────────────

    [Header("Wild Monsters")]
    [Tooltip("MonsterDefinitions eligible to spawn as wild cave monsters in chambers " +
             "on this floor. Empty or null = chambers auto-clear (no gate). " +
             "Picked from at random per spawn slot, deterministic from floorSeed + chamberId.")]
    [SerializeField] private List<MonsterDefinition> wildMonsterPool = new();

    [Tooltip("Minimum wild monsters per chamber. Used by the WildMonsterController formula.")]
    [SerializeField, Min(0)] private int wildMonsterMin = 2;

    [Tooltip("Maximum wild monsters per chamber.")]
    [SerializeField, Min(1)] private int wildMonsterMax = 6;

    [Tooltip("Divisor on chamber cell count to scale wild monster spawn target. " +
             "Final count = clamp(cellCount / divisor, min, max).")]
    [SerializeField, Min(1)] private int wildMonsterCellDivisor = 6;

    // ── Inspector — Debug ─────────────────────────────────────────

    // -- Inspector -- Deep Roads --------------------------------------

    [Header("Deep Roads")]
    [Tooltip("Per-floor road layout. Floors with no entry generate no roads and " +
             "cost nothing. Leave null to disable roads entirely.")]
    [SerializeField] private RoadNetworkProfile roadProfile;
    [Tooltip("Per-floor Buried Age site layout. Floors with no entry generate no " +
             "sites and cost nothing. Leave null to disable sites entirely. A " +
             "SEPARATE asset from the road profile on purpose: floor index 2 carries " +
             "a site and no road at all.")]
    [SerializeField] private AncientSiteProfile siteProfile;
    [Tooltip("Per-floor road tilemap, sorting above the floor and below units. " +
             "Road cells paint here as their segment is revealed. Null-safe.")]
    [SerializeField] private Tilemap roadTilemap;

    [Tooltip("How far neighbouring road segments overlap at their seam, in " +
             "cells. The chunk boundary was a square cut across the centreline, " +
             "so a revealed stretch ended in a straight fog edge running the " +
             "full width of the carriageway -- which reads as a wall, because " +
             "mid-corridor there is no architecture to justify one. Contested " +
             "cells in the overlap are settled by a stable per-cell hash, so " +
             "the seam frays. Set 0 for the old square cut. This moves an " +
             "OWNERSHIP line only: every cell is still opened and framed " +
             "exactly as before.")]
    [SerializeField, Min(0)] private int segmentSeamJitterCells = 3;

    [Tooltip("How far past a revealed stretch the road is PREPARED, in cells.\n\n" +
             "Those cells are NOT revealed. The fog over them merely thins, and " +
             "the rock beside them is framed with wall caps first -- which is what " +
             "makes thinning safe. CaveWallRenderer draws a cap only next to mined " +
             "floor, so fog thinned over unframed ground shows bare floor with no " +
             "wall on it.\n\n" +
             "The band is NOT marked mined: Minimap paints every mined tile as " +
             "floor, which would draw the network on the minimap from turn one.\n\n" +
             "Bounded to the frontier, so the cost tracks how far the player has " +
             "got rather than the size of the network. 0 disables the framing and " +
             "the fade together.")]
    [SerializeField, Min(0)] private int roadPrepareCells = 6;
    [SerializeField] private TileBase roadTile;

    [Header("River Rendering")]
    [Tooltip("Per-floor water tilemap, sorting above the floor and below units. " +
             "River cells paint here as they're revealed.")]
    [SerializeField] private Tilemap waterTilemap;
    [SerializeField] private TileBase waterTile;

    [Header("Surface River (floor 0 only)")]
    [Tooltip("Separate tilemap for the forest stretch of a river, sorting above the " +
             "surface ground and below units. Kept apart from the dungeon water tilemap " +
             "so the forest water sprite and its sorting are independent.")]
    [SerializeField] private Tilemap surfaceWaterTilemap;
    [Tooltip("Water tile for the forest stretch. Distinct sprite from the cave water.")]
    [SerializeField] private TileBase surfaceWaterTile;
    [Tooltip("How far past the rim a river runs, in cells. Match the deepest authored " +
             "surface band (SurfaceZoneProfile's largest outerDepth) so the river reaches " +
             "the forest edge. The river is painted in full immediately; the camera is " +
             "confined to the revealed bands, so the far stretch is simply out of view.")]
    [SerializeField, Min(0)] private int surfaceRiverDepth = 100;
    [Tooltip("Minimum angular separation between a river's rim bearing and the pilgrim " +
             "road bearing. Inside this cone the river would run alongside the road, so " +
             "its outward bearing is rotated away.")]
    [SerializeField, Min(0f)] private float roadClearanceDegrees = 25f;
    [Tooltip("How near to square a road crossing must be to become a ford. Crossings " +
             "outside this tolerance are avoided by rotating the river away instead.")]
    [SerializeField, Min(1f)] private float fordSquareToleranceDegrees = 25f;
    [Tooltip("Minimum river width for a ford. Narrower rivers are steered clear of the " +
             "road rather than forded.")]
    [SerializeField, Min(1)] private int minFordWidth = 2;
    [Tooltip("Cells either side of the surface river kept clear of trees, rocks and other " +
             "scatter. Prop sprites are taller and wider than their cell, so a prop sitting " +
             "directly on the bank still overhangs the water. 3 keeps the channel readable; " +
             "raise it if tall trees still crowd the banks. Set 0 to exclude only the water " +
             "cells themselves.")]
    [SerializeField, Min(0)] private int surfaceRiverPropClearance = 3;
    [Tooltip("Half-width of the pilgrim road used for crossing tests. Keep in step with " +
             "SurfaceZoneProfile.roadHalfWidth.")]
    [SerializeField, Min(0.5f)] private float roadHalfWidthForFord = 2.5f;

    // Water cells of every revealed river, accumulated as water is painted. The cave-wall
    // renderer reads this so a discovered river is framed by caps the moment it appears,
    // even on stretches where water meets rock directly (no banks).
    private readonly HashSet<Vector3Int> revealedRiverCells = new();
    public IReadOnlyCollection<Vector3Int> RevealedRiverCells => revealedRiverCells;

    [Header("Debug Visualization")]
    [SerializeField] private bool autoPaintDebugOverlay = false;
    [SerializeField] private Tilemap debugOverlayTilemap;
    [SerializeField] private TileBase debugRiverTile;
    [SerializeField] private TileBase debugChamberTile;
    [SerializeField] private TileBase debugRoadTile;
    [Tooltip("Debug overlay colour for Buried Age site interiors. Leave empty to " +
             "skip. Note debugRoadTile is also unassigned by default, so roads do " +
             "not show in the overlay either.")]
    [SerializeField] private TileBase debugSiteTile;
    [Tooltip("Logs why site generation produced the count it did. Cheap, and the " +
             "only way to tell an empty roster from an unreachable band.")]
    [SerializeField] private bool logSiteGeneration = true;

    [Tooltip("Kerb radius in cells for the junction fillet -- the structuring element " +
             "of the morphological closing that rounds the inside corners where roads " +
             "meet. 0 disables shaping and restores the square meeting. 3 against a " +
             "five-wide carriageway rounds the corner over most of the carriageway's " +
             "own half-width, which is what stops the meeting reading as a plaza.")]
    [SerializeField, Range(0, 8)] private int junctionFilletRadius = 3;

    /// <summary>Radius at which two road ends count as one junction node. The
    /// exact rule DeepRoadGraph clusters by, kept as one constant because the
    /// fillet and the site anchors must agree about where a meeting is.</summary>
    public const int RoadJunctionMergeRadius = 6;

    // ── State ─────────────────────────────────────────────────────

    private FloorRoot floor;
    private FloorFeatureSaveData featureData;
    private readonly Dictionary<Vector3Int, FeatureRef> cellLookup = new();
    private readonly HashSet<Vector3Int> reservedCoreCells = new();

    /// <summary>One reveal unit of one road: a run of centreline widened into
    /// carriageway. Runtime only -- rebuilt from the polyline on both generation
    /// and load, never serialised.</summary>
    private class RoadSegmentRuntime
    {
        public int segmentId;
        public int roadId;
        public readonly List<Vector3Int> cells = new();
    }

    private readonly List<RoadSegmentRuntime> roadSegments = new();
    private readonly HashSet<Vector3Int> roadCells = new();

    // Road anchors handed to the site builder. Junctions are the crossroads a
    // plaza wants; roadAnchorCells is a THINNED sample of centreline, because the
    // full set runs to tens of thousands of cells and the builder samples it on
    // every placement attempt; roadEndCells are the broken and rim-bound ends a
    // Sealed Gate wants to stand at. Runtime only -- all three are rebuilt from
    // the polylines and never serialised.
    private readonly List<Vector3Int> roadJunctions = new();
    private readonly List<Vector3Int> roadAnchorCells = new();
    private readonly List<Vector3Int> roadEndCells = new();

    // Every carved site interior cell on this floor, so chamber generation can be
    // kept off the ruins the same way it is kept off the carriageway.
    private readonly HashSet<Vector3Int> siteCells = new();

    public FloorFeatureSaveData FeatureData => featureData;
    public bool HasGenerated => featureData != null;
    public int RiverPathCost => riverPathCost;
    public float FordingSpeedMultiplier => fordingSpeedMultiplier;

    // DAY 31 PART 2 — Wild monster pool access for WildMonsterController.
    public IReadOnlyList<MonsterDefinition> WildMonsterPool => wildMonsterPool;
    public int WildMonsterMin => wildMonsterMin;
    public int WildMonsterMax => wildMonsterMax;
    public int WildMonsterCellDivisor => wildMonsterCellDivisor;

    // ── Events ────────────────────────────────────────────────────

    /// <summary>DAY 31 PART 2 — Fires whenever a chamber transitions from un-revealed
    /// to revealed. WildMonsterController subscribes to this to spawn wild monsters.
    /// Fires for both noisy and silent reveals (the controller spawns regardless).</summary>
    public event Action<int> OnChamberRevealed;

    /// <summary>Fires when a chamber's claim gate opens (all wild monsters
    /// cleared). InfluenceField subscribes to recompute its cost-distance
    /// field — cleared chamber cells drop from impassable to normal cost.</summary>
    public event Action<int> OnChamberCleared;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
            Debug.LogError($"[TerrainFeatureGenerator] No FloorRoot in parent of '{name}'.");
    }

    // ── Public API ────────────────────────────────────────────────

    public void GenerateNew(int floorSeed, Vector3Int centerCell, int floorRadius)
    {
        var rng = new System.Random(floorSeed);
        featureData = new FloorFeatureSaveData();

        if (floor != null && floor.FloorIndex == 0)
        {
            GenerateCoreCavernAndTunnels(rng, centerCell, floorRadius);
            GenerateEntranceCave(rng, centerCell, floorRadius);
        }

        // Carve precedence: core cavern, then the entrance, then ROADS, then
        // chambers, then rivers. Roads go in before chambers so a cave cannot
        // swallow the carriageway, and before rivers because a river should cut
        // through a road rather than the reverse -- the washed-out crossing is
        // free storytelling from the ordering alone.
        GenerateRoads(rng, centerCell, floorRadius);
        GenerateSites(rng, centerCell, floorRadius);
        GenerateChambers(rng, centerCell, floorRadius);
        GenerateRivers(rng, centerCell, floorRadius);

        RebuildLookup();
        PaintAllSurfaceRivers();

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} generated: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers, " +
            $"{featureData.roads.Count} roads ({roadSegments.Count} segments), " +
            $"{featureData.sites.Count} sites" +
            (featureData.coreCavern != null ? $", core cavern ({featureData.coreCavern.cells.Count} cells, {featureData.coreCavern.tunnels.Count} tunnels)" : "") +
            $" (seed {floorSeed}).");

        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    public void LoadFromSave(FloorFeatureSaveData data)
    {
        featureData = data ?? new FloorFeatureSaveData();
        RebuildLookup();
        RevealVersion++;   // a load replaces the reveal sets wholesale

        UnfogAllRevealedFeatures();
        RepaintRevealedRiverWater();
        RepaintRevealedRoads();
        PaintAllSurfaceRivers();

        // The type map regenerates from seed in RecreateFloorFromSave, which runs
        // BEFORE feature data is restored -- so the Ruins masonry has to be
        // re-applied here, once the sites are actually back.
        ApplyRuinsOverrides();
        SpawnDecorForRevealedSites();

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} loaded: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers, " +
            $"{featureData.revealedRiverIds.Count} rivers revealed, " +
            $"{featureData.revealedChamberIds.Count} chambers revealed" +
            (featureData.coreCavern != null ? $", core cavern present ({featureData.coreCavern.cells.Count} cells, {featureData.coreCavern.tunnels.Count} tunnels)" : "") + ".");


        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    public FloorFeatureSaveData GetSaveData() => featureData;

    public FeatureType GetFeatureAt(Vector3Int cell)
        => cellLookup.TryGetValue(cell, out var fref) ? fref.type : FeatureType.None;

    public bool IsRiver(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.River;



    public bool IsRoad(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.Road;

    public bool IsChamber(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.Chamber;
    public bool IsCoreCavern(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.CoreCavern;
    public bool IsReservedCoreFeature(Vector3Int cell) => reservedCoreCells.Contains(cell);
    public CoreCavernData CoreCavern => featureData?.coreCavern;
    public bool IsEntranceCave(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.EntranceCave;

    /// <summary>The resting place (canon 34), or null on a floor without one and
    /// on saves written before it existed.</summary>
    public EntranceCaveData RestingPlace
        => (featureData?.entranceCave != null && featureData.entranceCave.hasRest)
            ? featureData.entranceCave : null;
    public EntranceCaveData EntranceCave => featureData?.entranceCave;
    public bool IsEntranceDiscovered
        => featureData?.entranceCave != null && featureData.entranceCave.discovered;

    /// <summary>Marks the entrance cave found. Persists via FloorFeatureSaveData.</summary>
    public void MarkEntranceDiscovered()
    {
        if (featureData?.entranceCave == null) return;
        featureData.entranceCave.discovered = true;
        if (featureData.entranceCave.discoveredDay < 0)
            featureData.entranceCave.discoveredDay =
                DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        UnlockState.Unlock("event.entrance_discovered");   // reveals the scout branch
    }

    public int GetChamberId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.Chamber ? fref.featureId : -1;
    }

    public int GetRiverId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.River ? fref.featureId : -1;
    }

    public bool TryGetFeatureRef(Vector3Int cell, out FeatureRef fref)
        => cellLookup.TryGetValue(cell, out fref);

    /// <summary>DAY 31 PART 2 — Lookup chamber record by id, or null if not found.</summary>
    public ChamberData GetChamberById(int chamberId)
    {
        if (featureData == null) return null;
        foreach (var ch in featureData.chambers)
            if (ch.id == chamberId) return ch;
        return null;
    }

    // ── Reveal API ────────────────────────────────────────────────

    public bool IsRiverRevealed(int riverId)
        => featureData != null && featureData.revealedRiverIds.Contains(riverId);

    public bool IsChamberRevealed(int chamberId)
        => featureData != null && featureData.revealedChamberIds.Contains(chamberId);

    public bool IsFeatureRevealedAt(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return false;
        return fref.type switch
        {
            FeatureType.River => IsRiverRevealed(fref.featureId),
            FeatureType.Chamber => IsChamberRevealed(fref.featureId),
            FeatureType.Road => IsRoadSegmentRevealed(fref.featureId),
            FeatureType.AncientSite => IsSiteRevealed(fref.featureId),
            _ => false,
        };
    }

    public void RevealRiver(int riverId)
    {
        if (featureData == null) return;
        if (featureData.revealedRiverIds.Contains(riverId)) return;
        featureData.revealedRiverIds.Add(riverId);
        PaintRiverWater(riverId);
        PaintRiverOverlay(riverId);
        MarkRiverBanksAsFloor(riverId);
        UnfogRiver(riverId);
    }

    public void RevealChamber(int chamberId)
    {
        if (featureData == null) return;
        if (featureData.revealedChamberIds.Contains(chamberId)) return;
        featureData.revealedChamberIds.Add(chamberId);
        PaintChamberOverlay(chamberId);
        UnfogChamber(chamberId);

        // DAY 31 PART 2 — Notify the per-floor WildMonsterController so it can
        // spawn wild cave monsters in this chamber. Subscriber is expected to
        // be idempotent (it may already have spawned for this chamber if this
        // reveal came in via load).
        OnChamberRevealed?.Invoke(chamberId);
    }

    // -- Road Reveal API -------------------------------------------

    /// <summary>How many reveal segments this floor's roads split into.</summary>
    public int RoadSegmentCount => roadSegments.Count;

    /// <summary>Bumped on every reveal and on load. Consumers CACHING anything
    /// derived from reveal state compare this rather than subscribing: reveals
    /// are rare, and a silently missed subscription is the exact bug class that
    /// stranded the minimap for a whole session (canon Appendix D).</summary>
    public int RevealVersion { get; private set; }

    // Road cells past the revealed frontier, mapped to their depth into the dark
    // (1..roadPrepareCells). Rebuilt only when RevealVersion moves.
    private readonly Dictionary<Vector3Int, int> roadFeatherDepth = new();
    private readonly HashSet<Vector3Int> fadedFogCells = new();
    private readonly HashSet<Vector3Int> fadeScratch = new();
    private int preparedRevealVersion = -1;

    /// <summary>Road past the revealed edge with its depth. CaveWallRenderer
    /// frames the rock beside these cells so the fog over them can thin without
    /// exposing anything undrawn -- one set feeding both, so the lit region and
    /// the framed region cannot drift apart.</summary>
    public IReadOnlyDictionary<Vector3Int, int> RoadFeatherBand => roadFeatherDepth;

    /// <summary>Depth the band runs to. DungeonShadow needs it to ramp the band's
    /// light across the same span the fog thins over, so the two agree.</summary>
    public int RoadPrepareCells => roadPrepareCells;

    /// <summary>Rebuilds the prepared band if reveal state has moved. Cheap to
    /// call every frame; the version compare short-circuits.</summary>
    public void EnsureRoadFeatherBand()
    {
        if (preparedRevealVersion == RevealVersion) return;
        preparedRevealVersion = RevealVersion;

        roadFeatherDepth.Clear();
        if (roadPrepareCells > 0) BuildRoadFeatherBand();
        ApplyRoadFogFade();
    }

    private void BuildRoadFeatherBand()
    {
        // Multi-source walk over ROAD cells only, out from every revealed stretch.
        // Revealed cells seed it but never enter the band: they are already open,
        // framed and unfogged, so there is nothing to prepare.
        var seen = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        var depth = new Dictionary<Vector3Int, int>();

        foreach (var seg in roadSegments)
        {
            if (!IsRoadSegmentRevealed(seg.segmentId)) continue;
            foreach (var c in seg.cells)
                if (seen.Add(c)) { depth[c] = 0; queue.Enqueue(c); }
        }
        if (queue.Count == 0) return;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = depth[cur];
            if (d >= roadPrepareCells) continue;

            // EIGHT-neighbour, matching the set ApplyRoadFogFade lights. Walking
            // with Orth4 while the fade collected from the 8-neighbourhood left
            // diagonally-joined road cells -- the edge of a carriageway, every
            // bend -- lit by a neighbour's depth but absent from the band, so the
            // shadow never darkened them and they rendered as bright squares
            // along the edge. Whatever the fade can light, the walk must reach.
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector3Int(cur.x + dx, cur.y + dy, cur.z);
                    if (!seen.Add(n)) continue;
                    if (GetFeatureAt(n) != FeatureType.Road) continue;
                    depth[n] = d + 1;
                    roadFeatherDepth[n] = d + 1;
                    queue.Enqueue(n);
                }
        }
    }

    private void ApplyRoadFogFade()
    {
        var terrain = floor != null ? floor.Terrain : null;
        var fog = terrain != null ? terrain.FogTilemap : null;
        if (fog == null) return;

        // The band AND the rock flanking it. The wall renderer frames that rock,
        // so it is safe to show -- and thinning the carriageway without it would
        // leave the road fading between two hard black walls.
        fadeScratch.Clear();
        foreach (var kv in roadFeatherDepth)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = new Vector3Int(kv.Key.x + dx, kv.Key.y + dy, kv.Key.z);
                    if (fog.GetTile(c) == null) continue;   // already revealed

                    // Never thin fog over ROAD the band does not cover. Road is
                    // exempt from IsSolid, so it gets no wall cap, and it is only
                    // darkened if the band put it in the light map -- an
                    // unprepared road cell shown through thin fog is a bright
                    // square. If the walk and the fade ever disagree again, this
                    // fails to DARK, which is the right way round to fail.
                    if (!roadFeatherDepth.ContainsKey(c)
                        && GetFeatureAt(c) == FeatureType.Road) continue;

                    fadeScratch.Add(c);
                }

        // Restore anything that has LEFT the band. The frontier moves as stretches
        // reveal, and a cell left half-lit behind it reads as a hole in the dark.
        foreach (var c in fadedFogCells)
        {
            if (fadeScratch.Contains(c)) continue;
            if (fog.GetTile(c) == null) continue;
            var back = fog.GetColor(c);
            if (back.a >= 1f) continue;
            back.a = 1f;
            fog.SetTileFlags(c, TileFlags.None);
            fog.SetColor(c, back);
        }

        // Alpha by depth, quadratic: clears quickly just past the revealed edge
        // and thickens toward the dark, reaching solid exactly at roadPrepareCells
        // so it meets untouched fog with no step. Flanking rock takes the
        // SHALLOWEST depth of the road it touches, so a wall reads at the same
        // distance as the carriageway beside it.
        foreach (var c in fadeScratch)
        {
            int d = int.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var r = new Vector3Int(c.x + dx, c.y + dy, c.z);
                    if (roadFeatherDepth.TryGetValue(r, out int rd) && rd < d) d = rd;
                }
            if (d == int.MaxValue) continue;

            float t = Mathf.Clamp01(d / (float)Mathf.Max(1, roadPrepareCells));
            var col = fog.GetColor(c);
            col.a = t * t;
            fog.SetTileFlags(c, TileFlags.None);
            fog.SetColor(c, col);
        }

        fadedFogCells.Clear();
        foreach (var c in fadeScratch) fadedFogCells.Add(c);
    }

    /// <summary>How many road segments on this floor have been revealed. Drives
    /// the one-alert-per-floor rule in FeatureRevealController.</summary>
    public int RevealedRoadSegmentCount
        => featureData?.revealedRoadSegmentIds?.Count ?? 0;

    public bool IsRoadSegmentRevealed(int segmentId)
        => featureData != null && featureData.revealedRoadSegmentIds.Contains(segmentId);

    /// <summary>Reveals ONE stretch of road. Deliberately not per-road: a trunk
    /// runs rim to rim, and unfogging the whole thing off one touched cell would
    /// hand the player the floor's layout for free.</summary>
    public void RevealRoadSegment(int segmentId)
    {
        if (featureData == null) return;
        if (featureData.revealedRoadSegmentIds.Contains(segmentId)) return;
        featureData.revealedRoadSegmentIds.Add(segmentId);
        RevealVersion++;
        PaintRoadSegment(segmentId);
        UnfogRoadSegment(segmentId);
    }

    /// <summary>Every carriageway cell of one reveal segment, or null for an
    /// unknown id. Read by the caravan route report; runtime data, rebuilt on
    /// load like the rest of the segment layer.</summary>
    public IReadOnlyList<Vector3Int> RoadSegmentCells(int segmentId)
        => GetRoadSegment(segmentId)?.cells;

    /// <summary>True when the player HOLDS this stretch: every carriageway
    /// cell influence-claimed. The Living Holds' toll verb keys on this, and
    /// step 8's claiming penalties will key on the same test -- which is why
    /// it lives here rather than on the caravan.</summary>
    public bool IsRoadSegmentHeld(int segmentId)
    {
        var segment = GetRoadSegment(segmentId);
        if (segment == null || segment.cells.Count == 0) return false;
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return false;
        foreach (var c in segment.cells)
            if (!influence.IsTileClaimed(c)) return false;
        return true;
    }

    private RoadSegmentRuntime GetRoadSegment(int segmentId)
    {
        foreach (var s in roadSegments)
            if (s.segmentId == segmentId) return s;
        return null;
    }

    // -- Ancient Site Reveal API -----------------------------------

    /// <summary>How many Buried Age sites this floor carries.</summary>
    /// <summary>The last placement run's full result, rejection counters and
    /// all. Null until this floor generates in THIS session -- a floor
    /// restored from a save never ran the placement loop.</summary>
    public AncientSiteResult LastSitePlacement => lastSitePlacement;
    private AncientSiteResult lastSitePlacement;

    /// <summary>Why the last placement run laid nothing, when it laid
    /// nothing. NotRun until GenerateSites executes on this floor, which is
    /// itself the answer for a floor restored from a save.</summary>
    public SitePlacementSkip LastSitePlacementSkip => lastSitePlacementSkip;
    private SitePlacementSkip lastSitePlacementSkip = SitePlacementSkip.NotRun;

    public int SiteCount => featureData?.sites?.Count ?? 0;

    /// <summary>How many sites on this floor have been revealed. Drives the
    /// first-site wisp line in FeatureRevealController.</summary>
    public int RevealedSiteCount => featureData?.revealedSiteIds?.Count ?? 0;

    public bool IsAncientSite(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.AncientSite;

    public bool IsSiteRevealed(int siteId)
        => featureData != null && featureData.revealedSiteIds.Contains(siteId);

    /// <summary>True once site data exists to be asked about. Callers that
    /// CACHE an answer derived from the site list must check this first --
    /// GetOutpostSite returns null both for "no outpost" and for "not
    /// generated yet", and caching the second as the first is silent and
    /// permanent.</summary>
    public bool HasSiteData => featureData != null && featureData.sites != null;

    /// <summary>This floor's dwarven outpost, or null. Placement guarantees at
    /// most one per floor, so the first match is the answer.</summary>
    public SiteData GetOutpostSite()
    {
        if (featureData?.sites == null) return null;
        foreach (var s in featureData.sites)
            if (s != null && s.reservedForOutpost) return s;
        return null;
    }

    /// <summary>This floor's dwarven village, or null. Same single-per-floor
    /// guarantee as the outpost.</summary>
    public SiteData GetVillageSite()
    {
        if (featureData?.sites == null) return null;
        foreach (var s in featureData.sites)
            if (s != null && s.reservedForVillage) return s;
        return null;
    }

    public SiteData GetSiteById(int siteId)
    {
        if (featureData == null || featureData.sites == null) return null;
        foreach (var s in featureData.sites)
            if (s.id == siteId) return s;
        return null;
    }

    /// <summary>Reveals one whole site. Deliberately NOT split into stretches the
    /// way a road is: a trunk runs rim to rim and unfogging it from one touched
    /// cell would hand the player the floor's layout, whereas a site is a single
    /// set-piece and a floor holds a handful. It reveals entire, like a chamber.</summary>
    public void RevealSite(int siteId)
    {
        if (featureData == null) return;
        if (featureData.revealedSiteIds.Contains(siteId)) return;
        featureData.revealedSiteIds.Add(siteId);
        RevealVersion++;
        UnfogSite(siteId);
        SpawnSiteDecor(siteId);
    }

    // -- Site decor (canon 19: the decor-prefab hook) ----------------------

    private readonly HashSet<int> decorSpawned = new HashSet<int>();

    /// <summary>Instantiates the decor prefab mapped to a site's plan name, once,
    /// at the site's anchor (the plan's bounding-box centre -- the same origin a
    /// decor prefab is authored against). Decor is a pure visual skin: the plan
    /// keeps driving terrain, fog, mining and pathfinding; the prefab holds only
    /// dressing on carved floor. Spawned on reveal because a site reveals ENTIRE
    /// and fog is one-way, which reduces the whole fog question to this call.
    /// Decorated plans are @rotate: no (validator-enforced), so no rotation is
    /// applied here on purpose.</summary>
    private void SpawnSiteDecor(int siteId)
    {
        if (siteProfile == null || floor == null) return;
        var site = GetSiteById(siteId);
        if (site == null || string.IsNullOrEmpty(site.planName)) return;

        var prefab = siteProfile.GetDecorPrefab(site.planName);
        if (prefab == null) return;

        var terrain = floor.Terrain;
        if (terrain == null || terrain.FloorTilemap == null) return;
        if (!decorSpawned.Add(siteId)) return;

        Vector3 pos = terrain.FloorTilemap.GetCellCenterWorld(site.anchorCell.ToVector3Int());
        var go = Instantiate(prefab, pos, Quaternion.identity, floor.transform);
        go.name = "SiteDecor_" + site.planName.Replace(' ', '_');
    }

    /// <summary>Load-path sweep: a save can hold already-revealed sites, whose
    /// reveal call happened in a previous session. LoadFromSave runs this after
    /// the feature data is restored.</summary>
    private void SpawnDecorForRevealedSites()
    {
        if (featureData == null || featureData.revealedSiteIds == null) return;
        foreach (int id in featureData.revealedSiteIds) SpawnSiteDecor(id);
    }

    // ── Chamber Clear API (DAY 31 PART 2) ─────────────────────────

    public bool IsChamberCleared(int chamberId)
    {
        var ch = GetChamberById(chamberId);
        return ch != null && ch.cleared;
    }

    public void MarkChamberCleared(int chamberId)
    {
        var ch = GetChamberById(chamberId);
        if (ch == null || ch.cleared) return;
        ch.cleared = true;
        ch.aliveWildCount = 0;
        OnChamberCleared?.Invoke(chamberId);
    }

    /// <summary>True if the cell sits inside a chamber whose claim gate is still closed.</summary>
    public bool IsCellInUnclearedChamber(Vector3Int cell)
    {
        int chamberId = GetChamberId(cell);
        if (chamberId < 0) return false;
        var ch = GetChamberById(chamberId);
        if (ch == null) return false;
        return !ch.cleared;
    }

    public Vector3 GetFeatureCenterWorld(FeatureType type, int featureId)
    {
        if (featureData == null || floor == null || floor.TileInfluence == null)
            return transform.position;

        if (type == FeatureType.Chamber)
        {
            foreach (var ch in featureData.chambers)
                if (ch.id == featureId)
                    return floor.TileInfluence.CellToWorld(ch.centerCell.ToVector3Int());
        }
        else if (type == FeatureType.River)
        {
            foreach (var r in featureData.rivers)
                if (r.id == featureId && r.polyline.Count > 0)
                {
                    var mid = r.polyline[r.polyline.Count / 2].ToVector3Int();
                    return floor.TileInfluence.CellToWorld(mid);
                }
        }
        else if (type == FeatureType.Road)
        {
            var seg = GetRoadSegment(featureId);
            if (seg != null && seg.cells.Count > 0)
                return floor.TileInfluence.CellToWorld(seg.cells[seg.cells.Count / 2]);
        }
        else if (type == FeatureType.AncientSite)
        {
            var site = GetSiteById(featureId);
            if (site != null)
                return floor.TileInfluence.CellToWorld(site.anchorCell.ToVector3Int());
        }
        else if (type == FeatureType.EntranceCave && featureData.entranceCave != null)
        {
            return floor.TileInfluence.CellToWorld(featureData.entranceCave.mouthCell.ToVector3Int());
        }
        return transform.position;
    }

    // ── Chamber Generation ────────────────────────────────────────

    private void GenerateChambers(System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        // Scaled by RADIUS, not by area. The authored count is calibrated against
        // chamberReferenceRadius; an area scale would take floor index 4 (radius
        // 600) to roughly ninety-six chambers, which is a warren rather than a
        // floor. The player walks a radius, not an area, so a linear scale is the
        // honest one: radius 250 gets 5-10, radius 400 gets 8-16, radius 600 gets
        // 12-24 against the 3-6 a radius-150 floor rolls.
        //
        // NOTE: placement stays UNIFORM across the disc, so on a deep floor a good
        // share of these land past the reach the player will ever have. Confining
        // chambers to a band the way sites are is a separate call and is not taken
        // here.
        float radiusScale = Mathf.Max(1f, floorRadius / (float)Mathf.Max(1, chamberReferenceRadius));
        int rolled = rng.Next(minChambers, maxChambers + 1);
        int desiredCount = Mathf.Clamp(
            Mathf.RoundToInt(rolled * radiusScale), 1, Mathf.Max(1, chamberCountCeiling));
        int attempts = 0;
        int maxAttempts = desiredCount * 6;

        while (featureData.chambers.Count < desiredCount && attempts < maxAttempts)
        {
            attempts++;

            // Shrink the pick disc so chambers stay inside the bedrock rim. The
            // rim map is generated AFTER features, so it can't be queried here;
            // the margin conservatively clears the thickest possible rim.
            int chamberDisc = Mathf.Max(exclusionRadiusFromCenter + 1, floorRadius - chamberRimMargin);
            if (!PickRandomCellInDisc(rng, centerCell, chamberDisc, exclusionRadiusFromCenter, out var chamberCentre))
                continue;

            if (IsTooCloseToExistingChamber(chamberCentre)) continue;

            int boxSize = rng.Next(minChamberBoxSize, maxChamberBoxSize + 1);
            var cells = LargestConnectedRegion(
                RunChamberCA(rng, chamberCentre, boxSize, centerCell, floorRadius));

            // Chambers yield to the road. A cave that opens onto the carriageway
            // reads fine, but a cell can only have one owner, and the road was
            // carved first. Re-run the connectivity pass afterwards: a road
            // crossing a chamber can otherwise leave a sealed islet behind.
            if (roadCells.Count > 0 || siteCells.Count > 0)
            {
                cells.RemoveAll(c => roadCells.Contains(c) || siteCells.Contains(c));
                cells = LargestConnectedRegion(cells);
            }

            if (cells.Count < minChamberCellCount) continue;

            featureData.chambers.Add(new ChamberData
            {
                id = featureData.chambers.Count,
                centerCell = SerializableVector3Int.From(chamberCentre),
                cells = ToSerializable(cells),
                // aliveWildCount defaults to -1, cleared defaults to false — see ChamberData.
            });
        }
    }

    /// <summary>Cellular automata can leave disconnected blobs; a sealed islet
    /// recorded into a chamber's cells later strands wild spawns inside solid
    /// rock. Keep only the largest 4-connected region.</summary>
    private static List<Vector3Int> LargestConnectedRegion(List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return cells ?? new List<Vector3Int>();
        var remaining = new HashSet<Vector3Int>(cells);
        var best = new List<Vector3Int>();
        var region = new List<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        while (remaining.Count > 0)
        {
            region.Clear();
            Vector3Int seed = default;
            foreach (var c0 in remaining) { seed = c0; break; }
            remaining.Remove(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                region.Add(c);
                foreach (var n in new[] {
                    new Vector3Int(c.x + 1, c.y, 0), new Vector3Int(c.x - 1, c.y, 0),
                    new Vector3Int(c.x, c.y + 1, 0), new Vector3Int(c.x, c.y - 1, 0) })
                    if (remaining.Remove(n)) queue.Enqueue(n);
            }
            if (region.Count > best.Count) best = new List<Vector3Int>(region);
        }
        return best;
    }

    private bool IsTooCloseToExistingChamber(Vector3Int candidate)
    {
        foreach (var c in featureData.chambers)
        {
            var existing = c.centerCell.ToVector3Int();
            int dx = candidate.x - existing.x;
            int dy = candidate.y - existing.y;
            if (dx * dx + dy * dy < chamberSpacing * chamberSpacing) return true;
        }
        return false;
    }

    private List<Vector3Int> RunChamberCA(
        System.Random rng, Vector3Int chamberCentre, int boxSize,
        Vector3Int floorCentre, int floorRadius)
    {
        int size = boxSize;
        int half = size / 2;
        bool[,] walls = new bool[size, size];

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) walls[x, y] = true;
                else walls[x, y] = rng.NextDouble() < caInitialWallChance;
            }

        for (int iter = 0; iter < caSmoothingIterations; iter++)
        {
            bool[,] next = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    next[x, y] = CountWallNeighbours(walls, x, y) >= 5;
            walls = next;
        }

        int cx = half, cy = half;
        if (walls[cx, cy]) return new List<Vector3Int>();

        var visited = new bool[size, size];
        var stack = new Stack<(int x, int y)>();
        stack.Push((cx, cy));
        var localCells = new List<(int x, int y)>();

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= size || y >= size) continue;
            if (visited[x, y] || walls[x, y]) continue;
            visited[x, y] = true;
            localCells.Add((x, y));
            stack.Push((x + 1, y)); stack.Push((x - 1, y));
            stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }

        var result = new List<Vector3Int>(localCells.Count);
        foreach (var (lx, ly) in localCells)
        {
            var worldCell = new Vector3Int(
                chamberCentre.x + (lx - half),
                chamberCentre.y + (ly - half), 0);

            if (!IsInFloorRadius(worldCell, floorCentre, floorRadius)) continue;
            if (IsInExclusion(worldCell, floorCentre)) continue;
            if (reservedCoreCells.Contains(worldCell)) continue;

            result.Add(worldCell);
        }
        return result;
    }

    private static int CountWallNeighbours(bool[,] walls, int x, int y)
    {
        int count = 0;
        int w = walls.GetLength(0);
        int h = walls.GetLength(1);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) { count++; continue; }
                if (walls[nx, ny]) count++;
            }
        return count;
    }

    // ── River Generation ──────────────────────────────────────────

    private void GenerateRivers(System.Random rng, Vector3Int floorCentre, int floorRadius)
    {
        int riverCount = rng.Next(minRivers, maxRivers + 1);

        for (int i = 0; i < riverCount; i++)
        {
            int controlPoints = rng.Next(minRiverControlPoints, maxRiverControlPoints + 1);
            int width = rng.Next(minRiverWidth, maxRiverWidth + 1);

            var polyline = BuildRiverPolyline(rng, floorCentre, floorRadius, controlPoints);
            if (polyline.Count < 2) continue;

            var cells = PaintRiver(polyline, width, floorCentre, floorRadius);
            if (cells.Count == 0) continue;

            // Chambers yield the FULL footprint (water + banks) to the river.
            foreach (var chamber in featureData.chambers)
                chamber.cells.RemoveAll(sv => cells.Contains(sv.ToVector3Int()));

            // So do sites, masonry included. A river cuts through a ruin exactly as
            // it cuts through a road: the washed-out crossing is free storytelling
            // from the carve order alone, and a wall standing in a watercourse
            // would read as a bug rather than a ruin.
            if (featureData.sites != null)
                foreach (var site in featureData.sites)
                {
                    site.cells.RemoveAll(sv => cells.Contains(sv.ToVector3Int()));
                    site.ruinsCells.RemoveAll(sv => cells.Contains(sv.ToVector3Int()));
                    // The washed-out crossing stays washed out: a river through
                    // the paved band shows water, not paving.
                    site.pavedRoadCells.RemoveAll(sv => cells.Contains(sv.ToVector3Int()));
                }

            // Erode the outer shell into dry floor banks; the eroded core stays water.
            int bankWidth = rng.Next(minRiverBank, maxRiverBank + 1);
            SplitRiverBanks(cells, bankWidth, floorCentre,
                            Mathf.Max(0, floorRadius - riverBankRimMargin),
                            out var waterCells, out var bankCells);

            // The surface continuation. Floor 0 only: deeper floors have no forest to
            // flow into. No banks out here -- forest floor is already walkable.
            var surfaceCells = new HashSet<Vector3Int>();
            var fordCells = new HashSet<Vector3Int>();
            if (floor != null && floor.FloorIndex == 0 && surfaceRiverDepth > 0)
                BuildSurfaceStretch(rng, polyline, width, floorCentre, floorRadius,
                                    surfaceCells, fordCells);

            featureData.rivers.Add(new RiverData
            {
                surfaceCells = ToSerializableList(surfaceCells),
                fordCells = ToSerializableList(fordCells),
                id = featureData.rivers.Count,
                width = width,
                polyline = ToSerializable(polyline),
                cells = ToSerializable(new List<Vector3Int>(waterCells)),
                bankCells = ToSerializable(new List<Vector3Int>(bankCells)),
            });
        }

        featureData.chambers.RemoveAll(c => c.cells.Count == 0);
    }

    // -- Road Generation -------------------------------------------

    /// <summary>
    /// Lays this floor's roads, if the profile has an entry for it. Produces
    /// polylines and metadata only; the cells come from RebuildRoadCells, which
    /// the load path also calls, so generation and load can never disagree.
    ///
    /// Roads stop short of the bedrock rim by the entry's rimMargin. They cannot
    /// be driven through it: MarkNaturalFloor refuses to open bedrock, so a road
    /// in the rim would register as Road in the lookup while staying solid rock
    /// -- typed, revealed, and unwalkable. A rim-bound trunk therefore ends in
    /// collapse instead, which reads better anyway: the road ran on, the rim
    /// swallowed it.
    /// </summary>
    private void GenerateRoads(System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        if (roadProfile == null || floor == null) return;

        var entry = roadProfile.GetEntry(floor.FloorIndex);
        if (entry == null || entry.mode == RoadMode.None) return;

        var result = RoadNetworkBuilder.Build(
            rng, centerCell, floorRadius, entry, exclusionRadiusFromCenter);
        featureData.roads = result.roads;

        // Rasterise straight away so chamber generation can be kept off the
        // carriageway. Rivers have not run yet, so this pass does not know about
        // them; RebuildLookup recomputes once they have.
        RebuildRoadCells();
    }

    /// <summary>
    /// Rebuilds every road's cells and reveal segments from the stored polylines.
    /// Runs on fresh generation AND on load, which is the whole reason road cells
    /// are not serialised.
    /// </summary>
    private void RebuildRoadCells()
    {
        roadSegments.Clear();
        roadCells.Clear();
        if (featureData == null || featureData.roads == null) return;

        // The core cavern, its tunnels and the entrance cave were carved first
        // and keep their cells. Rivers take theirs back afterwards.
        var taken = new HashSet<Vector3Int>(reservedCoreCells);
        if (featureData.rivers != null)
            foreach (var r in featureData.rivers)
            {
                foreach (var sv in r.cells) taken.Add(sv.ToVector3Int());
                if (r.bankCells != null)
                    foreach (var sv in r.bankCells) taken.Add(sv.ToVector3Int());
            }

        int nextSegmentId = 0;
        foreach (var road in featureData.roads)
        {
            var line = RoadNetworkBuilder.Centreline(road);
            // SerializableVector3Int is a class, so guard rather than trust a
            // deserialiser to have built one.
            var roadCentre = road.floorCentre != null
                ? road.floorCentre.ToVector3Int()
                : Vector3Int.zero;
            int step = Mathf.Max(4, road.segmentLength);

            for (int i = 0; i < line.Count; i += step)
            {
                int count = Mathf.Min(step, line.Count - i);

                // FRAYED SEAM. A square cut here put the boundary between two
                // segments dead straight across a five-wide carriageway, and a
                // revealed stretch therefore ended in a fog edge that read as a
                // wall -- there is no architecture mid-corridor to justify one.
                // Overlap into the next chunk instead and let a stable per-cell
                // hash settle who keeps each contested cell.
                //
                // Ownership only. Every cell is still opened, revealed and
                // framed exactly as before, so nothing here can expose ground
                // the renderer has not prepared -- which is precisely what sank
                // the alpha-feather attempt.
                int over = Mathf.Clamp(segmentSeamJitterCells, 0, line.Count - (i + count));

                // Ring d is the dilation of the chunk extended by d centreline
                // cells, so the smallest d containing a cell is its depth into
                // the overlap. Cheap: a handful of extra dilations per segment,
                // at generation only.
                var rings = new List<HashSet<Vector3Int>>(over + 1);
                for (int d = 0; d <= over; d++)
                    rings.Add(RoadNetworkBuilder.Dilate(
                        line.GetRange(i, count + d), road.width, roadCentre, road.clampRadius));

                // The id advances whether or not the segment survives, so saved
                // reveal ids stay aligned even where a river ate a whole stretch.
                var seg = new RoadSegmentRuntime { segmentId = nextSegmentId++, roadId = road.id };
                foreach (var c in rings[over])
                {
                    if (taken.Contains(c)) continue;

                    if (over > 0)
                    {
                        int depth = 0;
                        while (depth < over && !rings[depth].Contains(c)) depth++;
                        // Beyond this cell's reach, so it belongs to the next
                        // segment, whose own dilation covers it.
                        if (depth > SeamReach(c, over)) continue;
                    }

                    if (!roadCells.Add(c)) continue;   // one owner per cell
                    seg.cells.Add(c);
                }
                if (seg.cells.Count > 0) roadSegments.Add(seg);
            }
        }

        // JUNCTION SHAPING (canon 19, was DEFERRED). One straight kernel dilated
        // two crossing carriageways into a blocky square with square inside
        // corners, so a meeting read as a plaza. A morphological closing at each
        // node fillets those corners into the kerb radius a real junction has.
        //
        // Runs here, at the END of the shared rebuild, so generation and load
        // produce byte-identical carriageways: the inputs are the saved
        // polylines and the same taken set the segment loop above used, and
        // nothing else. Sites are composed around a carriageway and already
        // subtract road cells from their plans, so running BEFORE site
        // generation is what keeps a filleted cell from ever landing under
        // masonry on the load path.
        FilletJunctionsIntoSegments(taken);

        RebuildRoadAnchors();
    }

    /// <summary>Cells added by the junction fillet, most recent rebuild. Read by
    /// the headless road report; runtime data, like the segments themselves.</summary>
    public int JunctionFilletCellCount { get; private set; }

    /// <summary>How many junction nodes the last rebuild shaped.</summary>
    public int JunctionNodeCount { get; private set; }

    /// <summary>Runs the fillet and hands each new cell to a segment, so a
    /// filleted corner reveals, opens and claims with the stretch it belongs to
    /// rather than sitting outside every segment as unowned road.
    ///
    /// Ownership goes to the lowest segment id holding an 8-neighbour. Lowest
    /// rather than nearest because it is stable: a tie broken by distance would
    /// flip on a rounding difference and move a cell between segments across a
    /// reload, which is the class of drift the frayed seam's stable hash exists
    /// to avoid.</summary>
    private void FilletJunctionsIntoSegments(HashSet<Vector3Int> taken)
    {
        JunctionFilletCellCount = 0;
        JunctionNodeCount = 0;
        if (junctionFilletRadius <= 0) return;
        if (featureData == null || featureData.roads == null || featureData.roads.Count == 0) return;
        if (roadSegments.Count == 0) return;

        var nodes = RoadNetworkBuilder.JunctionNodes(featureData.roads, RoadJunctionMergeRadius);
        JunctionNodeCount = nodes.Count;
        if (nodes.Count == 0) return;

        // Every road on a floor captured the same centre and clamp radius at
        // generation, so the first road speaks for all of them. Read from the
        // save rather than from the live profile for the same reason RoadData
        // stores them at all: a later profile edit must not re-rasterise an
        // existing floor.
        var first = featureData.roads[0];
        var centre = first.floorCentre != null ? first.floorCentre.ToVector3Int() : Vector3Int.zero;
        int clampRadius = first.clampRadius;
        int width = Mathf.Max(1, first.width);

        var added = RoadNetworkBuilder.FilletJunctions(
            roadCells, nodes, width, junctionFilletRadius, centre, clampRadius, taken);
        if (added.Count == 0) return;

        // Segment lookup built only over the node boxes. The whole network runs
        // to tens of thousands of cells on floor index 4 and the fillet is
        // node-local, so a full map would be paid for nothing.
        int reach = ((width - 1) / 2) + junctionFilletRadius + 3;
        var owners = new Dictionary<Vector3Int, int>();
        for (int s = 0; s < roadSegments.Count; s++)
        {
            var seg = roadSegments[s];
            for (int i = 0; i < seg.cells.Count; i++)
            {
                var c = seg.cells[i];
                if (!NearAnyNode(c, nodes, reach)) continue;
                if (!owners.ContainsKey(c) || owners[c] > seg.segmentId) owners[c] = seg.segmentId;
            }
        }

        for (int i = 0; i < added.Count; i++)
        {
            var c = added[i];
            int best = int.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (owners.TryGetValue(new Vector3Int(c.x + dx, c.y + dy, 0), out int id) && id < best)
                        best = id;
                }
            if (best == int.MaxValue) continue;      // touches no segment; leave it rock

            var target = GetRoadSegment(best);
            if (target == null) continue;
            if (!roadCells.Add(c)) continue;
            target.cells.Add(c);
            owners[c] = best;
            JunctionFilletCellCount++;
        }
    }

    private static bool NearAnyNode(Vector3Int cell, List<Vector3Int> nodes, int reach)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (Mathf.Abs(cell.x - nodes[i].x) > reach) continue;
            if (Mathf.Abs(cell.y - nodes[i].y) > reach) continue;
            return true;
        }
        return false;
    }

    /// <summary>How far into the overlap a contested cell lets the earlier
    /// segment reach, 0..over.
    ///
    /// Quantised to 2x2 blocks on purpose. A per-cell coin flip frays the seam
    /// into static; blocks give it teeth you can read as a broken edge. Stable
    /// on the cell alone, so a reload partitions the road identically and saved
    /// reveal ids keep meaning what they meant.</summary>
    private static int SeamReach(Vector3Int c, int over)
    {
        unchecked
        {
            int h = (c.x >> 1) * 73856093 ^ (c.y >> 1) * 19349663;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) % (over + 1);
        }
    }

    /// <summary>
    /// Collects the road anchors the site builder wants: junctions (rebuilt by
    /// proximity, since RoadNetworkResult.junctions is tooling-only and never
    /// persisted), a thinned centreline sample, and the ends roads stop at.
    /// Runs wherever road cells are rebuilt, so it is correct on load too.
    /// </summary>
    private void RebuildRoadAnchors()
    {
        roadJunctions.Clear();
        roadAnchorCells.Clear();
        roadEndCells.Clear();
        if (featureData == null || featureData.roads == null) return;

        const int SampleStride = 12;

        var endpoints = new List<Vector3Int>();
        foreach (var road in featureData.roads)
        {
            var line = RoadNetworkBuilder.Centreline(road);
            if (line.Count == 0) continue;

            for (int i = 0; i < line.Count; i += SampleStride)
                roadAnchorCells.Add(line[i]);

            endpoints.Add(line[0]);
            endpoints.Add(line[line.Count - 1]);

            // A road with a broken gap stops dead; that far end is a Sealed Gate's
            // natural home. A road that ran its whole length ends where it ends.
            if (road.brokenGapCells > 0) roadEndCells.Add(line[line.Count - 1]);
        }

        // Two road ends meeting inside this radius were one junction node before
        // the network was split into edges. Derived by RoadNetworkBuilder now,
        // because junction shaping runs off the same node list and two
        // derivations that disagreed by a cell would move carriageway ownership
        // under an existing save.
        roadJunctions.AddRange(
            RoadNetworkBuilder.JunctionNodes(featureData.roads, RoadJunctionMergeRadius));

        // No roads at all is a legitimate state, not a failure: floor index 2
        // carries a site and no road layer. Every anchor preference in the
        // builder degrades to a free in-band pick.
        if (roadEndCells.Count == 0 && endpoints.Count > 0)
            roadEndCells.AddRange(endpoints);
    }

    // -- Ancient Site Generation -----------------------------------

    /// <summary>
    /// Lays this floor's Buried Age sites, if the profile has an entry for it.
    /// Runs AFTER roads (a site is composed around a carriageway that is already
    /// there) and BEFORE chambers and rivers.
    /// </summary>
    private void GenerateSites(System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        siteCells.Clear();

        // Every exit from here records WHY. The report used to print one
        // sentence for four different silences, and printed it for floor 0 on
        // a new game -- which pointed the diagnosis at the save system when the
        // cause was somewhere else entirely.
        if (siteProfile == null)
        {
            lastSitePlacementSkip = SitePlacementSkip.NoProfileAssigned;
            Debug.LogWarning("[Sites] Floor " + (floor != null ? floor.FloorIndex : -1) +
                ": no AncientSiteProfile assigned on this TerrainFeatureGenerator, " +
                "so no sites. This was previously a silent return.");
            return;
        }
        if (floor == null)
        {
            lastSitePlacementSkip = SitePlacementSkip.NoFloor;
            Debug.LogError("[Sites] GenerateSites ran with a null FloorRoot. " +
                "That is an execution order fault -- see canon Appendix D.");
            return;
        }

        var entry = siteProfile.GetEntry(floor.FloorIndex);
        if (entry == null)
        {
            lastSitePlacementSkip = SitePlacementSkip.NoEntryForFloor;
            // Not an error -- most floors carry no sites. Logged all the same,
            // because "the profile has no entry for this floor" is by far the
            // most likely reason for an unexpectedly empty floor.
            if (logSiteGeneration)
                Debug.Log($"[Sites] Floor {floor.FloorIndex}: no entry on '{siteProfile.name}', " +
                          "so no sites. Add one if this floor should carry them.");
            return;
        }

        // Kept rather than discarded. Every rejection counter on the result
        // is the answer to "why is this floor empty", and throwing them away
        // meant that question could only ever be guessed at. Runtime only:
        // never serialized, and null on a floor restored from a save, which
        // the report states rather than printing zeroes that read as
        // rejections.
        var result = AncientSiteBuilder.Build(
            rng, centerCell, floorRadius, entry, exclusionRadiusFromCenter,
            roadJunctions, roadAnchorCells, roadEndCells,
            siteProfile.GetAuthoredPlans());
        lastSitePlacement = result;
        lastSitePlacementSkip = SitePlacementSkip.Placed;

        foreach (var plan in result.sites)
        {
            // The road and the core keep their cells outright. A site yields to
            // both: the carriageway was carved first, and nothing is ever allowed
            // to sit on the core cavern or the entrance.
            // Record the carriageway overlap BEFORE yielding it: those cells
            // get site paving on the road tilemap, so the room reads built
            // around the road rather than cut through by it. Core overlap is
            // yielded silently as before -- nothing paves the core cavern.
            var pavedRoad = new List<SerializableVector3Int>();
            foreach (var c in plan.cells)
                if (roadCells.Contains(c)) pavedRoad.Add(SerializableVector3Int.From(c));
            // The wall band yields road cells too (the road punches its own
            // gate); those pave as well, or the crossing shows road in every
            // doorway while the room around it is paved.
            foreach (var c in plan.ruinsCells)
                if (roadCells.Contains(c)) pavedRoad.Add(SerializableVector3Int.From(c));

            plan.cells.RemoveAll(c => roadCells.Contains(c) || reservedCoreCells.Contains(c));
            plan.ruinsCells.RemoveAll(c => roadCells.Contains(c) || reservedCoreCells.Contains(c));
            if (plan.cells.Count < 12)
            {
                // The builder's own size check ran BEFORE the carriageway was
                // subtracted, so a site can pass there and die here. On an ordinary
                // ruin that is fine and silent. On the outpost it means the floor
                // ships with no dwarves, which must never be a silent outcome --
                // the plan wants widening, not this guard removing.
                if (plan.reservedForOutpost || plan.reservedForVillage)
                    Debug.LogError("[TerrainFeatureGenerator] The guaranteed " +
                        (plan.reservedForOutpost ? "outpost" : "village") +
                        " was reduced below 12 cells by the carriageway subtraction " +
                        "and has been dropped. Widen the plan.");
                continue;
            }

            var data = new SiteData
            {
                id = featureData.sites.Count,
                archetype = plan.archetype,
                variant = plan.variant,
                planName = plan.planName,
                anchorCell = SerializableVector3Int.From(plan.anchor),
                heartCell = plan.hasHeart
                    ? SerializableVector3Int.From(plan.heartCell) : null,
                cells = ToSerializable(plan.cells),
                ruinsCells = ToSerializable(plan.ruinsCells),
                pavedRoadCells = pavedRoad,
                reservedForOutpost = plan.reservedForOutpost,
                reservedForVillage = plan.reservedForVillage,
            };
            featureData.sites.Add(data);
            foreach (var c in plan.cells) siteCells.Add(c);
        }

        if (logSiteGeneration)
        {
            int dropped = result.sites.Count - featureData.sites.Count;
            Debug.Log(
                $"[Sites] Floor {floor.FloorIndex} (radius {floorRadius}): {result.Summary()}. " +
                $"{dropped} lost to road/core overlap. " +
                $"Kept {featureData.sites.Count}. " +
                $"Anchors available: {roadJunctions.Count} junctions, " +
                $"{roadAnchorCells.Count} road samples, {roadEndCells.Count} road ends.");

            if (featureData.sites.Count == 0)
                Debug.LogWarning(
                    "[Sites] Floor " + floor.FloorIndex + " generated NO sites. The line above " +
                    "says which stage discarded them. Note that sites are only VISIBLE once " +
                    "influence reaches them -- check the count here before hunting a render bug.");
        }
    }

    /// <summary>
    /// Retypes every site's masonry to its family terrain -- MasonryTypeFor
    /// decides per site: DwarvenMasonry for the living dwarven structures (the
    /// village hold and the gatehouse outpost), Ruins for every dead site.
    /// Idempotent, and called from BOTH paths because the type map clears its
    /// overrides on GenerateNew: FloorRoot.Bootstrap calls it after building
    /// the map on a new floor, and LoadFromSave calls it once restored feature
    /// data is in hand.
    ///
    /// Both terrains carry resistance and tints in TerrainResistanceTable and
    /// map to the ancient_masonry pattern in PatternDiscovery, so this one
    /// call is the whole of the wiring; the name keeps its history (it placed
    /// an enum value that had been reserved since the terrain system shipped).
    /// </summary>
    public void ApplyRuinsOverrides()
    {
        if (featureData == null || featureData.sites == null || floor == null) return;
        var map = floor.TerrainTypeMap;
        if (map == null || !map.IsGenerated) return;

        bool any = false;
        var cells = new List<Vector3Int>();
        map.ClearHoldingsCells();
        map.ClearHolySiteCells();

        // Open carriageway is dwarven ground, but only where dwarves are still
        // alive to hold it. Floor 4 carries the DEAD network -- no patrols, no
        // caravans, nobody left to take offence -- and it already claims at
        // deadRoadClaimResistance rather than the living road's 8x, so it takes
        // no granite either and cost agrees with colour.
        //
        // Registered BEFORE the site loop below on purpose: a site's own paved
        // band is then overwritten with the SITE's key, so the paving through a
        // hold lights with the hold rather than with the stretch passing
        // through it.
        if (floor.HasLivingDwarvenSite)
            foreach (var seg in roadSegments)
                map.RegisterHoldingsCells(seg.cells,
                                          TerrainTypeMap.RoadOwnerKey(seg.segmentId));

        foreach (var s in featureData.sites)
        {
            if (s == null || s.ruinsCells == null || s.ruinsCells.Count == 0) continue;
            cells.Clear();
            foreach (var sv in s.ruinsCells) cells.Add(sv.ToVector3Int());
            map.ApplyFeatureOverride(cells, MasonryTypeFor(s));

            // Living dwarven sites register their WHOLE footprint -- masonry,
            // carved interior and the paved carriageway -- so the overlay
            // fills the hold rather than tracing its walls. Cleared and
            // refilled above because this method re-runs on both the fresh
            // and the load path.
            //
            // NOT the query for the ladder's resistance rung, whatever the
            // earlier comment here claimed: road and site cells are already
            // priced by FloorRoot.GetClaimCostMultiplier, and this registry
            // does not reach past a living floor. What it IS ready-made for
            // is keying the ladder's standing PENALTY on dwarven ground.
            if (MasonryTypeFor(s) == TerrainType.DwarvenMasonry)
            {
                int key = TerrainTypeMap.SiteOwnerKey(s.id);
                map.RegisterHoldingsCells(s.ruinsCells, key);
                map.RegisterHoldingsCells(s.cells, key);
                map.RegisterHoldingsCells(s.pavedRoadCells, key);
            }

            // A Church seal's GROUND is hallowed, not merely its wall, so
            // the carved interior is retyped too -- unlike every Buried Age
            // site, which retypes masonry alone. The interior then resists
            // at HolyGround's 10x rather than siteClaimResistance's 3x.
            //
            // This inverts the dwarven courtyard finding (canon 19) on
            // purpose. There the cheapest ground in the hold sat behind the
            // loudest announcement, and the ladder read told-then-not-felt.
            // Here the announcement and the cost agree.
            if (IsHolySite(s))
            {
                cells.Clear();
                foreach (var sv in s.cells) cells.Add(sv.ToVector3Int());
                map.ApplyFeatureOverride(cells, TerrainType.HolyGround);

                int holyKey = TerrainTypeMap.SiteOwnerKey(s.id);
                map.RegisterHolySiteCells(s.ruinsCells, holyKey);
                map.RegisterHolySiteCells(s.cells, holyKey);
            }
            any = true;
        }
        if (any) PaintSitePaving();

        // Paint the ENTIRE network, not just the revealed segments, and do it
        // AFTER PaintSitePaving -- that pass is what fills sitePavedRoad, and
        // PaintRoadSegment must skip those cells or a road tile lands on top of
        // the tinted paving and re-opens the pale band.
        //
        // Painting everything is free and invisible: fog hides unrevealed
        // ground, exactly as it already does for site paving. Painting per
        // segment instead left the one-cell halo that UnfogRoadSegment reveals
        // spilling onto the NEXT segment, which had no road tile yet -- bare
        // floor across the carriageway at every join. Reveal stays per segment;
        // only the paint is global.
        PaintAllRoads();
    }

    /// <summary>The terrain a site's masonry is retyped to -- the ONE place
    /// that decision lives, consumed by both the retype above and the paving
    /// pass below, so the wall family a site renders and the paving it takes
    /// can never disagree. Living dwarven structures (the village hold and
    /// the gatehouse outpost) are DwarvenMasonry; every dead site stays
    /// Ruins, which keeps the ossuary guarantee's Ruins-based reasoning
    /// true.</summary>
    public static TerrainType MasonryTypeFor(SiteData site)
        => IsHolySite(site) ? TerrainType.HolyGround
         : site != null && (site.reservedForVillage || site.reservedForOutpost)
            ? TerrainType.DwarvenMasonry
            : TerrainType.Ruins;

    /// <summary>True for the four Church archetypes. One place, because the
    /// terrain override, the holy registry and the desecration layer must never
    /// disagree about what counts as a seal.</summary>
    public static bool IsHolySite(SiteData site)
        => site != null && IsHolyArchetype(site.archetype);

    public static bool IsHolyArchetype(SiteArchetype a)
        => a == SiteArchetype.ChurchSeal
        || a == SiteArchetype.SealedCrypt
        || a == SiteArchetype.WardChapel
        || a == SiteArchetype.BlessedSpring
        || a == SiteArchetype.DeadCoreVault;

    // Cached child renderer for site paving; found once per floor lifetime.
    private CaveWallRenderer wallRendererForPaving;

    /// <summary>Paints each site's family paving variants over its carved interior
    /// (canon 19). Runs from ApplyRuinsOverrides because both the fresh-generation
    /// path (FloorRoot) and the load path already call it AFTER the floor
    /// tilemap's disc paint, so one choke point covers both and nothing can
    /// overpaint it later. The per-cell pick is a spatial hash, not an RNG: no
    /// seed to disagree about, no draw order, stable across reloads.
    ///
    /// NOTE for the lazy floor-paint backlog item: if disc painting ever moves
    /// into RevealTile, this pass must move with it or paving is overpainted.</summary>
    // Road cells that carry site paving instead of the road tile. Built by
    // PaintSitePaving, consulted by PaintRoadSegment -- roads paint lazily per
    // revealed segment, so a segment revealed AFTER the paving pass would
    // otherwise repaint road over it.
    private readonly HashSet<Vector3Int> sitePavedRoad = new HashSet<Vector3Int>();

    private TileBase SitePavingTileFor(Vector3Int cell, TileBase[] paving)
    {
        if (paving == null || paving.Length == 0) return null;
        int h = unchecked(cell.x * 73856093 ^ cell.y * 19349663 ^ (floor.FloorIndex + 1) * 83492791);
        return paving[(h & int.MaxValue) % paving.Length];
    }

    private void PaintSitePaving()
    {
        if (featureData == null || featureData.sites == null || floor == null) return;
        var terrain = floor.Terrain;
        if (terrain == null || terrain.FloorTilemap == null) return;

        if (wallRendererForPaving == null)
            wallRendererForPaving = floor.GetComponentInChildren<CaveWallRenderer>(true);
        if (wallRendererForPaving == null) return;

        var map = terrain.FloorTilemap;
        sitePavedRoad.Clear();
        foreach (var s in featureData.sites)
        {
            if (s == null || s.cells == null) continue;

            // Paving follows the site's masonry FAMILY, resolved through the
            // same MasonryTypeFor call that types the walls -- one decision,
            // consulted twice, so paving and masonry can never disagree. A
            // family with no paving tiles skips the site entirely: no paving,
            // and no road-cell claim either, exactly as the old global
            // early-out behaved when the ruins list was empty.
            var paving = wallRendererForPaving.PavingTilesFor(MasonryTypeFor(s));
            if (paving == null || paving.Length == 0) continue;

            foreach (var sv in s.cells)
            {
                var cell = sv.ToVector3Int();
                var tile = SitePavingTileFor(cell, paving);
                if (tile != null) map.SetTile(cell, tile);
            }

            // Carriageway cells the site yielded: paved on the FLOOR tilemap so
            // they take the floor tint like the rest of the room (painting the
            // untinted road tilemap was the pale-band bug), and the road tile
            // is cleared so nothing lighter sits above the paving.
            if (s.pavedRoadCells == null) continue;
            foreach (var sv in s.pavedRoadCells)
            {
                var cell = sv.ToVector3Int();
                sitePavedRoad.Add(cell);
                var tile = SitePavingTileFor(cell, paving);
                if (tile != null) map.SetTile(cell, tile);
                if (roadTilemap != null) roadTilemap.SetTile(cell, null);
            }
        }
    }

    /// <summary>
    /// Reveals one site with its wall border and registers the carved interior as
    /// natural floor -- walkable, unclaimed, mined -- exactly as a chamber does.
    /// The masonry is deliberately NOT marked: it stays solid so the cave-wall
    /// renderer frames the site with straight walls, which is the entire read.
    /// </summary>
    private void UnfogSite(int siteId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null) return;

        var site = GetSiteById(siteId);
        if (site == null || site.cells.Count == 0) return;

        // Carved floor plus its one-cell halo, and NOTHING else. Two separate
        // things decide whether a cell reads as a wall:
        //
        //   PAINTED  -- CaveWallRenderer caps and faces a solid cell when it is
        //               claimed or 8-adjacent to a MINED cell.
        //   REVEALED -- the fog over it has been cleared.
        //
        // A cell needs both. Revealed but unpainted shows the bare floor tile
        // underneath; painted but fogged is simply invisible, which is what left
        // sites with open floor and no wall attached to it.
        //
        // The halo is EXACTLY the set the renderer will paint, because "painted"
        // is defined as 8-adjacency to mined floor and the carved cells are the
        // mined floor. So this reveals every wall cell and not one cell more:
        // measured over the 24 plans, zero painted-but-fogged and zero
        // revealed-but-unpainted. The masonry skin is a subset of the halo, which
        // is why the separate skin pass that used to sit below is gone.
        //
        // Deeper masonry stays dark, exactly like the unexcavated rock it is drawn
        // as, and mining through the skin reveals the next layer by the ordinary
        // route.
        RevealWithBorder(terrain, site.cells);

        // The carriageway THROUGH the hold. It was subtracted from site.cells at
        // placement (the road is built AROUND a site, not cut through it), so it
        // is not in the halo above, and it used to sit fogged until its own road
        // segment happened to reveal -- a dark trench across a village the player
        // had fully discovered. A hold you have found does not have a fogged
        // strip through the middle of it.
        if (site.pavedRoadCells != null && site.pavedRoadCells.Count > 0)
            RevealWithBorder(terrain, site.pavedRoadCells);

        // Masonry needs no pass of its own. The skin -- the only masonry the
        // renderer ever paints -- is already inside the halo above, and anything
        // deeper must stay fogged or it shows as bare floor.

        var open = new List<Vector3Int>(site.cells.Count);
        foreach (var sv in site.cells) open.Add(sv.ToVector3Int());
        // The paving opens with the hold too. Revealing it without marking it
        // would recreate the exact fault the comment above warns about: the rock
        // flanking it needs a MINED neighbour before the renderer will frame it,
        // and unframed revealed rock shows as bare floor.
        if (site.pavedRoadCells != null)
            foreach (var sv in site.pavedRoadCells) open.Add(sv.ToVector3Int());
        floor.TileInfluence?.MarkNaturalFloor(open);
    }

    // ── Core Cavern Generation (DAY 34/35) ────────────────────────

    /// <summary>
    /// Generates a noisy-disc cavern around the core cell plus 1–3 outward
    /// tunnels. Populates featureData.coreCavern. Also seeds reservedCoreCells
    /// so chamber + river generation can avoid the cavern + tunnel footprint.
    ///
    /// Cavern cells are pre-revealed (no influence-touch reveal needed).
    /// </summary>
    private void GenerateCoreCavernAndTunnels(
        System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        var cavern = new CoreCavernData
        {
            centerCell = SerializableVector3Int.From(centerCell),
        };

        // ── Cavern shape: noisy disc ──────────────────────────────
        var cavernSet = new HashSet<Vector3Int> { centerCell };

        int innerSq = cavernInnerRadius * cavernInnerRadius;
        int outerSq = cavernOuterRadius * cavernOuterRadius;
        float innerR = cavernInnerRadius;
        float outerR = Mathf.Max(cavernInnerRadius + 0.001f, cavernOuterRadius);

        for (int dx = -cavernOuterRadius; dx <= cavernOuterRadius; dx++)
            for (int dy = -cavernOuterRadius; dy <= cavernOuterRadius; dy++)
            {
                int sq = dx * dx + dy * dy;
                if (sq > outerSq) continue;

                var c = new Vector3Int(centerCell.x + dx, centerCell.y + dy, 0);

                if (sq <= innerSq)
                {
                    cavernSet.Add(c);
                }
                else
                {
                    // Falloff: closer to inner radius -> more likely included.
                    float dist = Mathf.Sqrt(sq);
                    float t = (dist - innerR) / (outerR - innerR);   // 0 at inner, 1 at outer
                    double keepChance = 1.0 - t * 0.7;               // 1.0 -> 0.3
                    if (rng.NextDouble() < keepChance) cavernSet.Add(c);
                }
            }

        // Top up to min size by walking outward to adjacent cells.
        int safetyTopUp = 0;
        while (cavernSet.Count < cavernMinCells && safetyTopUp++ < 200)
        {
            var candidates = new List<Vector3Int>();
            foreach (var c in cavernSet)
            {
                TryAddCandidate(c + Vector3Int.up, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.down, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.left, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.right, cavernSet, candidates);
            }
            if (candidates.Count == 0) break;
            cavernSet.Add(candidates[rng.Next(candidates.Count)]);
        }

        // Trim to max size by removing farthest-from-core cells (never the core).
        while (cavernSet.Count > cavernMaxCells)
        {
            Vector3Int farthest = centerCell;
            int maxSq = -1;
            foreach (var c in cavernSet)
            {
                if (c == centerCell) continue;
                int sq = (c.x - centerCell.x) * (c.x - centerCell.x)
                       + (c.y - centerCell.y) * (c.y - centerCell.y);
                if (sq > maxSq) { maxSq = sq; farthest = c; }
            }
            if (farthest == centerCell) break;
            cavernSet.Remove(farthest);
        }

        cavern.cells = ToSerializable(new List<Vector3Int>(cavernSet));

        // Mirror into reserved set for chamber + river exclusion.
        reservedCoreCells.Clear();
        foreach (var c in cavernSet) reservedCoreCells.Add(c);

        // ── Tunnels ──────────────────────────────────────────────
        int tunnelCount = PickWeightedTunnelCount(rng);
        double baseAngle = rng.NextDouble() * 2.0 * Math.PI;
        var tunnelAngles = PickTunnelAngles(rng, tunnelCount, baseAngle);

        for (int i = 0; i < tunnelAngles.Count; i++)
        {
            var tunnel = BuildTunnel(
                rng, centerCell, cavernSet, tunnelAngles[i], floorRadius, i);
            if (tunnel == null || tunnel.cells.Count == 0) continue;

            cavern.tunnels.Add(tunnel);
            foreach (var sv in tunnel.cells)
                reservedCoreCells.Add(sv.ToVector3Int());
        }

        featureData.coreCavern = cavern;

        // Pre-reveal: cavern + tunnels are visible from start.
        UnfogCoreCavern();
    }

    private static void TryAddCandidate(
        Vector3Int cell, HashSet<Vector3Int> existing, List<Vector3Int> candidates)
    {
        if (!existing.Contains(cell)) candidates.Add(cell);
    }

    /// <summary>Picks 1, 2, or 3 from the configured weights.</summary>
    private int PickWeightedTunnelCount(System.Random rng)
    {
        int w1 = Mathf.Max(0, tunnelWeight1);
        int w2 = Mathf.Max(0, tunnelWeight2);
        int w3 = Mathf.Max(0, tunnelWeight3);
        int total = w1 + w2 + w3;
        if (total <= 0) return 2; // safety default
        int roll = rng.Next(total);
        if (roll < w1) return 1;
        if (roll < w1 + w2) return 2;
        return 3;
    }

    /// <summary>
    /// Picks angles from the 8-way set (rotated by baseAngle), ensuring no two
    /// picks are closer than tunnelMinAngleSeparation degrees apart.
    /// </summary>
    private List<double> PickTunnelAngles(System.Random rng, int count, double baseAngle)
    {
        var picks = new List<double>();
        var candidates = new List<double>();
        for (int i = 0; i < 8; i++)
            candidates.Add(baseAngle + i * Math.PI / 4.0);

        // Shuffle candidates (Fisher-Yates).
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        double minSep = tunnelMinAngleSeparation * Math.PI / 180.0;

        foreach (var a in candidates)
        {
            bool tooClose = false;
            foreach (var p in picks)
            {
                if (AngularDistance(a, p) < minSep) { tooClose = true; break; }
            }
            if (!tooClose) picks.Add(a);
            if (picks.Count >= count) break;
        }
        return picks;
    }

    private static double AngularDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % (2.0 * Math.PI);
        if (d > Math.PI) d = 2.0 * Math.PI - d;
        return d;
    }

    /// <summary>
    /// Builds one tunnel from the cavern edge outward along the given angle,
    /// with 2→1 width taper, perpendicular wobble, and floor-radius clamping.
    /// </summary>
    private TunnelData BuildTunnel(
        System.Random rng, Vector3Int coreCell, HashSet<Vector3Int> cavernCells,
        double angle, int floorRadius, int tunnelIndex)
    {
        int length = rng.Next(tunnelMinLength, tunnelMaxLength + 1);

        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);
        double perpDx = -dy;
        double perpDy = dx;

        // Find tunnel start: farthest cavern cell along the angle.
        Vector3Int startCell = coreCell;
        for (int r = 1; r <= cavernOuterRadius + 3; r++)
        {
            var test = new Vector3Int(
                coreCell.x + (int)Math.Round(r * dx),
                coreCell.y + (int)Math.Round(r * dy), 0);
            if (cavernCells.Contains(test)) startCell = test;
            else break;
        }

        // Walk a wobbling centreline outward from one step past the start cell.
        var centreline = new List<Vector3Int>();
        double curX = startCell.x + dx;
        double curY = startCell.y + dy;
        int driftSteps = 0;
        for (int step = 0; step < length; step++)
        {
            if (rng.NextDouble() < tunnelWobbleChance)
                driftSteps += (rng.Next(2) == 0) ? -1 : 1;

            var cell = new Vector3Int(
                (int)Math.Round(curX + driftSteps * perpDx),
                (int)Math.Round(curY + driftSteps * perpDy), 0);

            int rdx = cell.x - coreCell.x;
            int rdy = cell.y - coreCell.y;
            if (rdx * rdx + rdy * rdy > floorRadius * floorRadius) break;

            centreline.Add(cell);
            curX += dx;
            curY += dy;
        }

        // Dilate the centreline to a tapering width: tunnelWidth at the cavern mouth
        // narrowing to tunnelTipWidth at the far end. Square brush matches PaintRiver.
        var added = new HashSet<Vector3Int>();
        int span = centreline.Count;
        for (int i = 0; i < span; i++)
        {
            var c = centreline[i];
            float t = span > 1 ? (float)i / (span - 1) : 0f;
            int w = Mathf.Max(tunnelTipWidth, Mathf.RoundToInt(Mathf.Lerp(tunnelWidth, tunnelTipWidth, t)));
            int half = (w - 1) / 2;
            int extra = (w - 1) - 2 * half;
            for (int ox = -half; ox <= half + extra; ox++)
                for (int oy = -half; oy <= half + extra; oy++)
                {
                    var p = new Vector3Int(c.x + ox, c.y + oy, 0);
                    int pdx = p.x - coreCell.x;
                    int pdy = p.y - coreCell.y;
                    if (pdx * pdx + pdy * pdy > floorRadius * floorRadius) continue;
                    if (cavernCells.Contains(p)) continue;
                    added.Add(p);
                }
        }

        return new TunnelData
        {
            angleDegrees = (float)(angle * 180.0 / Math.PI),
            cells = ToSerializable(new List<Vector3Int>(added)),
        };
    }

    // ── Entrance Cave Generation ──────────────────────────────────

    /// <summary>
    /// Carves the surface entrance: a wobbling tunnel driven inward from a
    /// seeded point on the disc edge, through the bedrock rim, plus small CA
    /// offshoot chamberlets on its interior half. Cells are reserved so
    /// chambers and rivers avoid the footprint. Pre-revealed and registered
    /// as natural floor (walkable, unclaimed) like the core cavern.
    /// </summary>
    private void GenerateEntranceCave(System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        double angle = rng.NextDouble() * 2.0 * Math.PI;
        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);

        // Mouth: the outermost in-disc cell along the bearing.
        var mouth = new Vector3Int(
            centerCell.x + (int)Math.Round(floorRadius * dx),
            centerCell.y + (int)Math.Round(floorRadius * dy), 0);
        int guard = 0;
        while (!IsInFloorRadius(mouth, centerCell, floorRadius) && guard++ < 8)
            mouth = new Vector3Int(
                mouth.x - (int)Math.Round(dx),
                mouth.y - (int)Math.Round(dy), 0);

        // Centreline: walk inward (toward the core) with perpendicular wobble.
        double inDx = -dx, inDy = -dy;
        double perpDx = -inDy, perpDy = inDx;
        int length = rng.Next(entranceTunnelMinLength, entranceTunnelMaxLength + 1);

        // Straight approach run on the apron: the channel breaches the rim and
        // runs out along the road bearing, so the surface physically connects
        // to the tunnel — no fog band, no boxed-in mouth.
        var centreline = new List<Vector3Int>();
        for (int step = entranceApronRun; step >= 1; step--)
            centreline.Add(new Vector3Int(
                mouth.x + (int)Math.Round(step * dx),
                mouth.y + (int)Math.Round(step * dy), 0));
        centreline.Add(mouth);
        double curX = mouth.x + inDx;
        double curY = mouth.y + inDy;
        int driftSteps = 0;
        for (int step = 1; step < length; step++)
        {
            if (rng.NextDouble() < entranceWobbleChance)
                driftSteps += (rng.Next(2) == 0) ? -1 : 1;

            var cell = new Vector3Int(
                (int)Math.Round(curX + driftSteps * perpDx),
                (int)Math.Round(curY + driftSteps * perpDy), 0);

            if (IsInExclusion(cell, centerCell)) break;
            if (reservedCoreCells.Contains(cell)) break;

            centreline.Add(cell);
            curX += inDx;
            curY += inDy;
        }

        // Dilate with taper: mouth width at the surface end, tip width inside.
        var carved = new HashSet<Vector3Int>();
        int span = centreline.Count;
        for (int i = 0; i < span; i++)
        {
            var c = centreline[i];
            // Full mouth width across the whole outdoor approach run; the taper
            // toward tip width begins only once the channel enters the rock.
            float t;
            if (i <= entranceApronRun || span - 1 <= entranceApronRun) t = 0f;
            else t = Mathf.Clamp01((float)(i - entranceApronRun) / (span - 1 - entranceApronRun));
            int w = Mathf.Max(entranceTipWidth,
                Mathf.RoundToInt(Mathf.Lerp(entranceMouthWidth, entranceTipWidth, t)));
            int half = (w - 1) / 2;
            int extra = (w - 1) - 2 * half;
            for (int ox = -half; ox <= half + extra; ox++)
                for (int oy = -half; oy <= half + extra; oy++)
                {
                    var p = new Vector3Int(c.x + ox, c.y + oy, 0);
                    if (IsInExclusion(p, centerCell)) continue;
                    if (reservedCoreCells.Contains(p)) continue;
                    carved.Add(p);
                }
        }

        // Offshoot chamberlets: bud from the interior half of the centreline,
        // offset perpendicular so they hang off the tunnel like cave pockets.
        // RunChamberCA already filters floor radius, exclusion, and reserved cells.
        int offshoots = rng.Next(entranceOffshootMin, entranceOffshootMax + 1);
        for (int i = 0; i < offshoots && span > 3; i++)
        {
            var stem = centreline[rng.Next(span / 2, span)];
            int side = rng.Next(2) == 0 ? -1 : 1;
            int reach = 2 + rng.Next(2);
            var pocketCentre = new Vector3Int(
                stem.x + (int)Math.Round(side * reach * perpDx),
                stem.y + (int)Math.Round(side * reach * perpDy), 0);

            var pocket = LargestConnectedRegion(
                RunChamberCA(rng, pocketCentre, entranceOffshootBoxSize, centerCell, floorRadius));
            if (pocket.Count < 4) continue;
            foreach (var p in pocket) carved.Add(p);
        }

        // The resting place: rolled exactly like an offshoot, then withheld.
        // It is never added to `carved`, so UnfogEntranceCave never reveals it
        // and MarkNaturalFloor never opens it. It is simply a designated volume
        // of ordinary stone that nothing else is allowed to overwrite.
        var restPocket = new List<Vector3Int>();
        var restCentre = Vector3Int.zero;
        if (entranceRestPocket && span > 3)
        {
            for (int attempt = 0; attempt < 6 && restPocket.Count == 0; attempt++)
            {
                var stem = centreline[rng.Next(span / 2, span)];
                int side = rng.Next(2) == 0 ? -1 : 1;
                int reach = 3 + rng.Next(2);
                var centre = new Vector3Int(
                    stem.x + (int)Math.Round(side * reach * perpDx),
                    stem.y + (int)Math.Round(side * reach * perpDy), 0);

                var pocket = LargestConnectedRegion(
                    RunChamberCA(rng, centre, entranceOffshootBoxSize, centerCell, floorRadius));
                if (pocket.Count < 4) continue;

                // Never overlap the tunnel or another feature, and never sit in
                // the bedrock rim. The rim is unclaimable and unminable, so a
                // body sealed there is a body nobody ever finds -- and since the
                // rim map is built after features, the test is a margin in cells
                // rather than a bedrock query.
                long safeR = Math.Max(0, floorRadius - entranceRestRimMargin);
                bool clash = false;
                foreach (var p in pocket)
                {
                    long ddx = p.x - centerCell.x, ddy = p.y - centerCell.y;
                    if (ddx * ddx + ddy * ddy > safeR * safeR) { clash = true; break; }
                    if (carved.Contains(p) || reservedCoreCells.Contains(p)) { clash = true; break; }
                }
                if (clash) continue;

                restPocket.AddRange(pocket);
                restCentre = centre;
                if (!restPocket.Contains(restCentre)) restCentre = restPocket[0];
            }
        }

        if (carved.Count == 0) return;
        carved.Add(mouth);

        // Spawn point: the outer end of the approach run, on the pilgrim road —
        // parties materialize on the surface and walk in; retreaters leave in view.
        var spawn = centreline.Count > 0 ? centreline[0] : mouth;
        if (!carved.Contains(spawn)) spawn = mouth;

        featureData.entranceCave = new EntranceCaveData
        {
            mouthCell = SerializableVector3Int.From(mouth),
            spawnCell = SerializableVector3Int.From(spawn),
            hasSpawnCell = true,
            angleDegrees = (float)(angle * 180.0 / Math.PI),
            cells = ToSerializable(new List<Vector3Int>(carved)),
            restCells = ToSerializable(restPocket),
            restCell = SerializableVector3Int.From(restCentre),
            hasRest = restPocket.Count > 0,
        };

        // Reserve so chambers and rivers avoid the cave footprint. The resting
        // pocket is reserved too, though it is never carved: nothing else may
        // grow through the stone the body is in.
        foreach (var c in carved) reservedCoreCells.Add(c);
        foreach (var c in restPocket) reservedCoreCells.Add(c);

        UnfogEntranceCave();
    }

    /// <summary>
    /// Unfogs the entrance cave with a wall border and registers its cells as
    /// natural floor (walkable, unclaimed, mined). Runs on fresh generation
    /// and save-load; MarkNaturalFloor is idempotent so both paths are safe.
    /// </summary>
    private void UnfogEntranceCave()
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null || featureData.entranceCave == null) return;

        RevealWithBorder(terrain, featureData.entranceCave.cells);

        // Only IN-DISC cells become natural (mined) floor. Outdoor approach cells
        // stay out of minedTiles entirely: the wall renderer paints boundaries
        // around mined floor, so mined cells on the apron grow stone frames on
        // the grass, and the two-cell drape rule then blocks their walkability.
        // Outdoor cells are walkable via the entrance-cave feature lookup instead.
        var open = new List<Vector3Int>(featureData.entranceCave.cells.Count);
        foreach (var sv in featureData.entranceCave.cells)
        {
            var c = sv.ToVector3Int();
            if (terrain.IsWithinBounds(c)) open.Add(c);
        }
        floor.TileInfluence?.MarkNaturalFloor(open);
    }

    private List<Vector3Int> BuildRiverPolyline(
        System.Random rng, Vector3Int floorCentre, int floorRadius, int controlPointCount)
    {
        double startAngle = rng.NextDouble() * 2.0 * Math.PI;
        double startX = floorCentre.x + floorRadius * Math.Cos(startAngle);
        double startY = floorCentre.y + floorRadius * Math.Sin(startAngle);
        double direction = startAngle + Math.PI;

        double meanderRad = riverMeanderDegrees * Math.PI / 180.0;

        var polyline = new List<Vector3Int>
        {
            new Vector3Int((int)Math.Round(startX), (int)Math.Round(startY), 0),
        };

        double cx = startX, cy = startY;
        for (int i = 1; i < controlPointCount; i++)
        {
            double delta = (rng.NextDouble() - 0.5) * 2.0 * meanderRad;
            direction += delta;

            cx += Math.Cos(direction) * riverSegmentLength;
            cy += Math.Sin(direction) * riverSegmentLength;

            var next = new Vector3Int((int)Math.Round(cx), (int)Math.Round(cy), 0);
            if (!IsInFloorRadius(next, floorCentre, floorRadius)) break;
            polyline.Add(next);
        }
        return polyline;
    }

    /// <summary>
    /// Routes a river's outward stretch: from its rim end, across the forest, out to
    /// surfaceRiverDepth past the rim. Two conflicts with the pilgrim road are resolved
    /// here, in this order:
    ///
    ///   1. Running ALONGSIDE the road. If the river's rim bearing sits inside
    ///      roadClearanceDegrees of the road bearing, the outward bearing is rotated
    ///      away from the road (whichever side is further) before any routing happens.
    ///      Two rays leaving the same centre diverge, so adequate angular separation is
    ///      what stops the two features shadowing each other.
    ///
    ///   2. CROSSING the road. Meander can still sweep a river over the road further
    ///      out. A crossing is allowed -- and recorded as a ford -- only when it is
    ///      near-square (within fordSquareToleranceDegrees of 90) and the river is at
    ///      least minFordWidth wide, which is what the ford art needs. Otherwise the
    ///      candidate is rejected and re-routed on a bearing rotated further from the
    ///      road; after the attempt budget the river is pushed clear of the road cone
    ///      entirely and any residual crossing cells are dropped rather than painted as
    ///      an unartworked crossing.
    /// </summary>
    private void BuildSurfaceStretch(
        System.Random rng, List<Vector3Int> polyline, int width,
        Vector3Int floorCentre, int floorRadius,
        HashSet<Vector3Int> surfaceCells, HashSet<Vector3Int> fordCells)
    {
        Vector3Int rimEnd = polyline[0];
        double rimBearing = Math.Atan2(rimEnd.y - floorCentre.y, rimEnd.x - floorCentre.x);

        float roadDeg = featureData?.entranceCave != null
            ? featureData.entranceCave.angleDegrees
            : float.NaN;
        bool haveRoad = !float.IsNaN(roadDeg);
        double roadRad = haveRoad ? roadDeg * Math.PI / 180.0 : 0.0;

        // Conflict 1: push the outward bearing out of the road cone.
        double outBearing = rimBearing;
        if (haveRoad)
        {
            double sep = SignedAngleDelta(outBearing, roadRad);
            double need = roadClearanceDegrees * Math.PI / 180.0;
            if (Math.Abs(sep) < need)
                outBearing = roadRad + (sep >= 0 ? need : -need);
        }

        const int attempts = 6;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var line = BuildSurfacePolyline(rng, rimEnd, outBearing,
                                            floorCentre, floorRadius);
            if (line.Count < 2) return;

            var painted = PaintSurfaceRiver(line, width, floorCentre, floorRadius);
            if (painted.Count == 0) return;

            if (!haveRoad)
            {
                foreach (var c in painted) surfaceCells.Add(c);
                return;
            }

            // Conflict 2: judge every road crossing.
            bool lastPass = attempt == attempts - 1;
            var crossing = CollectRoadCrossing(painted, floorCentre, roadRad);
            if (crossing.Count == 0)
            {
                foreach (var c in painted) surfaceCells.Add(c);
                return;
            }

            bool square = CrossingIsSquare(line, floorCentre, roadRad);
            if (square && width >= minFordWidth)
            {
                foreach (var c in painted) surfaceCells.Add(c);
                foreach (var c in crossing) fordCells.Add(c);
                return;
            }

            if (lastPass)
            {
                // Out of attempts: keep the river but drop the unartworked crossing.
                foreach (var c in painted)
                    if (!crossing.Contains(c)) surfaceCells.Add(c);
                return;
            }

            // Rotate further from the road and try again.
            double away = SignedAngleDelta(outBearing, roadRad) >= 0 ? 1.0 : -1.0;
            outBearing += away * (roadClearanceDegrees * Math.PI / 180.0) * 0.75;
        }
    }

    /// <summary>Smallest signed angle from b to a, in radians, wrapped to -PI..PI.</summary>
    private static double SignedAngleDelta(double a, double b)
    {
        double d = a - b;
        while (d > Math.PI) d -= 2.0 * Math.PI;
        while (d < -Math.PI) d += 2.0 * Math.PI;
        return d;
    }

    /// <summary>Outward centreline, meandering like the cave stretch, stopping once it
    /// passes surfaceRiverDepth beyond the rim.</summary>
    private List<Vector3Int> BuildSurfacePolyline(
        System.Random rng, Vector3Int rimEnd, double bearing,
        Vector3Int floorCentre, int floorRadius)
    {
        double meanderRad = riverMeanderDegrees * Math.PI / 180.0;
        double limit = floorRadius + surfaceRiverDepth;

        var line = new List<Vector3Int> { rimEnd };
        double cx = rimEnd.x, cy = rimEnd.y, dir = bearing;

        // Generous step budget: meander means the path is longer than the radial gap.
        int maxSteps = Mathf.Max(4, (surfaceRiverDepth / Mathf.Max(1, riverSegmentLength)) * 3);
        for (int i = 0; i < maxSteps; i++)
        {
            // Half the cave meander: a forest river should wander, not switchback across
            // the road repeatedly, and every extra sweep is another crossing to judge.
            dir += (rng.NextDouble() - 0.5) * meanderRad;
            cx += Math.Cos(dir) * riverSegmentLength;
            cy += Math.Sin(dir) * riverSegmentLength;

            var next = new Vector3Int((int)Math.Round(cx), (int)Math.Round(cy), 0);
            line.Add(next);

            double ddx = cx - floorCentre.x, ddy = cy - floorCentre.y;
            if (Math.Sqrt(ddx * ddx + ddy * ddy) >= limit) break;
        }
        return line;
    }

    /// <summary>Dilate the outward centreline. Mirrors PaintRiver but keeps only cells
    /// OUTSIDE the disc, so the dungeon stretch is untouched and the rim itself stays
    /// solid -- the bedrock band is never watered by this pass.</summary>
    private HashSet<Vector3Int> PaintSurfaceRiver(
        List<Vector3Int> line, int width, Vector3Int floorCentre, int floorRadius)
    {
        var centreline = new HashSet<Vector3Int>();
        for (int i = 0; i < line.Count - 1; i++)
            foreach (var p in BresenhamLine(line[i], line[i + 1]))
                centreline.Add(p);

        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half;

        var dilated = new HashSet<Vector3Int>();
        foreach (var c in centreline)
            for (int dx = -half; dx <= half + extra; dx++)
                for (int dy = -half; dy <= half + extra; dy++)
                {
                    var p = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (IsInFloorRadius(p, floorCentre, floorRadius)) continue;   // disc is not ours
                    dilated.Add(p);
                }
        return dilated;
    }

    /// <summary>Painted cells that lie on the pilgrim road corridor.</summary>
    private HashSet<Vector3Int> CollectRoadCrossing(
        HashSet<Vector3Int> painted, Vector3Int floorCentre, double roadRad)
    {
        double ox = Math.Cos(roadRad), oy = Math.Sin(roadRad);
        var hit = new HashSet<Vector3Int>();
        foreach (var c in painted)
        {
            double dx = c.x - floorCentre.x, dy = c.y - floorCentre.y;
            double along = dx * ox + dy * oy;
            if (along <= 0) continue;
            double across = Math.Abs(dx * oy - dy * ox);
            if (across <= roadHalfWidthForFord) hit.Add(c);
        }
        return hit;
    }

    /// <summary>True when the centreline meets the road near-square. Measured on the
    /// segment whose midpoint sits closest to the road axis, which is the crossing the
    /// ford art would have to cover.</summary>
    private bool CrossingIsSquare(
        List<Vector3Int> line, Vector3Int floorCentre, double roadRad)
    {
        double ox = Math.Cos(roadRad), oy = Math.Sin(roadRad);
        double bestAcross = double.MaxValue;
        double bestAngle = 0;
        bool found = false;

        for (int i = 0; i < line.Count - 1; i++)
        {
            double mx = (line[i].x + line[i + 1].x) * 0.5 - floorCentre.x;
            double my = (line[i].y + line[i + 1].y) * 0.5 - floorCentre.y;
            if (mx * ox + my * oy <= 0) continue;
            double across = Math.Abs(mx * oy - my * ox);
            if (across >= bestAcross) continue;

            bestAcross = across;
            double sx = line[i + 1].x - line[i].x, sy = line[i + 1].y - line[i].y;
            if (sx == 0 && sy == 0) continue;
            bestAngle = Math.Atan2(sy, sx);
            found = true;
        }
        if (!found) return false;

        double delta = Math.Abs(SignedAngleDelta(bestAngle, roadRad)) * 180.0 / Math.PI;
        if (delta > 90.0) delta = 180.0 - delta;
        return Math.Abs(delta - 90.0) <= fordSquareToleranceDegrees;
    }

    private HashSet<Vector3Int> PaintRiver(
        List<Vector3Int> polyline, int width,
        Vector3Int floorCentre, int floorRadius)
    {
        var centreline = new HashSet<Vector3Int>();
        for (int i = 0; i < polyline.Count - 1; i++)
            foreach (var p in BresenhamLine(polyline[i], polyline[i + 1]))
                centreline.Add(p);

        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half;

        var dilated = new HashSet<Vector3Int>();
        foreach (var c in centreline)
        {
            for (int dx = -half; dx <= half + extra; dx++)
                for (int dy = -half; dy <= half + extra; dy++)
                {
                    var p = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (!IsInFloorRadius(p, floorCentre, floorRadius)) continue;
                    if (IsInExclusion(p, floorCentre)) continue;
                    if (reservedCoreCells.Contains(p)) continue;
                    dilated.Add(p);
                }
        }
        return dilated;
    }

    /// <summary>
    /// Splits a river footprint into a water core and dry floor banks by peeling the
    /// outer shell inward bankWidth times. Each pass moves every boundary cell (one with
    /// an 8-neighbour outside the remaining water) into the banks. Peeling stops early if
    /// the next pass would leave no water, so a channel always survives and very thin
    /// rivers simply get no banks. Erosion is symmetric, so both banks share a width.
    /// </summary>
    /// <summary>
    /// Splits a river footprint into a water core and dry floor banks by peeling the outer
    /// shell inward bankWidth times.
    ///
    /// Cells at or beyond noBankRadius (measured from floorCentre) are never peeled: where
    /// the river crosses the bedrock rim the channel stays water wall-to-wall. A dry bank
    /// there is registered as FeatureType.RiverBank, which IsRiver does not match, so
    /// CaveWallClassifier.IsSolid treats it as rock and the renderer frames it -- putting a
    /// wall between the cave river and the forest river. Keeping it water avoids that, and
    /// reads better besides: a gorge cut through bedrock has no walkable shelf.
    /// </summary>
    private static void SplitRiverBanks(
        HashSet<Vector3Int> footprint, int bankWidth,
        Vector3Int floorCentre, int noBankRadius,
        out HashSet<Vector3Int> water, out HashSet<Vector3Int> banks)
    {
        water = new HashSet<Vector3Int>(footprint);
        banks = new HashSet<Vector3Int>();
        var ring = new List<Vector3Int>();

        long noBankSq = (long)noBankRadius * noBankRadius;

        for (int k = 0; k < bankWidth; k++)
        {
            ring.Clear();
            foreach (var c in water)
            {
                // Out in the rim the channel keeps its full width as water.
                long ddx = c.x - floorCentre.x, ddy = c.y - floorCentre.y;
                if (noBankRadius > 0 && ddx * ddx + ddy * ddy >= noBankSq) continue;

                bool edge = false;
                for (int dx = -1; dx <= 1 && !edge; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!water.Contains(new Vector3Int(c.x + dx, c.y + dy, 0))) { edge = true; break; }
                    }
                if (edge) ring.Add(c);
            }
            if (ring.Count == 0 || ring.Count >= water.Count) break;   // keep a water core
            foreach (var c in ring) { water.Remove(c); banks.Add(c); }
        }
    }

    private static IEnumerable<Vector3Int> BresenhamLine(Vector3Int a, Vector3Int b)
    {
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            yield return new Vector3Int(x0, y0, 0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private bool PickRandomCellInDisc(
        System.Random rng, Vector3Int floorCentre, int floorRadius,
        int excludeRadius, out Vector3Int result)
    {
        for (int tries = 0; tries < 32; tries++)
        {
            double r = Math.Sqrt(rng.NextDouble()) * floorRadius;
            if (r < excludeRadius) continue;
            double a = rng.NextDouble() * 2.0 * Math.PI;
            var cell = new Vector3Int(
                floorCentre.x + (int)Math.Round(r * Math.Cos(a)),
                floorCentre.y + (int)Math.Round(r * Math.Sin(a)), 0);
            result = cell;
            return true;
        }
        result = default;
        return false;
    }

    private static bool IsInFloorRadius(Vector3Int cell, Vector3Int floorCentre, int floorRadius)
    {
        int dx = cell.x - floorCentre.x, dy = cell.y - floorCentre.y;
        return dx * dx + dy * dy <= floorRadius * floorRadius;
    }

    private bool IsInExclusion(Vector3Int cell, Vector3Int floorCentre)
    {
        int dx = cell.x - floorCentre.x, dy = cell.y - floorCentre.y;
        return dx * dx + dy * dy < exclusionRadiusFromCenter * exclusionRadiusFromCenter;
    }

    private static List<SerializableVector3Int> ToSerializable(List<Vector3Int> cells)
    {
        var list = new List<SerializableVector3Int>(cells.Count);
        foreach (var c in cells) list.Add(SerializableVector3Int.From(c));
        return list;
    }

    private void RebuildLookup()
    {
        cellLookup.Clear();
        reservedCoreCells.Clear();
        if (featureData == null) return;

        // DAY 34/35 — Cavern + tunnels share a single FeatureType.CoreCavern.
        if (featureData.coreCavern != null)
        {
            foreach (var sv in featureData.coreCavern.cells)
            {
                var c = sv.ToVector3Int();
                cellLookup[c] = new FeatureRef { type = FeatureType.CoreCavern, featureId = 0 };
                reservedCoreCells.Add(c);
            }
            foreach (var t in featureData.coreCavern.tunnels)
            {
                foreach (var sv in t.cells)
                {
                    var c = sv.ToVector3Int();
                    cellLookup[c] = new FeatureRef { type = FeatureType.CoreCavern, featureId = 0 };
                    reservedCoreCells.Add(c);
                }
            }
        }

        if (featureData.entranceCave != null)
        {
            foreach (var sv in featureData.entranceCave.cells)
            {
                var c = sv.ToVector3Int();
                cellLookup[c] = new FeatureRef { type = FeatureType.EntranceCave, featureId = 0 };
                reservedCoreCells.Add(c);
            }
        }

        // Roads before chambers and rivers: both were generated to yield to the
        // carriageway, and RebuildRoadCells has already handed back anything a
        // river took, so these three passes cannot fight over a cell.
        RebuildRoadCells();
        foreach (var seg in roadSegments)
            foreach (var c in seg.cells)
                cellLookup[c] = new FeatureRef { type = FeatureType.Road, featureId = seg.segmentId };

        // Sites after roads and before chambers, matching the carve order: a site
        // was composed around a carriageway that was already there, and chambers
        // were generated to avoid both. Only the CARVED interior enters the lookup
        // -- masonry is solid rock and belongs to the terrain type map, not here.
        siteCells.Clear();
        if (featureData.sites != null)
            foreach (var s in featureData.sites)
                foreach (var sv in s.cells)
                {
                    var c = sv.ToVector3Int();
                    siteCells.Add(c);
                    cellLookup[c] = new FeatureRef { type = FeatureType.AncientSite, featureId = s.id };
                }

        foreach (var ch in featureData.chambers)
            foreach (var sv in ch.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.Chamber, featureId = ch.id };

        foreach (var r in featureData.rivers)
        {
            foreach (var sv in r.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.River, featureId = r.id };
            if (r.bankCells != null)
                foreach (var sv in r.bankCells)
                    cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.RiverBank, featureId = r.id };
        }
    }

    // ── Debug Overlay ─────────────────────────────────────────────

    [ContextMenu("Paint Debug Overlay")]
    public void PaintDebugOverlay()
    {
        if (debugOverlayTilemap == null) { Debug.LogWarning("[TerrainFeatureGenerator] debugOverlayTilemap not assigned."); return; }
        if (featureData == null) { Debug.LogWarning("[TerrainFeatureGenerator] No feature data — generate or load first."); return; }

        debugOverlayTilemap.ClearAllTiles();

        if (debugChamberTile != null)
            foreach (var ch in featureData.chambers)
            {
                if (!IsChamberRevealed(ch.id)) continue;
                foreach (var sv in ch.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugChamberTile);
            }

        if (debugRiverTile != null)
            foreach (var r in featureData.rivers)
            {
                if (!IsRiverRevealed(r.id)) continue;
                foreach (var sv in r.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugRiverTile);
            }

        if (debugRoadTile != null)
            foreach (var seg in roadSegments)
            {
                if (!IsRoadSegmentRevealed(seg.segmentId)) continue;
                foreach (var c in seg.cells)
                    debugOverlayTilemap.SetTile(c, debugRoadTile);
            }

        // Sites. Unlike the three above, these are drawn whether revealed or not:
        // the overlay is a development tool and an unrevealed site is exactly what
        // you want to look at when checking a floor's layout.
        if (debugSiteTile != null && featureData.sites != null)
            foreach (var s in featureData.sites)
            {
                foreach (var sv in s.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugSiteTile);
                if (s.ruinsCells != null)
                    foreach (var sv in s.ruinsCells)
                        debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugSiteTile);
            }
    }

    /// <summary>
    /// Registers a revealed river's dry banks as natural floor (walkable, unclaimed,
    /// mined) so they read as ground, give units footing, and form a mined frontier the
    /// rock beyond them can be dug from. Mirrors the core cavern: it lands in minedTiles
    /// and is therefore saved, so banks persist across a reload without re-marking.
    /// </summary>
    private void MarkRiverBanksAsFloor(int riverId)
    {
        if (featureData == null || floor == null) return;
        var inf = floor.TileInfluence;
        if (inf == null) return;
        foreach (var r in featureData.rivers)
        {
            if (r.id != riverId) continue;
            if (r.bankCells == null || r.bankCells.Count == 0) return;
            var cells = new List<Vector3Int>(r.bankCells.Count);
            foreach (var sv in r.bankCells) cells.Add(sv.ToVector3Int());
            inf.MarkNaturalFloor(cells);
            return;
        }
    }

    /// <summary>Paint one river's cells into the water tilemap (real rendering).</summary>
    private static List<SerializableVector3Int> ToSerializableList(HashSet<Vector3Int> cells)
    {
        var list = new List<SerializableVector3Int>(cells.Count);
        foreach (var c in cells) list.Add(SerializableVector3Int.From(c));
        return list;
    }

    /// <summary>Every surface river cell on this floor. Read by SurfaceZoneGenerator so
    /// camps and trails can steer clear of water.</summary>
    public bool IsSurfaceRiver(Vector3Int cell) => surfaceRiverCells.Contains(cell);

    /// <summary>True where the surface river crosses the pilgrim road: the ford.</summary>
    public bool IsFord(Vector3Int cell) => surfaceFordCells.Contains(cell);

    /// <summary>True on the surface river OR within surfaceRiverPropClearance of it.
    /// Precomputed when the rivers are painted, so scatter code pays one hash lookup per
    /// candidate cell instead of probing a disc around every one.</summary>
    public bool IsNearSurfaceRiver(Vector3Int cell) => surfaceRiverNearCells.Contains(cell);

    private readonly HashSet<Vector3Int> surfaceRiverCells = new();
    private readonly HashSet<Vector3Int> surfaceFordCells = new();
    private readonly HashSet<Vector3Int> surfaceRiverNearCells = new();

    /// <summary>Paints every river's surface stretch and fills the lookup sets. Unlike the
    /// cave stretch this does NOT wait on discovery: water entering the forest is the
    /// agreed hint that a river runs somewhere in the rock below. Called on fresh
    /// generation and on load.</summary>
    public void PaintAllSurfaceRivers()
    {
        surfaceRiverCells.Clear();
        surfaceFordCells.Clear();
        surfaceRiverNearCells.Clear();
        if (featureData?.rivers == null) return;

        foreach (var r in featureData.rivers)
        {
            if (r.surfaceCells != null)
                foreach (var sv in r.surfaceCells)
                {
                    var c = sv.ToVector3Int();
                    surfaceRiverCells.Add(c);
                    if (surfaceWaterTilemap != null && surfaceWaterTile != null)
                        surfaceWaterTilemap.SetTile(c, surfaceWaterTile);
                }
            if (r.fordCells != null)
                foreach (var sv in r.fordCells) surfaceFordCells.Add(sv.ToVector3Int());
        }

        BuildSurfaceRiverClearance();
    }

    /// <summary>Dilates the surface river by surfaceRiverPropClearance into the lookup that
    /// scatter code tests. Circular rather than square, so the cleared bank follows the
    /// channel instead of blocking out a boxy corridor.</summary>
    private void BuildSurfaceRiverClearance()
    {
        surfaceRiverNearCells.Clear();
        int r = Mathf.Max(0, surfaceRiverPropClearance);
        int rSq = r * r;

        foreach (var c in surfaceRiverCells)
        {
            if (r == 0) { surfaceRiverNearCells.Add(c); continue; }
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy > rSq) continue;
                    surfaceRiverNearCells.Add(new Vector3Int(c.x + dx, c.y + dy, 0));
                }
        }
    }

    private void PaintRiverWater(int riverId)
    {
        if (waterTilemap == null || waterTile == null) return;
        foreach (var r in featureData.rivers)
        {
            if (r.id != riverId) continue;
            foreach (var sv in r.cells)
            {
                var rc = sv.ToVector3Int();
                waterTilemap.SetTile(rc, waterTile);
                revealedRiverCells.Add(rc);
            }
            return;
        }
    }

    /// <summary>Repaint water for every already-revealed river (used after a load).</summary>
    public void RepaintRevealedRiverWater()
    {
        if (featureData == null || waterTilemap == null || waterTile == null) return;
        foreach (int id in featureData.revealedRiverIds)
            PaintRiverWater(id);
    }

    private void PaintRiverOverlay(int riverId)
    {
        if (debugOverlayTilemap == null || debugRiverTile == null) return;
    }

    private void PaintChamberOverlay(int chamberId)
    {
        if (debugOverlayTilemap == null || debugChamberTile == null) return;
        foreach (var ch in featureData.chambers)
        {
            if (ch.id != chamberId) continue;
            foreach (var sv in ch.cells)
                debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugChamberTile);
            return;
        }
    }

    // ── Fog removal (DAY 31 PART 1) ──────────────────────────────

    private void UnfogRiver(int riverId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null) return;
        foreach (var r in featureData.rivers)
        {
            if (r.id != riverId) continue;
            RevealWithBorder(terrain, r.cells);
            if (r.bankCells != null) RevealWithBorder(terrain, r.bankCells);
            return;
        }
    }

    /// <summary>
    /// Reveals each listed cell plus its 1-cell border, so a river's wall caps sit on
    /// revealed ground the instant it's discovered (mirrors UnfogCoreCavern). Fog left
    /// under a cap shows through its transparent edges as a dark rim.
    /// </summary>
    private static void RevealWithBorder(DungeonTerrain terrain, List<SerializableVector3Int> cells)
    {
        foreach (var sv in cells)
        {
            var c = sv.ToVector3Int();
            terrain.RevealTile(c);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                    {
                        var n = new Vector3Int(c.x + dx, c.y + dy, c.z);
                        terrain.RevealTile(n);
                    }
        }
    }

    /// <summary>Paints one revealed road segment into the road tilemap.</summary>
    private void PaintRoadSegment(int segmentId)
    {
        if (roadTilemap == null || roadTile == null) return;
        var seg = GetRoadSegment(segmentId);
        if (seg == null) return;
        foreach (var c in seg.cells)
        {
            // Inside a site's yielded band the road is paved over on the floor
            // tilemap (canon 19). The segment must not paint here at all: a
            // road tile would sit above the tinted paving and re-open the pale
            // band this hook exists to prevent.
            if (sitePavedRoad.Contains(c)) continue;
            roadTilemap.SetTile(c, roadTile);
        }
    }

    /// <summary>Headless consistency report over this floor's reveal state.
    ///
    /// THREE independent mechanisms have to agree cell for cell and nothing has
    /// been checking that they do:
    ///
    ///   PAINTED  -- CaveWallRenderer caps a solid cell that is claimed or
    ///               8-adjacent to mined floor (or to revealed river water).
    ///   REVEALED -- the fog tile over the cell has been cleared.
    ///   OPEN     -- MarkNaturalFloor has entered it in minedTiles, which is
    ///               what makes its neighbours paintable in the first place.
    ///
    /// Revealed but unpainted shows the bare floor tile under a wall that was
    /// never drawn. Painted but fogged is an invisible wall. Both were measured
    /// by hand once during the site-plan pass and never re-measured, and every
    /// road artefact since has been one of these disagreements.
    ///
    /// Returns a human-readable multi-line report; the caller logs it.</summary>
    public string BuildRevealConsistencyReport(int sampleLimit = 6)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[RevealCheck] floor " + (floor != null ? floor.FloorIndex : -1));

        var terrain = floor != null ? floor.Terrain : null;
        var inf = floor != null ? floor.TileInfluence : null;
        var types = floor != null ? floor.TerrainTypeMap : null;
        if (terrain == null || inf == null || featureData == null)
        {
            sb.AppendLine("  ABORT: floor not ready.");
            return sb.ToString();
        }

        var fog = terrain.FogTilemap;
        var classifier = new CaveWallClassifier(inf, this, terrain);
        var owners = BuildOwnerMap();
        int radius = terrain.CurrentRadius;
        var core = terrain.CoreCell;

        var revealedUnpainted = new List<Vector3Int>();
        var paintedFogged = new List<Vector3Int>();
        var roadUnpainted = new List<Vector3Int>();
        var roadOverPaving = new List<Vector3Int>();
        var hardJoins = new List<Vector3Int>();
        // Owner on each side of a join, counted. A square cut across the
        // carriageway can come from a segment seam OR from a site's paved band
        // ending at its footprint, and the coordinates alone cannot tell them
        // apart.
        var joinPairs = new Dictionary<string, int>();

        // Histograms keyed by the feature that CAUSED the disagreement, so a
        // systemic source shows up as one bar instead of a list of
        // coordinates. Plain arrays indexed by the enum: no allocation, and it
        // cannot silently miss a value the way a switch can.
        int featureCount = System.Enum.GetValues(typeof(FeatureType)).Length + 1;
        var unpaintedBy = new int[featureCount];
        var foggedBy = new int[featureCount];

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                var cell = new Vector3Int(core.x + dx, core.y + dy, 0);

                bool revealed = fog == null || fog.GetTile(cell) == null;
                bool solid = classifier.IsSolid(cell);
                // Split rather than short-circuited. Written as
                // `solid && WouldBePainted(..., out cause)` the call is skipped
                // whenever solid is false, so `cause` is not definitely
                // assigned on every path and C# refuses the later read (CS0165).
                bool painted = false;
                FeatureType cause = (FeatureType)(-1);
                if (solid) painted = WouldBePainted(cell, inf, out cause);

                if (revealed && solid && !painted)
                {
                    revealedUnpainted.Add(cell);
                    unpaintedBy[FeatureIndex(GetFeatureAt(cell))]++;
                }
                if (!revealed && painted)
                {
                    paintedFogged.Add(cell);
                    foggedBy[FeatureIndex(cause)]++;
                }

                if (GetFeatureAt(cell) == FeatureType.Road)
                {
                    bool paved = sitePavedRoad.Contains(cell);
                    bool hasRoadTile = roadTilemap != null && roadTilemap.GetTile(cell) != null;
                    if (paved && hasRoadTile) roadOverPaving.Add(cell);
                    if (!paved && !hasRoadTile) roadUnpainted.Add(cell);

                    if (revealed && fog != null)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            var n = cell + Orth4[k];
                            if (GetFeatureAt(n) != FeatureType.Road) continue;
                            if (fog.GetTile(n) == null) continue;
                            hardJoins.Add(cell);

                            string mine = owners.TryGetValue(cell, out var oA) ? oA : "UNTRACKED";
                            string theirs = owners.TryGetValue(n, out var oB) ? oB : "UNTRACKED";
                            string pair = mine + "  ->  " + theirs;
                            joinPairs.TryGetValue(pair, out int seen);
                            joinPairs[pair] = seen + 1;
                            break;
                        }
                    }
                }
            }
        }

        Line(sb, "revealed but UNPAINTED (bare floor where a wall belongs)",
             revealedUnpainted, sampleLimit);
        Detail(sb, revealedUnpainted, sampleLimit, inf, types, fog, owners, true);
        Histogram(sb, "        by feature UNDER the cell:", unpaintedBy);

        Line(sb, "PAINTED but fogged (invisible wall)", paintedFogged, sampleLimit);
        Detail(sb, paintedFogged, sampleLimit, inf, types, fog, owners, false);
        Histogram(sb, "        by feature that PAINTED it:", foggedBy);

        Line(sb, "road cell with NO road tile", roadUnpainted, sampleLimit);
        Line(sb, "road tile sitting OVER site paving", roadOverPaving, sampleLimit);
        Line(sb, "reveal joins (revealed road touching fogged road)",
             hardJoins, sampleLimit);
        // The COUNT cannot move under a seam-ownership change, so it could never
        // say whether the fray worked. Straightness can: a square cut across a
        // five-wide carriageway leaves a run of five or more, a frayed seam
        // leaves ones and twos.
        sb.Append("          longest straight run of join cells: ")
          .Append(LongestStraightRun(hardJoins))
          .AppendLine("   (5+ means a square cut, 1-2 means frayed)");
        foreach (var kv in joinPairs)
            sb.Append("          ").Append(kv.Value.ToString().PadLeft(4))
              .Append("  ").AppendLine(kv.Key);

        int bad = revealedUnpainted.Count + paintedFogged.Count
                + roadUnpainted.Count + roadOverPaving.Count;
        sb.AppendLine(bad == 0
            ? "  PASS -- paint, reveal and framing agree on every cell."
            : "  FAIL -- " + bad + " disagreeing cell(s).");
        return sb.ToString();
    }

    private static int FeatureIndex(FeatureType f)
    {
        int i = (int)f;
        return (i < 0) ? 0 : i + 1;
    }

    private static void Histogram(System.Text.StringBuilder sb, string label, int[] counts)
    {
        bool any = false;
        for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) { any = true; break; }
        if (!any) return;

        sb.AppendLine(label);
        var values = System.Enum.GetValues(typeof(FeatureType));
        if (counts[0] > 0) sb.AppendLine("            " + counts[0] + "  (none)");
        foreach (FeatureType f in values)
        {
            int i = FeatureIndex(f);
            if (i < counts.Length && counts[i] > 0)
                sb.AppendLine("            " + counts[i] + "  " + f);
        }
    }

    /// <summary>Every cell this floor's generation knows about, tagged with the
    /// structure that produced it and that structure's reveal state.
    ///
    /// v2 asked GetFeatureAt and got None for every cause on all five floors.
    /// That is the wrong lens: the feature lookup carries carved interiors, not
    /// the bank / ford / surface / tunnel / masonry sets that MarkNaturalFloor
    /// and the unfog passes actually work on. This asks the save data instead.
    ///
    /// Later writers win, so the order runs general to specific -- a site's own
    /// paved band should report as the site's, not as the road's.</summary>
    private Dictionary<Vector3Int, string> BuildOwnerMap()
    {
        var m = new Dictionary<Vector3Int, string>();
        if (featureData == null) return m;

        if (featureData.coreCavern != null)
        {
            TagCells(m, featureData.coreCavern.cells, "coreCavern");
            if (featureData.coreCavern.tunnels != null)
                foreach (var t in featureData.coreCavern.tunnels)
                    TagCells(m, t.cells, "coreCavern.tunnel");
        }
        if (featureData.entranceCave != null)
            TagCells(m, featureData.entranceCave.cells, "entranceCave");

        if (featureData.rivers != null)
            foreach (var r in featureData.rivers)
            {
                string rev = featureData.revealedRiverIds != null
                          && featureData.revealedRiverIds.Contains(r.id) ? "revealed" : "FOGGED";
                TagCells(m, r.cells, "river" + r.id + ".water[" + rev + "]");
                TagCells(m, r.bankCells, "river" + r.id + ".BANK[" + rev + "]");
                TagCells(m, r.surfaceCells, "river" + r.id + ".surface[" + rev + "]");
                TagCells(m, r.fordCells, "river" + r.id + ".FORD[" + rev + "]");
            }

        if (featureData.chambers != null)
            foreach (var ch in featureData.chambers)
                TagCells(m, ch.cells, "chamber" + ch.id);

        foreach (var seg in roadSegments)
            TagCells(m, seg.cells, "roadSeg" + seg.segmentId + "["
                     + (IsRoadSegmentRevealed(seg.segmentId) ? "revealed" : "FOGGED") + "]");

        if (featureData.sites != null)
            foreach (var s in featureData.sites)
            {
                string rev = IsSiteRevealed(s.id) ? "revealed" : "FOGGED";
                TagCells(m, s.ruinsCells, "site" + s.id + ".MASONRY[" + rev + "]");
                TagCells(m, s.cells, "site" + s.id + ".interior[" + rev + "]");
                TagCells(m, s.pavedRoadCells, "site" + s.id + ".paving[" + rev + "]");
            }

        return m;
    }

    private static void TagCells(Dictionary<Vector3Int, string> m,
                                 List<SerializableVector3Int> cells, string label)
    {
        if (cells == null) return;
        foreach (var sv in cells) m[sv.ToVector3Int()] = label;
    }

    private static void TagCells(Dictionary<Vector3Int, string> m,
                                 List<Vector3Int> cells, string label)
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Count; i++) m[cells[i]] = label;
    }

    private string Describe(Vector3Int c, Dictionary<Vector3Int, string> owners,
                            TileInfluenceManager inf, UnityEngine.Tilemaps.Tilemap fog)
    {
        string owner = (owners != null && owners.TryGetValue(c, out var o)) ? o : "UNTRACKED";
        bool revealed = fog == null || fog.GetTile(c) == null;
        return "(" + c.x + "," + c.y + ") in=" + owner
             + " mined=" + (inf.IsTileMined(c) ? "Y" : "n")
             + " revealed=" + (revealed ? "Y" : "n")
             + " claimed=" + (inf.IsTileClaimed(c) ? "Y" : "n");
    }

    private static void Line(System.Text.StringBuilder sb, string label,
                             List<Vector3Int> cells, int sampleLimit)
    {
        sb.Append("  ").Append(cells.Count.ToString().PadLeft(6)).Append("  ").Append(label);
        if (cells.Count > 0)
        {
            sb.Append("   e.g.");
            for (int i = 0; i < cells.Count && i < sampleLimit; i++)
                sb.Append(" (").Append(cells[i].x).Append(",").Append(cells[i].y).Append(")");
        }
        sb.AppendLine();
    }

    /// <summary>Per-cell reasons, and the PRODUCER behind each. A count says
    /// something is wrong; a producer says which pass did it.</summary>
    private void Detail(System.Text.StringBuilder sb, List<Vector3Int> cells, int limit,
                        TileInfluenceManager inf, TerrainTypeMap types,
                        UnityEngine.Tilemaps.Tilemap fog,
                        Dictionary<Vector3Int, string> owners, bool unpaintedCase)
    {
        for (int i = 0; i < cells.Count && i < limit; i++)
        {
            var c = cells[i];
            int minedNbrs = 0, revealedNbrs = 0, riverNbrs = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (inf.IsTileMined(n)) minedNbrs++;
                    if (revealedRiverCells.Contains(n)) riverNbrs++;
                    if (fog == null || fog.GetTile(n) == null) revealedNbrs++;
                }

            sb.Append("        ").Append(Describe(c, owners, inf, fog))
              .Append("  terrain=").Append(types != null ? types.GetTerrainAt(c).ToString() : "?")
              .Append("  minedNbrs=").Append(minedNbrs)
              .Append("  riverNbrs=").Append(riverNbrs)
              .Append("  revealedNbrs=").Append(revealedNbrs)
              .AppendLine();

            // For a fogged-but-painted cell: whatever OPENED the neighbour that
            // paints it. For a revealed-but-unpainted cell: whatever REVEALED
            // the ground around it. Either way, the pass that did it.
            int shown = 0;
            for (int dy = -1; dy <= 1 && shown < 3; dy++)
                for (int dx = -1; dx <= 1 && shown < 3; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector3Int(c.x + dx, c.y + dy, 0);
                    bool nRevealed = fog == null || fog.GetTile(n) == null;
                    bool interesting = unpaintedCase ? nRevealed : inf.IsTileMined(n);
                    if (!interesting) continue;
                    sb.Append("             ").Append(unpaintedCase ? "revealer " : "opener   ")
                      .Append(Describe(n, owners, inf, fog)).AppendLine();
                    shown++;
                }
            if (shown == 0)
                sb.Append("             (no ").Append(unpaintedCase ? "revealed" : "mined")
                  .AppendLine(" neighbour found)");
        }
    }

    /// <summary>Longest run of join cells that are collinear and contiguous, on
    /// either axis. The straightness of the seam is the thing the eye reads as
    /// architecture, and it is the only part a seam-ownership change can move.</summary>
    private static int LongestStraightRun(List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return 0;
        var set = new HashSet<Vector3Int>(cells);
        int best = 1;
        foreach (var c in cells)
        {
            // Count only from a run's start, so each run is measured once.
            if (!set.Contains(new Vector3Int(c.x - 1, c.y, c.z)))
            {
                int n = 1;
                while (set.Contains(new Vector3Int(c.x + n, c.y, c.z))) n++;
                if (n > best) best = n;
            }
            if (!set.Contains(new Vector3Int(c.x, c.y - 1, c.z)))
            {
                int n = 1;
                while (set.Contains(new Vector3Int(c.x, c.y + n, c.z))) n++;
                if (n > best) best = n;
            }
        }
        return best;
    }

    private static readonly Vector3Int[] Orth4 =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
    };

    /// <summary>Mirror of CaveWallRenderer's paint rule, and reports WHICH rule
    /// fired. Kept deliberately literal rather than shared, so the report FAILS
    /// if the renderer's rule drifts instead of agreeing with it by
    /// construction.</summary>
    private bool WouldBePainted(Vector3Int cell, TileInfluenceManager inf, out FeatureType cause)
    {
        cause = (FeatureType)(-1);
        if (inf.IsTileClaimed(cell)) { cause = GetFeatureAt(cell); return true; }
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Vector3Int(cell.x + dx, cell.y + dy, 0);
                if (inf.IsTileMined(n)) { cause = GetFeatureAt(n); return true; }
                if (revealedRiverCells.Contains(n)) { cause = FeatureType.River; return true; }
            }
        return false;
    }

    /// <summary>Paints every segment on the floor, revealed or not. Fog is what
    /// hides a road the player has not found; the tilemap carries no secrets on
    /// its own. Called from ApplyRuinsOverrides on both the fresh and the load
    /// path, after PaintSitePaving has filled sitePavedRoad.</summary>
    public void PaintAllRoads()
    {
        foreach (var seg in roadSegments) PaintRoadSegment(seg.segmentId);
    }

    /// <summary>Repaints every already-revealed road segment (used after a load).</summary>
    public void RepaintRevealedRoads()
    {
        if (featureData == null || featureData.revealedRoadSegmentIds == null) return;
        foreach (int id in featureData.revealedRoadSegmentIds) PaintRoadSegment(id);
    }

    /// <summary>
    /// Reveals one road segment with its wall border and registers the carriageway
    /// as natural floor -- walkable, unclaimed, mined -- exactly as a chamber does.
    /// That is also what makes the wall renderer treat it as open, since IsSolid
    /// keys off minedTiles rather than the feature type.
    /// </summary>
    private void UnfogRoadSegment(int segmentId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null) return;

        var seg = GetRoadSegment(segmentId);
        if (seg == null || seg.cells.Count == 0) return;

        foreach (var c in seg.cells)
        {
            terrain.RevealTile(c);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        terrain.RevealTile(new Vector3Int(c.x + dx, c.y + dy, c.z));
        }

        floor.TileInfluence?.MarkNaturalFloor(seg.cells);
    }

    private void UnfogChamber(int chamberId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null) return;
        foreach (var ch in featureData.chambers)
        {
            if (ch.id != chamberId) continue;

            // Chambers reveal like every other feature: unfog WITH the wall border
            // so the surrounding rock shows, and register the floor as natural
            // (mined, unclaimed) so the wall renderer frames it, wilds and
            // adventurers can walk it, and the rock beside it becomes mineable
            // through the normal claimable ring. The clear-before-claim gate
            // (IsCellInUnclearedChamber) still holds until the wilds are dead.
            RevealWithBorder(terrain, ch.cells);

            var open = new List<Vector3Int>(ch.cells.Count);
            foreach (var sv in ch.cells)
                open.Add(sv.ToVector3Int());
            floor.TileInfluence?.MarkNaturalFloor(open);
            return;
        }
    }

    private void UnfogCoreCavern()
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null || featureData.coreCavern == null) return;

        var open = new List<Vector3Int>();
        foreach (var sv in featureData.coreCavern.cells)
        {
            var c = sv.ToVector3Int();
            terrain.RevealTile(c);
            open.Add(c);
        }
        foreach (var t in featureData.coreCavern.tunnels)
            foreach (var sv in t.cells)
            {
                var c = sv.ToVector3Int();
                terrain.RevealTile(c);
                open.Add(c);
            }

        // Also reveal the 1-cell wall border around the cavern so its wall caps
        // sit on floor, not fog. Fog left under a cap shows through the cap's
        // transparent edges as a dark outline.
        foreach (var c in open)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0)
                        terrain.RevealTile(new Vector3Int(c.x + dx, c.y + dy, c.z));

        // The cavern + tunnels are pre-existing open floor: register them as
        // walkable (mined) but unclaimed so they are passable and the wall
        // renderer treats them as open. Runs on fresh-gen and save-load.
        floor.TileInfluence?.MarkNaturalFloor(open);
    }

    /// <summary>Re-runs the unfog and open passes. The save controller calls this
    /// AFTER TileInfluence.LoadSaveData, and the ordering is the whole point:
    /// features load first, mark their ground open, and then LoadSaveData does
    /// minedTiles.Clear() and restores from the save -- silently discarding
    /// every MarkNaturalFloor that just ran. The reveals survive, because fog is
    /// a tilemap LoadSaveData never touches, so ground came back REVEALED but
    /// not OPEN and nothing would frame the rock beside it. Site paving and
    /// un-revealed road segments both reported revealed=Y mined=n in the
    /// consistency report because of this. Both halves are idempotent, so the
    /// second run costs only the walk.</summary>
    public void ReassertOpenGround() => UnfogAllRevealedFeatures();

    private void UnfogAllRevealedFeatures()
    {
        if (featureData == null) return;
        UnfogCoreCavern();
        UnfogEntranceCave();
        foreach (var rid in featureData.revealedRiverIds) UnfogRiver(rid);
        foreach (var cid in featureData.revealedChamberIds) UnfogChamber(cid);
        if (featureData.revealedRoadSegmentIds != null)
            foreach (var sid in featureData.revealedRoadSegmentIds) UnfogRoadSegment(sid);
        if (featureData.revealedSiteIds != null)
            foreach (var sid in featureData.revealedSiteIds) UnfogSite(sid);

        // Once, at the end. Each UnfogRoadSegment above bumps RevealVersion, so
        // rebuilding per segment would be quadratic on a floor with eighty of them.
        EnsureRoadFeatherBand();
    }

    [ContextMenu("Clear Debug Overlay")]
    public void ClearDebugOverlay()
    {
        if (debugOverlayTilemap != null) debugOverlayTilemap.ClearAllTiles();
    }

    [ContextMenu("Reveal All Features (debug)")]
    public void DebugRevealAll()
    {
        if (featureData == null) { Debug.LogWarning("[TerrainFeatureGenerator] No feature data."); return; }
        foreach (var ch in featureData.chambers) RevealChamber(ch.id);
        foreach (var r in featureData.rivers) RevealRiver(r.id);
        foreach (var seg in roadSegments) RevealRoadSegment(seg.segmentId);

        // Sites. Their absence here is why a floor could log "5 sites" and still
        // show nothing after Reveal All Features: the roads unfogged and the
        // ruins beside them stayed under fog.
        if (featureData.sites != null)
            foreach (var s in featureData.sites) RevealSite(s.id);

        Debug.Log($"[TerrainFeatureGenerator] All features revealed (debug): " +
                  $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers, " +
                  $"{roadSegments.Count} road segments, " +
                  $"{(featureData.sites != null ? featureData.sites.Count : 0)} sites.");
    }

    [ContextMenu("Log Feature Stats")]
    public void LogFeatureStats()
    {
        if (featureData == null) { Debug.Log("[TerrainFeatureGenerator] No feature data."); return; }
        int riverCells = 0; foreach (var r in featureData.rivers) riverCells += r.cells.Count;
        int chamberCells = 0; foreach (var c in featureData.chambers) chamberCells += c.cells.Count;
        int clearedChambers = 0;
        foreach (var c in featureData.chambers) if (c.cleared) clearedChambers++;
        int cavernCells = featureData.coreCavern != null ? featureData.coreCavern.cells.Count : 0;
        int tunnelCount = featureData.coreCavern != null ? featureData.coreCavern.tunnels.Count : 0;
        int tunnelCells = 0;
        if (featureData.coreCavern != null)
            foreach (var t in featureData.coreCavern.tunnels) tunnelCells += t.cells.Count;

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex}: " + 
            (featureData.coreCavern != null ? $"core cavern ({cavernCells} cells, {tunnelCount} tunnels, {tunnelCells} tunnel cells), " : "") +
            $"{featureData.chambers.Count} chambers ({chamberCells} cells, " +
            $"{featureData.revealedChamberIds.Count} revealed, {clearedChambers} cleared), " +
            $"{featureData.rivers.Count} rivers ({riverCells} cells, " +
            $"{featureData.revealedRiverIds.Count} revealed). " +
            $"{(featureData.sites != null ? featureData.sites.Count : 0)} sites " +
            $"({(featureData.revealedSiteIds != null ? featureData.revealedSiteIds.Count : 0)} revealed). " +
            $"{featureData.roads.Count} roads ({roadSegments.Count} segments, " +
            $"{roadCells.Count} cells, {featureData.revealedRoadSegmentIds.Count} revealed). " +
            $"Lookup size {cellLookup.Count}.");
    }
}