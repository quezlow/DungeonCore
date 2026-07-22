using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Two answers to one question: "can anything actually walk from my heart to
/// there?"
///
/// THE WATCHDOG -- after every dig it floods from the core using the SAME
/// passability rule the pathfinder uses, then checks whether the entrance is in
/// that set. If it is not, no adventurer can reach the core and no monster can
/// reach the mouth: the dungeon has stopped being a dungeon. That raises a Threat
/// alert and a wisp line, and a second line when the road is restored.
///
/// THE MINE-MODE OVERLAY -- while the player is digging, every cell joined to the
/// core is washed in a slow pulse. Absence is the warning: a tunnel that stays
/// dark is not connected, whatever it looks like. Crucially this is computed from
/// the PATHFINDER'S rule, not from "is this cell mined" -- mined-but-unwalkable
/// cells are precisely the failure the player cannot otherwise see.
///
/// SCENE SETUP: add this component to the dungeon GameController. It needs no
/// references and builds its own overlay tilemap per floor at runtime.
/// </summary>
public class ReachabilityDirector : MonoBehaviour
{
    public static ReachabilityDirector Instance { get; private set; }

    [Header("Overlay")]
    [Tooltip("Tint washed over every cell joined to the core while in Mine mode.")]
    [SerializeField] private Color reachTint = new Color(0.42f, 0.85f, 0.62f, 1f);
    [Tooltip("Alpha floor and ceiling of the pulse.")]
    [SerializeField, Range(0f, 1f)] private float pulseMin = 0.10f;
    [SerializeField, Range(0f, 1f)] private float pulseMax = 0.26f;
    [Tooltip("Seconds for one full breath of the pulse.")]
    [SerializeField] private float pulsePeriod = 2.6f;

    [Header("Recompute")]
    [Tooltip("Digging fires many events at once; wait this long after the last one.")]
    [SerializeField] private float recomputeDebounce = 0.25f;

    // Per-floor cache of everything reachable from that floor's core cell.
    private readonly Dictionary<FloorRoot, HashSet<Vector3Int>> reachable = new();
    private readonly Dictionary<FloorRoot, Tilemap> overlays = new();
    private readonly HashSet<TileInfluenceManager> hooked = new();

