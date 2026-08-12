using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor authoring for the den tunnel substrate (canon 42). One entry per
/// floor index that should carry a den; a floor without an entry generates
/// nothing and costs nothing -- the RoadNetworkProfile contract, for the same
/// reason: the floor template prefab is SHARED across floors, so per-floor
/// settings cannot live on the generator's own inspector.
/// </summary>
[Serializable]
public class DenTunnelFloorEntry
{
    [Tooltip("Zero-based floor index. Floor 2 in the UI is index 1.")]
    [Min(0)] public int floorIndex = 1;

    [Tooltip("Which population holds this floor. Occupier takes the network as "
           + "it finds it; Excavator widens and extends it over days.")]
    public DenKind kind = DenKind.Occupier;

    [Header("Band (fractions of floor radius)")]
    [Tooltip("Canon 19's placement band, and canon 42 holds the den to it: "
           + "reveal is influence-touch only, and a den outside the band is a "
           + "den nobody meets.")]
    [Range(0f, 1f)] public float bandInner = 0.15f;
    [Range(0f, 1f)] public float bandOuter = 0.65f;

    [Header("Runs")]
    [Tooltip("How many tunnels leave the den. Each takes a chamber if one is "
           + "in range and ends in the rock if none is.")]
    [Min(1)] public int runCount = 3;

    [Tooltip("A chamber past this fraction of the radius is not worth a tunnel: "
           + "it sits in the bedrock rim's approach and past anything the "
           + "player reaches. Measured at 0.85 -- see Tools/sim_den_tunnels.py.")]
    [Range(0.5f, 1f)] public float endpointClamp = 0.85f;

    [Tooltip("Longest run the den will drive, as a fraction of floor radius.")]
    [Range(0.1f, 1.5f)] public float maxRunFraction = 0.90f;

    [Tooltip("Nearer than this and the chamber IS the den -- no tunnel is cut.")]
    [Min(1)] public int minRunCells = 12;

    [Tooltip("Cells a dead-end run drives before stopping in the rock, minimum.")]
    [Min(1)] public int deadEndMin = 30;
    [Tooltip("...and maximum. Clamped to maxRunFraction of the radius.")]
    [Min(1)] public int deadEndMax = 80;

    [Header("Section")]
    [Tooltip("Tunnel width at the den mouth, in cells.")]
    [Min(1)] public int width = 3;
    [Tooltip("Width at the far tip. The run tapers from Width to this.")]
    [Min(1)] public int tipWidth = 2;

    [Header("Clearances")]
    [Tooltip("Tunnels keep this far clear of the stair landing's starter blob. "
           + "Canon 42: first contact must be the player's digging or creep "
           + "reaching the network, never the network reaching the landing.")]
    [Min(0)] public int landingKeepClear = 10;

    [Tooltip("Cells of centreline per reveal segment. The road contract: a run "
           + "comes into view a stretch at a time, never entire.")]
    [Min(4)] public int segmentLength = 40;
}

/// <summary>What holds a den, which decides the verb rather than the stats.</summary>
public enum DenKind
{
    // Appended only, never reordered: this serialises into saves as an int.
    Occupier = 0,   // goblins -- take the network as they find it, never dig
    Excavator = 1,  // kobolds -- widen and extend it over days
}

/// <summary>
/// The one authored asset for den tunnels. A fresh asset already carries the
/// shipped layout: floor index 1 the goblin hole, floor index 2 the kobold den
/// alongside the trunk road and the gatehouse.
/// </summary>
[CreateAssetMenu(fileName = "DenTunnelProfile", menuName = "Dungeon/Den Tunnel Profile")]
public class DenTunnelProfile : ScriptableObject
{
    [SerializeField]
    private List<DenTunnelFloorEntry> floors = new List<DenTunnelFloorEntry>
    {
        // Floor index 1, radius 150 -- the goblin hole. Three runs: measured
        // over 2000 seeds this lands 2.30 chamber links and 0.70 dead ends,
        // with no chamber link at all on 2.9 per cent of seeds. Four runs was
        // tried and dropped -- it buys 0.3 of a link and carves a third more
        // rock on the smallest den floor.
        new DenTunnelFloorEntry
        {
            floorIndex = 1,
            kind = DenKind.Occupier,
            runCount = 3,
        },

        // Floor index 2, radius 250 -- the kobold den. Four runs: 3.38 links
        // and 0.62 dead ends, no link at all on 0.7 per cent. The extra run
        // is earned by the radius rather than by the population; an excavator
        // adds its own over days.
        new DenTunnelFloorEntry
        {
            floorIndex = 2,
            kind = DenKind.Excavator,
            runCount = 4,
        },
    };

    public IReadOnlyList<DenTunnelFloorEntry> Floors => floors;

    /// <summary>The entry for a floor, or null when that floor carries no den.</summary>
    public DenTunnelFloorEntry For(int floorIndex)
    {
        for (int i = 0; i < floors.Count; i++)
            if (floors[i] != null && floors[i].floorIndex == floorIndex)
                return floors[i];
        return null;
    }
}
