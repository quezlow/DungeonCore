using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buried remains (canon 17): each floor hides a few deterministic sites in old
/// stone. Claiming near one makes the wisp murmur -- something waits in the
/// surrounding rock -- and the first live dig of the site cell grants a Bestiary
/// discovery matched to the core's type: lowest locked rung of the affinity
/// ladder, full tome-style bypass. An exhausted ladder pays a little research
/// instead, so a find is never a dead dig.
///
/// Sites come from TerrainTypeMap.GetBuriedSites (seeded; stable across loads);
/// the hooks are the live-claims-only OnTileClaimed / OnTileMined events. The
/// whisper fires once per site, pins the claimed cell (never the site), and
/// names no reward. Consumed and sensed sites persist per floor.
///
/// GrantExternalDiscovery is the re-entry point for canon 18's Holy Ground
/// desecration reward.
///
/// SCENE SETUP: add to the managers object. No wiring.
/// </summary>
public class BuriedRemainsController : MonoBehaviour
{
    public static BuriedRemainsController Instance { get; private set; }

    [Tooltip("Deterministic buried sites per floor.")]
    [SerializeField, Min(0)] private int sitesPerFloor = 2;

    [Tooltip("Minimum Chebyshev distance of a site from the floor's core cell.")]
    [SerializeField, Min(1)] private int minDistFromCenter = 6;

    [Tooltip("Claiming within this Chebyshev distance of an unfound site makes the wisp murmur (0 disables).")]
    [SerializeField, Min(0)] private int senseRadius = 2;

    [Tooltip("Research points paid when the Bestiary ladder is already exhausted.")]
    [SerializeField, Min(0)] private int duplicateResearchPoints = 10;

    [Header("The resting place (canon 34)")]
    [Tooltip("Optional. Spawned where the remains are found. Null-safe: without art " +
             "the beat still plays, it simply has no sprite.")]
    [SerializeField] private GameObject remainsPrefab;

    // Armed by the first descent below floor 0; the wisp admits it at the NEXT
    // dawn, so it never stacks on the descent's own echo.
    private bool restArmed;
    private bool restAnnounced;
    private bool restFound;
    private bool dawnHooked;

    private readonly HashSet<TileInfluenceManager> hooked = new();
    // Per-floor handler closures, kept so they can be unsubscribed. A lambda
    // subscribed inline cannot be removed (no stable reference), which on scene
    // teardown left this controller reachable from live floor events. Storing
    // the delegates here makes OnDisable able to detach every one.
    private readonly Dictionary<TileInfluenceManager, System.Action<Vector3Int>> minedHandlers = new();
    private readonly Dictionary<TileInfluenceManager, System.Action<Vector3Int>> claimedHandlers = new();
    private readonly Dictionary<int, List<Vector3Int>> siteCache = new();
    private readonly Dictionary<int, HashSet<Vector3Int>> consumed = new();
    private readonly Dictionary<int, HashSet<Vector3Int>> sensed = new();

    private void OnEnable()
    {
        Instance = this;
        if (!dawnHooked && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDawn;
            dawnHooked = true;
        }
    }

