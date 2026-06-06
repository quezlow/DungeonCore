using System;
using System.Collections.Generic;

/// <summary>
/// DAY 30 — Procedural terrain feature data, persisted per floor.
///
/// Serializable container for rivers and chambers generated on a single floor.
/// Cell lookup dictionary is built at runtime by TerrainFeatureGenerator and
/// is NOT serialized.
///
/// Rivers and chambers are stored as per-feature records (polyline / centre /
/// cell list). On load, TerrainFeatureGenerator rebuilds a flat
/// Dictionary&lt;Vector3Int, FeatureRef&gt; for fast cell lookups.
///
/// River cells that overlap chamber cells are removed from the chamber's
/// cells list at generation time (rivers overwrite). The serialized state
/// reflects the post-overwrite truth.
///
/// DAY 31 PART 1 — Reveal state
///   Per-feature reveal state is persisted in the revealedRiverIds and
///   revealedChamberIds lists. A feature is "revealed" the first time any
///   of its cells becomes claimable.
///
/// DAY 31 PART 2 — Chamber wild-monster state
///   ChamberData gains aliveWildCount (number of wild monsters still alive)
///   and cleared (true once the gate is cleared). aliveWildCount = -1 means
///   "never spawned" — used so the controller can distinguish a brand-new
///   reveal from a fully-cleared chamber.
/// </summary>
[Serializable]
public class FloorFeatureSaveData
{
    public List<RiverData> rivers = new();
    public List<ChamberData> chambers = new();

    // DAY 31 — Per-feature reveal state.
    public List<int> revealedRiverIds = new();
    public List<int> revealedChamberIds = new();
}

[Serializable]
public class RiverData
{
    public int id;
    public int width;
    /// <summary>Meander control points. Bresenham between consecutive points yields the centreline.</summary>
    public List<SerializableVector3Int> polyline = new();
    /// <summary>All painted river cells, post-dilation, post-exclusion, post-radius-clip.</summary>
    public List<SerializableVector3Int> cells = new();
}

[Serializable]
public class ChamberData
{
    public int id;
    public SerializableVector3Int centerCell;
    /// <summary>Chamber floor cells, post-river-overwrite. Wild monster spawning uses these.</summary>
    public List<SerializableVector3Int> cells = new();

    // ── Wild monster state (DAY 31 PART 2) ────────────────────────

    /// <summary>
    /// Number of wild monsters currently alive in this chamber.
    /// -1 = never spawned (chamber not yet revealed, or never visited in this run).
    /// 0  = all dead (chamber cleared).
    /// >0 = that many alive.
    /// On load, the controller respawns aliveWildCount monsters in revealed-uncleared chambers.
    /// </summary>
    public int aliveWildCount = -1;

    /// <summary>
    /// True once all wild monsters have been killed and the claim gate is open.
    /// Once true, this stays true forever (claimed-then-unclaimed chambers do
    /// not respawn wild monsters in v1).
    /// </summary>
    public bool cleared = false;
}

/// <summary>Cell-level reference into the feature map. Not serialized.</summary>
public struct FeatureRef
{
    public FeatureType type;
    public int featureId;
}

public enum FeatureType
{
    None,
    River,
    Chamber
}