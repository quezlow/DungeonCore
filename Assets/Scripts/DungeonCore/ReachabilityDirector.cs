using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Two answers to one question: "can anything actually walk from my heart to
/// there?"
///
/// THE WATCHDOG -- after every dig it floods from the core using the SAME
/// passability rule the pathfinder uses, spreading across stair pairs (matched
/// by the same cell on the linked floor, the traversal rule), then checks
/// whether the entrance is in floor 0's set. If it is not, no adventurer can
/// reach the core and no monster can reach the mouth: the dungeon has stopped
/// being a dungeon, wherever the core lives. That raises a Threat alert and a
/// wisp line, and a second line when the road is restored.
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

    [Header("Flow")]
    [Tooltip("Seconds between rings as the wash spreads. Smaller is faster.")]
    [SerializeField] private float flowRingInterval = 0.035f;

    // Per-floor cache of everything reachable from that floor's core cell.
    private readonly Dictionary<FloorRoot, HashSet<Vector3Int>> reachable = new();
    private readonly Dictionary<FloorRoot, Tilemap> overlays = new();
    // What is currently on each overlay, and the rings still spreading onto it.
    private readonly Dictionary<FloorRoot, HashSet<Vector3Int>> painted = new();
    private readonly Dictionary<FloorRoot, Queue<List<Vector3Int>>> flow = new();
    private float nextFlowStep;
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

        if (overlayVisible) { AdvanceFlow(); Pulse(); }
    }

    /// <summary>Ask for a rebuild shortly; repeated calls collapse into one.</summary>
    public void MarkDirty() => recomputeAt = Time.unscaledTime + recomputeDebounce;

    /// <summary>False ONLY when we have actually checked and found that nothing
    /// can walk from the mouth to the heart. Unknown, unchecked, or no director
    /// present all read true, so a missing watchdog can never stall the game.
    /// Waves gate on this: sending raiders at a dungeon they cannot enter just
    /// piles them up outside.</summary>
    public static bool RouteToCoreOpen =>
        Instance == null || !Instance.severedKnown || !Instance.severed;

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

    // Reused per recompute: stairs on the floor being expanded, and the
    // worklist of (floor, seed) hops still to flood.
    private static readonly List<DungeonStairs> _stairBuf = new();
    private readonly List<(FloorRoot floor, Vector3 seed)> _floodWork = new();

    private void Recompute()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;

        // Multi-floor flood through the stair web. Seed the CORE floor at the
        // heart itself (the core can be relocated, so the object's position is
        // authoritative over the terrain centre), then let reachability spread
        // across stair pairs -- matched by the SAME CELL on the linked floor,
        // exactly the traversal rule in HandleStairTraversal -- until no floor
        // gains new ground. Floors the web never reaches keep an empty set:
        // nothing there is joined to the heart, the overlay stays dark, and
        // that is the honest answer. Before this the per-floor floods seeded
        // from each floor's own terrain centre, which made every non-core
        // floor's overlay meaningless and left CheckSevered blind the moment
        // the core moved below floor 0.
        foreach (var floor in fm.AllFloors)
        {
            if (floor == null) continue;
            if (reachable.TryGetValue(floor, out var old)) old.Clear();
            else reachable[floor] = new HashSet<Vector3Int>();
        }

        var coreFloor = fm.GetFloor(fm.CoreFloorIndex);
        if (coreFloor == null || coreFloor.TileInfluence == null) return;
        Vector3 coreWorld = DungeonCore.Instance != null
            ? DungeonCore.Instance.transform.position
            : coreFloor.TileInfluence.CellToWorld(
                coreFloor.Terrain != null ? coreFloor.Terrain.CoreCell : Vector3Int.zero);

        _floodWork.Clear();
        _floodWork.Add((coreFloor, coreWorld));

        while (_floodWork.Count > 0)
        {
            var (floor, seed) = _floodWork[_floodWork.Count - 1];
            _floodWork.RemoveAt(_floodWork.Count - 1);
            if (floor == null || floor.TileInfluence == null) continue;

            var set = reachable[floor];
            Vector3Int seedCell = floor.TileInfluence.WorldToCell(seed);
            // A seed already inside this floor's set adds nothing -- this is
            // also the termination guard: every hop is pushed at most once
            // per side of a stair per recompute.
            if (set.Contains(seedCell)) continue;

            set.UnionWith(DungeonPathfinder.ReachableCells(floor, seed));

            if (floor.Entities == null) continue;
            floor.Entities.FillAll(_stairBuf);
            for (int i = 0; i < _stairBuf.Count; i++)
            {
                var stair = _stairBuf[i];
                if (stair == null || !set.Contains(stair.OccupiedCell)) continue;
                var dest = fm.GetFloor(stair.LinkedFloorIndex);
                if (dest == null || dest.TileInfluence == null || dest.Entities == null) continue;
                // The hop only exists where the matching stair actually
                // stands on the linked floor -- half a pair carries nobody.
                if (dest.Entities.GetAtCell<DungeonStairs>(stair.OccupiedCell) == null) continue;
                if (reachable.TryGetValue(dest, out var destSet) && destSet.Contains(stair.OccupiedCell)) continue;
                _floodWork.Add((dest, dest.TileInfluence.CellToWorld(stair.OccupiedCell)));
            }
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

        // Say nothing until the player has actually FOUND the mouth. Before that
        // the halls are meant to be unjoined -- warning about it is both wrong and
        // a spoiler, announcing there is an entrance out there to dig toward.
        // Mirrors the spawners' gate: a floor with no seeded cave counts as
        // discovered, so hand-built floors are never permanently muted.
        var features = fm.GetFloor(0) != null ? fm.GetFloor(0).FeatureGenerator : null;
        if (features != null && features.EntranceCave != null && !features.IsEntranceDiscovered)
        {
            // Keep the state unlatched so the first real check after discovery
            // reports honestly instead of being swallowed as "no change".
            severedKnown = false;
            return;
        }

        // The entrance always stands on floor 0; the flood above carries the
        // heart's reach up the stair web, so the surface set is meaningful
        // wherever the core lives. An empty surface set is only a VERDICT
        // once the core floor's own flood has ground under it -- a heart
        // with no reachable cells is a bootstrap state, not a severance.
        FloorRoot surface = fm.GetFloor(0);
        if (surface == null) return;
        if (!reachable.TryGetValue(surface, out var set)) return;
        var coreFloorRoot = fm.GetFloor(fm.CoreFloorIndex);
        if (coreFloorRoot == null
            || !reachable.TryGetValue(coreFloorRoot, out var coreSet)
            || coreSet.Count == 0) return;

        bool nowSevered = !set.Contains(entrance.OccupiedCell);
        if (severedKnown && nowSevered == severed) return;

        severed = nowSevered;
        severedKnown = true;

        if (severed)
        {
            AlertsLog.Instance?.AddAlert(
                "No road runs from the mouth to the core -- the halls are broken.",
                entrance.transform.position, 0, AlertCategory.Threat,
                AlertSeverity.Critical);
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

    /// <summary>A cell is only washed once the player has actually SEEN it.
    /// Natural floor (chamber floors, river banks) is mined from generation, so
    /// without this the wash would trace routes through undiscovered ground and
    /// give away rivers and chambers the player has not met yet. Reachability
    /// itself still counts those cells -- only the painting waits.</summary>
    // Forwards to FloorRoot, which now owns the test. The rule described above
    // turned out to be wanted by the minimap and the world-space UI too, and
    // three copies of a fog check is three chances to disagree.
    private static bool IsRevealed(FloorRoot floor, Vector3Int cell)
        => floor.IsRevealed(cell);

    /// <summary>Diffs the wash against what is reachable-and-seen, then lets the
    /// difference SPREAD rather than snap: new ground is queued into rings by
    /// step distance from the ground already lit, so a fresh tunnel fills from
    /// its mouth outward.</summary>
    private void RepaintAll()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;

        foreach (var floor in fm.AllFloors)
        {
            if (floor == null) continue;
            var map = OverlayFor(floor);
            if (map == null) continue;

            if (!painted.TryGetValue(floor, out var lit))
            {
                lit = new HashSet<Vector3Int>();
                painted[floor] = lit;
            }

            var target = new HashSet<Vector3Int>();
            if (reachable.TryGetValue(floor, out var set))
                foreach (var cell in set)
                    if (IsRevealed(floor, cell)) target.Add(cell);

            // Ground that fell out of reach (or back under fog) goes at once --
            // a warning should never be delayed by an animation.
            var stale = new List<Vector3Int>();
            foreach (var cell in lit)
                if (!target.Contains(cell)) stale.Add(cell);
            foreach (var cell in stale) { map.SetTile(cell, null); lit.Remove(cell); }

            var fresh = new HashSet<Vector3Int>();
            foreach (var cell in target)
                if (!lit.Contains(cell)) fresh.Add(cell);

            flow[floor] = BuildRings(floor, lit, fresh);
        }
    }

    /// <summary>Orders new cells into rings by step distance from the lit edge --
    /// or from the core when nothing is lit yet, so entering Mine mode blooms
    /// outward from the heart.</summary>
    private Queue<List<Vector3Int>> BuildRings(
        FloorRoot floor, HashSet<Vector3Int> lit, HashSet<Vector3Int> fresh)
    {
        var rings = new Queue<List<Vector3Int>>();
        if (fresh.Count == 0) return rings;

        var frontier = new List<Vector3Int>();
        if (lit.Count > 0)
        {
            foreach (var cell in lit) frontier.Add(cell);
        }
        else if (floor.Terrain != null)
        {
            frontier.Add(floor.Terrain.CoreCell);
        }

        var pending = new HashSet<Vector3Int>(fresh);
        var seeds = new List<Vector3Int>(frontier);

        while (pending.Count > 0)
        {
            var ring = new List<Vector3Int>();
            foreach (var seed in seeds)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3Int n = seed + (i == 0 ? Vector3Int.up
                                        : i == 1 ? Vector3Int.down
                                        : i == 2 ? Vector3Int.left : Vector3Int.right);
                    if (pending.Remove(n)) ring.Add(n);
                }
            }
            if (ring.Count == 0)
            {
                // Detached pocket (a chamber joined by a route already lit in a
                // single step): flush the remainder so nothing is left unpainted.
                var rest = new List<Vector3Int>(pending);
                pending.Clear();
                rings.Enqueue(rest);
                break;
            }
            rings.Enqueue(ring);
            seeds = ring;
        }
        return rings;
    }

    /// <summary>Paints one ring per interval across every floor.</summary>
    private void AdvanceFlow()
    {
        if (Time.unscaledTime < nextFlowStep) return;
        nextFlowStep = Time.unscaledTime + Mathf.Max(0.005f, flowRingInterval);

        foreach (var kv in flow)
        {
            var floor = kv.Key;
            var queue = kv.Value;
            if (queue == null || queue.Count == 0) continue;
            if (!overlays.TryGetValue(floor, out var map) || map == null) continue;
            if (!painted.TryGetValue(floor, out var lit)) continue;

            var ring = queue.Dequeue();
            foreach (var cell in ring)
            {
                map.SetTile(cell, overlayTile);
                lit.Add(cell);
            }
        }
    }

    private void ClearAll()
    {
        foreach (var kv in overlays)
            if (kv.Value != null) kv.Value.ClearAllTiles();
        painted.Clear();
        flow.Clear();
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
