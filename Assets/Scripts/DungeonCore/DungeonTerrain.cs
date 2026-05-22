using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(-10)] // runs after DungeonCore (-20) but before TileInfluenceManager (0)
public class DungeonTerrain : MonoBehaviour
{
    public static DungeonTerrain Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap fogTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase floorTile;
    [SerializeField] private TileBase fogTile;

    [Header("Boundary Settings")]
    [SerializeField] private int initialRadius = 10;
    [SerializeField] private int tilesPerLevel = 5;

    private int currentRadius;
    private Vector3Int coreCell;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (DungeonCore.Instance == null)
        {
            Debug.LogError("DungeonTerrain: DungeonCore.Instance is null.");
            return;
        }

        coreCell = floorTilemap.WorldToCell(DungeonCore.Instance.transform.position);
        currentRadius = initialRadius;

        GenerateTerrain(coreCell, currentRadius);

        DungeonCore.Instance.OnLevelUp += HandleLevelUp;
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
    }

    // ── Terrain Generation ────────────────────────────────────────

    private void GenerateTerrain(Vector3Int centre, int radius)
    {
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                Vector3Int pos = centre + new Vector3Int(x, y, 0);
                if (!IsWithinRadius(pos, radius)) continue;

                floorTilemap.SetTile(pos, floorTile);
                fogTilemap.SetTile(pos, fogTile);
            }
    }

    private void HandleLevelUp(int newLevel)
    {
        int newRadius = initialRadius + (newLevel - 1) * tilesPerLevel;
        ExpandTo(newRadius);
    }

    private void ExpandTo(int newRadius)
    {
        for (int x = -newRadius; x <= newRadius; x++)
            for (int y = -newRadius; y <= newRadius; y++)
            {
                Vector3Int pos = coreCell + new Vector3Int(x, y, 0);

                // Only fill tiles that are in the new ring (outside old radius, inside new)
                if (!IsWithinRadius(pos, newRadius)) continue;
                if (IsWithinRadius(pos, currentRadius)) continue;

                floorTilemap.SetTile(pos, floorTile);
                fogTilemap.SetTile(pos, fogTile);
            }

        currentRadius = newRadius;

        // Let TileInfluenceManager know bounds changed so claimable ring can update
        TileInfluenceManager.Instance?.OnBoundsExpanded();
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Removes fog at a position, revealing the floor beneath.</summary>
    public void RevealTile(Vector3Int pos)
    {
        fogTilemap.SetTile(pos, null);
    }

    public bool IsWithinBounds(Vector3Int pos) => IsWithinRadius(pos, currentRadius);

    public Vector3Int CoreCell => coreCell;

    // ── Helpers ───────────────────────────────────────────────────

    private bool IsWithinRadius(Vector3Int pos, int radius)
    {
        int dx = pos.x - coreCell.x;
        int dy = pos.y - coreCell.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }
}