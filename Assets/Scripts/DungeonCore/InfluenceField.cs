using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor influence field — the growth engine of the claim system.
///
/// Maintains a cost-distance field D over the floor: a Dijkstra flood from the
/// floor's core cell where stepping into a cell costs that cell's terrain
/// resistance (FloorRoot.GetClaimCostMultiplier). Expansion is 8-directional:
/// diagonals cost sqrt(2) x the entered cell and never cut a corner past an
/// impassable cell, so the field radiates round instead of in Manhattan
/// diamonds. Claimed-set connectivity is untouched — the ring still grows
/// 4-connected; only the reach geometry rounds. Rim bedrock (minus the
/// entrance carve, which costs 1x as already-excavated passage) and uncleared
/// chambers are impassable, so the field respects the rim and the chamber
/// gates by construction. D depends on terrain only — claimed status never
/// shortens it — so the free-growth zone stays anchored to the core rather
/// than compounding off claimed tendrils.
///
/// Free growth (the cap model):
///   - Reach R = baseReach + (dungeonLevel - 1) * reachPerLevel.
///   - Ambient creep claims the CHEAPEST claimable-ring cell with D within R
///     at a slow interval, for free. When everything within R is claimed,
///     growth idles until R rises.
///   - On a confirmed level-up the creep sprints for surgeDuration (the
///     surge), so the ring visibly blooms out to the new reach.
///   - Creep never claims river cells (crossing water stays a deliberate,
///     player-paid act) and never claims into uncleared chambers. Manual
///     claiming is unrestricted by reach — mana is its only governor.
///
/// Breach suppression (edge recede):
///   - On the first core breach, effective reach collapses to
///     R * (1 - suppressionFraction) and claimed territory beyond it is
///     unclaimed in one batch on every floor (mined tunnels persist).
///     Effective reach then recovers linearly as the instability timer runs
///     down, and the creep — at surge rate by default — regrows the edge
///     across the window. All state derives from DungeonCore (IsUnstable /
///     InstabilityTimer / InstabilityDuration), so nothing new is saved and
///     mid-instability loads resume at the correct suppression point.
///
/// Recompute triggers: first ready frame, and OnChamberCleared (a cleared
/// chamber's cells drop from impassable to normal resistance). Reach changes
/// never require a recompute — D is terrain-only.
///
/// Setup: lives on the FloorRoot GameObject (the Floor 1 scene object and the
/// Floor Template prefab) and is assigned to FloorRoot's Influence Field slot.
/// </summary>
[DisallowMultipleComponent]
public class InfluenceField : MonoBehaviour
{
    [Header("Reach")]
    [Tooltip("Free-growth reach (resistance-weighted cost; ~cells over dirt) at dungeon level 1.")]
    [SerializeField, Min(0f)] private float baseReach = 8f;
    [Tooltip("Extra reach granted per dungeon level above 1.")]
    [SerializeField, Min(0f)] private float reachPerLevel = 3f;
    [Tooltip("Dijkstra bound margin past max-level reach; cells beyond it are never computed.")]
    [SerializeField, Min(0f)] private float fieldDepthMargin = 10f;

    [Header("Creep")]
    [Tooltip("Seconds between free ambient claims.")]
    [SerializeField, Min(0.05f)] private float ambientInterval = 3f;
    [Tooltip("Seconds between free claims while surging (level-up bloom / breach recovery).")]
    [SerializeField, Min(0.02f)] private float surgeInterval = 0.2f;
    [Tooltip("How long the level-up surge lasts, in seconds.")]
    [SerializeField, Min(0f)] private float surgeDuration = 30f;

    [Header("Breach Suppression")]
    [Tooltip("Fraction of reach lost at the moment of a first breach. Recovers across the instability window.")]
    [SerializeField, Range(0f, 1f)] private float suppressionFraction = 0.5f;
    [Tooltip("Creep runs at surge rate while the core is unstable, so the receded edge visibly regrows.")]
    [SerializeField] private bool recoveryUsesSurgeRate = true;

    // ── State ─────────────────────────────────────────────────────

    private readonly Dictionary<Vector3Int, float> costDistance = new Dictionary<Vector3Int, float>();
    private static readonly List<Vector3Int> recedeScratch = new List<Vector3Int>();

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private bool fieldDirty = true;
    private bool subscribedCore;
    private bool subscribedChambers;
    private float surgeUntil = -1f;
    private Coroutine creepRoutine;

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    private static readonly Vector3Int[] Diagonals =
    {
        new Vector3Int(1, 1, 0), new Vector3Int(1, -1, 0),
        new Vector3Int(-1, 1, 0), new Vector3Int(-1, -1, 0),
    };

    private const float DiagonalFactor = 1.41421356f;

