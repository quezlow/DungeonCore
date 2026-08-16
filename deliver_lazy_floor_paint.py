#!/usr/bin/env python3
# Delivery: Lazy Floor Paint (canon 47).
#
# What this applies, in one breath: floors other than 0 stop painting the
# floor half of the disc at creation; the fog disc still paints in full
# (fog-tile absence is the revealed flag), and the floor tile is laid per
# cell inside RevealTile the moment fog lifts. Diagnostics ship in the same
# edit: a fog/floor split on the bootstrap log line, per-floor counters on
# DungeonTerrain, and two new audits in Validate Reveal Consistency. The
# canon edits (entry 47, the entry-19 rider amendment, the TOC) ride this
# script, and the guide is written to Docs/.
#
# Discipline: every anchor is asserted count == 1 against the normalised
# text BEFORE anything is modified; all edits stage in memory; line endings
# are normalised on read and restored per file on write; BOMs are preserved;
# a re-run aborts on the idempotency guard; all writes complete before any
# output is printed.

import os
import sys

# ---------------------------------------------------------------- repo ----

def resolve_repo():
    cands = []
    if len(sys.argv) > 1:
        cands.append(sys.argv[1])
    env = os.environ.get("DCR_REPO")
    if env:
        cands.append(env)
    d = os.getcwd()
    while True:
        cands.append(d)
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    for c in cands:
        if (os.path.isfile(os.path.join(c, "Docs", "DESIGN_CANON.md"))
                and os.path.isdir(os.path.join(c, "Assets"))):
            return os.path.abspath(c)
    sys.exit("[deliver] Could not resolve the DungeonCore repo. Run from "
             "inside the checkout, pass its path as argument 1, or set "
             "DCR_REPO.")


REPO = resolve_repo()
LOG = []


def note(msg):
    LOG.append(msg)


# ------------------------------------------------------------- file IO ----

class Src(object):
    """One target file: read once, edit the normalised text in memory,
    restore its own line endings (and BOM, if any) on write."""

    def __init__(self, rel):
        self.rel = rel
        self.path = os.path.join(REPO, rel)
        if not os.path.isfile(self.path):
            sys.exit("[deliver] Missing target file: " + rel)
        raw = open(self.path, "rb").read()
        self.bom = raw.startswith(b"\xef\xbb\xbf")
        if self.bom:
            raw = raw[3:]
        crlf = raw.count(b"\r\n")
        lf = raw.replace(b"\r\n", b"\n").count(b"\n") - 0
        self.use_crlf = crlf > 0 and crlf >= (lf - crlf)
        self.text = raw.replace(b"\r\n", b"\n").decode("utf-8")

    def must_count(self, needle, n, tag):
        c = self.text.count(needle)
        if c != n:
            sys.exit("[deliver] Anchor '%s' in %s: expected %d hit(s), "
                     "found %d. Tree untouched." % (tag, self.rel, n, c))

    def must_absent(self, needle, tag):
        if needle in self.text:
            sys.exit("[deliver] Idempotency guard '%s' tripped in %s: the "
                     "delivery appears to be already applied. Tree "
                     "untouched." % (tag, self.rel))

    def replace_once(self, old, new, tag):
        self.must_count(old, 1, tag)
        self.text = self.text.replace(old, new, 1)
        note("  edit  %-46s %s" % (tag, self.rel))

    def staged_bytes(self):
        data = self.text.encode("utf-8")
        if self.use_crlf:
            data = data.replace(b"\n", b"\r\n")
        if self.bom:
            data = b"\xef\xbb\xbf" + data
        return data


def ascii_only(s, tag):
    for i, ch in enumerate(s):
        if ord(ch) > 127:
            sys.exit("[deliver] Non-ASCII character in inserted text '%s' "
                     "at offset %d (%r). Tree untouched." % (tag, i, ch))


def balance_csharp(text, rel):
    """Comment- and string-aware bracket balance over a whole C# file."""
    depth = {"{": 0, "(": 0, "[": 0}
    close = {"}": "{", ")": "(", "]": "["}
    i, n = 0, len(text)
    mode = None  # None | 'line' | 'block' | 'str' | 'chr'
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if mode == "line":
            if ch == "\n":
                mode = None
        elif mode == "block":
            if ch == "*" and nxt == "/":
                mode = None
                i += 1
        elif mode == "str":
            if ch == "\\":
                i += 1
            elif ch == '"':
                mode = None
        elif mode == "chr":
            if ch == "\\":
                i += 1
            elif ch == "'":
                mode = None
        else:
            if ch == "/" and nxt == "/":
                mode = "line"
                i += 1
            elif ch == "/" and nxt == "*":
                mode = "block"
                i += 1
            elif ch == '"':
                mode = "str"
            elif ch == "'":
                mode = "chr"
            elif ch in depth:
                depth[ch] += 1
            elif ch in close:
                depth[close[ch]] -= 1
                if depth[close[ch]] < 0:
                    sys.exit("[deliver] Bracket underflow (%s) in staged %s. "
                             "Tree untouched." % (ch, rel))
        i += 1
    if mode in ("block", "str", "chr"):
        sys.exit("[deliver] Unterminated %s in staged %s. Tree untouched."
                 % (mode, rel))
    for k, v in depth.items():
        if v != 0:
            sys.exit("[deliver] Unbalanced '%s' (%+d) in staged %s. Tree "
                     "untouched." % (k, v, rel))


# ============================================================ DT edits ====

dt = Src("Assets/Scripts/DungeonCore/DungeonTerrain.cs")
dt.must_absent("lazyFloorPaint", "DT already lazy")

DT_FIELDS_OLD = "    private bool initialised = false;\n"

DT_FIELDS_NEW = """    private bool initialised = false;

    // -- Lazy floor paint (canon 47) --------------------------------------
    // Floors other than 0 no longer paint the floor disc at creation: the
    // fog disc still paints in full (fog-tile ABSENCE is the revealed datum
    // FloorRoot.IsRevealed and the staging systems read, so fog cannot be
    // deferred), and the floor tile is laid per cell by RevealTile the
    // moment fog lifts. Measured at HEAD before the change: the combined
    // disc paint was 2855 ms of floor 5's 3047 ms bootstrap at radius 600,
    // and the two block passes are symmetric, so this removes roughly half.
    // Floor 0 stays fully eager -- its rim facade unfogs around RevealTile
    // and SurfaceZoneGenerator deliberately clears rim floor tiles.
    private bool lazyFloorPaint;
    private int floorCellsPaintedOnReveal;
    private int revealPaintSkippedExisting;
    private int revealPaintOutsideDisc;
    private long revealPaintTicks;
    private long lastFogPaintMs = -1;
    private long lastFloorPaintMs = -1;
"""

