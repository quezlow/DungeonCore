using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor terrain manager.
///
/// DAY 27 SECTION 2B CHANGE
///   - HandleLevelUp now guards against expanding any floor that isn't the
///     core's current floor. Only the floor hosting the core gets terrain
///     expansion when the dungeon levels up; older floors stay at their
///     current size.
/// </summary>
[DefaultExecutionOrder(-10)]
public class DungeonTerrain : MonoBehaviour
{
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
    private bool initialised = false;
    private FloorRoot myFloor;

    private void Start()
    {
        myFloor = GetComponentInParent<FloorRoot>();

        if (myFloor != null && myFloor.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null) { Debug.LogError("[DungeonTerrain] DungeonCore.Instance is null (Floor 1)."); return; }
            GenerateAt(floorTilemap.WorldToCell(DungeonCore.Instance.transform.position));
        }

        // Every floor's terrain listens to level-ups, but only expands if it
        // currently hosts the core (see HandleLevelUp).
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
    }

    public void GenerateAt(Vector3Int centre)
    {
        if (initialised) return;
        initialised = true;
        coreCell = centre;
        currentRadius = initialRadius;
        PaintTerrain(coreCell, currentRadius);
    }

    private void HandleLevelUp(int newLevel)
    {
        // Only the floor currently hosting the core expands.
        if (myFloor == null || FloorManager.Instance == null) return;
        if (myFloor.FloorIndex != FloorManager.Instance.CoreFloorIndex) return;

        int newRadius = initialRadius + (newLevel - 1) * tilesPerLevel;
        ExpandTo(newRadius);
    }

    private void ExpandTo(int newRadius)
    {
        for (int x = -newRadius; x <= newRadius; x++)
            for (int y = -newRadius; y <= newRadius; y++)
            {
                Vector3Int pos = coreCell + new Vector3Int(x, y, 0);
                if (!IsWithinRadius(pos, newRadius)) continue;
                if (IsWithinRadius(pos, currentRadius)) continue;
                floorTilemap.SetTile(pos, floorTile);
                fogTilemap.SetTile(pos, fogTile);
            }

        currentRadius = newRadius;
        myFloor?.TileInfluence?.OnBoundsExpanded();
    }

    private void PaintTerrain(Vector3Int centre, int radius)
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

    public void RevealTile(Vector3Int pos) => fogTilemap.SetTile(pos, null);

    public void RefogTile(Vector3Int pos)
    {
        if (IsWithinBounds(pos)) fogTilemap.SetTile(pos, fogTile);
    }

    public bool IsWithinBounds(Vector3Int pos) => IsWithinRadius(pos, currentRadius);
    public Vector3Int CoreCell => coreCell;

    private bool IsWithinRadius(Vector3Int pos, int radius)
    {
        int dx = pos.x - coreCell.x;
        int dy = pos.y - coreCell.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }
}