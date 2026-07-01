using System;
using System.Collections.Generic;

/// <summary>
/// Top-level container serialised to DungeonSaveData.json.
///
/// DAY 31 PART 3D — MonsterSpawnerSaveData gains order state (orderMode,
///   patrolWaypoints, patrolLoop, hasAttackTarget, attackTargetCell).
///   Old saves missing these fields default to Wander (backwards compatible).
/// </summary>
[Serializable]
public class DungeonSaveData
{
    /// <summary>
    /// Schema version of this save data. Bump when making a non-additive change
    /// (renamed/removed/semantically-changed field) and register a migration in
    /// SaveMigrationRegistry. Additive changes (new fields) do not require a
    /// version bump — JsonUtility's default-tolerant deserialization handles them.
    /// </summary>
    public const int CURRENT_VERSION = 3;

    /// <summary>
    /// Version stamped at save time. Compared against CURRENT_VERSION on load
    /// to decide whether migration is needed. Saves predating this field
    /// deserialize with saveVersion == 0, which is treated as an implicit v1.
    /// </summary>
    public int saveVersion = 0;

    public string dungeonName = "Unnamed Dungeon";

    public bool hasSave;
    public int worldSeed;

    public DungeonCoreSaveData coreData;
    public DayNightSaveData dayNightData;

    public int coreFloorIndex;
    public bool hasCoreCell;                          
    public SerializableVector3Int coreCell;
    public int pendingCoreRelocationFloor = -1;
    public List<int> visitedFloors = new();

    public List<FloorSaveData> floors = new();

    public bool hasEntrance;
    public SerializableVector3Int entranceCell;

    public bool hasCameraState;
    public SerializableVector3 cameraWorldPos;  // world space, includes floor Y offset
    public int cameraFloorIndex;
    public List<CameraBookmarkSaveData> cameraBookmarks = new();

    public List<AlertEntrySaveData> alertHistory = new();
    public int alertUnreadCount = 0;

    public RunStatsSaveData runStats;

    public List<TrackedParty> trackedParties = new();
}

[Serializable]
public class CameraBookmarkSaveData
{
    public bool set;
    public SerializableVector3 pos;
    public int floor;
    public float zoom;
}

[Serializable]
public class RunStatsSaveData
{
    // Cumulative (whole run)
    public List<ClassKillSaveData> killsByClass = new();
    public int monstersLost;
    public int biggestParty;
    public int goldEarned;
    public int maxDayReached = 1;

    // Current day (preserves a mid-day save/load's partial tally)
    public int currentDay = 1;
    public int partiesToday;
    public int slainToday;
    public int monstersLostToday;
    public int goldEarnedToday;
    public float notorietyAtDayStart;
}

[Serializable]
public class ClassKillSaveData
{
    public string className;
    public int count;
}

[Serializable]
public class FloorSaveData
{
    public int floorIndex;
    public SerializableVector3Int centerCell;
    public int floorSeed;
    public FloorFeatureSaveData featureData;
    public TileInfluenceSaveData tileData;
    public List<MonsterSpawnerSaveData> spawners = new();
    public List<DungeonChestSaveData> chests = new();
    public List<FurnitureSaveData> furniture = new();
    public List<RoomAnchorSaveData> roomAnchors = new();
    public List<TrapSaveData> traps = new();
    public List<StairsSaveData> stairs = new();
    public string floorName;   // player-set floor name (additive; null on old saves)
}

[Serializable]
public class MonsterSpawnerSaveData
{
    public string monsterName;
    public SerializableVector3Int cell;

    // DAY 31 PART 3D — Orders.
    public int orderMode = 0; // SpawnerOrderMode enum int
    public List<SerializableVector3Int> patrolWaypoints = new();
    public bool patrolLoop = true;
    public bool hasAttackTarget = false;
    public SerializableVector3Int attackTargetCell;
    public bool allowDefendCore = true;

    // DAY 31 — Alive monster state. Captured when this spawner has a live monster
    // at save time; consumed by the spawner's first SpawnMonster() on load.
    // PART 3 CLOSE-OUT — XP + isVeteran added so veteran progress survives reload.
    public bool hasAliveMonster;
    public float aliveMonsterHP;
    public SerializableVector3Int aliveMonsterCell;
    public int alivePatrolIndex;
    public float aliveMonsterXP;
    public bool aliveMonsterIsVeteran;
    public int aliveMonsterKills;
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
    public int tier = 1;
    public List<SerializableVector3Int> footprint = new();
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
    public int direction;
}