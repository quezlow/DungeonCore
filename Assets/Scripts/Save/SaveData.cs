using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SaveData
{
    public Vector3 playerPosition;
    public string mapBoundary; //Boundary name for the map
    public List<InventorySaveData> inventorySaveData;
    public List<InventorySaveData> hotbarSaveData;
    public List<SceneChestData> allSceneChests = new List<SceneChestData>();
    public List<QuestProgress> questProgressData;
    public List<string> handInQuestIDs;
}

[System.Serializable]
public class ChestSaveData
{
    public string chestID;
    public bool isOpened;
}