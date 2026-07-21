using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Camp growth, identity, and pressure: survivors settle the surface, camps
/// declare a faction, and settled camps push back on the dungeon sim.
///
/// GROWTH -- every adventurer who leaves the dungeon alive
/// (AdventurerParty.MemberEscaped) adds one growth to a receiving camp:
/// camp.main until its cap, then satellites in unlock order. Growth crosses
/// the profile's authored tier thresholds (Waystation -> Camp -> Settlement
/// -> whatever rows come later, e.g. a Town); tiers rebuild the buildout.
///
/// IDENTITY -- at tier 1+ a camp declares the majority faction of its
/// recorded settlers (ties break to the Guild; waystations stay neutral).
/// Sticky: re-evaluated only on tier-up; the banner comes down if decay
/// drops the camp back to tier 0.
///
/// EFFECTS -- queried by the sim, tier-scaled, summed across camps:
///   Guild camps shave seconds off the wave interval (spawner floor-guarded);
///   Cultist camps dampen notoriety decay (DungeonCore multiplies);
///   Holy Order camps tax mana regen (CurrentManaRegen multiplies, capped);
///   Mercenary camps declare but exert no pressure yet.
///
/// DECAY -- on each dawn, a camp with no settlers for the grace period
/// bleeds growth; tiers drop, buildout and framing recede, floor at zero.
///
/// FRAMING -- when growth reaches framingFraction of the NEXT tier's
/// threshold, that tier's construction-site look appears: framingProps[i]
/// renders at the exact positions props[i] will take (per-prop position
/// hashing makes foundation and finished building land identically); the
/// commerce framing rises beside the current anchor, and the finished
/// piece takes the anchor spot on tier-up.
///
/// PERSISTENCE: one additive block (campGrowth on DungeonSaveData): growth,
/// per-faction tallies, declared faction, and last-settle day per zone id.
/// Buildout is never saved; it rebuilds from ledger + tier tables, silently
/// after a load (barks fire only on live changes).
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
    [Tooltip("Spoken when a camp declares its faction. {0} = faction display name.")]
    [SerializeField]
    private string identityBarkFormat = "The camp at the wood's edge raises colours -- {0}.";
    [SerializeField] private float rescanSeconds = 3f;

    private FloorRoot floor;
    private SurfaceZoneGenerator surface;
    private SurfaceZoneProfile profile;
    private bool armed;
    private bool daySubscribed;
    private float nextRescan;
    private float barkSuppressedUntil;
    private Vector3 centreWorld;

    private readonly Dictionary<string, int> growth = new Dictionary<string, int>();
    private readonly Dictionary<string, int[]> factionTally = new Dictionary<string, int[]>();
    private readonly Dictionary<string, int> declaredFaction = new Dictionary<string, int>();
    private readonly Dictionary<string, int> lastSettleDay = new Dictionary<string, int>();
    private readonly Dictionary<string, int> builtState = new Dictionary<string, int>();

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

    private void OnDisable()
    {
        AdventurerParty.MemberEscaped -= HandleEscape;
        if (daySubscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        daySubscribed = false;
    }

    private void Update()
    {
        if (!armed) { TryArm(); return; }

        if (!daySubscribed && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
            daySubscribed = true;
        }

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
        barkSuppressedUntil = Time.time + 2f;   // reveal existing state silently
        armed = true;
        SyncBuildouts();
    }

    private static int Today()
        => DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;

    // -- growth --------------------------------------------------------------

    private void HandleEscape(AdventurerParty party, PartyMember member)
    {
        string zone = ReceivingZone();
        if (zone == null) return;   // every camp at cap: the world is full

        int before = TierOf(zone);
        growth[zone] = GrowthOf(zone) + 1;
        lastSettleDay[zone] = Today();
        TallyFaction(zone, party, member);
        Debug.Log($"[CampGrowth] A survivor settles at {zone} ({growth[zone]}).");

        int after = TierOf(zone);
        if (after > before)
        {
            Bark(tierUpBarks.Count == 0 ? null
                : tierUpBarks[Mathf.Clamp(after, 0, tierUpBarks.Count - 1)]);
            EvaluateIdentity(zone, announce: true);
            DungeonCore.Instance?.NotifyManaRegenDisplay();
        }
        SyncBuildouts();
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

    // -- identity ------------------------------------------------------------

    /// <summary>Declared faction as a FactionId int, or -1 while neutral.</summary>
    public int DeclaredFactionOf(string zoneId)
        => declaredFaction.TryGetValue(zoneId, out int f) ? f : -1;

    private void EvaluateIdentity(string zone, bool announce)
    {
        if (TierOf(zone) < 1) return;
        int f = MajorityFaction(zone);
        if (f < 0) return;

        bool had = declaredFaction.TryGetValue(zone, out int prev);
        if (had && prev == f) return;
        declaredFaction[zone] = f;

        if (announce)
            Bark(string.Format(identityBarkFormat,
                FactionInfo.DisplayName((FactionId)f)));
    }

    private int MajorityFaction(string zone)
    {
        if (!factionTally.TryGetValue(zone, out var tally)) return -1;
        int best = -1, bestCount = 0;
        foreach (var f in FactionInfo.All)
        {
            int c = tally[(int)f];
            if (c > bestCount) { bestCount = c; best = (int)f; }
        }
        if (bestCount <= 0) return -1;
        // Ties break to the Guild when it shares the lead.
        if (tally[(int)FactionId.AdventurersGuild] == bestCount)
            return (int)FactionId.AdventurersGuild;
        return best;
    }

    // -- effects (queried by the sim; tier-scaled, summed across camps) ------

    public float GuildIntervalFloorFraction
        => profile != null ? profile.guildIntervalFloorFraction : 0.6f;

    public float GuildIntervalReductionSeconds
    {
        get
        {
            if (profile == null) return 0f;
            float s = 0f;
            foreach (var kv in declaredFaction)
                if (kv.Value == (int)FactionId.AdventurersGuild)
                    s += profile.guildIntervalSecondsPerTier * TierOf(kv.Key);
            return s;
        }
    }

    public float CultistNotorietyDecayMultiplier
    {
        get
        {
            if (profile == null) return 1f;
            float m = 1f;
            foreach (var kv in declaredFaction)
                if (kv.Value == (int)FactionId.Cultists)
                    m *= Mathf.Max(0f,
                        1f - profile.cultistDecayDampenPerTier * TierOf(kv.Key));
            return Mathf.Max(profile.cultistDecayMultiplierMin, m);
        }
    }

    public float HolyManaRegenMultiplier
    {
        get
        {
            if (profile == null) return 1f;
            float tax = 0f;
            foreach (var kv in declaredFaction)
                if (kv.Value == (int)FactionId.HolyOrder)
                    tax += profile.holyManaTaxPerTier * TierOf(kv.Key);
            return 1f - Mathf.Min(profile.holyManaTaxCap, tax);
        }
    }

    // -- decay ---------------------------------------------------------------

    private void HandleDayStarted()
    {
        if (!armed || profile == null) return;
        int today = Today();
        bool changed = false;

        var zones = new List<string>(growth.Keys);
        foreach (var zone in zones)
        {
            if (!lastSettleDay.TryGetValue(zone, out int last) || last <= 0)
            {
                lastSettleDay[zone] = today;   // unknown (old save): start counting
                continue;
            }
            if (today - last <= profile.decayGraceDays) continue;

            int before = TierOf(zone);
            growth[zone] = Mathf.Max(0, growth[zone] - profile.decayPerDay);
            changed = true;
            if (TierOf(zone) < before && TierOf(zone) == 0)
                declaredFaction.Remove(zone);   // the banner comes down
        }

        if (changed)
        {
            SyncBuildouts();
            DungeonCore.Instance?.NotifyManaRegenDisplay();
        }
    }

    // -- buildout ------------------------------------------------------------

    private bool FramingDue(string zoneId, int tier)
    {
        if (profile == null) return false;
        int next = tier + 1;
        if (next >= profile.campTiers.Count) return false;
        int threshold = profile.campTiers[next].growthThreshold;
        if (threshold <= 0) return false;
        return GrowthOf(zoneId)
            >= Mathf.CeilToInt(threshold * profile.framingFraction);
    }

    private int BuildStateOf(string zoneId)
    {
        int tier = TierOf(zoneId);
        return tier * 2 + (FramingDue(zoneId, tier) ? 1 : 0);
    }

    private void SyncBuildouts()
    {
        if (!armed) return;
        var markers = floor.GetComponentsInChildren<CampZoneMarker>(true);
        foreach (var m in markers)
        {
            if (m == null || string.IsNullOrEmpty(m.ZoneId)) continue;
            int state = BuildStateOf(m.ZoneId);
            if (builtState.TryGetValue(m.ZoneId, out int built) && built == state)
                continue;
            RebuildBuildout(m, state);
            builtState[m.ZoneId] = state;
        }
    }

    private void RebuildBuildout(CampZoneMarker marker, int state)
    {
        var old = marker.transform.Find("Buildout");
        if (old != null) Destroy(old.gameObject);

        var parent = new GameObject("Buildout").transform;
        parent.SetParent(marker.transform, false);

        if (profile.campTiers.Count == 0) return;
        int tier = state / 2;
        bool framing = (state & 1) == 1;
        var def = profile.campTiers[Mathf.Clamp(tier, 0, profile.campTiers.Count - 1)];
        float radius = Mathf.Max(1f, marker.Radius);

        // The commerce anchor faces the way home -- roads and trails all run
        // dungeon-ward, so "toward the centre" is toward the path in.
        Vector3 dirHome = (centreWorld - marker.transform.position).normalized;
        Vector3 commerceLocal = dirHome * (radius * 0.75f);
        if (def.commercePrefab != null)
        {
            var c = Instantiate(def.commercePrefab, parent);
            c.name = "Commerce";
            c.transform.localPosition = commerceLocal;
        }

        PlaceRow(parent, marker.ZoneId, tier, def.props, null, radius, commerceLocal);

        if (framing && tier + 1 < profile.campTiers.Count)
        {
            var next = profile.campTiers[tier + 1];
            // Commerce framing rises beside the current anchor; the finished
            // piece takes the anchor spot on tier-up.
            if (next.framingCommercePrefab != null)
            {
                var perp = new Vector3(-dirHome.y, dirHome.x, 0f);
                var fc = Instantiate(next.framingCommercePrefab, parent);
                fc.name = "CommerceFraming";
                fc.transform.localPosition = commerceLocal + perp * 1.5f;
            }
            // Props framing lands at the exact final positions (shared hash).
            PlaceRow(parent, marker.ZoneId, tier + 1, next.props,
                     next.framingProps, radius, commerceLocal);
        }
    }

    /// <summary>Places one tier's prop rows. With substitutes null, the final
    /// prefabs render; otherwise substitutes[i] renders IN PLACE OF props[i]
    /// (skipping null slots) at the identical hashed positions.</summary>
    private void PlaceRow(Transform parent, string zoneId, int tier,
                          List<CampPropEntry> rows, List<GameObject> substitutes,
                          float radius, Vector3 commerceLocal)
    {
        if (rows == null) return;
        for (int entry = 0; entry < rows.Count; entry++)
        {
            var row = rows[entry];
            if (row == null) continue;
            GameObject prefab = substitutes == null
                ? row.prefab
                : (entry < substitutes.Count ? substitutes[entry] : null);
            if (prefab == null) continue;

            for (int i = 0; i < row.count; i++)
            {
                Vector3 local = PropLocal(zoneId, tier, entry, i,
                                          radius, commerceLocal);
                var p = Instantiate(prefab, parent);
                p.name = prefab.name;
                p.transform.localPosition = local;
            }
        }
    }

    /// <summary>Deterministic per-prop position: identical inputs give the
    /// identical spot, so a tier's framing and its finished props coincide
    /// by construction. Salted retries steer clear of the commerce anchor.</summary>
    private static Vector3 PropLocal(string zoneId, int tier, int entry, int i,
                                     float radius, Vector3 commerceLocal)
    {
        int zs = StableHash(zoneId);
        int key = tier * 8191 + entry * 131 + i;
        Vector3 local = Vector3.zero;
        for (int k = 0; k < 12; k++)
        {
            float ang = Hash01(zs, key, 17 + k) * Mathf.PI * 2f;
            float r = radius * (0.3f + 0.55f * Hash01(zs, key, 911 + k));
            local = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
            if ((local - commerceLocal).sqrMagnitude >= 1.44f) break;
        }
        return local;
    }

    private void Bark(string line)
    {
        if (line == null || Time.time < barkSuppressedUntil) return;
        // No word of the camps until the player can SEE them: the surface band
        // they sit in is revealed by the scout_1 node. Barking earlier narrates
        // a place still under fog.
        if (!UnlockState.IsUnlocked("tech.scout_1")) return;
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

    private static float Hash01(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)a * 2246822519u ^ (uint)b * 3266489917u;
            h ^= (uint)c * 668265263u;
            h = (h << 13) | (h >> 19);
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // -- persistence ---------------------------------------------------------

    public List<CampGrowthSaveData> GetSaveData()
    {
        var list = new List<CampGrowthSaveData>();
        foreach (var kv in growth)
        {
            var rec = new CampGrowthSaveData
            {
                zoneId = kv.Key,
                growth = kv.Value,
                declaredFaction = DeclaredFactionOf(kv.Key),
                lastSettleDay = lastSettleDay.TryGetValue(kv.Key, out int d) ? d : 0,
            };
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
        declaredFaction.Clear();
        lastSettleDay.Clear();
        builtState.Clear();   // force a silent rebuild at the restored state
        barkSuppressedUntil = Time.time + 2f;
        if (data == null) return;
        foreach (var rec in data)
        {
            if (rec == null || string.IsNullOrEmpty(rec.zoneId)) continue;
            growth[rec.zoneId] = rec.growth;
            if (rec.declaredFaction >= 0 && rec.declaredFaction < FactionCount)
                declaredFaction[rec.zoneId] = rec.declaredFaction;
            if (rec.lastSettleDay > 0)
                lastSettleDay[rec.zoneId] = rec.lastSettleDay;
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

/// <summary>Additive save record for one camp's ledger. Field initialisers
/// double as old-save defaults (JsonUtility leaves missing fields at their
/// constructed values): -1 = still neutral, 0 = settle-day unknown.</summary>
[Serializable]
public class CampGrowthSaveData
{
    public string zoneId;
    public int growth;
    public int[] factionTallies;
    public int declaredFaction = -1;
    public int lastSettleDay = 0;
}