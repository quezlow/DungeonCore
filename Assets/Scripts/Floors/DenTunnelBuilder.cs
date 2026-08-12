using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One tunnel leaving the den: a straight run with two named ends.</summary>
public class DenTunnelRun
{
    public Vector3Int a;              // the den end
    public Vector3Int b;              // a chamber centre, or the dead stop
    public int chamberId = -1;        // -1 when this run ends in the rock
    public bool DeadEnd => chamberId < 0;
    public int width = 3;
    public int tipWidth = 2;
}

/// <summary>A chosen network, before a single cell is drawn.</summary>
public class DenTunnelPlan
{
    public bool valid;
    public Vector3Int centre;
    public Vector3Int den;            // the den anchor, always inside the band
    public int bandInner, bandOuter;
    public DenTunnelFloorEntry entry;
    public List<DenTunnelRun> runs = new List<DenTunnelRun>();

    public int ChamberLinks
    {
        get { int n = 0; for (int i = 0; i < runs.Count; i++) if (!runs[i].DeadEnd) n++; return n; }
    }
    public int DeadEnds => runs.Count - ChamberLinks;
}

/// <summary>
/// Chooses a floor's den tunnels WITHOUT drawing any of them -- the
/// RoadNetworkBuilder Plan/Rasterise split, adopted wholesale because it earned
/// itself there: a run with an exact direction and two named ends can be
/// negotiated against chambers, the road and the landing BEFORE anything is
/// rasterised, whereas a bag of cells has to have its direction estimated and
/// its ownership settled after the fact.
///
/// THE SHAPE, and why it is this one. Canon 42 first agreed that tunnels would
/// link chambers and stay inside canon 19's 15-65 per cent band. They cannot do
/// both: GenerateChambers places chambers UNIFORMLY across the disc and says so
/// in its own comment, so measured over 2000 seeds floor index 1 has fewer than
/// TWO in-band chambers on 30.8 per cent of them. Three shapes were measured in
/// Tools/sim_den_tunnels.py before any of this was written:
///
///   A  link the nearest chambers, band or not -- 17-18 per cent of tunnel
///      length falls outside the band and the worst endpoint reaches 0.96 of
///      the radius, which is inside the bedrock rim's approach.
///   D  a self-contained in-band network, chambers joined opportunistically --
///      robust and pointless: it touches NO chamber on some 40 per cent of
///      seeds.
///   E  a FIXED number of runs; each takes a chamber if one is in range and
///      ends in the rock if none is. Cannot starve by construction, because
///      chamber count changes the FLAVOUR of a network rather than whether one
///      exists.
///
/// E ships. A dead end is content, not failure: it reads as an unfinished dig,
/// and on an Excavator floor it is exactly what the population extends.
///
/// One implementation note worth keeping, because it cost a measurable amount
/// and would be undone by anyone tidying: runs choose their chambers
/// NEAREST-FIRST, not by assigned bearing. Bearing-first was written and
/// dropped -- it discarded a perfectly good chamber for sitting off its run's
/// heading, which cost floor index 1 a chamber link on a quarter of seeds and
/// bought nothing.
/// </summary>
public static class DenTunnelBuilder
{
    /// <summary>
    /// Picks the den anchor and its runs. Deterministic from the rng handed in,
    /// so generation and any headless report agree cell for cell.
    /// </summary>
    /// <param name="chamberCentres">Chamber centres in WORLD cells, index-aligned
    /// with chamberIds.</param>
    /// <param name="landing">The stair-landing cell tunnels must keep clear of.</param>
    public static DenTunnelPlan Plan(
        System.Random rng, Vector3Int centre, int radius,
        DenTunnelFloorEntry entry, int coreExclusionRadius,
        IReadOnlyList<Vector3Int> chamberCentres, IReadOnlyList<int> chamberIds,
        Vector3Int landing, int starterRoomRadius)
    {
        var plan = new DenTunnelPlan { centre = centre, entry = entry };
        if (rng == null || entry == null || radius <= 0) return plan;

        int inner = Mathf.Max(coreExclusionRadius + 2, Mathf.RoundToInt(radius * entry.bandInner));
        int outer = Mathf.RoundToInt(radius * entry.bandOuter);
        if (outer <= inner) return plan;
        plan.bandInner = inner;
        plan.bandOuter = outer;

        int keepClear = starterRoomRadius + Mathf.Max(0, entry.landingKeepClear);

        // -- The den anchor. Uniform in the band, rejecting anything on the
        //    landing. 96 samples is TryPickAnchor's own budget.
        Vector3Int den = default(Vector3Int);
        bool found = false;
        for (int i = 0; i < 96 && !found; i++)
        {
            int dx = rng.Next(-outer, outer + 1);
            int dy = rng.Next(-outer, outer + 1);
            int d2 = dx * dx + dy * dy;
            if (d2 < inner * inner || d2 > outer * outer) continue;
            var c = new Vector3Int(centre.x + dx, centre.y + dy, 0);
            if (ChebyshevOrEuclid(c, landing) < keepClear) continue;
            den = c;
            found = true;
        }
        if (!found) return plan;
        plan.den = den;
        plan.valid = true;

        // -- Eligible chambers, nearest first. A chamber inside minRunCells IS
        //    the den for these purposes and is skipped rather than tunnelled to.
        float clampR = radius * Mathf.Clamp01(entry.endpointClamp);
        float maxRun = radius * Mathf.Max(0.01f, entry.maxRunFraction);
        var eligible = new List<int>();
        if (chamberCentres != null)
        {
            for (int i = 0; i < chamberCentres.Count; i++)
            {
                var c = chamberCentres[i];
                if (Dist(c, centre) > clampR) continue;
                float d = Dist(c, den);
                if (d < entry.minRunCells || d > maxRun) continue;
                if (SegmentTooCloseTo(den, c, landing, keepClear)) continue;
                eligible.Add(i);
            }
            eligible.Sort((x, y) => Dist(chamberCentres[x], den)
                                    .CompareTo(Dist(chamberCentres[y], den)));
        }

        int runs = Mathf.Max(1, entry.runCount);
        int taken = Mathf.Min(runs, eligible.Count);

        for (int i = 0; i < taken; i++)
        {
            int ci = eligible[i];
            plan.runs.Add(new DenTunnelRun
            {
                a = den,
                b = chamberCentres[ci],
                chamberId = (chamberIds != null && ci < chamberIds.Count) ? chamberIds[ci] : ci,
                width = entry.width,
                tipWidth = entry.tipWidth,
            });
        }

        // -- Dead ends fill the remainder, on bearings spread away from the
        //    runs already taken so a den does not drive two tunnels into the
        //    same rock. A bearing that would cross the landing or leave the
        //    clamp is RETRIED rather than abandoned: dropping it silently gave
        //    a den fewer runs than the profile authored, which is invisible in
        //    the inspector and reads as the generator having quietly failed.
        int wanted = runs - taken;
        for (int i = 0; i < wanted; i++)
        {
            int lo = Mathf.Max(1, Mathf.Min(entry.deadEndMin, entry.deadEndMax));
            int hi = Mathf.Max(lo, Mathf.Max(entry.deadEndMin, entry.deadEndMax));

            bool placed = false;
            for (int attempt = 0; attempt < 16 && !placed; attempt++)
            {
                double bearing = PickFreeBearing(rng, plan.runs, den);
                int len = Mathf.Min(rng.Next(lo, hi + 1), Mathf.RoundToInt(maxRun));

                // Shorten rather than surrender: a bearing blocked at full
                // length is usually fine at half, and a short dead end is a
                // better answer than a missing tunnel.
                for (int shrink = 0; shrink < 3 && !placed; shrink++)
                {
                    int tryLen = Mathf.Max(entry.minRunCells, len >> shrink);
                    var stop = new Vector3Int(
                        den.x + (int)Math.Round(tryLen * Math.Cos(bearing)),
                        den.y + (int)Math.Round(tryLen * Math.Sin(bearing)), 0);

                    if (Dist(stop, centre) > clampR) continue;
                    if (SegmentTooCloseTo(den, stop, landing, keepClear)) continue;

                    plan.runs.Add(new DenTunnelRun
                    {
                        a = den, b = stop, chamberId = -1,
                        width = entry.width, tipWidth = entry.tipWidth,
                    });
                    placed = true;
                }
            }
        }

        return plan;
    }

