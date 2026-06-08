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
    public bool hasSave;
    public int worldSeed;

    public DungeonCoreSaveData coreData;
    public DayNightSaveData dayNightData;

    public int coreFloorIndex;
    public int pendingCoreRelocationFloor = -1;
    public List<int> visitedFloors = new();

    public List<FloorSaveData> floors = new();

    public bool hasEntrance;
    public SerializableVector3Int entranceCell;
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
    public int direction;
}