    private void OnDisable()
    {
        // Detach every per-floor hook so no closure outlives this controller.
        foreach (var kv in minedHandlers)
            if (kv.Key != null) kv.Key.OnTileMined -= kv.Value;
        foreach (var kv in claimedHandlers)
            if (kv.Key != null) kv.Key.OnTileClaimed -= kv.Value;
        minedHandlers.Clear();
        claimedHandlers.Clear();
        hooked.Clear();

        if (dawnHooked && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDawn;
        dawnHooked = false;

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Late dawn hook: DayNightCycle may not exist yet at OnEnable.
        if (!dawnHooked && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDawn;
            dawnHooked = true;
        }

        // Floors spawn dynamically; hook each one's events as it appears.
        var fm = FloorManager.Instance;
        if (fm == null) return;
        foreach (var floor in fm.AllFloors)
        {
            if (floor?.TileInfluence == null || hooked.Contains(floor.TileInfluence)) continue;
            var captured = floor;
            var infl = captured.TileInfluence;
            // Named delegates stored per influence manager so OnDisable can detach
            // them; the captured floor keeps the same closure identity in the map.
            System.Action<Vector3Int> onMined = pos => HandleMined(captured, pos);
            System.Action<Vector3Int> onClaimed = pos => HandleClaimed(captured, pos);
            infl.OnTileMined += onMined;
            infl.OnTileClaimed += onClaimed;
            minedHandlers[infl] = onMined;
            claimedHandlers[infl] = onClaimed;
            hooked.Add(infl);
        }
    }

    private void HandleClaimed(FloorRoot floor, Vector3Int pos)
    {
        if (senseRadius <= 0) return;
        if (DungeonSaveController.IsLoading) return;
        if (floor?.TerrainTypeMap == null) return;

        var sites = SitesFor(floor);
        if (sites.Count == 0) return;
        var used = ConsumedFor(floor.FloorIndex);
        var felt = SensedFor(floor.FloorIndex);

        // Mark every in-halo site as sensed, but murmur only once per claim.
        bool spoke = false;
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            if (used.Contains(site) || felt.Contains(site)) continue;
            int d = Mathf.Max(Mathf.Abs(site.x - pos.x), Mathf.Abs(site.y - pos.y));
            if (d > senseRadius) continue;
            felt.Add(site);
            if (spoke) continue;
            spoke = true;
            // The ledger is research-gated but the sensed flag persists, so a
            // pre-research whisper would be consumed in silence forever. The
            // wisp is never gated; the ledger keeps the record once learned.
            const string murmur = "Something waits in the stone nearby. Dig, and I will remember.";
            WispCompanion.Instance?.SpeakLine(murmur);
            AlertsLog.Instance?.AddAlert(
                murmur,
                floor.TileInfluence.CellToWorld(pos), floor.FloorIndex, AlertCategory.Discovery);
        }
    }

    private void HandleMined(FloorRoot floor, Vector3Int pos)
    {
        if (DungeonSaveController.IsLoading) return;
        if (floor?.TerrainTypeMap == null) return;

        // The resting place first, and it never falls through to the ordinary
        // grant: it is not a buried-remains site and must not pay like one.
        if (!restFound && floor.FloorIndex == 0 && CoreMemory.Lived)
        {
            var rest = floor.FeatureGenerator?.RestingPlace;
            if (rest != null && rest.restCell.ToVector3Int() == pos)
            {
                // Dug before the wisp was willing to mention it: the stone is
                // inert, and stays inert, until the descent arms it.
                if (!restArmed) return;
                RevealRestingPlace(floor, pos);
                return;
            }
        }

        var sites = SitesFor(floor);
        if (!sites.Contains(pos)) return;

        var used = ConsumedFor(floor.FloorIndex);
        if (!used.Add(pos)) return;

        Grant(floor.TileInfluence.CellToWorld(pos), floor.FloorIndex);
    }

    /// <summary>The shared grant: Bestiary ladder first, consolation research after.
    /// Public via GrantExternalDiscovery for the desecration reward (canon 18).</summary>
    private void Grant(Vector3 worldPos, int floorIndex)
    {
        var core = DungeonCore.Instance;
        var coreType = core != null ? core.DungeonType : DungeonType.None;
        var node = ResearchController.Instance != null
            ? ResearchController.Instance.GrantBuriedDiscovery(coreType)
            : null;

        if (node != null)
        {
            const string line = "Old bones in old stone -- and a memory still in them.";
            WispCompanion.Instance?.SpeakLine(line);
            AlertsLog.Instance?.AddAlert(line, worldPos, floorIndex, AlertCategory.Discovery);
            DeedsController.Instance?.NotifyMoment("first_buried");
            CoreMemory.Recall(CoreMemory.FirstBuried);
        }
        else
        {
            core?.AddResearch(duplicateResearchPoints);
            const string line = "Bones I already understand. Their patience is worth a little insight.";
            WispCompanion.Instance?.SpeakLine(line);
            AlertsLog.Instance?.AddAlert(line, worldPos, floorIndex, AlertCategory.Discovery);
        }
    }

