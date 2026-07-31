using UnityEngine;

/// <summary>
/// What the core remembers of the life it lived above (canon 34).
///
/// Persistence is the prologue's own store and is documented as such; this is
/// the dungeon's read surface over it. Nothing below the surface touches
/// Persistence directly.
///
/// A memory ECHO is one wisp line, fired once ever, at a dungeon moment that
/// rhymes with something the player actually did on their last day alive.
/// Recall(momentId) is the whole API: drop it at the event site and the table
/// decides whether anything is said.
///
/// Three outcomes at any moment:
///   - never lived the prologue          -> silence, always
///   - lived it and holds the flag       -> the echo line
///   - lived it empty-handed             -> a hollow line, at most three ever
///   - lived it, holds other flags       -> silence (this memory is not theirs)
///
/// Echo lines are authored with once = true in WispScript, so the shipped
/// spoken-line save field already remembers them. This class adds no save
/// state of its own beyond the flag list itself.
/// </summary>
public static class CoreMemory
{
    // -- Moment ids ------------------------------------------------
    // Named constants rather than loose strings so a rename is a compile error
    // and not a silently dead echo. Where a deed already owns the same moment
    // the ids deliberately match, so the two systems read as one vocabulary.

    public const string FirstRaise   = "first_raise";     // a corpse walks again
    public const string FirstPin     = "first_pin";       // a capture trap closes
    public const string FirstTribute = "first_tribute";   // an offering is absorbed
    public const string FirstSpoils  = "first_spoils";    // an adventurer is stripped
    public const string FirstDescent = "first_descent";   // a deeper floor is entered
    public const string FirstTrap    = "first_trap";      // a trap fires on the living
    public const string FirstBuried  = "first_buried";    // old bones come out of stone

    // -- The table -------------------------------------------------
    // One row per echo: the moment, the deed in life it answers, the WispScript
    // line id. Adding an echo is a row here plus a line in WispScript plus one
    // Recall() call at the site. Nothing else.

    private struct Row
    {
        public string moment;
        public string flag;
        public string lineId;
    }

    private static readonly Row[] Table =
    {
        new Row { moment = FirstRaise,   flag = TutorialFlags.DigGrave,     lineId = "echo_grave" },
        new Row { moment = FirstPin,     flag = TutorialFlags.FreeNet,      lineId = "echo_net" },
        new Row { moment = FirstTribute, flag = TutorialFlags.TakeOffering, lineId = "echo_offering" },
        new Row { moment = FirstSpoils,  flag = TutorialFlags.GiveAlms,     lineId = "echo_alms" },
        new Row { moment = FirstDescent, flag = TutorialFlags.MillClimb,    lineId = "echo_climb" },
        new Row { moment = FirstTrap,    flag = TutorialFlags.Quench,       lineId = "echo_quench" },
        new Row { moment = FirstBuried,  flag = TutorialFlags.PrayShrine,   lineId = "echo_stone" },
    };

    /// <summary>The empty-handed voice. Spoken in order, one per qualifying
    /// moment, and then never again -- the wisp stops reaching rather than
    /// nagging. Three is the whole allowance.</summary>
    private static readonly string[] HollowLines =
    {
        "echo_hollow_1",
        "echo_hollow_2",
        "echo_hollow_3",
    };

    // -- Queries ---------------------------------------------------

    /// <summary>True if the player lived the prologue rather than skipping it.</summary>
    public static bool Lived => Persistence.HasFlag(TutorialFlags.Lived);

    /// <summary>True if the core remembers this deed.</summary>
    public static bool Remembers(string flag) => Persistence.HasFlag(flag);

    /// <summary>Lived the day and earned nothing that weighs an affinity. A
    /// legitimate path, and the one the ceremony calls its own kind of freedom.
    /// Distinct from a skipped prologue, which is not a life at all.</summary>
    public static bool EmptyHanded
    {
        get
        {
            if (!Lived) return false;
            for (int i = 0; i < TutorialFlags.AffinityFlags.Length; i++)
                if (Persistence.HasFlag(TutorialFlags.AffinityFlags[i])) return false;
            return true;
        }
    }

    // -- The one call ----------------------------------------------

    /// <summary>Fire any echo bound to this moment. Safe to call for ids no row
    /// uses, and safe to call every time the moment happens -- the once-flag on
    /// the line does the gating, so the site never needs its own bookkeeping.</summary>
    public static void Recall(string momentId)
    {
        if (string.IsNullOrEmpty(momentId)) return;

        // A restore replays a great deal of history in a few frames; none of it
        // is happening to the player. Mirrors DeedsController.NotifyMoment.
        if (DungeonSaveController.IsLoading) return;

        var wisp = WispCompanion.Instance;
        if (wisp == null) return;
        if (!Lived) return;

        // One memory per frame. A capture trap resolves BeginPinned inside
        // ApplyEffect and then the trap site recalls in the same call, so a life
        // holding both deeds would hear two echoes stacked on one snare. Echoes
        // are supposed to be rare; two at once reads as a system, not a memory.
        if (Time.frameCount == lastEchoFrame) return;

        bool hollow = EmptyHanded;

        for (int i = 0; i < Table.Length; i++)
        {
            if (Table[i].moment != momentId) continue;

            if (hollow)
            {
                SpeakNextHollow(wisp);
                return;
            }

            if (!Remembers(Table[i].flag)) return;
            lastEchoFrame = Time.frameCount;
            wisp.Speak(Table[i].lineId);
            return;
        }
    }

    private static int lastEchoFrame = -1;

    /// <summary>The next unspoken hollow line, or nothing once all three are
    /// spent. The wisp gives up looking, which is the point.</summary>
    private static void SpeakNextHollow(WispCompanion wisp)
    {
        for (int i = 0; i < HollowLines.Length; i++)
        {
            if (wisp.HasSpoken(HollowLines[i])) continue;
            lastEchoFrame = Time.frameCount;
            wisp.Speak(HollowLines[i]);
            return;
        }
    }
}
