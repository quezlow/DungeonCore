using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor terrain manager.
///
/// TIER PROGRESSION
///   Each floor's radius is set ONCE from DungeonCore.Progression.FloorRadius(N)
///   when the floor is first generated. Per-level terrain expansion is removed.
///   Floors do not grow within a tier — only the initial radius set at floor
///   creation defines the floor's size.
///
/// FOG TILE
///   The fog layer is colour-driven (DungeonShadow's fog match paints it the
///   deep-void tone). If the Fog Tile slot is empty or the assigned Tile has
///   no sprite, a solid white tile is built at runtime — recommended: leave
///   the slot empty and let the colour do the work.
/// </summary>
[DefaultExecutionOrder(-10)]
public class DungeonTerrain : MonoBehaviour
{
[Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap fogTilemap;

    public Tilemap FloorTilemap => floorTilemap;
    public Tilemap FogTilemap => fogTilemap;

    // Cells ever revealed by digging or by the terrain generator are PERMANENT:
    // a breach that unclaims them must not re-fog them. Ownership-only reveals
    // (a claimed cell that was never dug) are NOT added here, so they can fog
    // back when the fringe recedes. RefogTile consults this set.
    private readonly HashSet<Vector3Int> permanentlyRevealed = new HashSet<Vector3Int>();
    public void MarkPermanentlyRevealed(Vector3Int pos) => permanentlyRevealed.Add(pos);

    [Header("Tile Assets")]
    [SerializeField] private TileBase floorTile;
    [Tooltip("Optional. Left empty (recommended with Fog Matches Void), a solid white tile is " +
             "built at runtime and the fog colour does all the work. Assign custom art only if " +
             "you also turn DungeonShadow's Fog Matches Void off.")]
    [SerializeField] private TileBase fogTile;

    [Header("Fallback Radius")]
    [Tooltip("Used only if DungeonCore is missing or has no progression table.")]
    [SerializeField] private int fallbackRadius = 100;

    private int currentRadius;
    private Vector3Int coreCell;
    private bool initialised = false;
    private FloorRoot myFloor;
    private Tile runtimeFogTile;

    private void Start()
    {
        myFloor = GetComponentInParent<FloorRoot>();

        if (myFloor != null && myFloor.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null) { Debug.LogError("[DungeonTerrain] DungeonCore.Instance is null (Floor 0)."); return; }
            GenerateAt(floorTilemap.WorldToCell(DungeonCore.Instance.transform.position));
        }
    }

    /// <summary>Pulls radius from DungeonCore's progression table based on this floor's index.</summary>
    private int RadiusForThisFloor()
    {
        if (myFloor == null || DungeonCore.Instance == null || DungeonCore.Instance.Progression == null)
            return fallbackRadius;
        return DungeonCore.Instance.Progression.FloorRadius(myFloor.FloorIndex);
    }

    public void GenerateAt(Vector3Int centre)
    {
        if (initialised) return;
        initialised = true;
        EnsureFogTile();
        coreCell = centre;
        currentRadius = RadiusForThisFloor();
        PaintTerrain(coreCell, currentRadius);
    }

    /// <summary>Fog is colour-driven; the sprite only needs to be solid and
    /// bright. If the slot is empty or the assigned Tile has no sprite, build a
    /// solid white tile at runtime so the fog can never silently vanish — an
    /// empty slot, a sprite-less Tile asset, or a transparent sprite all used
    /// to render as no fog at all, exposing unexplored terrain.</summary>
    private void EnsureFogTile()
    {
        bool spriteless = fogTile is Tile assigned && assigned.sprite == null;
        if (fogTile != null && !spriteless) return;

        if (runtimeFogTile == null)
        {
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            runtimeFogTile = ScriptableObject.CreateInstance<Tile>();
            runtimeFogTile.sprite = sprite;
            runtimeFogTile.color = Color.white;
            runtimeFogTile.flags = TileFlags.None;   // the layer colour drives the look
            runtimeFogTile.name = "RuntimeFogTile";
        }

        fogTile = runtimeFogTile;
        Debug.Log("[DungeonTerrain] Fog tile missing or sprite-less — using runtime solid white (colour-driven fog).");
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
        // Never re-fog ground the player dug or the generator exposed; only
        // ownership-revealed cells return to the dark.
        if (permanentlyRevealed.Contains(pos)) return;
        if (IsWithinBounds(pos)) fogTilemap.SetTile(pos, fogTile);
    }

    public bool IsWithinBounds(Vector3Int pos) => IsWithinRadius(pos, currentRadius);
    public Vector3Int CoreCell => coreCell;
    public int CurrentRadius => currentRadius;

    private bool IsWithinRadius(Vector3Int pos, int radius)
    {
        int dx = pos.x - coreCell.x;
        int dy = pos.y - coreCell.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }
}