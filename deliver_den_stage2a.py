#!/usr/bin/env python3
"""
deliver_den_stage2a.py -- Den arc, STAGE 2a: THE DIG (canon 42).

Idempotent. Every anchor is asserted count == 1 BEFORE anything is written,
every edit is staged in memory, and a late failure leaves a completely clean
tree. Line endings are normalised on read and restored on write; a BOM is
preserved where one exists.

WHAT THIS SHIPS
  * Exploratory digging: the kobolds extend their network over days, wandering
    a persistent bearing, breaking into whatever they pass, and stopping when
    the cap is spent or the floor's remains are all taken.
  * The remains hookup: NotifyRemainsExcavated gets its first caller, the cell
    is marked consumed AND sensed so the claim-halo murmur cannot invite the
    player to dig ground that is already open, and consumed/sensed state is
    persisted for the first time.
  * The marker prop, on the DenHoardProp contract.
  * ClearDen pays the taken remains back through GrantExternalDiscovery.
  * Work sites: DiggerBudget finally has a behaviour.

WHAT THIS DOES NOT SHIP: kobold theft and stolenHoard (canon 42, ruling 7).
Deferred to stage 2b by decision, because its own canon note requires re-running
both sims and the cavity-growth sim could not be re-run honestly until the
remains lump had a caller -- which is what this stage creates.
"""

import os
import py_compile
import shutil
import subprocess
import sys
import tempfile

def _looks_like_dcr(path):
    """A DCR checkout, not merely a git one. Tested on a file this script has
    to edit anyway, so a wrong-but-plausible directory fails HERE rather than
    on an anchor assertion twenty lines later."""
    if not path:
        return False
    # A worktree or submodule stores .git as a FILE, not a directory, so isdir
    # would reject a perfectly good checkout.
    if not os.path.exists(os.path.join(path, ".git")):
        return False
    return os.path.isfile(os.path.join(
        path, "Assets", "Scripts", "Floors", "DenTunnelProfile.cs"))


def _find_repo():
    """Resolve the checkout, in order: argument, environment, the directory
    this is run from and its parents, then the old default.

    THE OLD DEFAULT WAS THE ONLY OPTION AND IT WAS WRONG on a Windows box whose
    checkout is not under the home directory -- the failure read as "not a git
    checkout" when the truth was "not there at all"."""
    if len(sys.argv) > 1:
        cand = os.path.abspath(os.path.expanduser(sys.argv[1]))
        if not _looks_like_dcr(cand):
            raise SystemExit(
                "Not a DungeonCore checkout: %s\n"
                "  (looked for .git and Assets/Scripts/Floors/DenTunnelProfile.cs)" % cand)
        return cand

    env = os.environ.get("DCR_REPO")
    if env and _looks_like_dcr(os.path.abspath(os.path.expanduser(env))):
        return os.path.abspath(os.path.expanduser(env))

    here = os.path.abspath(os.getcwd())
    while True:
        if _looks_like_dcr(here):
            return here
        parent = os.path.dirname(here)
        if parent == here:
            break
        here = parent

    home = os.path.expanduser(os.path.join("~", "DungeonCore"))
    if _looks_like_dcr(home):
        return home

    raise SystemExit(
        "Could not find the DungeonCore checkout.\n"
        "  Tried: the path given as an argument, $DCR_REPO, the current\n"
        "  directory and its parents, and %s\n"
        "  Fix: cd into the checkout and re-run, or pass the path --\n"
        "    python deliver_den_stage2a.py C:\\path\\to\\DungeonCore" % home)


REPO = None                          # resolved in main(), never at import

GUARD = "exploratoryCellCap"          # present after a successful run


# ---- file helpers -------------------------------------------------------

def load(rel):
    path = os.path.join(REPO, rel)
    with open(path, "rb") as fh:
        raw = fh.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    crlf = b"\r\n" in raw
    text = raw.replace(b"\r\n", b"\n").decode("utf-8")
    return text, crlf, bom


def store(rel, text, crlf, bom):
    path = os.path.join(REPO, rel)
    data = text.encode("utf-8")
    if crlf:
        data = data.replace(b"\n", b"\r\n")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as fh:
        fh.write(data)


class Edit(object):
    """One file's worth of staged changes."""

    def __init__(self, rel):
        self.rel = rel
        self.text, self.crlf, self.bom = load(rel)

    def sub(self, anchor, replacement, count=1):
        found = self.text.count(anchor)
        if found != count:
            raise SystemExit(
                "ANCHOR FAIL in %s: expected %d, found %d for:\n---\n%s\n---"
                % (self.rel, count, found, anchor[:400]))
        self.text = self.text.replace(anchor, replacement, count)
        return self

    def after(self, anchor, addition):
        return self.sub(anchor, anchor + addition)

    def before(self, anchor, addition):
        return self.sub(anchor, addition + anchor)


# ---- 1. DenTunnelProfile.cs --------------------------------------------

def edit_profile_cs():
    e = Edit("Assets/Scripts/Floors/DenTunnelProfile.cs")

    e.sub(
        "    [Min(0)] public int chamberSeatClearance = 20;\n}",
        r"""    [Min(0)] public int chamberSeatClearance = 20;

    [Header("Exploratory dig (canon 42, stage 2)")]
    [Tooltip("Rock the diggings may cut in TOTAL, over the whole run. 0 "
           + "disables the dig, which is what an Occupier authors -- goblins "
           + "never dig, and a blank here would be indistinguishable from a "
           + "floor whose cap nobody filled in.\n\n"
           + "THIS IS THE CONTENT KNOB; THE BUDGET BELOW IS ONLY THE PACING "
           + "KNOB, and that split is measured rather than asserted. At this "
           + "cap, moving the budget from x2 to x3 changes the share of "
           + "dungeons that lose a remains from 14.7 to 14.0 per cent -- "
           + "nothing -- while the first find moves from day 75 to day 64 and "
           + "the digging ends on day 129 against day 104.\n\n"
           + "IT NEEDS A CAP BECAUSE NOTHING ELSE BOUNDS IT. Claimed ground is "
           + "refused and a leg turns at endpointClamp, but both bound WHERE "
           + "and neither bounds HOW MUCH: making claimed ground a hard wall "
           + "changed the total by under half a per cent, because a typical "
           + "dungeon's claim is under three per cent of the diggable disc -- "
           + "an island in a lake. Uncapped, a typical den cuts 4,725 cells by "
           + "day 200 at x2 and 7,028 at x3, against a GENERATED network of "
           + "1107 and entry 19's warning at 3000.\n\n"
           + "2400 is about twice the generated network, so the den at most "
           + "TRIPLES its own diggings. Lower was measured and rejected: a cap "
           + "of 1107 holds the contested-discovery beat to 7.3-8.0 per cent "
           + "of dungeons, and a set-piece firing on under one run in twelve "
           + "is content nobody meets. Section J of Tools/sim_den_digger.py "
           + "prints the whole sweep.")]
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
           + "into, in cells.")]
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
}""")

    e.sub(
        "            cavityTier1Cells = 268,\n        },",
        r"""            cavityTier1Cells = 268,
            // Goblins never dig, so the cap is zero and the dig never runs.
            // Written explicitly rather than inherited: a blank cap and an
            // authored zero are indistinguishable in the inspector.
            exploratoryCellCap = 0,
        },""")

    e.sub(
        "            cavityTier1Cells = 150,\n        },",
        r"""            cavityTier1Cells = 150,
            // The dig. 2400 cells of rock is about twice the generated
            // network, so the den at most triples its own diggings; at x3 the
            // first find lands about day 64 and the digging stops about day
            // 105. See the field tooltips for what each number was measured
            // against and what a lower cap costs.
            exploratoryCellCap = 2400,
            exploratoryBudget = 3f,
            exploratoryWidth = 2,
            exploratorySenseRadius = 15,
            exploratoryDriftDegrees = 12f,
        },""")

    e.sub(
        "    public GameObject HoardPrefab => hoardPrefab;",
        r"""    public GameObject HoardPrefab => hoardPrefab;

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

    public GameObject RemainsMarkerPrefab => remainsMarkerPrefab;""")
    return e


# ---- 2. FloorFeatureSaveData.cs ----------------------------------------

def edit_feature_save():
    e = Edit("Assets/Scripts/Floors/FloorFeatureSaveData.cs")

    e.sub(
        "    public List<SerializableVector3Int> polyline = new List<SerializableVector3Int>();\n}",
        r"""    public List<SerializableVector3Int> polyline = new List<SerializableVector3Int>();

    /// <summary>True on a LEG the diggers cut at runtime, as against a run the
    /// generator laid down. Appended, so a legacy save reads false and carries
    /// no legs -- which is exactly true of any save written before the dig.
    ///
    /// LEGS ARE APPENDED TO THE END OF THE TUNNEL LIST AND ONLY THE LAST ONE
    /// EVER GROWS, and that is load-bearing rather than tidy. Segment ids come
    /// from ONE counter running across every tunnel in list order
    /// (RebuildDenTunnelCells), so lengthening any but the last run past a
    /// segmentLength boundary renumbers every later run's segments -- and
    /// revealedDenTunnelSegmentIds is PERSISTED, so a reload would then unfog
    /// the wrong stretches. Appending at the end can only ever add ids above
    /// the ones already saved.</summary>
    public bool exploratory;
}""")

    e.sub(
        "    public DenCavityData denCavity;\n}",
        r"""    public DenCavityData denCavity;

    // Buried-remains cells the diggers opened before the player found them
    // (canon 42's contested discovery). Persisted because the empty hole and
    // its marker have to survive a reload; the GRANT is recovered by clearing
    // the den rather than from here. Appended: legacy saves load it empty.
    public List<SerializableVector3Int> denTakenRemainsCells = new();
}""")
    return e


# ---- 3. TerrainFeatureGenerator.cs -- the dig ---------------------------

