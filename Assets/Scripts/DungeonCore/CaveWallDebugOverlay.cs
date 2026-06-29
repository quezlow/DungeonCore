using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// STAGE 2 — Debug visualiser for CaveWallClassifier. Tints every solid cell
/// across the dug-out region so you can confirm solid vs open and, above all,
/// which cells are south-facing and what face variant each would take — BEFORE
/// any real walls are painted in Stage 3.
///
/// Attach to a child of a FloorRoot (under its Grid). Assign an overlay Tilemap
/// whose renderer is on a front sorting layer (e.g. WorldUI) so the colours read
/// on top, and an UnlockedTile asset so per-cell SetColor works.
///
/// Toggle with the toggle key (default F8) at runtime, or the context menu.
/// While visible it refreshes whenever a tile is mined.
/// </summary>
[DisallowMultipleComponent]
public class CaveWallDebugOverlay : MonoBehaviour
{
    [Header("Overlay")]
    [Tooltip("Tilemap to paint the debug colours onto. Put its renderer on a " +
             "front sorting layer (e.g. WorldUI) so it draws over walls and fog.")]
    [SerializeField] private Tilemap overlayTilemap;

    [Tooltip("An UnlockedTile asset (white sprite), so per-cell SetColor works.")]
    [SerializeField] private TileBase overlayTile;

    [Header("Toggle")]
    [SerializeField] private Key toggleKey = Key.F8;
    [SerializeField] private bool visibleOnStart = false;

    [Header("Colours")]
    [SerializeField] private Color solidColor = new Color(0.45f, 0.45f, 0.50f, 0.40f); // solid, not south-facing
    [SerializeField] private Color straightColor = new Color(0.20f, 0.80f, 1.00f, 0.55f); // cyan
    [SerializeField] private Color cornerWColor = new Color(0.30f, 0.90f, 0.40f, 0.55f); // green
    [SerializeField] private Color cornerEColor = new Color(0.20f, 0.50f, 1.00f, 0.55f); // blue
    [SerializeField] private Color pillarColor = new Color(1.00f, 0.30f, 0.85f, 0.55f); // magenta
    [SerializeField] private Color nubEastColor = new Color(1.00f, 0.60f, 0.15f, 0.55f); // orange
    [SerializeField] private Color nubWestColor = new Color(1.00f, 0.90f, 0.20f, 0.55f); // yellow

    [Header("Region")]
    [Tooltip("Cells of padding added around the mined bounding box.")]
    [SerializeField] private int regionPadding = 3;

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private CaveWallClassifier classifier;
    private bool visible;
    private bool subscribed;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[CaveWallDebugOverlay] No FloorRoot in parents — disabling.");
            enabled = false;
            return;
        }
        influence = floor.TileInfluence;
        if (influence != null) classifier = new CaveWallClassifier(influence, floor.FeatureGenerator);
    }

    private void OnEnable()
    {
        if (influence != null && !subscribed)
        {
            influence.OnTileMined += HandleTileMined;
            subscribed = true;
        }
        SetVisible(visibleOnStart);
    }

    private void OnDisable()
    {
        if (influence != null && subscribed)
        {
            influence.OnTileMined -= HandleTileMined;
            subscribed = false;
        }
        Clear();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetVisible(!visible);
    }

    private void HandleTileMined(Vector3Int _)
    {
        if (visible) Rebuild();
    }

    [ContextMenu("Toggle Overlay")]
    private void ToggleFromMenu() => SetVisible(!visible);

    private void SetVisible(bool on)
    {
        visible = on;
        if (on) Rebuild();
        else Clear();
    }

    private void Clear()
    {
        if (overlayTilemap != null) overlayTilemap.ClearAllTiles();
    }

    private void Rebuild()
    {
        if (overlayTilemap == null || overlayTile == null || classifier == null || influence == null)
            return;

        overlayTilemap.ClearAllTiles();

        IReadOnlyCollection<Vector3Int> mined = influence.MinedTiles;
        if (mined == null || mined.Count == 0) return;

        // Bounding box of the dug-out region, padded so the wall ring is included.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in mined)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        minX -= regionPadding; minY -= regionPadding;
        maxX += regionPadding; maxY += regionPadding;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                if (!classifier.IsSolid(cell)) continue;   // open cells: leave the floor showing

                Color col = solidColor;
                switch (classifier.FaceVariant(cell))
                {
                    case CaveFace.Straight: col = straightColor; break;
                    case CaveFace.CornerW: col = cornerWColor; break;
                    case CaveFace.CornerE: col = cornerEColor; break;
                    case CaveFace.Pillar: col = pillarColor; break;
                    case CaveFace.NubEast: col = nubEastColor; break;
                    case CaveFace.NubWest: col = nubWestColor; break;
                        // CaveFace.None => solid but not south-facing => solidColor
                }

                overlayTilemap.SetTile(cell, overlayTile);
                overlayTilemap.SetTileFlags(cell, TileFlags.None); // belt-and-suspenders with UnlockedTile
                overlayTilemap.SetColor(cell, col);
            }
        }
    }
}
