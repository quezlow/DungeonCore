using System;
using System.Collections.Generic;

/// <summary>
/// DAY 30 — Procedural terrain feature data, persisted per floor.
///
/// DAY 31 PART 2 — aliveWildCount + cleared on ChamberData.
/// DAY 31 PART 3F — wildMonsters list on ChamberData captures per-monster
///   position, HP, and definition name. aliveWildCount remains as the
///   sentinel (-1 = never spawned). On load, WildMonsterController prefers
///   wildMonsters when present and falls back to a coarse re-roll using
///   aliveWildCount otherwise.
/// </summary>
[Serializable]
public class FloorFeatureSaveData
{
    public List<RiverData> rivers = new();
    public List<ChamberData> chambers = new();
    public List<int> revealedRiverIds = new();
    public List<int> revealedChamberIds = new();

    public CoreCavernData coreCavern;
}

[Serializable]
public class RiverData
{
    public int id;
    public int width;
    public List<SerializableVector3Int> polyline = new();
    // Water-channel cells: fordable, un-mineable, painted with the water tile.
    public List<SerializableVector3Int> cells = new();
    // Dry floor banks eroded from the river's outer shell (walkable natural floor).
    // Empty on pre-bank saves, which keep behaving as all-water rivers.
    public List<SerializableVector3Int> bankCells = new();
}

[Serializable]
public class ChamberData
{
    public int id;
    public SerializableVector3Int centerCell;
    public List<SerializableVector3Int> cells = new();

    // DAY 31 PART 2 — sentinel + cleared flag.
    public int aliveWildCount = -1;
    public bool cleared = false;

    // DAY 31 PART 3F — per-monster snapshot.
    public List<WildMonsterSaveData> wildMonsters = new();
}

[Serializable]
public class WildMonsterSaveData
{
    public string monsterName;
    public SerializableVector3Int cell;
    public float currentHP;
}

public struct FeatureRef
{
    public FeatureType type;
    public int featureId;
}

public enum FeatureType { None, River, Chamber, CoreCavern, RiverBank }

[Serializable]
public class CoreCavernData
{
    public SerializableVector3Int centerCell;
    public List<SerializableVector3Int> cells = new();
    public List<TunnelData> tunnels = new();
}

[Serializable]
public class TunnelData
{
    /// <summary>Outward bearing from the cavern, in degrees (debug / tuning only).</summary>
    public float angleDegrees;
    public List<SerializableVector3Int> cells = new();
}