DIG_CS = r"""
    // -- The exploratory dig (canon 42, stage 2) ---------------------------

    /// <summary>Next reveal-segment id this floor will hand out.
    ///
    /// A FIELD RATHER THAN A MAXIMUM OVER denTunnelSegments, and the difference
    /// is not cosmetic: RebuildDenTunnelCells advances the id for every index
    /// block whether or not the block kept a single cell, so a stretch entirely
    /// swallowed by a chamber leaves a gap. The highest LIVE id can therefore
    /// sit below the true next one, and a leg numbering itself off the live
    /// list would collide with a stretch the next reload is going to
    /// recreate.</summary>
    private int nextDenSegmentId;

    /// <summary>Markers already standing, so a sweep is free to re-run.</summary>
    private readonly Dictionary<Vector3Int, GameObject> denRemainsMarkers
        = new Dictionary<Vector3Int, GameObject>();

    /// <summary>What one dawn of digging did. Returned rather than logged
    /// because the ledger has to bank the rock, speak for a remains and place
    /// the work site off a single call.</summary>
    public class DenDigStep
    {
        /// <summary>Novel cells of rock opened. COUNTED, never predicted: the
        /// brush overlaps itself as the leg advances, so the cells a step
        /// yields depend on the turn it just took.</summary>
        public int cellsCut;

        /// <summary>The leg could not find a legal cell in any direction. A
        /// den boxed in by the player's own claim is the race working, so this
        /// is reported rather than treated as a fault.</summary>
        public bool boxedIn;

        public Vector3Int head;
        public bool headKnown;
        public float headingRadians;
        public int finds;

        /// <summary>Remains cells broken into this dawn. Empty on almost every
        /// dawn -- this is the set-piece, not the routine.</summary>
        public readonly List<Vector3Int> remainsTaken = new List<Vector3Int>();
    }

    /// <summary>Cells of rock the legs have opened so far. Derived from the
    /// legs themselves rather than counted into a field, so the cap can never
    /// drift from the geometry it is capping -- canon 42's own coupling
    /// argument, applied to the tunnel.</summary>
    public int DenExploratoryCellCount
    {
        get
        {
            if (featureData == null || featureData.denTunnels == null) return 0;
            int n = 0;
            foreach (var seg in denTunnelSegments)
            {
                var t = TunnelById(seg.tunnelId);
                if (t != null && t.exploratory) n += seg.cells.Count;
            }
            return n;
        }
    }

    private DenTunnelData TunnelById(int id)
    {
        if (featureData == null || featureData.denTunnels == null) return null;
        foreach (var t in featureData.denTunnels) if (t.id == id) return t;
        return null;
    }

    /// <summary>The leg currently being cut, or null before the first one.
    /// ALWAYS THE LAST ELEMENT -- see DenTunnelData.exploratory for why that is
    /// a rule rather than a convenience.</summary>
    private DenTunnelData GrowingLeg()
    {
        if (featureData == null || featureData.denTunnels == null) return null;
        int last = featureData.denTunnels.Count - 1;
        if (last < 0) return null;
        var t = featureData.denTunnels[last];
        return t != null && t.exploratory ? t : null;
    }

    /// <summary>
    /// Advances the diggings by one dawn's worth of rock, and returns what
    /// happened. Null on a floor with no den, no profile or no cap.
    ///
    /// THE WALK IS PERSISTENT, NOT RANDOM. Each step turns the bearing by a
    /// bounded amount rather than re-rolling it, because a pure random walk's
    /// displacement grows as the square root of its length -- a thousand cells
    /// of digging would end thirty cells from the den and read as a scribble.
    /// BuildTunnel already wobbles a bearing for the same reason.
    ///
    /// THE BRUSH IS TESTED, NOT THE CENTRELINE, and that is what keeps the live
    /// tree and the reloaded tree identical. Cells are DERIVED from the
    /// polyline, and the rebuild on load has no idea which cells were claimed
    /// when they were cut -- worse, it runs in the save controller's pass 1,
    /// before tile influence is restored in pass 3, so it could not ask. If the
    /// leg only turned when its CENTRELINE met claimed ground, its 2-wide brush
    /// could still clip a cell of the player's frontier, and that cell would be
    /// re-carved and marked walkable on the next load -- the player's own
    /// claimed stone quietly opening itself. Testing the whole footprint means
    /// a leg's cells contain no claimed ground by construction, so the rebuild
    /// reproduces them whatever the influence manager happens to know.
    ///
    /// A LEG NEVER RETRACES ITSELF, for the same class of reason.
    /// DenTunnelBuilder.Centreline de-duplicates, so a revisited cell would be
    /// dropped from the line and shift every later index -- and the index is
    /// what decides which reveal segment a cell belongs to. Refusing the step
    /// keeps polyline and centreline the same length, so runtime and reload
    /// partition segments identically.
    /// </summary>
    public DenDigStep AdvanceDenDig(int rockBudget, float headingRadians,
                                    bool hasHeading, int senseRadiusOverride = -1)
    {
        var entry = DenProfileEntry;
        if (entry == null || entry.exploratoryCellCap <= 0) return null;
        if (rockBudget <= 0) return null;
        if (featureData == null || featureData.denTunnels == null
            || featureData.denTunnels.Count == 0) return null;
        if (floor == null || floor.Terrain == null) return null;

        var step = new DenDigStep { headingRadians = headingRadians };

        var leg = GrowingLeg();
        if (leg == null)
        {
            leg = StartExploratoryLeg(entry, ref headingRadians);
            if (leg == null) return null;
            hasHeading = true;
        }
        if (!hasHeading) headingRadians = HeadingOfLeg(leg);

        var line = DenTunnelBuilder.Centreline(leg);
        if (line.Count == 0) return null;
        var onLeg = new HashSet<Vector3Int>(line);

        Vector3Int head = line[line.Count - 1];
        float x = head.x, y = head.y;

        Vector3Int centre = leg.floorCentre != null
            ? leg.floorCentre.ToVector3Int() : Vector3Int.zero;
        // Canon's own clamp, NOT the persisted rasterise clamp. clampRadius is
        // floorRadius minus the rim margin -- about 0.96 of the disc -- and
        // nothing about the dig was ever measured out there. endpointClamp is
        // the number entry 19's reach argument produced and the one the sim
        // turns at.
        float clampR = floor.Terrain.CurrentRadius * Mathf.Clamp01(entry.endpointClamp);
        float drift = entry.exploratoryDriftDegrees * Mathf.Deg2Rad;
        int width = Mathf.Max(2, entry.exploratoryWidth);
        int sense = senseRadiusOverride >= 0
            ? senseRadiusOverride : Mathf.Max(1, entry.exploratorySenseRadius);
        int segStep = Mathf.Max(4, leg.segmentLength);
        int segBase = nextDenSegmentId - SegmentsIn(line.Count, segStep);

        bool driving = false;
        Vector3Int driveTo = default(Vector3Int);
        int blocked = 0;
        int guard = rockBudget * 4 + 32;

        while (step.cellsCut < rockBudget && guard-- > 0)
        {
            if (driving)
            {
                headingRadians = Mathf.Atan2(driveTo.y - y, driveTo.x - x);
            }
            else
            {
                headingRadians += UnityEngine.Random.Range(-drift, drift);
            }

            float nx = x + Mathf.Cos(headingRadians);
            float ny = y + Mathf.Sin(headingRadians);
            var cell = new Vector3Int(Mathf.RoundToInt(nx), Mathf.RoundToInt(ny), 0);

            string findKey;
            if (!CanCutAt(cell, centre, clampR, width, onLeg, out findKey))
            {
                if (findKey != null && step.finds < int.MaxValue) step.finds++;
                // Turn hard rather than nudging: a small correction against a
                // wall walks along it, which reads as a tunnel tracing the
                // player's frontier instead of prospecting away from it.
                headingRadians += Mathf.PI * UnityEngine.Random.Range(0.5f, 1.5f);
                driving = false;
                if (++blocked > 64) { step.boxedIn = true; break; }
                continue;
            }
            blocked = 0;

            x = nx; y = ny;
            line.Add(cell);
            onLeg.Add(cell);
            leg.polyline.Add(SerializableVector3Int.From(cell));

            int segId = segBase + (line.Count - 1) / segStep;
            step.cellsCut += CarveLegCell(leg, segId, cell, centre, clampR, width);

            if (driving && cell == driveTo)
            {
                driving = false;
                if (TakeRemainsAt(cell))
                {
                    step.remainsTaken.Add(cell);
                    step.finds++;
                }
                headingRadians = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            }
            else if (!driving)
            {
                Vector3Int target;
                if (SenseRemains(cell, sense, out target)) { driving = true; driveTo = target; }
            }
        }

        // The id advances by INDEX BLOCK, never by surviving stretch, because
        // that is exactly what the rebuild on load does.
        nextDenSegmentId = Mathf.Max(nextDenSegmentId,
                                     segBase + SegmentsIn(line.Count, segStep));

        step.head = line[line.Count - 1];
        step.headKnown = true;
        step.headingRadians = headingRadians;
        return step;
    }

    private static int SegmentsIn(int lineCount, int segStep)
        => segStep <= 0 ? 0 : (lineCount + segStep - 1) / segStep;

    /// <summary>May the leg put its brush down here? Answers the FIND as well,
    /// because in this model breaking into something and being stopped by it
    /// are the same event -- ruling 4's "digs to it, then stops and picks a new
    /// bearing", with contact standing in for the breaking in.
    ///
    /// Only REMAINS are sensed at a distance (see SenseRemains). Everything
    /// else is met on contact, deliberately: a nearest-search over every road
    /// and site cell on the floor, once per walked cell, would cost more than
    /// the behaviour is worth, and remains are the one kind the dig is
    /// actually FOR. The consequence is recorded rather than hidden -- a leg
    /// ranges slightly further between turns than a range-sensing one would,
    /// so the sim mirrors this rule rather than the other way round.</summary>
    private bool CanCutAt(Vector3Int cell, Vector3Int centre, float clampR,
                          int width, HashSet<Vector3Int> onLeg, out string findKey)
    {
        findKey = null;
        if (onLeg.Contains(cell)) return false;      // never retrace: see AdvanceDenDig

        var influence = floor != null ? floor.TileInfluence : null;
        var types = floor != null ? floor.TerrainTypeMap : null;

        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half;
        for (int dx = -half; dx <= half + extra; dx++)
            for (int dy = -half; dy <= half + extra; dy++)
            {
                var p = new Vector3Int(cell.x + dx, cell.y + dy, 0);
                float ddx = p.x - centre.x, ddy = p.y - centre.y;
                if (ddx * ddx + ddy * ddy > clampR * clampR) return false;
                if (types != null && types.IsBedrock(p)) return false;

                // The player's ground is theirs. The cavity growth already
                // refuses a claimed cell for this reason and the tunnel must
                // agree, or the two halves of one den would disagree about
                // whose the rock is.
                if (influence != null && influence.IsTileClaimed(p))
                { findKey = "claim:" + (p.x >> 3) + ":" + (p.y >> 3); return false; }

                // The den's OWN unopened reserve. Not a find and not a
                // trespass: digging it would take cells GrowDenCavity is
                // waiting for, so the two verbs would eat each other.
                if (reservedCoreCells.Contains(p)) return false;

                var f = GetFeatureAt(p);
                if (f == FeatureType.Chamber)
                { findKey = "chamber:" + GetChamberId(p); return false; }
                if (f == FeatureType.Road)
                { findKey = "road:" + (p.x >> 3) + ":" + (p.y >> 3); return false; }
                if (f == FeatureType.AncientSite)
                { findKey = "site:" + (p.x >> 3) + ":" + (p.y >> 3); return false; }
            }
        return true;
    }

    /// <summary>Lays one centreline cell's brush, returning the NOVEL cells it
    /// opened. Everything already owned is left alone, which is
    /// RebuildDenTunnelCells' ownership rule applied a cell at a time.</summary>
    private int CarveLegCell(DenTunnelData leg, int segId, Vector3Int cell,
                             Vector3Int centre, float clampR, int width)
    {
        var seg = GetDenTunnelSegment(segId);
        if (seg == null)
        {
            seg = new DenTunnelSegmentRuntime { segmentId = segId, tunnelId = leg.id };
            denTunnelSegments.Add(seg);

            // A NEW STRETCH INHERITS THE REVEAL OF THE ONE BEFORE IT, so a dig
            // the player has walked up to goes on being visible as it advances
            // -- canon 42's "progress VISIBLE between visits", literally. It
            // can only ever show tunnel the diggers have actually cut, and it
            // never reaches past ground the player has already been shown.
            if (featureData.revealedDenTunnelSegmentIds.Contains(segId - 1)
                && !featureData.revealedDenTunnelSegmentIds.Contains(segId))
                featureData.revealedDenTunnelSegmentIds.Add(segId);
        }

        bool revealed = featureData.revealedDenTunnelSegmentIds.Contains(segId);
        var opened = new List<Vector3Int>();

        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half;
        for (int dx = -half; dx <= half + extra; dx++)
            for (int dy = -half; dy <= half + extra; dy++)
            {
                var p = new Vector3Int(cell.x + dx, cell.y + dy, 0);
                float ddx = p.x - centre.x, ddy = p.y - centre.y;
                if (ddx * ddx + ddy * ddy > clampR * clampR) continue;
                if (cellLookup.ContainsKey(p)) continue;
                if (!denTunnelCells.Add(p)) continue;
                seg.cells.Add(p);
                cellLookup[p] = new FeatureRef { type = FeatureType.DenTunnel, featureId = segId };
                opened.Add(p);
            }

        // Unrevealed ground is left UNMARKED as well as unlit. Fog is one-way
        // and walkable-but-invisible is the reserve's own banned state.
        if (revealed && opened.Count > 0) RevealGrownCells(opened);
        return opened.Count;
    }

    /// <summary>Starts the first leg, at the tip of a generated run. Prefers a
    /// DEAD END, which canon already names as "exactly what the population
    /// extends"; falls back to the longest run so a den whose every run found a
    /// chamber still digs. Deterministic tiebreak on id, so two calls on one
    /// floor can never pick differently.</summary>
    private DenTunnelData StartExploratoryLeg(DenTunnelFloorEntry entry, ref float heading)
    {
        DenTunnelData best = null;
        int bestScore = int.MinValue;
        foreach (var t in featureData.denTunnels)
        {
            if (t == null || t.exploratory || t.polyline == null || t.polyline.Count < 2) continue;
            var lineT = DenTunnelBuilder.Centreline(t);
            int score = lineT.Count + (t.chamberId < 0 ? 100000 : 0);
            if (score > bestScore) { bestScore = score; best = t; }
        }
        if (best == null) return null;

        var parent = DenTunnelBuilder.Centreline(best);
        var mouth = parent[parent.Count - 1];
        heading = parent.Count >= 2
            ? Mathf.Atan2(mouth.y - parent[parent.Count - 2].y,
                          mouth.x - parent[parent.Count - 2].x)
            : UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        return AppendLeg(entry, mouth, best);
    }

    /// <summary>Appends a fresh leg at the END of the tunnel list. Every leg
    /// after the first starts where the last one stopped, because a digger that
    /// has just broken into something carries on from where it stands --
    /// sending it home to start again would read as the tunnel being
    /// deleted.</summary>
    public void StartNextExploratoryLeg(float heading)
    {
        var entry = DenProfileEntry;
        if (entry == null || entry.exploratoryCellCap <= 0) return;
        var leg = GrowingLeg();
        if (leg == null) return;
        var line = DenTunnelBuilder.Centreline(leg);
        if (line.Count == 0) return;
        AppendLeg(entry, line[line.Count - 1], leg);
    }

    private DenTunnelData AppendLeg(DenTunnelFloorEntry entry, Vector3Int mouth,
                                    DenTunnelData parent)
    {
        int id = 0;
        foreach (var t in featureData.denTunnels) if (t.id >= id) id = t.id + 1;

        var leg = new DenTunnelData
        {
            id = id,
            chamberId = -1,
            width = Mathf.Max(2, entry.exploratoryWidth),
            tipWidth = Mathf.Max(2, entry.exploratoryWidth),
            segmentLength = entry.segmentLength,
            floorCentre = parent != null ? parent.floorCentre : null,
            clampRadius = parent != null ? parent.clampRadius : 0,
            exploratory = true,
        };
        leg.polyline.Add(SerializableVector3Int.From(mouth));
        featureData.denTunnels.Add(leg);

        // The leg picks up the reveal of whatever stretch its mouth stands in,
        // so a dig starting at ground the player has already walked is visible
        // from its first cell rather than from its second stretch.
        FeatureRef fref;
        if (cellLookup.TryGetValue(mouth, out fref) && fref.type == FeatureType.DenTunnel
            && featureData.revealedDenTunnelSegmentIds.Contains(fref.featureId)
            && !featureData.revealedDenTunnelSegmentIds.Contains(nextDenSegmentId))
            featureData.revealedDenTunnelSegmentIds.Add(nextDenSegmentId);

        return leg;
    }

    private float HeadingOfLeg(DenTunnelData leg)
    {
        var line = DenTunnelBuilder.Centreline(leg);
        if (line.Count < 2) return UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        var a = line[line.Count - 2];
        var b = line[line.Count - 1];
        return Mathf.Atan2(b.y - a.y, b.x - a.x);
    }

    private bool SenseRemains(Vector3Int from, int radius, out Vector3Int target)
    {
        target = default(Vector3Int);
        var brc = BuriedRemainsController.Instance;
        if (brc == null || floor == null) return false;
        var cells = brc.UntakenRemainsOn(floor);
        if (cells == null || cells.Count == 0) return false;

        long best = (long)radius * radius + 1;
        bool found = false;
        for (int i = 0; i < cells.Count; i++)
        {
            long dx = cells[i].x - from.x, dy = cells[i].y - from.y;
            long d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; target = cells[i]; found = true; }
        }
        return found;
    }

    private bool TakeRemainsAt(Vector3Int cell)
    {
        var brc = BuriedRemainsController.Instance;
        if (brc == null || floor == null) return false;
        if (!brc.NotifyTakenExternally(floor, cell)) return false;

        if (featureData.denTakenRemainsCells == null)
            featureData.denTakenRemainsCells = new List<SerializableVector3Int>();
        featureData.denTakenRemainsCells.Add(SerializableVector3Int.From(cell));
        SpawnDenRemainsMarkers();
        return true;
    }

    /// <summary>Stands a marker in every taken remains the player can actually
    /// see, and is free to re-run. Idempotent by cell rather than by a flag,
    /// because the same sweep serves excavation, reveal and the load path.
    ///
    /// GATED ON THE CELL BEING REVEALED, not on the den being anything: the
    /// hole is the whole of what makes the loss legible, and a prop standing in
    /// fog would be a legibility rule talking to nobody.</summary>
    public void SpawnDenRemainsMarkers()
    {
        if (featureData == null || featureData.denTakenRemainsCells == null) return;
        if (denTunnelProfile == null || floor == null) return;
        var prefab = denTunnelProfile.RemainsMarkerPrefab;
        if (prefab == null) return;                 // null-safe: the beat still plays
        var terrain = floor.Terrain;
        if (terrain == null || terrain.FloorTilemap == null) return;

        foreach (var sv in featureData.denTakenRemainsCells)
        {
            var cell = sv.ToVector3Int();
            if (denRemainsMarkers.ContainsKey(cell)) continue;
            if (!floor.IsRevealed(cell)) continue;
            var go = Instantiate(prefab, terrain.FloorTilemap.GetCellCenterWorld(cell),
                                 Quaternion.identity, floor.transform);
            go.name = "DenRemainsMarker_" + cell.x + "_" + cell.y;
            denRemainsMarkers[cell] = go;
        }
    }

    /// <summary>Reveal stretches this floor holds, generated and dug alike.
    /// Printed beside the revealed count, because "3 of 3 revealed" and "3 of
    /// 14 revealed" are the difference between a dig nobody has found and a dig
    /// that is not happening.</summary>
    public int DenTunnelSegmentCount => denTunnelSegments.Count;

    /// <summary>False when the profile carries no marker prefab, so a robbed
    /// remains leaves no visible hole. Surfaced rather than inferred: canon 42
    /// makes the hole the whole of what tells the player they were robbed, and
    /// an unassigned prop is the ambiguous default in its usual form -- it
    /// looks exactly like a den that has taken nothing.</summary>
    public bool DenRemainsMarkerPrefabAssigned
        => denTunnelProfile != null && denTunnelProfile.RemainsMarkerPrefab != null;

    /// <summary>How many remains the diggers have opened on this floor. Read by
    /// the report and by the marker sweep's own diagnostics.</summary>
    public int DenTakenRemainsCount
        => featureData != null && featureData.denTakenRemainsCells != null
            ? featureData.denTakenRemainsCells.Count : 0;
"""


