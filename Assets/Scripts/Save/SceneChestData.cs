// SceneChestData.cs
// Stores chest states for a single scene by name.
// Used by SaveData to track chest states across multiple scenes independently.
// Add this file alongside your other save data classes.

using System.Collections.Generic;

[System.Serializable]
public class SceneChestData
{
    public string sceneName;
    public List<ChestSaveData> chests = new List<ChestSaveData>();
}
