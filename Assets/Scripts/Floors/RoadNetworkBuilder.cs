using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What one Build call produced. Junctions are reported for tooling only
/// (the headless road report draws them); nothing persists them.</summary>
public class RoadNetworkResult
{
    public List<RoadData> roads = new List<RoadData>();
    public List<Vector3Int> junctions = new List<Vector3Int>();
}

/// <summary>
/// The deep-road substrate, as a PURE function of (seed, centre, radius, entry).
///
/// Nothing here touches a scene, a floor, a tilemap or a singleton. That is
/// deliberate: it lets the road report in Commands.cs generate and measure a
/// floor's whole road network without instantiating the floor, which matters a
/// great deal on floor index 4 where the terrain pass alone paints 1.44 million
/// cells.
///
/// DETERMINISM
///   Build consumes the supplied System.Random in a fixed order. On floors with
///   no core cavern and no entrance cave -- every floor below the first -- the
///   generator hands roads a freshly seeded Random, so a headless report seeded
///   with the same floor seed reproduces the in-game network exactly. That does
///   NOT hold for floor index 0, where the cavern and entrance draw first.
///
/// GEOMETRY, AND WHY IT IS NOT BuildRiverPolyline
///   The river polyline picks a rim point, heads across on the opposite bearing
///   and wanders until it leaves the disc. That is right for a river, which may
///   end anywhere, and wrong for a road, which must arrive: a rim-to-rim trunk
///   that wanders out early is not a trunk. Edges here are built between two
///   fixed endpoints with a perpendicular meander that is pinned to zero at both
///   ends, so an edge always lands on its target. The meander is otherwise the
///   same idea at a much lower amplitude.
/// </summary>
public static class RoadNetworkBuilder
{
    // ---- Public API ------------------------------------------------

    /// <summary>
    /// Builds a floor's roads. Returns polylines and metadata only -- no cells.
    /// Cells are rasterised later, by the same Centreline/Dilate pair the load
    /// path uses, so generation and load can never disagree.
    /// </summary>
    public static RoadNetworkResult Build(
        System.Random rng, Vector3Int centre, int radius,
        RoadFloorEntry entry, int coreExclusionRadius)
    {
        var result = new RoadNetworkResult();
        if (rng == null || entry == null || entry.mode == RoadMode.None) return result;

        int usable = Mathf.Max(coreExclusionRadius + 4, radius - Mathf.Max(0, entry.rimMargin));
        if (usable <= coreExclusionRadius) return result;

        switch (entry.mode)
        {
            case RoadMode.Trunk:
                BuildTrunk(rng, centre, usable, entry, coreExclusionRadius, result);
                break;
            case RoadMode.Network:
                BuildNetwork(rng, centre, usable, entry, coreExclusionRadius, result);
                break;
        }

        for (int i = 0; i < result.roads.Count; i++) result.roads[i].id = i;
        return result;
    }

    /// <summary>
    /// The ordered, de-duplicated centreline cells of a road, with the broken-end
    /// gap already removed. Deterministic from the stored polyline alone, which is
    /// why road cells are never persisted.
    /// </summary>
    public static List<Vector3Int> Centreline(RoadData road)
    {
        var line = new List<Vector3Int>();
        if (road == null || road.polyline == null || road.polyline.Count == 0) return line;

        var seen = new HashSet<Vector3Int>();
        for (int i = 0; i < road.polyline.Count - 1; i++)
        {
            var a = road.polyline[i].ToVector3Int();
            var b = road.polyline[i + 1].ToVector3Int();
            foreach (var p in Line(a, b))
                if (seen.Add(p)) line.Add(p);
        }
        if (road.polyline.Count == 1)
        {
            var only = road.polyline[0].ToVector3Int();
            if (seen.Add(only)) line.Add(only);
        }

        // The broken end: the polyline was built in full and these cells are simply
        // never opened. They stay ordinary stone, so the road visibly stops.
        int gap = Mathf.Clamp(road.brokenGapCells, 0, Mathf.Max(0, line.Count - 1));
        if (gap > 0) line.RemoveRange(line.Count - gap, gap);
        return line;
    }

