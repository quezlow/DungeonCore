using UnityEngine;

/// <summary>
/// The opening beat: the first party leaves empty-handed, says so, the wisp
/// admits the level was never set, and the notice opens.
///
/// IT FIRES ON THE FIRST PARTY TO FULLY RESOLVE, NOT ON THE FIRST TO LEAVE,
/// and the difference is the whole reason this class exists rather than a
/// two-line hook on the exit path. A party that is wiped never reaches the
/// exit, so a beat keyed on departure would never fire against a competent
/// dungeon, and the loot level would sit at Unset paying nothing for the rest
/// of the run with no prompt and no explanation.
///
/// So: resolution is the trigger, and the DRESSING varies with the outcome.
/// If anyone walked out, they walk out complaining and the wisp answers the
/// complaint. If nobody did, there is no one to complain and the wisp says so
/// instead. Both paths open the notice, because both leave the player with an
/// unset policy they need to know about.
///
/// ONE-SHOT PER RUN, on the DungeonAdventurer.firstPartyAnnounced precedent,
/// with the same explicit reset -- a static does not clear itself between
/// runs.
/// </summary>
public static class LootPolicyPrompt
{
    /// <summary>Gates the panel row button. Until the beat fires, the button
    /// is HIDDEN rather than greyed, on canon 40's rule that a button for a
    /// system the player has never heard of is a spoiler and a dead click.</summary>
    public const string UnlockKey = "event.loot_policy";

    private static bool fired;

    public static bool HasFired => fired;

    /// <summary>Called by AdventurerParty when every member has resolved.
    /// Silent on every party after the first.</summary>
    public static void NotifyPartyResolved(AdventurerParty party, bool anyLeftAlive)
    {
        if (fired) return;
        // Only the beat's DRESSING depends on the party; the trigger does not.
        // A null party still arms the notice rather than swallowing it.
        fired = true;

        UnlockState.Unlock(UnlockKey);

        if (anyLeftAlive)
        {
            BanterLines.ReactEmptyHanded(party);
            WispCompanion.Instance?.Speak("loot_policy_unset");
        }
        else
        {
            // Nobody survived to complain, so the complaint line would be a
            // lie. The notice still opens: the policy is still unset.
            WispCompanion.Instance?.Speak("loot_policy_unset_nosurvivors");
        }

        LootPolicyPanel.Instance?.Open(true);
    }

    /// <summary>Fresh dungeon re-arms the beat.</summary>
    public static void ResetForNewGame() => fired = false;

    public static LootPolicyPromptSaveData GetSaveData()
        => new LootPolicyPromptSaveData { fired = fired };

    /// <summary>A save written before this system existed loads with the beat
    /// ALREADY SPENT, matching LootPolicy healing such a save to Average: an
    /// established dungeon must not be interrupted on day forty to be told its
    /// loot level was never set, when in fact it now has one.</summary>
    public static void RestoreFromSave(LootPolicyPromptSaveData data)
    {
        fired = data == null || data.fired;
    }
}

[System.Serializable]
public class LootPolicyPromptSaveData
{
    public bool fired;
}
