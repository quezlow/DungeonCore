using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for all furniture types.
/// Referenced by DungeonSaveController for name-based restore on load.
/// Create via: right-click → Create → Dungeon → Furniture Definition Registry
/// </summary>
[CreateAssetMenu(fileName = "FurnitureDefinitionRegistry",
                 menuName  = "Dungeon/Furniture Definition Registry")]
public class FurnitureDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<FurnitureDefinition> definitions = new();
    public IReadOnlyList<FurnitureDefinition> All => definitions;

    public FurnitureDefinition GetByName(string furnitureName)
    {
        if (string.IsNullOrEmpty(furnitureName)) return null;
        var def = definitions.Find(d => d.furnitureName == furnitureName);
        if (def == null)
            Debug.LogWarning($"[FurnitureDefinitionRegistry] '{furnitureName}' not found.");
        return def;
    }
}
