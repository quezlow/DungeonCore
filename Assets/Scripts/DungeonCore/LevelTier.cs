using System;
using UnityEngine;

/// <summary>
/// Tiered progression for the dungeon level.
///
/// Internal storage in DungeonCore is a flat 1-26 integer (dungeonLevel).
/// This class maps that integer to a (tier, rank) pair for display and
/// for unlock gating (e.g. "stairs to Floor 2 unlock at Silver 1").
///
/// Tier sizes (ranks per tier):
///   Bronze   1-10  → flat indices 1-10
///   Silver   1-7   → flat indices 11-17
///   Gold     1-5   → flat indices 18-22
///   Diamond  1-3   → flat indices 23-25
///   God      1     → flat index 26
///
/// Diamond 3 → God 1 is gated by a special requirement (TBD). Until that
/// requirement is defined, ConfirmLevelUp() refuses the Diamond 3 → God 1
/// transition. DungeonCore.LevelUpAvailable reports false at Diamond 3.
/// </summary>
public enum LevelTier
{
    Bronze,
    Silver,
    Gold,
    Diamond,
    God,
}

public static class LevelTierUtil
{
    public static readonly int[] RanksPerTier = { 10, 7, 5, 3, 1 };

    /// <summary>Total flat levels across all tiers (currently 26).</summary>
    public static int MaxFlatLevel
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < RanksPerTier.Length; i++) sum += RanksPerTier[i];
            return sum;
        }
    }

    /// <summary>Convert flat 1-based level to (tier, rank-within-tier 1-based).</summary>
    public static (LevelTier tier, int rank) FromFlatLevel(int flatLevel)
    {
        if (flatLevel < 1) flatLevel = 1;

        int remaining = flatLevel;
        for (int i = 0; i < RanksPerTier.Length; i++)
        {
            if (remaining <= RanksPerTier[i])
                return ((LevelTier)i, remaining);
            remaining -= RanksPerTier[i];
        }

        // Above God 1 — clamp.
        return (LevelTier.God, 1);
    }

    /// <summary>Convert (tier, rank) to flat 1-based level.</summary>
    public static int ToFlatLevel(LevelTier tier, int rank)
    {
        int flat = 0;
        for (int i = 0; i < (int)tier; i++) flat += RanksPerTier[i];
        return flat + Mathf.Clamp(rank, 1, RanksPerTier[(int)tier]);
    }

    /// <summary>
    /// Returns the flat level at which a tier begins (its rank 1).
    /// Bronze 1 = 1, Silver 1 = 11, Gold 1 = 18, Diamond 1 = 23, God 1 = 26.
    /// </summary>
    public static int FlatLevelForTierStart(LevelTier tier)
        => ToFlatLevel(tier, 1);

    /// <summary>Is the next level-up a tier transition (e.g. Bronze 10 → Silver 1)?</summary>
    public static bool IsTierBoundary(int currentFlatLevel)
    {
        var (tier, rank) = FromFlatLevel(currentFlatLevel);
        return rank == RanksPerTier[(int)tier] && tier != LevelTier.God;
    }

    /// <summary>Pretty-printed level name, e.g. "Bronze 7" or "God 1".</summary>
    public static string DisplayName(int flatLevel)
    {
        var (tier, rank) = FromFlatLevel(flatLevel);
        return $"{tier} {rank}";
    }

    /// <summary>
    /// True if this flat level is Diamond 3 — the cap pending the God 1 unlock.
    /// Used by DungeonCore.LevelUpAvailable to refuse the final transition.
    /// </summary>
    public static bool IsDiamondCap(int flatLevel)
    {
        var (tier, rank) = FromFlatLevel(flatLevel);
        return tier == LevelTier.Diamond && rank == RanksPerTier[(int)LevelTier.Diamond];
    }
}