    /// <summary>Hand out a buried discovery from outside the dig flow (canon 18:
    /// the Holy Ground desecration reward calls this).</summary>
    public void GrantExternalDiscovery(Vector3 worldPos, int floorIndex)
        => Grant(worldPos, floorIndex);

    // -- The resting place (canon 34) ------------------------------

    public bool RestArmedForSave => restArmed;
    public bool RestAnnouncedForSave => restAnnounced;
    public bool RestFoundForSave => restFound;

    public void RestoreRestState(bool armed, bool announced, bool found)
    {
        restArmed = armed;
        restAnnounced = announced;
        restFound = found;
    }

    /// <summary>Called on the first descent below floor 0. The player has stopped
    /// being a hole in the ground and started being a dungeon; that is when it is
    /// worth showing them what they used to be. Nothing is said until the next
    /// dawn, so this never lands on top of the descent's own memory echo.</summary>
    public void ArmRestingPlace()
    {
        if (restFound || restArmed) return;
        if (!CoreMemory.Lived) return;   // no life, no body
        restArmed = true;
    }

    private void HandleDawn()
    {
        if (!restArmed || restAnnounced || restFound) return;
        if (DungeonSaveController.IsLoading) return;

        var floor = FloorManager.Instance?.GetFloor(0);
        var rest = floor?.FeatureGenerator?.RestingPlace;
        if (floor?.TileInfluence == null || rest == null) return;

        restAnnounced = true;
        var world = floor.TileInfluence.CellToWorld(rest.restCell.ToVector3Int());

        WispCompanion.Instance?.Speak("rest_murmur");
        AlertsLog.Instance?.AddAlert(
            "There is a pocket in the stone by your own mouth that I have never let you look at.",
            world, 0, AlertCategory.Discovery);
    }

    /// <summary>The stone is open. Whatever the player expected, this is the one
    /// discovery in the dungeon that pays nothing.</summary>
    private void RevealRestingPlace(FloorRoot floor, Vector3Int cell)
    {
        restFound = true;
        var world = floor.TileInfluence.CellToWorld(cell);

        if (remainsPrefab != null)
        {
            var go = Instantiate(remainsPrefab, world, Quaternion.identity);
            go.transform.SetParent(floor.transform, true);
        }

        WispCompanion.Instance?.Speak("rest_found_1");
        WispCompanion.Instance?.Speak("rest_found_2");
        WispCompanion.Instance?.Speak(CoreMemory.EmptyHanded ? "rest_found_empty" : "rest_found_3");

        AlertsLog.Instance?.AddAlert(
            "You have found yourself.", world, 0, AlertCategory.Discovery);
        DeedsController.Instance?.NotifyMoment("found_self");
    }

    // -- The diggers' door in (canon 42) ---------------------------

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

    // -- Save / restore surface ------------------------------------

    public void GatherConsumed(FloorRoot floor, List<SerializableVector3Int> into)
    {
        if (floor == null || into == null) return;
        if (!consumed.TryGetValue(floor.FloorIndex, out var used)) return;
        foreach (var cell in used) into.Add(SerializableVector3Int.From(cell));
    }

    public void RestoreConsumed(FloorRoot floor, List<SerializableVector3Int> cells)
    {
        if (floor == null || cells == null) return;
        var used = ConsumedFor(floor.FloorIndex);
        foreach (var c in cells) used.Add(c.ToVector3Int());
    }

    public void GatherSensed(FloorRoot floor, List<SerializableVector3Int> into)
    {
        if (floor == null || into == null) return;
        if (!sensed.TryGetValue(floor.FloorIndex, out var felt)) return;
        foreach (var cell in felt) into.Add(SerializableVector3Int.From(cell));
    }

