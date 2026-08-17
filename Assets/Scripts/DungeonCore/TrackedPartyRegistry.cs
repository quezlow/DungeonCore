using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks "named" adventurer parties so they persist across visits. A party that
/// resolves (every member dead or escaped) with at least one survivor is recorded;
/// after a cooldown it regroups and the spawner re-deploys it — survivors return as
/// their exact selves, fallen members are replaced by a fresh roll of the same type.
/// A total wipe (no survivors) ends the nemesis. Persisted via DungeonSaveData.
///
/// A party becomes tracked automatically when it contains a named member (Hero), or
/// when the player pins it from the KnownPartiesPanel.
/// </summary>
public class TrackedPartyRegistry : MonoBehaviour
{
    public static TrackedPartyRegistry Instance { get; private set; }

    [Header("Return")]
    [Tooltip("In-game days before a tracked party regroups and returns.")]
    [SerializeField] private int returnDelayDays = 2;

    [Header("Names")]
    [Tooltip("Pool of names assigned to named adventurers (Heroes), cycled in order.")]
    [SerializeField]
    private List<string> namePool = new()
    {
        "Garrick the Bold", "Sera Dawnblade", "Aldric Vane", "Mira Thorne",
        "Roland Greycloak", "Lyssa Quickfoot", "Bran Ironhand", "Elara Sunwell",
        "Kael the Undaunted", "Petra Stoneheart", "Dorian Ashford", "Vesna Wolfsbane"
    };

    private readonly List<TrackedParty> pending = new();
    private readonly List<AdventurerParty> active = new();
    private int nextNameIndex = 0;

    public IReadOnlyList<TrackedParty> PendingParties => pending;

    /// <summary>Drop a pending record (player unpin) — that party never returns.
    /// Named parties are refused; nemeses are permanent.</summary>
    public void ForgetPending(TrackedParty rec)
    {
        if (rec == null || HasNamedMember(rec)) return;
        pending.Remove(rec);
    }

    public static bool HasNamedMember(TrackedParty rec)
    {
        if (rec == null) return false;
        foreach (var m in rec.members)
            if (m != null && m.named && !string.IsNullOrEmpty(m.name)) return true;
        return false;
    }
    public IReadOnlyList<AdventurerParty> ActiveParties => active;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Active party roster (for the KnownPartiesPanel) ──────────
    public void RegisterActive(AdventurerParty p) { if (p != null && !active.Contains(p)) active.Add(p); }
    public void DeregisterActive(AdventurerParty p) { active.Remove(p); }

    /// <summary>Next name from the pool (cycles).</summary>
    public string GenerateName()
    {
        if (namePool == null || namePool.Count == 0) return "Champion";
        string n = namePool[nextNameIndex % namePool.Count];
        nextNameIndex++;
        return n;
    }

    /// <summary>Record a fully-resolved tracked party. Schedules a return only if someone survived.</summary>
    public void RecordResolvedParty(AdventurerParty party)
    {
        if (party == null) return;
        if (party.isClimax) return;                       // the finale's host doesn't return as a nemesis
        foreach (var m in party.Members)
            if (m.type == AdventurerType.Noble) return;   // a slain house sends vengeance, not a returning nemesis

        var record = new TrackedParty();
        int survivors = 0;
        int deaths = 0;      // actual deaths: not escaped, not breached, not taken alive
        int trapDeaths = 0;  // of those, the ones traps did the killing on
        foreach (var m in party.Members)
        {
            record.members.Add(new TrackedMember
            {
                type = (int)m.type,
                combatClass = (int)m.combatClass,
                name = m.name,
                named = m.named,
                survived = m.escaped,
                xp = m.xp,
                grudgeMonster = m.grudgeMonster,
            });
            if (m.escaped) survivors++;
            else if (!m.breached && !m.captured)
            {
                deaths++;
                if (m.diedToTraps) trapDeaths++;
            }
        }

        if (survivors == 0) return;   // total wipe — the nemesis is gone

        // Escapee intel (canon 48): the survivors carry home ONE party-level
        // fact. Traps did the killing when they account for at least half
        // the actual deaths -- captures and core-breaches sit outside the
        // ratio, because a member taken alive or one that reached the core
        // is not a trap story. One unlucky pitfall in a party the garrison
        // wiped stays noise, which is why a single trap death is not enough
        // on its own unless it was the only death.
        record.trapWise = trapDeaths >= 1 && trapDeaths * 2 >= deaths;

        record.bannerColorIndex = party.bannerColorIndex;

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        record.returnDay = day + Mathf.Max(0, returnDelayDays);
        pending.Add(record);
        Debug.Log($"[TrackedParty] {LabelFor(record)} resolved -- {survivors} escaped, "
                + $"{deaths} fell ({trapDeaths} to traps)"
                + (record.trapWise ? "; they will return wise to the traps" : "")
                + $". Returns day {record.returnDay}.");
    }

    /// <summary>Pull a party whose return day has arrived, or null. Removes it from the pending list.</summary>
    public TrackedParty TakeReturningParty()
    {
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        for (int i = 0; i < pending.Count; i++)
        {
            if (pending[i].returnDay <= day)
            {
                var p = pending[i];
                pending.RemoveAt(i);
                return p;
            }
        }
        return null;
    }

    public List<TrackedParty> GetSaveData() => new(pending);

    public void RestoreFromSave(List<TrackedParty> data)
    {
        pending.Clear();
        if (data != null) pending.AddRange(data);
    }

    // ── Display labels ───────────────────────────────────────────
    public static string LabelFor(AdventurerParty p)
    {
        if (p == null) return "Party";
        if (!string.IsNullOrEmpty(p.bannerLabelOverride)) return p.bannerLabelOverride;
        foreach (var m in p.Members)
            if (m.named && !string.IsNullOrEmpty(m.name)) return $"{m.name}'s company";
        string t = p.Members.Count > 0 ? p.Members[0].type.ToString() : "Adventurer";
        return $"{t} party ({p.Members.Count})";
    }

    public static string LabelFor(TrackedParty rec)
    {
        if (rec == null) return "Party";
        foreach (var m in rec.members)
            if (m.named && !string.IsNullOrEmpty(m.name)) return $"{m.name}'s company";
        string t = rec.members.Count > 0 ? ((AdventurerType)rec.members[0].type).ToString() : "Adventurer";
        return $"{t} party ({rec.members.Count})";
    }
}

[Serializable]
public class TrackedParty
{
    public int returnDay;
    public int bannerColorIndex = -1;
    /// <summary>Escapee intel (canon 48): traps accounted for at least half
    /// of this party's actual deaths, so the return seats a Rogue in a
    /// fresh slot. Recomputed at every resolution -- the record is destroyed
    /// on return -- so the intel decays by construction. Additive: legacy
    /// saves read false.</summary>
    public bool trapWise;
    public List<TrackedMember> members = new();
}

[Serializable]
public class TrackedMember
{
    public int type;          // AdventurerType
    public int combatClass;   // CombatClass
    public string name;
    public bool named;
    public bool survived;     // escaped alive last visit (else replaced by a fresh roll)
    public int xp;            // cumulative kill XP carried across returns
    public string grudgeMonster;   // monster type this survivor holds a grudge against
}