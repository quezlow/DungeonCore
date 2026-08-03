using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The dwarven village: the hold the Deep Holds are named for, one floor below
/// the gatehouse (canon 19, part 3).
///
/// The site itself is terrain, placed and persisted by the site builder with
/// SiteData.reservedForVillage set. This controller is the part that is alive:
/// it watches for that site to be revealed, rolls the settlement's name from a
/// roster, marks the Deep Holds encountered (idempotent -- a player can reach
/// this before ever unfogging the gatehouse), and sets a handful of villagers
/// WALKING the lanes. No vendor: they trade at the gate, they live here.
///
/// WALKERS, NOT ENTITIES (The Living Holds). Villagers are DwarfWalkerPuppets
/// -- no pathfinding registry, no combat entity, no adventurer or monster
/// interaction. Each waits a few seconds, hops to a nearby lane cell found by
/// a bounded BFS INSIDE the site's own cells (so nobody strays onto the
/// carriageway or into the rock -- the candidate rule that placed them picks
/// every destination too), and waits again. Night stills the lanes: walkers
/// freeze at dusk on the same clock the caravan camps by.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenOutpostController. No per-floor wiring -- it finds its floor through
/// FloorManager. villagerSprites may stay empty; the village then
/// establishes with nobody drawn yet, exactly as the gatekeeper does.
///
/// WHY THIS POLLS RATHER THAN LISTENING: the same reason the outpost
/// controller does. Fresh discovery runs through RevealSite, but the LOAD path
/// calls UnfogSite directly and never touches RevealSite, so an event would
/// fire for a player who discovers the village this session and stay silent
/// for one who reloaded afterwards. A one-second poll that stops dead the
/// moment the village is established cannot get that wrong. The discovery
/// alert therefore re-fires once per session after a reload, exactly as the
/// outpost's does -- recorded, accepted behaviour.
/// </summary>
public class DwarvenVillageController : MonoBehaviour
{
    public static DwarvenVillageController Instance { get; private set; }

