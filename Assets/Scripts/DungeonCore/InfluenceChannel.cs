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
/// The push sweeps channelWidth cells wide: each spine step claims its centre
/// first, then the lateral flanks, every cell paying its own time and mana —
/// a 3-wide dirt corridor takes 3x the duration at the same drain rate.
/// Flanks never take rivers or gated chambers; only the spine crosses water.
/// A started rank survives replans, so a moving cursor can never starve the
/// flanks into a 1-wide shaft.
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
    [Tooltip("Cells wide the push claims. Odd values only — even values snap up.")]
    [SerializeField, Range(1, 7)] private int channelWidth = 3;
    [Tooltip("Reach (in cells) at which the pseudopod base reaches full width. Shorter reaches " +
             "stay thinner overall; a long reach commits a full-width base. Lower fattens sooner.")]
    [SerializeField, Min(1)] private int fullWidthReachCells = 6;
    [Tooltip("Fraction of the arm that holds full width before easing to the rounded tip. Higher " +
             "gives a fatter arm with a shorter taper; lower gives a longer graceful taper.")]
    [SerializeField, Range(0f, 0.95f)] private float tipStartFraction = 0.5f;

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

    // The full claim order woven from the spine: centre-first ranks with their
    // lateral flanks. bool = spine cell (a stolen spine replans; flanks skip).
    private readonly List<(Vector3Int cell, bool spine)> claimSeq = new List<(Vector3Int, bool)>();
    private readonly HashSet<Vector3Int> seqSeen = new HashSet<Vector3Int>();

    // The rank being claimed right now: one spine cell plus its flanks. Replans
    // rebuild claimSeq freely, but never touch this — the fix for cursor jitter
    // discarding pending flanks and leaving 1-wide shafts.
    private readonly List<(Vector3Int cell, bool spine)> currentRank = new List<(Vector3Int, bool)>();

    /// <summary>channelWidth snapped odd (even snaps up) so flanks stay symmetric.</summary>
    private int WidthSnapped => Mathf.Max(1, channelWidth | 1);

    // Per-spine-cell half-width of the pseudopod (index 0 = anchored base,
    // last = reaching tip), rebuilt each replan by ComputeHalfProfile.
    private readonly List<int> halfProfile = new List<int>();

    // Spine cells claimed since the last replan. The path trims from the front
    // as it claims, so this realigns the width profile to the shrinking path.
    private int spineClaimedSinceReplan;

    // Preview-line width-curve cache (rebuilt only when the path shape changes).
    private int lineCurveN = -1;
    private int lineCurveTrim = -1;
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (line != null) line.widthMultiplier = lineWidth;
    }