    /// <summary>
    /// Widens a run of centreline cells into a carriageway. Mirrors the river
    /// painter's square dilation so roads and rivers rasterise identically.
    /// Cells outside clampRadius of floorCentre are dropped.
    /// </summary>
    public static HashSet<Vector3Int> Dilate(
        IEnumerable<Vector3Int> centreline, int width, Vector3Int floorCentre, int clampRadius)
    {
        var dilated = new HashSet<Vector3Int>();
        if (centreline == null) return dilated;

        int w = Mathf.Max(1, width);
        int half = (w - 1) / 2;
        int extra = (w - 1) - 2 * half;
        long clampSq = (long)clampRadius * clampRadius;

        foreach (var c in centreline)
            for (int dx = -half; dx <= half + extra; dx++)
                for (int dy = -half; dy <= half + extra; dy++)
                {
                    var p = new Vector3Int(c.x + dx, c.y + dy, 0);
                    long ddx = p.x - floorCentre.x, ddy = p.y - floorCentre.y;
                    if (ddx * ddx + ddy * ddy > clampSq) continue;
                    dilated.Add(p);
                }
        return dilated;
    }

    /// <summary>The junction nodes of a road network, derived from the SAVED
    /// polylines alone. Two road ends meeting inside mergeRadius were one node
    /// before the network was split into edges -- cheaper and more robust than
    /// persisting RoadNetworkResult.junctions, which is tooling-only and never
    /// written to a save.
    ///
    /// This is the ONE derivation. The generator, the load path and the headless
    /// road report all call it, because junction shaping changes which cells are
    /// carriageway: two derivations that disagreed by a cell would repartition
    /// segments differently on load and quietly move ownership under a save.</summary>
    public static List<Vector3Int> JunctionNodes(List<RoadData> roads, int mergeRadius)
    {
        var nodes = new List<Vector3Int>();
        if (roads == null) return nodes;

        var endpoints = new List<Vector3Int>();
        foreach (var road in roads)
        {
            var line = Centreline(road);
            if (line.Count == 0) continue;
            endpoints.Add(line[0]);
            endpoints.Add(line[line.Count - 1]);
        }

        long rSq = (long)mergeRadius * mergeRadius;
        for (int i = 0; i < endpoints.Count; i++)
            for (int j = i + 1; j < endpoints.Count; j++)
            {
                long dx = endpoints[i].x - endpoints[j].x;
                long dy = endpoints[i].y - endpoints[j].y;
                if (dx * dx + dy * dy > rSq) continue;
                if (!nodes.Contains(endpoints[i])) nodes.Add(endpoints[i]);
                break;
            }
        return nodes;
    }

    /// <summary>Rounds the inside corners where roads meet, and returns the cells
    /// ADDED in deterministic order.
    ///
    /// The defect this fixes: Dilate stamps one straight kernel, so two five-wide
    /// carriageways crossing dilate to a roughly nine-by-nine square with square
    /// corners -- a plaza rather than a widened meeting. The fix is a
    /// morphological CLOSING restricted to a box around each node: dilate the
    /// carriageway by a disc of filletRadius, then erode by the same disc. That
    /// fills every concave notch smaller than the disc and leaves convex corners
    /// alone, which is precisely the kerb radius a real junction carries.
    ///
    /// ADDITIVE on purpose. Chamfering the outer corners instead would REMOVE
    /// cells, and road cells regenerate from the polyline on load while the mined
    /// set is restored from the save file -- so every removed cell would come back
    /// mined, revealed and no longer typed as road, drawing as bare floor beside
    /// the carriageway. Adding cannot produce that state.
    ///
    /// Bounded to a node box so a shallow meeting (minJunctionAngleDegrees is 25)
    /// cannot close a long thin wedge far out along two diverging arms. Blocked
    /// cells -- reserved core cells and river water -- are never taken, and the
    /// clamp disc is honoured exactly as Dilate honours it.</summary>
    public static List<Vector3Int> FilletJunctions(
        HashSet<Vector3Int> carriageway, List<Vector3Int> nodes,
        int width, int filletRadius, Vector3Int floorCentre, int clampRadius,
        HashSet<Vector3Int> blocked)
    {
        var added = new List<Vector3Int>();
        if (carriageway == null || nodes == null || filletRadius <= 0) return added;

        int r = filletRadius;
        int rSq = r * r;

        // Disc offsets, built once. This is the structuring element for both
        // halves of the closing, so dilation and erosion cannot drift apart.
        var disc = new List<Vector3Int>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= rSq) disc.Add(new Vector3Int(dx, dy, 0));

        int w = Mathf.Max(1, width);
        int half = (w - 1) / 2;
        int extra = (w - 1) - 2 * half;
        // Candidate box: half the carriageway, plus the disc, plus one cell of
        // slack. Dilation is measured one disc further out again so erosion at
        // the candidate edge sees the same neighbourhood it would see inside.
        int reach = half + extra + r + 1;
        long clampSq = (long)clampRadius * clampRadius;

