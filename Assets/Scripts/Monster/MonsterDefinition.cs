using UnityEngine;

/// <summary>
/// ScriptableObject defining a monster type available for spawning.
/// Create via: Assets → Create → Dungeon → Monster Definition
///
/// Create one asset per monster type (Skeleton, Zombie, Armoured Skeleton, etc.)
/// and assign them to the MonsterSpawner's available types list.
///
/// SUBCLASSING
///   BossVariantDefinition : MonsterDefinition overrides CapacityCost to apply
///   a multiplier. If you add more cost-related fields here, expose them as
///   virtual properties so boss variants can scale them too.
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
    [SerializeField] private int capacityCost = 5;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;

    /// <summary>
    /// Mana/capacity cost to keep this monster active.
    /// Virtual so BossVariantDefinition can scale it.
    /// </summary>
    public virtual int CapacityCost => capacityCost;
}