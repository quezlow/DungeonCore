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

    [Tooltip("The creature this den is populated with. BOTH kinds are "
           + "inhabited -- goblins hold the hole, kobolds dig it and rob "
           + "the floor. Its prefab must NOT be "
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
           + "out rounder the more it is cut. 19 for the occupier band and 24 "
           + "for the excavator reserve both correct a median of ZERO cells. "
           + "BOTH were re-swept, not scaled, when their holes shrank by a "
           + "third: scaling a box does not scale its yield linearly. Sweep on "
           + "the FULL pipeline and not on a single carve -- a 1000-sample "
           + "sweep put the excavator's worst span at 32 for box 24 and 28 for "
           + "box 23, and 1500 seeds through the real anchor and seating path "
           + "reversed it to 27 and 29. A maximum is the least stable statistic "
           + "there is.")]
    [Min(8)] public int cavityBox = 19;

    [Tooltip("The pile in the cavity, one sprite per tier, index 0 being tier 1. "
           + "Canon 42 makes a den's tier legible off population AND hoard, and "
           + "an excavator has no population at all -- so on floor index 2 this "
           + "array is the whole of the signal. An empty slot disables the "
           + "renderer rather than showing the tier below, so a partly authored "
           + "set reads as absent rather than as a den that stopped growing.")]
    public Sprite[] hoardSprites = new Sprite[5];

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

    [Header("Exploratory dig (canon 42, stage 2)")]
    [Tooltip("Rock the diggings may cut in TOTAL, over the whole run. 0 "
           + "disables the dig, which is what an Occupier authors -- goblins "
           + "never dig, and a blank here would be indistinguishable from a "
           + "floor whose cap nobody filled in.\n\n"
           + "THIS IS THE CONTENT KNOB; THE BUDGET BELOW IS ONLY THE PACING "
           + "KNOB, and that split SURVIVED re-measurement against the "
           + "shipped walk. At this cap, moving the budget from x2 to x3 "
           + "moves the share of dungeons that lose a remains by under half "
           + "a point -- nothing -- while the digging ends on day 106 "
           + "against day 132.\n\n"
           + "IT NEEDS A CAP BECAUSE NOTHING ELSE BOUNDS IT. Claimed ground is "
           + "refused and a leg turns at endpointClamp, but both bound WHERE "
           + "and neither bounds HOW MUCH: making claimed ground a hard wall "
           + "changed the total by under half a per cent, because a typical "
           + "dungeon's claim is under three per cent of the diggable disc -- "
           + "an island in a lake. Uncapped, a typical den cuts about 4,400 "
           + "cells by day 200 at x2 and 6,400 at x3, against a GENERATED "
           + "network of 1107 and entry 19's warning at 3000.\n\n"
           + "2400 is about twice the generated network, so the den at most "
           + "TRIPLES its own diggings, and that SCALE argument is now the "
           + "number's whole case: the beat rate is bought by the sense "
           + "radius below, never by rock. Lower was measured and rejected "
           + "twice -- a cap of 1107 holds the contested-discovery beat to "
           + "about 6 per cent at the shipped sense radius, and a set-piece "
           + "firing on under one run in twelve is content nobody meets. "
           + "Section J of Tools/sim_den_digger.py prints the whole sweep "
           + "against the shipped rules; the 7.3-8.0 and 14.0-14.7 figures "
           + "older text quoted were a retired model's.")]
    [Min(0)] public int exploratoryCellCap = 0;

    [Tooltip("Rock the diggings cut per day, as a MULTIPLE of that tier's "
           + "cavity rate. ADDITIVE, never a share of it: the ledger pays on "
           + "reserve cells only, so diverting the cavity budget into a tunnel "
           + "freezes the hoard, freezes the tier and slows the very dig that "
           + "was diverted -- measured, and share 1.00 arrives LATER than "
           + "share 0.50.")]
    [Min(0f)] public float exploratoryBudget = 3f;

    [Tooltip("Section of an exploratory leg, in cells. UNIFORM rather than "
           + "tapered, and 2 is a FLOOR rather than a preference.\n\n"
           + "A 1-wide leg is not 4-CONNECTED and nothing could walk it: "
           + "Centreline is Bresenham and takes diagonal steps, and Dilate at "
           + "width 1 emits the cell alone, so two consecutive diagonal cells "
           + "share no edge. Canon already rests the generated network's "
           + "breach guarantee on the same fact -- 'a 2-wide tip stays "
           + "4-connected across a diagonal step'.\n\n"
           + "Uniform is what makes GROWTH safe. The shared rasteriser lerps "
           + "the taper across the run's CURRENT length, so a tapered leg "
           + "would rewiden its own older cells every time it grew -- new "
           + "cells appearing inside stretches that were revealed days ago.")]
    [Min(2)] public int exploratoryWidth = 2;

    [Tooltip("How far off its path a leg notices something worth breaking "
           + "into, in cells. ONLY REMAINS are sensed at range -- everything "
           + "else is met on contact -- so this is a pure beat-rate knob: it "
           + "moves how often contested discovery fires and nothing about "
           + "pacing, rock or the save shape. Measured against the shipped "
           + "walk at the shipped cap: 15 held the beat to 6.5 per cent on "
           + "the sim against the report's real-geometry 4.7 -- under the "
           + "one-run-in-twelve bar canon 42 rejects content at -- and 30 "
           + "reads about 9 on the sim (expect the report one to three "
           + "points under) at zero new rock and an unchanged stop day. "
           + "Kobolds smell bones further than they see.")]
    [Min(1)] public int exploratorySenseRadius = 15;

    [Tooltip("How far a leg's bearing may wander per cell, in degrees. A "
           + "PERSISTENT walk rather than a pure one, and the distinction is "
           + "the whole model: a pure random walk's displacement grows as the "
           + "square root of its length, so a thousand cells of digging would "
           + "end thirty cells from the den and read as a scribble rather than "
           + "as prospecting. BuildTunnel already wobbles a bearing rather "
           + "than re-rolling one, so persistence is the shipped idiom as well "
           + "as the legible one.")]
    [Min(0f)] public float exploratoryDriftDegrees = 12f;
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
            // Goblins never dig, so the cap is zero and the dig never runs.
            // Written explicitly rather than inherited: a blank cap and an
            // authored zero are indistinguishable in the inspector.
            exploratoryCellCap = 0,
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
            // Reserves 400 and opens ~150 at tier 1. The reserve is carved into
            // the feature data at generation so chambers and rivers keep off it;
            // opening the rest as tier rises is half B.
            //
            // SHRUNK BY A THIRD from 550-600, following the goblin hole down.
            // The win is the span tail rather than the median: at 600 the
            // reserve reached span 33 on about 4 per cent of seeds and canon
            // accepted that as the cap doing its job, and at 400 the worst of
            // 1500 seeds is 27, so the whole distribution now sits inside entry
            // 19's 16-28 budget and the exception is deleted rather than
            // carried.
            //
            // TIER 1 DELIBERATELY STAYS AT 150. Scaling it by the same third
            // gives 100 cells against a largest-possible cave chamber of 133,
            // so a tier-1 den would be smaller than an ordinary cave on its own
            // floor -- the same trap that killed span-scaling for the goblin
            // hole. 150 to 400 is still 2.7x of visible growth.
            cavityBox = 24,
            cavityMinCells = 350,
            cavityMaxCells = 400,
            cavityTier1Cells = 150,
            // The dig. 2400 cells of rock is about twice the generated
            // network, so the den at most triples its own diggings; at x3
            // the digging stops about day 106. See the field tooltips for
            // what each number was measured against and what a lower cap
            // costs.
            exploratoryCellCap = 2400,
            exploratoryBudget = 3f,
            exploratoryWidth = 2,
            // 30, RAISED FROM 15 when the cap was re-measured: 15 held the
            // contested-discovery beat under the bar canon 42 rejects
            // content at, and the sense radius buys the beat back at zero
            // rock where a bigger cap would spend past entry 19's 3000-cell
            // warning.
            exploratorySenseRadius = 30,
            exploratoryDriftDegrees = 12f,
        },
    };

    public IReadOnlyList<DenTunnelFloorEntry> Floors => floors;

    [Tooltip("The hoard prop, shared by every den. A SpriteRenderer on the "
           + "Player sorting layer (Appendix B: every Y-sorting entity lives "
           + "there, and a pile with height is one -- the avatar passes behind "
           + "it from above and in front from below). PIVOT AT THE BASE OF THE "
           + "PILE, not its centre: Player sorts on Y, and a centre pivot makes "
           + "a tall hoard sort half a tile further back than it stands. The "
           + "per-tier sprites live on each floor entry, not here, because an "
           + "occupier hoards stolen coin and an excavator hoards dug spoil.")]
    [SerializeField] private GameObject hoardPrefab;

    public GameObject HoardPrefab => hoardPrefab;

    [Tooltip("The empty hole left where the diggers reached a buried remains "
           + "before the player did. Shared by every den, on the hoard "
           + "prefab's contract: a SpriteRenderer on the Player sorting layer "
           + "with its pivot at the BASE.\n\n"
           + "It is the whole of what makes the loss visible. The claim-halo "
           + "murmur only fires within two cells of a claimed tile, so a "
           + "player who never sensed that remains would otherwise never learn "
           + "they had been robbed of it -- canon 42 requires the visible hole "
           + "for exactly that reason. Null-safe: without art the beat still "
           + "plays and the auditor rules the slot Required, which is the "
           + "truth.")]
    [SerializeField] private GameObject remainsMarkerPrefab;

    public GameObject RemainsMarkerPrefab => remainsMarkerPrefab;

    /// <summary>The entry for a floor, or null when that floor carries no den.</summary>
    public DenTunnelFloorEntry For(int floorIndex)
    {
        for (int i = 0; i < floors.Count; i++)
            if (floors[i] != null && floors[i].floorIndex == floorIndex)
                return floors[i];
        return null;
    }
}