    /// <summary>Fires after the cost-distance field is (re)computed.</summary>
    public event Action OnFieldRecomputed;

    // ── Public Reads ──────────────────────────────────────────────

    /// <summary>Level-based reach, before any breach suppression.</summary>
    public float MaxReach
    {
        get
        {
            var core = DungeonCore.Instance;
            return ReachAtLevel(core != null ? core.DungeonLevel : 1);
        }
    }

    /// <summary>Reach after breach suppression. Recovers linearly across the
    /// instability window; equals MaxReach while the core is stable.</summary>
    public float EffectiveReach
    {
        get
        {
            float r = MaxReach;
            var core = DungeonCore.Instance;
            if (core != null && core.IsUnstable && core.InstabilityDuration > 0f)
            {
                float recovery = 1f - Mathf.Clamp01(core.InstabilityTimer / core.InstabilityDuration);
                return Mathf.Lerp(r * (1f - suppressionFraction), r, recovery);
            }
            return r;
        }
    }

    public float ReachAtLevel(int level)
        => baseReach + Mathf.Max(0, level - 1) * reachPerLevel;

    /// <summary>Cost-distance from the floor's core cell, if reachable at all.</summary>
    public bool TryGetCost(Vector3Int cell, out float cost)
        => costDistance.TryGetValue(cell, out cost);

