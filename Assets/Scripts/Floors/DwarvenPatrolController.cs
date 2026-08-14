using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dwarven patrols: armed, unhurried, walking roads their grandfathers cut
/// (canon 19, The Living Holds; canon 44, the mortal body layer).
///
/// THEY CAN DIE NOW, and the sentence this doc used to open with -- "STATELESS
/// on purpose: patrols are ambient texture and re-derive each session;
/// persisting a guard's footsteps would be save weight spent on nothing a
/// player could notice" -- was true and is now false. Footsteps are still not
/// saved and never will be. What IS saved is which guards are DEAD and when
/// they fell, because a player who clears the road and reloads must not find it
/// walked again. The distinction the old sentence missed is between a position
/// nobody could notice and an absence everybody would.
///
/// A PATROL IS A SQUAD, not a body. The gatehouse beat is one guard, which is
/// what makes it beatable by a late-tier den; the road patrol is two, which is
/// what makes it a choice rather than a formality. Bodies walk in a trailing
/// column on the squad's single rail cursor -- body i sits i cells behind the
/// leader -- so a squad needs no path arithmetic of its own.
///
/// COMBAT TAKES THE BODY AND THE SQUAD WAITS. While any member answers
/// DungeonMonster.CombatHoldsBody the whole squad suspends: its walkers stop
/// writing transforms, the fighter chases freely, and the others hold rather
/// than marching on without him. When the last fight ends every walker is
/// SnapTo'd to where its body actually stands and the cursor is re-derived from
/// the leader's cell, because the fight moved bodies this class never saw move.
///
/// REPLACEMENT IS THE NEXT DAWN, ALWAYS, and deliberately not conditional on
/// who did the killing. A den that grinds a patrol down and a player who clears
/// it buy the same thing: one day of quiet road. What separates them is the
/// standing bill, which only the player ever pays.
///
/// THE GATE BEAT IS A SET OF CELLS, NOT A WINDOW OF INDICES, and it had to
/// become one. Measured over 300 seeds of floor index 2, the rail nearest the
/// outpost anchor was a LANE on 199 and a SPUR on 101 and the TRUNK on NONE, so
/// the squad guarding the road paced a gatehouse it never left -- the authored
/// +/-60 clamped to a mean rail of nineteen cells and gateBeatHalfCells was
/// inert. Two things fix it and both are load-bearing: the seat is GRADED
/// (DeepRoadGraph.SeatPatrol prefers the trunk), and the bound is a
/// GRAPH-DISTANCE ball (DeepRoadGraph.BeatSet) rather than an index range on
/// one rail, because AncientSiteBuilder chops the trunk wherever it seats a
/// site and an index-bounded beat is therefore bounded by site placement.
/// </summary>
public class DwarvenPatrolController : MonoBehaviour
{
    public static DwarvenPatrolController Instance { get; private set; }

    /// <summary>What the road actually fields, exposed READ-ONLY so Road Breach
    /// Report resolves a skirmish against the authored encounter rather than a
    /// copy of it.
    ///
    /// THIS REPLACES A MIRROR THAT WAS ALREADY WRONG-SHAPED. Commands.cs held
    /// `private const int GateBeatHalfCells = 60`, transcribed from the
    /// inspector, so the report's beat and the squad's beat were two numbers
    /// free to disagree -- in a report whose own canon entry praises it for
    /// CALLING SeatPatrol and BeatSet rather than restating them. The seat and
    /// the bound could not drift and the radius could. It can no longer.</summary>
    public int GateSquadSize => gateSquadSize;
    public int RoadSquadSize => roadSquadSize;
    public int GateBeatHalfCells => gateBeatHalfCells;
    public MonsterDefinition GuardDefinition => guardDefinition;

    [Header("Bodies")]
    [Tooltip("The dwarven guard. Its PREFAB carries the stats -- a DungeonMonster " +
             "with no LootTable component, which is what enforces canon 44's " +
             "no-loot rule. Leave unassigned and patrols stay dormant, exactly " +
             "as they do with no sprites: an invisible guard who can be killed " +
             "for standing is worse than no guard.")]
    [SerializeField] private MonsterDefinition guardDefinition;