def edit_generator():
    e = Edit("Assets/Scripts/Floors/TerrainFeatureGenerator.cs")

    # The id counter, set where the rebuild finishes assigning them.
    e.sub(
        "                if (seg.cells.Count > 0) denTunnelSegments.Add(seg);\n            }\n        }\n    }",
        "                if (seg.cells.Count > 0) denTunnelSegments.Add(seg);\n"
        "            }\n        }\n\n"
        "        // The runtime dig hands out ids from here on. Set from the same\n"
        "        // counter that just partitioned the saved runs, so a leg cut this\n"
        "        // session numbers itself exactly as the next reload will.\n"
        "        nextDenSegmentId = nextSegmentId;\n    }")

    # The dig itself, next to the tunnel machinery it extends.
    e.before("    public int DenTunnelCount => featureData?.denTunnels?.Count ?? 0;",
             DIG_CS.lstrip("\n") + "\n")

    # Markers follow reveal, on both doors into it.
    e.sub(
        "        featureData.revealedDenTunnelSegmentIds.Add(segmentId);\n"
        "        RevealVersion++;\n"
        "        UnfogDenTunnelSegment(segmentId);",
        "        featureData.revealedDenTunnelSegmentIds.Add(segmentId);\n"
        "        RevealVersion++;\n"
        "        UnfogDenTunnelSegment(segmentId);\n"
        "        SpawnDenRemainsMarkers();")

    e.sub("        UnfogDenCavity();\n        SpawnDenHoard();",
          "        UnfogDenCavity();\n        SpawnDenHoard();\n        SpawnDenRemainsMarkers();")

    # Clearing takes the pile down; the empty holes STAY. The hoard was a claim
    # on gold already paid, but a robbed remains really did happen.
    e.sub(
        "    public void DespawnDenHoard()\n    {\n        if (denHoard == null) return;\n"
        "        Destroy(denHoard.gameObject);\n        denHoard = null;\n    }",
        "    public void DespawnDenHoard()\n    {\n        if (denHoard == null) return;\n"
        "        Destroy(denHoard.gameObject);\n        denHoard = null;\n    }\n\n"
        "    /// <summary>The markers are deliberately NOT taken down with the pile.\n"
        "    /// A hoard left standing after ClearDen would be claiming gold already\n"
        "    /// in the player's purse; an emptied remains is a thing that actually\n"
        "    /// happened, and the hole is the record of it. Clearing recovers the\n"
        "    /// GRANT, not the stone.</summary>\n"
        "    public int DenRemainsMarkerCount => denRemainsMarkers.Count;")
    return e


# ---- 4. BuriedRemainsController.cs -------------------------------------