    public void RestoreSensed(FloorRoot floor, List<SerializableVector3Int> cells)
    {
        if (floor == null || cells == null) return;
        var felt = SensedFor(floor.FloorIndex);
        foreach (var c in cells) felt.Add(c.ToVector3Int());
    }

    // -- Internals -------------------------------------------------

    private List<Vector3Int> SitesFor(FloorRoot floor)
    {
        if (siteCache.TryGetValue(floor.FloorIndex, out var cached)) return cached;
        var sites = floor.TerrainTypeMap.GetBuriedSites(sitesPerFloor, minDistFromCenter);

        // An Ossuary would be a poor ossuary with nothing in it: every placed
        // one guarantees exactly one buried-remains cell in its masonry
        // (canon 19). Appended here rather than sampled by GetBuriedSites,
        // because that sampler accepts only Stone and Granite, and site
        // masonry has been retyped to Ruins long before anyone digs.
        AppendOssuaryRemains(floor, sites);

        // Sites may not have generated yet on a floor that somehow takes a
        // claim first; caching then would lose the ossuary cells forever, and
        // GetBuriedSites is deterministic, so recomputing costs nothing.
        if (floor.FeatureGenerator != null && floor.FeatureGenerator.HasGenerated)
            siteCache[floor.FloorIndex] = sites;
        return sites;
    }

    /// <summary>One guaranteed buried-remains cell per placed Ossuary (canon
    /// 19), chosen deterministically from its masonry so saves and reloads
    /// agree. The cell must border the carved interior: the claim-halo murmur
    /// senses from CLAIMED tiles, and masonry buried two deep in the ring
    /// walls would be found only by accident.</summary>
    private static void AppendOssuaryRemains(FloorRoot floor, List<Vector3Int> sites)
    {
        var data = floor.FeatureGenerator != null ? floor.FeatureGenerator.FeatureData : null;
        if (data == null || data.sites == null) return;

        foreach (var site in data.sites)
        {
            if (site == null || site.archetype != SiteArchetype.Ossuary) continue;
            if (site.ruinsCells == null || site.ruinsCells.Count == 0) continue;

            var carved = new HashSet<Vector3Int>();
            foreach (var sv in site.cells) carved.Add(sv.ToVector3Int());

            // Candidates walked in serialised order, so the pick survives a
            // reload byte for byte.
            var candidates = new List<Vector3Int>();
            foreach (var sv in site.ruinsCells)
            {
                var cell = sv.ToVector3Int();
                bool bordersFloor = false;
                for (int dx = -1; dx <= 1 && !bordersFloor; dx++)
                    for (int dy = -1; dy <= 1 && !bordersFloor; dy++)
                        if (carved.Contains(new Vector3Int(cell.x + dx, cell.y + dy, cell.z)))
                            bordersFloor = true;
                if (bordersFloor) candidates.Add(cell);
            }
            if (candidates.Count == 0) continue;

            // A cheap hash on the site's identity rather than an RNG: there is
            // no seed for two machines to disagree about and no draw order to
            // preserve. The constants are the usual spatial-hash primes.
            int h = unchecked(site.id * 73856093
                            ^ site.anchorCell.x * 19349663
                            ^ site.anchorCell.y * 83492791);
            var pick = candidates[(h & int.MaxValue) % candidates.Count];
            if (!sites.Contains(pick)) sites.Add(pick);
        }
    }

    private HashSet<Vector3Int> ConsumedFor(int floorIndex)
    {
        if (!consumed.TryGetValue(floorIndex, out var used))
        {
            used = new HashSet<Vector3Int>();
            consumed[floorIndex] = used;
        }
        return used;
    }

    private HashSet<Vector3Int> SensedFor(int floorIndex)
    {
        if (!sensed.TryGetValue(floorIndex, out var felt))
        {
            felt = new HashSet<Vector3Int>();
            sensed[floorIndex] = felt;
        }
        return felt;
    }
}