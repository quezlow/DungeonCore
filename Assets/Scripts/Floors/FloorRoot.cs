using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor container that bundles all the systems a single floor needs.
///
/// FLOOR ROOT GAMEOBJECT (Floor 1)
///   - Assign TileInfluenceManager, TrapRegistry, DungeonTerrain.
///   - Assign the PolygonCollider2D used as the Cinemachine confiner bounds.
///   - DAY 30: Assign TerrainFeatureGenerator.
///   - DAY 31 PART 1: Assign FeatureRevealController.
///   - DAY 31 PART 2: Assign WildMonsterController.
///   - DAY 32: Assign TerrainTypeMap.
///
/// FLOOR TEMPLATE PREFAB (Floor 2+)
///   - Self-contained prefab with all the above components wired internally.
///   - FloorManager sets floorIndex and world position at runtime.
///   - Each floor is offset by floorIndex * -2000 on Y so floors never overlap.
/// </summary>
public class FloorRoot : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private int floorIndex = 0;

    [Header("Per-Floor Managers")]
    [SerializeField] private TileInfluenceManager tileInfluence;
    [SerializeField] private TrapRegistry trapRegistry;
    [SerializeField] private DungeonTerrain terrain;
    [SerializeField] private Tilemap highlightTilemap;
    [SerializeField] private TerrainFeatureGenerator featureGenerator;
    [SerializeField] private FeatureRevealController featureRevealController;
    [SerializeField] private WildMonsterController wildMonsterController;
    [SerializeField] private TerrainTypeMap terrainTypeMap;
    [SerializeField] private FloorEntityRegistry entities;
    [SerializeField] private InfluenceField influenceField;

    [Header("Camera Bounds")]
    [SerializeField] private PolygonCollider2D cameraBounds;

    // ── Properties ────────────────────────────────────────────────

    public int FloorIndex => floorIndex;
    public TileInfluenceManager TileInfluence => tileInfluence;
    public TrapRegistry TrapRegistry => trapRegistry;
    public DungeonTerrain Terrain => terrain;
    public TerrainFeatureGenerator FeatureGenerator => featureGenerator;
    public FeatureRevealController FeatureRevealController => featureRevealController;
    public WildMonsterController WildMonsterController => wildMonsterController;
    public TerrainTypeMap TerrainTypeMap => terrainTypeMap;
    public FloorEntityRegistry Entities => entities;
    public InfluenceField InfluenceField => influenceField;
    public PolygonCollider2D CameraBounds => cameraBounds;
    public Tilemap HighlightTilemap => highlightTilemap;

    /// <summary>World Y between one floor and the next. Floors are ALL
    /// ALWAYS ACTIVE and simulate together -- see FloorManager's class doc --
    /// so this offset keeps their geometry from overlapping and is NOT a
    /// visibility or activation mechanism. Anything that treats "the active
    /// floor" as "the only floor running" is wrong.</summary>
    public const float FloorSpacingY = 2000f;

    public float WorldOriginY => floorIndex * -FloorSpacingY;

    /// <summary>Which floor a world position sits on. The ONE place that
    /// answers it. PatternDiscovery carried a private copy with the spacing
    /// hard-coded and a comment pointing back here; a second copy was about to
    /// appear for den scavengers, which need to tell their own floor's loot
    /// from the floor below's. A distance test written twice is a distance
    /// test that will disagree with itself -- the same argument LootAbsorbGate
    /// already makes about proximity.</summary>
    public static int FloorIndexFromWorld(Vector3 worldPos)
        => Mathf.Max(0, Mathf.RoundToInt(-worldPos.y / FloorSpacingY));

    /// <summary>True when a world position belongs to the given floor.</summary>
    public static bool IsOnFloor(Vector3 worldPos, int floorIndex)
        => FloorIndexFromWorld(worldPos) == floorIndex;

    /// <summary>True when the fog has lifted from this cell. The ABSENCE of a
    /// fog tile is the test, which is the rule ReachabilityDirector has used
    /// since it shipped -- its influence wash skips unrevealed cells so it does
    /// not give away rivers and chambers the player has not met. Hoisted here so
    /// the drawing code can ask the same question the pathing code already
    /// asked, rather than each keeping its own copy.</summary>
    public bool IsRevealed(Vector3Int cell)
    {
        var fog = terrain != null ? terrain.FogTilemap : null;
        return fog == null || fog.GetTile(cell) == null;
    }

    /// <summary>True when the fog has lifted where a world position stands.
    ///
    /// FAILS OPEN. Every caller is a visibility gate, so anything unresolvable
    /// -- no manager, no floor, no tile grid -- counts as revealed. A hidden
    /// interface is far worse than a briefly over-shown one, and this runs
    /// during scene setup when those references are legitimately still
    /// null.</summary>
    public static bool IsRevealedWorld(Vector3 worldPos)
    {
        if (FloorManager.Instance == null) return true;
        var floor = FloorManager.Instance.GetFloor(FloorIndexFromWorld(worldPos));
        if (floor == null || floor.TileInfluence == null) return true;
        return floor.IsRevealed(floor.TileInfluence.WorldToCell(worldPos));
    }

    // Does this floor carry a LIVING dwarven holding (outpost or village)?
    // Drives the road claim price and, with it, the granite holdings overlay.
    // Cached because sites are authored once at generation and never added
    // later -- but only ONCE the site list exists, since GetOutpostSite
    // answers null for "not generated yet" as well as for "none here".
    private int livingDwarvenSite = -1;   // -1 unknown, 0 no, 1 yes

    public bool HasLivingDwarvenSite
    {
        get
        {
            if (livingDwarvenSite >= 0) return livingDwarvenSite == 1;
            if (featureGenerator == null || !featureGenerator.HasSiteData) return false;
            bool has = featureGenerator.GetOutpostSite() != null
                    || featureGenerator.GetVillageSite() != null;
            livingDwarvenSite = has ? 1 : 0;
            return has;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (tileInfluence != null && terrain != null)
            tileInfluence.InjectTerrain(terrain);
    }

    private void OnDestroy()
    {
        FloorManager.Instance?.UnregisterFloor(this);
    }

    public void Initialise(int index)
    {
        floorIndex = index;
        transform.position = new Vector3(0f, index * -2000f, 0f);
        Debug.Log($"[FloorRoot] Initialise: index={index}, floorIndex now={floorIndex}, name={name}");
        FloorManager.Instance?.RegisterFloor(this);
    }

    /// <summary>Set true to have Bootstrap log a per-stage timing breakdown.
    /// Off by default: this runs on every floor creation in a live game, and the
    /// numbers are only wanted while somebody is chasing a cost.</summary>
    public static bool LogBootstrapTimings;

    public void Bootstrap(Vector3Int centerCell, int floorSeed)
    {
        var sw = LogBootstrapTimings ? System.Diagnostics.Stopwatch.StartNew() : null;
        long tTerrain = 0, tFeatures = 0, tTypeMap = 0, tInfluence = 0;

        if (terrain != null)
            terrain.GenerateAt(centerCell);
        if (sw != null) { tTerrain = sw.ElapsedMilliseconds; sw.Restart(); }

        if (featureGenerator != null && terrain != null)
            featureGenerator.GenerateNew(floorSeed, centerCell, terrain.CurrentRadius);
        if (sw != null) { tFeatures = sw.ElapsedMilliseconds; sw.Restart(); }

        // Terrain type map after feature gen so radial+patches can be queried
        // by anything else that needs them.
        if (terrainTypeMap != null && terrain != null)
            terrainTypeMap.GenerateNew(floorSeed, centerCell, terrain.CurrentRadius);

        // GenerateNew clears the type map's override table, so a site's masonry has
        // to be retyped AFTER it, not during feature generation. The load path does
        // the same thing from TerrainFeatureGenerator.LoadFromSave, where the
        // ordering is reversed.
        featureGenerator?.ApplyRuinsOverrides();
        if (sw != null) { tTypeMap = sw.ElapsedMilliseconds; sw.Restart(); }

        if (tileInfluence != null)
        {
            tileInfluence.InjectTerrain(terrain);
            tileInfluence.ClaimStarterArea(centerCell);
        }
        if (sw != null)
        {
            tInfluence = sw.ElapsedMilliseconds;
            sw.Stop();
            Debug.Log($"[FloorRoot] Bootstrap floor {floorIndex + 1} " +
                      $"(radius {(terrain != null ? terrain.CurrentRadius : -1)}): " +
                      $"terrain {tTerrain} ms, features {tFeatures} ms, " +
                      $"typemap {tTypeMap} ms, influence {tInfluence} ms, " +
                      $"total {tTerrain + tFeatures + tTypeMap + tInfluence} ms.");
        }
    }

    // ── DAY 32 — Centralised claim cost & tint helpers ────────────

    /// <summary>
    /// Effective claim cost multiplier for a cell.
    /// River cells use TerrainResistanceTable.riverClaimResistance.
    /// Cleared chamber cells use chamberClaimResistance (1× by default).
    /// Road cells use roadClaimResistance -- the terrain-resistance rung of the
    /// road-claiming warning ladder, felt before anything is told.
    /// Otherwise terrain type lookup.
    /// </summary>
    public float GetClaimCostMultiplier(Vector3Int cell)
    {
        if (featureGenerator != null)
        {
            if (featureGenerator.IsRiver(cell))
                return terrainTypeMap?.ResistanceTable?.riverClaimResistance ?? 1f;
            if (featureGenerator.IsChamber(cell))
                return terrainTypeMap?.ResistanceTable?.chamberClaimResistance ?? 1f;
            if (featureGenerator.IsRoad(cell))
            {
                var table = terrainTypeMap?.ResistanceTable;
                if (table == null) return 1f;
                // A floor with no living holding carries the DEAD network. The
                // road still resists, but at granite's price, not the living
                // road's: there is nobody left down there to object.
                return HasLivingDwarvenSite ? table.roadClaimResistance
                                            : table.deadRoadClaimResistance;
            }
            if (featureGenerator.IsAncientSite(cell))
                return terrainTypeMap?.ResistanceTable?.siteClaimResistance ?? 1f;
        }
        return terrainTypeMap != null ? terrainTypeMap.GetResistance(cell) : 1f;
    }
}