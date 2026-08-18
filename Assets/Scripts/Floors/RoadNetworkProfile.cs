using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a floor's road layer is laid out.
///   None    -- no roads on this floor (the default for every floor without an entry).
///   Trunk   -- one surviving road, rim to rim, low meander. The living road.
///   Network -- a junction graph plus rim-bound trunks and broken spurs. The dead network.
/// </summary>
public enum RoadMode
{
    None = 0,
    Trunk = 1,
    Network = 2,
}

/// <summary>
/// Per-floor road authoring. One entry per floor index that should carry roads;
/// floors with no entry generate nothing and cost nothing.
///
/// The floor template prefab is SHARED across floors 2-5, so per-floor road
/// settings cannot live on the generator's own inspector -- they live here, on
/// one asset the shared prefab references.
/// </summary>
[Serializable]
public class RoadFloorEntry
{
    [Tooltip("Zero-based floor index. Floor 4 in the UI is index 3.")]
    [Min(0)] public int floorIndex = 3;

    public RoadMode mode = RoadMode.Trunk;

    [Header("Geometry")]
    [Tooltip("Carriageway width in cells for trunk roads. Odd widths centre exactly on " +
             "the rasterised centreline; even widths sit one cell off to the north-east.")]
    [Min(1)] public int trunkWidth = 5;

    [Tooltip("Carriageway width in cells for broken spurs.")]
    [Min(1)] public int spurWidth = 2;

    [Tooltip("Cells of centreline per reveal segment. Reveal, and later claiming, are " +
             "per segment -- touching one end of a road must not unfog the whole floor.")]
    [Min(4)] public int segmentLength = 40;

    [Tooltip("How far in from the disc edge a road stops, in cells. Roads are natural " +
             "floor and MarkNaturalFloor refuses to open bedrock, so a road driven into " +
             "the rim would register as Road in the lookup while staying solid rock. " +
             "Cover TerrainTypeMap's maxRingThickness (6 by default) plus slack, exactly " +
             "as riverBankRimMargin does.")]
    [Min(0)] public int rimMargin = 7;

    [Header("Meander")]
    [Tooltip("Cells between meander control points along an edge. Larger = straighter.")]
    [Min(4)] public int meanderStep = 24;

    [Tooltip("Maximum perpendicular wander from the straight line, in cells. The offset " +
             "is pinned to zero at both ends, so an edge always reaches its endpoint.")]
    [Min(0f)] public float meanderAmplitude = 6f;

    [Header("Network mode only")]
    [Tooltip("How many junction nodes to scatter across the disc.")]
    [Min(2)] public int junctionCount = 7;

    [Tooltip("Minimum spacing between junction nodes, in cells.")]
    [Min(1)] public int junctionMinSpacing = 60;

    [Tooltip("Non-tree edges added after the spanning tree, shortest first. These are " +
             "what turn a tree into a network with loops. Junction DEGREE is the thing " +
             "this really controls: a spanning tree alone averages just under 2 roads per " +
             "junction, so every junction reads as a bend. Loop edges are what produce " +
             "genuine crossroads for a site to sit on.")]
    [Min(0)] public int extraLoopEdges = 4;

    [Tooltip("Junctions that send a road outward toward the rim. They stop short of it " +
             "and end in collapse -- the road ran on, the rim swallowed it.")]
    [Min(0)] public int rimTrunkCount = 3;

    [Tooltip("Spurs that leave a junction and stop dead. The spurs that once climbed.")]
    [Min(0)] public int brokenSpurCount = 4;

    [Tooltip("Cells of centreline left uncarved at a broken end. The polyline is built " +
             "in full and then these cells are simply never opened -- the same trick the " +
             "resting pocket uses. The road visibly stops.")]
    [Min(1)] public int brokenGapCells = 6;

    [Tooltip("Shortest and longest a broken spur may run before its gap, in cells.")]
    [Min(4)] public int spurMinLength = 30;
    [Min(4)] public int spurMaxLength = 80;

    [Header("Trunk mode only")]
    [Tooltip("How far the far rim point may deviate from directly opposite, in degrees. " +
             "0 gives a road straight through the middle of the disc.")]
    [Range(0f, 60f)] public float trunkBearingSpread = 30f;

    [Tooltip("Smallest angle, in degrees, permitted between two roads meeting at the " +
             "same junction. Measured over 300 generated networks, the unconstrained " +
             "builder produced 4.7 pairs per floor under 25 degrees -- the long thin " +
             "slivers, worst case 0.0 degrees (two roads exactly on top of each other). " +
             "25 removes all of them and costs about 0.2 loop edges. Below 20 the " +
             "slivers come back; above 30 loop edges start being refused for nothing. " +
             "Zero disables the rule.")]
    [Range(0f, 60f)] public float minJunctionAngleDegrees = 25f;

