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
///
/// DAY 31 PART 1
///   - Added isAquatic flag. DungeonMonster reads spawner.Definition.isAquatic
///     to decide whether to ignore river fording slowdown. Default false.
///
/// DAY 31 PART 3A — Passive HP regen
///   - passiveRegenPerSecond: HP/sec restored in non-combat states after
///     regenCooldown seconds with no damage. Default 0 (disabled).
///   - regenCooldown: seconds since last damage before regen resumes.
///   - wildRegenMultiplier: multiplier applied when this definition is
///     used by a wild cave monster. Default 0 — wild monsters never
///     regen unless this is explicitly raised per-definition.
///   Boss scaling happens in DungeonMonster.ApplyBossModifiers (multiplied
///   by hpMultiplier so a 5× HP boss heals 5× faster, keeping the relative
///   heal rate constant).
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

    [Header("Terrain")]
    [Tooltip("DAY 31 — If true, this monster ignores river fording slowdown. " +
             "Reserved for future aquatic creature designs.")]
    public bool isAquatic = false;

    [Header("Passive Regen (DAY 31 PART 3A)")]
    [Tooltip("HP restored per second in Wander/Patrol/Idle states. " +
             "0 = no regen. Boss variants scale this by hpMultiplier.")]
    [Min(0f)] public float passiveRegenPerSecond = 0f;

    [Tooltip("Seconds the monster must go without taking damage before regen begins.")]
    [Min(0f)] public float regenCooldown = 5f;

    [Tooltip("Multiplier applied to passiveRegenPerSecond when this definition is used " +
             "by a wild cave monster (IsWild). Default 0 — wild monsters never regen by " +
             "default. Raise per-definition if a specific wild species should heal.")]
    [Min(0f)] public float wildRegenMultiplier = 0f;

    /// <summary>
    /// Mana/capacity cost to keep this monster active.
    /// Virtual so BossVariantDefinition can scale it.
    /// </summary>
    public virtual int CapacityCost => capacityCost;
}