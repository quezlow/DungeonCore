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

    private void OnEnable() { Instance = this; }

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

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
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
        siteCache[floor.FloorIndex] = sites;
        return sites;
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