using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The surface-life layer: cosmetic sprite puppets that make the forest feel
/// inhabited without touching the simulation. Two behaviours, one component:
///
///   ROAD APPROACH -- inside the wave lead window (SecondsUntilNextParty),
///   2-3 walkers appear up the pilgrim road and walk to the mouth, then
///   vanish the moment the real party registers (the spawner's
///   PartyRegistered choke-point event). Commoner waves included; dispatch
///   and climax parties bypass the timer and get no lead-in. Night never
///   plays approaches -- SpawningActive is already false.
///
///   CAMP MILLERS -- each CampZoneMarker keeps a wandering population: by
///   day 5-7 at camp.main and 3-5 per satellite, by night a small watch
///   (night-watch sprites, falling back to the day pool). Markers are
///   re-scanned every few seconds, so live band unlocks grow the population
///   with no coupling to the generator.
///
/// Everything is a plain SpriteRenderer moved on scaled time (the First
/// Blood idiom): pause freezes it, speed-up hastens it, nothing is saved,
/// and no puppet ever becomes an entity. Real-party muster at the mouth
/// lives in DungeonAdventurer, not here.
///
/// SCENE SETUP (floor 0 only):
///   Put this beside SurfaceZoneGenerator under the FloorRoot and assign a
///   few day sprites (and optionally night-watch sprites). Placeholders are
///   fine until the art pass.
/// </summary>
public class SurfaceLifeController : MonoBehaviour
{
    [Header("Puppet sprites (placeholders fine until the art pass)")]
    [SerializeField] private List<Sprite> daySprites = new List<Sprite>();
    [Tooltip("Night-watch figures. Empty falls back to the day list.")]
    [SerializeField] private List<Sprite> nightWatchSprites = new List<Sprite>();
    [Tooltip("Sorting layer the puppets render on - matches live entities.")]
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;

    [Header("Camp millers")]
    [Min(0)][SerializeField] private int dayMainMin = 5;
    [Min(0)][SerializeField] private int dayMainMax = 7;
    [Min(0)][SerializeField] private int daySatelliteMin = 3;
    [Min(0)][SerializeField] private int daySatelliteMax = 5;
    [Min(0)][SerializeField] private int nightMainCount = 2;
    [Min(0)][SerializeField] private int nightSatelliteCount = 1;
    [SerializeField] private float wanderSpeed = 1.1f;
    [SerializeField] private float campRescanSeconds = 3f;

    [Header("Road approach")]
    [Tooltip("Seconds before a predicted wave that walkers appear up the road.")]
    [SerializeField] private float approachLeadSeconds = 8f;
    [Min(0)][SerializeField] private int approachMin = 2;
    [Min(0)][SerializeField] private int approachMax = 3;
    [SerializeField] private float approachWalkSpeed = 2.6f;
    [Tooltip("Seconds walkers linger at the mouth before giving up if no party arrives.")]
    [SerializeField] private float mouthLingerTimeout = 12f;

    // -- floor + anchor state ------------------------------------------------
    private FloorRoot floor;
    private SurfaceZoneGenerator surface;
    private Transform lifeParent;
    private bool armed;
    private Vector3 mouthWorld;
    private Vector2 outward;
    private Vector3Int center;
    private int rim;
    private bool lastNight;
    private float nextRescan;
    private bool warnedNoSprites;

    private class Puppet
    {
        public SpriteRenderer sr;
        public Vector3 target;
        public Vector3 wanderCenter;
        public float wanderRadius;
        public float speed;
        public float nextPick;
        public bool isWalker;
        public float arrivedAt = -1f;
    }

    private readonly Dictionary<CampZoneMarker, List<Puppet>> campPuppets =
        new Dictionary<CampZoneMarker, List<Puppet>>();
    private readonly List<Puppet> walkers = new List<Puppet>();

