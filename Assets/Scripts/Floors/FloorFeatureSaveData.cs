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

    // Seeded surface entrance: tunnel through the bedrock rim + offshoot
    // chamberlets. Null on floors without one and on legacy saves.
    public EntranceCaveData entranceCave;
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
    // Water cells OUTSIDE the disc: the surface continuation across the forest.
    // Deliberately separate from `cells` and `bankCells`. IsBedrock returns false
    // outside the disc, so the MarkNaturalFloor bedrock guard would not filter
    // these; folding them into bankCells would register thousands of forest cells
    // as mined dungeon floor, inflating regenPerTile mana and painting the
    // minimap. Never pass this list to MarkNaturalFloor or the dungeon water
    // painter. Empty on saves written before the surface extension.
    public List<SerializableVector3Int> surfaceCells = new();
    // Cells where the surface river crosses the pilgrim road at a near-square
    // angle: the ford. Kept so the ford art (and, later, the slow-ford movement
    // rule) can be applied to exactly these cells.
    public List<SerializableVector3Int> fordCells = new();
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

public enum FeatureType { None, River, Chamber, CoreCavern, RiverBank, EntranceCave }

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

[Serializable]
public class EntranceCaveData
{
    /// <summary>Cell at the outer disc edge where the tunnel meets the surface.
    /// The DungeonEntrance object stands here; adventurers spawn here.</summary>
    public SerializableVector3Int mouthCell;

    /// <summary>Bearing from the floor centre to the mouth, in degrees.
    /// The apron reads this to lay the pilgrim road.</summary>
    public float angleDegrees;

    /// <summary>Every carved cell: tunnel + offshoot chamberlets, mouth included.</summary>
    public List<SerializableVector3Int> cells = new();

    /// <summary>True once the player's influence has touched the cave.
    /// Gates the discovery alert and the compass HUD.</summary>
    public bool discovered;

    /// <summary>Interior cell a few steps down the tunnel where the (invisible)
    /// entrance stands and parties spawn — deep enough that spawn scatter always
    /// lands on carved floor, never the apron.</summary>
    public SerializableVector3Int spawnCell;
    public bool hasSpawnCell;

    /// <summary>Day the seal broke. The first wave arrives the day after;
    /// -1 = not yet discovered.</summary>
    public int discoveredDay = -1;
}