using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The road network as a walkable GRAPH, rebuilt from saved RoadData (canon 19,
/// The Living Holds). Pure static, like RoadNetworkBuilder, and for the same
/// reason: the caravan route report in Commands.cs must be able to measure a
/// route without a scene in play mode.
///
/// WHY A GRAPH AND NOT THE PATHFINDER. MarkNaturalFloor puts every road cell in
/// minedTiles, so DungeonPathfinder would happily carry a dwarven caravan
/// through the player's own tunnels whenever they were shorter -- which is
/// exactly wrong. The road IS the route: walkers follow Centreline() cells, so
/// they visibly keep to the carriageway, thread the village gates the plans
/// drew (the carriageway was subtracted from the sites, never the reverse), and
/// wade the washed-out river crossings the carve order left behind.
///
/// NODES are endpoint clusters. Edge polylines share junction cells by
/// construction, but RebuildRoadAnchors merges endpoints by proximity rather
/// than equality and calls that "more robust" -- so this mirrors the shipped
/// rule (merge radius 6) instead of trusting exactness the generator never
/// promised.
///
/// RIM ENDS are where a road leaves for another floor, in fiction: the trunk
/// runs on and the rim swallowed it. Identified from save data alone --
/// kind == Trunk and the RAW polyline end within 2 cells of clampRadius, which
/// is where OnCircle put it at generation. The broken gap does not move the raw
/// endpoint, so this holds for floor index 3's collapsed rim trunks exactly as
/// it does for floor index 2's unbroken one. Spurs are RoadKind.Spur and never
/// qualify.
/// </summary>
public static class DeepRoadGraph
{
    /// <summary>Two road ends within this distance were one junction before the
    /// network was split into edges. Matches RebuildRoadAnchors.</summary>
    public const int JunctionMergeRadius = 6;

    /// <summary>One road as the walkers see it: the ordered walkable centreline
    /// (broken gap already removed by Centreline) plus which node cluster each
    /// raw end belongs to.</summary>
    public class Rail
    {
        public RoadData road;
        public List<Vector3Int> walk;        // ordered, walkable
        public Vector3Int rawStart, rawEnd;  // pre-break polyline ends
        public int nodeStart = -1, nodeEnd = -1;
        public bool endIsRim;                // rawEnd sits on the clamp circle
        public bool startIsRim;              // floor 2's trunk has two rim ends
    }

    public class Graph
    {
        public List<Rail> rails = new List<Rail>();
        public List<Vector3Int> nodes = new List<Vector3Int>();
        // node index -> list of (rail index, atStart?) touching it
        public List<List<(int rail, bool atStart)>> adjacency
            = new List<List<(int rail, bool atStart)>>();
    }

    /// <summary>A rim end a journey can leave the floor by: the walkable
    /// terminus cell and the bearing of the RAW end from the floor centre --
    /// the bearing is what pairs it with the matching end one floor down.</summary>
    public struct RimEnd
    {
        public int railIndex;
        public Vector3Int walkTerminus;
        public float bearingDegrees;
    }

    public static Graph Build(List<RoadData> roads)
    {
        var g = new Graph();
        if (roads == null) return g;

        foreach (var road in roads)
        {
            if (road == null || road.polyline == null || road.polyline.Count == 0) continue;
            var walk = RoadNetworkBuilder.Centreline(road);
            if (walk.Count == 0) continue;

            var rail = new Rail
            {
                road = road,
                walk = walk,
                rawStart = road.polyline[0].ToVector3Int(),
                rawEnd = road.polyline[road.polyline.Count - 1].ToVector3Int(),
            };

            var centre = road.floorCentre != null
                ? road.floorCentre.ToVector3Int() : Vector3Int.zero;
            long clamp = road.clampRadius;
            rail.startIsRim = road.kind == RoadKind.Trunk && NearRadius(rail.rawStart, centre, clamp);
            rail.endIsRim = road.kind == RoadKind.Trunk && NearRadius(rail.rawEnd, centre, clamp);
            g.rails.Add(rail);
        }

        // Cluster raw endpoints into nodes.
        foreach (var rail in g.rails)
        {
            rail.nodeStart = NodeFor(g, rail.rawStart);
            rail.nodeEnd = NodeFor(g, rail.rawEnd);
        }
        for (int i = 0; i < g.rails.Count; i++)
        {
            var rail = g.rails[i];
            g.adjacency[rail.nodeStart].Add((i, true));
            g.adjacency[rail.nodeEnd].Add((i, false));
        }
        return g;
    }

    private static bool NearRadius(Vector3Int p, Vector3Int centre, long radius)
    {
        long dx = p.x - centre.x, dy = p.y - centre.y;
        long d2 = dx * dx + dy * dy;
        long lo = radius - 2; if (lo < 0) lo = 0;
        return d2 >= lo * lo;
    }

    private static int NodeFor(Graph g, Vector3Int p)
    {
        const long r2 = JunctionMergeRadius * JunctionMergeRadius;
        for (int i = 0; i < g.nodes.Count; i++)
        {
            long dx = g.nodes[i].x - p.x, dy = g.nodes[i].y - p.y;
            if (dx * dx + dy * dy <= r2) return i;
        }
        g.nodes.Add(p);
        g.adjacency.Add(new List<(int, bool)>());
        return g.nodes.Count - 1;
    }

    public static List<RimEnd> RimEnds(Graph g)
    {
        var ends = new List<RimEnd>();
        for (int i = 0; i < g.rails.Count; i++)
        {
            var rail = g.rails[i];
            var centre = rail.road.floorCentre != null
                ? rail.road.floorCentre.ToVector3Int() : Vector3Int.zero;
            if (rail.startIsRim)
                ends.Add(new RimEnd
                {
                    railIndex = i,
                    walkTerminus = rail.walk[0],
                    bearingDegrees = Bearing(centre, rail.rawStart),
                });
            if (rail.endIsRim)
                ends.Add(new RimEnd
                {
                    railIndex = i,
                    walkTerminus = rail.walk[rail.walk.Count - 1],
                    bearingDegrees = Bearing(centre, rail.rawEnd),
                });
        }
        return ends;
    }

    public static float Bearing(Vector3Int centre, Vector3Int p)
        => Mathf.Atan2(p.y - centre.y, p.x - centre.x) * Mathf.Rad2Deg;

    /// <summary>Absolute angular difference on the circle, 0..180.</summary>
    public static float BearingDelta(float a, float b)
        => Mathf.Abs(Mathf.DeltaAngle(a, b));

    /// <summary>Nearest walkable centreline cell to a cell, across every rail.
    /// Linear over a few thousand cells, run once per journey -- measured at
    /// well under a millisecond on floor index 3's eight rails.</summary>
    public static bool NearestWalkCell(Graph g, Vector3Int near,
        out int railIndex, out int cellIndex)
        => NearestWalkCell(g, near, null, out railIndex, out cellIndex);

    /// <summary>The same search, restricted to the rails a caller will accept.
    /// One implementation rather than two, because the unfiltered form is this
    /// one with a null filter -- a second copy of a nearest-cell loop is a
    /// second copy free to disagree.</summary>
    public static bool NearestWalkCell(Graph g, Vector3Int near,
        System.Func<Rail, bool> admit, out int railIndex, out int cellIndex)
    {
        railIndex = -1; cellIndex = -1;
        if (g == null) return false;
        long best = long.MaxValue;
        for (int r = 0; r < g.rails.Count; r++)
        {
            if (admit != null && !admit(g.rails[r])) continue;
            var walk = g.rails[r].walk;
            for (int i = 0; i < walk.Count; i++)
            {
                long dx = walk[i].x - near.x, dy = walk[i].y - near.y;
                long d = dx * dx + dy * dy;
                if (d < best) { best = d; railIndex = r; cellIndex = i; }
            }
        }
        return railIndex >= 0;
    }

    /// <summary>WHERE A PATROL STANDS UP, and it is deliberately NOT simply the
    /// nearest walk cell.
    ///
    /// Measured, 300 seeds of floor index 2: the rail nearest the outpost's own
    /// anchor was a LANE on 199 and a SPUR on 101, and the TRUNK on NONE of
    /// them. A Lane paints no carriageway at all -- RebuildRoadCells skips it
    /// before any id is drawn -- and the outpost's approach spur is a doorway,
    /// so plain proximity seated the guard of a road on everything except the
    /// road. The preference is therefore GRADED rather than binary: excluding
    /// Lane alone would still have left a third of seeds pacing a doorway.
    ///
    /// It degrades rather than fails, on SiteAnchor's own precedent, so a floor
    /// whose roads are all spurs still seats a patrol somewhere.</summary>
    public static bool SeatPatrol(Graph g, Vector3Int near,
        out int railIndex, out int cellIndex)
    {
        if (NearestWalkCell(g, near, r => r.road != null && r.road.kind == RoadKind.Trunk,
                            out railIndex, out cellIndex)) return true;
        if (NearestWalkCell(g, near, r => r.road != null && r.road.kind != RoadKind.Lane,
                            out railIndex, out cellIndex)) return true;
        return NearestWalkCell(g, near, null, out railIndex, out cellIndex);
    }

    /// <summary>A patrol's BEAT: every walk cell reachable within `radiusCells`
    /// STEPS of a seat, over the network rather than through the rock.
    ///
    /// GRAPH DISTANCE, NOT EUCLIDEAN, and that is the whole of the argument. A
    /// straight-line ball counts a cell a few world-cells away that is three
    /// hundred walk cells around the network, which a squad can never reach
    /// inside its beat -- so a coverage figure taken off one would be a lie in
    /// the direction that flatters it.
    ///
    /// AN INDEX WINDOW ON ONE RAIL CANNOT DO THIS JOB, which is what it was
    /// doing before. AncientSiteBuilder.SplitChordsForSites chops the trunk
    /// wherever it seats a site, so a beat bounded by index is bounded by an
    /// artefact of site placement. MEASURED on the live floor: a 191-cell gate
    /// rail with the seat at its GATE END, because the nearest trunk cell to
    /// the outpost anchor is the one just outside its gate -- so an index
    /// window of +/-60 holds 61 cells of ONE approach where the ball holds 124
    /// across both. The ball roughly doubles the beat and every extra cell is
    /// on the far side of the gatehouse.
    ///
    /// LANES ARE ADMITTED AS TRANSIT, and the measurement is why. A laned site
    /// is threaded gate-to-gate by a Lane, so excluding Lane rails DISCONNECTS
    /// the two halves of the trunk at exactly the site the gate guard exists
    /// for: coverage of the trunk fell to 13.6 per cent for a laned outpost
    /// against 26.3 for a spur-seated one, purely because the site pass had
    /// threaded the road through the building. Admitting the lane evens them at
    /// 25.7 and 29.7. The guard walks through his own gatehouse, which is what
    /// a gatehouse on a road is for.</summary>
    public static HashSet<long> BeatSet(Graph g, int railIndex, int cellIndex,
                                        int radiusCells)
    {
        var set = new HashSet<long>();
        if (g == null || railIndex < 0 || railIndex >= g.rails.Count) return set;
        if (cellIndex < 0 || cellIndex >= g.rails[railIndex].walk.Count) return set;
        if (radiusCells < 0) return set;

        var dist = new Dictionary<long, int>();
        var queue = new Queue<long>();
        long seed = BeatKey(railIndex, cellIndex);
        dist[seed] = 0;
        set.Add(seed);
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            long cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= radiusCells) continue;
            int r = (int)(cur >> 32);
            int i = (int)(cur & 0xFFFFFFFFL);
            var rail = g.rails[r];

            if (i > 0) BeatVisit(set, dist, queue, BeatKey(r, i - 1), d + 1);
            if (i < rail.walk.Count - 1)
                BeatVisit(set, dist, queue, BeatKey(r, i + 1), d + 1);

            // A rail END steps to every other rail sharing that node cluster,
            // entering at that rail's near end. Route() already stitches across
            // the few cells a cluster can span, so the hop costs one step.
            if (i == 0) BeatHop(g, set, dist, queue, rail.nodeStart, r, d + 1);
            if (i == rail.walk.Count - 1)
                BeatHop(g, set, dist, queue, rail.nodeEnd, r, d + 1);
        }
        return set;
    }

    private static void BeatVisit(HashSet<long> set, Dictionary<long, int> dist,
                                  Queue<long> queue, long key, int d)
    {
        if (dist.ContainsKey(key)) return;
        dist[key] = d;
        set.Add(key);
        queue.Enqueue(key);
    }

    private static void BeatHop(Graph g, HashSet<long> set, Dictionary<long, int> dist,
                                Queue<long> queue, int node, int fromRail, int d)
    {
        if (node < 0 || node >= g.adjacency.Count) return;
        foreach (var opt in g.adjacency[node])
        {
            if (opt.rail == fromRail) continue;
            var other = g.rails[opt.rail];
            if (other.walk.Count == 0) continue;
            BeatVisit(set, dist, queue,
                      BeatKey(opt.rail, opt.atStart ? 0 : other.walk.Count - 1), d);
        }
    }

    /// <summary>Rail and cell index packed into one key. Indices are never
    /// negative, so the low word is unsigned and the pack is exact.</summary>
    public static long BeatKey(int railIndex, int cellIndex)
        => ((long)railIndex << 32) | (uint)cellIndex;

    /// <summary>
    /// Ordered walkable cells from one on-road point to another, along the
    /// network. BFS over node clusters (a handful of nodes, so shortest-hop is
    /// plenty); junction mouths are bridged with a Bresenham stitch because
    /// clustered endpoints may sit a few cells apart -- the stitch stays inside
    /// the dilated junction carriageway. Returns an empty list only when the two
    /// points sit on disconnected components, which the generator does not
    /// produce.
    /// </summary>
    public static List<Vector3Int> Route(Graph g,
        int fromRail, int fromIndex, int toRail, int toIndex)
    {
        var path = new List<Vector3Int>();
        if (g == null || fromRail < 0 || toRail < 0) return path;

        if (fromRail == toRail)
        {
            AppendRun(path, g.rails[fromRail].walk, fromIndex, toIndex);
            return path;
        }

        // BFS over nodes, seeded from BOTH ends of the entry rail: standing
        // mid-rail, either end may be the shorter way round.
        int nodeCount = g.nodes.Count;
        var prevNode = new int[nodeCount];
        var prevRail = new int[nodeCount];
        var prevAtStart = new bool[nodeCount];
        for (int i = 0; i < nodeCount; i++) prevNode[i] = -2;   // -2 unvisited

        var queue = new Queue<int>();
        var entry = g.rails[fromRail];
        Seed(entry.nodeStart); Seed(entry.nodeEnd);
        void Seed(int n)
        {
            if (n < 0 || prevNode[n] != -2) return;
            prevNode[n] = -1; queue.Enqueue(n);
        }

        int goalStart = g.rails[toRail].nodeStart;
        int goalEnd = g.rails[toRail].nodeEnd;
        int reached = -1;

        while (queue.Count > 0)
        {
            int n = queue.Dequeue();
            if (n == goalStart || n == goalEnd) { reached = n; break; }
            foreach (var (rail, atStart) in g.adjacency[n])
            {
                var r = g.rails[rail];
                int other = atStart ? r.nodeEnd : r.nodeStart;
                if (other < 0 || prevNode[other] != -2) continue;
                prevNode[other] = n; prevRail[other] = rail; prevAtStart[other] = atStart;
                queue.Enqueue(other);
            }
        }
        if (reached < 0) return path;

        // Unwind the node chain into a rail chain.
        var chain = new List<(int rail, bool enteredAtStart)>();
        for (int n = reached; prevNode[n] != -1; n = prevNode[n])
            chain.Add((prevRail[n], prevAtStart[n]));
        chain.Reverse();

        // Entry rail: from the standing index to whichever of its ends the
        // chain (or the goal, when the chain is empty) departs through.
        int firstNode = chain.Count > 0
            ? (chain[0].enteredAtStart ? g.rails[chain[0].rail].nodeStart
                                       : g.rails[chain[0].rail].nodeEnd)
            : reached;
        AppendRun(path, entry.walk, fromIndex,
            firstNode == entry.nodeStart ? 0 : entry.walk.Count - 1);

        foreach (var (rail, enteredAtStart) in chain)
        {
            var r = g.rails[rail];
            Stitch(path, enteredAtStart ? r.walk[0] : r.walk[r.walk.Count - 1]);
            if (enteredAtStart) AppendRun(path, r.walk, 0, r.walk.Count - 1);
            else AppendRun(path, r.walk, r.walk.Count - 1, 0);
        }

        // Exit rail: from the end the chain arrived at, in to the target index.
        var exit = g.rails[toRail];
        int arriveIndex = reached == exit.nodeStart ? 0 : exit.walk.Count - 1;
        Stitch(path, exit.walk[arriveIndex]);
        AppendRun(path, exit.walk, arriveIndex, toIndex);
        return path;
    }

    private static void AppendRun(List<Vector3Int> path, List<Vector3Int> walk, int from, int to)
    {
        if (walk == null || walk.Count == 0) return;
        from = Mathf.Clamp(from, 0, walk.Count - 1);
        to = Mathf.Clamp(to, 0, walk.Count - 1);
        int step = to >= from ? 1 : -1;
        for (int i = from; ; i += step)
        {
            if (path.Count == 0 || path[path.Count - 1] != walk[i]) path.Add(walk[i]);
            if (i == to) break;
        }
    }

    private static void Stitch(List<Vector3Int> path, Vector3Int to)
    {
        if (path.Count == 0) { path.Add(to); return; }
        var from = path[path.Count - 1];
        if (from == to) return;
        foreach (var c in RoadNetworkBuilder.Line(from, to))
            if (path[path.Count - 1] != c) path.Add(c);
    }

    /// <summary>World-unit length of a cell run (cells are one world unit).</summary>
    public static float PathLength(List<Vector3Int> cells)
    {
        float len = 0f;
        for (int i = 1; i < cells.Count; i++)
            len += Vector2.Distance(
                new Vector2(cells[i - 1].x, cells[i - 1].y),
                new Vector2(cells[i].x, cells[i].y));
        return len;
    }
}
