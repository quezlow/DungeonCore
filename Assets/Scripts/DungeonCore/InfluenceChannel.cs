using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The push — the player's directed influence tool, and the manual counterpart
/// to InfluenceField's free growth. Where the field creeps outward on its own
/// within the reach cap, the push is inflation under the hand: hold and aim, and
/// your domain swells outward from the core toward the cursor.
///
/// The model is boundary pressure, not a path. Every cell on the claimable
/// frontier that lies in the corridor between the core and the cursor accrues
/// pressure toward being claimed — strongest on the core-to-cursor axis,
/// tapering to nothing at the corridor edge, and gated at the cursor distance so
/// the swell reaches the cursor and stops rather than overshooting. Off-corridor
/// boundary stays perfectly still, so a lopsided domain never sprays claims from
/// every facing edge. Terrain shapes it: a cell's pressure threshold rises with
/// its resistance (softened by terrainDeflection), so the swell bulges further
/// through soft dirt and deflects around hard rock. Rim bedrock and uncleared
/// chambers are impassable (InfluenceField.GetStepCost returns infinity); rivers
/// are finite, so the swell CAN cross water at its full cost — the deliberate,
/// player-paid act this tool exists for.
///
/// Once the swell touches the cursor, a second pass fills the concave notches
/// where the reaching bulge meets the round core, pulling the core edge out to
/// smooth the whole thing into one bulge. That smoothing is bounded — it settles
/// once the shape is smooth rather than growing forever.
///
/// Feel rules:
///   - Mana per claimed cell = manaPerCell x the cell's terrain resistance, so
///     shoving through rock costs proportionally more. Empty mana stalls the
///     push without bursting; it resumes as regen catches up.
///   - Releasing lets un-filled pressure fade; claimed territory stays, exactly
///     like every other claim, until a breach recede takes it back.
///   - Reach is irrelevant here: mana is the only governor, so the push can
///     swell past the free-growth reach cap.
///
/// The preview line the old channel drew is retired — the swelling boundary is
/// the feedback. The LineRenderer stays (required component) but is disabled.
///
/// Setup: lives on the same GameObject as DungeonBuildController. Everything is
/// code-configured; no scene wiring beyond adding the component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class InfluenceChannel : MonoBehaviour
{
    public static InfluenceChannel Instance { get; private set; }

    [Header("Inflation")]
    [Tooltip("Mana spent per claimed cell, multiplied by that cell's terrain resistance.")]
    [SerializeField, Min(0f)] private float manaPerCell = 1f;
    [Tooltip("How fast the swelling boundary accrues pressure toward the cursor. Low is a slow, deliberate push.")]
    [SerializeField, Min(0.1f)] private float pushStrength = 5f;
    [Tooltip("Half-width in cells of the corridor between core and cursor that the swell fills. Wider fills a broader region.")]
    [SerializeField, Min(1f)] private float corridorHalfWidth = 6f;
    [Tooltip("How softly terrain resistance deflects the swell. 1 = full deflection; lower rounds around rock more gently.")]
    [SerializeField, Range(0.1f, 1f)] private float terrainDeflection = 0.6f;
    [Tooltip("Per-second rate at which un-filled pressure fades once you release.")]
    [SerializeField, Min(0f)] private float pressureFadeRate = 4f;

    [Header("Smoothing")]
    [Tooltip("Once the swell reaches the cursor, boundary cells more surrounded than this fraction (0..1) fill in, pulling the core edge out to smooth the shape.")]
    [SerializeField, Range(0.3f, 0.95f)] private float smoothingThreshold = 0.55f;
    [Tooltip("Neighbourhood radius (cells) used to measure how concave a boundary cell is.")]
    [SerializeField, Range(1, 6)] private int smoothingRadius = 3;

    // ── State ──────────────────────────────

    private LineRenderer line;

    // Accumulated pressure per claimable frontier cell. Only cells inside the
    // active corridor (or a concave notch during smoothing) keep an entry;
    // everything else is cleared each tick, so this stays bounded to the push.
    private readonly Dictionary<Vector3Int, float> pressure = new Dictionary<Vector3Int, float>();

    // Reused each tick: claim only AFTER the frontier scan (never mutate the ring
    // set mid-enumeration), and fade without mutating the dictionary mid-loop.
    private readonly List<Vector3Int> claimBuffer = new List<Vector3Int>();
    private readonly List<Vector3Int> fadeKeys = new List<Vector3Int>();

    // Set by Tick each frame the push runs, so Inflate can ask about dwarven
    // ground without taking a FloorRoot parameter through a private method
    // whose signature four other members already agree on.
    private TerrainTypeMap holdingsMap;

    private int lastFloorIndex = int.MinValue;
    private bool subscribedModeChanges;

    private static readonly Vector3Int[] CellDirs =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Lifecycle ────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InfluenceChannel] Duplicate instance — destroying this one.");
            Destroy(this);
            return;
        }
        Instance = this;

        // The preview line is retired; the swelling boundary is the feedback.
        line = GetComponent<LineRenderer>();
        if (line != null)
        {
            line.enabled = false;
            line.positionCount = 0;
        }
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

    /// <summary>Drops all pending pressure. Claimed territory is unaffected.</summary>
    public void CancelChannel()
    {
        pressure.Clear();
    }

    // ── Per-frame driver (called by DungeonBuildController in Push mode) ──

    public void Tick(FloorRoot floor, Vector3Int? hoverCell, bool held)
    {
        if (floor == null || floor.TileInfluence == null
            || floor.InfluenceField == null || floor.Terrain == null)
        {
            CancelChannel();
            return;
        }

        int floorIndex = floor.FloorIndex;
        if (floorIndex != lastFloorIndex)
        {
            pressure.Clear();
            lastFloorIndex = floorIndex;
        }

        if (!held || hoverCell == null)
        {
            FadePressure();
            return;
        }

        holdingsMap = floor.TerrainTypeMap;
        Inflate(floor.TileInfluence, floor.InfluenceField, floor.Terrain.CoreCell, hoverCell.Value);
    }

    // ── Inflation ────────────────────────

    private void Inflate(TileInfluenceManager influence, InfluenceField field, Vector3Int coreCell, Vector3Int cursor)
    {
        float ax = cursor.x - coreCell.x;
        float ay = cursor.y - coreCell.y;
        float reach = Mathf.Sqrt(ax * ax + ay * ay);
        if (reach < 0.001f)   // cursor on the core — nothing to push toward
        {
            FadePressure();
            return;
        }
        float dirx = ax / reach;
        float diry = ay / reach;

        bool smoothing = ReachedCursor(influence, cursor);
        float dt = Time.deltaTime;

        // Hoisted out of the frontier loop: a floor with no dwarven ground
        // then costs one null test per push rather than one probe per
        // claimable cell per frame.
        var holdings = holdingsMap != null && holdingsMap.HasHoldings ? holdingsMap : null;

        // Pass 1: accrue pressure on frontier cells in the corridor (and, once
        // reached, in concave notches near it). Selection only — the ring set is
        // never mutated while it is being enumerated.
        claimBuffer.Clear();
        foreach (Vector3Int cell in influence.ClaimableTiles)
        {
            float w = WeightFor(cell, coreCell, dirx, diry, reach, influence, smoothing);
            if (w <= 0f)
            {
                pressure.Remove(cell);
                continue;
            }

            float cost = ClaimThreshold(field, cell);
            if (float.IsPositiveInfinity(cost))   // bedrock / gated chamber — impassable
            {
                pressure.Remove(cell);
                continue;
            }

            float p = (pressure.TryGetValue(cell, out float cur) ? cur : 0f) + w * pushStrength * dt;
            if (p >= cost) claimBuffer.Add(cell);
            else pressure[cell] = p;

            // RUNG 2 of the warning ladder. Canon wants the wisp to speak
            // BEFORE the first claim completes, and pressure is the only
            // state in the system that exists while the decision is still
            // reversible: the swell leans on a frontier cell for a while
            // before it takes it, and leaning on dwarven ground is the
            // intent signal. Nothing new is stored -- the ledger returns on
            // a bool once it has spoken.
            if (holdings != null && holdings.IsHoldingsCell(cell))
                DwarvenClaimLedger.NotifyPressureOnHoldings();
        }

        // Pass 2: claim the ready cells, paying mana per cell scaled by terrain.
        // Empty mana stalls without bursting — the ready cells simply wait.
        var core = DungeonCore.Instance;
        for (int i = 0; i < claimBuffer.Count; i++)
        {
            Vector3Int cell = claimBuffer[i];
            if (influence.IsTileClaimed(cell)) { pressure.Remove(cell); continue; }
            if (!influence.IsTileClaimable(cell)) { pressure.Remove(cell); continue; }

            float mana = manaPerCell * field.GetStepCost(cell);
            if (core == null || !core.SpendMana(mana)) break;   // mana empty — stall

            influence.ClaimTile(cell);
            pressure.Remove(cell);
        }
    }

    /// <summary>Pressure rate for a frontier cell: highest on the core-to-cursor
    /// axis, tapering to zero at the corridor edge, gated at the cursor distance
    /// so the swell stops there. Behind the core is zero. During smoothing, a
    /// concave notch near the corridor contributes instead, pulling the core edge
    /// out to round the shape.</summary>
    private float WeightFor(Vector3Int cell, Vector3Int coreCell, float dirx, float diry, float reach, TileInfluenceManager influence, bool smoothing)
    {
        float tx = cell.x - coreCell.x;
        float ty = cell.y - coreCell.y;
        float proj = tx * dirx + ty * diry;
        if (proj < 0f) return 0f;                         // behind the core — never grows backward

        float perpx = tx - proj * dirx;
        float perpy = ty - proj * diry;
        float perp = Mathf.Sqrt(perpx * perpx + perpy * perpy);

        float w = 0f;
        if (proj <= reach && perp <= corridorHalfWidth)
            w = (1f - perp / corridorHalfWidth) * (0.6f + 0.4f * (proj / reach));

        if (smoothing && perp <= corridorHalfWidth + 2f * smoothingRadius)
        {
            float c = Concavity(cell, influence);
            if (c > smoothingThreshold)
            {
                float ws = (c - smoothingThreshold) / (1f - smoothingThreshold);
                if (ws > w) w = ws;
            }
        }
        return w;
    }

    /// <summary>Fraction of the disc of radius smoothingRadius around a cell that
    /// is already claimed. High in a concave notch, low on a convex bump — so a
    /// threshold on it fills notches and leaves convex edges alone.</summary>
    private float Concavity(Vector3Int cell, TileInfluenceManager influence)
    {
        int claimed = 0, total = 0;
        int r = smoothingRadius;
        int r2 = r * r;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                total++;
                if (influence.IsTileClaimed(new Vector3Int(cell.x + dx, cell.y + dy, cell.z))) claimed++;
            }
        }
        return total > 0 ? (float)claimed / total : 0f;
    }

    /// <summary>True once the growing domain touches the cursor cell — the moment
    /// the push stops extending and starts smoothing.</summary>
    private bool ReachedCursor(TileInfluenceManager influence, Vector3Int cursor)
    {
        if (influence.IsTileClaimed(cursor)) return true;
        for (int i = 0; i < CellDirs.Length; i++)
            if (influence.IsTileClaimed(cursor + CellDirs[i])) return true;
        return false;
    }

    /// <summary>A cell's pressure threshold: its terrain resistance softened by
    /// terrainDeflection. Infinity for impassable cells (bedrock, gated chambers).
    /// Mana cost uses the raw resistance separately, so deflection shapes the
    /// swell without discounting the mana of pushing through rock.</summary>
    private float ClaimThreshold(InfluenceField field, Vector3Int cell)
    {
        float m = field.GetStepCost(cell);
        if (float.IsPositiveInfinity(m)) return float.PositiveInfinity;
        return Mathf.Pow(m, terrainDeflection);
    }

    private void FadePressure()
    {
        if (pressure.Count == 0) return;
        float dec = pressureFadeRate * Time.deltaTime;
        fadeKeys.Clear();
        fadeKeys.AddRange(pressure.Keys);
        for (int i = 0; i < fadeKeys.Count; i++)
        {
            Vector3Int k = fadeKeys[i];
            float v = pressure[k] - dec;
            if (v <= 0f) pressure.Remove(k);
            else pressure[k] = v;
        }
    }
}