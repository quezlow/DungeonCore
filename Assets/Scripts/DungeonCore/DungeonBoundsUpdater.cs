using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Per-floor dynamic camera bounds. Lives on the same GameObject as the
/// floor's PolygonCollider2D (the "DungeonBounds" child of each FloorRoot).
///
/// BEHAVIOUR
///   - Listens to its floor's TileInfluenceManager.OnTileCountChanged.
///   - On change, rebuilds the PolygonCollider2D points to an axis-aligned
///     bounding box around all owned tiles, plus paddingCells of slack on
///     every side.
///   - Enforces a minimum size derived from the actual camera viewport at
///     max zoom × viewportSafetyMultiplier — so a tiny starter dungeon never
///     produces bounds narrower than the camera frustum (which would cause
///     the Cinemachine confiner to lock the camera in that dimension).
///   - Recalcs are coalesced into a single end-of-frame pass via LateUpdate.
///   - If this floor is the active floor at the time of the rebuild, asks
///     DungeonCameraController to invalidate the confiner cache. Otherwise
///     the geometry is updated silently and the next floor switch's
///     FloorManager.MoveCameraToFloor() will invalidate naturally.
///
/// SETUP
///   - Place a child GameObject named "DungeonBounds" under each FloorRoot.
///   - Add a PolygonCollider2D component (any starting points — they'll be
///     overwritten on Start) and this script.
///   - Wire that PolygonCollider2D into FloorRoot.CameraBounds.
///
/// COORDINATE NOTES
///   - TileInfluenceManager.CellToWorld returns world space (including the
///     floor's -2000 * floorIndex Y offset).
///   - PolygonCollider2D.points are local-space relative to its transform.
///   - We convert with transform.InverseTransformPoint(worldPoint).
/// </summary>
[RequireComponent(typeof(PolygonCollider2D))]
public class DungeonBoundsUpdater : MonoBehaviour
{
    [Header("Padding")]
    [Tooltip("Cells of empty space added on every side of the AABB around owned tiles.")]
    [SerializeField] private int paddingCells = 6;

    [Header("Minimum Size")]
    [Tooltip("Multiplier on the camera viewport size at max zoom. 1.0 = bounds exactly fill the viewport (camera will lock — DO NOT USE). 1.5 = 50% extra pan room on a tiny dungeon. Increase for more headroom on small dungeons.")]
    [SerializeField] private float viewportSafetyMultiplier = 1.5f;

    [Tooltip("Fallback min width (world units) used only if Camera.main or DungeonCameraController is unavailable.")]
    [SerializeField] private float fallbackMinWidth = 64f;

    [Tooltip("Fallback min height (world units) used only if Camera.main or DungeonCameraController is unavailable.")]
    [SerializeField] private float fallbackMinHeight = 36f;

    // ── References ────────────────────────────────────────────────

    private PolygonCollider2D poly;
    private FloorRoot myFloor;
    private TileInfluenceManager influence;

    // ── State ─────────────────────────────────────────────────────

    private bool boundsDirty;

    // Scratch buffer for SetPath — avoids per-recalc allocation.
    private readonly Vector2[] pointBuffer = new Vector2[4];

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        poly = GetComponent<PolygonCollider2D>();

        myFloor = GetComponentInParent<FloorRoot>();
        if (myFloor == null)
        {
            Debug.LogError($"[DungeonBoundsUpdater] No FloorRoot in parent chain of '{name}'. Disabling.");
            enabled = false;
            return;
        }

