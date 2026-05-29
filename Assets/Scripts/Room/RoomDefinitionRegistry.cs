using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for all room types.
/// Referenced by DungeonSaveController for name-based restore on load.
/// Create via: right-click → Create → Dungeon → Room Definition Registry
/// </summary>
[CreateAssetMenu(fileName = "RoomDefinitionRegistry",
                 menuName  = "Dungeon/Room Definition Registry")]
public class RoomDefinitionRegistry : ScriptableObject
{
    [SerializeField] private List<RoomDefinition> definitions = new();
    public IReadOnlyList<RoomDefinition> All => definitions;

    public RoomDefinition GetByName(string roomName)
    {
        if (string.IsNullOrEmpty(roomName)) return null;
        var def = definitions.Find(d => d.roomName == roomName);
        if (def == null)
            Debug.LogWarning($"[RoomDefinitionRegistry] '{roomName}' not found.");
        return def;
    }
}