def edit_buried():
    e = Edit("Assets/Scripts/Gameplay/BuriedRemainsController.cs")

    e.before(
        "    // -- Save / restore surface ------------------------------------",
        r"""    // -- The diggers' door in (canon 42) ---------------------------

    /// <summary>How many buried-remains cells this floor really holds.
    ///
    /// EXISTS BECAUSE THE DEN LEDGER WAS GUESSING. sitesPerFloor is private and
    /// had no accessor, so NotifyRemainsExcavated's cap was a hardcoded 2 --
    /// wrong on any floor carrying an Ossuary, since AppendOssuaryRemains adds
    /// one guaranteed cell per placed one ON TOP of the sampled sites. A den
    /// that could mint no discoveries and a den that could mint one extra look
    /// identical from the ledger.</summary>
    public int SiteCountFor(FloorRoot floor)
        => floor == null || floor.TerrainTypeMap == null ? 0 : SitesFor(floor).Count;

    /// <summary>Remains on this floor that nobody has opened yet -- the
    /// diggers' target list. A fresh list each call: the caller walks it per
    /// cell and must not be handed the live consumed set to iterate.</summary>
    public List<Vector3Int> UntakenRemainsOn(FloorRoot floor)
    {
        var open = new List<Vector3Int>();
        if (floor == null || floor.TerrainTypeMap == null) return open;
        var used = ConsumedFor(floor.FloorIndex);
        foreach (var cell in SitesFor(floor))
            if (!used.Contains(cell)) open.Add(cell);
        return open;
    }

    /// <summary>
    /// Something other than the player has opened this remains. Returns false
    /// if it was not a site, or was already taken.
    ///
    /// MARKS IT SENSED AS WELL AS CONSUMED, and the second half is the whole
    /// point rather than belt and braces. HandleClaimed murmurs "something
    /// waits in the stone nearby -- dig, and I will remember" for any site in
    /// its halo that is neither consumed nor sensed. A kobold-opened cell is
    /// made walkable by MarkNaturalFloor, which fires no OnTileMined, so
    /// without this it would stay unconsumed for ever -- and the wisp would
    /// invite the player to dig ground MineTile silently refuses, because that
    /// method early-returns on a cell already in minedTiles. An invitation the
    /// game then declines is worse than saying nothing.
    ///
    /// The player cannot be paid twice for the same stone and needs no flag for
    /// it: the same early return enforces it by geometry.
    /// </summary>
    public bool NotifyTakenExternally(FloorRoot floor, Vector3Int cell)
    {
        if (floor == null || floor.TerrainTypeMap == null) return false;
        if (!SitesFor(floor).Contains(cell)) return false;
        if (!ConsumedFor(floor.FloorIndex).Add(cell)) return false;
        SensedFor(floor.FloorIndex).Add(cell);
        return true;
    }

""")
    return e


# ---- 5. Save wiring for consumed / sensed ------------------------------

def edit_save_data():
    e = Edit("Assets/Scripts/Save/DungeonSaveData.cs")
    e.sub(
        "    public string floorName;   // player-set floor name (additive; null on old saves)",
        "    public string floorName;   // player-set floor name (additive; null on old saves)\n\n"
        "    // Buried remains already opened, and already felt through the claim halo\n"
        "    // (canon 17). NOT PERSISTED UNTIL NOW, and the gap only became visible\n"
        "    // when the kobolds acquired a way to open one: a mined cell is not\n"
        "    // re-minable, so losing consumed state was nearly harmless while the\n"
        "    // player was the only one digging. It is not harmless once something\n"
        "    // else can take a remains, because the murmur would come back after a\n"
        "    // reload and point at stone that is already open. Additive: both load\n"
        "    // empty on an old save, which is the pre-dig behaviour exactly.\n"
        "    public List<SerializableVector3Int> buriedConsumed = new();\n"
        "    public List<SerializableVector3Int> buriedSensed = new();")
    return e


def edit_save_controller():
    e = Edit("Assets/Scripts/Save/DungeonSaveController.cs")

    e.sub(
        "            floorName = FloorManager.Instance.GetFloorName(floor.FloorIndex),\n        };",
        "            floorName = FloorManager.Instance.GetFloorName(floor.FloorIndex),\n        };\n\n"
        "        // Buried-remains state, which four methods on the controller have\n"
        "        // been waiting to be called by since they shipped. They were dead\n"
        "        // code, and canon 42 required stage 2 to rule on them before the\n"
        "        // diggers used the same store.\n"
        "        if (BuriedRemainsController.Instance != null)\n"
        "        {\n"
        "            BuriedRemainsController.Instance.GatherConsumed(floor, data.buriedConsumed);\n"
        "            BuriedRemainsController.Instance.GatherSensed(floor, data.buriedSensed);\n"
        "        }")

    e.sub(
        "                if (floor?.FeatureGenerator != null)\n"
        "                    floor.FeatureGenerator.LoadFromSave(floorData.featureData);",
        "                if (floor?.FeatureGenerator != null)\n"
        "                    floor.FeatureGenerator.LoadFromSave(floorData.featureData);\n\n"
        "                // Consumed and sensed remains, restored beside the feature data\n"
        "                // they refer to. Safe this early: the controller keys both on\n"
        "                // floor INDEX and needs nothing from the floor but that.\n"
        "                if (floor != null && BuriedRemainsController.Instance != null)\n"
        "                {\n"
        "                    BuriedRemainsController.Instance.RestoreConsumed(floor, floorData.buriedConsumed);\n"
        "                    BuriedRemainsController.Instance.RestoreSensed(floor, floorData.buriedSensed);\n"
        "                }")
    return e


# ---- 6. DenController.cs -----------------------------------------------

def edit_den_controller():
    e = Edit("Assets/Scripts/DungeonCore/DenController.cs")

    e.sub(
        "    public int deathsNotByDungeon;\n}",
        r"""    public int deathsNotByDungeon;

    // ---- the exploratory dig (canon 42, stage 2) --------------------------

    /// <summary>Fractional rock carried between dawns, for the TUNNEL. Separate
    /// from digCarry above because the two budgets are separate: the cavity's
    /// is what the ledger pays on and the tunnel's pays nothing, and pooling
    /// them would be the shared-budget model that was measured and rejected.</summary>
    public float tunnelCarry;

    /// <summary>The leg's bearing, in DEGREES. Degrees rather than radians only
    /// so a ledger dump is readable; the dig converts on both sides.</summary>
    public float digHeadingDegrees;
    public bool digHeadingKnown;

    /// <summary>The diggings have finished -- the cap is spent, or every
    /// remains on the floor is already theirs.</summary>
    public bool digStopped;
    public bool spokenDigDone;

    /// <summary>Everything the legs have broken into. Diagnostics only, and it
    /// exists because a dig that has found nothing and a dig that is not
    /// running look identical in every other column.</summary>
    public int digFinds;
}""")

    e.sub(
        "            if ((DenKind)den.kind == DenKind.Excavator)\n                EarnByDigging(den, tier);",
        "            if ((DenKind)den.kind == DenKind.Excavator)\n"
        "            {\n"
        "                EarnByDigging(den, tier);\n"
        "                // The tunnel AFTER the hole, and paying nothing for itself.\n"
        "                // Canon 42's ruling 5: reserve cells pay, tunnel cells do\n"
        "                // not, and that is what keeps \"tier 5 IS the completed\n"
        "                // hole\" true against a dig that runs for another fifty days.\n"
        "                TickExploratoryDig(den, tier);\n"
        "            }")

    e.before(
        "    private static readonly int[] DiggersByTier = { 1, 1, 2, 3, 4 };",
        r"""    /// <summary>
    /// One dawn of the exploratory dig (canon 42, stage 2).
    ///
    /// ADDITIVE, NOT A SHARE, and that was measured rather than preferred. The
    /// ledger pays on RESERVE cells alone, so diverting the cavity budget into
    /// a tunnel freezes the hoard, freezes the tier and thereby slows the very
    /// dig it was diverted to -- share 1.00 arrives LATER than share 0.50.
    ///
    /// TWO WAYS IT ENDS, and the first is the point of the whole thing: every
    /// remains on the floor is theirs, or the cap is spent. It needs the cap
    /// because nothing else bounds it -- see exploratoryCellCap's own tooltip
    /// for what claimed ground and the endpoint clamp do and do not do.
    /// </summary>
    private void TickExploratoryDig(DenSaveEntry den, int tier)
    {
        var floor = FindFloor(den.floorIndex);
        var features = floor != null ? floor.FeatureGenerator : null;
        if (features == null) return;

        var entry = features.DenProfileEntry;
        if (entry == null || entry.exploratoryCellCap <= 0) return;

        if (den.digStopped) { ClearWorkSites(den.floorIndex); return; }

        var brc = BuriedRemainsController.Instance;
        int onFloor = brc != null ? brc.SiteCountFor(floor) : 0;
        if (onFloor > 0 && den.remainsTaken >= onFloor) { StopDig(den); return; }

        int cut = features.DenExploratoryCellCount;
        if (cut >= entry.exploratoryCellCap) { StopDig(den); return; }

        float wanted = DigCellsPerDay[tier - 1] * ExpansionMultiplier(den.floorIndex)
                     * Mathf.Max(0f, entry.exploratoryBudget) + den.tunnelCarry;
        int rock = Mathf.FloorToInt(wanted);
        den.tunnelCarry = wanted - rock;
        rock = Mathf.Min(rock, entry.exploratoryCellCap - cut);
        if (rock <= 0) return;

        var step = features.AdvanceDenDig(
            rock, den.digHeadingDegrees * Mathf.Deg2Rad, den.digHeadingKnown);
        if (step == null) return;

        den.digHeadingDegrees = step.headingRadians * Mathf.Rad2Deg;
        den.digHeadingKnown = true;
        den.digFinds += step.finds;

        for (int i = 0; i < step.remainsTaken.Count; i++)
        {
            // The cap is the floor's REAL count now. It used to be a hardcoded
            // 2, which is wrong on any floor with an Ossuary -- SitesFor
            // appends one guaranteed cell per placed one on top of
            // sitesPerFloor.
            if (!NotifyRemainsExcavated(den.floorIndex, Mathf.Max(1, onFloor))) continue;
            AnnounceRemainsTaken(floor, step.remainsTaken[i]);
        }

        // A leg that has arrived somewhere starts a fresh one FROM WHERE IT
        // STANDS. Sending it back to the den to begin again would read as the
        // tunnel having been deleted.
        if (step.remainsTaken.Count > 0 || step.boxedIn)
            features.StartNextExploratoryLeg(den.digHeadingDegrees * Mathf.Deg2Rad);

        if (step.headKnown) AssignWorkSites(den.floorIndex, floor, step.head);
    }

    private void StopDig(DenSaveEntry den)
    {
        den.digStopped = true;
        ClearWorkSites(den.floorIndex);
        if (den.spokenDigDone) return;
        den.spokenDigDone = true;
        WispCompanion.Instance?.Speak("den_tunnel_done");
    }

    /// <summary>
    /// The contested discovery, said out loud.
    ///
    /// SPOKEN AT EXCAVATION RATHER THAN WHEN THE HOLE IS SEEN, and the
    /// alternative was tried on paper first: firing when the marker prop
    /// spawns ties the telling to the seeing, but it also makes the beat depend
    /// on art that is not authored yet, and a set-piece that waits for a sprite
    /// is a set-piece that does not exist. The PROP is the lasting record; this
    /// is the event. The alert pins the cell itself, so a player who wants to
    /// go and look at what they lost can click straight to it -- the camera
    /// roams the whole floor by Appendix C, so pointing at fog leaks nothing.
    /// </summary>
    private void AnnounceRemainsTaken(FloorRoot floor, Vector3Int cell)
    {
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return;
        Vector3 where = influence.CellToWorld(cell);
        WispCompanion.Instance?.Speak("den_remains_taken");
        AlertsLog.Instance?.AddAlert(
            "Old stone below has been opened by someone else.",
            where, floor.FloorIndex, AlertCategory.Discovery);
    }

    /// <summary>Sends the den's diggers to the face and calls everyone else
    /// home. THE ROLE IS READ OFF POSITION IN THE POPULATION LIST, exactly as
    /// MayForage reads the forager role, so a death re-assigns it for free and
    /// nothing can drift out of step with the roll.
    ///
    /// A work site is an OVERRIDE on the cavity leash and never a wider leash:
    /// the leash is membership of the cavity's own cell set and was made so for
    /// a measured reason -- the yo-yo at radius six -- so letting diggers out
    /// by widening it again would reopen a fault already paid for once.</summary>
    private void AssignWorkSites(int floorIndex, FloorRoot floor, Vector3Int face)
    {
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return;
        Vector3 world = influence.CellToWorld(face);
        int diggers = DiggerBudget(floorIndex);
        var live = LiveOn(floorIndex);
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i] == null) continue;
            if (i < diggers) live[i].SetDenWorkSite(world);
            else live[i].ClearDenWorkSite();
        }
    }

    private void ClearWorkSites(int floorIndex)
    {
        var live = LiveOn(floorIndex);
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null) live[i].ClearDenWorkSite();
    }

""")

    e.sub(
        "        den.heldSpoilRarities.Clear();",
        r"""        den.heldSpoilRarities.Clear();

        // THE CONTESTED DISCOVERY, RECOVERED. Canon 42 has named
        // GrantExternalDiscovery as this beat's re-entry point since the
        // decision record, and that method's own doc comment has named the
        // desecration arc as its ONLY caller since it shipped -- this is the
        // second, and the one canon was waiting for. What the diggers took
        // before the player found it, killing them gives back.
        //
        // The COUNT is kept rather than cleared: remainsTaken is the record of
        // what happened on that floor, and ClearDen cannot run twice on one den
        // anyway, so there is nothing for a reset to protect against.
        if (den.remainsTaken > 0)
        {
            for (int i = 0; i < den.remainsTaken; i++)
                BuriedRemainsController.Instance?.GrantExternalDiscovery(where, floorIndex);
            WispCompanion.Instance?.Speak("den_remains_returned");
        }""")
    return e


