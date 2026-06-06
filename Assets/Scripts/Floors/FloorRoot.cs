using UnityEngine;

/// <summary>
/// Component on the root GameObject of each floor's hierarchy.
/// Acts as the per-floor service locator — holds references to all
/// managers that used to be scene-level singletons.
///
/// FLOOR 1 SETUP (existing scene objects)
///   - Create an empty GameObject "Floor1Root" at scene root, Y = 0.
///   - Add this component, set floorIndex = 0.
///   - Assign TileInfluenceManager, TrapRegistry, DungeonTerrain.
///   - Assign the PolygonCollider2D used as the Cinemachine confiner bounds.
///   - DAY 30: Assign TerrainFeatureGenerator.
///   - DAY 31 PART 1: Assign FeatureRevealController.
///   - DAY 31 PART 2: Assign WildMonsterController.
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

        if (tileInfluence != null)
        {
            tileInfluence.InjectTerrain(terrain);
            tileInfluence.ClaimStarterArea(centerCell);
        }
    }
}