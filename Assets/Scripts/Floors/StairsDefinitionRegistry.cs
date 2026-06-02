using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry for stairs definitions. One entry expected for now (basic stairs).
/// Referenced by save/load and DungeonBuildController.
/// </summary>
[CreateAssetMenu(fileName = "StairsDefinitionRegistry",
                 menuName  = "Dungeon/Stairs Definition Registry")]
public class StairsDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<StairsDefinition> definitions = new();
    public IReadOnlyList<StairsDefinition> All => definitions;

    public StairsDefinition GetByName(string stairsName)
    {
        if (string.IsNullOrEmpty(stairsName)) return null;
        var def = definitions.Find(d => d.stairsName == stairsName);
        if (def == null)
            Debug.LogWarning($"[StairsDefinitionRegistry] '{stairsName}' not found.");
        return def;
    }
}