# ---- 7. WispScript.cs --------------------------------------------------

def edit_wisp():
    e = Edit("Assets/Scripts/Wisp/WispScript.cs")
    e.before(
        '            new Line { id = "den_diggings_done", once = true,',
        r"""            // The dig (canon 42, stage 2). Only REMAINS speaks: a den finds
            // roughly seven things by day 150 and a line for each would be
            // spam, so a chamber or a stretch of the player's own frontier
            // being broken into is silent and consequential.
            new Line { id = "den_remains_taken", once = true,
                text = "They have opened old stone down there - and whatever was resting in it is theirs now, not yours. That is what waiting costs, and it is the only debt in this place that grows while you do nothing." },
            new Line { id = "den_remains_returned", once = true,
                text = "What they dug out of your stone has come back with the rest of it. Late, and by the only road that was ever open - through them." },
            new Line { id = "den_tunnel_done", once = true,
                text = "The digging has stopped for good. They went as far as they had rock and patience for, and what they found on the way they have already taken." },

""")
    return e


# ---- 8. The authored asset ---------------------------------------------

def edit_asset():
    e = Edit("Assets/ScriptableObjects/Floors/DenTunnelProfile.asset")

    # Canon 42: the authored numbers live in the ASSET, not only in the C#
    # defaults. Unity keeps a field initialiser only until the first time
    # anyone opens the asset, after which it bakes whatever the defaults were
    # at that instant and editing the C# does nothing at all.
    e.sub("    cavityTier1Cells: 268\n    chamberSeatClearance: 20",
          "    cavityTier1Cells: 268\n    chamberSeatClearance: 20\n"
          "    exploratoryCellCap: 0\n"
          "    exploratoryBudget: 3\n"
          "    exploratoryWidth: 2\n"
          "    exploratorySenseRadius: 15\n"
          "    exploratoryDriftDegrees: 12")

    e.sub("    cavityTier1Cells: 150\n    chamberSeatClearance: 20",
          "    cavityTier1Cells: 150\n    chamberSeatClearance: 20\n"
          "    exploratoryCellCap: 2400\n"
          "    exploratoryBudget: 3\n"
          "    exploratoryWidth: 2\n"
          "    exploratorySenseRadius: 15\n"
          "    exploratoryDriftDegrees: 12")

    e.sub("  hoardPrefab: {fileID: 7951733088947934359, guid: 5fbbea327160b144f8233aeed7b44789, type: 3}",
          "  hoardPrefab: {fileID: 7951733088947934359, guid: 5fbbea327160b144f8233aeed7b44789, type: 3}\n"
          "  remainsMarkerPrefab: {fileID: 0}")
    return e


# ---- 9. Commands.cs -- diagnostics --------------------------------------

def edit_commands():
    e = Edit("Assets/Scripts/TESTING/Commands.cs")

    e.sub(
        'sb.AppendLine("floor  kind        tribe    tier  hoard    next tier  earned   rem  raids  tgt%  dug    left   pop  out  dig  work  lost  state");',
        'sb.AppendLine("floor  kind        tribe    tier  hoard    next tier  earned   rem  raids  tgt%  dug    left   tunnel      find  pop  out  dig  work  lost  state");')

    e.sub(
        '            string tribe = denDef != null ? denDef.tribe.ToString() : "-";',
        '            string tribe = denDef != null ? denDef.tribe.ToString() : "-";\n\n'
        '            // The DIG, beside the hole, because they are two budgets on one\n'
        '            // den and the whole reason the tunnel is additive is that they\n'
        '            // must not be read as one. A trailing * means the diggings have\n'
        '            // stopped -- cap spent, or every remains on the floor taken --\n'
        '            // which is otherwise indistinguishable from a den digging slowly.\n'
        '            int digCap = denEntry != null ? denEntry.exploratoryCellCap : 0;\n'
        '            string tunnel = digCap <= 0 ? "-"\n'
        '                : (denFeatures.DenExploratoryCellCount + "/" + digCap\n'
        '                   + (den.digStopped ? "*" : ""));')

    e.sub('{den.cellsDug,-6} {left,-6} "',
          '{den.cellsDug,-6} {left,-6} {tunnel,-11} {den.digFinds,-5} "')

    e.before(
        '    [ContextMenu("Reset Cross-Tribe Counter")]',
        r'''    /// <summary>The diggings, leg by leg.
    ///
    /// BUILT BECAUSE A STALLED DIG AND A SLOW ONE LOOK IDENTICAL ON SCREEN, and
    /// this arc has already paid for that lesson twice -- once when an
    /// excavator capped at tier 3 in silence, and once when den tunnels shipped
    /// absent from every diagnostic surface and read as a generator that had
    /// done nothing. A leg that is boxed in by the player's own claim is the
    /// race WORKING; a leg that never started is a fault; the two are one line
    /// apart here and indistinguishable anywhere else.</summary>
    [ContextMenu("Print Den Dig")]
    void PrintDenDig()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Commands] Den diggings");

        if (FloorManager.Instance == null)
        {
            Debug.Log("[Commands] No FloorManager in the scene.");
            return;
        }

        bool any = false;
        foreach (var f in FloorManager.Instance.AllFloors)
        {
            var fg = f != null ? f.FeatureGenerator : null;
            if (fg == null) continue;
            var pe = fg.DenProfileEntry;
            if (pe == null || pe.exploratoryCellCap <= 0) continue;
            any = true;

            var data = fg.FeatureData;
            int legs = 0, generated = 0;
            if (data != null && data.denTunnels != null)
                foreach (var t in data.denTunnels)
                {
                    if (t == null) continue;
                    if (t.exploratory) legs++; else generated++;
                }

            sb.AppendLine($"  floor {f.FloorIndex}: {generated} generated runs, {legs} legs, "
                        + $"{fg.DenExploratoryCellCount}/{pe.exploratoryCellCap} cells cut "
                        + $"at section {pe.exploratoryWidth}, budget x{pe.exploratoryBudget:F1}");
            sb.AppendLine($"    reveal: {fg.RevealedDenTunnelSegmentCount} of "
                        + $"{fg.DenTunnelSegmentCount} stretches");

            var brc = BuriedRemainsController.Instance;
            int onFloor = brc != null ? brc.SiteCountFor(f) : -1;
            int untaken = brc != null ? brc.UntakenRemainsOn(f).Count : -1;
            sb.AppendLine($"    remains: {fg.DenTakenRemainsCount} taken of "
                        + $"{(onFloor < 0 ? "?" : onFloor.ToString())} on the floor, "
                        + $"{(untaken < 0 ? "?" : untaken.ToString())} still buried, "
                        + $"{fg.DenRemainsMarkerCount} markers standing");
            if (onFloor == 0)
                sb.AppendLine("    !! this floor has NO buried remains at all, so the "
                            + "contested-discovery beat cannot fire here. Expected on "
                            + "some seeds -- GetBuriedSites takes only Stone and Granite.");
            if (!fg.DenRemainsMarkerPrefabAssigned)
                sb.AppendLine("    !! no remains marker prefab assigned on the profile, so "
                            + "a robbed remains leaves no visible hole. The wisp still "
                            + "speaks; the lasting record does not exist.");

            var ledger = DenController.Instance;
            if (ledger != null)
                foreach (var den in ledger.AllDens)
                {
                    if (den.floorIndex != f.FloorIndex) continue;
                    sb.AppendLine($"    ledger: heading {den.digHeadingDegrees:F0} deg, "
                                + $"carry {den.tunnelCarry:F2}, finds {den.digFinds}, "
                                + $"{(den.digStopped ? "STOPPED" : "digging")}");
                }
        }

        if (!any)
            sb.AppendLine("  (no floor carries a dig -- only an Excavator with a non-zero "
                        + "exploratoryCellCap does)");
        Debug.Log(sb.ToString());
    }

''')
    return e


# ---- 10. The sims -------------------------------------------------------

