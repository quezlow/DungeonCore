using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dwarven patrols: armed, unhurried, walking roads their grandfathers cut
/// (canon 19, The Living Holds). One pacing a bounded stretch of the gatehouse
/// trunk, two walking the village floor's network. STATELESS on purpose --
/// patrols are ambient texture and re-derive each session; persisting a
/// guard's footsteps would be save weight spent on nothing a player could
/// notice.
///
/// BROKEN ENDS get the beat the warning ladder wants: the patrol walks to
/// where the road stops, pauses facing the collapse, and turns back. Rung 5 of
/// canon 19's ladder is "a dwarf patrol stops, looks, and turns back" at a
/// CLAIMED stretch -- that arrives with road claiming in step 8; exercising
/// the same choreography on natural geometry now means step 8 re-aims a beat
/// that already works instead of inventing one.
///
/// REACTIONS exercise the entry-7 matrix for the first time: any adventurer
/// within watch range halts the patrol to watch; an adventurer of a faction
/// the matrix marks Hostile to the Deep Holds sends it withdrawing toward
/// home instead. Scans are throttled -- the ScanForHostiles allocation lesson.
///
/// Walkers are DwarfWalkerPuppet: not combat entities, invisible to
/// ScanForHostiles and traps, hidden by fog on unrevealed stretches for free.
/// </summary>
public class DwarvenPatrolController : MonoBehaviour
{
    public static DwarvenPatrolController Instance { get; private set; }

    [Header("Sprites")]
    [Tooltip("Optional. Dealt across patrols; falls back to the caravan's " +
             "walker list, then stays dormant. Armed variants belong here once " +
             "the art pass makes them.")]
    [SerializeField] private List<Sprite> patrolSprites = new List<Sprite>();
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;

    [Header("Routes")]
    [Tooltip("Half-length, in centreline cells, of the gatehouse patrol's beat " +
             "either side of the outpost.")]
    [SerializeField, Min(10)] private int gateBeatHalfCells = 60;
    [SerializeField, Min(0)] private int villagePatrolCount = 2;

    [Header("Movement")]
    [Tooltip("Plain speed, not day-derived: a patrol loops with no arrival to " +
             "keep, so the days constraint does not bind it.")]
    [SerializeField, Min(0.2f)] private float patrolSpeed = 2.2f;
    [SerializeField, Min(0f)] private float endPauseSeconds = 2f;
    [Tooltip("Longer pause at a broken end -- the stop-and-look beat.")]
    [SerializeField, Min(0f)] private float brokenEndPauseSeconds = 3f;

    [Header("Reactions")]
    [SerializeField, Min(1f)] private float watchRadius = 8f;
    [Tooltip("A withdrawing patrol resumes once no hostile is inside this.")]
    [SerializeField, Min(1f)] private float clearRadius = 14f;

    private class Patrol
    {
        public FloorRoot floor;
        public DeepRoadGraph.Graph graph;
        public DwarfWalkerPuppet puppet;
        public int rail, index, direction;      // walk-cell cursor
        public int homeRail, homeIndex;
        public int beatMin = -1, beatMax = -1;  // gatehouse beat window, or -1
        public float pauseUntil;
        public bool withdrawing;
        public float scanAt;
        public float stepProgress;
        public bool watching;
        public int cachedFrom = -1, cachedTo = -1;
        public readonly List<Vector3> pathBuf = new List<Vector3>(2);
    }