dt.replace_once(DT_FIELDS_OLD, DT_FIELDS_NEW, "DT fields")

DT_GEN_OLD = """        coreCell = centre;
        currentRadius = RadiusForThisFloor();
        PaintTerrain(coreCell, currentRadius);
"""

DT_GEN_NEW = """        coreCell = centre;
        currentRadius = RadiusForThisFloor();

        // Canon 47: only floor 0 pays the floor half of the disc paint here.
        // The load path shares this decision for free -- RecreateFloorFromSave
        // reaches this same call, and the restore sweeps re-reveal every
        // revealed cell through RevealTile, which repaints exactly that set.
        lazyFloorPaint = myFloor != null && myFloor.FloorIndex != 0;

        PaintTerrain(coreCell, currentRadius);
"""

dt.replace_once(DT_GEN_OLD, DT_GEN_NEW, "DT GenerateAt lazy flag")

DT_PAINT_OLD = """    /// <summary>
    /// Paints the floor and fog layers across the disc.
    ///
    /// This used to call SetTile twice per cell. Tilemap pays chunk lookup and
    /// dirtying on every one of those, which measured at a flat ~3.4 us per call
    /// regardless of radius: 7.7 SECONDS to create floor 5 at radius 600, where
    /// the disc holds about 1.13 million cells. Cost was perfectly linear in
    /// cell count, so the problem was never the loop -- it was paying per-call
    /// overhead 2.26 million times.
    ///
    /// SetTilesBlock hands the tilemap a whole rectangle at once. Cells outside
    /// the disc are left null in the array, which is safe here because
    /// PaintTerrain runs exactly once per floor, from GenerateAt, behind the
    /// 'initialised' guard, and therefore only ever writes to empty tilemaps.
    /// </summary>
    private void PaintTerrain(Vector3Int centre, int radius)
    {
        if (floorTilemap == null || fogTilemap == null) return;
        if (radius < 0) return;

        long radiusSq = (long)radius * radius;

        for (int bandStart = -radius; bandStart <= radius; bandStart += PaintBandRows)
        {
            int bandEnd = Mathf.Min(bandStart + PaintBandRows - 1, radius);
            int height = bandEnd - bandStart + 1;

            // The widest row in this band is the one nearest the centre line, so
            // bands near the poles get a correspondingly narrow rectangle rather
            // than the full 2r+1 and a great deal of null.
            int nearestDy = bandStart > 0 ? bandStart : (bandEnd < 0 ? -bandEnd : 0);
            int halfWidth = IntSqrt(radiusSq - (long)nearestDy * nearestDy);
            int width = halfWidth * 2 + 1;

            var floorBlock = new TileBase[width * height];
            var fogBlock = new TileBase[width * height];

            for (int row = 0; row < height; row++)
            {
                long dy = bandStart + row;
                long spanSq = radiusSq - dy * dy;
                if (spanSq < 0) continue;

                int span = IntSqrt(spanSq);
                int rowBase = row * width;
                for (int i = halfWidth - span; i <= halfWidth + span; i++)
                {
                    floorBlock[rowBase + i] = floorTile;
                    fogBlock[rowBase + i] = fogTile;
                }
            }

            var bounds = new BoundsInt(
                centre.x - halfWidth, centre.y + bandStart, 0, width, height, 1);

            floorTilemap.SetTilesBlock(bounds, floorBlock);
            fogTilemap.SetTilesBlock(bounds, fogBlock);
        }
    }
"""

DT_PAINT_NEW = """    /// <summary>
    /// Paints the floor and fog layers across the disc.
    ///
    /// This used to call SetTile twice per cell. Tilemap pays chunk lookup and
    /// dirtying on every one of those, which measured at a flat ~3.4 us per call
    /// regardless of radius: 7.7 SECONDS to create floor 5 at radius 600, where
    /// the disc holds about 1.13 million cells. Cost was perfectly linear in
    /// cell count, so the problem was never the loop -- it was paying per-call
    /// overhead 2.26 million times.
    ///
    /// SetTilesBlock hands the tilemap a whole rectangle at once. Cells outside
    /// the disc are left null in the array, which is safe here because each
    /// layer's disc is written exactly once, from GenerateAt, behind the
    /// 'initialised' guard, and therefore only ever lands on an empty tilemap.
    ///
    /// CANON 47 -- the two layers are written by one single-layer helper,
    /// timed apart, and the FLOOR pass is skipped entirely on lazy floors:
    /// measured at HEAD the combined pass was 2855 ms of floor 5's 3047 ms
    /// bootstrap (radius 600), and the floor half of that is what RevealTile
    /// now pays per cell as fog lifts. Fog still paints in full every time --
    /// fog-tile absence IS the revealed flag (FloorRoot.IsRevealed), so a
    /// missing fog disc would read as a fully revealed floor.
    /// </summary>
    private void PaintTerrain(Vector3Int centre, int radius)
    {
        if (floorTilemap == null || fogTilemap == null) return;
        if (radius < 0) return;

        bool timing = FloorRoot.LogBootstrapTimings;
        var sw = timing ? System.Diagnostics.Stopwatch.StartNew() : null;

        PaintDiscLayer(fogTilemap, fogTile, centre, radius);
        if (sw != null) { lastFogPaintMs = sw.ElapsedMilliseconds; sw.Restart(); }

        if (!lazyFloorPaint)
            PaintDiscLayer(floorTilemap, floorTile, centre, radius);
        if (sw != null)
        {
            lastFloorPaintMs = lazyFloorPaint ? 0 : sw.ElapsedMilliseconds;
            sw.Stop();
        }
    }

    /// <summary>One layer of the banded disc write. Kept single-layer so the
    /// lazy path can skip the floor pass without threading nulls through a
    /// combined fill loop, and so the bootstrap log can time the halves
    /// apart.</summary>
    private void PaintDiscLayer(Tilemap map, TileBase tile, Vector3Int centre, int radius)
    {
        long radiusSq = (long)radius * radius;

        for (int bandStart = -radius; bandStart <= radius; bandStart += PaintBandRows)
        {
            int bandEnd = Mathf.Min(bandStart + PaintBandRows - 1, radius);
            int height = bandEnd - bandStart + 1;

            // The widest row in this band is the one nearest the centre line, so
            // bands near the poles get a correspondingly narrow rectangle rather
            // than the full 2r+1 and a great deal of null.
            int nearestDy = bandStart > 0 ? bandStart : (bandEnd < 0 ? -bandEnd : 0);
            int halfWidth = IntSqrt(radiusSq - (long)nearestDy * nearestDy);
            int width = halfWidth * 2 + 1;

            var block = new TileBase[width * height];

            for (int row = 0; row < height; row++)
            {
                long dy = bandStart + row;
                long spanSq = radiusSq - dy * dy;
                if (spanSq < 0) continue;

                int span = IntSqrt(spanSq);
                int rowBase = row * width;
                for (int i = halfWidth - span; i <= halfWidth + span; i++)
                    block[rowBase + i] = tile;
            }

            var bounds = new BoundsInt(
                centre.x - halfWidth, centre.y + bandStart, 0, width, height, 1);

            map.SetTilesBlock(bounds, block);
        }
    }
"""