    [Header("Sprites")]
    [Tooltip("Optional OVERRIDE for the prefab's own sprite, dealt across " +
             "patrols. Falls back to the caravan's walker list. Left empty the " +
             "prefab draws itself, which is the ordinary case now that a guard " +
             "is a real body.")]
    [SerializeField] private List<Sprite> patrolSprites = new List<Sprite>();
    // sortingLayerName and sortingOrder are DELETED rather than suppressed.
    // They fed DwarfWalkerPuppet.Create, which built a bare GameObject and its
    // own SpriteRenderer and therefore had to be told where to draw. A guard is
    // a prefab body now and brings a renderer with its sorting already
    // authored on it; a controller-level override would be a second source of
    // truth for the same two values, free to disagree with the prefab and
    // invisible when it did.

    [Header("Routes")]
    [Tooltip("Radius of the gatehouse patrol's beat, in STEPS THROUGH THE ROAD " +
             "NETWORK from its seat -- not cells along one rail. The site pass " +
             "splits the trunk wherever it seats a site, so an index range is " +
             "bounded by site placement rather than by this number: measured " +
             "on the live floor, 60 holds 61 cells as an index window on one " +
             "approach against 124 as a graph ball across both.")]
    [SerializeField, Min(10)] private int gateBeatHalfCells = 60;
    [SerializeField, Min(0)] private int villagePatrolCount = 2;
    [Tooltip("Bodies in the gatehouse beat. ONE, deliberately: a lone 100 HP " +
             "guard loses to four kobolds, which is exactly ThievesByTier at " +
             "tier 5, so the den can eventually take the gate and cannot take " +
             "it early.")]
    [SerializeField, Min(1)] private int gateSquadSize = 1;
    [Tooltip("Bodies in the roaming road patrol on the outpost's floor. TWO: " +
             "two guards beat four kobolds comfortably, so the roaming pair is " +
             "the obstacle and the lone gate guard is the opening.")]
    [SerializeField, Min(1)] private int roadSquadSize = 2;

    [Header("Movement")]
    [Tooltip("Plain speed, not day-derived: a patrol loops with no arrival to " +
             "keep, so the days constraint does not bind it.")]
    [SerializeField, Min(0.2f)] private float patrolSpeed = 2.2f;
    [SerializeField, Min(0f)] private float endPauseSeconds = 2f;
    [Tooltip("Longer pause at a broken end -- the stop-and-look beat.")]
    [SerializeField, Min(0f)] private float brokenEndPauseSeconds = 3f;
    [Tooltip("World units between bodies in a squad's trailing column.")]
    [SerializeField, Min(1)] private int columnSpacingCells = 2;

    [Header("Reactions")]
    [SerializeField, Min(1f)] private float watchRadius = 8f;

    private class Body
    {
        public DungeonMonster monster;
        public DwarfWalkerPuppet puppet;
        public int slot;
        public int deathDay = -1;      // -1 = alive, otherwise the day it fell
        public readonly List<Vector3> pathBuf = new List<Vector3>(2);
        public int cachedFrom = -1, cachedTo = -1;
    }

    private class Patrol
    {
        public int id;                          // stable across a session AND a save
        public FloorRoot floor;
        public DeepRoadGraph.Graph graph;
        public readonly List<Body> bodies = new List<Body>();
        public int rail, index, direction;      // walk-cell cursor for the SQUAD
        // The beat, as the cells it contains. NULL on a roaming squad, and
        // `bounded` says which rather than leaving an empty set to mean both --
        // "not filled in" and "everywhere" must never look alike.
        public HashSet<long> beat;
        public bool bounded;
        // homeRail and homeIndex were DELETED here. They were assigned at three
        // sites and read at none since the class was written; the beat set is
        // the only job they could have had, and it takes its seat directly.
        public float pauseUntil;
        public float scanAt;
        public float stepProgress;
        public bool watching;
        public bool suspended;                  // combat held the squad last tick
    }

    private readonly List<Patrol> patrols = new List<Patrol>();
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();
    private bool gateSpawned, villageSpawned;
    private float establishPollAt;
    private bool subscribed;

