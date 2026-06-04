using System;
using System.Collections.Generic;

/// <summary>
/// Top-level container serialised to DungeonSaveData.json by DungeonSaveController.
///
/// DAY 27 MULTI-FLOOR
///   Per-object save data now lives PER FLOOR (FloorSaveData below).
///   Each per-object record additionally records its floor index for
///   defensive cross-checks during load.
///
/// DAY 30 PROCEDURAL FEATURES
///   worldSeed: random 32-bit int generated once at new-game time. Persisted.
///   FloorSaveData now carries floorSeed (the per-floor RNG seed) and
///   featureData (rivers + chambers).
/// </summary>
[Serializable]
public class DungeonSaveData
{
    public bool hasSave;

    // ── World-wide procgen ────────────────────────────────────────
    /// <summary>Random seed for this run. Set once on new game and never changed.</summary>
    public int worldSeed;

    // ── Core systems ──────────────────────────────────────────────
    public DungeonCoreSaveData coreData;
    public DayNightSaveData dayNightData;

    // ── Multi-floor state ─────────────────────────────────────────
    public int coreFloorIndex;
    public int pendingCoreRelocationFloor = -1;
    public List<int> visitedFloors = new();

    // ── Per-floor data ────────────────────────────────────────────
    /// <summary>
    /// One entry per floor, including Floor 0 (the scene-placed Floor 1).
    /// Floor 0's entry will have centerCell unused (terrain seeded at scene start).
    /// </summary>
    public List<FloorSaveData> floors = new();

    // ── Entrance (singleton — always on Floor 0) ──────────────────
    public bool hasEntrance;
    public SerializableVector3Int entranceCell;
}

[Serializable]
public class FloorSaveData
{
    public int floorIndex;
    /// <summary>Cell used to seed terrain for Floor 1+. Unused for Floor 0.</summary>
    public SerializableVector3Int centerCell;

    /// <summary>DAY 30 — Per-floor RNG seed used for feature generation. Persisted.</summary>
    public int floorSeed;

    /// <summary>DAY 30 — Rivers + chambers generated for this floor.</summary>
    public FloorFeatureSaveData featureData;

    public TileInfluenceSaveData tileData;

    public List<MonsterSpawnerSaveData> spawners = new();
    public List<DungeonChestSaveData> chests = new();
    public List<FurnitureSaveData> furniture = new();
    public List<RoomAnchorSaveData> roomAnchors = new();
    public List<TrapSaveData> traps = new();
    public List<StairsSaveData> stairs = new();
}

// ── Per-object save data ──────────────────────────────────────────────────────

[Serializable]
public class MonsterSpawnerSaveData
{
    public string monsterName;
    public SerializableVector3Int cell;
}

[Serializable]
public class DungeonChestSaveData
{
    public SerializableVector3Int cell;
    public bool isOpened;
    public string chestName;
}

[Serializable]
public class FurnitureSaveData
{
    public string furnitureName;
    public SerializableVector3Int cell;
}

[Serializable]
public class RoomAnchorSaveData
{
    public SerializableVector3Int cell;
    public string assignedRoomName;
}

[Serializable]
public class TrapSaveData
{
    public string trapName;
    public SerializableVector3Int cell;
    public bool isFlagged;
    public string warningLabel;
    public bool hasLink;
    public SerializableVector3Int linkedCell;
}

[Serializable]
public class StairsSaveData
{
    public SerializableVector3Int cell;
    /// <summary>0 = Down, 1 = Up. Avoids serialising the enum directly.</summary>
    public int direction;
}