dt.replace_once(DT_PAINT_OLD, DT_PAINT_NEW, "DT PaintTerrain split")

DT_REVEAL_OLD = ("    public void RevealTile(Vector3Int pos) => "
                 "fogTilemap.SetTile(pos, null);\n")

DT_REVEAL_NEW = """    public void RevealTile(Vector3Int pos)
    {
        fogTilemap.SetTile(pos, null);
        EnsureFloorPainted(pos);
    }

    /// <summary>Canon 47: lays the plain floor tile under a cell the moment it
    /// becomes visible. Every reveal path funnels through RevealTile, so this
    /// covers claims, mining halos, feature reveals, and the load-path restore
    /// sweeps for free; ApplyRoadFogFade calls it directly for the feather band
    /// it half-fades WITHOUT revealing. The HasTile skip is the paving rule:
    /// site paving lands unconditionally at ApplyRuinsOverrides on both paths,
    /// and a cell that already holds any floor-layer tile is never overwritten,
    /// so the two sides cannot disagree whichever runs first. No-op on eager
    /// floors (floor 0) and outside the disc, mirroring how fog behaves
    /// there.</summary>
    public void EnsureFloorPainted(Vector3Int pos)
    {
        if (!lazyFloorPaint || floorTilemap == null || floorTile == null) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!IsWithinRadius(pos, currentRadius))
        {
            revealPaintOutsideDisc++;
        }
        else if (floorTilemap.HasTile(pos))
        {
            revealPaintSkippedExisting++;
        }
        else
        {
            floorTilemap.SetTile(pos, floorTile);
            floorCellsPaintedOnReveal++;
        }
        revealPaintTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
    }

    /// <summary>True on floors whose floor disc defers to reveal (canon 47).</summary>
    public bool LazyFloorPaint => lazyFloorPaint;

    /// <summary>The plain disc tile, exposed so Validate Reveal Consistency
    /// can tell a deferred-paint leak from legitimate site paving under
    /// fog.</summary>
    public TileBase FloorTile => floorTile;

    public int FloorCellsPaintedOnReveal => floorCellsPaintedOnReveal;
    public int RevealPaintSkippedExisting => revealPaintSkippedExisting;
    public int RevealPaintOutsideDisc => revealPaintOutsideDisc;

    /// <summary>Cumulative milliseconds spent laying floor tiles on reveal.</summary>
    public long RevealPaintMs
        => revealPaintTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Per-half timings of the last disc paint; -1 until a bootstrap
    /// has run with FloorRoot.LogBootstrapTimings on.</summary>
    public long LastFogPaintMs => lastFogPaintMs;
    public long LastFloorPaintMs => lastFloorPaintMs;
"""

dt.replace_once(DT_REVEAL_OLD, DT_REVEAL_NEW, "DT RevealTile + accessors")

# ===================================================== FloorRoot edit =====

fr = Src("Assets/Scripts/Floors/FloorRoot.cs")
fr.must_absent("paintSplit", "FloorRoot already split")

FR_LOG_OLD = """            Debug.Log($"[FloorRoot] Bootstrap floor {floorIndex + 1} " +
                      $"(radius {(terrain != null ? terrain.CurrentRadius : -1)}): " +
                      $"terrain {tTerrain} ms, features {tFeatures} ms, " +
                      $"typemap {tTypeMap} ms, influence {tInfluence} ms, " +
                      $"total {tTerrain + tFeatures + tTypeMap + tInfluence} ms.");
"""

FR_LOG_NEW = """            // Canon 47 -- the terrain bucket is split so the deferred floor
            // half stays visible against the HEAD measurement (2855 ms of
            // 3047 ms at radius 600) without re-instrumenting anything.
            string paintSplit = terrain == null ? "" :
                terrain.LazyFloorPaint
                    ? $" (fog {terrain.LastFogPaintMs} ms, floor deferred)"
                    : $" (fog {terrain.LastFogPaintMs} ms, floor {terrain.LastFloorPaintMs} ms)";
            Debug.Log($"[FloorRoot] Bootstrap floor {floorIndex + 1} " +
                      $"(radius {(terrain != null ? terrain.CurrentRadius : -1)}): " +
                      $"terrain {tTerrain} ms{paintSplit}, features {tFeatures} ms, " +
                      $"typemap {tTypeMap} ms, influence {tInfluence} ms, " +
                      $"total {tTerrain + tFeatures + tTypeMap + tInfluence} ms.");
"""

fr.replace_once(FR_LOG_OLD, FR_LOG_NEW, "FloorRoot split log line")

# =========================================================== TFG edits ====

tfg = Src("Assets/Scripts/Floors/TerrainFeatureGenerator.cs")
tfg.must_absent("revealedNoFloor", "TFG already audited")
tfg.must_absent("EnsureFloorPainted", "TFG already feathered")