        var dilated = new HashSet<Vector3Int>();
        var seen = new HashSet<Vector3Int>();

        foreach (var node in nodes)
        {
            dilated.Clear();

            int outer = reach + r;
            for (int dy = -outer; dy <= outer; dy++)
                for (int dx = -outer; dx <= outer; dx++)
                {
                    var p = new Vector3Int(node.x + dx, node.y + dy, 0);
                    if (carriageway.Contains(p)) { dilated.Add(p); continue; }
                    for (int k = 0; k < disc.Count; k++)
                        if (carriageway.Contains(p + disc[k])) { dilated.Add(p); break; }
                }

            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    var p = new Vector3Int(node.x + dx, node.y + dy, 0);
                    if (carriageway.Contains(p)) continue;
                    if (blocked != null && blocked.Contains(p)) continue;
                    if (!seen.Add(p)) continue;          // a shared node box, counted once

                    long ddx = p.x - floorCentre.x, ddy = p.y - floorCentre.y;
                    if (ddx * ddx + ddy * ddy > clampSq) continue;

                    bool eroded = true;
                    for (int k = 0; k < disc.Count; k++)
                        if (!dilated.Contains(p + disc[k])) { eroded = false; break; }
                    if (eroded) added.Add(p);
                }
        }
        return added;
    }

    /// <summary>Bresenham between two cells. Kept here rather than borrowed from the
    /// feature generator so this class stays free of scene-side dependencies.</summary>
    public static IEnumerable<Vector3Int> Line(Vector3Int a, Vector3Int b)
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

    // ---- Truncation around a blocked region ------------------------

    /// <summary>What one truncation pass did, so the generator can report it
    /// rather than leave "the road looks short" to be noticed on screen.</summary>
    public class RoadTruncation
    {
        public int roadsExamined;
        public int roadsTruncated;
        public int roadsDropped;

        /// <summary>Centreline cells removed. MEASURED against the line the clip
        /// actually analysed, not predicted from a run length -- the re-drawn
        /// polyline is allowed to differ from the kept run by a cell.</summary>
        public int centrelineCellsRemoved;

        /// <summary>One line per road that changed, for the log.</summary>
        public readonly List<string> notes = new List<string>();

        public bool AnythingChanged => roadsTruncated > 0 || roadsDropped > 0;

        public string Summary() =>
            $"examined {roadsExamined} road(s): {roadsTruncated} truncated, " +
            $"{roadsDropped} dropped, {centrelineCellsRemoved} centreline cells removed";
    }

    /// <summary>A run shorter than the road is wide is not a road, it is a
    /// smear. Runs at or above this survive as fragments, which is deliberate --
    /// a severed spur reading as a stub of dead network is correct on floor
    /// index 4.</summary>
    private const int MinSurvivingRunFactor = 1;

    /// <summary>
    /// Clips every road out of a blocked region, keeping each road's LONGEST
    /// unblocked run (ties to the earlier one). Pure: takes cells and polylines,
    /// touches no scene.
    ///
    /// THE ANALYSIS RUNS ON Centreline(road), NOT ON THE RAW POLYLINE, and that
    /// is a correctness requirement rather than convenience. Centreline applies
    /// brokenGapCells by trimming the TAIL of the whole line, and floor index 4's
    /// roads carry a gap of six. Clipping the raw polyline would leave the run
    /// this method measured and the cells that actually rasterise disagreeing by
    /// six cells at the very end -- which is the end the vault is usually at.
    /// Any road this method clips therefore has brokenGapCells ZEROED: the gap is
    /// already spent in `line`, and re-applying it would trim the kept run twice.
    /// The vault wall is the road's stated reason for stopping now anyway, and a
    /// second unexplained break beyond it would read as a bug.
    ///
    /// CLEARANCE is width/2 + 1 plus whatever the caller adds, and the +1 is
    /// LOAD-BEARING rather than padding. The new polyline keeps the run's two
    /// ends plus the original waypoints strictly inside it, so the line is
    /// RE-DRAWN, and Bresenham restarted at an interior lattice point does not
    /// reproduce the tail of the original. In this file's own Line(), (0,0) to
    /// (6,4) passes through (2,1),(3,2),(4,3) while (2,1) to (6,4) gives
    /// (3,2),(4,2). That is not a rare case: measured over 51,081 restarts of
    /// random lines, 45,858 -- ninety per cent -- diverge from the tail they
    /// restart on. Both paths stay within half a cell of the true line, so the
    /// divergence is bounded at ONE cell Chebyshev, which was the worst observed
    /// across every one of those restarts, and the +1 absorbs exactly that.
    /// Remove it and the carriageway reaches the vault wall.
    ///
    /// A road left with nothing keeps its RoadData entry and loses its polyline.
    /// Removing the entry would be worse than it looks: FilletJunctionsIntoSegments
    /// reads roads[0] for the network's width, centre and clamp radius, so
    /// dropping an entry can swap a five-wide trunk for a two-wide spur and
    /// change fillet geometry across the whole floor. Every derivation --
    /// Centreline, JunctionNodes, RebuildRoadAnchors, DeepRoadGraph.Build --
    /// already guards on an empty polyline.
    /// </summary>
    public static RoadTruncation TruncateAroundBlocked(
        List<RoadData> roads, HashSet<Vector3Int> blocked, int extraClearance)
    {
        var report = new RoadTruncation();
        if (roads == null || blocked == null || blocked.Count == 0) return report;

        foreach (var road in roads)
        {
            if (road == null || road.polyline == null || road.polyline.Count == 0) continue;
            report.roadsExamined++;

            var line = Centreline(road);
            if (line.Count == 0) continue;

            int width = Mathf.Max(1, road.width);
            int reach = (width / 2) + 1 + Mathf.Max(0, extraClearance);
            int minRun = Mathf.Max(2, width * MinSurvivingRunFactor);

            // Longest unblocked run; ties keep the earlier one, so the result is
            // stable rather than dependent on scan direction.
            int bestStart = -1, bestLen = 0, runStart = -1;
            for (int i = 0; i < line.Count; i++)
            {
                if (NearBlocked(line[i], blocked, reach))
                {
                    runStart = -1;
                    continue;
                }
                if (runStart < 0) runStart = i;
                int len = i - runStart + 1;
                if (len > bestLen) { bestLen = len; bestStart = runStart; }
            }

            if (bestLen == line.Count) continue;      // clear of the region entirely

            if (bestLen < minRun)
            {
                report.roadsDropped++;
                report.centrelineCellsRemoved += line.Count;
                report.notes.Add("road " + road.id + " (" + road.kind + ", width " +
                                 width + ") DROPPED -- longest clear run was " +
                                 bestLen + " cell(s) of " + line.Count +
                                 ", under the " + minRun + "-cell minimum");
                road.polyline.Clear();
                road.brokenGapCells = 0;
                continue;
            }

            int a = bestStart, b = bestStart + bestLen - 1;

            // Centreline dedupes through a HashSet, so a cell appears once and
            // its index is its FIRST occurrence. A meander that revisits a cell
            // could therefore report an index that walks backwards, which would
            // fold the polyline over itself -- hence the strictly-forward test
            // below as well as the strictly-inside one.
            var index = new Dictionary<Vector3Int, int>(line.Count);
            for (int i = 0; i < line.Count; i++)
                if (!index.ContainsKey(line[i])) index[line[i]] = i;

            var kept = new List<SerializableVector3Int>
            {
                SerializableVector3Int.From(line[a]),
            };
            int lastIdx = a;
            foreach (var sv in road.polyline)
            {
                if (sv == null) continue;
                if (!index.TryGetValue(sv.ToVector3Int(), out int at)) continue;
                if (at <= lastIdx || at >= b) continue;
                kept.Add(SerializableVector3Int.From(line[at]));
                lastIdx = at;
            }
            kept.Add(SerializableVector3Int.From(line[b]));

            report.roadsTruncated++;
            report.centrelineCellsRemoved += line.Count - bestLen;
            report.notes.Add("road " + road.id + " (" + road.kind + ", width " +
                             width + ") truncated -- kept " + bestLen + " of " +
                             line.Count + " centreline cells across " +
                             kept.Count + " waypoint(s), from " +
                             road.polyline.Count + " before");

            road.polyline.Clear();
            road.polyline.AddRange(kept);
            road.brokenGapCells = 0;
        }

        return report;
    }

    /// <summary>Whether any blocked cell lies within `reach` (Chebyshev) of this
    /// one. Probing a square around the candidate rather than dilating the whole
    /// blocked set: the vault runs to some five thousand cells and dilating it by
    /// three would build a set of a hundred and forty thousand to answer a few
    /// thousand questions.</summary>
    private static bool NearBlocked(Vector3Int cell, HashSet<Vector3Int> blocked, int reach)
    {
        for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reach; dy <= reach; dy++)
                if (blocked.Contains(new Vector3Int(cell.x + dx, cell.y + dy, 0)))
                    return true;
        return false;
    }

    // ---- Trunk mode ------------------------------------------------

    /// <summary>
    /// One road, rim to rim. The chord is re-rolled until it clears the core's
    /// exclusion disc: a trunk driven straight over the core would have its middle
    /// stretch rejected cell by cell and read as a road with a hole in it.
    /// </summary>
    private static void BuildTrunk(
        System.Random rng, Vector3Int centre, int usable,
        RoadFloorEntry entry, int coreExclusionRadius, RoadNetworkResult result)
    {
        double spread = entry.trunkBearingSpread * Math.PI / 180.0;
        int clearance = coreExclusionRadius + entry.trunkWidth;

        Vector3Int a = default, b = default;
        bool found = false;

        for (int tries = 0; tries < 24 && !found; tries++)
        {
            double startAngle = rng.NextDouble() * 2.0 * Math.PI;
            double endAngle = startAngle + Math.PI + (rng.NextDouble() - 0.5) * 2.0 * spread;

            a = OnCircle(centre, usable, startAngle);
            b = OnCircle(centre, usable, endAngle);

            if (PerpendicularDistanceFromCentre(a, b, centre) >= clearance) found = true;
        }
        if (!found) return;

        result.roads.Add(MakeRoad(
            BuildEdgePolyline(rng, a, b, entry),
            RoadKind.Trunk, entry.trunkWidth, entry, centre, usable, 0));
    }

    // ---- Network mode ----------------------------------------------

    /// <summary>
    /// Junction nodes, a spanning tree over them, a few loop edges, roads reaching
    /// out toward the rim, and spurs that stop dead. Denser than the trunk floor on
    /// purpose: deeper is older, and older is when the thing was whole.
    /// </summary>
    private static void BuildNetwork(
        System.Random rng, Vector3Int centre, int usable,
        RoadFloorEntry entry, int coreExclusionRadius, RoadNetworkResult result)
    {
        var nodes = ScatterJunctions(rng, centre, usable, entry, coreExclusionRadius);
        result.junctions = nodes;
        if (nodes.Count < 2) return;

        // Accepted edges as straight endpoint pairs. The spacing rules are applied
        // to these rather than to the meandered polylines: the meander is pinned to
        // zero at both ends and bounded by meanderAmplitude, so the chord is a fair
        // proxy and costs nothing to test.
        var placed = new List<EdgeChord>();

        // Spanning-tree edges are placed UNCONDITIONALLY. Connectivity beats
        // spacing: refusing one could orphan a junction, and the tree is by
        // construction the shortest set of edges that reaches every node.
        foreach (var e in SpanningTreeEdges(nodes))
        {
            placed.Add(new EdgeChord { a = nodes[e.Key], b = nodes[e.Value], na = e.Key, nb = e.Value });
            result.roads.Add(MakeRoad(
                BuildEdgePolyline(rng, nodes[e.Key], nodes[e.Value], entry),
                RoadKind.Trunk, entry.trunkWidth, entry, centre, usable, 0));
        }

        // Loop edges draw from a wider candidate list than they need, so the rules
        // trim the pool rather than starve it. ExtraLoopEdges returns the SHORTEST
        // unused node pairs, and those are precisely the pairs already joined by a
        // short tree path -- which is why the unconstrained builder made so many
        // thin triangles.
        int loopsPlaced = 0;
        foreach (var e in ExtraLoopEdges(nodes, entry.extraLoopEdges * 3))
        {
            if (loopsPlaced >= entry.extraLoopEdges) break;
            if (!ChordAccepted(nodes[e.Key], nodes[e.Value], e.Key, e.Value, placed, entry)) continue;
            loopsPlaced++;
            placed.Add(new EdgeChord { a = nodes[e.Key], b = nodes[e.Value], na = e.Key, nb = e.Value });
            result.roads.Add(MakeRoad(
                BuildEdgePolyline(rng, nodes[e.Key], nodes[e.Value], entry),
                RoadKind.Trunk, entry.trunkWidth, entry, centre, usable, 0));
        }

        // Rim-bound trunks: the outermost junctions send a road on outward. It stops
        // short of the bedrock and ends in collapse.
        var byDistance = new List<int>();
        for (int i = 0; i < nodes.Count; i++) byDistance.Add(i);
        byDistance.Sort((x, y) => SqDist(nodes[y], centre).CompareTo(SqDist(nodes[x], centre)));

        // Walks every junction outward-first and stops once enough rim trunks are
        // placed, so one refusal costs a candidate rather than a road.
        int rimPlaced = 0;
        for (int i = 0; i < byDistance.Count && rimPlaced < entry.rimTrunkCount; i++)
        {
            int ni = byDistance[i];
            var from = nodes[ni];
            double bearing = Math.Atan2(from.y - centre.y, from.x - centre.x);
            var to = OnCircle(centre, usable, bearing);
            if (SqDist(to, from) < 64) continue;
            if (!ChordAccepted(from, to, ni, -1, placed, entry)) continue;

            rimPlaced++;
            placed.Add(new EdgeChord { a = from, b = to, na = ni, nb = -1 });
            result.roads.Add(MakeRoad(
                BuildEdgePolyline(rng, from, to, entry),
                RoadKind.Trunk, entry.trunkWidth, entry, centre, usable, entry.brokenGapCells));
        }

        // Broken spurs: the ones that once climbed toward the floor above.
        int minLen = Mathf.Min(entry.spurMinLength, entry.spurMaxLength);
        int maxLen = Mathf.Max(entry.spurMinLength, entry.spurMaxLength);

        // A spur used to fire at a fully random bearing from a random node, which
        // is how one came out 0.0 degrees off an existing road -- laid exactly on
        // top of it. Each spur now gets several bearings to find one that clears.
        int spursPlaced = 0;
        for (int i = 0; i < entry.brokenSpurCount * 8 && spursPlaced < entry.brokenSpurCount; i++)
        {
            int ni = rng.Next(nodes.Count);
            var from = nodes[ni];
            double bearing = rng.NextDouble() * 2.0 * Math.PI;
            int length = rng.Next(minLen, maxLen + 1);

            var to = new Vector3Int(
                from.x + (int)Math.Round(Math.Cos(bearing) * length),
                from.y + (int)Math.Round(Math.Sin(bearing) * length), 0);
            to = ClampIntoDisc(to, centre, usable);
            if (SqDist(to, from) < 64) continue;
            if (!ChordAccepted(from, to, ni, -1, placed, entry)) continue;

            spursPlaced++;
            placed.Add(new EdgeChord { a = from, b = to, na = ni, nb = -1 });
            result.roads.Add(MakeRoad(
                BuildEdgePolyline(rng, from, to, entry),
                RoadKind.Spur, entry.spurWidth, entry, centre, usable, entry.brokenGapCells));
        }
    }

    // ---- Spacing rules ---------------------------------------------

    /// <summary>An accepted edge as a straight chord, with the junction indices it
    /// touches. Index -1 means a free end (a rim trunk's rim end, a spur's stub).</summary>
    private struct EdgeChord
    {
        public Vector3Int a, b;
        public int na, nb;
    }

    /// <summary>
    /// The two geometric rules that stop the network reading as a pile of slivers.
    ///
    ///   1. MINIMUM JUNCTION ANGLE. Two roads leaving the same junction must
    ///      diverge by at least minJunctionAngleDegrees. Without it, measurement
    ///      over 300 generated networks found 4.7 pairs per floor under 25 degrees,
    ///      the worst being 0.0 -- two roads exactly superimposed.
    ///   2. MINIMUM SEPARATION. Two roads sharing NO junction must stay at least
    ///      minRoadSeparation cells apart, so they cannot run alongside each other.
    ///
    /// Both are off when their tunable is zero.
    /// </summary>
    private static bool ChordAccepted(
        Vector3Int a, Vector3Int b, int na, int nb,
        List<EdgeChord> placed, RoadFloorEntry entry)
    {
        if (entry.minJunctionAngleDegrees > 0f)
        {
            double minRad = entry.minJunctionAngleDegrees * Math.PI / 180.0;
            foreach (var e in placed)
            {
                // Compare bearings only where the two edges genuinely meet at a
                // shared junction. A free end (-1) is not a shared node.
                if (na >= 0 && (na == e.na || na == e.nb))
                {
                    var other = (na == e.na) ? e.b : e.a;
                    if (AngleBetween(a, b, a, other) < minRad) return false;
                }
                if (nb >= 0 && (nb == e.na || nb == e.nb))
                {
                    var other = (nb == e.na) ? e.b : e.a;
                    if (AngleBetween(b, a, b, other) < minRad) return false;
                }
            }
        }

        if (entry.minRoadSeparation > 0f)
        {
            foreach (var e in placed)
            {
                bool sharesNode = (na >= 0 && (na == e.na || na == e.nb))
                               || (nb >= 0 && (nb == e.na || nb == e.nb));
                if (sharesNode) continue;
                if (SegmentDistance(a, b, e.a, e.b) < entry.minRoadSeparation) return false;
            }
        }

        return true;
    }

    /// <summary>Angle between the bearings from-to1 and from-to2, in radians.</summary>
    private static double AngleBetween(Vector3Int from, Vector3Int to1, Vector3Int from2, Vector3Int to2)
    {
        double b1 = Math.Atan2(to1.y - from.y, to1.x - from.x);
        double b2 = Math.Atan2(to2.y - from2.y, to2.x - from2.x);
        double d = Math.Abs(b1 - b2) % (2.0 * Math.PI);
        return Math.Min(d, 2.0 * Math.PI - d);
    }

    /// <summary>Smallest distance between two straight segments, taken as the least
    /// of the four endpoint-to-segment distances. Exact unless the segments cross,
    /// and a crossing pair is well under any sane threshold anyway.</summary>
    private static double SegmentDistance(Vector3Int p1, Vector3Int p2, Vector3Int q1, Vector3Int q2)
    {
        return Math.Min(
            Math.Min(PointToSegment(p1, q1, q2), PointToSegment(p2, q1, q2)),
            Math.Min(PointToSegment(q1, p1, p2), PointToSegment(q2, p1, p2)));
    }

    private static double PointToSegment(Vector3Int p, Vector3Int a, Vector3Int b)
    {
        double dx = b.x - a.x, dy = b.y - a.y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 0.0) return Math.Sqrt(SqDist(p, a));
        double t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / lenSq;
        t = Math.Max(0.0, Math.Min(1.0, t));
        double cx = a.x + t * dx, cy = a.y + t * dy;
        double ex = p.x - cx, ey = p.y - cy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static List<Vector3Int> ScatterJunctions(
        System.Random rng, Vector3Int centre, int usable,
        RoadFloorEntry entry, int coreExclusionRadius)
    {
        var nodes = new List<Vector3Int>();
        int inner = coreExclusionRadius + 10;
        int outer = Mathf.Max(inner + 1, (int)(usable * 0.85f));
        long spacingSq = (long)entry.junctionMinSpacing * entry.junctionMinSpacing;

        for (int tries = 0; tries < entry.junctionCount * 40 && nodes.Count < entry.junctionCount; tries++)
        {
            double r = Math.Sqrt(rng.NextDouble()) * outer;
            if (r < inner) continue;
            double angle = rng.NextDouble() * 2.0 * Math.PI;
            var cell = new Vector3Int(
                centre.x + (int)Math.Round(r * Math.Cos(angle)),
                centre.y + (int)Math.Round(r * Math.Sin(angle)), 0);

            bool tooClose = false;
            foreach (var n in nodes)
                if (SqDist(n, cell) < spacingSq) { tooClose = true; break; }
            if (tooClose) continue;

            nodes.Add(cell);
        }
        return nodes;
    }

    /// <summary>Prim's spanning tree over the junctions, by squared Euclidean distance.
    /// A tree first, loops after: it guarantees every junction is reachable.</summary>
    private static List<KeyValuePair<int, int>> SpanningTreeEdges(List<Vector3Int> nodes)
    {
        var edges = new List<KeyValuePair<int, int>>();
        int n = nodes.Count;
        if (n < 2) return edges;

        var inTree = new bool[n];
        inTree[0] = true;

        for (int added = 1; added < n; added++)
        {
            long best = long.MaxValue;
            int bestFrom = -1, bestTo = -1;

            for (int i = 0; i < n; i++)
            {
                if (!inTree[i]) continue;
                for (int j = 0; j < n; j++)
                {
                    if (inTree[j]) continue;
                    long d = SqDist(nodes[i], nodes[j]);
                    if (d < best) { best = d; bestFrom = i; bestTo = j; }
                }
            }
            if (bestTo < 0) break;
            inTree[bestTo] = true;
            edges.Add(new KeyValuePair<int, int>(bestFrom, bestTo));
        }
        return edges;
    }

    /// <summary>The shortest node pairs that the spanning tree did not already use.
    /// These are what turn a tree into a network you can go round.</summary>
    private static List<KeyValuePair<int, int>> ExtraLoopEdges(List<Vector3Int> nodes, int count)
    {
        var extra = new List<KeyValuePair<int, int>>();
        if (count <= 0 || nodes.Count < 3) return extra;

        var tree = new HashSet<long>();
        foreach (var e in SpanningTreeEdges(nodes)) tree.Add(EdgeKey(e.Key, e.Value));

        var candidates = new List<KeyValuePair<long, KeyValuePair<int, int>>>();
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                if (tree.Contains(EdgeKey(i, j))) continue;
                candidates.Add(new KeyValuePair<long, KeyValuePair<int, int>>(
                    SqDist(nodes[i], nodes[j]), new KeyValuePair<int, int>(i, j)));
            }

        candidates.Sort((x, y) => x.Key.CompareTo(y.Key));
        for (int i = 0; i < candidates.Count && extra.Count < count; i++)
            extra.Add(candidates[i].Value);
        return extra;
    }

    private static long EdgeKey(int a, int b)
    {
        int lo = Math.Min(a, b), hi = Math.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    // ---- Edge geometry ---------------------------------------------

    /// <summary>
    /// A meandering polyline from a to b. The perpendicular offset is a bounded
    /// random walk multiplied by a sine envelope, so it is zero at both ends and the
    /// edge always arrives exactly where it was sent.
    /// </summary>
    private static List<Vector3Int> BuildEdgePolyline(
        System.Random rng, Vector3Int a, Vector3Int b, RoadFloorEntry entry)
    {
        var points = new List<Vector3Int> { a };

        double dx = b.x - a.x, dy = b.y - a.y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2.0) { points.Add(b); return points; }

        int steps = Mathf.Max(1, (int)Math.Round(length / Math.Max(4, entry.meanderStep)));
        double ux = dx / length, uy = dy / length;
        double px = -uy, py = ux;

        double amplitude = Math.Max(0.0, entry.meanderAmplitude);
        double walk = 0.0;

        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;

            walk += (rng.NextDouble() - 0.5) * 2.0 * amplitude * 0.6;
            if (walk > amplitude) walk = amplitude;
            if (walk < -amplitude) walk = -amplitude;

            double offset = walk * Math.Sin(Math.PI * t);   // pinned to zero at both ends

            points.Add(new Vector3Int(
                (int)Math.Round(a.x + dx * t + px * offset),
                (int)Math.Round(a.y + dy * t + py * offset), 0));
        }

        points.Add(b);
        return points;
    }

    private static RoadData MakeRoad(
        List<Vector3Int> polyline, RoadKind kind, int width,
        RoadFloorEntry entry, Vector3Int centre, int clampRadius, int brokenGapCells)
    {
        var road = new RoadData
        {
            kind = kind,
            width = Mathf.Max(1, width),
            segmentLength = Mathf.Max(4, entry.segmentLength),
            brokenGapCells = Mathf.Max(0, brokenGapCells),
            clampRadius = clampRadius,
            floorCentre = SerializableVector3Int.From(centre),
        };
        foreach (var p in polyline) road.polyline.Add(SerializableVector3Int.From(p));
        return road;
    }

    // ---- Small helpers ---------------------------------------------

    private static Vector3Int OnCircle(Vector3Int centre, int radius, double angle)
        => new Vector3Int(
            centre.x + (int)Math.Round(radius * Math.Cos(angle)),
            centre.y + (int)Math.Round(radius * Math.Sin(angle)), 0);

    private static long SqDist(Vector3Int a, Vector3Int b)
    {
        long dx = a.x - b.x, dy = a.y - b.y;
        return dx * dx + dy * dy;
    }

    private static Vector3Int ClampIntoDisc(Vector3Int cell, Vector3Int centre, int radius)
    {
        double dx = cell.x - centre.x, dy = cell.y - centre.y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d <= radius || d < 0.001) return cell;
        double s = radius / d;
        return new Vector3Int(
            centre.x + (int)Math.Round(dx * s),
            centre.y + (int)Math.Round(dy * s), 0);
    }

    /// <summary>Perpendicular distance from the floor centre to the infinite line ab.
    /// Used to keep a rim-to-rim trunk off the core's exclusion disc.</summary>
    private static double PerpendicularDistanceFromCentre(Vector3Int a, Vector3Int b, Vector3Int centre)
    {
        double dx = b.x - a.x, dy = b.y - a.y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) return double.MaxValue;
        double cross = dx * (centre.y - a.y) - dy * (centre.x - a.x);
        return Math.Abs(cross) / len;
    }
}
