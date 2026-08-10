using System.Collections.Generic;

/// <summary>
/// Files the wisp's sayings into readable groups for the journal's LORE tab
/// (canon 19A). The grouping key is the id PREFIX -- "echo_grave" files under
/// Echoes, "kin_ferro_fall" under Kin -- which is why chapter 23 of the content
/// authoring guide tells authors to keep those prefixes meaningful.
///
/// Prefix rather than a category field on WispScript.Line deliberately: a field
/// would be tidier, but it makes every new line depend on regenerating the asset
/// through Fill Canon Lines, and that step fails silently. A prefix map costs
/// nothing at author time and cannot be forgotten.
///
/// An id whose prefix is unmapped is NOT dropped -- it falls to "Other sayings"
/// and is named by the Print Wisp Lore Page command, so a new content family
/// announces itself instead of quietly vanishing into a bucket.
/// </summary>
public static class WispLoreIndex
{
    /// <summary>One journal group: a heading and the lines gathered under it.</summary>
    public class Group
    {
        public string title;
        public List<string> lines = new List<string>();
    }

    private const string Other = "Other sayings";

    // Display order. The first days first, the personal last: a player scrolling
    // this page should travel outward from the dungeon and end on the life it lost.
    private static readonly string[] Order =
    {
        "The First Days",
        "Firsts",
        "The Dead Below",
        "The Surface",
        "Kin",
        "Echoes",
        Other,
    };

    // Prefix -> group. Matched on the id up to its first underscore.
    private static readonly Dictionary<string, string> ByPrefix = new Dictionary<string, string>
    {
        { "arrive",    "The First Days" },

        { "first",     "Firsts" },
        { "pressed",   "Firsts" },
        { "patrol",    "Firsts" },
        { "notoriety", "Firsts" },
        { "pattern",   "Firsts" },
        { "merchant",  "Firsts" },

        { "site",      "The Dead Below" },
        { "holy",      "The Dead Below" },
        { "rest",      "The Dead Below" },

        { "village",   "The Surface" },
        { "outpost",   "The Surface" },
        { "caravan",   "The Surface" },
        { "road",      "The Surface" },

        { "kin",       "Kin" },

        { "echo",      "Echoes" },
    };

    public static string PrefixOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        int cut = id.IndexOf('_');
        return cut < 0 ? id : id.Substring(0, cut);
    }

    public static string GroupFor(string id)
        => ByPrefix.TryGetValue(PrefixOf(id), out string group) ? group : Other;

    public static bool IsMapped(string id) => ByPrefix.ContainsKey(PrefixOf(id));

    /// <summary>Every saying the wisp has actually spoken, grouped and in authored
    /// order. HEARD ONLY: a placeholder per unheard line would advertise how many
    /// kin there are to meet and how many echoes a life could hold, before the
    /// player has met one. Empty groups are omitted rather than shown bare.
    ///
    /// Repeatable lines (once = false) can never appear: WispCompanion only records
    /// one-shot ids as spoken, and widening that would change the speak-once gate
    /// for the sake of four greetings.</summary>
    public static List<Group> Gather(WispScript script, WispCompanion wisp)
    {
        var groups = new List<Group>();
        if (script == null || script.lines == null || wisp == null) return groups;

        var byTitle = new Dictionary<string, Group>();
        foreach (string title in Order)
        {
            var g = new Group { title = title };
            byTitle[title] = g;
            groups.Add(g);
        }

        foreach (WispScript.Line line in script.lines)
        {
            if (line == null || string.IsNullOrEmpty(line.id) || string.IsNullOrEmpty(line.text)) continue;
            if (!wisp.HasSpoken(line.id)) continue;
            byTitle[GroupFor(line.id)].lines.Add(line.text);
        }

        groups.RemoveAll(g => g.lines.Count == 0);
        return groups;
    }

    /// <summary>How many of the wisp's one-shot sayings have been gathered, and how
    /// many exist. Repeatable lines are excluded from BOTH halves -- counting a line
    /// that can never be gathered would leave the total permanently unreachable.</summary>
    public static void Tally(WispScript script, WispCompanion wisp, out int heard, out int total)
    {
        heard = 0;
        total = 0;
        if (script == null || script.lines == null) return;
        foreach (WispScript.Line line in script.lines)
        {
            if (line == null || string.IsNullOrEmpty(line.id) || !line.once) continue;
            total++;
            if (wisp != null && wisp.HasSpoken(line.id)) heard++;
        }
    }
}
