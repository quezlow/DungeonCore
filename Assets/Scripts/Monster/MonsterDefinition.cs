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
    [Tooltip("Mana spent to place a spawner of this monster. Boss variants scale it.")]
    [SerializeField] private float manaCost = 25f;

    [Header("Unlock")]
    [Tooltip("Level tier at which this monster becomes placeable. Bronze rank 1 = available from the start.")]
    public LevelTier requiredTier = LevelTier.Bronze;
    [Tooltip("Rank within the required tier at which this monster unlocks (1-based).")]
    [Min(1)] public int requiredRank = 1;

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

    [Header("Stamina")]
    [Tooltip("Combat stamina pool. 0 = tireless: no stamina bar, never skips a swing. " +
             "Drains by Attack Stamina Cost per attack; regen rates live on DungeonMonster.")]
    [Min(0f)] public float maxStamina = 0f;
    [Tooltip("Stamina spent per attack. Ignored when Max Stamina is 0.")]
    [Min(0f)] public float attackStaminaCost = 0f;

    [Header("Targeting")]
    [Tooltip("Which adventurer this monster prefers to attack. Nearest = no class bias " +
             "(default). Casters = Mage/Cleric; Healers = Cleric; Wounded = lowest HP%. " +
             "A taunting Tank still overrides this.")]
    public TargetPriority targetPriority = TargetPriority.Nearest;

    [Header("Knockback")]
    [Tooltip("Shove distance applied to a target on a heavy hit. 0 = this monster never knocks back.")]
    [Min(0f)] public float knockbackForce = 0f;
    [Tooltip("Minimum damage this monster's hit must deal to trigger knockback.")]
    [Min(0f)] public float knockbackMinDamage = 0f;

    [Header("Necromancy")]
    [Tooltip("If true, this monster raises nearby adventurer corpses into transient minions.")]
    public bool isNecromancer = false;
    [Tooltip("Radius (world units) within which the necromancer can raise a corpse.")]
    [Min(0f)] public float raiseRange = 3f;
    [Tooltip("Seconds the necromancer channels (holding still) before a raise completes.")]
    [Min(0f)] public float raiseChannelSeconds = 1.5f;
    [Tooltip("Cooldown between raises.")]
    [Min(0f)] public float raiseCooldown = 5f;
    [Tooltip("Maximum living raised minions this necromancer sustains at once.")]
    [Min(0)] public int maxRisen = 3;
    [Tooltip("Seconds a raised minion lives before crumbling for good.")]
    [Min(0f)] public float risenLifetime = 45f;
    [Tooltip("Possible monster types a raise produces — one is chosen at random (e.g. Skeleton, Zombie).")]
    public System.Collections.Generic.List<MonsterDefinition> risenDefinitions = new();

    [Header("Telegraph")]
    [Tooltip("Windup seconds before this monster's attack lands (shows a pre-attack tell). 0 = instant, no telegraph.")]
    [Min(0f)] public float telegraphSeconds = 0f;

    /// <summary>
    /// Mana/capacity cost to keep this monster active.
    /// Virtual so BossVariantDefinition can scale it.
    /// </summary>
    public virtual int CapacityCost => capacityCost;

    /// <summary>Mana spent to place a spawner of this monster. Virtual so bosses scale it.</summary>
    public virtual float ManaCost => manaCost;

    /// <summary>Flat dungeon level at which this monster unlocks (from requiredTier / requiredRank).</summary>
    public int RequiredFlatLevel => LevelTierUtil.ToFlatLevel(requiredTier, requiredRank);
}