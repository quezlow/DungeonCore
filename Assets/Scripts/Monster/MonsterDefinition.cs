using UnityEngine;

/// <summary>
/// ScriptableObject defining a monster type available for spawning.
/// Create via: Assets → Create → Dungeon → Monster Definition
///
/// Create one asset per monster type (Skeleton, Zombie, Armoured Skeleton, etc.)
/// and assign them to the MonsterSpawner's available types list.
/// </summary>
[CreateAssetMenu(fileName = "MonsterDef_New", menuName = "Dungeon/Monster Definition")]
public class MonsterDefinition : ScriptableObject
{
    [Header("Identity")]
    public string monsterName = "Monster";
    public Sprite icon;                    // shown in the spawner selection UI

    [Header("Prefab")]
    public DungeonMonster prefab;

    [Header("Cost")]
    public int capacityCost = 5;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;
}