    [Header("Villagers")]
    [Tooltip("Optional. The dwarves who walk the lanes -- any number of " +
             "variants. Assignment is a seeded round-robin over a shuffled " +
             "copy, so counts stay as even as the list allows. Leave empty " +
             "and the village still establishes -- nobody is drawn yet.")]
    [SerializeField] private List<Sprite> villagerSprites = new List<Sprite>();
    [Tooltip("How many villagers walk the lanes. Eight since The Living " +
             "Holds -- they earn the head-count by moving.")]
    [SerializeField, Min(0)] private int villagerCount = 8;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Walking (The Living Holds)")]
    [Tooltip("World units per second. Deliberately slower than any adventurer " +
             "-- these are people at home, not units.")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.2f;
    [Tooltip("Seconds a villager stands between hops (min..max, rolled).")]
    [SerializeField, Min(0.5f)] private float pauseSecondsMin = 4f;
    [SerializeField, Min(0.5f)] private float pauseSecondsMax = 10f;
    [Tooltip("Chebyshev radius, in cells, of one wander hop.")]
    [SerializeField, Min(1)] private int wanderHopCells = 6;

    [Header("Names")]
    [Tooltip("The settlement's name is rolled from this roster, seeded from the " +
             "floor seed and the site id -- deterministic, so it re-derives " +
             "identically on every load and needs no save field.")]
    [SerializeField] private List<string> villageNames = new List<string>
    {
        "The Hearth of the Deep",
        "The Last Hearth",
        "Hearthdeep",
        "The Undervault",
        "Emberhold",
        "Cinderhold",
        "Delvehold",
        "Gravenhold",
    };

    [Header("Discovery Poll")]
    [Tooltip("Seconds between checks for a revealed village. The poll stops " +
             "for good once the village is established.")]
    [SerializeField, Min(0.25f)] private float pollSeconds = 1f;

    private float nextPoll;
    private readonly List<DwarfWalkerPuppet> villagers = new List<DwarfWalkerPuppet>();

    // Wander bookkeeping, index-parallel with villagers.
    private readonly List<float> wanderAt = new List<float>();
    private readonly List<bool> waiting = new List<bool>();
    private readonly HashSet<Vector3Int> laneCells = new HashSet<Vector3Int>();
    private readonly List<Vector3Int> laneCandidates = new List<Vector3Int>();
    private TileInfluenceManager villageInfluence;

    /// <summary>True once the village has been found.</summary>
    public bool Established { get; private set; }

    /// <summary>Floor index the village stands on, or -1.</summary>
    public int VillageFloorIndex { get; private set; } = -1;

    /// <summary>The rolled settlement name, valid once established.</summary>
    public string VillageName { get; private set; } = "";

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
        if (!Established)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + pollSeconds;
            TryEstablish();
            return;
        }
        HandleClick();
        WanderTick();
    }

    private void TryEstablish()
    {
        var floors = FloorManager.Instance;
        if (floors == null) return;

        foreach (var floor in floors.AllFloors)
        {
            if (floor == null) continue;
            var features = floor.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;

            var site = features.GetVillageSite();
            if (site == null) continue;
            if (!features.IsSiteRevealed(site.id)) continue;

            Establish(floor, site);
            return;
        }
    }

    private void Establish(FloorRoot floor, SiteData site)
    {
        Established = true;
        VillageFloorIndex = floor.FloorIndex;

        // Deterministic per run: floor seed times a small prime plus the site
        // id, the same recipe WildMonsterController uses per chamber. The name
        // and the villagers' places re-derive identically on every load, so
        // nothing here needs a save field.
        int worldSeed = DungeonSaveController.Instance != null
            ? DungeonSaveController.Instance.WorldSeed : 0;
        var rng = new System.Random(unchecked(
            FloorManager.DeriveFloorSeed(worldSeed, floor.FloorIndex) * 31 + site.id));

        VillageName = villageNames != null && villageNames.Count > 0
            ? villageNames[rng.Next(villageNames.Count)]
            : "The Hearth of the Deep";

        // Idempotent, and deliberately fired here as well as at the gatehouse:
        // stairs are player-placed, so a run can genuinely reach this floor and
        // walk into the village before the gatehouse site was ever unfogged.
        FactionIntel.NotifyEncounter(FactionId.Dwarves);

        PlaceVillagers(floor, site, rng);

        Vector3 at = villagers.Count > 0
            ? villagers[0].transform.position
            : new Vector3(0f, floor.WorldOriginY, 0f);

        AlertsLog.Instance?.AddAlert(
            "Hearthsmoke in the deep - " + VillageName + " still stands.",
            at, floor.FloorIndex, AlertCategory.Discovery);

        var wisp = WispCompanion.Instance;
        if (wisp != null)
        {
            wisp.Speak("village_first");
            wisp.Excite(0.7f);
        }
    }

    /// <summary>
    /// Stands the villagers on interior lane cells. A candidate cell must have
    /// its two north neighbours carved too -- the builder's own walkable rule
    /// -- which keeps everyone clear of the wall drape AND off the carriageway,
    /// because road cells were subtracted from the site and a cell just south
    /// of the road therefore fails the rule.
    /// </summary>
    private void PlaceVillagers(FloorRoot floor, SiteData site, System.Random rng)
    {
        var influence = floor.TileInfluence;
        if (influence == null || villagerCount <= 0) return;
        if (site.cells == null || site.cells.Count == 0) return;

        // Null-safe deck of variants. A seeded shuffle then a round-robin
        // deal keeps counts as even as the list allows -- pure per-villager
        // random hands out four identical dwarves about one run in eight,
        // which reads as a bug rather than a family.
        var deck = new List<Sprite>();
        if (villagerSprites != null)
            foreach (var s in villagerSprites)
                if (s != null) deck.Add(s);
        if (deck.Count == 0) return;
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        laneCells.Clear();
        foreach (var sv in site.cells) laneCells.Add(sv.ToVector3Int());

        laneCandidates.Clear();
        foreach (var c in laneCells)
            if (laneCells.Contains(new Vector3Int(c.x, c.y + 1, 0))
                && laneCells.Contains(new Vector3Int(c.x, c.y + 2, 0)))
                laneCandidates.Add(c);
        if (laneCandidates.Count == 0) return;
        villageInfluence = influence;
        var candidates = laneCandidates;

        var taken = new List<Vector3Int>();
        const int MinSeparationSq = 16;   // four cells apart reads as a lane, not a queue
        for (int i = 0; i < villagerCount; i++)
        {
            Vector3Int pick = candidates[rng.Next(candidates.Count)];
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var c = candidates[rng.Next(candidates.Count)];
                bool clear = true;
                foreach (var t in taken)
                {
                    long dx = c.x - t.x, dy = c.y - t.y;
                    if (dx * dx + dy * dy < MinSeparationSq) { clear = false; break; }
                }
                if (clear) { pick = c; break; }
            }
            taken.Add(pick);

            var puppet = DwarfWalkerPuppet.Create("DwarvenVillager" + (i + 1),
                deck[i % deck.Count], sortingLayerName, sortingOrder,
                influence.CellToWorld(pick));
            puppet.Speed = walkSpeed;
            villagers.Add(puppet);
            waiting.Add(true);
            // Staggered first hops, or the whole hold sets off in lockstep on
            // the same frame -- which reads as a cutscene, not a town.
            wanderAt.Add(Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax));
        }
    }

    // -- The wander (The Living Holds) ---------------------------------------

    private void WanderTick()
    {
        if (villagers.Count == 0 || villageInfluence == null) return;

        // Night stills the lanes -- same clock the caravan camps by.
        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        for (int i = 0; i < villagers.Count; i++)
        {
            var v = villagers[i];
            if (v == null) continue;
            v.Frozen = night;
            if (night) continue;

            if (!waiting[i])
            {
                if (!v.Arrived) continue;
                waiting[i] = true;
                wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
                continue;
            }
            if (Time.time < wanderAt[i]) continue;
            waiting[i] = !TryHop(v);
            if (waiting[i])   // nowhere to go this time; try again shortly
                wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
        }
    }

    /// <summary>One short hop: a lane candidate within the hop radius, reached
    /// by a bounded BFS through the site's own cells. Both ends and every step
    /// stay inside laneCells, so a villager can never wander onto the
    /// carriageway (subtracted from the site) or into the rock.</summary>
    private bool TryHop(DwarfWalkerPuppet v)
    {
        var from = villageInfluence.WorldToCell(v.LogicalPosition);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var target = laneCandidates[Random.Range(0, laneCandidates.Count)];
            if (target == from) continue;
            if (Mathf.Max(Mathf.Abs(target.x - from.x), Mathf.Abs(target.y - from.y))
                > wanderHopCells) continue;

            var path = BfsPath(from, target);
            if (path == null) continue;

            var world = new List<Vector3>(path.Count);
            foreach (var c in path) world.Add(villageInfluence.CellToWorld(c));
            v.SetPath(world);
            return true;
        }
        return false;
    }

    /// <summary>4-neighbour BFS inside laneCells, capped at 200 expansions --
    /// a hop radius of 6 needs far fewer, so the cap only ever trips on a
    /// target walled off inside the plan, which is then simply skipped.</summary>
    private List<Vector3Int> BfsPath(Vector3Int from, Vector3Int to)
    {
        if (!laneCells.Contains(from) || !laneCells.Contains(to)) return null;
        var prev = new Dictionary<Vector3Int, Vector3Int> { [from] = from };
        var queue = new Queue<Vector3Int>();
        queue.Enqueue(from);
        int expansions = 0;
        Vector3Int[] steps =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
        };
        while (queue.Count > 0 && expansions < 200)
        {
            var c = queue.Dequeue();
            expansions++;
            if (c == to)
            {
                var path = new List<Vector3Int>();
                for (var cur = to; ; cur = prev[cur])
                {
                    path.Add(cur);
                    if (cur == from) break;
                }
                path.Reverse();
                return path;
            }
            foreach (var s in steps)
            {
                var n = c + s;
                if (!laneCells.Contains(n) || prev.ContainsKey(n)) continue;
                prev[n] = c;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (villagers.Count == 0) return;
        if (PauseController.IsGamePaused) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        foreach (var v in villagers)
        {
            if (v == null) continue;
            world.z = v.transform.position.z;
            if (Vector3.Distance(world, v.transform.position) > clickRadius) continue;
            // Speak() honours the line's own once flag; the greeting repeats.
            WispCompanion.Instance?.Speak("village_greeting");
            return;
        }
    }
}
