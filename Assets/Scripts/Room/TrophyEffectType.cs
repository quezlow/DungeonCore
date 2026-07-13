/// <summary>
/// What a trophy contributes while displayed in a valid Trophy Hall (canon 23).
/// Append-only -- ordinals are not persisted directly, but keep them stable so
/// authored TrophyDefinition assets do not silently repoint.
/// </summary>
public enum TrophyEffectType
{
    MonsterDamage,   // additive fraction to a global monster attack multiplier (0.05 = +5%)
    ManaRegen,       // mana per second added to the core's regeneration
    TrapDamage,      // additive fraction to the global trap-damage multiplier
    Notoriety        // notoriety per second, a slow menacing trickle
}