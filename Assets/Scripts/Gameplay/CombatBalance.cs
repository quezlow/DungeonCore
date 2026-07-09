using UnityEngine;

/// <summary>
/// Global combat multipliers, applied once as each entity wakes. They scale the values that come
/// off the definition assets, so balance can be tuned in one place without editing every asset.
///
/// The static defaults below apply even when no CombatBalance component exists in the scene, so
/// the game stays balanced out of the box. Drop this component on a persistent object (alongside
/// the other managers) to override them from the Inspector.
///
/// Baseline before any multipliers: an adventurer has 50 HP and hits for 8 every 1.5s (5.3 dps);
/// a monster has 30 HP and hits for 5 every 1.5s (3.3 dps). One adventurer therefore trades
/// evenly with roughly 2.7 monsters before falling - and that is before class multipliers and
/// level scaling stack on top.
/// </summary>
[DefaultExecutionOrder(-500)]
public class CombatBalance : MonoBehaviour
{
    public const float DefaultAdventurerHp = 0.75f;
    public const float DefaultAdventurerDamage = 0.65f;
    public const float DefaultMonsterHp = 1f;
    public const float DefaultMonsterDamage = 1f;

    public static float AdventurerHp { get; private set; } = DefaultAdventurerHp;
    public static float AdventurerDamage { get; private set; } = DefaultAdventurerDamage;
    public static float MonsterHp { get; private set; } = DefaultMonsterHp;
    public static float MonsterDamage { get; private set; } = DefaultMonsterDamage;

    [Header("Adventurers")]
    [Tooltip("Scales every adventurer's max HP as it spawns.")]
    [SerializeField, Min(0.05f)] private float adventurerHpMultiplier = DefaultAdventurerHp;

    [Tooltip("Scales every adventurer's attack damage as it spawns.")]
    [SerializeField, Min(0.05f)] private float adventurerDamageMultiplier = DefaultAdventurerDamage;

    [Header("Monsters")]
    [Tooltip("Scales every monster's max HP as it spawns.")]
    [SerializeField, Min(0.05f)] private float monsterHpMultiplier = DefaultMonsterHp;

    [Tooltip("Scales every monster's attack damage as it spawns.")]
    [SerializeField, Min(0.05f)] private float monsterDamageMultiplier = DefaultMonsterDamage;

    private void Awake()
    {
        AdventurerHp = adventurerHpMultiplier;
        AdventurerDamage = adventurerDamageMultiplier;
        MonsterHp = monsterHpMultiplier;
        MonsterDamage = monsterDamageMultiplier;
    }
}