using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Camp growth: survivors settle the surface. Every adventurer who leaves
/// the dungeon alive (AdventurerParty.MemberEscaped) adds one growth to a
/// receiving camp -- camp.main until its cap, then satellites in unlock
/// order. Growth crosses the profile's authored tier thresholds (Waystation
/// -> Camp -> Settlement -> whatever rows come later, e.g. a Town), and each
/// tier rebuilds the camp's buildout: the commerce anchor facing the way
/// home (cart -> stall -> shop; the wandering merchant's eventual dock)
/// plus the tier's prop table, placed deterministically as LOCAL offsets
/// under the marker so retuned camp positions carry their buildings along.
///
/// Faction tallies are recorded per camp from day one; the identity/effects
/// layer (a later guide) reads them -- nothing else does yet.
///
/// PERSISTENCE: the ledger saves additively (campGrowth on DungeonSaveData,
/// keyed by the immutable zone ids -- the exact purpose they were reserved
/// for). Buildout is never saved; it rebuilds from ledger + tier tables.
/// Growth accrues to zone ids even before their band is researched: the
/// guild was already gathering, so a late-researched camp can reveal
/// mid-tier (silently -- barks fire only on live tier-ups).
///
/// SCENE SETUP (floor 0 only):
///   Put this beside SurfaceZoneGenerator under the FloorRoot. Nothing to
///   wire; it reads the generator's profile and finds camp markers itself.
/// </summary>
public class CampGrowthController : MonoBehaviour
{
    public static CampGrowthController Instance { get; private set; }

    [Tooltip("Wisp lines on tier-up, indexed by the tier reached (last entry repeats for later tiers).")]
    [SerializeField]
    private List<string> tierUpBarks = new List<string>
    {
        "A cart has stopped at the wood's edge. Word of us travels.",
        "Tents now, and a market stall. They mean to stay.",
        "A settlement takes root out there. We are becoming somewhere.",
    };
    [SerializeField] private float rescanSeconds = 3f;

    private FloorRoot floor;
    private SurfaceZoneGenerator surface;
    private SurfaceZoneProfile profile;
    private bool armed;
    private float nextRescan;
    private float barkSuppressedUntil;
    private Vector3 centreWorld;

    private readonly Dictionary<string, int> growth = new Dictionary<string, int>();
    private readonly Dictionary<string, int[]> factionTally = new Dictionary<string, int[]>();
    private readonly Dictionary<string, int> builtTier = new Dictionary<string, int>();

    private static int FactionCount => Enum.GetValues(typeof(FactionId)).Length;