    private Tile overlayTile;
    private float recomputeAt = -1f;
    private bool overlayVisible;
    private bool severed;
    private bool severedKnown;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var inf in hooked)
            if (inf != null) inf.OnTileMined -= HandleTileMined;
        hooked.Clear();
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
    }

    private void Start()
    {
        if (DungeonBuildController.Instance != null)
        {
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
            HandleModeChanged(DungeonBuildController.Instance.CurrentMode);
        }
        MarkDirty();
    }

    private void Update()
    {
        HookNewFloors();

        if (recomputeAt > 0f && Time.unscaledTime >= recomputeAt)
        {
            recomputeAt = -1f;
            Recompute();
        }

        if (overlayVisible) Pulse();
    }

    /// <summary>Ask for a rebuild shortly; repeated calls collapse into one.</summary>
    public void MarkDirty() => recomputeAt = Time.unscaledTime + recomputeDebounce;

    /// <summary>True when the cell is joined to its floor's core by a walkable route.</summary>
    public bool IsJoinedToCore(FloorRoot floor, Vector3Int cell)
        => floor != null && reachable.TryGetValue(floor, out var set) && set.Contains(cell);

    // -- Wiring ------------------------------------------------------------

    private void HookNewFloors()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;
        foreach (var floor in fm.AllFloors)
        {
            if (floor == null || floor.TileInfluence == null) continue;
            if (hooked.Add(floor.TileInfluence))
            {
                floor.TileInfluence.OnTileMined += HandleTileMined;
                MarkDirty();
            }
        }
    }

    private void HandleTileMined(Vector3Int _) => MarkDirty();

    private void HandleModeChanged(BuildMode mode)
    {
        bool show = mode == BuildMode.Mine;
        if (show == overlayVisible) return;
        overlayVisible = show;
        if (show) { Recompute(); RepaintAll(); }
        else ClearAll();
    }

    // -- The flood ---------------------------------------------------------

    private void Recompute()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;

        foreach (var floor in fm.AllFloors)
        {
            if (floor == null || floor.TileInfluence == null || floor.Terrain == null) continue;
            Vector3 coreWorld = floor.TileInfluence.CellToWorld(floor.Terrain.CoreCell);
            reachable[floor] = DungeonPathfinder.ReachableCells(floor, coreWorld);
        }

        CheckSevered();
        if (overlayVisible) RepaintAll();
    }

    // -- The watchdog ------------------------------------------------------

    private void CheckSevered()
    {
        var entrance = DungeonEntrance.Instance;
        var fm = FloorManager.Instance;
        if (entrance == null || fm == null) return;

        // The entrance always stands on floor 0; the core may live deeper, in
        // which case the stairs carry the route and this check is not meaningful.
        FloorRoot surface = fm.GetFloor(0);
        if (surface == null || fm.CoreFloorIndex != 0) return;
        if (!reachable.TryGetValue(surface, out var set) || set.Count == 0) return;

        bool nowSevered = !set.Contains(entrance.OccupiedCell);
        if (severedKnown && nowSevered == severed) return;

        severed = nowSevered;
        severedKnown = true;

        if (severed)
        {
            AlertsLog.Instance?.AddAlert(
                "No road runs from the mouth to the core -- the halls are broken.",
                entrance.transform.position, 0, AlertCategory.Threat);
            WispCompanion.Instance?.SpeakLine(
                "Nothing can walk from the door to your heart. Whatever we carved, " +
                "it does not join. Look for the gap.");
        }
        else
        {
            WispCompanion.Instance?.SpeakLine("The road holds again. They can find us now.");
        }
    }

    // -- The overlay -------------------------------------------------------

    private void Pulse()
    {
        float t = Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.05f, pulsePeriod * 0.5f), 1f);
        float a = Mathf.Lerp(pulseMin, pulseMax, Mathf.SmoothStep(0f, 1f, t));
        var c = new Color(reachTint.r, reachTint.g, reachTint.b, a);
        foreach (var kv in overlays)
            if (kv.Value != null) kv.Value.color = c;
    }

    private void RepaintAll()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;
        foreach (var floor in fm.AllFloors)
        {
            if (floor == null) continue;
            var map = OverlayFor(floor);
            if (map == null) continue;
            map.ClearAllTiles();
            if (!reachable.TryGetValue(floor, out var set)) continue;
            foreach (var cell in set) map.SetTile(cell, overlayTile);
        }
    }

    private void ClearAll()
    {
        foreach (var kv in overlays)
            if (kv.Value != null) kv.Value.ClearAllTiles();
    }

    /// <summary>The overlay tilemap for a floor, built on first use so no floor
    /// prefab needs hand-wiring.</summary>
    private Tilemap OverlayFor(FloorRoot floor)
    {
        if (overlays.TryGetValue(floor, out var existing) && existing != null) return existing;

        var sibling = floor.HighlightTilemap;
        if (sibling == null) return null;

        var go = new GameObject("ReachabilityOverlay");
        go.transform.SetParent(sibling.transform.parent, false);

        var map = go.AddComponent<Tilemap>();
        var renderer = go.AddComponent<TilemapRenderer>();
        var siblingRenderer = sibling.GetComponent<TilemapRenderer>();
        if (siblingRenderer != null)
        {
            renderer.sortingLayerID = siblingRenderer.sortingLayerID;
            renderer.sortingOrder = siblingRenderer.sortingOrder - 1;
        }

        overlays[floor] = map;
        EnsureTile(sibling);
        return map;
    }

    /// <summary>One white cell-sized tile, generated so no art asset is needed.</summary>
    private void EnsureTile(Tilemap reference)
    {
        if (overlayTile != null) return;

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.filterMode = FilterMode.Point;
        tex.Apply();

        // Pixels-per-unit chosen so the single pixel covers exactly one cell.
        float cellWidth = 1f;
        var grid = reference.layoutGrid;
        if (grid != null && grid.cellSize.x > 0.001f) cellWidth = grid.cellSize.x;

        var sprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f), 1f / cellWidth);

        overlayTile = ScriptableObject.CreateInstance<Tile>();
        overlayTile.sprite = sprite;
        overlayTile.color = Color.white;
    }
}
