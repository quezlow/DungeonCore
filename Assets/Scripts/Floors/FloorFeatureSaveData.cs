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
/// </summary>
[Serializable]
public class FloorFeatureSaveData
{
    public List<RiverData> rivers = new();
    public List<ChamberData> chambers = new();
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
    /// <summary>Chamber floor cells, post-river-overwrite. Wild monster spawning (Day 31) uses these.</summary>
    public List<SerializableVector3Int> cells = new();
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