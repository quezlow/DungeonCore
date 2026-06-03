using UnityEngine;

/// <summary>
/// Component on the root GameObject of each floor's hierarchy.
/// Acts as the per-floor service locator — holds references to all
/// managers that used to be scene-level singletons.
///
/// FLOOR 1 SETUP (existing scene objects)
///   - Create an empty GameObject "Floor1Root" at scene root, Y = 0.
///   - Add this component, set floorIndex = 0.
///   - Assign TileInfluenceManager, TrapRegistry, DungeonTerrain from
///     their existing scene GameObjects (they can stay wherever they are
///     in the hierarchy as long as references are wired here).
///   - Assign the PolygonCollider2D used as the Cinemachine confiner bounds.
///
/// FLOOR TEMPLATE PREFAB (Floor 2+)
///   - Self-contained prefab: Grid → Tilemaps + DungeonTerrain,
///     TileInfluenceManager, TrapRegistry all as children.
///   - Wire all references internally in the prefab.
///   - FloorManager sets floorIndex and world position at runtime.
///   - Each floor is offset by floorIndex * -2000 on Y so floors never overlap.
/// </summary>
public class FloorRoot : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("0 = Floor 1. Set automatically by FloorManager for Floor 2+.")]
    [SerializeField] private int floorIndex = 0;

    [Header("Per-Floor Managers")]
    [SerializeField] private TileInfluenceManager tileInfluence;
    [SerializeField] private TrapRegistry trapRegistry;
    [SerializeField] private DungeonTerrain terrain;

    [Header("Camera Bounds")]
    [Tooltip("PolygonCollider2D used as the Cinemachine confiner for this floor.")]
    [SerializeField] private PolygonCollider2D cameraBounds;

    // ── Properties ────────────────────────────────────────────────

    public int FloorIndex => floorIndex;
    public TileInfluenceManager TileInfluence => tileInfluence;
    public TrapRegistry TrapRegistry => trapRegistry;
    public DungeonTerrain Terrain => terrain;
    public PolygonCollider2D CameraBounds => cameraBounds;

    /// <summary>World-space Y origin of this floor (floorIndex * -2000).</summary>
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

    /// <summary>
    /// Seeds the floor's terrain and influence from a stair cell on the
    /// floor above. The cell coordinates are in the floor above's space;
    /// since all floors share the same local coordinate system (just offset
    /// in world Y), the same cell coords are valid here.
    /// Called by FloorManager after Initialise().
    /// </summary>
    public void Bootstrap(Vector3Int centerCell)
    {
        if (terrain != null)
            terrain.GenerateAt(centerCell);

        if (tileInfluence != null)
        {
            tileInfluence.InjectTerrain(terrain); // ensure terrain is set before claiming
            tileInfluence.ClaimStarterArea(centerCell);
        }
    }
}
