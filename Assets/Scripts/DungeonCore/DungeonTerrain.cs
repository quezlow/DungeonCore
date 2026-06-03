using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor terrain manager. No longer a singleton.
/// Access via FloorRoot.Terrain.
///
/// CHANGES FROM PRE-DAY-27
///   - Static Instance removed.
///   - GenerateAt(Vector3Int) added so Floor 2+ can seed terrain around
///     the stair cell rather than around DungeonCore's position.
///   - OnLevelUp terrain expansion only fires on Floor 1 for now
///     (deeper floors expand via stair unlock — deferred).
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

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        // Floor 1: generate terrain around DungeonCore position.
        // Floor 2+: GenerateAt() is called by FloorRoot.Bootstrap() instead.
        var floorRoot = GetComponentInParent<FloorRoot>();
        if (floorRoot != null && floorRoot.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null)
            {
                Debug.LogError("[DungeonTerrain] DungeonCore.Instance is null (Floor 1).");
                return;
            }

            GenerateAt(floorTilemap.WorldToCell(DungeonCore.Instance.transform.position));
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
        }
        // Floor 2+: Bootstrap() will call GenerateAt() directly.
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
    }

    // ── Terrain Generation ────────────────────────────────────────

    /// <summary>
    /// Generates terrain centred on the given cell.
    /// Safe to call multiple times — only generates once (guarded by initialised flag).
    /// Called by Start() for Floor 1, by FloorRoot.Bootstrap() for Floor 2+.
    /// </summary>
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

        // Notify sibling TileInfluenceManager that bounds changed.
        var floorRoot = GetComponentInParent<FloorRoot>();
        floorRoot?.TileInfluence?.OnBoundsExpanded();
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

    // ── Public API ────────────────────────────────────────────────

    public void RevealTile(Vector3Int pos)
    {
        fogTilemap.SetTile(pos, null);
    }

    public void RefogTile(Vector3Int pos)
    {
        if (IsWithinBounds(pos))
            fogTilemap.SetTile(pos, fogTile);
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