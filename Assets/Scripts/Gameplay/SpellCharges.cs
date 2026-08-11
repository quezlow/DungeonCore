using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Banked castings of workings the core does not hold (canon 41).
///
/// WHAT A CHARGE IS. One casting of a spell, spent on use. It reuses the entire
/// shipped cast surface -- the CAST tab, targeting, the radius ghost, the cost
/// preview, cooldowns and the pause rule -- and adds one integer per spell id.
/// There is deliberately no inventory, no item, no slot and no new UI: a
/// consumable system would have needed all four, and the whole reason this
/// shape was chosen is that it needs none of them.
///
/// KEYED ON SPELL ID, never on an enum ordinal, because ids are declared stable
/// and never renamed after ship (canon 38) while an appended effect value would
/// silently re-key every banked charge in every existing save.
///
/// A charge for a spell the core ALSO holds permanently is never spent --
/// SpellBook.HeldPermanently decides, and the permanent grant always wins. That
/// stops the trap where researching a working quietly eats the scrolls you were
/// saving for the raid.
/// </summary>
public static class SpellCharges
{
    private static readonly Dictionary<string, int> charges = new Dictionary<string, int>();

    /// <summary>Raised whenever a count changes. SpellBook relays this to
    /// OnRosterChanged so the picker rebuilds; nothing polls.</summary>
    public static event System.Action OnChanged;

    public static int CountFor(string spellId)
    {
        if (string.IsNullOrEmpty(spellId)) return 0;
        return charges.TryGetValue(spellId, out int n) ? n : 0;
    }

    public static int CountFor(SpellDefinition def)
        => def == null ? 0 : CountFor(def.id);

    /// <summary>True when any working at all is banked. Cheap enough to call
    /// per frame; the dictionary is at most roster-sized.</summary>
    public static bool AnyHeld
    {
        get
        {
            foreach (var kvp in charges) if (kvp.Value > 0) return true;
            return false;
        }
    }

    /// <summary>Bank castings. Additive: buying a second scroll stacks.</summary>
    public static void Grant(string spellId, int count)
    {
        if (string.IsNullOrEmpty(spellId) || count <= 0) return;
        charges[spellId] = CountFor(spellId) + count;
        Changed();
    }

    /// <summary>Spend one casting. False when none are banked -- the caller has
    /// already resolved the spell by this point, so a false here means the
    /// availability check and the ledger disagreed and nothing should be
    /// silently swallowed.</summary>
    public static bool TrySpend(string spellId)
    {
        int n = CountFor(spellId);
        if (n <= 0) return false;
        if (n == 1) charges.Remove(spellId);      // keep the ledger sparse
        else charges[spellId] = n - 1;
        Changed();
        return true;
    }

    public static void Clear()
    {
        if (charges.Count == 0) return;
        charges.Clear();
        Changed();
    }

    private static void Changed()
    {
        OnChanged?.Invoke();
        SpellBook.NotifyChargesChanged();
    }

    // -- Persistence ---------------------------------------------------------
    // Two parallel lists rather than a dictionary: JsonUtility cannot serialise
    // a Dictionary, and the paired-list shape already has precedent in the save
    // data. Both are additive and empty on legacy saves, so nothing migrates.

    public static void CaptureSaveState(DungeonSaveData save)
    {
        if (save == null) return;
        save.spellChargeIds = new List<string>();
        save.spellChargeCounts = new List<int>();
        foreach (var kvp in charges)
        {
            if (kvp.Value <= 0) continue;
            save.spellChargeIds.Add(kvp.Key);
            save.spellChargeCounts.Add(kvp.Value);
        }
    }

    public static void RestoreSaveState(DungeonSaveData save)
    {
        charges.Clear();
        if (save != null && save.spellChargeIds != null && save.spellChargeCounts != null)
        {
            // Length mismatch can only come from a hand-edited save. Take the
            // shorter of the two rather than throwing: a corrupt ledger must not
            // cost the player the rest of the file.
            int n = Mathf.Min(save.spellChargeIds.Count, save.spellChargeCounts.Count);
            for (int i = 0; i < n; i++)
            {
                string id = save.spellChargeIds[i];
                int count = save.spellChargeCounts[i];
                if (string.IsNullOrEmpty(id) || count <= 0) continue;
                charges[id] = count;
            }
        }
        Changed();
    }
}