    // -- lifecycle -----------------------------------------------------------

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null || floor.FloorIndex != 0) { enabled = false; return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable() { AdventurerParty.MemberEscaped += HandleEscape; }
    private void OnDisable() { AdventurerParty.MemberEscaped -= HandleEscape; }

    private void Update()
    {
        if (!armed) { TryArm(); return; }

        if (Time.time >= nextRescan)
        {
            nextRescan = Time.time + rescanSeconds;
            SyncBuildouts();
        }
    }

    private void TryArm()
    {
        if (floor.Terrain == null || floor.TileInfluence == null) return;
        surface = floor.GetComponentInChildren<SurfaceZoneGenerator>(true);
        if (surface == null || surface.Profile == null) return;

        profile = surface.Profile;
        centreWorld = floor.TileInfluence.CellToWorld(floor.Terrain.CoreCell);
        barkSuppressedUntil = Time.time + 2f;   // reveal existing tiers silently
        armed = true;
        SyncBuildouts();
    }

    // -- growth --------------------------------------------------------------

    private void HandleEscape(AdventurerParty party, PartyMember member)
    {
        string zone = ReceivingZone();
        if (zone == null) return;   // every camp at cap: the world is full

        int before = TierOf(zone);
        growth[zone] = GrowthOf(zone) + 1;
        TallyFaction(zone, party, member);
        Debug.Log($"[CampGrowth] A survivor settles at {zone} ({growth[zone]}).");

        int after = TierOf(zone);
        if (after > before)
        {
            SyncBuildouts();
            Bark(after);
        }
    }

    private string ReceivingZone()
    {
        if (GrowthOf("camp.main") < MainCap()) return "camp.main";
        for (int i = 1; i <= MaxSatellites(); i++)
        {
            string id = $"camp.sat.{i}";
            if (GrowthOf(id) < SatelliteCap()) return id;
        }
        return null;
    }

    private void TallyFaction(string zone, AdventurerParty party, PartyMember member)
    {
        if (party == null || member == null) return;
        if (!factionTally.TryGetValue(zone, out var tally))
        {
            tally = new int[FactionCount];
            factionTally[zone] = tally;
        }
        int f = (int)FactionSystem.FactionForKill(member.type, party.Formation);
        if (f >= 0 && f < tally.Length) tally[f]++;
    }

    public int GrowthOf(string zoneId)
        => growth.TryGetValue(zoneId, out int g) ? g : 0;

    public int TierOf(string zoneId)
    {
        if (profile == null || profile.campTiers.Count == 0) return 0;
        int g = GrowthOf(zoneId), tier = 0;
        for (int i = 0; i < profile.campTiers.Count; i++)
            if (g >= profile.campTiers[i].growthThreshold) tier = i;
        return tier;
    }

    public float MillerMultiplier(string zoneId)
    {
        if (profile == null || profile.campTiers.Count == 0) return 1f;
        return profile.campTiers[TierOf(zoneId)].millerMultiplier;
    }

    private int MainCap() => profile != null ? profile.mainGrowthCap : int.MaxValue;
    private int SatelliteCap() => profile != null ? profile.satelliteGrowthCap : int.MaxValue;

    private int MaxSatellites()
    {
        if (profile == null) return 0;
        int n = 0;
        foreach (var b in profile.bands) n += b.satelliteCampCount;
        return n;
    }

    // -- buildout ------------------------------------------------------------

    private void SyncBuildouts()
    {
        if (!armed) return;
        var markers = floor.GetComponentsInChildren<CampZoneMarker>(true);
        foreach (var m in markers)
        {
            if (m == null || string.IsNullOrEmpty(m.ZoneId)) continue;
            int tier = TierOf(m.ZoneId);
            if (builtTier.TryGetValue(m.ZoneId, out int built) && built == tier) continue;
            RebuildBuildout(m, tier);
            builtTier[m.ZoneId] = tier;
        }
    }

    private void RebuildBuildout(CampZoneMarker marker, int tier)
    {
        var old = marker.transform.Find("Buildout");
        if (old != null) Destroy(old.gameObject);

        var parent = new GameObject("Buildout").transform;
        parent.SetParent(marker.transform, false);

        if (profile.campTiers.Count == 0) return;
        var def = profile.campTiers[Mathf.Clamp(tier, 0, profile.campTiers.Count - 1)];
        var rng = new System.Random(StableHash(marker.ZoneId));
        float radius = Mathf.Max(1f, marker.Radius);

        // The commerce anchor faces the way home -- roads and trails all run
        // dungeon-ward, so "toward the centre" is toward the path in.
        Vector3 commerceLocal = Vector3.zero;
        if (def.commercePrefab != null)
        {
            Vector3 dirHome = (centreWorld - marker.transform.position).normalized;
            commerceLocal = dirHome * (radius * 0.75f);
            var c = Instantiate(def.commercePrefab, parent);
            c.name = "Commerce";
            c.transform.localPosition = commerceLocal;
        }

        foreach (var entry in def.props)
        {
            if (entry == null || entry.prefab == null) continue;
            for (int i = 0; i < entry.count; i++)
            {
                Vector3 local;
                int guard = 12;
                do
                {
                    float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                    float r = radius * (0.3f + 0.55f * (float)rng.NextDouble());
                    local = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                } while ((local - commerceLocal).sqrMagnitude < 1.44f && guard-- > 0);

                var p = Instantiate(entry.prefab, parent);
                p.name = entry.prefab.name;
                p.transform.localPosition = local;
            }
        }
    }

    private void Bark(int tier)
    {
        if (Time.time < barkSuppressedUntil || tierUpBarks.Count == 0) return;
        string line = tierUpBarks[Mathf.Clamp(tier, 0, tierUpBarks.Count - 1)];
        WispCompanion.Instance?.SpeakLine(line);
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }

    // -- persistence ---------------------------------------------------------

    public List<CampGrowthSaveData> GetSaveData()
    {
        var list = new List<CampGrowthSaveData>();
        foreach (var kv in growth)
        {
            var rec = new CampGrowthSaveData { zoneId = kv.Key, growth = kv.Value };
            if (factionTally.TryGetValue(kv.Key, out var tally))
                rec.factionTallies = (int[])tally.Clone();
            list.Add(rec);
        }
        return list;
    }

    public void RestoreFromSave(List<CampGrowthSaveData> data)
    {
        growth.Clear();
        factionTally.Clear();
        builtTier.Clear();   // force a silent rebuild at the restored tiers
        barkSuppressedUntil = Time.time + 2f;
        if (data == null) return;
        foreach (var rec in data)
        {
            if (rec == null || string.IsNullOrEmpty(rec.zoneId)) continue;
            growth[rec.zoneId] = rec.growth;
            if (rec.factionTallies != null && rec.factionTallies.Length > 0)
            {
                var tally = new int[FactionCount];
                int n = Mathf.Min(tally.Length, rec.factionTallies.Length);
                for (int i = 0; i < n; i++) tally[i] = rec.factionTallies[i];
                factionTally[rec.zoneId] = tally;
            }
        }
    }
}

/// <summary>Additive save record for one camp's growth ledger.</summary>
[Serializable]
public class CampGrowthSaveData
{
    public string zoneId;
    public int growth;
    public int[] factionTallies;
}