    [Tooltip("Smallest distance, in cells, permitted between two roads that do NOT " +
             "share a junction. Stops near-parallel roads running alongside each " +
             "other. Zero disables the rule.")]
    [Min(0f)] public float minRoadSeparation = 20f;
}

/// <summary>
/// The one authored asset for the deep roads. Holds a per-floor entry list;
/// a floor without an entry has no roads.
///
/// A fresh asset already carries the shipped layout: floor index 2 (radius 250)
/// gets the surviving trunk and the dwarven gatehouse it runs through, floor
/// index 3 (radius 400) the sparse living network the village sits on, floor
/// index 4 (radius 600) the dead network. Edit or delete entries freely --
/// nothing else reads the floor indices.
/// </summary>
[CreateAssetMenu(fileName = "RoadNetworkProfile", menuName = "Dungeon/Road Network Profile")]
public class RoadNetworkProfile : ScriptableObject
{
    [SerializeField]
    private List<RoadFloorEntry> floors = new List<RoadFloorEntry>
    {
        // The trunk moved down from index 3 (radius 400) to index 2 (radius 250)
        // with the floor-plan correction. trunkWidth does NOT scale with the
        // floor -- a five-cell road is five cells wide wherever it is cut -- but
        // meanderStep does: left at 32 the road would cross a floor this size in
        // barely a dozen steps and read as a straight line.
        new RoadFloorEntry
        {
            floorIndex = 2,
            mode = RoadMode.Trunk,
            trunkWidth = 5,
            meanderStep = 20,
            meanderAmplitude = 5f,
        },

        // Floor index 3 -- radius 400, the village floor. Network mode tuned
        // SPARSE, and the offshoot ladder is deliberate: index 2's trunk has 0
        // offshoots, this floor 4 (rim trunks 2 + broken spurs 2), index 4 has
        // 7 -- so the floor reads as the last living crossroads with the
        // network already dying at its edges. meanderStep 32 / amplitude 5 are
        // the values this radius carried before the correction moved the trunk
        // down to index 2. junctionCount 4 at spacing 90 keeps the graph
        // legible on a 400 disc; loop edges stay at 1 because loops are what
        // close thin triangles.
        new RoadFloorEntry
        {
            floorIndex = 3,
            mode = RoadMode.Network,
            trunkWidth = 5,
            spurWidth = 2,
            meanderStep = 32,
            meanderAmplitude = 5f,
            junctionCount = 4,
            junctionMinSpacing = 90,
            extraLoopEdges = 1,
            rimTrunkCount = 2,
            brokenSpurCount = 2,
        },

        new RoadFloorEntry
        {
            floorIndex = 4,
            mode = RoadMode.Network,
            trunkWidth = 5,
            spurWidth = 2,
            junctionCount = 7,
            junctionMinSpacing = 90,
            extraLoopEdges = 4,
            rimTrunkCount = 3,
            brokenSpurCount = 4,
        },
    };

    // -- Road lamps (canon 54) -----------------------------------------------
    // Network-wide rather than per floor. A dwarven lamp is a dwarven lamp
    // wherever the road runs, and a per-floor spacing would be four numbers to
    // keep in agreement for no gain anybody could see.

    [Header("Lamps")]
    [Tooltip("Spawned along the centreline of every revealed Trunk and Spur " +
             "segment. Carries a DungeonPointLight; its radius, target and " +
             "halo are the prefab's own, and only the colour and lit state are " +
             "set at spawn. Null leaves the whole feature inert.")]
    [SerializeField] private GameObject lampPrefab;

    [Tooltip("Cells of CENTRELINE between lamps. Counted from the road's own " +
             "start, not from each segment's, so the spacing does not jitter " +
             "at a segment join. 0 disables lamps entirely.")]
    [SerializeField, Min(0)] private int lampSpacingCells = 8;

    [Tooltip("Dwarven lamplight. Warm, and deliberately the same family as the " +
             "hold torches: the roads and the holds are one civilisation.")]
    [SerializeField] private Color lampColour = new Color(1f, 0.82f, 0.45f, 1f);

    public GameObject LampPrefab => lampPrefab;
    public int LampSpacingCells => lampSpacingCells;
    public Color LampColour => lampColour;

    public IReadOnlyList<RoadFloorEntry> Floors => floors;

    /// <summary>The entry for a floor, or null if that floor carries no roads.</summary>
    public RoadFloorEntry GetEntry(int floorIndex)
    {
        if (floors == null) return null;
        foreach (var e in floors)
            if (e != null && e.floorIndex == floorIndex)
                return e;
        return null;
    }
}
