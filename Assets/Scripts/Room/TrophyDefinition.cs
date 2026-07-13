using UnityEngine;

/// <summary>
/// A trophy: furniture that, when standing in a VALID Trophy Hall, grants a small
/// stacking bonus (canon 23). It is placeable anywhere like any furniture -- the
/// bonus simply does nothing outside a Hall. Gated by an earned deed: the trophy
/// only appears in the build picker once its deed is done, so trophies are how
/// deeds finally reach into play.
///
/// AUTHORING: right-click -> Create -> Dungeon -> Trophy Definition. It IS a
/// FurnitureDefinition (same placement, mana cost, prefab, icon), plus the deed
/// gate and one effect. Register in the FurnitureDefinitionRegistry like any
/// furniture.
/// </summary>
[CreateAssetMenu(fileName = "Trophy_", menuName = "Dungeon/Trophy Definition")]
public class TrophyDefinition : FurnitureDefinition
{
    [Header("Trophy")]
    [Tooltip("Deed that must be earned before this trophy appears in the build picker. " +
             "Use the deed's id (the 'deed.' + id key) or leave blank for an ungated trophy.")]
    public string requiredDeedKey;

    [Tooltip("What this trophy grants while displayed in a valid Trophy Hall.")]
    public TrophyEffectType effect;

    [Tooltip("Magnitude. MonsterDamage/TrapDamage are additive fractions (0.05 = +5%); " +
             "ManaRegen is mana/sec; Notoriety is notoriety/sec.")]
    public float magnitude = 0.05f;
}