TFG_FEATHER_OLD = """            float t = Mathf.Clamp01(d / (float)Mathf.Max(1, roadPrepareCells));
            var col = fog.GetColor(c);
            col.a = t * t;
            fog.SetTileFlags(c, TileFlags.None);
            fog.SetColor(c, col);
        }
"""

TFG_FEATHER_NEW = """            float t = Mathf.Clamp01(d / (float)Mathf.Max(1, roadPrepareCells));
            var col = fog.GetColor(c);
            col.a = t * t;
            fog.SetTileFlags(c, TileFlags.None);
            fog.SetColor(c, col);

            // Canon 47: this band is the one place a cell shows through fog
            // WITHOUT being revealed, so the deferred floor tile must land
            // here too or the feather thins onto bare void. Solid-alpha cells
            // skip -- nothing shows through them, and painting there would
            // read as a leak in Validate Reveal Consistency.
            if (col.a < 1f && terrain != null) terrain.EnsureFloorPainted(c);
        }
"""

tfg.replace_once(TFG_FEATHER_OLD, TFG_FEATHER_NEW, "TFG feather floor paint")

TFG_NOTE_OLD = """    /// NOTE for the lazy floor-paint backlog item: if disc painting ever moves
    /// into RevealTile, this pass must move with it or paving is overpainted.</summary>
"""

TFG_NOTE_NEW = """    /// CANON 47 RESOLUTION of the old lazy floor-paint note: disc painting
    /// DID move into RevealTile, and this pass did NOT move with it.
    /// RevealTile's floor paint skips any cell already holding a floor-layer
    /// tile, so paving laid here survives whichever side runs first, on fresh
    /// generation and load alike.</summary>
"""

tfg.replace_once(TFG_NOTE_OLD, TFG_NOTE_NEW, "TFG paving note resolved")

TFG_DECL_OLD = "        var hardJoins = new List<Vector3Int>();\n"

TFG_DECL_NEW = """        var hardJoins = new List<Vector3Int>();
        // Canon 47 -- the deferred-floor audits. Only meaningful where the
        // floor disc is lazy; floor 0 stays eager and is skipped whole.
        var revealedNoFloor = new List<Vector3Int>();
        var plainFloorUnderFog = new List<Vector3Int>();
        bool lazyFloor = terrain.LazyFloorPaint;
        var floorMap = terrain.FloorTilemap;
        var plainFloorTile = terrain.FloorTile;
"""

tfg.replace_once(TFG_DECL_OLD, TFG_DECL_NEW, "TFG audit declarations")

TFG_SWEEP_OLD = """                if (!revealed && painted)
                {
                    paintedFogged.Add(cell);
                    foggedBy[FeatureIndex(cause)]++;
                }
"""

TFG_SWEEP_NEW = """                if (!revealed && painted)
                {
                    paintedFogged.Add(cell);
                    foggedBy[FeatureIndex(cause)]++;
                }

                if (lazyFloor && floorMap != null)
                {
                    var floorHere = floorMap.GetTile(cell);
                    if (revealed && floorHere == null)
                        revealedNoFloor.Add(cell);
                    // Site paving under fog is legitimate (it lands eagerly at
                    // ApplyRuinsOverrides); only the PLAIN disc tile under
                    // fully solid fog means the reveal hook leaked. Feather
                    // cells carry alpha below 1 by construction and are
                    // exempt.
                    if (!revealed && floorHere == plainFloorTile
                        && fog != null && fog.GetColor(cell).a >= 0.999f)
                        plainFloorUnderFog.Add(cell);
                }
"""

tfg.replace_once(TFG_SWEEP_OLD, TFG_SWEEP_NEW, "TFG audit sweep")

TFG_BAD_OLD = """        int bad = revealedUnpainted.Count + paintedFogged.Count
                + roadUnpainted.Count + roadOverPaving.Count;
"""

TFG_BAD_NEW = """        Line(sb, "revealed cell with NO floor tile (deferred paint missed it)",
             revealedNoFloor, sampleLimit);
        Line(sb, "plain floor tile under SOLID fog (deferred paint leaked)",
             plainFloorUnderFog, sampleLimit);
        sb.Append("          lazy floor paint: ")
          .Append(lazyFloor ? "ON" : "off (eager floor)")
          .Append("   painted-on-reveal ").Append(terrain.FloorCellsPaintedOnReveal)
          .Append(", skipped-existing ").Append(terrain.RevealPaintSkippedExisting)
          .Append(", outside-disc ").Append(terrain.RevealPaintOutsideDisc)
          .Append(", reveal-paint total ").Append(terrain.RevealPaintMs)
          .AppendLine(" ms");

        int bad = revealedUnpainted.Count + paintedFogged.Count
                + roadUnpainted.Count + roadOverPaving.Count
                + revealedNoFloor.Count + plainFloorUnderFog.Count;
"""

tfg.replace_once(TFG_BAD_OLD, TFG_BAD_NEW, "TFG audit print + verdict")

# ========================================================= canon edits ====

canon = Src("Docs/DESIGN_CANON.md")
canon.must_absent("## 47.", "canon 47")

CANON_TOC_OLD = """44. Monster Allegiance (the Fourth Side)

**Appendix**"""

CANON_TOC_NEW = """44. Monster Allegiance (the Fourth Side)
45. The Loot Policy Dial (and the Appeal Ledger's Poverty Half)
46. The Deep Occupants -- Substrate (canon 42's nameless things)
47. Lazy Floor Paint (Deferred Disc Painting)

**Appendix**"""

canon.replace_once(CANON_TOC_OLD, CANON_TOC_NEW,
                   "canon TOC (45/46 hygiene + 47)")

CANON_RIDER_OLD = """and the load path call after the disc paint; if the lazy floor-paint
backlog item ever lands, the paving pass must move with it. The carriageway"""

CANON_RIDER_NEW = """and the load path call after the disc paint; ~~if the lazy floor-paint
backlog item ever lands, the paving pass must move with it.~~ **RESOLVED
OTHERWISE by entry 47 -- the paving pass stays exactly here; lazy reveal
paint skips any cell that already holds a floor-layer tile, so paving can
never be overpainted whichever side runs first.** The carriageway"""

canon.replace_once(CANON_RIDER_OLD, CANON_RIDER_NEW,
                   "canon 19 paving rider amended")

