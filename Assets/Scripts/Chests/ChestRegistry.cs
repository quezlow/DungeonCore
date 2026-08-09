using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks every live chest so raids can re-arm them as a group. Chests reset
/// when the dungeon empties of raiders (event-driven) rather than on a private
/// timer, so a chest refills between parties instead of at an arbitrary moment.
/// </summary>
public static class ChestRegistry
{
    private static readonly HashSet<DungeonChest> live = new();

    public static void Register(DungeonChest c) { if (c != null) live.Add(c); }
    public static void Unregister(DungeonChest c) { if (c != null) live.Remove(c); }

    /// <summary>Re-arm every opened chest. Called when the last raiding party
    /// leaves or dies, so the next arrivals meet full chests.</summary>
    public static void ResetAll()
    {
        foreach (var c in live)
            if (c != null) c.Close();
        LogCycleTierStats();
    }

    // -- Tier diagnostics ------------------------------------------------
    // The tier hook is pure behaviour with no UI, so it is only provable
    // in a live raid. These counters let one playtest answer "did the
    // hunter cross for the gold chest" from the log rather than from
    // watching every run. Never saved: diagnostics, not state.
    private static readonly int[] cycleTargetedLoot = new int[3];
    private static readonly int[] cycleTargetedDelve = new int[3];
    private static readonly int[] cycleOpenedLoot = new int[3];
    private static readonly int[] cycleOpenedDelve = new int[3];
    private static readonly int[] runTargeted = new int[3];
    private static readonly int[] runOpened = new int[3];

    /// <summary>An adventurer picked this chest as its target.</summary>
    public static void RecordTargeted(ChestTier tier, AdventurerGoal goal)
    {
        int t = (int)tier;
        if (t < 0 || t > 2) return;
        runTargeted[t]++;
        if (goal == AdventurerGoal.LootAndLeave) cycleTargetedLoot[t]++;
        else cycleTargetedDelve[t]++;
    }

    /// <summary>An adventurer opened this chest.</summary>
    public static void RecordOpened(ChestTier tier, AdventurerGoal goal)
    {
        int t = (int)tier;
        if (t < 0 || t > 2) return;
        runOpened[t]++;
        if (goal == AdventurerGoal.LootAndLeave) cycleOpenedLoot[t]++;
        else cycleOpenedDelve[t]++;
    }

    /// <summary>Per-raid-cycle summary, emitted where the cycle already
    /// ends (ResetAll). Silent when no chest was touched, so quiet days
    /// stay quiet in the log.</summary>
    private static void LogCycleTierStats()
    {
        int total = 0;
        for (int i = 0; i < 3; i++)
            total += cycleTargetedLoot[i] + cycleTargetedDelve[i]
                   + cycleOpenedLoot[i] + cycleOpenedDelve[i];
        if (total == 0) return;

        Debug.Log("[ChestTiers] cycle targeted B/S/G "
            + $"loot={cycleTargetedLoot[0]}/{cycleTargetedLoot[1]}/{cycleTargetedLoot[2]} "
            + $"delve={cycleTargetedDelve[0]}/{cycleTargetedDelve[1]}/{cycleTargetedDelve[2]}; "
            + "opened "
            + $"loot={cycleOpenedLoot[0]}/{cycleOpenedLoot[1]}/{cycleOpenedLoot[2]} "
            + $"delve={cycleOpenedDelve[0]}/{cycleOpenedDelve[1]}/{cycleOpenedDelve[2]}");

        for (int i = 0; i < 3; i++)
        {
            cycleTargetedLoot[i] = 0; cycleTargetedDelve[i] = 0;
            cycleOpenedLoot[i] = 0; cycleOpenedDelve[i] = 0;
        }
    }

    /// <summary>Whole-run totals on demand (Commands context menu).</summary>
    public static void PrintTierStats()
    {
        Debug.Log("[ChestTiers] run targeted B/S/G "
            + $"{runTargeted[0]}/{runTargeted[1]}/{runTargeted[2]}; opened "
            + $"{runOpened[0]}/{runOpened[1]}/{runOpened[2]}");
    }
}
