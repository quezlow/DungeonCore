using UnityEngine;

/// <summary>
/// ScriptableObject defining a combat class archetype.
/// AdventurerSpawner picks from a list of these to determine which prefab to
/// instantiate. DungeonAdventurer.Initialise() applies the stats at runtime.
///
/// DAY 21: One stub definition covers all party members (same prefab, different
/// stats per class asset).
///
/// All Six Combat Classes: combat role is a SEPARATE axis. See
/// CombatClassDefinition (one asset per class) + the spawner's per-member
/// assignment. This definition only carries the type + base stats.
///
/// CREATE ASSETS: right-click in Project →
///   Create → Dungeon → Adventurer Definition
/// </summary>
[CreateAssetMenu(fileName = "NewAdventurerDefinition",
                 menuName  = "Dungeon/Adventurer Definition")]
public class AdventurerDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name for this combat class (e.g. Fighter, Mage, Rogue).")]
    public string className = "Adventurer";

    [Tooltip("If true, instances spawn as named individuals and their party is tracked as a persistent nemesis (default-on for Hero).")]
    public bool named = false;

    [Header("Type (Day 37)")]
    [Tooltip("Which of the nine adventurer types this asset represents. Drives the " +
             "derived intent (reward category) and goal (behaviour) via AdventurerTypeInfo.")]
    public AdventurerType type = AdventurerType.Mercenary;

    [Tooltip("If true, every instance is forced to the trait below instead of the " +
             "spawner's random roll (e.g. the Noble is always Cowardly).")]
    public bool overrideTrait = false;
    public BehaviourTrait forcedTrait = BehaviourTrait.Balanced;

    [Header("Prefab")]
    [Tooltip("The DungeonAdventurer prefab used for this class. " +
             "All classes can share the same prefab until sprites are added on Day 39.")]
    public DungeonAdventurer prefab;

    [Header("Base Stats")]
    public float maxHP            = 50f;
    public float moveSpeed        = 2f;
    public float attackDamage     = 8f;
    public float attackRange      = 1.2f;
    public float attackCooldown   = 1.5f;
    public float detectionRange   = 2.5f;
    public float chestDetectionRange = 3f;
    public float xpOnDeath = 15f;

    [Header("Knockback")]
    [Tooltip("Shove distance applied to a target on a heavy hit. 0 = never knocks back.")]
    public float knockbackForce = 0f;
    [Tooltip("Minimum damage this adventurer's hit must deal to trigger knockback.")]
    public float knockbackMinDamage = 0f;

    [Header("Trap Detection (Day 39 — Rogue Class)")]
    [Tooltip("If true, instances of this class can detect and flag nearby traps.")]
    public bool canDetectTraps = false;
    public float trapDetectionRadius = 2.5f;
    public float trapDetectionChancePerSecond = 0.3f;
}