ENTRY_47 = """## 47. Lazy Floor Paint (Deferred Disc Painting)

Status: SHIPPED. Verified: <date of landing>.

Floors no longer pay the floor half of the disc paint at creation. Measured
at HEAD with `FloorRoot.LogBootstrapTimings` before the change: the terrain
bucket -- `DungeonTerrain.GenerateAt`, radius resolve plus the two banded
`SetTilesBlock` passes -- was 2855 ms of floor 5's 3047 ms bootstrap at
radius 600 and 1220 of 1288 at radius 400, roughly area-proportional and
about 94 per cent of a deep floor's creation, NOT the 99.5 per cent the
backlog note remembered. The two passes are symmetric, so deferring the
floor pass removes roughly half of a deep floor's creation cost; the fog
pass cannot defer, below.

**The rule, one sentence:** the fog disc still paints in full at
`GenerateAt`; the floor tile is laid per cell inside `RevealTile`, the
moment fog lifts, and nowhere else.

**Why fog stays eager.** Fog-tile absence IS the revealed flag --
`FloorRoot.IsRevealed` reads it, and ReachabilityDirector, the minimap and
`DeadCoreSaturation` (the canon 42/46 staging) read THAT. An unpainted fog
disc would report every cell revealed and give away rivers and chambers
wholesale. Fog is also the substrate the road fog feather and the floor-0
rim gloom tint per cell. Re-founding IsRevealed on a revealed set was
considered for the remaining half and rejected as reveal-semantics surgery.

**Why reveal, not proximity or chunks.** Walkability is born in
`TileInfluenceManager`'s claimed/mined sets and read by
`DungeonPathfinder.IsWalkable`; no tilemap is consulted, so pixels were
already decoupled from movement. Every reveal path -- claims, the starter
blob and its border, mining halos, feature reveals, and the load restore
sweeps (silent `ClaimTile`, the mined sweep, `UnfogAllRevealedFeatures`) --
funnels through `RevealTile`, so one hook covers fresh and load alike; and
rivers and roads had already established paint-on-reveal as the house
pattern, load-path repaint sweeps included. Camera-proximity painting would
paint under fog, invisibly.

**Floor 0 is the one eager floor.** Its rim facade unfogs cells around
`RevealTile` (direct `fogTilemap.SetTile`), and
`SurfaceZoneGenerator.PaintRimGround` deliberately CLEARS rim floor tiles;
a reveal-hook repaint there would undo that. Bronze radius keeps the kept
cost in the tens of milliseconds. This also stays clear of entry 24's
rejection of staged tile painting, which stands untouched: that rejection
is about the SURFACE bands, where the confiner ceiling is the only thing
between the camera and the void (Appendix C). The dungeon disc differs
precisely because fog covers everything unrevealed on every floor and the
camera bound is radius-derived, not paint-derived.

**Paving cannot be overpainted.** The old rider in entry 19's paving note
said the paving pass must move with lazy paint; it is amended in place. The
resolution is a skip, not a move: `EnsureFloorPainted` lays the plain tile
only where the floor layer holds NOTHING, while site paving lands
unconditionally at `ApplyRuinsOverrides` on both paths -- so the two sides
agree whichever runs first, fresh or load.

**The feather band.** `ApplyRoadFogFade` is the one place a cell shows
through fog WITHOUT being revealed; it now calls `EnsureFloorPainted` for
every cell it thins below solid alpha, so the gauze thins onto ground, not
void. Solid-alpha cells are skipped -- painting there would be a leak.

**Eviction: never.** Paint-once-forever, mirroring the FOG IS ONE-WAY
ruling in `DungeonTerrain`'s header. Revealed-only floor paint also bounds
the floor tilemap's memory below the old full-disc allocation.

**Diagnostics (v1, same delivery).** The bootstrap log line splits the
terrain bucket into fog/floor halves (`floor deferred` on lazy floors);
`DungeonTerrain` carries per-floor counters (painted-on-reveal,
skipped-existing, outside-disc, cumulative reveal-paint ms); and
`Commands -> Validate Reveal Consistency` gains two audits that WEIGH INTO
its FAIL verdict -- a revealed cell with no floor tile (the miss class) and
the plain disc tile under solid fog on a lazy floor (the leak class; paving
under fog is legitimate and exempt, as are feather cells by their alpha) --
plus a per-floor counters line. Declared-but-uncalled members: none; every
added accessor is consumed by the bootstrap log or the validator in this
same delivery.

**Key files:** `DungeonCore/DungeonTerrain.cs` (the lazy flag,
`PaintDiscLayer`, `EnsureFloorPainted`, counters), `Floors/FloorRoot.cs`
(the split log line), `Floors/TerrainFeatureGenerator.cs` (the feather
call, the amended paving note, the validator audits). Entry 19's paving
note carries the in-place amendment; the guide is
`Docs/DCR_Guide_Lazy_Floor_Paint.html`."""

CANON_APP_OLD = """files are listed at the end of its own section above.


# APPENDIX"""

CANON_APP_NEW = ("files are listed at the end of its own section above.\n\n"
                 "---\n\n" + ENTRY_47 + "\n\n\n# APPENDIX")

canon.replace_once(CANON_APP_OLD, CANON_APP_NEW, "canon entry 47 inserted")

# ========================================================= guide file =====

GUIDE_REL = os.path.join("Docs", "DCR_Guide_Lazy_Floor_Paint.html")
GUIDE_PATH = os.path.join(REPO, GUIDE_REL)
if os.path.exists(GUIDE_PATH):
    sys.exit("[deliver] Idempotency guard: %s already exists. Tree "
             "untouched." % GUIDE_REL)

