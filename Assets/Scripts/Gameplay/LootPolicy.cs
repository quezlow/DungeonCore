/// <summary>
/// How much the dungeon lets its dead and its strongboxes pay out, and the
/// clock that stops the player retuning it every morning.
///
/// WHY A DIAL AT ALL, when this project's habit is to make the player express
/// intent by BUILDING rather than by setting a number. Because on drops there
/// is nothing to build with. Canon 15B authors loot on the PREFAB and records
/// that 112 definitions resolve to 49 prefabs, with slot-mates inheriting
/// identical tables and per-type flavour rejected outright. Rooms and
/// attractors change WHO arrives; chest tiers change WHICH chest gets picked.
/// Neither changes how much anyone gets. The dial is the only lever there is,
/// and canon 15B's own rejection points here: it refuses the dial as part of
/// Monster Drops and says the tension "belongs" in the satisfaction layer.
/// This is that layer.
///
/// UNSET IS A REAL ZERO, not a hidden Average. The opening beat has the first
/// party leave empty-handed and the wisp admit the level was never set, and
/// that line is only honest if nothing actually dropped. A band that quietly
/// behaved as Average would make the wisp a liar, which this project does not
/// ship.
///
/// STATIC RATHER THAN A SCENE SINGLETON, deliberately. There is no per-scene
/// state here and nothing to place; a MonoBehaviour would add an Inspector
/// reference whose absence fails silently, which canon 40 names as the reason
/// the panel row builds itself in code. The cost is that a static needs an
/// explicit reset on a new game, and ResetForNewGame is it.
/// </summary>
public enum LootGenerosity
{
    // APPEND ONLY. This serialises into the save as an int.
    Unset,
    Poor,
    BelowAverage,
    Average,
    AboveAverage,
    Generous,
}

/// <summary>The live loot policy: the band, the clock, and nothing else.</summary>
public static class LootPolicy
{
    /// <summary>Dawns between one change and the next being permitted.</summary>
    public const int CooldownDays = 7;

    private static LootGenerosity level = LootGenerosity.Unset;

    /// <summary>The day the band was last set. -1 means never -- which is what
    /// arms the opening beat, and is NOT the same as "set on day zero".</summary>
    private static int lastChangedDay = -1;

    public static LootGenerosity Level => level;
    public static int LastChangedDay => lastChangedDay;

    /// <summary>True once the player has made a choice. The panel button gates
    /// its visibility on this, on canon 40's rule that a button for a system
    /// the player has never heard of is a spoiler and a dead click.</summary>
    public static bool HasBeenSet => lastChangedDay >= 0;

    /// <summary>What a loot roll multiplies by. Unset pays NOTHING -- see the
    /// enum's own note on why that is a real zero.</summary>
    public static float Multiplier => MultiplierFor(level);

    public static float MultiplierFor(LootGenerosity band)
    {
        switch (band)
        {
            case LootGenerosity.Poor: return 0.5f;
            case LootGenerosity.BelowAverage: return 0.75f;
            case LootGenerosity.Average: return 1f;
            case LootGenerosity.AboveAverage: return 1.4f;
            case LootGenerosity.Generous: return 2f;
            default: return 0f;   // Unset
        }
    }

    /// <summary>Player-facing band name, in the wisp's register rather than in
    /// enum case.</summary>
    public static string DisplayName(LootGenerosity band)
    {
        switch (band)
        {
            case LootGenerosity.Poor: return "Poor";
            case LootGenerosity.BelowAverage: return "Below Average";
            case LootGenerosity.Average: return "Average";
            case LootGenerosity.AboveAverage: return "Above Average";
            case LootGenerosity.Generous: return "Generous";
            default: return "Not Set";
        }
    }

    /// <summary>Dawns remaining before the band may change again; 0 when it may
    /// change now. The FIRST setting is always free -- there is no previous
    /// change to wait out.</summary>
    public static int DaysUntilChangeAllowed(int today)
    {
        if (lastChangedDay < 0) return 0;
        int elapsed = today - lastChangedDay;
        return elapsed >= CooldownDays ? 0 : CooldownDays - elapsed;
    }

    public static bool CanChange(int today) => DaysUntilChangeAllowed(today) <= 0;

    /// <summary>Set the band. Refuses inside the cooldown and returns false, so
    /// a caller cannot spend the change without knowing it did. Setting the
    /// SAME band still costs the cooldown: the clock is on the act, not on the
    /// difference, or a player could probe the panel for free.</summary>
    public static bool TrySet(LootGenerosity band, int today)
    {
        if (!CanChange(today)) return false;
        level = band;
        lastChangedDay = today;
        return true;
    }

    /// <summary>A fresh dungeon starts UNSET, which is what arms the opening
    /// beat. Statics do not clear themselves between runs.</summary>
    public static void ResetForNewGame()
    {
        level = LootGenerosity.Unset;
        lastChangedDay = -1;
    }

    // -- Save / Load ---------------------------------------------------

    public static LootPolicySaveData GetSaveData()
        => new LootPolicySaveData { level = (int)level, lastChangedDay = lastChangedDay };

    /// <summary>Restore, and heal a save written before this system existed.
    ///
    /// A NULL BLOCK HEALS TO AVERAGE, NOT TO UNSET, and the distinction is the
    /// whole reason this method has a comment. An existing dungeon has been
    /// dropping loot at 1x for its whole run; loading it into Unset would stop
    /// every drop in the game with no warning and no way for the player to
    /// connect the two. It also marks the policy as already chosen, so an old
    /// save does not get the opening beat replayed at it on day forty.</summary>
    public static void RestoreFromSave(LootPolicySaveData data)
    {
        if (data == null)
        {
            level = LootGenerosity.Average;
            lastChangedDay = 0;
            return;
        }
        level = (LootGenerosity)data.level;
        lastChangedDay = data.lastChangedDay;
    }
}

[System.Serializable]
public class LootPolicySaveData
{
    public int level;
    public int lastChangedDay = -1;
}
