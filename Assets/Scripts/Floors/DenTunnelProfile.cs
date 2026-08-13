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

    [Tooltip("The creature this den is populated with. Occupier dens only for "
           + "now -- excavators have no diggers yet. Its prefab must NOT be "
           + "relied on for a LootTable: den bodies deliberately roll no drops, "
           + "or every death would mint gold for a den whose whole income is "
           + "theft.")]
    public MonsterDefinition scavengerDefinition;

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

    [Header("Cavity")]
    [Tooltip("Cellular-automata box the cavity is carved in. READ OFF THE SWEEP "
           + "in Tools/sim_den_cavity.py, not chosen: a box whose RAW yield "
           + "already lands in the target band keeps the CA's own silhouette, "
           + "while one that overshoots is trimmed farthest-cell-first and comes "
           + "out rounder the more it is cut. 19 for the occupier band and 28 "
           + "for the excavator reserve both correct a median of ZERO cells. "
           + "The occupier was re-swept, not scaled, when its hole shrank by a "
           + "third: scaling a box does not scale its yield linearly.")]
    [Min(8)] public int cavityBox = 19;

    [Tooltip("Smallest cavity, in cells. The size clamp tops up to this.")]
    [Min(16)] public int cavityMinCells = 167;

    [Tooltip("Largest cavity, in cells -- the RESERVED footprint. Canon 42: the "
           + "occupier hole is 250-400 FIXED because goblins never dig; the "
           + "excavator reserves up to 600 and opens it over tiers. Measured "
           + "against entry 19's SPAN budget of 16-28 cells (twice the chamber "
           + "box size), NOT against its cell count -- that comparator was wrong "
           + "and is corrected in canon.")]
    [Min(16)] public int cavityMaxCells = 268;

    [Tooltip("Cells open at tier 1. Equal to cavityMaxCells for an occupier, "
           + "which opens its whole hole at once. Lower for an excavator, which "
           + "grows into its reserve -- that growth is half B and is not built.")]
    [Min(16)] public int cavityTier1Cells = 268;

    [Tooltip("Reject a den anchor within this many cells of a chamber centre. "
           + "Without it minRunCells lets a nearby chamber BE the den, which "
           + "with a real cavity means a 49-cell cave standing in for a "
           + "300-cell hole. Sized as cavity radius plus the largest chamber "
           + "radius plus margin; measured to cost 0.09 rejected samples per "
           + "seed and to starve no seed at all.")]
    [Min(0)] public int chamberSeatClearance = 20;
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
            // The hole is FIXED: they never dig, so tier reads off how full it
            // is rather than how big. Tier 1 opens all of it.
            //
            // SHRUNK BY A THIRD after it read too big on screen at 250-400.
            // Cell count was scaled, NOT span: span x0.67 would have given about
            // 145 cells, and the largest chamber RunChamberCA can produce is 133
            // -- so the den would have been the size of an ordinary cave on its
            // own floor and stopped reading as a den at all. At 167-268 it is
            // 4.6x the median chamber, 1.7x the largest, and spans 17 against
            // entry 19's budget of 16-28.
            cavityBox = 19,
            cavityMinCells = 167,
            cavityMaxCells = 268,
            cavityTier1Cells = 268,
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
            // Reserves 600 and opens ~150 at tier 1. The reserve is carved into
            // the feature data at generation so chambers and rivers keep off it;
            // opening the rest as tier rises is half B.
            cavityBox = 28,
            cavityMinCells = 550,
            cavityMaxCells = 600,
            cavityTier1Cells = 150,
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