    /// <summary>A bearing at least 40 degrees off every run already planned, or
    /// a free sample when none can be found in budget.</summary>
    private static double PickFreeBearing(System.Random rng, List<DenTunnelRun> taken, Vector3Int den)
    {
        const double MinSeparation = 40.0 * Math.PI / 180.0;
        double best = rng.NextDouble() * 2.0 * Math.PI;
        double bestWorst = -1.0;
        for (int i = 0; i < 24; i++)
        {
            double cand = rng.NextDouble() * 2.0 * Math.PI;
            double worst = Math.PI;
            for (int r = 0; r < taken.Count; r++)
            {
                var run = taken[r];
                double ang = Math.Atan2(run.b.y - den.y, run.b.x - den.x);
                double off = Math.Abs(NormaliseAngle(cand - ang));
                if (off < worst) worst = off;
            }
            if (worst >= MinSeparation) return cand;
            if (worst > bestWorst) { bestWorst = worst; best = cand; }
        }
        return best;
    }

    private static double NormaliseAngle(double a)
    {
        while (a > Math.PI) a -= 2.0 * Math.PI;
        while (a < -Math.PI) a += 2.0 * Math.PI;
        return a;
    }

    private static float Dist(Vector3Int a, Vector3Int b)
    {
        float dx = a.x - b.x, dy = a.y - b.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static float ChebyshevOrEuclid(Vector3Int a, Vector3Int b) => Dist(a, b);

    /// <summary>Distance from a point to the segment ab, against a keep-clear.
    /// The landing test has to be on the SEGMENT rather than on either end: a
    /// run whose two ends both clear the starter blob can still drive straight
    /// through it.</summary>
    private static bool SegmentTooCloseTo(Vector3Int a, Vector3Int b, Vector3Int p, int keep)
    {
        float dx = b.x - a.x, dy = b.y - a.y;
        if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
            return Dist(a, p) < keep;
        float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy);
        t = Mathf.Clamp01(t);
        float cx = a.x + t * dx, cy = a.y + t * dy;
        float ex = p.x - cx, ey = p.y - cy;
        return Mathf.Sqrt(ex * ex + ey * ey) < keep;
    }