    /// <summary>True when free growth can reach this cell at the current effective reach.</summary>
    public bool IsWithinFreeGrowth(Vector3Int cell)
        => costDistance.TryGetValue(cell, out float d) && d <= EffectiveReach;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[InfluenceField] No FloorRoot in parents — disabling.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        fieldDirty = true;
        if (creepRoutine == null) creepRoutine = StartCoroutine(CreepRoutine());
    }

    private void OnDisable()
    {
        if (creepRoutine != null)
        {
            StopCoroutine(creepRoutine);
            creepRoutine = null;
        }
        Unsubscribe();
    }

    private void LateUpdate()
    {
        ResolveAndSubscribe();
        if (fieldDirty && DependenciesReady())
        {
            RecomputeField();
            fieldDirty = false;
        }
    }

    private void ResolveAndSubscribe()
    {
        if (influence == null && floor != null) influence = floor.TileInfluence;

        if (!subscribedCore && DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
            DungeonCore.Instance.OnFirstBreach += HandleFirstBreach;
            subscribedCore = true;
        }

        if (!subscribedChambers && floor != null && floor.FeatureGenerator != null)
        {
            floor.FeatureGenerator.OnChamberCleared += HandleChamberCleared;
            subscribedChambers = true;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedCore && DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
            DungeonCore.Instance.OnFirstBreach -= HandleFirstBreach;
        }
        subscribedCore = false;

        if (subscribedChambers && floor != null && floor.FeatureGenerator != null)
            floor.FeatureGenerator.OnChamberCleared -= HandleChamberCleared;
        subscribedChambers = false;
    }

    private bool DependenciesReady()
        => floor != null
        && influence != null
        && floor.Terrain != null
        && floor.FeatureGenerator != null
        && floor.FeatureGenerator.HasGenerated;

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleLevelUp(int newLevel)
    {
        surgeUntil = Time.time + surgeDuration;
    }

    private void HandleChamberCleared(int chamberId)
    {
        fieldDirty = true;
    }

    private void HandleFirstBreach()
    {
        // Mid-instability loads re-fire OnFirstBreach to restore UI state. The
        // recede already ran before that save — running it again would eat any
        // territory the player manually reclaimed during the window.
        if (DungeonSaveController.IsLoading) return;

        if (fieldDirty && DependenciesReady())
        {
            RecomputeField();
            fieldDirty = false;
        }
        RecedeBeyondEffectiveReach();
    }

    // ── Breach Recede ─────────────────────────────────────────────

    private void RecedeBeyondEffectiveReach()
    {
        if (influence == null || floor == null || floor.Terrain == null) return;
        if (costDistance.Count == 0) return; // field never computed — never nuke blind

        float reach = EffectiveReach;
        Vector3Int coreCell = floor.Terrain.CoreCell;

        recedeScratch.Clear();
        foreach (Vector3Int cell in influence.ClaimedTiles)
        {
            if (cell == coreCell) continue;
            if (costDistance.TryGetValue(cell, out float d) && d <= reach) continue;
            recedeScratch.Add(cell);
        }

        if (recedeScratch.Count == 0) return;
        influence.UnclaimTilesBatch(recedeScratch);
        Debug.Log($"[InfluenceField] Floor {floor.FloorIndex}: breach recede unclaimed {recedeScratch.Count} cell(s) beyond reach {reach:0.0}.");
    }

    // ── Creep ─────────────────────────────────────────────────────

    private IEnumerator CreepRoutine()
    {
        while (true)
        {
            float interval = CurrentInterval();
            float elapsed = 0f;
            while (elapsed < interval)
            {
                if (!PauseController.IsGamePaused)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (fieldDirty || !DependenciesReady()) continue;
            TryCreepOnce();
        }
    }

    private float CurrentInterval()
    {
        bool surging = Time.time < surgeUntil;
        var core = DungeonCore.Instance;
        if (recoveryUsesSurgeRate && core != null && core.IsUnstable)
            surging = true;
        return surging ? surgeInterval : ambientInterval;
    }

    private void TryCreepOnce()
    {
        float reach = EffectiveReach;
        var features = floor.FeatureGenerator;

        Vector3Int best = default;
        float bestCost = float.MaxValue;
        bool found = false;

        // Selection only — no mutation while enumerating the live ring set.
        foreach (Vector3Int cell in influence.ClaimableTiles)
        {
            if (!costDistance.TryGetValue(cell, out float d)) continue;
            if (d > reach || d >= bestCost) continue;
            if (features != null)
            {
                if (features.IsRiver(cell)) continue;                  // water stays player-decision territory
                if (features.IsCellInUnclearedChamber(cell)) continue; // chamber gate
            }
            best = cell;
            bestCost = d;
            found = true;
        }

        if (found) influence.ClaimTile(best); // free — the cap model's unaided growth
    }

    // ── Field Compute ─────────────────────────────────────────────

    private void RecomputeField()
    {
        costDistance.Clear();

        var terrain = floor.Terrain;
        Vector3Int core = terrain.CoreCell;
        float bound = ReachAtLevel(LevelTierUtil.MaxFlatLevel) + fieldDepthMargin;

        var open = new MinHeap();
        costDistance[core] = 0f;
        open.Push(0f, core);

        while (open.TryPop(out float d, out Vector3Int cell))
        {
            if (d > costDistance[cell]) continue; // stale heap entry

            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int n = cell + dir;
                float step = GetStepCost(n);
                if (float.IsPositiveInfinity(step)) continue;

                float nd = d + step;
                if (nd >= bound) continue; // depth bound — beyond any conceivable reach
                if (costDistance.TryGetValue(n, out float old) && old <= nd) continue;

                costDistance[n] = nd;
                open.Push(nd, n);
            }

            foreach (Vector3Int dir in Diagonals)
            {
                Vector3Int n = cell + dir;
                float step = GetStepCost(n);
                if (float.IsPositiveInfinity(step)) continue;

                // No corner cutting: both orthogonal intermediates must be
                // passable, so the field can't slip through bedrock or gated
                // chamber corners. (A river corner can be skirted — harmless:
                // the ring itself still grows 4-connected, so the creep can
                // never actually cross without a claimed route over the water.)
                if (float.IsPositiveInfinity(GetStepCost(cell + new Vector3Int(dir.x, 0, 0)))) continue;
                if (float.IsPositiveInfinity(GetStepCost(cell + new Vector3Int(0, dir.y, 0)))) continue;

                float nd = d + step * DiagonalFactor;
                if (nd >= bound) continue;
                if (costDistance.TryGetValue(n, out float old2) && old2 <= nd) continue;

                costDistance[n] = nd;
                open.Push(nd, n);
            }
        }

        OnFieldRecomputed?.Invoke();
    }

    /// <summary>Cost to step INTO a cell. Positive infinity marks impassable:
    /// out of bounds, rim bedrock outside the entrance carve, or a chamber
    /// whose claim gate is still closed. Public so InfluenceChannel paths with
    /// exactly the same costs the field uses.</summary>
    public float GetStepCost(Vector3Int cell)
    {
        var terrain = floor.Terrain;
        if (terrain == null || !terrain.IsWithinBounds(cell)) return float.PositiveInfinity;

        var features = floor.FeatureGenerator;

        // The entrance carve is already-excavated passage through the rim —
        // it costs like cave floor regardless of the bedrock band beneath it.
        if (features != null && features.IsEntranceCave(cell)) return 1f;

        var typeMap = floor.TerrainTypeMap;
        if (typeMap != null && typeMap.IsBedrock(cell)) return float.PositiveInfinity;

        if (features != null && features.IsCellInUnclearedChamber(cell))
            return float.PositiveInfinity;

        return floor.GetClaimCostMultiplier(cell);
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
                int parent = (i - 1) / 2;
                if (items[parent].cost <= items[i].cost) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
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
                int smallest = i;
                if (l < items.Count && items[l].cost < items[smallest].cost) smallest = l;
                if (r < items.Count && items[r].cost < items[smallest].cost) smallest = r;
                if (smallest == i) break;
                (items[i], items[smallest]) = (items[smallest], items[i]);
                i = smallest;
            }
            return true;
        }
    }
}