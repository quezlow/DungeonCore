using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for all trap types.
/// Referenced by DungeonSaveController for name-based restore on load,
/// and by TrapSelectionUI for the placement picker.
/// </summary>
[CreateAssetMenu(fileName = "TrapDefinitionRegistry",
                 menuName  = "Dungeon/Trap Definition Registry")]
public class TrapDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<TrapDefinition> definitions = new();
    public IReadOnlyList<TrapDefinition> All => definitions;

    public TrapDefinition GetByName(string trapName)
    {
        if (string.IsNullOrEmpty(trapName)) return null;
        var def = definitions.Find(d => d.trapName == trapName);
        if (def == null)
            Debug.LogWarning($"[TrapDefinitionRegistry] '{trapName}' not found.");
        return def;
    }
}
