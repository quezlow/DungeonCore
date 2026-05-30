using UnityEngine;

/// <summary>
/// ScriptableObject defining a combat class archetype.
/// AdventurerSpawner picks from a list of these to determine which prefab to
/// instantiate. DungeonAdventurer.Initialise() applies the stats at runtime.
///
/// DAY 21: One stub definition covers all party members (same prefab, different
/// stats per class asset).
///
/// DAY 39 — All Six Combat Classes: add Fighter, Mage, Rogue, Cleric, Explorer,
/// Tank as separate assets. Uncomment the CombatClass field and unique behaviour
/// hook once the class system is built. No code changes needed here or in
/// DungeonAdventurer — just populate the new fields on each asset.
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
    public float xpOnDeath        = 15f;

    [Header("Trap Detection (Day 39 — Rogue Class)")]
    [Tooltip("If true, instances of this class can detect and flag nearby traps.")]
    public bool canDetectTraps = false;
    public float trapDetectionRadius = 2.5f;
    public float trapDetectionChancePerSecond = 0.3f;


    // ── Day 39 stub ───────────────────────────────────────────────
    // Uncomment when combat class unique behaviours are implemented.
    //
    // [Header("Day 39 — Combat Class")]
    // public CombatClass combatClass = CombatClass.Fighter;
    //
    // [Tooltip("Optional: hook for class-unique behaviour. Resolved in
    //           DungeonAdventurer.HandleCombatClassBehaviour().")]
    // public bool hasUniqueAbility = false;
}
