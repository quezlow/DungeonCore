using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A corner minimap of the active floor. The tile layer is a painted texture (one block per cell,
/// auto-fit to the claimed bounds) that repaints only when tiles change or the floor changes. Live
/// adventurers (red), monsters (green) and the core (gold) are pooled UI dots positioned each
/// frame, with an outline showing the current camera view. Click the map to pan the camera there.
///
/// SCENE SETUP (place/anchor the panel wherever you like -- e.g. top-right):
///   MinimapPanel
///     +-- CollapseButton (optional) -> OnClick calls Minimap.Toggle()
///     +-- Body (assign to 'body' -- the part that hides on collapse)
///           +-- MapImage  (RawImage, Raycast Target ON) -> put THIS component here
///           |     +-- DotLayer (empty RectTransform, stretched to fill MapImage) -> assign 'dotLayer'
///           |     +-- ViewRect (Image outline, Raycast Target OFF)               -> assign 'viewRect'
///           +-- FloorLabel (TMP text) -> assign 'floorLabel'
///   Assign nothing else; dots are created at runtime.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class Minimap : MonoBehaviour, IPointerClickHandler
{
    [Header("References")]
    [SerializeField] private RectTransform dotLayer;
    [SerializeField] private RectTransform viewRect;   // optional camera-view outline
    [SerializeField] private TMP_Text floorLabel;
    [SerializeField] private GameObject body;          // the collapsible part

    [Header("Texture")]
    [SerializeField, Min(32)] private int textureSize = 220;
    [SerializeField, Min(0)] private int paddingCells = 2;

    [Tooltip("Smallest square the map will frame, in cells. A fresh floor holds " +
             "only its starter chamber (starterRoomRadius ~3, so roughly 11-13 " +
             "cells across with padding), and without a floor under the zoom the " +
             "texture blows that up to ~18 pixels a cell -- one chamber filling " +
             "the map, with the camera outline bigger than the frame it sits in. " +
             "64 clears the viewport at max zoom in both axes, so the outline " +
             "always fits inside rather than clamping. Capped at the floor's own " +
             "diameter, so the frame never shows more void than there is floor.")]
    [SerializeField, Min(8)] private int minSpanCells = 64;

    [Header("Tile Colours")]
    [SerializeField] private Color rockColour = new Color(0.06f, 0.06f, 0.10f);
    [SerializeField] private Color wallColour = new Color(0.28f, 0.26f, 0.34f);
    [SerializeField] private Color floorColour = new Color(0.62f, 0.58f, 0.50f);
    [SerializeField] private Color roomColour = new Color(0.42f, 0.34f, 0.52f);
    [SerializeField] private Color coreColour = new Color(1f, 0.82f, 0.25f);
    [SerializeField] private Color entranceColour = new Color(0.45f, 0.80f, 0.95f);

    [Header("Dot Colours")]
    [SerializeField] private Color adventurerDot = new Color(0.90f, 0.25f, 0.25f);
    [SerializeField] private Color monsterDot = new Color(0.35f, 0.85f, 0.40f);
    [Tooltip("Wild and invading monsters are hostile, so they read red like adventurers.")]
    [SerializeField] private Color hostileMonsterDot = new Color(0.90f, 0.25f, 0.25f);
    [Tooltip("Bodies belonging to a faction that is NOT currently at war -- " +
             "dwarves while standing holds. A third colour because the other " +
             "two are both lies about them: green says the player commands " +
             "them, red says they are coming for the core.")]
    [SerializeField] private Color neutralMonsterDot = new Color(0.55f, 0.60f, 0.85f);
    [SerializeField] private Color coreDot = new Color(1f, 0.82f, 0.25f);
    [SerializeField, Min(2f)] private float dotSize = 5f;

    // -- runtime --
    private RawImage mapImage;
    private RectTransform mapRect;
    private Camera mainCam;
    private Texture2D texture;
    private Color32[] pixels;

    private TileInfluenceManager influence;   // active floor's tiles
    private FloorRoot floor;
    private bool dirty;
    [Tooltip("Minimum seconds between full repaints. Each one rebuilds the whole " +
             "texture and re-uploads it, so claiming at speed must not drive it per frame.")]
    [SerializeField] private float repaintInterval = 0.2f;
    private float nextRepaintAt;

    // paint frame (bounds + scale) shared by the per-frame mapping
    private Vector2Int paintBoundsMin;
    private float paintScale = 1f;
    private Vector3 worldMin;
    private float worldPerCell = 1f;
    private bool hasPaint;

    private readonly List<DungeonAdventurer> advBuf = new();
    private readonly List<DungeonMonster> monBuf = new();
    private readonly List<Image> dotPool = new();

    private void Awake()
    {
        // This component is meant to live on MapImage, but it sits on the panel
        // root in the scene. GetComponent then binds to the PANEL's background
        // RawImage: the map paints there, MapImage's own untextured RawImage
        // draws on top and hides it, and the dots anchor to the wrong rect.
        // Resolve the real map surface by name so either placement works.
        mapImage = null;
        var mapTf = FindMapSurface(transform);
        if (mapTf != null) mapImage = mapTf.GetComponent<RawImage>();
        if (mapImage == null) mapImage = GetComponent<RawImage>();
        mapRect = (RectTransform)mapImage.transform;

        // If we retargeted, the panel's own RawImage must stop drawing over the
        // map -- it is the flat dark square the player was left staring at.
        var ownImage = GetComponent<RawImage>();
        if (ownImage != null && ownImage != mapImage) ownImage.enabled = false;
        texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        pixels = new Color32[textureSize * textureSize];
        mapImage.texture = texture;

        // The panel's own RawImage (RequireComponent forces one) is the dark
        // square; the placeholder label reads 'New Text' from the prefab.
        if (floorLabel != null) floorLabel.text = "";
    }

    private void OnEnable()
    {
        mainCam = Camera.main;
        TryHookFloorManager();
        HookFloor();
        dirty = true;

        UnlockState.OnChanged += HandleUnlockChanged;
        ApplyGate();
    }

    private void OnDisable()
    {
        if (FloorManager.Instance != null)
            FloorManager.Instance.OnActiveFloorChanged -= OnFloorChanged;
        floorEventsHooked = false;
        UnlockState.OnChanged -= HandleUnlockChanged;
        UnhookInfluence();
    }

    // FloorManager carries no [DefaultExecutionOrder], so its Awake races this
    // component's OnEnable at scene load. When it lost, the subscription was
    // skipped SILENTLY and never attempted again: the map still hooked floor
    // 0's influence on the LateUpdate retry, so it looked perfectly healthy
    // there, and then ignored every floor change for the rest of the session.
    // The label froze at Floor 0, floor 0's tiles stayed painted, and the view
    // outline vanished the moment the camera moved to another floor's Y offset
    // (it clamps to the painted frame and hides itself when nothing is left).
    // Retried until it takes; the identity check in LateUpdate is the net.
    private bool floorEventsHooked;

    private void TryHookFloorManager()
    {
        if (floorEventsHooked || FloorManager.Instance == null) return;
        FloorManager.Instance.OnActiveFloorChanged += OnFloorChanged;
        floorEventsHooked = true;
    }

    // Gated behind the Map the Deep Warren research node. The whole map hides
    // until the key is set; it appears the moment research completes.
    private void HandleUnlockChanged(string key) => ApplyGate();

    private void ApplyGate()
    {
        bool unlocked = UnlockState.IsUnlocked("tech.minimap");
        if (body != null && body.activeSelf != unlocked) body.SetActive(unlocked);
        // Hide the painted surface itself until unlocked, so no square shows
        // through while the node is unresearched.
        if (mapImage != null && mapImage.enabled != unlocked) mapImage.enabled = unlocked;
        // The texture was last painted while locked (all rock = dark). On unlock,
        // force a repaint so it shows the claimed territory, not a stale dark fill.
        if (unlocked) dirty = true;
    }

    private void OnFloorChanged(int _)
    {
        HookFloor();
        dirty = true;
    }

    private void HookFloor()
    {
        UnhookInfluence();
        floor = FloorManager.Instance != null ? FloorManager.Instance.ActiveFloor : null;
        influence = floor != null ? floor.TileInfluence : null;
        if (influence != null)
        {
            influence.OnTileCountChanged += OnTilesChanged;
            influence.OnClaimedTileCountChanged += OnTilesChanged;
        }
        UpdateFloorLabel();
    }

    private void UnhookInfluence()
    {
        if (influence != null)
        {
            influence.OnTileCountChanged -= OnTilesChanged;
            influence.OnClaimedTileCountChanged -= OnTilesChanged;
        }
    }

    private void OnTilesChanged(int _) => dirty = true;

    private void UpdateFloorLabel()
    {
        if (floorLabel == null || FloorManager.Instance == null) return;
        int idx = FloorManager.Instance.ActiveFloorIndex;
        string name = FloorManager.Instance.GetFloorName(idx);
        floorLabel.text = string.IsNullOrEmpty(name) ? $"Floor {idx}" : name;
    }

    private void LateUpdate()
    {
        // The floor is often not ready when OnEnable runs at scene load, so
        // HookFloor leaves influence null and the tile-change subscriptions are
        // never made. Retry until it takes -- otherwise nothing can ever mark the
        // map dirty again and it stays a flat rock fill until the panel is
        // toggled by hand.
        //
        // The floor IDENTITY test beside it is the safety net. Following the
        // active floor must not depend on the subscription having been made,
        // because when that race was lost the map went on looking correct on
        // floor 0 and silently stopped following. One reference comparison per
        // frame buys immunity from ever losing that race again.
        TryHookFloorManager();
        var activeFloor = FloorManager.Instance != null
                        ? FloorManager.Instance.ActiveFloor : null;
        if (influence == null || (activeFloor != null && floor != activeFloor))
            HookFloor();

        // Only clear the flag once a repaint could actually draw something;
        // consuming it on a bailed paint is what stranded the map.
        // Repainting rebuilds every pixel, walks all claimed tiles and re-uploads
        // the texture. Push claims continuously, so doing that per frame is what
        // made a half-claimed floor crawl. Only repaint when the map is actually
        // on screen, and never faster than repaintInterval; dirty simply waits.
        if (dirty)
        {
            bool onScreen = mapImage != null && mapImage.enabled
                            && (body == null || body.activeInHierarchy);
            if (onScreen && Time.unscaledTime >= nextRepaintAt)
            {
                nextRepaintAt = Time.unscaledTime + Mathf.Max(0.02f, repaintInterval);
                Repaint();
                if (influence != null && influence.ClaimedTileCount > 0) dirty = false;
            }
        }
        UpdateDots();
        UpdateViewRect();
    }

    // -- paint the tile texture --------------------------------------------------

    private void Repaint()
    {
        hasPaint = false;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = rockColour;

        if (influence == null || influence.ClaimedTileCount == 0)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return;
        }

        // Bounds = claimed cells (+ padding).
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in influence.ClaimedTiles)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        // Centre the map on the core: take a square around the core cell just big enough to
        // hold every claimed tile. Floors without a core fall back to the plain claimed bounds.
        var coreForBounds = DungeonCore.Instance;
        if (coreForBounds != null && OnThisFloor(coreForBounds.transform.position.y))
        {
            Vector3Int coreCell = influence.WorldToCell(coreForBounds.transform.position);
            int radius = 0;
            radius = Mathf.Max(radius, Mathf.Abs(coreCell.x - minX));
            radius = Mathf.Max(radius, Mathf.Abs(maxX - coreCell.x));
            radius = Mathf.Max(radius, Mathf.Abs(coreCell.y - minY));
            radius = Mathf.Max(radius, Mathf.Abs(maxY - coreCell.y));
            radius += paddingCells;
            minX = coreCell.x - radius; maxX = coreCell.x + radius;
            minY = coreCell.y - radius; maxY = coreCell.y + radius;
        }
        else
        {
            minX -= paddingCells; minY -= paddingCells; maxX += paddingCells; maxY += paddingCells;
        }
        // Minimum framing, applied to whichever branch above produced the box.
        // Only ever grows it: a floor with plenty claimed is untouched.
        int floorSpan = floor != null && floor.Terrain != null
                        ? floor.Terrain.CurrentRadius * 2 + 1
                        : int.MaxValue;
        int wantSpan = Mathf.Min(minSpanCells, floorSpan);
        InflateToSpan(ref minX, ref maxX, wantSpan);
        InflateToSpan(ref minY, ref maxY, wantSpan);

        paintBoundsMin = new Vector2Int(minX, minY);
        int spanX = maxX - minX + 1, spanY = maxY - minY + 1;
        paintScale = (float)textureSize / Mathf.Max(spanX, spanY);

        worldPerCell = Mathf.Abs(influence.CellToWorld(new Vector3Int(1, 0, 0)).x
                                 - influence.CellToWorld(Vector3Int.zero).x);
        if (worldPerCell < 0.0001f) worldPerCell = 1f;
        worldMin = influence.CellToWorld(new Vector3Int(minX, minY, 0))
                   - new Vector3(worldPerCell * 0.5f, worldPerCell * 0.5f, 0f);
        hasPaint = true;

        // Walls (claimed, not dug) then dug floor on top.
        foreach (var c in influence.ClaimedTiles)
            if (!influence.IsTileMined(c)) PaintCell(c, wallColour);
        foreach (var c in influence.MinedTiles) PaintCell(c, floorColour);

        // Room tint on the dug tiles they cover.
        if (floor != null && floor.Entities != null)
        {
            var rooms = floor.Entities.GetAll<RoomAnchor>();
            for (int r = 0; r < rooms.Count; r++)
            {
                var anchor = rooms[r];
                if (anchor == null || !anchor.IsValid) continue;
                var tiles = anchor.GetRoomTiles();
                if (tiles == null) continue;
                foreach (var c in tiles) PaintCell(c, roomColour);
            }
        }

        // Core + entrance markers (only when they belong to this floor).
        var core = DungeonCore.Instance;
        if (core != null && OnThisFloor(core.transform.position.y))
            PaintCell(influence.WorldToCell(core.transform.position), coreColour);

        var entrance = DungeonEntrance.Instance;
        if (entrance != null && OnThisFloor(entrance.SpawnPosition.y))
            PaintCell(influence.WorldToCell(entrance.SpawnPosition), entranceColour);

        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    private void PaintCell(Vector3Int cell, Color32 colour)
    {
        int px0 = Mathf.FloorToInt((cell.x - paintBoundsMin.x) * paintScale);
        int py0 = Mathf.FloorToInt((cell.y - paintBoundsMin.y) * paintScale);
        int px1 = Mathf.Max(px0 + 1, Mathf.CeilToInt((cell.x - paintBoundsMin.x + 1) * paintScale));
        int py1 = Mathf.Max(py0 + 1, Mathf.CeilToInt((cell.y - paintBoundsMin.y + 1) * paintScale));
        for (int y = Mathf.Max(0, py0); y < Mathf.Min(textureSize, py1); y++)
        {
            int row = y * textureSize;
            for (int x = Mathf.Max(0, px0); x < Mathf.Min(textureSize, px1); x++)
                pixels[row + x] = colour;
        }
    }

    /// <summary>Grows a cell range about its own centre until it spans at
    /// least want cells. Never shrinks, so a well-claimed floor keeps the
    /// tight framing it earned.</summary>
    private static void InflateToSpan(ref int lo, ref int hi, int want)
    {
        int span = hi - lo + 1;
        if (span >= want) return;
        int grow = want - span;
        lo -= grow / 2;
        hi += grow - grow / 2;
    }

    private bool OnThisFloor(float worldY)
        => floor != null && Mathf.Abs(worldY - floor.WorldOriginY) < 1000f;

    /// <summary>Has the fog lifted where this thing stands? Fails open, so a
    /// floor without a tile grid still draws its dots.</summary>
    private bool SeenHere(Vector3 worldPos)
        => floor == null || influence == null
        || floor.IsRevealed(influence.WorldToCell(worldPos));

    // -- per-frame overlays ------------------------------------------------------

    private void UpdateDots()
    {
        int used = 0;
        if (hasPaint && floor != null && floor.Entities != null)
        {
            // Fog hides the map as well as the eye. Until den populations shipped
            // nothing had ever STOOD on unrevealed ground -- wild chamber monsters
            // spawn on reveal -- so this filter had never been needed. Without it a
            // den paints its own location in red dots from the day its floor is
            // created, and canon 42 requires first contact to be the player's doing.
            // Adventurers are filtered too: pre-mined den tunnels are open ground the
            // pathfinder routes them down, and a dot over an occluded body is the
            // same giveaway.
            floor.Entities.FillAll(advBuf);
            for (int i = 0; i < advBuf.Count; i++)
                if (advBuf[i] != null && SeenHere(advBuf[i].transform.position))
                    used = PlaceDot(used, advBuf[i].transform.position, adventurerDot);

            floor.Entities.FillAll(monBuf);
            for (int i = 0; i < monBuf.Count; i++)
                if (monBuf[i] != null && SeenHere(monBuf[i].transform.position))
                    used = PlaceDot(used, monBuf[i].transform.position,
                                    monBuf[i].ServesDungeon ? monsterDot
                                    : monBuf[i].HostileToDungeon ? hostileMonsterDot
                                    : neutralMonsterDot);

            var core = DungeonCore.Instance;
            if (core != null && OnThisFloor(core.transform.position.y))
                used = PlaceDot(used, core.transform.position, coreDot);
        }
        for (int i = used; i < dotPool.Count; i++)
            if (dotPool[i].gameObject.activeSelf) dotPool[i].gameObject.SetActive(false);
    }

    private int PlaceDot(int index, Vector3 worldPos, Color colour)
    {
        if (dotLayer == null || !WorldToMap(worldPos, out Vector2 anchored)) return index;
        Image dot = index < dotPool.Count ? dotPool[index] : CreateDot();
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        dot.color = colour;
        var rt = (RectTransform)dot.transform;
        rt.sizeDelta = new Vector2(dotSize, dotSize);
        rt.anchoredPosition = anchored;
        return index + 1;
    }

    private Image CreateDot()
    {
        var go = new GameObject("MinimapDot", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(dotLayer, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        dotPool.Add(img);
        return img;
    }

    private void UpdateViewRect()
    {
        if (viewRect == null) return;
        if (!hasPaint || mainCam == null) { if (viewRect.gameObject.activeSelf) viewRect.gameObject.SetActive(false); return; }

        Vector2 mapSize = mapRect.rect.size;
        // View half-extents, in map-local units.
        float halfW = (mainCam.orthographicSize * mainCam.aspect / worldPerCell) * paintScale / textureSize * mapSize.x;
        float halfH = (mainCam.orthographicSize / worldPerCell) * paintScale / textureSize * mapSize.y;

        WorldToMap(mainCam.transform.position, out Vector2 c);   // view centre in map-local space

        // Clamp the box to the minimap so it never overflows - e.g. early game, when the camera
        // sees more than the whole claimed floor. It just fills the map instead of spilling past it.
        Vector2 half = mapSize * 0.5f;
        float minX = Mathf.Max(c.x - halfW, -half.x);
        float maxX = Mathf.Min(c.x + halfW, half.x);
        float minY = Mathf.Max(c.y - halfH, -half.y);
        float maxY = Mathf.Min(c.y + halfH, half.y);

        bool visible = maxX > minX && maxY > minY;
        if (viewRect.gameObject.activeSelf != visible) viewRect.gameObject.SetActive(visible);
        if (!visible) return;

        viewRect.sizeDelta = new Vector2(maxX - minX, maxY - minY);
        viewRect.anchoredPosition = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    // -- mapping + interaction ---------------------------------------------------

    private bool WorldToMap(Vector3 worldPos, out Vector2 anchored)
    {
        anchored = default;
        if (!hasPaint || mapRect == null) return false;
        float cx = (worldPos.x - worldMin.x) / worldPerCell;
        float cy = (worldPos.y - worldMin.y) / worldPerCell;
        float nx = cx * paintScale / textureSize;
        float ny = cy * paintScale / textureSize;
        Vector2 mapSize = mapRect.rect.size;
        anchored = new Vector2((nx - 0.5f) * mapSize.x, (ny - 0.5f) * mapSize.y);
        return true;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!hasPaint || influence == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapRect, e.position, e.pressEventCamera, out Vector2 local)) return;

        Vector2 mapSize = mapRect.rect.size;
        float nx = local.x / mapSize.x + 0.5f;
        float ny = local.y / mapSize.y + 0.5f;
        float cx = nx * textureSize / paintScale;
        float cy = ny * textureSize / paintScale;
        Vector3 world = worldMin + new Vector3(cx * worldPerCell, cy * worldPerCell, 0f);
        DungeonCameraController.Instance?.PanTo(world);
    }

    /// <summary>Collapse / expand the map body (wire to a button).</summary>
    public void Toggle()
    {
        if (body != null) body.SetActive(!body.activeSelf);
    }

    /// <summary>Finds the RawImage the map should paint on: a descendant named
    /// "MapImage" if one exists, otherwise null so the caller falls back to this
    /// object's own RawImage. Searches inactive children too, since Body starts
    /// collapsed while the research node is still locked.</summary>
    private static Transform FindMapSurface(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == "MapImage" && t.GetComponent<RawImage>() != null)
                return t;
        return null;
    }
}