    // The ledger: "<patrolId>:<slot>:<deathDay>". Static so it can be restored
    // before any patrol has been spawned -- the load path runs long before the
    // outpost poll establishes anything, exactly as the caravan's schedule does.
    private static readonly Dictionary<string, int> deadSlots = new Dictionary<string, int>();

    private static string SlotKey(int patrolId, int slot) => patrolId + ":" + slot;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (subscribed || DayNightCycle.Instance == null) return;
        DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Late subscribe: this controller can wake before the day clock does.
        if (!subscribed && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
            subscribed = true;
        }

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

    // -- Persistence ---------------------------------------------------------

    /// <summary>The dead, for the save. Footsteps are still not persisted; an
    /// absence is.</summary>
    public static List<string> DeadForSave()
    {
        var list = new List<string>();
        foreach (var kv in deadSlots) list.Add(kv.Key + ":" + kv.Value);
        return list;
    }

    public static void RestoreDeadFromSave(List<string> saved)
    {
        deadSlots.Clear();
        if (saved == null) return;
        foreach (var entry in saved)
        {
            if (string.IsNullOrEmpty(entry)) continue;
            var parts = entry.Split(':');
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[0], out int pid)) continue;
            if (!int.TryParse(parts[1], out int slot)) continue;
            if (!int.TryParse(parts[2], out int day)) continue;
            deadSlots[SlotKey(pid, slot)] = day;
        }
    }

    /// <summary>Fresh dungeon, every guard back on his feet. The statics outlive
    /// a slot switch, so one core must never wake up remembering another core's
    /// dead.</summary>
    public static void ResetForNewGame() => deadSlots.Clear();

    // -- Dawn ----------------------------------------------------------------

    /// <summary>Losses are made good overnight and never during the day -- the
    /// den's own rhythm, for the den's own reason: instant replacement makes a
    /// patrol impossible to finish, and no replacement at all makes one killing
    /// permanent. A day of quiet road is the price either way.</summary>
    private void HandleDayStarted()
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        foreach (var p in patrols)
        {
            foreach (var b in p.bodies)
            {
                if (b.monster != null) continue;
                if (b.deathDay < 0) continue;
                if (b.deathDay >= today) continue;   // fell today; tomorrow, not tonight
                deadSlots.Remove(SlotKey(p.id, b.slot));
                b.deathDay = -1;
                RaiseBody(p, b);
            }
        }
    }

    // -- Spawning ------------------------------------------------------------

    private void TrySpawn()
    {
        if (!gateSpawned
            && DwarvenOutpostController.Instance != null
            && DwarvenOutpostController.Instance.Established)
            gateSpawned = SpawnOutpostPatrols();

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

    private bool Dormant()
    {
        if (guardDefinition != null && guardDefinition.prefab != null) return false;
        WarnDormantOnce();
        return true;
    }

    /// <summary>Both of the outpost floor's patrols: the bounded gatehouse beat
    /// and the roaming road squad. THEY SHARE A FLOOR ON PURPOSE -- the kobold
    /// den is on the outpost's floor and the village is one below, so a second
    /// patrol placed at the village would have put no dwarf in front of a kobold
    /// at all.</summary>
    private bool SpawnOutpostPatrols()
    {
        var floor = FloorWithOutpost(out var site);
        if (floor == null || site == null) return false;
        if (Dormant()) return true;

        var graph = DeepRoadGraph.Build(floor.FeatureGenerator.FeatureData.roads);
        if (graph.rails.Count == 0) return true;   // a roadless outpost patrols nowhere
        // SeatPatrol rather than NearestWalkCell: the nearest rail to this
        // anchor is the outpost's own lane or approach spur on every seed
        // measured, and neither is the road the guard is there to guard.
        if (!DeepRoadGraph.SeatPatrol(graph, site.anchorCell.ToVector3Int(),
                out int rail, out int index)) return true;

        var beat = DeepRoadGraph.BeatSet(graph, rail, index, gateBeatHalfCells);
        if (beat.Count <= 1)
        {
            // A beat of one cell is a squad standing still, which is the fault
            // this replaced wearing different clothes. Say so and let it roam
            // rather than pinning it silently.
            Debug.LogWarning("[DwarvenPatrol] Gate beat resolved to " + beat.Count
                           + " cell(s); the squad will roam instead. Check the "
                           + "outpost's road graph.");
            beat = null;
        }

        var gate = new Patrol
        {
            id = 0,
            floor = floor,
            graph = graph,
            rail = rail,
            index = index,
            direction = 1,
            beat = beat,
            bounded = beat != null,
        };
        BuildSquad(gate, gateSquadSize);
        patrols.Add(gate);

        // No beat window: the road squad wanders the whole floor network the way
        // the village pair does, picking a connected rail at each junction.
        var road = new Patrol
        {
            id = 1,
            floor = floor,
            graph = graph,
            rail = rail,
            index = index,
            direction = -1,          // sets off the other way from the gate beat
        };
        BuildSquad(road, roadSquadSize);
        patrols.Add(road);
        return true;
    }

    private bool SpawnVillagePatrols()
    {
        var floor = FloorWithVillage(out var site);
        if (floor == null || site == null) return false;
        if (Dormant()) return true;

        var graph = DeepRoadGraph.Build(floor.FeatureGenerator.FeatureData.roads);
        if (graph.rails.Count == 0) return true;
        if (!DeepRoadGraph.NearestWalkCell(graph, site.anchorCell.ToVector3Int(),
                out int homeRail, out int homeIndex)) return true;

        for (int i = 0; i < villagePatrolCount; i++)
        {
            var p = new Patrol
            {
                id = 2 + i,          // ids 0 and 1 belong to the outpost floor
                floor = floor,
                graph = graph,
                rail = homeRail,
                index = homeIndex,
                direction = i % 2 == 0 ? 1 : -1,   // the pair sets off opposite ways
            };
            BuildSquad(p, 1);
            patrols.Add(p);
        }
        return true;
    }

    private void BuildSquad(Patrol p, int size)
    {
        for (int i = 0; i < size; i++)
        {
            var b = new Body { slot = i };
            p.bodies.Add(b);
            // A guard killed before this session still lies where he fell.
            if (deadSlots.TryGetValue(SlotKey(p.id, i), out int day))
            {
                b.deathDay = day;
                continue;
            }
            RaiseBody(p, b);
        }
    }

    /// <summary>Stand a guard up at his squad's current cell.</summary>
    private void RaiseBody(Patrol p, Body b)
    {
        if (guardDefinition == null || guardDefinition.prefab == null) return;
        if (p.floor == null || p.floor.TileInfluence == null) return;

        var walk = p.graph.rails[p.rail].walk;
        int idx = Mathf.Clamp(p.index - b.slot * p.direction * columnSpacingCells,
                              0, walk.Count - 1);
        Vector3 at = p.floor.TileInfluence.CellToWorld(walk[idx]);

        var monster = Instantiate(guardDefinition.prefab, at, Quaternion.identity);
        monster.transform.SetParent(p.floor.transform, true);
        monster.name = "DwarvenGuard" + p.id + "_" + b.slot;
        // Normal rather than Aggressive: Aggressive would have a guard cut down
        // pilgrims on the road, and the Holds' quarrel with the Church is the
        // matrix's business rather than a stance's.
        monster.InitialiseAsFactionBody(p.floor, guardDefinition, FactionId.Dwarves,
                                        FactionBodyRole.Guard, MonsterAggression.Normal);

        var puppet = DwarfWalkerPuppet.AttachTo(monster.gameObject);
        puppet.Speed = patrolSpeed;
        var over = PickSprite(p.id + b.slot);
        if (over != null) puppet.SetSprite(over);

        b.monster = monster;
        b.puppet = puppet;
        b.cachedFrom = -1;
        b.cachedTo = -1;

        var captured = b;
        var capturedPatrol = p;
        monster.OnDied += _ => HandleBodyDied(capturedPatrol, captured);
    }

    private void HandleBodyDied(Patrol p, Body b)
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        b.deathDay = today;
        deadSlots[SlotKey(p.id, b.slot)] = today;

        // The wisp speaks the FIRST time the dungeon is responsible, and only
        // then. A trap laid near the road bills standing for a death the player
        // never chose, and a bill nobody can trace is a bug in the player's
        // model of the game rather than a consequence.
        bool ours = b.monster != null && b.monster.DungeonDealtDamage;
        b.monster = null;
        b.puppet = null;
        if (ours) WispCompanion.Instance?.Speak("dwarf_slain_first");
    }

    private bool warned;
    private void WarnDormantOnce()
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning("[DwarvenPatrol] No guard definition or prefab assigned - " +
                         "patrols stay dormant until one is.");
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
        bool anyAlive = false;
        bool inCombat = false;
        foreach (var b in p.bodies)
        {
            if (b.monster == null) continue;
            anyAlive = true;
            if (b.monster.CombatHoldsBody) inCombat = true;
        }
        if (!anyAlive) return;   // the dawn puts them back up

        if (inCombat)
        {
            if (!p.suspended)
            {
                p.suspended = true;
                foreach (var b in p.bodies)
                    if (b.puppet != null) b.puppet.Suspended = true;
            }
            return;
        }

        if (p.suspended)
        {
            // Handing the bodies back. Every walker adopts where its body now
            // stands, and the squad cursor is re-derived from the leader's cell
            // -- a fight can end a long way from where it started, and resuming
            // the old cursor would walk everyone back through the rock.
            p.suspended = false;
            Body leader = null;
            foreach (var b in p.bodies)
            {
                if (b.monster == null || b.puppet == null) continue;
                b.puppet.Suspended = false;
                b.puppet.SnapTo(b.monster.transform.position);
                b.cachedFrom = -1;
                b.cachedTo = -1;
                if (leader == null) leader = b;
            }
            if (leader != null
                && DeepRoadGraph.NearestWalkCell(p.graph,
                       p.floor.TileInfluence.WorldToCell(leader.monster.transform.position),
                       out int rail, out int index))
            {
                p.rail = rail;
                p.index = index;
                p.stepProgress = 0f;
            }
        }

        // Reactions, throttled.
        if (Time.time >= p.scanAt)
        {
            p.scanAt = Time.time + 0.5f;
            ScanReactions(p);
        }

        bool held = p.watching || Time.time < p.pauseUntil;
        foreach (var b in p.bodies)
            if (b.puppet != null) b.puppet.Frozen = held;
        if (held) return;

        // Step cell to cell along the current rail. Fractional progress carries
        // across cells so speed is exact regardless of frame rate.
        p.stepProgress += patrolSpeed * dt;
        while (p.stepProgress >= 1f)
        {
            p.stepProgress -= 1f;
            StepOneCell(p);
        }

        var walk = p.graph.rails[p.rail].walk;
        foreach (var b in p.bodies)
        {
            if (b.monster == null || b.puppet == null) continue;
            // The trailing column: body i walks i spacings behind the leader on
            // the squad's own cursor, so a squad needs no second path model.
            int here = Mathf.Clamp(p.index - b.slot * p.direction * columnSpacingCells,
                                   0, walk.Count - 1);
            int next = Mathf.Clamp(here + p.direction, 0, walk.Count - 1);
            if (b.cachedFrom != here || b.cachedTo != next)
            {
                b.cachedFrom = here; b.cachedTo = next;
                b.pathBuf.Clear();
                b.pathBuf.Add(p.floor.TileInfluence.CellToWorld(walk[here]));
                b.pathBuf.Add(p.floor.TileInfluence.CellToWorld(walk[next]));
                b.puppet.SetPath(b.pathBuf);
            }
            b.puppet.SetDistance(p.stepProgress * b.puppet.PathLength);
        }

        FirstSightingLine(p, walk[p.index]);
    }

    private void StepOneCell(Patrol p)
    {
        var rail = p.graph.rails[p.rail];
        int next = p.index + p.direction;

        bool atRailEnd = next < 0 || next > rail.walk.Count - 1;

        // THE BEAT IS TESTED ONLY FROM INSIDE IT, which is StandsOnTakenGround's
        // rule a few lines below arriving a second time for the same reason: a
        // squad a fight has dragged off its beat must be able to walk back, and
        // a test that fired wherever it stood would turn it on every step and
        // jitter it in place. Leaving the beat is blocked; being outside one is
        // not a trap.
        bool onBeat = p.bounded
                   && p.beat.Contains(DeepRoadGraph.BeatKey(p.rail, p.index));
        bool leavesBeat = onBeat && !atRailEnd
                       && !p.beat.Contains(DeepRoadGraph.BeatKey(p.rail, next));

        if (atRailEnd || leavesBeat)
        {
            bool brokenHere = atRailEnd && RoadStopsDead(rail, next > 0);

            // A bounded squad MAY cross a junction now. It is held by the beat
            // set on the far side instead, so its range stops being an accident
            // of where the site pass happened to cut the trunk.
            if (atRailEnd && !brokenHere
                && TryTurnAtJunction(p, next > 0, onBeat)) return;

            // The beat: stop, look at where the road stops, turn back.
            p.pauseUntil = Time.time
                + (brokenHere ? brokenEndPauseSeconds : endPauseSeconds);
            if (brokenHere && rail.walk.Count >= 2)
            {
                // Look PAST the collapse: extend the last walk step's own
                // direction, so a north-running spur gets a northward stare.
                int endIdx = p.direction > 0 ? rail.walk.Count - 1 : 0;
                int prevIdx = p.direction > 0 ? rail.walk.Count - 2 : 1;
                var endW = p.floor.TileInfluence.CellToWorld(rail.walk[endIdx]);
                var prevW = p.floor.TileInfluence.CellToWorld(rail.walk[prevIdx]);
                FaceAll(p, endW + (endW - prevW));
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
            var hereW = p.floor.TileInfluence.CellToWorld(rail.walk[p.index]);
            var aheadW = p.floor.TileInfluence.CellToWorld(rail.walk[next]);
            FaceAll(p, aheadW + (aheadW - hereW));
            p.direction = -p.direction;
            return;
        }

        p.index = next;
    }

    private static void FaceAll(Patrol p, Vector3 worldPos)
    {
        foreach (var b in p.bodies)
            if (b.puppet != null) b.puppet.Face(worldPos);
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
    /// immediate about-face when the junction offers anywhere else to go.
    ///
    /// keepToBeat filters the options to rails a bounded squad may enter. It is
    /// passed FALSE when the squad is already off its beat, so a guard a fight
    /// carried away can find his way back rather than being refused at the one
    /// junction that would return him.</summary>
    private bool TryTurnAtJunction(Patrol p, bool leavingByEnd, bool keepToBeat)
    {
        var rail = p.graph.rails[p.rail];
        int node = leavingByEnd ? rail.nodeEnd : rail.nodeStart;
        if (node < 0 || node >= p.graph.adjacency.Count) return false;

        var options = new List<(int rail, bool atStart)>();
        foreach (var opt in p.graph.adjacency[node])
        {
            if (opt.rail == p.rail) continue;
            var other = p.graph.rails[opt.rail];
            if (other.walk.Count == 0) continue;
            if (keepToBeat)
            {
                int entry = opt.atStart ? 0 : other.walk.Count - 1;
                if (!p.beat.Contains(DeepRoadGraph.BeatKey(opt.rail, entry))) continue;
            }
            options.Add(opt);
        }
        if (options.Count == 0) return false;

        var pick = options[Random.Range(0, options.Count)];
        p.rail = pick.rail;
        var newWalk = p.graph.rails[p.rail].walk;
        p.index = pick.atStart ? 0 : newWalk.Count - 1;
        p.direction = pick.atStart ? 1 : -1;
        foreach (var b in p.bodies) { b.cachedFrom = -1; b.cachedTo = -1; }
        return true;
    }

    // -- Reactions -----------------------------------------------------------

    /// <summary>The watch beat, and ONLY the watch beat.
    ///
    /// THE WITHDRAWAL IS GONE, and its removal is the fork-5 reversal arriving
    /// where it is most visible. A patrol used to retreat toward home when a
    /// Holy Order adventurer came near, because it could not fight. It can now,
    /// and the entry-7 matrix says it should: Dwarves against the Holy Order is
    /// the one Hostile edge the Deep Holds carry. So the Church gets a brawl
    /// where it used to get a back, and nothing here needs to arrange it --
    /// DungeonMonster.ScanForHostiles does, through EngagesAdventurer.
    ///
    /// What survives is the halt-and-look at a NON-hostile adventurer, which was
    /// always flavour and is still worth having: it is the one moment a player
    /// sees a dwarf notice them and decide to do nothing.</summary>
    private void ScanReactions(Patrol p)
    {
        p.watching = false;
        if (p.floor?.Entities == null) return;

        Body lead = null;
        foreach (var b in p.bodies)
            if (b.monster != null) { lead = b; break; }
        if (lead == null) return;

        var pos = lead.monster.transform.position;
        p.floor.Entities.WithinRadius(pos, watchRadius, advBuf);

        DungeonAdventurer nearest = null;
        float nearestSq = float.MaxValue;
        foreach (var a in advBuf)
        {
            if (a == null) continue;
            // Someone the guards are about to fight is not someone they stop to
            // watch. Leaving hostiles in would freeze the squad on the spot the
            // moment a pilgrim party arrived, mid-fight.
            if (FactionRelations.AreHostile(FactionId.Dwarves,
                    AdventurerTypeInfo.FactionOf(a.Type))) continue;
            float d = ((Vector2)(a.transform.position - pos)).sqrMagnitude;
            if (d < nearestSq) { nearestSq = d; nearest = a; }
        }

        if (nearest != null)
        {
            p.watching = true;
            FaceAll(p, nearest.transform.position);
        }
    }

    // -- The road breach (canon 42 stage 2c) -----------------------------------

    /// <summary>Which squad would meet a breach at this cell, as a body count.
    ///
    /// GEOMETRY DECIDES, NOT PROXIMITY. A breach whose nearest walk cell is
    /// inside the gate squad's beat is met by the gate squad; anything else on a
    /// floor the road squad walks is met by the road squad, which is unbounded
    /// and roams the whole network. Zero means nobody is coming, which is a real
    /// answer and not a failure -- a floor whose patrols are dormant, or a
    /// breach on no rail at all.
    ///
    /// THE SAME TEST Road Breach Report USES, deliberately: the report asks
    /// DeepRoadGraph for the nearest walk cell and tests the beat set, and so
    /// does this. A runtime that decided the squad differently would make every
    /// engagement figure in that readout a measurement of something else.</summary>
    public int GuardsMeeting(FloorRoot floor, Vector3Int cell)
    {
        var p = PatrolMeeting(floor, cell);
        if (p == null) return 0;
        int alive = 0;
        foreach (var b in p.bodies) if (b.monster != null) alive++;
        return alive;
    }

    /// <summary>One guard falls to something the player did not do.
    ///
    /// ROUTED THROUGH TakeDamage WITH fromOutsider TRUE, rather than destroying
    /// the body, and the distinction is the whole of it. fromOutsider leaves
    /// dungeonDealtDamage false, so Die() bills no standing, unlocks no bestiary
    /// line, pays no core XP and speaks no dwarf_slain_first -- which is
    /// correct, because the player neither swung nor chose this. Everything
    /// downstream still runs: OnDied reaches HandleBodyDied, the slot enters
    /// dwarvenPatrolDead, and the road is short one guard until the next dawn
    /// puts him back. Destroying the GameObject would have skipped all of it and
    /// left a squad quietly one body light for ever.</summary>
    public bool FellOneAt(FloorRoot floor, Vector3Int cell)
    {
        var p = PatrolMeeting(floor, cell);
        if (p == null) return false;
        foreach (var b in p.bodies)
        {
            if (b.monster == null) continue;
            b.monster.TakeDamage(b.monster.MaxHP + 1f, true);
            return true;
        }
        return false;
    }

    private Patrol PatrolMeeting(FloorRoot floor, Vector3Int cell)
    {
        if (floor == null) return null;
        Patrol roaming = null;
        foreach (var p in patrols)
        {
            if (p.floor != floor || p.graph == null) continue;
            bool anyAlive = false;
            foreach (var b in p.bodies) if (b.monster != null) { anyAlive = true; break; }
            if (!anyAlive) continue;

            if (!DeepRoadGraph.NearestWalkCell(p.graph, cell, out int rail, out int index))
                continue;
            if (p.bounded && p.beat != null
                && p.beat.Contains(DeepRoadGraph.BeatKey(rail, index))) return p;
            if (!p.bounded && roaming == null) roaming = p;
        }
        return roaming;
    }

    // -- First sighting --------------------------------------------------------

    private void FirstSightingLine(Patrol p, Vector3Int cell)    {
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
