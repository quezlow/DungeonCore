using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The channel push — the player's directed influence tool, and the manual
/// counterpart to InfluenceField's free growth.
///
/// While DungeonBuildController is in Push mode it ticks this component every
/// frame with the hover cell and hold state. Holding LMB pours mana at a
/// constant rate (channelManaPerSecond) while influence creeps from the
/// claimed frontier toward the cursor along the CHEAPEST path — the same
/// terrain costs the field uses (InfluenceField.GetStepCost), so rim bedrock
/// and uncleared chambers are impassable, the entrance carve costs 1x, and
/// rivers cost their full resistance. Each cell takes
/// secondsPerCell * resistance to claim, so granite visibly slows the creep
/// under the hand and automatically costs proportionally more mana. Rivers
/// CAN be pushed across — crossing water is exactly the deliberate,
/// player-paid act this tool exists for.
///
/// Feel rules:
///   - Mana empty: the channel stalls (no progress, no drain) and resumes as
///     regen catches up — same convention as the dig queue.
///   - Releasing keeps partial progress on the current target cell; progress
///     resets only when the target changes.
///   - Reach is irrelevant here: mana is the only governor. Pushed territory
///     persists until a breach recede claims it back.
///
/// A LineRenderer (auto-added) previews the exact claim path from the claimed
/// frontier to the cursor whenever a path exists — hidden over claimed ground,
/// unreachable cells, or UI. The waver shader arriving in Session 3 is
/// cosmetic; this line is the precise truth.
///
/// Setup: lives on the same GameObject as DungeonBuildController. Everything
/// is code-configured; no scene wiring beyond adding the component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class InfluenceChannel : MonoBehaviour
{
    public static InfluenceChannel Instance { get; private set; }

    [Header("Channel")]
    [Tooltip("Seconds to claim a 1x-resistance (dirt) cell. Actual time = this x resistance.")]
    [SerializeField, Min(0.02f)] private float secondsPerCell = 0.35f;
    [Tooltip("Mana drained per second while the channel is actively progressing.")]
    [SerializeField, Min(0f)] private float channelManaPerSecond = 3f;

    [Header("Path Search")]
    [Tooltip("Give up when the cheapest route to the cursor exceeds this total cost.")]
    [SerializeField, Min(1f)] private float maxSearchCost = 400f;
    [Tooltip("Hard cap on nodes explored per search — safety valve on huge floors.")]
    [SerializeField, Min(100)] private int maxNodesPerSearch = 20000;

    [Header("Preview Line")]
    [SerializeField, Min(0.01f)] private float lineWidth = 0.12f;
    [SerializeField] private Color lineStartColor = new Color(0.784f, 0.565f, 0.165f, 0.85f); // gold
    [SerializeField] private Color lineEndColor = new Color(0.914f, 0.271f, 0.376f, 0.95f);   // accent
    [Tooltip("Sorting order for the preview line (fog sits at 50).")]
    [SerializeField] private int lineSortingOrder = 60;
    [Tooltip("Optional material override. Left null, a Sprites/Default material is created.")]
    [SerializeField] private Material lineMaterial;

    // ── State ─────────────────────────────────────────────────────

    private LineRenderer line;

    // Claim-ordered path: [frontier cell .. hover cell]. Element 0 is always a
    // member of the claimable ring while the path is valid.
    private readonly List<Vector3Int> path = new List<Vector3Int>();
    private Vector3Int pathRootClaimedCell;   // claimed anchor the line is rooted at
    private Vector3Int lastHoverCell;
    private int lastFloorIndex = int.MinValue;

    // Partial progress carries across release/resume while the target cell and
    // floor stay the same; it resets the moment the target changes.
    private float progressSeconds;
    private Vector3Int progressCell;
    private int progressFloorIndex = int.MinValue;

    private bool subscribedModeChanges;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InfluenceChannel] Duplicate instance — destroying this one.");
            Destroy(this);
            return;
        }
        Instance = this;

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.positionCount = 0;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.sortingOrder = lineSortingOrder;
        if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
        line.material = lineMaterial;

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(lineStartColor, 0f), new GradientColorKey(lineEndColor, 1f) },
            new[] { new GradientAlphaKey(lineStartColor.a, 0f), new GradientAlphaKey(lineEndColor.a, 1f) });
        line.colorGradient = grad;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (subscribedModeChanges && DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
    }

    private void LateUpdate()
    {
        if (!subscribedModeChanges && DungeonBuildController.Instance != null)
        {
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
            subscribedModeChanges = true;
        }
    }

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode != BuildMode.Push) CancelChannel();
    }

    /// <summary>Hides the preview and drops the current path. Partial progress on
    /// the current target survives — it self-invalidates if the target changes.</summary>
    public void CancelChannel()
    {
        path.Clear();
        HideLine();
    }

    // ── Per-frame driver (called by DungeonBuildController in Push mode) ──

    public void Tick(FloorRoot floor, Vector3Int? hoverCell, bool held)
    {
        if (floor == null || floor.TileInfluence == null || floor.InfluenceField == null)
        {
            CancelChannel();
            return;
        }

        var influence = floor.TileInfluence;
        var field = floor.InfluenceField;
        int floorIndex = floor.FloorIndex;

        if (hoverCell == null || influence.IsTileClaimed(hoverCell.Value))
        {
            // Nothing to push toward — over UI, off-grid, or already ours.
            path.Clear();
            HideLine();
            return;
        }

        Vector3Int hover = hoverCell.Value;

        bool pathValid =
            path.Count > 0
            && floorIndex == lastFloorIndex
            && hover == lastHoverCell
            && influence.IsTileClaimable(path[0]);

        if (!pathValid && !TryComputePath(influence, field, hover, floorIndex))
        {
            path.Clear();
            HideLine();
            return;
        }

        DrawLine(influence);

        if (!held || path.Count == 0) return;

        // Progress carries only while the target cell (and floor) are unchanged.
        Vector3Int target = path[0];
        if (target != progressCell || floorIndex != progressFloorIndex)
        {
            progressCell = target;
            progressFloorIndex = floorIndex;
            progressSeconds = 0f;
        }

        // Constant drain while progressing; empty mana stalls without bursting,
        // matching the dig queue's convention.
        var core = DungeonCore.Instance;
        if (core == null || !core.SpendMana(channelManaPerSecond * Time.deltaTime)) return;

        progressSeconds += Time.deltaTime;

        int safety = 0;
        while (path.Count > 0 && safety < 8)
        {
            target = path[0];
            float need = secondsPerCell * field.GetStepCost(target);
            if (progressSeconds < need) break;

            if (!influence.IsTileClaimable(target))
            {
                // Ring shifted under us (creep or another claim) — replan next tick.
                path.Clear();
                HideLine();
                return;
            }

            influence.ClaimTile(target);
            path.RemoveAt(0);
            progressSeconds -= need;   // remainder carries into the next cell
            safety++;

            if (path.Count > 0)
            {
                progressCell = path[0];
                progressFloorIndex = floorIndex;
            }
        }

        if (path.Count == 0)
        {
            // Arrived — the hover cell is ours now; next tick hides the line.
            progressSeconds = 0f;
            HideLine();
        }
    }

    // ── Path search ───────────────────────────────────────────────

    /// <summary>Dijkstra from the hover cell outward through UNCLAIMED cells
    /// (cost = InfluenceField.GetStepCost of the entered cell) until the flood
    /// touches claimed territory. The parent chain from that contact point back
    /// to the hover cell is the claim order.</summary>
    private bool TryComputePath(TileInfluenceManager influence, InfluenceField field, Vector3Int hover, int floorIndex)
    {
        lastHoverCell = hover;
        lastFloorIndex = floorIndex;
        path.Clear();

        float hoverCost = field.GetStepCost(hover);
        if (float.IsPositiveInfinity(hoverCost)) return false;

        var dist = new Dictionary<Vector3Int, float>();
        var parent = new Dictionary<Vector3Int, Vector3Int>();
        var open = new MinHeap();

        dist[hover] = hoverCost;
        open.Push(hoverCost, hover);

        int explored = 0;
        while (open.TryPop(out float d, out Vector3Int cell))
        {
            if (d > dist[cell]) continue;               // stale heap entry
            if (d > maxSearchCost) return false;         // cheapest frontier too far — give up
            if (++explored > maxNodesPerSearch) return false;

            foreach (Vector3Int dir in CellDirs)
            {
                Vector3Int n = cell + dir;

                if (influence.IsTileClaimed(n))
                {
                    // Contact — walk the parent chain back to the hover cell.
                    pathRootClaimedCell = n;
                    Vector3Int walk = cell;
                    path.Add(walk);
                    while (walk != hover)
                    {
                        walk = parent[walk];
                        path.Add(walk);
                    }
                    return true;
                }

                float step = field.GetStepCost(n);
                if (float.IsPositiveInfinity(step)) continue;

                float nd = d + step;
                if (nd > maxSearchCost) continue;
                if (dist.TryGetValue(n, out float old) && old <= nd) continue;

                dist[n] = nd;
                parent[n] = cell;
                open.Push(nd, n);
            }
        }
        return false;
    }

    private static readonly Vector3Int[] CellDirs =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Preview line ──────────────────────────────────────────────

    private void DrawLine(TileInfluenceManager influence)
    {
        line.positionCount = path.Count + 1;
        line.SetPosition(0, influence.CellToWorld(pathRootClaimedCell));
        for (int i = 0; i < path.Count; i++)
            line.SetPosition(i + 1, influence.CellToWorld(path[i]));
    }

    private void HideLine()
    {
        if (line != null) line.positionCount = 0;
    }

    // ── Min-Heap (no PriorityQueue in Unity's .NET profile) ───────

    private sealed class MinHeap
    {
        private readonly List<(float cost, Vector3Int cell)> items = new List<(float, Vector3Int)>();

        public void Push(float cost, Vector3Int cell)
        {
            items.Add((cost, cell));
            int i = items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (items[p].cost <= items[i].cost) break;
                (items[p], items[i]) = (items[i], items[p]);
                i = p;
            }
        }

        public bool TryPop(out float cost, out Vector3Int cell)
        {
            if (items.Count == 0)
            {
                cost = 0f;
                cell = default;
                return false;
            }

            (cost, cell) = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                int s = i;
                if (l < items.Count && items[l].cost < items[s].cost) s = l;
                if (r < items.Count && items[r].cost < items[s].cost) s = r;
                if (s == i) break;
                (items[i], items[s]) = (items[s], items[i]);
                i = s;
            }
            return true;
        }
    }
}