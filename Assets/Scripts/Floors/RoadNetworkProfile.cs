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
             "what turn a tree into a network with loops.")]
    [Min(0)] public int extraLoopEdges = 2;

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
}

/// <summary>
/// The one authored asset for the deep roads. Holds a per-floor entry list;
/// a floor without an entry has no roads.
///
/// A fresh asset already carries the shipped layout: floor index 3 (the fourth
/// floor, radius 400) gets the surviving trunk, floor index 4 (radius 600) gets
/// the dead network. Edit or delete entries freely -- nothing else reads the
/// floor indices.
/// </summary>
[CreateAssetMenu(fileName = "RoadNetworkProfile", menuName = "Dungeon/Road Network Profile")]
public class RoadNetworkProfile : ScriptableObject
{
    [SerializeField]
    private List<RoadFloorEntry> floors = new List<RoadFloorEntry>
    {
        new RoadFloorEntry
        {
            floorIndex = 3,
            mode = RoadMode.Trunk,
            trunkWidth = 5,
            meanderStep = 32,
            meanderAmplitude = 5f,
        },
        new RoadFloorEntry
        {
            floorIndex = 4,
            mode = RoadMode.Network,
            trunkWidth = 5,
            spurWidth = 2,
            junctionCount = 7,
            junctionMinSpacing = 90,
            extraLoopEdges = 2,
            rimTrunkCount = 3,
            brokenSpurCount = 4,
        },
    };

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
