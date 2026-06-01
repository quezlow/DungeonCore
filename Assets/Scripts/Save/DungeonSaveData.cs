using System;
using System.Collections.Generic;

/// <summary>
/// Top-level container serialised to DungeonSaveData.json by DungeonSaveController.
///
/// Individual system data classes (DungeonCoreSaveData, TileInfluenceSaveData) live
/// alongside their owning scripts. Dungeon-object data classes are defined below
/// since they have no other logical home.
/// </summary>
[Serializable]
public class DungeonSaveData
{
    /// <summary>
    /// Guard flag — false on a default-constructed instance, so DungeonSaveController
    /// skips the load pass cleanly if the file somehow exists but is empty/corrupt.
    /// </summary>
    public bool hasSave;

    // ── System snapshots ──────────────────────────────────────────
    public DungeonCoreSaveData coreData;
    public TileInfluenceSaveData tileData;

    // ── Placed objects ────────────────────────────────────────────
    public bool hasEntrance;
    public SerializableVector3Int entranceCell;

    public DayNightSaveData dayNightData;
    public List<MonsterSpawnerSaveData> spawners = new();
    public List<DungeonChestSaveData> chests = new();
    public List<FurnitureSaveData> furniture = new();
    public List<RoomAnchorSaveData> roomAnchors = new();
    public List<TrapSaveData> traps = new();

}

// ── Per-object save data ──────────────────────────────────────────────────────

[Serializable]
public class MonsterSpawnerSaveData
{
    /// <summary>
    /// Looked up against MonsterDefinitionRegistry.GetByName() on load.
    /// Matches MonsterDefinition.monsterName exactly.
    /// </summary>
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
    public string furnitureName; // matched via FurnitureDefinitionRegistry
    public SerializableVector3Int cell;
}

[Serializable]
public class RoomAnchorSaveData
{
    public SerializableVector3Int cell;
    public string assignedRoomName; // matched via RoomDefinitionRegistry
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