        influence = myFloor.TileInfluence;
        if (influence == null)
        {
            Debug.LogError($"[DungeonBoundsUpdater] FloorRoot on '{myFloor.name}' has no TileInfluenceManager. Disabling.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (influence != null)
            influence.OnClaimedTileCountChanged += HandleTileCountChanged;
    }

    private void OnDisable()
    {
        if (influence != null)
            influence.OnClaimedTileCountChanged -= HandleTileCountChanged;
    }

    private void Start()
    {
        // Initial recalc — covers starter area on new floors and pre-loaded
        // tile set on save-loaded floors.
        boundsDirty = true;
    }

    private void LateUpdate()
    {
        if (!boundsDirty) return;
        boundsDirty = false;
        Recalculate();
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleTileCountChanged(int _) => boundsDirty = true;

    // ── Recalculation ─────────────────────────────────────────────

    private void Recalculate()
    {
        if (poly == null || influence == null) return;

        IReadOnlyCollection<Vector3Int> owned = influence.ClaimedTiles;

        float minWorldX, maxWorldX, minWorldY, maxWorldY;

        if (owned == null || owned.Count == 0)
        {
            // Defensive fallback: empty owned set.
            (float minW, float minH) = ComputeMinSize();
            Vector3 c = transform.position;
            minWorldX = c.x - minW * 0.5f;
            maxWorldX = c.x + minW * 0.5f;
            minWorldY = c.y - minH * 0.5f;
            maxWorldY = c.y + minH * 0.5f;
        }
        else
        {
            // Walk owned tiles once to find the cell-space AABB.
            bool first = true;
            int cellMinX = 0, cellMaxX = 0, cellMinY = 0, cellMaxY = 0;
            foreach (var cell in owned)
            {
                if (first)
                {
                    cellMinX = cellMaxX = cell.x;
                    cellMinY = cellMaxY = cell.y;
                    first = false;
                }
                else
                {
                    if (cell.x < cellMinX) cellMinX = cell.x;
                    if (cell.x > cellMaxX) cellMaxX = cell.x;
                    if (cell.y < cellMinY) cellMinY = cell.y;
                    if (cell.y > cellMaxY) cellMaxY = cell.y;
                }
            }

            // Apply padding in cell space.
            cellMinX -= paddingCells;
            cellMaxX += paddingCells;
            cellMinY -= paddingCells;
            cellMaxY += paddingCells;

            // CellToWorld returns the tile centre. Offset by half a cell on
            // each side to get the outer edges of the min/max cells.
            Vector3 minWorld = influence.CellToWorld(new Vector3Int(cellMinX, cellMinY, 0));
            Vector3 maxWorld = influence.CellToWorld(new Vector3Int(cellMaxX, cellMaxY, 0));

            // Measured half-cell extents (handles any grid scale).
            Vector3 cellSpan = influence.CellToWorld(new Vector3Int(1, 1, 0))
                             - influence.CellToWorld(new Vector3Int(0, 0, 0));
            float halfCellX = Mathf.Abs(cellSpan.x) * 0.5f;
            float halfCellY = Mathf.Abs(cellSpan.y) * 0.5f;

            minWorldX = minWorld.x - halfCellX;
            maxWorldX = maxWorld.x + halfCellX;
            minWorldY = minWorld.y - halfCellY;
            maxWorldY = maxWorld.y + halfCellY;
        }

        // Enforce minimum size by inflating around the AABB centre.
        // Min size is derived from the camera viewport at max zoom, so the
        // confiner can never clamp narrower than the camera frustum.
        (float minWidth, float minHeight) = ComputeMinSize();

        float width = maxWorldX - minWorldX;
        if (width < minWidth)
        {
            float cx = (minWorldX + maxWorldX) * 0.5f;
            minWorldX = cx - minWidth * 0.5f;
            maxWorldX = cx + minWidth * 0.5f;
        }
        float height = maxWorldY - minWorldY;
        if (height < minHeight)
        {
            float cy = (minWorldY + maxWorldY) * 0.5f;
            minWorldY = cy - minHeight * 0.5f;
            maxWorldY = cy + minHeight * 0.5f;
        }

        // World-space corners → local-space relative to this collider's transform.
        Vector3 worldBL = new Vector3(minWorldX, minWorldY, 0f);
        Vector3 worldBR = new Vector3(maxWorldX, minWorldY, 0f);
        Vector3 worldTR = new Vector3(maxWorldX, maxWorldY, 0f);
        Vector3 worldTL = new Vector3(minWorldX, maxWorldY, 0f);

        pointBuffer[0] = transform.InverseTransformPoint(worldBL);
        pointBuffer[1] = transform.InverseTransformPoint(worldBR);
        pointBuffer[2] = transform.InverseTransformPoint(worldTR);
        pointBuffer[3] = transform.InverseTransformPoint(worldTL);

        poly.pathCount = 1;
        poly.SetPath(0, pointBuffer);

        // Invalidate the confiner cache only if this is the active floor.
        DungeonCameraController.Instance?.InvalidateBoundsForFloor(myFloor.FloorIndex);
    }

    /// <summary>
    /// Computes the minimum bounds size from the camera viewport at max zoom,
    /// scaled by viewportSafetyMultiplier. Falls back to inspector values if
    /// the camera or controller aren't available.
    /// </summary>
    private (float width, float height) ComputeMinSize()
    {
        var controller = DungeonCameraController.Instance;
        Camera cam = Camera.main;

        if (controller != null && cam != null && cam.aspect > 0.01f)
        {
            float orthoMax = controller.MaxZoom;          // ortho size = half height
            float viewH = orthoMax * 2f;
            float viewW = viewH * cam.aspect;
            return (viewW * viewportSafetyMultiplier,
                    viewH * viewportSafetyMultiplier);
        }

        return (fallbackMinWidth, fallbackMinHeight);
    }

    // ── Debug ─────────────────────────────────────────────────────

    [ContextMenu("Log Current Bounds")]
    private void DebugLogBounds()
    {
        if (poly == null) poly = GetComponent<PolygonCollider2D>();
        if (poly == null) { Debug.Log("[DungeonBoundsUpdater] No PolygonCollider2D."); return; }

        var path = poly.GetPath(0);
        if (path == null || path.Length == 0) { Debug.Log("[DungeonBoundsUpdater] No polygon path."); return; }

        float minX = path[0].x, maxX = path[0].x, minY = path[0].y, maxY = path[0].y;
        foreach (var p in path)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        (float mw, float mh) = ComputeMinSize();
        Debug.Log($"[DungeonBoundsUpdater] Floor {myFloor?.FloorIndex} bounds (local): " +
                  $"{maxX - minX:F1} × {maxY - minY:F1} | " +
                  $"min size: {mw:F1} × {mh:F1} | " +
                  $"owned tiles: {influence? .ClaimedTiles?.Count ?? 0}");
    }
}