    private readonly List<Patrol> patrols = new List<Patrol>();
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();
    private bool gateSpawned, villageSpawned;
    private float establishPollAt;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!gateSpawned || !villageSpawned)
        {
            if (Time.unscaledTime >= establishPollAt)
            {
                establishPollAt = Time.unscaledTime + 1f;
                TrySpawn();
            }
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        foreach (var p in patrols) TickPatrol(p, dt);
    }

    // -- Spawning ------------------------------------------------------------

    private void TrySpawn()
    {
        if (!gateSpawned
            && DwarvenOutpostController.Instance != null
            && DwarvenOutpostController.Instance.Established)
            gateSpawned = SpawnGatePatrol();

        if (!villageSpawned
            && DwarvenVillageController.Instance != null
            && DwarvenVillageController.Instance.Established)
            villageSpawned = SpawnVillagePatrols();
    }

    private Sprite PickSprite(int i)
    {
        var deck = new List<Sprite>();
        if (patrolSprites != null)
            foreach (var s in patrolSprites) if (s != null) deck.Add(s);
        if (deck.Count == 0 && DwarvenCaravanController.Instance != null)
            foreach (var s in DwarvenCaravanController.Instance.WalkerSpriteFallback)
                if (s != null) deck.Add(s);
        if (deck.Count > 0) return deck[i % deck.Count];
        return null;
    }

    private bool SpawnGatePatrol()
    {
        var floor = FloorWithOutpost(out var site);
        if (floor == null || site == null) return false;
        var graph = DeepRoadGraph.Build(floor.FeatureGenerator.FeatureData.roads);
        if (graph.rails.Count == 0) return true;   // a roadless outpost patrols nowhere
        if (!DeepRoadGraph.NearestWalkCell(graph, site.anchorCell.ToVector3Int(),
                out int rail, out int index)) return true;

        var sprite = PickSprite(0);
        if (sprite == null) { WarnDormantOnce(); return true; }

        int count = graph.rails[rail].walk.Count;
        var p = new Patrol
        {
            floor = floor,
            graph = graph,
            rail = rail,
            index = index,
            direction = 1,
            homeRail = rail,
            homeIndex = index,
            beatMin = Mathf.Max(0, index - gateBeatHalfCells),
            beatMax = Mathf.Min(count - 1, index + gateBeatHalfCells),
        };
        p.puppet = MakePuppet("DwarvenPatrolGate", sprite, p);
        patrols.Add(p);
        return true;
    }

    private bool SpawnVillagePatrols()
    {
        var floor = FloorWithVillage(out var site);
        if (floor == null || site == null) return false;
        var graph = DeepRoadGraph.Build(floor.FeatureGenerator.FeatureData.roads);
        if (graph.rails.Count == 0) return true;
        if (!DeepRoadGraph.NearestWalkCell(graph, site.anchorCell.ToVector3Int(),
                out int homeRail, out int homeIndex)) return true;

        for (int i = 0; i < villagePatrolCount; i++)
        {
            var sprite = PickSprite(i);
            if (sprite == null) { WarnDormantOnce(); break; }
            var p = new Patrol
            {
                floor = floor,
                graph = graph,
                rail = homeRail,
                index = homeIndex,
                direction = i % 2 == 0 ? 1 : -1,   // the pair sets off opposite ways
                homeRail = homeRail,
                homeIndex = homeIndex,
            };
            p.puppet = MakePuppet("DwarvenPatrolVillage" + (i + 1), sprite, p);
            patrols.Add(p);
        }
        return true;
    }

    private DwarfWalkerPuppet MakePuppet(string name, Sprite sprite, Patrol p)
    {
        var cell = p.graph.rails[p.rail].walk[p.index];
        var at = p.floor.TileInfluence.CellToWorld(cell);
        var puppet = DwarfWalkerPuppet.Create(name, sprite, sortingLayerName, sortingOrder, at);
        puppet.Speed = patrolSpeed;
        return puppet;
    }

    private bool warned;
    private void WarnDormantOnce()
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("[DwarvenPatrol] No patrol or fallback sprites assigned - " +
                         "patrols stay dormant until a list is filled.");
    }

    private static FloorRoot FloorWithOutpost(out SiteData site)
    {
        site = null;
        var fm = FloorManager.Instance;
        if (fm == null) return null;
        foreach (var floor in fm.AllFloors)
        {
            var f = floor?.FeatureGenerator;
            if (f == null || !f.HasGenerated) continue;
            var s = f.GetOutpostSite();
            if (s != null) { site = s; return floor; }
        }
        return null;
    }

    private static FloorRoot FloorWithVillage(out SiteData site)
    {
        site = null;
        var fm = FloorManager.Instance;
        if (fm == null) return null;
        foreach (var floor in fm.AllFloors)
        {
            var f = floor?.FeatureGenerator;
            if (f == null || !f.HasGenerated) continue;
            var s = f.GetVillageSite();
            if (s != null) { site = s; return floor; }
        }
        return null;
    }

    // -- The walk ------------------------------------------------------------

    private void TickPatrol(Patrol p, float dt)
    {
        if (p.puppet == null) return;

        // Reactions, throttled.
        if (Time.time >= p.scanAt)
        {
            p.scanAt = Time.time + 0.5f;
            ScanReactions(p);
        }
        if (p.watching)
        {
            p.puppet.Frozen = true;
            return;
        }
        p.puppet.Frozen = false;

        if (Time.time < p.pauseUntil) { p.puppet.Frozen = true; return; }

        // Step cell to cell along the current rail. Fractional progress carries
        // across cells so speed is exact regardless of frame rate.
        p.stepProgress += patrolSpeed * dt;
        while (p.stepProgress >= 1f)
        {
            p.stepProgress -= 1f;
            StepOneCell(p);
        }
        var walk = p.graph.rails[p.rail].walk;
        var cellNow = walk[p.index];
        int nextIndex = Mathf.Clamp(p.index + p.direction, 0, walk.Count - 1);

        // Re-path only on a cell change - a fresh two-point path per frame is
        // the per-frame allocation habit this project already paid to unlearn.
        if (p.cachedFrom != p.index || p.cachedTo != nextIndex)
        {
            p.cachedFrom = p.index; p.cachedTo = nextIndex;
            p.pathBuf.Clear();
            p.pathBuf.Add(p.floor.TileInfluence.CellToWorld(cellNow));
            p.pathBuf.Add(p.floor.TileInfluence.CellToWorld(walk[nextIndex]));
            p.puppet.SetPath(p.pathBuf);
        }
        p.puppet.SetDistance(p.stepProgress * p.puppet.PathLength);

        FirstSightingLine(p, cellNow);
    }

    private void StepOneCell(Patrol p)
    {
        var rail = p.graph.rails[p.rail];
        int next = p.index + p.direction;

        int lo = p.beatMin >= 0 ? p.beatMin : 0;
        int hi = p.beatMax >= 0 ? p.beatMax : rail.walk.Count - 1;

        if (next < lo || next > hi)
        {
            bool atRailEnd = next < 0 || next > rail.walk.Count - 1;
            bool brokenHere = atRailEnd && RoadStopsDead(rail, next > 0);

            if (atRailEnd && !brokenHere && p.beatMin < 0
                && TryTurnAtJunction(p, next > 0)) return;

            // The beat: stop, look at where the road stops, turn back.
            p.pauseUntil = Time.time
                + (brokenHere ? brokenEndPauseSeconds : endPauseSeconds);
            if (brokenHere && p.puppet != null && rail.walk.Count >= 2)
            {
                // Look PAST the collapse: extend the last walk step's own
                // direction, so a north-running spur gets a northward stare.
                int endIdx = p.direction > 0 ? rail.walk.Count - 1 : 0;
                int prevIdx = p.direction > 0 ? rail.walk.Count - 2 : 1;
                var endW = p.floor.TileInfluence.CellToWorld(rail.walk[endIdx]);
                var prevW = p.floor.TileInfluence.CellToWorld(rail.walk[prevIdx]);
                p.puppet.Face(endW + (endW - prevW));
            }
            p.direction = -p.direction;
            return;
        }

        // RUNG 5, re-aimed. The stop-and-look beat was written against
        // natural geometry -- a collapse -- and canon step 8 always meant
        // it to answer a CLAIMED stretch as well. Dwarven ground the player
        // has taken is exactly holdings-and-claimed: two dictionary probes
        // on the one cell the patrol is about to step into.
        //
        // Only at the EDGE. Without the test on the cell underfoot, a
        // patrol whose whole beat had been claimed would turn on every
        // step and jitter in place forever.
        if (!StandsOnTakenGround(p, rail.walk[p.index])
            && StandsOnTakenGround(p, rail.walk[next]))
        {
            p.pauseUntil = Time.time + brokenEndPauseSeconds;
            if (p.puppet != null)
            {
                // Look PAST the edge, along the road's own bearing, the same
                // way the collapse beat does.
                var hereW = p.floor.TileInfluence.CellToWorld(rail.walk[p.index]);
                var aheadW = p.floor.TileInfluence.CellToWorld(rail.walk[next]);
                p.puppet.Face(aheadW + (aheadW - hereW));
            }
            p.direction = -p.direction;
            return;
        }

        p.index = next;
    }

    /// <summary>True when this cell is dwarven ground the player holds.
    /// Holdings alone is not enough -- the whole beat runs on dwarven ground
    /// by construction -- and claimed alone is not either, since a patrol
    /// crossing the player's ordinary tunnels is not a diplomatic event.</summary>
    private static bool StandsOnTakenGround(Patrol p, Vector3Int cell)
    {
        if (p.floor == null) return false;
        var map = p.floor.TerrainTypeMap;
        var inf = p.floor.TileInfluence;
        if (map == null || inf == null || !map.HasHoldings) return false;
        return map.IsHoldingsCell(cell) && inf.IsTileClaimed(cell);
    }

    /// <summary>True when this end of the rail is a collapse -- a broken spur
    /// or a rim trunk's swallowed end -- rather than a junction.</summary>
    private static bool RoadStopsDead(DeepRoadGraph.Rail rail, bool atEnd)
    {
        if (rail.road.brokenGapCells > 0 && atEnd) return true;
        return atEnd ? rail.endIsRim : rail.startIsRim;
    }

    /// <summary>At a junction, continue onto a connected rail, avoiding an
    /// immediate about-face when the junction offers anywhere else to go.</summary>
    private bool TryTurnAtJunction(Patrol p, bool leavingByEnd)
    {
        var rail = p.graph.rails[p.rail];
        int node = leavingByEnd ? rail.nodeEnd : rail.nodeStart;
        if (node < 0 || node >= p.graph.adjacency.Count) return false;

        var options = new List<(int rail, bool atStart)>();
        foreach (var opt in p.graph.adjacency[node])
            if (opt.rail != p.rail) options.Add(opt);
        if (options.Count == 0) return false;

        var pick = options[Random.Range(0, options.Count)];
        p.rail = pick.rail;
        var newWalk = p.graph.rails[p.rail].walk;
        p.index = pick.atStart ? 0 : newWalk.Count - 1;
        p.direction = pick.atStart ? 1 : -1;
        return true;
    }

    // -- Reactions -----------------------------------------------------------

    private void ScanReactions(Patrol p)
    {
        p.watching = false;
        if (p.floor?.Entities == null || p.puppet == null) return;

        var pos = p.puppet.LogicalPosition;
        p.floor.Entities.WithinRadius(pos, p.withdrawing ? clearRadius : watchRadius, advBuf);

        bool any = false, hostile = false;
        DungeonAdventurer nearest = null;
        float nearestSq = float.MaxValue;
        foreach (var a in advBuf)
        {
            if (a == null) continue;
            any = true;
            float d = ((Vector2)(a.transform.position - pos)).sqrMagnitude;
            if (d < nearestSq) { nearestSq = d; nearest = a; }
            if (FactionRelations.AreHostile(FactionId.Dwarves,
                    AdventurerTypeInfo.FactionOf(a.Type)))
                hostile = true;
        }

        if (p.withdrawing)
        {
            if (!any) p.withdrawing = false;
            else SetCourseHome(p);
            return;
        }

        if (hostile)
        {
            // The matrix, felt: the Holds do not brawl the Church's people in
            // the road. They leave.
            p.withdrawing = true;
            SetCourseHome(p);
        }
        else if (any)
        {
            p.watching = true;
            if (nearest != null) p.puppet.Face(nearest.transform.position);
        }
    }

    private static void SetCourseHome(Patrol p)
    {
        if (p.rail == p.homeRail)
        {
            p.direction = p.index > p.homeIndex ? -1 : 1;
            return;
        }
        // Off the home rail: head for the junction end that leads back. The
        // network is a handful of rails, so one BFS hop query is cheap enough
        // to just re-route through DeepRoadGraph.
        var route = DeepRoadGraph.Route(p.graph, p.rail, p.index, p.homeRail, p.homeIndex);
        if (route.Count > 1)
        {
            var rail = p.graph.rails[p.rail];
            // Second route cell tells us which way along the current rail.
            int here = p.index;
            int nextIdx = IndexOnRail(rail, route[1]);
            p.direction = nextIdx >= here ? 1 : -1;
        }
    }

    private static int IndexOnRail(DeepRoadGraph.Rail rail, Vector3Int cell)
    {
        for (int i = 0; i < rail.walk.Count; i++)
            if (rail.walk[i] == cell) return i;
        return 0;
    }

    // -- First sighting --------------------------------------------------------

    private void FirstSightingLine(Patrol p, Vector3Int cell)
    {
        var wisp = WispCompanion.Instance;
        if (wisp == null || wisp.HasSpoken("patrol_first")) return;
        var features = p.floor?.FeatureGenerator;
        if (features == null) return;
        if (!features.TryGetFeatureRef(cell, out var fref)
            || fref.type != FeatureType.Road) return;
        if (!features.IsRoadSegmentRevealed(fref.featureId)) return;
        wisp.Speak("patrol_first");
    }
}
