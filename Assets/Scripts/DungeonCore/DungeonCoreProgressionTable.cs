using System;
using UnityEngine;

/// <summary>
/// Per-tier mana and capacity progression. Serialized on DungeonCore so
/// values can be tweaked in the inspector without code changes.
///
/// HOW IT WORKS
///   Each tier has a startMana / startCapacity (= tier rank 1 value) and a
///   perRankMana / perRankCapacity (added per rank inside the tier).
///
///   Bronze 1 mana = bronze.startMana
///   Bronze 2 mana = bronze.startMana + 1 * bronze.perRankMana
///   Bronze 10 mana = bronze.startMana + 9 * bronze.perRankMana
///   Silver 1 mana = silver.startMana (which should equal 2 × Bronze 10 by design)
///
///   The tier-up doubling rule is enforced by YOU at setup time —
///   set silver.startMana = 2 × bronze.final, etc. Defaults below preserve
///   the rule with placeholder numbers.
/// </summary>
[Serializable]
public class TierProgression
{
    public LevelTier tier;
    public int startMana;
    public int perRankMana;
    public int startCapacity;
    public int perRankCapacity;
    public int floorRadius;   // floor radius for the floor this tier unlocks (Bronze ignored)
}

[Serializable]
public class DungeonCoreProgressionTable
{
    /// <summary>One entry per tier, in tier order (Bronze, Silver, Gold, Diamond, God).</summary>
    public TierProgression[] tiers = new TierProgression[]
    {
        new TierProgression { tier = LevelTier.Bronze,  startMana = 100,  perRankMana = 20,  startCapacity = 100,  perRankCapacity = 10,  floorRadius = 100 },
        new TierProgression { tier = LevelTier.Silver,  startMana = 560,  perRankMana = 40,  startCapacity = 380,  perRankCapacity = 20,  floorRadius = 150 },
        new TierProgression { tier = LevelTier.Gold,    startMana = 1600, perRankMana = 80,  startCapacity = 1040, perRankCapacity = 40,  floorRadius = 250 },
        new TierProgression { tier = LevelTier.Diamond, startMana = 3840, perRankMana = 160, startCapacity = 2400, perRankCapacity = 80,  floorRadius = 400 },
        new TierProgression { tier = LevelTier.God,     startMana = 8320, perRankMana = 0,   startCapacity = 4960, perRankCapacity = 0,   floorRadius = 600 },
    };

    public TierProgression Get(LevelTier tier) => tiers[(int)tier];

    public int ManaAt(int flatLevel)
    {
        var (tier, rank) = LevelTierUtil.FromFlatLevel(flatLevel);
        var t = Get(tier);
        return t.startMana + (rank - 1) * t.perRankMana;
    }

    public int CapacityAt(int flatLevel)
    {
        var (tier, rank) = LevelTierUtil.FromFlatLevel(flatLevel);
        var t = Get(tier);
        return t.startCapacity + (rank - 1) * t.perRankCapacity;
    }

    /// <summary>Floor radius for floor N. Floor 0 uses Bronze's value.</summary>
    public int FloorRadius(int floorIndex)
    {
        int idx = Mathf.Clamp(floorIndex, 0, tiers.Length - 1);
        return tiers[idx].floorRadius;
    }
}
