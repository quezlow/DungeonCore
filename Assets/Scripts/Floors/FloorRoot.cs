using UnityEngine;

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
    [SerializeField] private TerrainFeatureGenerator featureGenerator;
    [SerializeField] private FeatureRevealController featureRevealController;
    [SerializeField] private WildMonsterController wildMonsterController;
    [SerializeField] private TerrainTypeMap terrainTypeMap;   // DAY 32

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
    public TerrainTypeMap TerrainTypeMap => terrainTypeMap;   // DAY 32
    public PolygonCollider2D CameraBounds => cameraBounds;

    public float WorldOriginY => floorIndex * -2000f;

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

    public void Bootstrap(Vector3Int centerCell, int floorSeed)
    {
        if (terrain != null)
            terrain.GenerateAt(centerCell);

        if (featureGenerator != null && terrain != null)
            featureGenerator.GenerateNew(floorSeed, centerCell, terrain.CurrentRadius);

        // DAY 32 — terrain type map after feature gen so radial+patches can
        //          be queried by anything else that needs them.
        if (terrainTypeMap != null && terrain != null)
            terrainTypeMap.GenerateNew(floorSeed, centerCell, terrain.CurrentRadius);

        if (tileInfluence != null)
        {
            tileInfluence.InjectTerrain(terrain);
            tileInfluence.ClaimStarterArea(centerCell);
        }
    }

    // ── DAY 32 — Centralised claim cost & tint helpers ────────────

    /// <summary>
    /// Effective claim cost multiplier for a cell.
    /// River cells use TerrainResistanceTable.riverClaimResistance.
    /// Cleared chamber cells use chamberClaimResistance (1× by default).
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
        }
        return terrainTypeMap != null ? terrainTypeMap.GetResistance(cell) : 1f;
    }

    /// <summary>
    /// Claimable-ring tint for a cell.
    /// River and (cleared) chamber cells use feature-specific tints;
    /// otherwise terrain band tint.
    /// </summary>
    public Color GetClaimableRingTint(Vector3Int cell)
    {
        if (featureGenerator != null && terrainTypeMap != null && terrainTypeMap.ResistanceTable != null)
        {
            if (featureGenerator.IsRiver(cell))
                return terrainTypeMap.ResistanceTable.riverClaimableTint;
            if (featureGenerator.IsChamber(cell))
                return terrainTypeMap.ResistanceTable.chamberClaimableTint;
        }
        return terrainTypeMap != null ? terrainTypeMap.GetTint(cell) : Color.white;
    }
}