    // ---- Rasterise --------------------------------------------------

    /// <summary>
    /// Draws a chosen plan: one wobbling, tapering polyline per run.
    ///
    /// Consumes rng ONLY for the wobble, exactly as RoadNetworkBuilder.Rasterise
    /// consumes it only for the meander -- so a caller that hands the same
    /// Random to Plan and then to this gets the shipped behaviour, and a caller
    /// that wants to re-draw a saved plan can do so without touching Plan.
    /// </summary>
    public static List<DenTunnelData> Rasterise(
        System.Random rng, DenTunnelPlan plan, int clampRadius, int wobbleStep,
        float wobbleAmplitude)
    {
        var result = new List<DenTunnelData>();
        if (rng == null || plan == null || !plan.valid) return result;

        for (int i = 0; i < plan.runs.Count; i++)
        {
            var run = plan.runs[i];
            var data = new DenTunnelData
            {
                id = i,
                chamberId = run.chamberId,
                width = run.width,
                tipWidth = run.tipWidth,
                segmentLength = plan.entry != null ? plan.entry.segmentLength : 40,
                floorCentre = SerializableVector3Int.From(plan.centre),
                clampRadius = clampRadius,
            };
            foreach (var p in Wobble(rng, run.a, run.b, wobbleStep, wobbleAmplitude))
                data.polyline.Add(SerializableVector3Int.From(p));
            result.Add(data);
        }
        return result;
    }