#endif

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode != BuildMode.Push) CancelChannel();
    }

    /// <summary>Hides the preview and drops the current path. Partial progress on
    /// the current target survives — it self-invalidates if the target changes.</summary>
    public void CancelChannel()
    {
        path.Clear();
        claimSeq.Clear();
        currentRank.Clear();
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
        var features = floor.FeatureGenerator;
        int floorIndex = floor.FloorIndex;

        if (hoverCell == null)
        {
            path.Clear();
            claimSeq.Clear();
            HideLine();
            return;
        }

        Vector3Int hover = hoverCell.Value;
        bool hoverClaimed = influence.IsTileClaimed(hover);
        int pending = currentRank.Count + claimSeq.Count;

        // Done (or moot) with nothing left to sweep — stand down.
        if (hoverClaimed && pending == 0)
        {
            path.Clear();
            HideLine();
            return;
        }

        // Replan only while the destination is unclaimed; once the hover cell is
        // ours, the remaining rank(s) just drain out. Replans rebuild claimSeq —
        // never currentRank, so a started rank always completes.
        if (!hoverClaimed)
        {
            bool planValid =
                pending > 0
                && floorIndex == lastFloorIndex
                && hover == lastHoverCell;

            if (!planValid && !TryComputePath(influence, field, features, hover, floorIndex))
            {
                path.Clear();
                claimSeq.Clear();
                if (currentRank.Count == 0)
                {
                    HideLine();
                    return;
                }
            }
        }

        if (path.Count > 0) DrawLine(influence);
        else HideLine();

        if (!held) return;

        // Pull the next rank when the current one is spent, and pop anything the
        // creep already claimed for free.
        while (true)
        {
            if (currentRank.Count == 0)
            {
                RefillRank();
                if (currentRank.Count == 0) break;
            }
            if (!influence.IsTileClaimed(currentRank[0].cell)) break;
            PopRankFront();
        }

        if (currentRank.Count == 0)
        {
            progressSeconds = 0f;
            if (path.Count == 0) HideLine();
            return;
        }

        // Progress carries only while the front cell (and floor) hold.
        Vector3Int target = currentRank[0].cell;
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
        while (safety < 8)
        {
            if (currentRank.Count == 0)
            {
                RefillRank();
                if (currentRank.Count == 0) break;
            }

            (Vector3Int cellToClaim, bool isSpine) = currentRank[0];

            if (influence.IsTileClaimed(cellToClaim))
            {
                PopRankFront();   // free — someone else got it
                continue;
            }

            float need = secondsPerCell * field.GetStepCost(cellToClaim);
            if (progressSeconds < need) break;

            if (!influence.IsTileClaimable(cellToClaim))
            {
                if (isSpine)
                {
                    // The spine's footing shifted under us — replan next tick.
                    path.Clear();
                    claimSeq.Clear();
                    currentRank.Clear();
                    HideLine();
                    return;
                }
                PopRankFront();   // a flank we can't have right now — skip it
                continue;
            }

            influence.ClaimTile(cellToClaim);
            progressSeconds -= need;   // remainder carries into the next cell
            PopRankFront();
            safety++;

            if (currentRank.Count > 0)
            {
                progressCell = currentRank[0].cell;
                progressFloorIndex = floorIndex;
            }
        }

        if (currentRank.Count == 0 && claimSeq.Count == 0)
        {
            // Swept through — next tick stands down.
            progressSeconds = 0f;
            HideLine();
        }
    }

    private void PopRankFront()
    {
        (Vector3Int cell, bool spine) = currentRank[0];
        currentRank.RemoveAt(0);
        if (spine && path.Count > 0 && path[0] == cell)
        {
            path.RemoveAt(0);
            spineClaimedSinceReplan++;
        }
    }

    /// <summary>Moves the next rank — one spine cell plus its trailing flanks —
    /// from claimSeq into currentRank. currentRank is immune to replans: cursor
    /// jitter rebuilds the plan, but a started rank always finishes, so the
    /// flanks can never be starved into a 1-wide shaft.</summary>
    private void RefillRank()
    {
        if (claimSeq.Count == 0) return;
        currentRank.Add(claimSeq[0]);
        claimSeq.RemoveAt(0);
        while (claimSeq.Count > 0 && !claimSeq[0].spine)
        {
            currentRank.Add(claimSeq[0]);
            claimSeq.RemoveAt(0);
        }
    }



    // ── Path search ───────────────────────────────────────────────

    /// <summary>Dijkstra from the hover cell outward through UNCLAIMED cells
    /// (cost = InfluenceField.GetStepCost of the entered cell) until the flood
    /// touches claimed territory. The parent chain from that contact point back
    /// to the hover cell is the claim order.</summary>
    private bool TryComputePath(TileInfluenceManager influence, InfluenceField field, TerrainFeatureGenerator features, Vector3Int hover, int floorIndex)
    {
        lastHoverCell = hover;
        lastFloorIndex = floorIndex;
        path.Clear();
        spineClaimedSinceReplan = 0;

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
                    BuildClaimSequence(influence, field, features);
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

    /// <summary>Expands the spine path into the full claim order: each spine cell,
    /// centre first, then its lateral flanks out to WidthSnapped. Every cell pays
    /// its own time and mana; flanks never take rivers or gated chambers — width
    /// is convenience, not a way to buy water by accident.</summary>
    /// <summary>Expands the spine path into the full claim order: each spine cell
    /// centre-first, then its lateral flanks out to that cell's profile width.
    /// The width tapers from an anchored base to a rounded tip (see
    /// ComputeHalfProfile). Every cell pays its own time and mana; flanks never
    /// take rivers or gated chambers.</summary>
    private void BuildClaimSequence(TileInfluenceManager influence, InfluenceField field, TerrainFeatureGenerator features)
    {
        claimSeq.Clear();
        seqSeen.Clear();

        ComputeHalfProfile(path.Count);

        Vector3Int prev = pathRootClaimedCell;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3Int center = path[i];
            if (seqSeen.Add(center)) claimSeq.Add((center, true));

            int half = halfProfile[i];
            if (half > 0)
            {
                Vector3Int dir = center - prev;
                var perp = new Vector3Int(-dir.y, dir.x, 0);
                for (int k = 1; k <= half; k++)
                {
                    TryAddFlank(center + perp * k, influence, field, features);
                    TryAddFlank(center - perp * k, influence, field, features);
                }
            }
            prev = center;
        }
    }

    /// <summary>Builds the pseudopod's per-spine-cell half-width profile: an
    /// anchored base whose girth grows with reach, easing to a rounded nub at the
    /// tip. Index 0 is the base (adjacent to claimed rock); the last index is the
    /// reaching tip. A width-1 push (no flanks possible) yields an all-zero
    /// profile, i.e. the classic single-file push. Result lives in halfProfile.</summary>
    private void ComputeHalfProfile(int len)
    {
        halfProfile.Clear();
        if (len <= 0) return;

        int maxHalf = (WidthSnapped - 1) / 2;
        if (maxHalf <= 0 || len == 1)
        {
            for (int i = 0; i < len; i++) halfProfile.Add(0);
            return;
        }

        // Base girth grows with reach: a short poke stays thin, a long reach
        // commits a full-width base.
        float reachT = Mathf.Clamp01((float)len / Mathf.Max(1, fullWidthReachCells));
        int baseHalf = Mathf.RoundToInt(maxHalf * reachT);
        if (baseHalf <= 0)
        {
            for (int i = 0; i < len; i++) halfProfile.Add(0);
            return;
        }

        // Eased taper: hold the base girth through tipStartFraction, then
        // smoothstep down toward the tip.
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / (len - 1);
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tipStartFraction, 1f, t));
            halfProfile.Add(Mathf.RoundToInt(baseHalf * (1f - fall)));
        }

        // Rounded nub: force a gradual, monotone taper (drop at most one cell per
        // step) so the tip is a soft cap, never a sudden chop or a lone spike.
        for (int i = 1; i < len; i++)
        {
            if (halfProfile[i] > halfProfile[i - 1]) halfProfile[i] = halfProfile[i - 1];
            if (halfProfile[i] < halfProfile[i - 1] - 1) halfProfile[i] = halfProfile[i - 1] - 1;
        }
    }

    private void TryAddFlank(Vector3Int cell, TileInfluenceManager influence, InfluenceField field, TerrainFeatureGenerator features)
    {
        if (!seqSeen.Add(cell)) return;
        if (influence.IsTileClaimed(cell)) return;
        if (float.IsPositiveInfinity(field.GetStepCost(cell))) return;   // bounds, rim, gated chamber
        if (features != null && features.IsRiver(cell)) return;          // water stays a spine decision
        claimSeq.Add((cell, false));
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

        UpdateLineWidthCurve();
    }

    // Tapers the preview line to match the pseudopod: fat at the base end
    // (param 0), rounded to the tip (param 1). Rebuilt only when the visible path
    // length or the front-trim changes, to avoid per-frame curve allocation.
    private void UpdateLineWidthCurve()
    {
        int n = path.Count;
        if (n == lineCurveN && spineClaimedSinceReplan == lineCurveTrim) return;
        lineCurveN = n;
        lineCurveTrim = spineClaimedSinceReplan;

        if (halfProfile.Count == 0 || n <= 0)
        {
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            return;
        }

        int total = n + 1;
        var keys = new Keyframe[total];
        for (int q = 0; q < total; q++)
        {
            int profIdx = Mathf.Clamp(spineClaimedSinceReplan + Mathf.Max(q - 1, 0), 0, halfProfile.Count - 1);
            float factor = 2f * halfProfile[profIdx] + 1f;
            float tparam = total > 1 ? (float)q / (total - 1) : 0f;
            keys[q] = new Keyframe(tparam, factor);
        }
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        line.widthCurve = curve;
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