    // -- lifecycle -----------------------------------------------------------

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null || floor.FloorIndex != 0) enabled = false;
    }

    private void OnEnable() { AdventurerSpawner.PartyRegistered += HandlePartyRegistered; }
    private void OnDisable() { AdventurerSpawner.PartyRegistered -= HandlePartyRegistered; }

    private void Update()
    {
        if (!armed) { TryArm(); return; }

        // Day-night flip: repopulate camps, clear any mid-walk approach.
        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        if (night != lastNight)
        {
            lastNight = night;
            ClearCampPuppets();
            ClearWalkers();
            RescanCamps(night);
            nextRescan = Time.time + campRescanSeconds;
        }

        if (Time.time >= nextRescan)
        {
            nextRescan = Time.time + campRescanSeconds;
            RescanCamps(night);
        }

        TryStartApproach();
        TickPuppets(Time.deltaTime);
    }

    private void TryArm()
    {
        var features = floor.FeatureGenerator;
        if (features == null || !features.HasGenerated) return;
        var cave = features.EntranceCave;
        if (cave == null) { enabled = false; return; }   // legacy save: no surface life
        if (floor.Terrain == null || floor.TileInfluence == null) return;

        center = floor.Terrain.CoreCell;
        rim = floor.Terrain.CurrentRadius;
        mouthWorld = floor.TileInfluence.CellToWorld(cave.mouthCell.ToVector3Int());
        float rad = cave.angleDegrees * Mathf.Deg2Rad;
        outward = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        surface = floor.GetComponentInChildren<SurfaceZoneGenerator>(true);

        var go = new GameObject("SurfaceLife");
        go.transform.SetParent(transform, false);
        lifeParent = go.transform;

        lastNight = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        armed = true;
    }

    // -- camp millers --------------------------------------------------------

    private void RescanCamps(bool night)
    {
        // Prune camps whose markers are gone (surface regenerated).
        var dead = new List<CampZoneMarker>();
        foreach (var kv in campPuppets)
            if (kv.Key == null) dead.Add(kv.Key);
        foreach (var k in dead)
        {
            foreach (var p in campPuppets[k])
                if (p.sr != null) Destroy(p.sr.gameObject);
            campPuppets.Remove(k);
        }

        var markers = floor.GetComponentsInChildren<CampZoneMarker>(true);
        foreach (var m in markers)
        {
            if (m == null || campPuppets.ContainsKey(m)) continue;
            campPuppets[m] = SpawnCampPopulation(m, night);
        }
    }

    private List<Puppet> SpawnCampPopulation(CampZoneMarker marker, bool night)
    {
        bool main = marker.ZoneId == "camp.main";
        int count = night
            ? (main ? nightMainCount : nightSatelliteCount)
            : (main ? Random.Range(dayMainMin, dayMainMax + 1)
                    : Random.Range(daySatelliteMin, daySatelliteMax + 1));

        // Camp tier scales the crowd: waystations are sparse, settlements busy.
        if (CampGrowthController.Instance != null)
            count = Mathf.Max(1, Mathf.RoundToInt(
                count * CampGrowthController.Instance.MillerMultiplier(marker.ZoneId)));

        var list = new List<Puppet>();
        for (int i = 0; i < count; i++)
        {
            var sprite = PickSprite(night);
            if (sprite == null) break;
            Vector2 off = Random.insideUnitCircle * (marker.Radius * 0.6f);
            var p = MakePuppet($"Miller_{marker.ZoneId}_{i}", sprite,
                               marker.transform.position + new Vector3(off.x, off.y, 0f));
            p.wanderCenter = marker.transform.position;
            p.wanderRadius = Mathf.Max(1f, marker.Radius * 0.85f);
            p.speed = wanderSpeed * Random.Range(0.85f, 1.15f);
            p.target = p.sr.transform.position;
            p.nextPick = Time.time + Random.Range(0f, 2f);
            list.Add(p);
        }
        return list;
    }

    private void ClearCampPuppets()
    {
        foreach (var kv in campPuppets)
            foreach (var p in kv.Value)
                if (p.sr != null) Destroy(p.sr.gameObject);
        campPuppets.Clear();
    }

    // -- road approach -------------------------------------------------------

    private void TryStartApproach()
    {
        if (walkers.Count > 0) return;
        var sp = AdventurerSpawner.Instance;
        if (sp == null || !sp.SpawningActive || sp.PartyCapReached) return;

        float s = sp.SecondsUntilNextParty;
        if (s <= 0.5f || s > approachLeadSeconds) return;

        float revealed = surface != null ? surface.RevealedDepthCells : 32f;
        if (revealed < 8f) return;
        float startDepth = Mathf.Clamp(approachWalkSpeed * s + 2f, 6f, revealed - 2f);

        int n = Random.Range(approachMin, approachMax + 1);
        for (int i = 0; i < n; i++)
        {
            var sprite = PickSprite(false);
            if (sprite == null) return;

            float lateral = Random.Range(-1.2f, 1.2f);
            float depth = startDepth + i * 1.1f + Random.Range(0f, 0.6f);
            Vector3 perp = new Vector3(-outward.y, outward.x, 0f);
            Vector3 start = RoadPoint(depth) + perp * lateral;

            var p = MakePuppet($"Approach_{i}", sprite, start);
            p.isWalker = true;
            p.speed = approachWalkSpeed * Random.Range(0.9f, 1.1f);
            p.target = mouthWorld + (Vector3)(outward * 1.2f) + perp * (lateral * 0.4f);
            walkers.Add(p);
        }
    }

    private void HandlePartyRegistered()
    {
        // The real party has appeared at the entrance below: the walkers
        // "went in". No fade needed - the cut reads as the handoff.
        ClearWalkers();
    }

    private void ClearWalkers()
    {
        foreach (var p in walkers)
            if (p.sr != null) Destroy(p.sr.gameObject);
        walkers.Clear();
    }

    // -- puppet ticking ------------------------------------------------------

    private void TickPuppets(float dt)
    {
        foreach (var kv in campPuppets)
            foreach (var p in kv.Value)
                TickWanderer(p, dt);

        for (int i = walkers.Count - 1; i >= 0; i--)
        {
            var p = walkers[i];
            if (p.sr == null) { walkers.RemoveAt(i); continue; }

            Move(p, dt);
            if (p.arrivedAt < 0f && AtTarget(p))
            {
                // Reached the mouth: mill in place until the party appears
                // below, or give up after the linger timeout.
                p.arrivedAt = Time.time;
                p.wanderCenter = p.target;
                p.wanderRadius = 0.8f;
                p.nextPick = 0f;
            }
            if (p.arrivedAt >= 0f)
            {
                TickWanderer(p, dt);
                if (Time.time - p.arrivedAt > mouthLingerTimeout)
                {
                    Destroy(p.sr.gameObject);
                    walkers.RemoveAt(i);
                }
            }
        }
    }

    private void TickWanderer(Puppet p, float dt)
    {
        if (p.sr == null) return;
        if (AtTarget(p) && Time.time >= p.nextPick)
        {
            Vector2 off = Random.insideUnitCircle * p.wanderRadius;
            p.target = p.wanderCenter + new Vector3(off.x, off.y, 0f);
            p.nextPick = Time.time + Random.Range(1.5f, 3.5f);
        }
        Move(p, dt);
    }

    private void Move(Puppet p, float dt)
    {
        Vector3 pos = p.sr.transform.position;
        if ((p.target - pos).sqrMagnitude < 0.0004f) return;
        p.sr.flipX = p.target.x < pos.x;
        p.sr.transform.position = Vector3.MoveTowards(pos, p.target, p.speed * dt);
    }

    private static bool AtTarget(Puppet p)
        => (p.target - p.sr.transform.position).sqrMagnitude < 0.01f;

    // -- helpers -------------------------------------------------------------

    private Puppet MakePuppet(string name, Sprite sprite, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(lifeParent, false);
        go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        return new Puppet { sr = sr };
    }

    private Sprite PickSprite(bool night)
    {
        var pool = night && nightWatchSprites.Count > 0 ? nightWatchSprites : daySprites;
        if (pool.Count == 0)
        {
            if (!warnedNoSprites)
            {
                warnedNoSprites = true;
                Debug.LogWarning("[SurfaceLifeController] No puppet sprites assigned - surface life stays empty.");
            }
            return null;
        }
        return pool[Random.Range(0, pool.Count)];
    }

    private Vector3 RoadPoint(float depth)
    {
        float r = rim + depth;
        var cell = new Vector3Int(center.x + Mathf.RoundToInt(outward.x * r),
                                  center.y + Mathf.RoundToInt(outward.y * r), 0);
        return floor.TileInfluence.CellToWorld(cell);
    }
}