    /// <summary>
    /// The ordered, de-duplicated centreline of one run, with its taper already
    /// decided per cell. Deterministic from the STORED polyline alone -- which
    /// is why den tunnel cells are never persisted, exactly as road cells are
    /// not: the polyline plus the two widths rebuilds them, and one shared
    /// rasteriser serves generation and load so the two can never disagree.
    /// </summary>
    public static List<Vector3Int> Centreline(DenTunnelData tunnel)
    {
        var line = new List<Vector3Int>();
        if (tunnel == null || tunnel.polyline == null || tunnel.polyline.Count == 0)
            return line;

        var seen = new HashSet<Vector3Int>();
        for (int i = 0; i < tunnel.polyline.Count - 1; i++)
        {
            var a = tunnel.polyline[i].ToVector3Int();
            var b = tunnel.polyline[i + 1].ToVector3Int();
            foreach (var p in RoadNetworkBuilder.Line(a, b))
                if (seen.Add(p)) line.Add(p);
        }
        if (tunnel.polyline.Count == 1)
        {
            var only = tunnel.polyline[0].ToVector3Int();
            if (seen.Add(only)) line.Add(only);
        }
        return line;
    }

    /// <summary>
    /// Every cell of one run: the centreline dilated to a width that tapers from
    /// the mouth to the tip.
    ///
    /// A road dilates at ONE width and can call RoadNetworkBuilder.Dilate once.
    /// A tunnel narrows as it goes -- the core cavern's own tunnels have done
    /// since they shipped -- so it dilates per centreline cell at that cell's
    /// own width. Same square brush, same clamp; only the width varies.
    /// </summary>
    public static HashSet<Vector3Int> Cells(DenTunnelData tunnel)
    {
        var cells = new HashSet<Vector3Int>();
        if (tunnel == null) return cells;

        var line = Centreline(tunnel);
        if (line.Count == 0) return cells;

        var centre = tunnel.floorCentre != null
            ? tunnel.floorCentre.ToVector3Int() : Vector3Int.zero;

        int mouth = Mathf.Max(1, tunnel.width);
        int tip = Mathf.Max(1, tunnel.tipWidth);

        for (int i = 0; i < line.Count; i++)
        {
            float t = line.Count > 1 ? i / (float)(line.Count - 1) : 0f;
            int w = Mathf.Max(tip, Mathf.RoundToInt(Mathf.Lerp(mouth, tip, t)));
            foreach (var p in RoadNetworkBuilder.Dilate(
                         new[] { line[i] }, w, centre, tunnel.clampRadius))
                cells.Add(p);
        }
        return cells;
    }

    /// <summary>A wobbling polyline from a to b. The drift is perpendicular and
    /// bounded, so an endpoint never moves: a run that wandered off its chamber
    /// would be a run that links nothing, and the whole shape rests on knowing
    /// which chamber a run reaches.</summary>
    private static List<Vector3Int> Wobble(
        System.Random rng, Vector3Int a, Vector3Int b, int step, float amplitude)
    {
        var line = new List<Vector3Int> { a };
        if (step <= 0 || amplitude <= 0f) { line.Add(b); return line; }

        float dx = b.x - a.x, dy = b.y - a.y;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < step * 2) { line.Add(b); return line; }

        float px = -dy / len, py = dx / len;
        int knots = Mathf.Max(1, Mathf.RoundToInt(len / step));

        for (int k = 1; k < knots; k++)
        {
            float t = k / (float)knots;
            // Taper the drift to zero at both ends so the mouth leaves the den
            // straight and the tip arrives on its chamber straight.
            float taper = Mathf.Sin(t * Mathf.PI);
            float off = ((float)rng.NextDouble() * 2f - 1f) * amplitude * taper;
            line.Add(new Vector3Int(
                Mathf.RoundToInt(a.x + dx * t + px * off),
                Mathf.RoundToInt(a.y + dy * t + py * off), 0));
        }
        line.Add(b);
        return line;
    }
}
