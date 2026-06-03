using System;
using System.Collections.Generic;

/// <summary>
/// Top-level container serialised to DungeonSaveData.json by DungeonSaveController.
///
/// DAY 27 MULTI-FLOOR
///   Per-object save data now lives PER FLOOR (FloorSaveData below).
///   Each per-object record additionally records its floor index for
///   defensive cross-checks during load.
/// </summary>
[Serializable]
public class DungeonSaveData
{
    public bool hasSave;

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