using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that acts as the single source of truth for all available
/// monster types. Both MonsterSelectionUI and DungeonSaveController reference
/// this asset instead of maintaining separate lists.
///
/// CREATE THE ASSET: right-click in the Project panel →
///   Create → Dungeon → Monster Definition Registry
/// Then add all MonsterDefinition assets to the Definitions list.
///
/// FUTURE — Bestiary System: this registry is the natural hook. A BestiaryManager
/// can check which definitions have been "seen" by the player, unlocking entries.
/// </summary>
[CreateAssetMenu(fileName = "MonsterDefinitionRegistry", menuName = "Dungeon/Monster Definition Registry")]
public class MonsterDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<MonsterDefinition> definitions = new();

    /// <summary>All registered monster definitions (read-only).</summary>
    public IReadOnlyList<MonsterDefinition> All => definitions;

    /// <summary>
    /// Finds a definition by MonsterDefinition.monsterName.
    /// Returns null and logs a warning if not found.
    /// </summary>
    public MonsterDefinition GetByName(string monsterName)
    {
        if (string.IsNullOrEmpty(monsterName)) return null;

        var def = definitions.Find(d => d.monsterName == monsterName);

        if (def == null)
            Debug.LogWarning($"[MonsterDefinitionRegistry] No definition found for '{monsterName}'.");

        return def;
    }

    public bool Contains(MonsterDefinition def) => definitions.Contains(def);
}