GUIDE_HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>DCR Guide &mdash; Lazy Floor Paint (Canon 47)</title>
<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@600;800&family=Crimson+Text:ital@0;1&family=JetBrains+Mono:wght@400;600&display=swap" rel="stylesheet">
<style>
:root{--bg:#0d0d1a;--accent:#e94560;--gold:#c8902a;--ink:#d8d4c8;--dim:#8a8698;--panel:#14142a;--line:#2a2a45;}
*{box-sizing:border-box;}
body{margin:0;background:var(--bg);color:var(--ink);font-family:'Crimson Text',serif;font-size:18px;line-height:1.55;}
header{padding:34px 22px 18px;border-bottom:1px solid var(--line);}
h1{font-family:Cinzel,serif;font-weight:800;color:var(--gold);margin:0 0 6px;font-size:30px;letter-spacing:1px;}
.sub{color:var(--dim);font-style:italic;margin:0;}
.progress-wrap{position:sticky;top:0;z-index:9;background:var(--bg);border-bottom:1px solid var(--line);padding:8px 22px;display:flex;align-items:center;gap:14px;}
.progress-track{flex:1;height:8px;background:var(--panel);border:1px solid var(--line);border-radius:5px;overflow:hidden;}
.progress-bar{height:100%;width:0%;background:var(--accent);transition:width .25s;}
.progress-label{font-family:'JetBrains Mono',monospace;font-size:12px;color:var(--dim);min-width:64px;text-align:right;}
.reset-btn{font-family:'JetBrains Mono',monospace;font-size:12px;background:none;border:1px solid var(--line);color:var(--dim);padding:4px 10px;border-radius:4px;cursor:pointer;}
.reset-btn:hover{color:var(--accent);border-color:var(--accent);}
main{max-width:900px;margin:0 auto;padding:20px 22px 80px;}
details.chapter{background:var(--panel);border:1px solid var(--line);border-radius:8px;margin:18px 0;padding:0;}
details.chapter>summary{cursor:pointer;list-style:none;font-family:Cinzel,serif;font-weight:600;color:var(--accent);font-size:20px;padding:14px 18px;}
details.chapter>summary::-webkit-details-marker{display:none;}
details.chapter>summary::before{content:"\25B8\00a0";color:var(--gold);}
details[open].chapter>summary::before{content:"\25BE\00a0";}
.chap-body{padding:2px 20px 18px;border-top:1px solid var(--line);}
h3{font-family:Cinzel,serif;color:var(--gold);font-size:16px;margin:20px 0 6px;letter-spacing:.5px;}
code{font-family:'JetBrains Mono',monospace;font-size:14.5px;color:var(--gold);background:#0a0a14;padding:1px 5px;border-radius:3px;}
pre{background:#0a0a14;border:1px solid var(--line);border-radius:6px;padding:12px 14px;overflow-x:auto;font-family:'JetBrains Mono',monospace;font-size:13.5px;line-height:1.5;color:#c8c4d8;}
table{border-collapse:collapse;margin:12px 0;font-size:16px;}
th,td{border:1px solid var(--line);padding:6px 12px;text-align:right;}
th:first-child,td:first-child{text-align:left;}
th{font-family:Cinzel,serif;color:var(--gold);font-weight:600;font-size:13px;}
.callout{border-left:3px solid var(--accent);background:#191930;padding:10px 14px;margin:14px 0;font-size:16.5px;}
.step{display:flex;gap:10px;align-items:flex-start;margin:10px 0;padding:9px 12px;background:#101024;border:1px solid var(--line);border-radius:6px;}
.step input{margin-top:6px;accent-color:var(--accent);width:16px;height:16px;flex:none;}
.step label{cursor:pointer;}
.fork b{color:var(--accent);}
.dim{color:var(--dim);}
</style>
</head>
<body>
<header>
  <h1>Lazy Floor Paint</h1>
  <p class="sub">Canon 47 &mdash; the floor disc stops paying its paint at creation and pays it as fog lifts.</p>
</header>
<div class="progress-wrap">
  <div class="progress-track"><div class="progress-bar" id="bar"></div></div>
  <span class="progress-label" id="lbl">0 / 0</span>
  <button class="reset-btn" id="reset" type="button">reset checkmarks</button>
</div>
<main>

<details class="chapter" open><summary>1. The Measurement</summary><div class="chap-body">
<p>Re-measured at HEAD with <code>FloorRoot.LogBootstrapTimings</code> via <code>Commands &rarr; Test Generate All Floors</code>, correcting the remembered 99.5%:</p>
<table>
<tr><th>floor</th><th>radius</th><th>terrain (paint)</th><th>bootstrap total</th><th>share</th></tr>
<tr><td>2</td><td>150</td><td>230 ms</td><td>305 ms</td><td>75%</td></tr>
<tr><td>3</td><td>250</td><td>516 ms</td><td>556 ms</td><td>93%</td></tr>
<tr><td>4</td><td>400</td><td>1220 ms</td><td>1288 ms</td><td>95%</td></tr>
<tr><td>5</td><td>600</td><td>2855 ms</td><td>3047 ms</td><td>94%</td></tr>
</table>
<p><code>GenerateAt</code> is radius resolve plus <code>PaintTerrain</code> and nothing else, so the terrain bucket <em>is</em> the disc paint: two symmetric banded <code>SetTilesBlock</code> passes, floor and fog. The fog pass cannot defer (next chapter), so this delivery removes the <em>floor half</em> &mdash; expect floor 5's creation to land somewhere near <b>1.5&ndash;1.8&nbsp;s</b>, with the exact split printed by the new log line rather than estimated.</p>
</div></details>

<details class="chapter"><summary>2. The Rule and the Five Locked Forks</summary><div class="chap-body">
<p class="callout"><b>The rule, one sentence:</b> the fog disc still paints in full at <code>GenerateAt</code>; the floor tile is laid per cell inside <code>RevealTile</code>, the moment fog lifts, and nowhere else.</p>
<p class="fork"><b>1. Trigger &mdash; on reveal.</b> Every reveal path already funnels through <code>RevealTile</code>: claims, the starter blob and its border, mining halos, feature reveals, and the load restore sweeps (silent <code>ClaimTile</code>, the mined sweep, <code>UnfogAllRevealedFeatures</code>). Rivers and roads already paint lazily on reveal with load-path repaint sweeps, so the floor tile joins the house pattern. Camera-proximity and chunk-on-demand were rejected: fog hides unrevealed pixels, so both paint invisible work.</p>
<p class="fork"><b>2. Fog stays eager.</b> Fog-tile <em>absence</em> is the revealed flag &mdash; <code>FloorRoot.IsRevealed</code> reads it, and ReachabilityDirector, the minimap and <code>DeadCoreSaturation</code> read that. Fog is also the per-cell tint substrate for the road feather and the floor-0 rim gloom. Re-founding IsRevealed on a set was rejected as reveal-semantics surgery for the remaining half.</p>
<p class="fork"><b>3. Floor 0 stays fully eager.</b> Its rim facade unfogs around <code>RevealTile</code> and <code>PaintRimGround</code> deliberately clears rim floor tiles. This also stays clear of entry 24's standing rejection of staged tile painting, which is about the surface bands where the confiner ceiling is the only void guard (Appendix C); the disc differs because fog covers everything unrevealed and the camera bound is radius-derived.</p>
<p class="fork"><b>4. Load is lazy by the same single rule.</b> The restore sweeps re-reveal every revealed cell through <code>RevealTile</code>, which repaints exactly that set. Order against paving is proof by construction: paving overwrites unconditionally at <code>ApplyRuinsOverrides</code>, reveal paint skips any cell already holding a floor-layer tile.</p>
<p class="fork"><b>5. Eviction: never.</b> Paint-once-forever, mirroring the FOG IS ONE-WAY ruling in <code>DungeonTerrain</code>'s header.</p>
<h3>Walkability is untouched, by construction</h3>
<p>Walkability is born in <code>TileInfluenceManager</code>'s claimed/mined sets and read by <code>DungeonPathfinder.IsWalkable</code>; no tilemap is consulted anywhere on that path. This change moves pixels only.</p>
</div></details>

<details class="chapter"><summary>3. What the Script Changes</summary><div class="chap-body">
<h3>DungeonCore/DungeonTerrain.cs</h3>
<p><code>GenerateAt</code> sets <code>lazyFloorPaint</code> (every floor but 0). <code>PaintTerrain</code> becomes two calls to a new single-layer <code>PaintDiscLayer</code> &mdash; fog always, floor only when eager &mdash; timed apart when <code>LogBootstrapTimings</code> is on. <code>RevealTile</code> gains one line: <code>EnsureFloorPainted(pos)</code>, which no-ops on eager floors and outside the disc, skips any cell already holding a floor-layer tile (the paving rule), lays the plain tile otherwise, and keeps four counters plus a cumulative-ms tally. Accessors expose the lot to the log line and the validator.</p>
<h3>Floors/FloorRoot.cs</h3>
<p>The bootstrap log line splits its terrain bucket: <code>terrain N ms (fog X ms, floor Y ms)</code>, printing <code>floor deferred</code> on lazy floors.</p>
<h3>Floors/TerrainFeatureGenerator.cs</h3>
<p><code>ApplyRoadFogFade</code> calls <code>EnsureFloorPainted</code> for every cell it thins below solid alpha &mdash; the one place a cell shows through fog without being revealed &mdash; so the gauze thins onto ground, not void. The old paving NOTE is resolved in place. <code>BuildRevealConsistencyReport</code> gains two audits that weigh into FAIL &mdash; <em>revealed cell with no floor tile</em> (miss) and <em>plain disc tile under solid fog</em> (leak; paving under fog is exempt, feather cells exempt by alpha) &mdash; plus a per-floor counters line.</p>
<h3>Docs/DESIGN_CANON.md</h3>
<p>Entry 47 inserted before the Appendix; entry 19's paving rider struck and amended in place; TOC gains 45/46 (drift hygiene) and 47.</p>
</div></details>

<details class="chapter"><summary>4. Apply</summary><div class="chap-body">
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s01"><label for="dcr-lazypaint-v1-s01">Working tree clean on <code>main</code> (<code>git status</code>).</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s02"><label for="dcr-lazypaint-v1-s02">Run <code>python deliver_lazy_floor_paint.py</code> from anywhere inside the checkout (or pass the repo path as argument 1 / set <code>DCR_REPO</code>). Expect the edit list and <code>APPLIED</code>; any anchor mismatch aborts with the tree untouched.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s03"><label for="dcr-lazypaint-v1-s03">Review <code>git diff</code> &mdash; three C# files, the canon, and the new guide under <code>Docs/</code>.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s04"><label for="dcr-lazypaint-v1-s04">Commit and push.</label></div>
<p class="dim">Rollback at any point: <code>git checkout -- Assets/Scripts/DungeonCore/DungeonTerrain.cs Assets/Scripts/Floors/FloorRoot.cs Assets/Scripts/Floors/TerrainFeatureGenerator.cs Docs/DESIGN_CANON.md</code> and delete <code>Docs/DCR_Guide_Lazy_Floor_Paint.html</code>. A second run of the script aborts on its idempotency guard rather than double-applying.</p>
</div></details>

<details class="chapter"><summary>5. Verify in Unity</summary><div class="chap-body">
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s05"><label for="dcr-lazypaint-v1-s05">Scripts recompile clean.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s06"><label for="dcr-lazypaint-v1-s06">On a throwaway save: <code>Commands &rarr; Test Generate All Floors</code>. Each deep floor's line now reads <code>terrain N ms (fog X ms, floor deferred)</code>, with the terrain bucket landing near the fog half of chapter 1's table (floor 5 roughly 1.3&ndash;1.6&nbsp;s instead of 2.9&nbsp;s).</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s07"><label for="dcr-lazypaint-v1-s07"><code>Commands &rarr; Validate Reveal Consistency</code>: PASS on every floor, and each floor prints its <code>lazy floor paint</code> counters line. Visited deep floors show <code>painted-on-reveal</code> &gt; 0 (the starter cavern and its border).</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s08"><label for="dcr-lazypaint-v1-s08">Claim and mine outward on floor 2 for a minute: ground appears exactly as before, wall caps sit on ground (the revealed border paints too), no black squares.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s09"><label for="dcr-lazypaint-v1-s09">Reveal a road stretch: the fog feather past the frontier thins onto ground, not onto void.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s10"><label for="dcr-lazypaint-v1-s10">Save, load, run <code>Validate Reveal Consistency</code> again: PASS, and everything previously revealed is visibly floored (the restore sweeps repaint it).</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s11"><label for="dcr-lazypaint-v1-s11">Floor 0 unchanged, rim included: its line prints a real floor timing, not <code>deferred</code>.</label></div>
<div class="step"><input type="checkbox" id="dcr-lazypaint-v1-s12"><label for="dcr-lazypaint-v1-s12">Paste the new Test Generate All Floors block back to the session for the before/after record.</label></div>
</div></details>

<details class="chapter"><summary>6. Diagnostics Reference</summary><div class="chap-body">
<p><b>painted-on-reveal</b> &mdash; cells whose plain floor tile was laid by the reveal hook. <b>skipped-existing</b> &mdash; reveals that found a tile already there (paving, or a cell revealed twice via overlapping halos). <b>outside-disc</b> &mdash; halo reveals past the disc edge; a no-op, mirroring fog. <b>reveal-paint total</b> &mdash; cumulative ms inside <code>EnsureFloorPainted</code> since the floor was created or loaded.</p>
<p><b>FAIL classes added to the validator:</b> <em>revealed cell with NO floor tile</em> means some path unfogged without going through <code>RevealTile</code> on a lazy floor &mdash; the miss class this whole design guards against; <em>plain floor tile under SOLID fog</em> means something painted without revealing &mdash; the leak class. Both print sample coordinates under the existing detail format.</p>
</div></details>

<details class="chapter"><summary>7. Update the Canon</summary><div class="chap-body">
<p>All three canon edits ride the delivery script &mdash; nothing manual. For the record, they are: the TOC gains lines 45 and 46 (drift hygiene; the headers existed, the contents list had stopped at 44) and line 47; entry 19's paving rider is struck in place and resolved (<em>&ldquo;the paving pass stays exactly here; lazy reveal paint skips any cell that already holds a floor-layer tile&rdquo;</em>); and the full entry below lands before the Appendix.</p>
<pre id="canon-entry"></pre>
</div></details>

</main>
<script>
(function(){
  var P='dcr-lazypaint-v1-';
  var boxes=Array.prototype.slice.call(document.querySelectorAll('.step input[type=checkbox]'));
  var bar=document.getElementById('bar');
  var lbl=document.getElementById('lbl');
  function refresh(){
    var done=0;
    for(var i=0;i<boxes.length;i++){ if(boxes[i].checked) done++; }
    var pct=boxes.length?Math.round(100*done/boxes.length):0;
    bar.style.width=pct+'%';
    lbl.textContent=done+' / '+boxes.length;
  }
  for(var i=0;i<boxes.length;i++){
    (function(b){
      try{ if(localStorage.getItem(b.id)==='1') b.checked=true; }catch(e){}
      b.addEventListener('change',function(){
        try{ localStorage.setItem(b.id, b.checked?'1':'0'); }catch(e){}
        refresh();
      });
    })(boxes[i]);
  }
  document.getElementById('reset').addEventListener('click',function(){
    for(var i=0;i<boxes.length;i++){
      boxes[i].checked=false;
      try{ localStorage.removeItem(boxes[i].id); }catch(e){}
    }
    refresh();
  });
  var pre=document.getElementById('canon-entry');
  if(pre&&window.CANON_47) pre.textContent=window.CANON_47;
  refresh();
})();
</script>
<script id="canon-src">
window.CANON_47 = __CANON_47_JSON__;
</script>
</body>
</html>
"""

# Embed the canon entry into the guide as a JS string so the two can never
# drift apart -- the guide shows exactly what the script inserted.
import json
GUIDE_HTML = GUIDE_HTML.replace("__CANON_47_JSON__", json.dumps(ENTRY_47))

# ---------------------------------------------------- staged validation ---

ascii_only(DT_FIELDS_NEW, "DT fields")
ascii_only(DT_GEN_NEW, "DT GenerateAt")
ascii_only(DT_PAINT_NEW, "DT PaintTerrain")
ascii_only(DT_REVEAL_NEW, "DT RevealTile")
ascii_only(FR_LOG_NEW, "FloorRoot log")
ascii_only(TFG_FEATHER_NEW, "TFG feather")
ascii_only(TFG_NOTE_NEW, "TFG note")
ascii_only(TFG_DECL_NEW, "TFG decls")
ascii_only(TFG_SWEEP_NEW, "TFG sweep")
ascii_only(TFG_BAD_NEW, "TFG verdict")
ascii_only(CANON_TOC_NEW, "canon TOC")
ascii_only(CANON_RIDER_NEW, "canon rider")
ascii_only(ENTRY_47, "canon entry 47")

balance_csharp(dt.text, dt.rel)
balance_csharp(fr.text, fr.rel)
balance_csharp(tfg.text, tfg.rel)

# Post-stage symbol presence -- the cheap invented-API tripwire before the
# compiler pass gets its turn.
dt.must_count("EnsureFloorPainted(", 2, "DT EnsureFloorPainted def+call")
dt.must_count("PaintDiscLayer(", 3, "DT PaintDiscLayer def+2 calls")
dt.must_count("public bool LazyFloorPaint", 1, "DT LazyFloorPaint accessor")
fr.must_count("terrain.LazyFloorPaint", 1, "FR reads LazyFloorPaint")
tfg.must_count("terrain.EnsureFloorPainted(c)", 1, "TFG feather call")
tfg.must_count("revealedNoFloor", 4, "TFG miss audit wired")
tfg.must_count("plainFloorUnderFog", 4, "TFG leak audit wired")
canon.must_count("## 47. Lazy Floor Paint", 1, "canon 47 header")
canon.must_count("47. Lazy Floor Paint (Deferred Disc Painting)", 2,
                 "canon 47 TOC + header")
canon.must_count("~~if the lazy floor-paint", 1, "canon rider struck")

# --------------------------------------------------------------- write ----

targets = [dt, fr, tfg, canon]
for t in targets:
    with open(t.path, "wb") as f:
        f.write(t.staged_bytes())
with open(GUIDE_PATH, "wb") as f:
    f.write(GUIDE_HTML.encode("utf-8"))

note("  new   %-46s %s" % ("guide written", GUIDE_REL))

# -------------------------------------------------------------- report ----

print("[deliver] Lazy Floor Paint (canon 47) -- APPLIED")
print("[deliver] repo: " + REPO)
for line in LOG:
    print(line)
print("[deliver] Files touched: DungeonTerrain.cs, FloorRoot.cs, "
      "TerrainFeatureGenerator.cs, DESIGN_CANON.md (+ new guide).")
print("[deliver] Next: open Unity, let scripts compile, then follow the "
      "guide's Verify chapter (Test Generate All Floors, Validate Reveal "
      "Consistency).")
