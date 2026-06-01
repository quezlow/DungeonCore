using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for all chest types.
/// Referenced by DungeonSaveController for name-based restore on load,
/// and by ChestSelectionUI for the placement picker.
/// </summary>
[CreateAssetMenu(fileName = "ChestDefinitionRegistry",
                 menuName  = "Dungeon/Chest Definition Registry")]
public class ChestDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<ChestDefinition> definitions = new();
    public IReadOnlyList<ChestDefinition> All => definitions;

    public ChestDefinition GetByName(string chestName)
    {
        if (string.IsNullOrEmpty(chestName)) return null;
        var def = definitions.Find(d => d.chestName == chestName);
        if (def == null)
            Debug.LogWarning($"[ChestDefinitionRegistry] '{chestName}' not found.");
        return def;
    }
}
