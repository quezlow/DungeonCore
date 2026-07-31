using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The surnames of the town the player lived in, and which deed of that last
/// day belongs to each of them (canon 34).
///
/// Nobody who knew the player's face is alive. What survives is a NAME. When
/// the dungeon's reputation reaches the Guild's third grade the town's
/// descendants come down to see it for themselves -- and the houses that come
/// are the ones whose lives the player actually touched, read straight off the
/// persisted prologue flags.
///
/// The beggar at the gate is deliberately absent. He says his own name is not
/// worth giving; he has no line to descend, and `flag_give_alms` therefore
/// makes nobody eligible. That is authored, not an oversight.
/// </summary>
public static class PrologueHouses
{
    /// <summary>A house, the deed that makes it eligible, and its two wisp lines.</summary>
    public struct House
    {
        public string surname;
        public string flag;
        public string arriveLineId;   // spoken once, if this house leads the party
        public string fallLineId;     // spoken once, when this house's descendant dies
    }

    // Serra Vane led the three who killed the player at the opened seal. Her
    // line is eligible no matter what the player did that day, which is why it
    // is the fallback: an empty-handed core touched nobody, and gets Vane alone.
    public const string VaneSurname = "Vane";

    private static readonly House[] Table =
    {
        new House { surname = "Ferro",    flag = TutorialFlags.Bellows,
                    arriveLineId = "kin_ferro",    fallLineId = "kin_ferro_fall" },
        new House { surname = "Cress",    flag = TutorialFlags.FillJug,
                    arriveLineId = "kin_cress",    fallLineId = "kin_cress_fall" },
        new House { surname = "Bramm",    flag = TutorialFlags.DigRow,
                    arriveLineId = "kin_bramm",    fallLineId = "kin_bramm_fall" },
        new House { surname = "Ashcombe", flag = TutorialFlags.HelpHealer,
                    arriveLineId = "kin_ashcombe", fallLineId = "kin_ashcombe_fall" },
        new House { surname = "Latch",    flag = TutorialFlags.SmashCrates,
                    arriveLineId = "kin_latch",    fallLineId = "kin_latch_fall" },
        new House { surname = "Sedge",    flag = TutorialFlags.TakeOffering,
                    arriveLineId = "kin_sedge",    fallLineId = "kin_sedge_fall" },
        new House { surname = "Crane",    flag = TutorialFlags.LightCandle,
                    arriveLineId = "kin_crane",    fallLineId = "kin_crane_fall" },
        new House { surname = VaneSurname, flag = null,
                    arriveLineId = "kin_vane",     fallLineId = "kin_vane_fall" },
    };

    // Given names for the descendants. Generations on, the town's naming has
    // drifted; these are plainer than the noble pool on purpose -- these are
    // farmers' and smiths' great-grandchildren, not lords.
    private static readonly string[] GivenNames =
    {
        "Aldous", "Bryn", "Corran", "Della", "Edwin", "Fen", "Garrow", "Hesper",
        "Ivo", "Jessa", "Kell", "Lys", "Morrow", "Nell", "Orin", "Perrin",
        "Rhoda", "Sable", "Tobin", "Wren",
    };

    /// <summary>Every house whose deed the core remembers. Vane is never in this
    /// list -- it is the fallback, added only to fill a short party.</summary>
    public static List<House> EligibleHouses()
    {
        var found = new List<House>();
        if (!CoreMemory.Lived) return found;

        for (int i = 0; i < Table.Length; i++)
        {
            if (string.IsNullOrEmpty(Table[i].flag)) continue;   // Vane
            if (CoreMemory.Remembers(Table[i].flag)) found.Add(Table[i]);
        }
        return found;
    }

    /// <summary>The houses that come down, between min and max of them. Eligible
    /// houses are shuffled and taken first; Vane fills any shortfall. An
    /// empty-handed life yields exactly one name, and it is hers.</summary>
    public static List<House> PickParty(int min, int max)
    {
        var pool = EligibleHouses();
        var chosen = new List<House>();
        if (!CoreMemory.Lived) return chosen;

        // Fisher-Yates on a copy; Random is Unity's, so the pick varies per run.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int want = Random.Range(Mathf.Max(1, min), Mathf.Max(1, max) + 1);
        for (int i = 0; i < pool.Count && chosen.Count < want; i++) chosen.Add(pool[i]);

        if (chosen.Count == 0) chosen.Add(VaneHouse());
        return chosen;
    }

    public static House VaneHouse()
    {
        for (int i = 0; i < Table.Length; i++)
            if (Table[i].surname == VaneSurname) return Table[i];
        return Table[Table.Length - 1];
    }

    /// <summary>"Bryn Ashcombe" -- a given name and the house it descends from.</summary>
    public static string NameFor(House house)
    {
        string given = GivenNames.Length > 0
            ? GivenNames[Random.Range(0, GivenNames.Length)]
            : "Wren";
        return given + " " + house.surname;
    }

    /// <summary>Match a spawned adventurer's display name back to its house, so
    /// the death path can speak that line. Returns false for every other
    /// adventurer in the game, including nobles (whose houses never collide with
    /// the prologue roster).</summary>
    public static bool TryHouseOf(string displayName, out House house)
    {
        house = default;
        if (string.IsNullOrEmpty(displayName)) return false;

        int space = displayName.LastIndexOf(' ');
        if (space < 0 || space == displayName.Length - 1) return false;
        string surname = displayName.Substring(space + 1);

        for (int i = 0; i < Table.Length; i++)
            if (Table[i].surname == surname) { house = Table[i]; return true; }
        return false;
    }
}
