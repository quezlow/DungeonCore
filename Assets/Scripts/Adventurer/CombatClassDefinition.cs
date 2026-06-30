using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// DAY 39 — Defines a combat class as a multiplier OVERLAY on a member's
/// type-derived base stats, plus its unique-behaviour parameters. The spawner
/// holds one asset per class and assigns them per member (variety-biased).
/// DungeonAdventurer.Initialise applies the overlay after the type's base stats.
///
/// CREATE ASSETS: right-click in Project → Create → Dungeon → Combat Class
/// </summary>
[CreateAssetMenu(fileName = "NewCombatClass", menuName = "Dungeon/Combat Class")]
public class CombatClassDefinition : ScriptableObject
{
    [Header("Identity")]
    public CombatClass combatClass = CombatClass.Fighter;

    [Tooltip("Relative spawn weight among the combat classes. The spawner's variety " +
             "bias down-weights classes already present in the party.")]
    public float spawnWeight = 1f;

    [Header("Stat Multipliers (overlay on the type's base stats)")]
    public float hpMultiplier = 1f;
    public float moveSpeedMultiplier = 1f;
    public float attackDamageMultiplier = 1f;
    public float attackRangeMultiplier = 1f;
    public float attackCooldownMultiplier = 1f;
    public float detectionRangeMultiplier = 1f;

    [Header("Mage")]
    [Tooltip("Cosmetic marker — the ranged feel comes from a large attackRangeMultiplier.")]
    public bool rangedAttacker = false;

    [Header("Rogue")]
    [Tooltip("Forces canDetectTraps on, so this class spots and flags traps.")]
    public bool detectsTraps = false;

    [Header("Cleric")]
    public bool healsAllies = false;
    public float healAmount = 6f;
    public float healInterval = 3f;
    public float healRadius = 4f;

    [Header("Tank")]
    [Tooltip("Nearby monsters prefer this adventurer as their target (minimal taunt).")]
    public bool taunts = false;

    [Header("Explorer")]
    [Tooltip("Detours to scout this many random rooms before pursuing its goal.")]
    public int scoutRooms = 0;

    [Header("Resources")]
    [Tooltip("Stamina pool. 0 = no stamina bar. Basic attacks spend Attack Cost from here " +
             "unless Attack Uses Mana is ticked.")]
    public float maxStamina = 0f;
    [Tooltip("Mana pool. 0 = no mana bar. Casters (Mage bolts, Cleric heals) spend from here.")]
    public float maxMana = 0f;
    [Tooltip("If true, this class's basic attack costs MANA instead of stamina (Mage).")]
    public bool attackUsesMana = false;
    [Tooltip("Cost of one basic attack, drawn from mana if Attack Uses Mana else stamina.")]
    public float attackCost = 0f;
    [Tooltip("Mana cost per heal cast (Cleric). Ignored if the class doesn't heal.")]
    public float healManaCost = 0f;

    [Header("Loot")]
    [Tooltip("Gold this class drops on death (weighted). Rolled in addition to the adventurer's own LootTable.")]
    public List<LootTable.DropEntry> classLoot = new();
}