def edit_sim_digger():
    e = Edit("Tools/sim_den_digger.py")

    e.sub(
        "CRAWLWAY_MOUTH, CRAWLWAY_TIP = 2, 1\nCRAWLWAY_MEAN_WIDTH = (CRAWLWAY_MOUTH + CRAWLWAY_TIP) / 2.0",
        r'''# CORRECTED FROM 2->1 TO A UNIFORM 2, AND EVERY REACH FIGURE ABOVE MOVED WITH
# IT. A 1-wide tip is NOT 4-CONNECTED and nothing could walk the far end of it:
# DenTunnelBuilder.Centreline is Bresenham and takes diagonal steps, and
# RoadNetworkBuilder.Dilate at width 1 emits the cell alone, so two consecutive
# diagonal cells share no edge. Canon already rests the generated network's
# breach guarantee on exactly this -- "a 2-wide tip stays 4-connected across a
# diagonal step" -- so 2 is a floor rather than a preference.
#
# Uniform is also what makes a GROWING leg safe: the shared rasteriser lerps the
# taper across the run's CURRENT length, so a tapered leg would rewiden its own
# older cells every time it grew.
#
# Cost: the same rock buys 25 per cent fewer centreline cells, so the reach
# figures printed here are the honest ones and anything quoted from the 1.5
# model was optimistic.
CRAWLWAY_MOUTH, CRAWLWAY_TIP = 2, 2
CRAWLWAY_MEAN_WIDTH = (CRAWLWAY_MOUTH + CRAWLWAY_TIP) / 2.0''')

    e.sub(
        '''    out = []
    for t in buried_sites(seed * 7919 + EXCAVATOR_FLOOR, radius):
        out.append(("remains", t[0], t[1], detect))
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    for c in centres:
        out.append(("chamber", c[0], c[1], detect + CHAMBER_EFFECTIVE_RADIUS))
    out.append(("claimed", 0.0, 0.0, detect + claimed_radius(claimed_cells)))
    return out''',
        r'''    ONLY REMAINS ARE SENSED AT RANGE, and this file now MIRRORS the shipped rule
    rather than leading it. TerrainFeatureGenerator.CanCutAt meets a chamber, a
    road, a site or the player's frontier ON CONTACT -- a nearest-search over
    every road and site cell on the floor, once per walked cell, would cost more
    than the behaviour is worth, and remains are the one kind the dig is
    actually FOR. The consequence is that a leg ranges slightly further between
    turns than a range-sensing one would.
    """
    out = []
    for t in buried_sites(seed * 7919 + EXCAVATOR_FLOOR, radius):
        out.append(("remains", t[0], t[1], detect))
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    for c in centres:
        out.append(("chamber", c[0], c[1], CHAMBER_EFFECTIVE_RADIUS))
    out.append(("claimed", 0.0, 0.0, claimed_radius(claimed_cells)))
    return out''')

    # Close the docstring the replacement above re-opened.
    e.sub('''    quietly omitted."""
    ONLY REMAINS ARE SENSED AT RANGE''',
          '''    quietly omitted.

    ONLY REMAINS ARE SENSED AT RANGE''')

    e.sub('    print("   2->1 crawlway, additive tunnel budget, stop-and-new-bearing.")',
          '    print("   uniform %d-wide crawlway, additive budget, stop-and-new-bearing."\n          % CRAWLWAY_MOUTH)')

    e.sub(
        '''    report_exploration(min(seeds, 600), radius)
    report_poi(min(seeds, 400), radius)''',
        '''    report_exploration(min(seeds, 600), radius)
    report_poi(min(seeds, 400), radius)
    report_cap(min(seeds, 250), radius)''')

    e.before(
        "def main():\n    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000",
        r'''def report_cap(seeds, radius):
    """WHY THE DIG NEEDS A CAP, AND WHAT A CAP COSTS.

    Two containments already exist and BOTH WERE ALREADY MODELLED here: the leg
    turns at endpointClamp, and claimed ground stops it. Neither BOUNDS the dig,
    they only aim it -- making claimed ground a hard wall changes the total by
    under half a per cent, because a typical dungeon's claim is under three per
    cent of the diggable disc. Uncapped, a typical den cuts 2,894 cells by day
    150 and 7,430 by day 300 against a GENERATED network of 1107.

    The sweep also separates the two knobs, which is the useful part: at a fixed
    cap the BUDGET barely moves how much is found, and the CAP barely moves how
    fast. Tune content with one and pacing with the other.
    """
    print("J. THE CAP  (typical dungeon, uniform %d-wide section, sense 15)"
          % CRAWLWAY_MOUTH)
    print("   cap     rate  finds/d150  remains ever%  1st find  dig stops  cells")
    for cap in (1107, 1600, 2400, 0):
        for rate in (2.0, 3.0):
            f150, rem, cells, first, stops = [], 0, [], [], []
            for s in range(seeds):
                finds, cut, stopped = career_capped(s, radius, PROFILES[1], rate, cap)
                f150.append(sum(1 for d, _k in finds if d <= 150))
                if any(k == "remains" for _d, k in finds):
                    rem += 1
                cells.append(cut)
                if finds:
                    first.append(finds[0][0])
                if stopped:
                    stops.append(stopped)
            print("   %-7s x%-4.0f %-11.1f %-14.1f %-9s %-10s %.0f"
                  % (cap if cap else "none", rate,
                     statistics.mean(f150), 100.0 * rem / seeds,
                     ("d%.0f" % pct(first, 0.5)) if first else "-",
                     ("d%.0f" % statistics.mean(stops)) if stops else "never",
                     statistics.mean(cells)))
    print()
    print("   SHIPPED: cap 2400 (about twice the generated network, so the den at")
    print("   most triples its own diggings) at budget x3. A cap of 1107 was")
    print("   measured and rejected -- it holds the contested-discovery beat to")
    print("   under one dungeon in ten, and a set-piece nobody meets is not one.")
    print()


def career_capped(seed, radius, profile, rate, cap, detect=15, drift_deg=12,
                  days=200, clamp_fraction=0.85):
    """explore_poi, plus a cap on NOVEL cells and a brush that counts them.

    Cells are counted rather than predicted because the brush overlaps itself as
    the leg advances, so what a step yields depends on the turn it just took.
    """
    rng = random.Random(seed ^ 0x51E2C0DE)
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    anchor, _i, _o, _r = pick_anchor(
        random.Random(seed * 7919 + EXCAVATOR_FLOOR), radius, centres,
        CHAMBER_SEAT_CLEARANCE)
    if anchor is None:
        return [], 0, None

    name, claimed, per_day = profile
    lim = radius * clamp_fraction
    drift = math.radians(drift_deg)
    x, y = float(anchor[0]), float(anchor[1])
    theta = rng.uniform(0.0, 2.0 * math.pi)
    carry, hoard = 0.0, 0.0
    open_cells = EXCAVATOR_TIER1_CELLS
    cut, finds, hit_already = set(), [], set()
    stopped_on = None

    for day in range(1, days + 1):
        claimed += per_day
        if day <= GRACE_DAYS:
            continue
        if cap and len(cut) >= cap:
            if stopped_on is None:
                stopped_on = day
            continue
        tier = tier_for(hoard)
        reserve_budget = SHIPPED_DIG_CELLS_PER_DAY[tier - 1] \
            * expansion_multiplier(claimed)
        opened = min(int(reserve_budget), EXCAVATOR_MAX_CELLS - open_cells)
        open_cells += max(0, opened)
        hoard += max(0, opened) * SPOIL_PER_CELL

        carry += reserve_budget * rate / CRAWLWAY_MEAN_WIDTH
        steps = int(carry)
        carry -= steps
        targets = build_targets(seed, radius, detect, claimed)
        for _ in range(steps):
            theta += rng.uniform(-drift, drift)
            nx, ny = x + math.cos(theta), y + math.sin(theta)
            if math.hypot(nx, ny) > lim:
                theta += math.pi * rng.uniform(0.5, 1.5)
                continue
            x, y = nx, ny
            cx, cy = int(round(x)), int(round(y))
            cut.update(((cx, cy), (cx + 1, cy), (cx, cy + 1), (cx + 1, cy + 1)))
            kind = poi_hit(x, y, targets)
            key = (kind, round(x / 8.0), round(y / 8.0))
            if kind and key not in hit_already:
                hit_already.add(key)
                finds.append((day, kind))
                theta = rng.uniform(0.0, 2.0 * math.pi)
    return finds, len(cut), stopped_on


''')
    return e


def edit_sim_cavity_growth():
    e = Edit("Tools/sim_den_cavity_growth.py")

    e.sub(
        """  2. THE REMAINS LUMP CANNOT BE COUNTED ON. NotifyRemainsExcavated has NO
     CALLER in the shipped build -- canon 42 put it in ahead of the agents
     that will call it, and the kobold digger pass is still separate. So the
     whole curve must come from digging alone. Modelled at zero here, and the
     day it acquires a caller this file is re-run.""",
        """  2. THE REMAINS LUMP NOW HAS A CALLER, AND THIS FILE HAS BEEN RE-RUN, which
     is what its own header used to promise for the day the digger pass
     landed. Section R below is that re-run. THE ANSWER IS THAT THE NUMBERS
     STAND: on a typical or killer dungeon the lump changes the tier-5 day by
     NOTHING AT ALL, because the median first find lands about day 51 and
     tier 5 is already reached on day 49 or day 40 -- the lump arrives after
     the race is over and only pads the hoard. Only a passive dungeon moves,
     by three days, which is less than the reserve BAND already moves it.""")

    e.sub(
        '''def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 90''',
        r'''# DenController.remainsLump, shipped. Credited outside the cells-opened
# coupling, which is exactly why it had to be checked against it.
REMAINS_LUMP = 120.0

# Days a remains is reached on, from sim_den_digger's confirmed model: the
# median FIRST find is about day 51 for a typical dungeon, and a second and
# third arrive later if the floor holds them (an Ossuary adds one on top of
# sitesPerFloor).
LUMP_DAYS = (51, 90, 120)


def run_with_lumps(days, c0, cpd, reserve, spoil, dig_scale, lumps):
    """The same coupled loop, with remains lumps credited on their measured
    days. Kept separate from run() rather than adding a parameter to it, so the
    shipped curve above is still solved and swept against digging ALONE."""
    hoard, cells_open = 0.0, TIER1_CELLS
    headroom = reserve - TIER1_CELLS
    claimed, first, day_spent = float(c0), {}, None
    on = set(LUMP_DAYS[:lumps])
    for day in range(1, days + 1):
        tier = tier_for(hoard)
        first.setdefault(tier, day)
        claimed += cpd
        if day <= GRACE_DAYS:
            continue
        wanted = (DIG_CELLS_PER_DAY_BY_TIER[tier - 1] * dig_scale
                  * expansion_multiplier(claimed))
        opened = min(wanted, headroom)
        headroom -= opened
        cells_open += opened
        hoard += opened * spoil
        if day in on:
            hoard += REMAINS_LUMP
        if headroom <= 0 and day_spent is None:
            day_spent = day
    return first, hoard, day_spent


def report_lumps(spoil, dig_scale):
    """DOES THE REMAINS LUMP BREAK "TIER 5 IS THE COMPLETED HOLE"?

    It has to be asked, because the lump is credited OUTSIDE the cells-opened
    coupling -- exactly the shape canon 42 rejected for kobold theft, where
    uncoupling a third of the hoard would have made Den Cavity Report's coupling
    assertion a permanent red. The difference is size and timing: 120 a lump
    against a 1400 threshold, arriving after the race on every profile that is
    not passive."""
    print("R. THE REMAINS LUMP AGAINST THE COUPLING  (lump %.0f, days %s)"
          % (REMAINS_LUMP, ", ".join(str(d) for d in LUMP_DAYS)))
    print("   %-26s %-8s %-8s %-8s %s"
          % ("profile", "lumps", "T5 day", "hole", "verdict"))
    worst = 0
    for label, c0, cpd in PROFILES:
        for reserve in (RESERVE_MIN, RESERVE_MAX):
            base = None
            for lumps in (0, 1, 2, 3):
                first, _h, spent = run_with_lumps(
                    VERDICT_HORIZON, c0, cpd, reserve, spoil, dig_scale, lumps)
                t5 = first.get(5)
                if lumps == 0:
                    base = t5
                    continue
                shift = (base - t5) if (base and t5) else 0
                worst = max(worst, shift)
                print("   %-26s %-8d %-8s %-8s %s"
                      % ("%s r%d" % (label.split()[0], reserve), lumps,
                         t5 if t5 else "-", spent if spent else "-",
                         "unchanged" if shift == 0 else "%d day(s) earlier" % shift))
    print()
    print("   Worst movement: %d day(s). The numbers STAND -- no re-tune." % worst)
    print("   The coupling assertion in Den Cavity Report is a static bound on")
    print("   diggable cells times spoil and is untouched by a lump either way.")
    print()


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 90''')

    # Called where the robustness sweep already sits, so the re-run canon asked
    # for prints as part of the standing verdict rather than as a side report
    # somebody has to remember to invoke.
    e.sub("""    print()
    print("Robustness sweep -- every reserve in the band, plus a hermit:")""",
          """    report_lumps(spoil, dig_scale)

    print()
    print("Robustness sweep -- every reserve in the band, plus a hermit:")""")
    return e


