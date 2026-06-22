using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// STAGE 3a — Paints the cave wall CAPS (rock tops) for one floor. No faces yet
/// (3b). Caps go on CLAIMED solid cells — your dungeon walls, the "claimed stone."
/// Mined cells are open floor (no cap); unclaimed cells stay under fog (no cap).
///
/// Architecture (locked): no RuleTile. The CaveWallClassifier computes the 16-mask,
/// this renderer looks up the matching cap sprite, sliced straight off MainLev.png
/// at runtime by the coordinates pinned in cap_reference.png. The tiles are
/// UnlockedTile instances so per-cell SetColor (the Stage 4 fade / Stage 5 tint)
/// will take effect later.
///
/// Attach to a child of a FloorRoot, under its Grid. Assign the caps Tilemap
/// (Sorting Layer = Player, Order = 0, Renderer Mode = Individual) and the sheet
/// texture (MainLev). It rebuilds when claimed/mined state changes.
/// </summary>
[DisallowMultipleComponent]
public class CaveWallRenderer : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Caps tilemap. Sorting Layer = Player, Order in Layer = 0, Mode = Individual.")]
    [SerializeField] private Tilemap capsTilemap;

    [Header("Sheet")]
    [Tooltip("MainLev.png (the wall sheet). Sliced at runtime by cell coordinate.")]
    [SerializeField] private Texture2D sheet;
    [SerializeField] private int cellSize = 32;

    // Cap mask (N=1,E=2,S=4,W=8; set = solid) -> sheet cell (col, row from top),
    // pinned from cap_reference.png. A wrong-looking cap is a one-line fix here.
    private static readonly Vector2Int[] CapCell =
    {
        new Vector2Int(6, 8),   // 0  none      -> pillar top
        new Vector2Int(6, 8),   // 1  N         -> pillar top
        new Vector2Int(14, 9),  // 2  E         -> nubEast top
        new Vector2Int(7, 6),   // 3  N+E       -> cornerW (SW) top
        new Vector2Int(6, 0),   // 4  S
        new Vector2Int(11, 3),  // 5  N+S
        new Vector2Int(0, 0),   // 6  E+S
        new Vector2Int(0, 1),   // 7  N+E+S
        new Vector2Int(13, 9),  // 8  W         -> nubWest top
        new Vector2Int(11, 6),  // 9  N+W       -> cornerE (SE) top
        new Vector2Int(8, 3),   // 10 E+W
        new Vector2Int(2, 4),   // 11 N+E+W     -> straight S-wall top
        new Vector2Int(5, 0),   // 12 S+W
        new Vector2Int(5, 1),   // 13 N+S+W
        new Vector2Int(1, 0),   // 14 E+S+W
        new Vector2Int(1, 1),   // 15 all       -> interior
    };

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private CaveWallClassifier classifier;
    private TileBase[] capTiles;
    private bool subscribed;
    private bool dirty;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[CaveWallRenderer] No FloorRoot in parents — disabling.");
            enabled = false;
            return;
        }
        influence = floor.TileInfluence;
        if (influence != null) classifier = new CaveWallClassifier(influence);
        BuildCapTiles();
    }

    private void BuildCapTiles()
    {
        if (sheet == null) { Debug.LogWarning("[CaveWallRenderer] No sheet texture assigned."); return; }
        capTiles = new TileBase[16];
        for (int mask = 0; mask < 16; mask++)
            capTiles[mask] = MakeTile(CapCell[mask].x, CapCell[mask].y);
    }

    private TileBase MakeTile(int col, int rowFromTop)
    {
        int px = col * cellSize;
        int py = sheet.height - (rowFromTop + 1) * cellSize;   // sheet rows top-down; texture Y bottom-up
        Sprite spr = Sprite.Create(sheet, new Rect(px, py, cellSize, cellSize),
                                   new Vector2(0.5f, 0.5f), cellSize);
        var tile = ScriptableObject.CreateInstance<UnlockedTile>();
        tile.sprite = spr;
        return tile;
    }

    private void OnEnable()
    {
        if (influence != null && !subscribed)
        {
            influence.OnClaimedTileCountChanged += MarkDirty;   // a wall was claimed
            influence.OnTileCountChanged += MarkDirty;   // a wall was mined into floor
            subscribed = true;
        }
        dirty = true;   // first build once the floor is ready
    }

    private void OnDisable()
    {
        if (influence != null && subscribed)
        {
            influence.OnClaimedTileCountChanged -= MarkDirty;
            influence.OnTileCountChanged -= MarkDirty;
            subscribed = false;
        }
        if (capsTilemap != null) capsTilemap.ClearAllTiles();
    }

    private void MarkDirty(int _) => dirty = true;

    // Coalesce a burst of claims/mines in one frame into a single rebuild.
    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        RebuildAll();
    }

    [ContextMenu("Rebuild Walls")]
    public void RebuildAll()
    {
        if (capsTilemap == null || classifier == null || influence == null || capTiles == null) return;

        capsTilemap.ClearAllTiles();

        // Caps land on claimed solid cells (claimed stone = your walls).
        // claimed + mined  -> open floor, no cap.
        // unclaimed        -> under fog, no cap.
        foreach (Vector3Int cell in influence.ClaimedTiles)
        {
            if (!classifier.IsSolid(cell)) continue;
            capsTilemap.SetTile(cell, capTiles[classifier.CapMask(cell)]);
        }
    }
}
