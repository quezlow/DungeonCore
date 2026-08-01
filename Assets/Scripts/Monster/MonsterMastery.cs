/// <summary>
/// Mutation research multipliers for the dungeon's own monsters -- the
/// Bestiary twin of TrapMastery. Read live at the strike, the wound and the
/// stride, so every placed monster, past and future, changes the moment a
/// node completes. Wild monsters and invaders are excluded at every call
/// site (the IsWild gate), matching the trapworks wild ruling.
///
/// Tier I  (tech.mutation_1): damage x1.15, damage taken x0.9.
/// Tier II (tech.mutation_2): damage x1.3, damage taken x0.8, speed x1.1.
///
/// Reach was considered and dropped: attack range feeds the combat state
/// machine's spacing decisions and cannot be verified off-screen.
///
/// WHY cached rather than TrapMastery's read-through properties: the speed
/// multiplier sits on the per-frame movement path of every live monster,
/// and per-frame string-hash lookups are not worth paying there (the
/// EntityAnimationDriver hash-guard lesson). Values recompute only on
/// UnlockState.OnChanged; the static constructor's initial Recompute covers
/// unlocks loaded from a save before the first read.
/// </summary>
public static class MonsterMastery
{
    public const string TierOneKey = "tech.mutation_1";
    public const string TierTwoKey = "tech.mutation_2";

    public static float DamageMultiplier { get; private set; } = 1f;
    public static float DamageTakenMultiplier { get; private set; } = 1f;
    public static float SpeedMultiplier { get; private set; } = 1f;

    static MonsterMastery()
    {
        UnlockState.OnChanged += _ => Recompute();
        Recompute();
    }

    private static void Recompute()
    {
        if (UnlockState.IsUnlocked(TierTwoKey))
        {
            DamageMultiplier = 1.3f;
            DamageTakenMultiplier = 0.8f;
            SpeedMultiplier = 1.1f;
        }
        else if (UnlockState.IsUnlocked(TierOneKey))
        {
            DamageMultiplier = 1.15f;
            DamageTakenMultiplier = 0.9f;
            SpeedMultiplier = 1f;
        }
        else
        {
            DamageMultiplier = 1f;
            DamageTakenMultiplier = 1f;
            SpeedMultiplier = 1f;
        }
    }
}