# ---- 11. DESIGN_CANON.md ------------------------------------------------

CANON_BAND_OLD = """- **Buried remains are NOT band-confined.** `GetBuriedSites` samples uniformly
  across the usable disc, so a remains cell can land in the outer third that
  entry 19 says nobody reaches. Kobolds target only remains INSIDE the 15-65
  per cent band; remains outside it are left alone, so the band measurement
  stays intact."""

CANON_BAND_NEW = """- **Buried remains are NOT band-confined**, and ~~kobolds target only remains
  INSIDE the 15-65 per cent band~~ **AMENDED IN STAGE 2a, because that rule
  could not survive the model the arc had already chosen.** `GetBuriedSites`
  samples uniformly across the usable disc, so a remains cell can land in the
  outer third entry 19 says nobody reaches. The band clause was written for the
  BEELINE dig -- drive at a known remains -- which `Tools/sim_den_digger.py`
  killed. Under the wander model the diggers target nothing; they find what they
  pass.

  Measured, and the reason the clause had to go: only **12.4 per cent of seeds
  carry any in-band remains at all**, so band-confined targeting is a hard
  ceiling of 12.4 per cent before a single cell is cut, and delivers **2.8 per
  cent** in play against 15.2 for the shipped rule. It would have held this
  entry's own headline beat -- "the first thing in DCR that punishes slowness
  rather than aggression" -- to one dungeon in thirty-five.

  The band measurement stays intact by a different route, and the digger sim
  had already made the argument: the DEN, its CAVITY and its tunnel MOUTHS are
  all in band, and a leg turns at `endpointClamp` (0.85). A dig that goes out to
  fetch is not content placed out there, because the beat still happens where
  the player is."""

CANON_DIG = """#### The exploratory dig (BUILT: stage 2a)

Status: BUILT. `NotifyRemainsExcavated` has a caller at last, the contested
discovery fires and is recovered on clearing, and `SetDenWorkSite` and
`DiggersByTier` both have behaviours. NOT built: kobold theft and `stolenHoard`,
which are stage 2b.

**A leg is an ordinary `DenTunnelData` appended to the END of the list, and only
the last one ever grows.** This is not tidiness, it is the only shape that
survives the reveal machinery. `RebuildDenTunnelCells` numbers reveal segments
with ONE counter running across every tunnel in list order, and
`revealedDenTunnelSegmentIds` is persisted -- so lengthening any but the last
run past a `segmentLength` boundary renumbers every later run's segments and a
reload unfogs the wrong stretches. Appending at the end can only add ids above
the ones already saved. It also means the dig inherits the save shape, the
reveal model, the ownership order, the debug overlay, `DebugRevealAll` and
`LogFeatureStats` for nothing -- this entry's "any feature added to a floor is
added to all three surfaces in the same pass" satisfied by construction rather
than by remembering.

**The section is UNIFORM 2, and 1 was never available.** A 1-wide run is not
4-connected and nothing could walk it: `Centreline` is Bresenham and takes
diagonal steps, and `Dilate` at width 1 emits the cell alone. This entry already
rests the generated network's breach guarantee on the same fact. Uniform rather
than tapered is what makes GROWTH safe: the shared rasteriser lerps the taper
across the run's CURRENT length, so a tapered leg would rewiden its own older
cells every time it grew -- new cells appearing inside stretches revealed days
ago. The digger sim's recorded 2->1 crawlway is corrected accordingly, and every
reach figure it printed at mean width 1.5 was optimistic by 25 per cent.

**THE BRUSH IS TESTED, NOT THE CENTRELINE, and that is what keeps the live tree
and the reloaded tree identical.** Cells are derived from the polyline, and the
rebuild on load cannot know which cells were claimed when they were cut -- worse,
it runs in the save controller's pass 1, before tile influence is restored in
pass 3, so it could not ask. A leg that only turned when its CENTRELINE met
claimed ground would still clip the player's frontier with its 2-wide brush, and
that cell would be re-carved and marked walkable on the next load: the player's
own claimed stone quietly opening itself. Testing the whole footprint means a
leg's cells contain no claimed ground by construction.

**A leg never retraces itself**, for the same class of reason. `Centreline`
de-duplicates, so a revisited cell would be dropped from the line and shift
every later index -- and the index is what decides which reveal stretch a cell
belongs to.

**THE DIG NEEDS A CAP, AND THE TWO CONTAINMENTS THAT LOOK LIKE ONE ARE NOT.**
Claimed ground is refused and a leg turns at `endpointClamp`. Both bound WHERE
and neither bounds HOW MUCH: making claimed ground a hard wall changes the total
by **under half a per cent**, because a typical dungeon's claim is under three
per cent of the diggable disc -- an island in a lake. Uncapped, a typical den
cuts **4,725 cells by day 200 at x2 and 7,028 at x3**, against a generated
network of 1107, a cavity reserve of 400, and this entry's own mana-gift
paragraph sizing the whole gift at "under one per cent of either disc, and well
clear of the roughly 3000-cell site scale entry 19 warned about".

`exploratoryCellCap` is 2400 on floor index 2 -- about twice the generated
network, so **the den at most triples its own diggings**. A cap of 1107 was
measured and rejected: it holds the contested-discovery beat to **7.3-8.0 per
cent** of dungeons, and a set-piece firing on under one run in twelve is content
nobody meets -- entry 19's own argument about the placement band, pointed
inward.

**The cap is the CONTENT knob and the budget is only the PACING knob**, which is
measured rather than asserted and is the useful half of the sweep. Section J of
`Tools/sim_den_digger.py`, typical dungeon:

| cap | rate | finds by d150 | remains ever | first find | dig stops | cells |
|---|---|---|---|---|---|---|
| 1107 | x2 / x3 | 0.8 / 0.6 | 8.0% / 7.3% | d55 / d47 | d87 / d73 | ~1120 |
| 1600 | x2 / x3 | 1.1 / 0.9 | 12.0% / 12.0% | d68 / d57 | d104 / d86 | ~1615 |
| **2400** | **x2 / x3** | 1.9 / 1.5 | **14.7% / 14.0%** | d75 / **d64** | d129 / **d104** | ~2420 |
| none | x2 / x3 | 2.5 / 3.8 | 24.0% / 32.0% | d99 / d92 | never | 4725 / 7028 |

Read the 2400 row across: the beat rate hardly moves with the budget (14.7
against 14.0) while the first find moves eleven days and the end moves
twenty-five. Read the `none` row across and the rate matters again, because with
no cap the rate IS the total. **Tune content with the cap, pacing with the
budget, and do not read a rate change as a content change.** Shipped at **x3**:
first find about day 64, digging over about day 104.

**The budget is ADDITIVE and pays nothing**, which is ruling 5 unchanged: reserve
cells pay the ledger, tunnel cells do not. A share was measured and is a trap --
the ledger pays on reserve cells alone, so diverting the cavity budget freezes
the hoard, freezes the tier and thereby slows the very dig it was diverted to.
Share 1.00 arrives LATER than share 0.50. This is also what keeps "tier 5 IS the
completed hole" true against a dig that runs another fifty days past it.

**THE REMAINS LUMP DOES NOT BREAK THE COUPLING, and it was checked rather than
assumed** -- `Tools/sim_den_cavity_growth.py` said in its own header that it
would be re-run the day `NotifyRemainsExcavated` acquired a caller, and this is
that day. On a typical or killer dungeon the lump moves the tier-5 day by
NOTHING: the median first find lands about day 64 and tier 5 is already reached
on day 49 or day 40, so the lump arrives after the race and only pads the hoard.
Only a passive dungeon moves, by three days, which is less than the reserve BAND
already moves it with no lump at all. **The recorded pacing stands -- tier 2 on
day 13, tier 5 on day 49 -- and nothing was re-tuned.**

**Only remains is sensed at a distance; everything else is met on contact.** A
nearest-search over every road and site cell on the floor, once per walked cell,
would cost more than the behaviour is worth, and remains are the one kind the
dig is FOR. The consequence is recorded rather than hidden: a leg ranges
slightly further between turns than a range-sensing one would, and the sim now
MIRRORS this rule rather than leading it.

**Two ways the diggings end, and the first is the point of them.** Every remains
on the floor is theirs, or the cap is spent. A wisp line says so once, because
coupled or not, a dig that has stopped looks exactly like a dig that is slow.

**A new stretch inherits the reveal of the one before it**, so a dig the player
has walked up to goes on being visible as it advances -- this entry's "progress
VISIBLE between visits", literally. It can only ever show tunnel the diggers
have actually cut and never reaches past ground the player has already been
shown, so fog stays one-way in the direction that matters.

**The dig refuses its own reserve.** `reserveCells` enter `reservedCoreCells`
and NOT the lookup, so they read as `FeatureType.None` -- a leg testing only
`GetFeatureAt` would tunnel through the hole `GrowDenCavity` is waiting for and
the den's two verbs would eat each other.

**The wisp speaks at EXCAVATION, not when the hole is seen.** Tying the telling
to the seeing was the tidier design and was rejected on a dependency: the marker
prop is authored art that does not exist yet, and a set-piece that waits for a
sprite is a set-piece that does not exist. The PROP is the lasting record; the
line is the event. The alert pins the cell, so a player can click straight to
what they lost -- the camera roams the whole floor by Appendix C, so pointing at
fog leaks nothing.

**The markers are NOT taken down on clearing, and the hoard is.** A pile left
standing after `ClearDen` would be claiming gold already in the player's purse;
an emptied remains is a thing that actually happened, and the hole is its
record. Clearing recovers the GRANT, not the stone.

**Clearing pays the remains back**, through `BuriedRemainsController.
GrantExternalDiscovery` -- which this entry has named as the beat's re-entry
point since the decision record, and whose own doc comment has named the
desecration arc as its ONLY caller since it shipped. This is the second.

**`sitesPerFloor` stopped being a guess.** `SiteCountFor` exposes the real
count, so `NotifyRemainsExcavated`'s cap is right on a floor carrying an
Ossuary, where `SitesFor` appends one guaranteed cell per placed one on top of
the sampled sites.

**The four dead persistence methods are ALIVE, and the dig is why they had to
be.** `GatherConsumed`, `RestoreConsumed`, `GatherSensed` and `RestoreSensed`
had no callers anywhere. Losing consumed state was nearly harmless while the
player was the only one digging, because a mined cell is not re-minable. It stops
being harmless the moment something else can take a remains: `MarkNaturalFloor`
fires no `OnTileMined`, so a kobold-opened cell would never be marked consumed,
and `HandleClaimed` would go on murmuring "something waits in the stone nearby --
dig, and I will remember" at ground `MineTile` silently refuses. **An invitation
the game then declines is worse than saying nothing.** `NotifyTakenExternally`
marks the cell sensed as well as consumed for exactly that reason.

**A work site is an OVERRIDE, and the leash did not move.** `DiggerBudget`
bodies are sent to the leg's head each dawn and everyone else is called home;
the role is read off position in the population list exactly as `MayForage`
reads the forager role, so a death re-assigns it for free. The cavity leash is
membership of the cavity's own cell set and stays so -- widening it would
reopen the yo-yo at radius six that this entry already paid for once.

**Key files:** `Floors/TerrainFeatureGenerator.cs` (`AdvanceDenDig`,
`CanCutAt`, `CarveLegCell`, `SpawnDenRemainsMarkers`),
`Floors/DenTunnelProfile.cs`, `Floors/FloorFeatureSaveData.cs`
(`DenTunnelData.exploratory`, `denTakenRemainsCells`),
`DungeonCore/DenController.cs` (`TickExploratoryDig`, `AssignWorkSites`),
`Gameplay/BuriedRemainsController.cs` (`SiteCountFor`, `UntakenRemainsOn`,
`NotifyTakenExternally`), `Save/DungeonSaveController.cs`,
`TESTING/Commands.cs` ("Print Den Dig"), `Tools/sim_den_digger.py`,
`Tools/sim_den_cavity_growth.py`.

"""

