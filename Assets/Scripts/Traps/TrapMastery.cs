using UnityEngine;

/// <summary>
/// Trapwright research multipliers, read at fire time so every placed trap --
/// past and future -- sharpens the moment a node completes. Stacks
/// multiplicatively with the room-side lever (RoomEffectCensus.
/// TrapDamageMultiplier from Forges and mounted trophies): research is the
/// permanent hand, rooms are the standing infrastructure.
///
/// Tier I  (tech.trapwright_1): +25% damage, +25% affliction durations.
/// Tier II (tech.trapwright_2): +50% damage, +50% affliction durations over
///                              base, and cooldowns cut to 80%.
///
/// The capture hold is deliberately outside DurationMultiplier -- lengthening
/// it widens the rescue window and would weaken the trap.
/// </summary>
public static class TrapMastery
{
    public const string TierOneKey = "tech.trapwright_1";
    public const string TierTwoKey = "tech.trapwright_2";

    public static float DamageMultiplier =>
        UnlockState.IsUnlocked(TierTwoKey) ? 1.5f
        : UnlockState.IsUnlocked(TierOneKey) ? 1.25f : 1f;

    public static float DurationMultiplier =>
        UnlockState.IsUnlocked(TierTwoKey) ? 1.5f
        : UnlockState.IsUnlocked(TierOneKey) ? 1.25f : 1f;

    public static float CooldownMultiplier =>
        UnlockState.IsUnlocked(TierTwoKey) ? 0.8f : 1f;
}