CANON_OWES_OLD = """- **Kobold diggers.** `NotifyRemainsExcavated` still has no caller, so the
  runtime dig toward buried remains -- this entry's "a dig visibly heading
  somewhere over days is a stronger race than a tunnel that always pointed
  there" -- does not exist. Floor index 2 now has BODIES; what it has not got is
  a dig. The exploratory-dig measurements in `Tools/sim_den_digger.py` are what
  that stage rests on and must not be re-derived.

"""

CANON_OWES_NEW = """- ~~**Kobold diggers.**~~ **SHIPPED as stage 2a -- see "The exploratory dig"
  above.**

"""

CANON_DEAD_OLD = """- **`BuriedRemainsController` persistence is dead code.** `GatherConsumed`,
  `RestoreConsumed`, `GatherSensed` and `RestoreSensed` have no callers anywhere
  in `Assets/`, so consumed and sensed state is not persisted -- the same fault
  class this entry records for `remainsLump`. Nearly harmless today, since a
  mined cell is not re-minable, but it is the natural home for a kobold-taken
  remains and stage 2 must rule on it before using it.

- **`NotifyRemainsExcavated`'s cap is a guess.** `sitesPerFloor` is private with
  no accessor, so the `remainsOnFloor = 2` default is hardcoded and WRONG on any
  floor with an Ossuary, since `SitesFor` appends a guaranteed remains per placed
  Ossuary on top of it. Recommended and unbuilt: expose
  `BuriedRemainsController.SiteCountFor(floor)`.

"""

CANON_DEAD_NEW = """- ~~**`BuriedRemainsController` persistence is dead code.**~~ **RULED ON AND
  WIRED in stage 2a: all four methods have callers, and the dig is what made
  losing consumed state stop being harmless.**

- ~~**`NotifyRemainsExcavated`'s cap is a guess.**~~ **FIXED in stage 2a:
  `SiteCountFor` exposes the real count, Ossuary included.**

"""


def edit_canon():
    e = Edit("Docs/DESIGN_CANON.md")
    e.sub(CANON_BAND_OLD, CANON_BAND_NEW)
    e.before("#### What the den arc still owes", CANON_DIG)
    e.sub(CANON_OWES_OLD, CANON_OWES_NEW)
    e.sub(CANON_DEAD_OLD, CANON_DEAD_NEW)
    return e


AUTHORING_CH41 = """
  <details>
    <summary>41. The excavated-remains marker (the hole they leave behind)</summary>
    <div class="body">
      <div class="why">Canon 42 requires this chapter. The marker is not decoration &mdash;
      it is the ONLY lasting evidence that the kobolds robbed the player of a buried
      remains. The claim-halo murmur fires within two cells of a claimed tile, so a player
      who never sensed that stone would otherwise have nothing to look at. The wisp speaks
      once at the moment of excavation; the hole is what is still there a week later.</div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s1"><label for="dcr-authoring-v1-c41s1"><b>Confirm the debt first.</b> <code>Print Den Dig</code> on a floor-2 run prints <i>no remains marker prefab assigned on the profile</i> while the slot is empty, and <code>Dungeon Core &rarr; Audit Art Debt</code> lists <code>DenTunnelProfile.remainsMarkerPrefab</code> as <b>REQUIRED</b>. It is left null on purpose: a placeholder here would score FILLED, and this is the one document whose whole job is to be an honest work queue.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s2"><label for="dcr-authoring-v1-c41s2"><b>It is a PROP, not a creature, so drop the proportions clause.</b> Chapter 0's Style Contract, prop branch: fixed head, subject slot, fixed tail; orthographic front view; target <b>2 cells = 64&nbsp;px</b>; palette <code>dcr-props-1x.png</code>. Generation floor <code>256x192</code>, INSPYRENET for background removal, then <code>Tools/dcr_sprite_post.py</code> for the area downscale and own-palette quantisation. CFG 1 leaves the negative prompt inert &mdash; put nothing there.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s3"><label for="dcr-authoring-v1-c41s3"><b>Draw the ABSENCE, not the find.</b> This is the opposite of the resting place: broken stone, a shallow scraped pit, spoil heaped to one side, maybe a splinter of bone nobody bothered to take. If it reads as treasure the beat inverts &mdash; the player is meant to feel late, not rewarded. Nothing gold, nothing glinting.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s4"><label for="dcr-authoring-v1-c41s4"><b>PIVOT AT THE BASE, sorting layer <code>Player</code>.</b> Appendix B puts every Y-sorting entity on <code>Player</code>, and the hoard prop already records why the pivot matters: <code>Player</code> sorts on Y, so a centre pivot makes a prop with height sort half a tile behind where it stands and the avatar clips through it. Copy <code>HoardPrefab.prefab</code>'s transform setup rather than rebuilding it.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s5"><label for="dcr-authoring-v1-c41s5"><b>No collider, no interaction, no script.</b> The marker is a pure visual skin like <code>DenHoardProp</code>. Nothing pathfinds around it and nothing mines it &mdash; the cell underneath it is already open ground, and <code>MineTile</code> refuses a mined cell, which is what stops the player being paid twice for stone the kobolds opened.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s6"><label for="dcr-authoring-v1-c41s6"><b>Assign it on the PROFILE, not per floor.</b> <code>Assets/ScriptableObjects/Floors/DenTunnelProfile.asset</code> &rarr; <code>remainsMarkerPrefab</code>. It is shared by every den, exactly as <code>hoardPrefab</code> is; the per-floor entries carry only the things that differ between an occupier and an excavator.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s7"><label for="dcr-authoring-v1-c41s7"><b>ONE sprite is the whole slot.</b> There is no tier ladder here &mdash; unlike the hoard, a robbed remains does not grow. If you want variation later it belongs in a future array with an explicit toggle, never in an empty-slot-means-something convention.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c41s8"><label for="dcr-authoring-v1-c41s8"><b>Check it in the dark, at the zoom the game is played at.</b> Floor index 2, let the dig run or force it, then <code>Print Den Dig</code> for the marker count and go and look. A marker that reads at 100% and vanishes into tunnel floor at play zoom has not done the one job it exists for.</label></div>
    </div>
  </details>
"""


def edit_authoring_guide():
    e = Edit("Docs/DCR_Guide_Content_Authoring.html")
    # Appended after chapter 40's close, at the one place the chapter list ends.
    e.sub("""      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c40s8">""",
          """      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c40s8">""")
    anchor = "</details>\n\n  \n</div>\n\n<script>"
    e.sub(anchor, "</details>\n" + AUTHORING_CH41 + "\n  \n</div>\n\n<script>")
    return e



# ---- validation ---------------------------------------------------------

def balanced(text, opens="{([", closes="})]"):
    """Brace/paren/bracket balance OUTSIDE string and comment context. Crude by
    design: it is a smoke test for a botched insertion, not a parser."""
    depth = {o: 0 for o in opens}
    i, n = 0, len(text)
    in_line, in_block, in_str, in_chr, in_verb = False, False, False, False, False
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if in_line:
            if c == "\n": in_line = False
        elif in_block:
            if c == "*" and nxt == "/": in_block = False; i += 1
        elif in_verb:
            if c == '"':
                if nxt == '"': i += 1
                else: in_verb = False
        elif in_str:
            if c == "\\": i += 1
            elif c == '"': in_str = False
        elif in_chr:
            if c == "\\": i += 1
            elif c == "'": in_chr = False
        else:
            if c == "/" and nxt == "/": in_line = True; i += 1
            elif c == "/" and nxt == "*": in_block = True; i += 1
            elif c == "@" and nxt == '"': in_verb = True; i += 1
            elif c == '"': in_str = True
            elif c == "'": in_chr = True
            elif c in opens: depth[c] += 1
            elif c in closes: depth[opens[closes.index(c)]] -= 1
        i += 1
    bad = {k: v for k, v in depth.items() if v != 0}
    return bad


def ascii_only(text, label):
    for lineno, line in enumerate(text.split("\n"), 1):
        for ch in line:
            if ord(ch) > 126:
                raise SystemExit("NON-ASCII in inserted text (%s line %d): %r"
                                 % (label, lineno, ch))


BUILDERS = [
    edit_profile_cs, edit_feature_save, edit_generator, edit_buried,
    edit_save_data, edit_save_controller, edit_den_controller, edit_wisp,
    edit_asset, edit_commands, edit_sim_digger, edit_sim_cavity_growth,
    edit_canon, edit_authoring_guide,
]


def main():
    global REPO
    REPO = _find_repo()
    print("repo: " + REPO)

    # Idempotency guard, checked against the ONE file whose marker cannot
    # plausibly arrive by another route.
    probe, _c, _b = load("Assets/Scripts/Floors/DenTunnelProfile.cs")
    if GUARD in probe:
        raise SystemExit(
            "ALREADY APPLIED: DenTunnelProfile.cs already carries '%s'. "
            "Re-running would double-apply; reset the tree first." % GUARD)

    # -- stage everything in memory; nothing is written until all of it builds
    staged = []
    for fn in BUILDERS:
        staged.append(fn())

    # -- validate the staged text BEFORE any write
    for e in staged:
        if e.rel.endswith(".cs"):
            bad = balanced(e.text)
            if bad:
                raise SystemExit("UNBALANCED %s: %s" % (e.rel, bad))
        if e.rel.endswith(".py"):
            tmp = os.path.join(tempfile.gettempdir(), os.path.basename(e.rel))
            with open(tmp, "w", encoding="utf-8") as fh:
                fh.write(e.text)
            py_compile.compile(tmp, doraise=True)
            os.remove(tmp)

    # ASCII on INSERTED text only -- HEAD's own em-dashes and box drawing stay.
    for blob, label in ((DIG_CS, "DIG_CS"), (CANON_DIG, "CANON_DIG"),
                        (CANON_BAND_NEW, "CANON_BAND_NEW"),
                        (CANON_OWES_NEW, "CANON_OWES_NEW"),
                        (CANON_DEAD_NEW, "CANON_DEAD_NEW"),
                        (AUTHORING_CH41, "AUTHORING_CH41")):
        ascii_only(blob, label)

    # -- write
    for e in staged:
        store(e.rel, e.text, e.crlf, e.bom)

    print("deliver_den_stage2a: applied %d files" % len(staged))
    for e in staged:
        print("   " + e.rel)


if __name__ == "__main__":
    main()
