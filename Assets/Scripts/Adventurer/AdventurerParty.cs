using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight, non-MonoBehaviour grouping for a single spawned wave of
/// adventurers (Day 35). Created once per AdventurerSpawner.SpawnParty() and
/// shared by every member of that wave via DungeonAdventurer.Initialise().
///
/// Holds the party-wide Intent plus one-shot latches so party-level effects
/// fire exactly once regardless of how many members trigger them:
///   exitBonusApplied — guards the Pilgrim Notoriety reduction so a multi-
///                      pilgrim party only calms the dungeon a single time.
///
/// Deliberately minimal. The Phase 4 party banner / named-adventurer tracking
/// will extend this; nothing here needs to change for that.
/// </summary>
public class AdventurerParty
{
    public PartyIntent Intent { get; }

    /// <summary>Set true by the first member that completes a peaceful
    /// pilgrimage exit, so the Notoriety reduction is applied only once.</summary>
    public bool exitBonusApplied = false;

    // ── Formation / organize ────────────────────────────
    public FormationType Formation = FormationType.None;
    public float OrganizeEndTime = 0f;          // Time.time the party finishes forming up
    public Vector2 AdvanceDir = Vector2.right;  // entrance -> core, orients formation slots

    // ── Rogue trap-warning halt ─────────────────────────
    public float HaltUntil = 0f;        // members freeze movement until this time
    public float HaltCooldownEnd = 0f;  // earliest a new halt may begin

    private readonly Dictionary<int, int> slotCounts = new();

    public AdventurerParty(PartyIntent intent)
    {
        Intent = intent;
    }

    /// <summary>Hand out a per-lane ordinal so members in the same formation lane
    /// spread out instead of stacking. lane = class rank (Assault) or VIP/guard tier (Escort).</summary>
    public int ClaimSlot(int lane)
    {
        slotCounts.TryGetValue(lane, out int n);
        slotCounts[lane] = n + 1;
        return n;
    }

    // ── Named / tracked party (persistent nemesis) ──────
    public bool tracked = false;                       // set by a named member, or a player pin
    public readonly List<PartyMember> Members = new();
    public float notorietyDelta = 0f;                  // net notoriety this party caused (raid summary)
    private int resolvedCount = 0;

    // ── Party banner ────────────────────────────────────
    public bool hasBanner = false;             // guards one banner per party
    public int bannerColorIndex = -1;          // pinned-pool index (persisted); -1 = intent-coloured
    private readonly List<DungeonAdventurer> live = new();

    /// <summary>Track a member's live instance (for the banner's lead + majority logic).</summary>
    public void RegisterLive(DungeonAdventurer a) { if (a != null && !live.Contains(a)) live.Add(a); }
    public void DeregisterLive(DungeonAdventurer a) { live.Remove(a); }

    /// <summary>Members still alive in the dungeon (died and fled both leave this list).</summary>
    public int LiveCount()
    {
        int n = 0;
        foreach (var a in live) if (a != null) n++;
        return n;
    }

    /// <summary>The party's current banner-bearer among live members: Hero, else an
    /// Escort VIP (Noble / Scholar / Inspector), else the first live member.</summary>
    public DungeonAdventurer CurrentLead()
    {
        DungeonAdventurer vip = null, first = null;
        foreach (var a in live)
        {
            if (a == null) continue;
            if (a.Type == AdventurerType.Hero) return a;
            if (vip == null && (a.Type == AdventurerType.Noble || a.Type == AdventurerType.Scholar || a.Type == AdventurerType.Inspector)) vip = a;
            if (first == null) first = a;
        }
        return vip != null ? vip : first;
    }

    /// <summary>Registers a member as it spawns. A named member marks the whole party tracked.</summary>
    public PartyMember RegisterMember(AdventurerType type, string name, bool named)
    {
        var m = new PartyMember { type = type, name = name, named = named };
        Members.Add(m);
        if (named) tracked = true;
        return m;
    }

    /// <summary>Called when a member dies or escapes. When all members have resolved,
    /// a tracked party is recorded for return and the party leaves the active list.</summary>
    public void OnMemberResolved(PartyMember member, bool escaped, bool breached = false, int lootValue = 0)
    {
        if (member == null || member.resolved) return;
        member.resolved = true;
        member.escaped = escaped;
        member.breached = breached;
        member.lootValue = lootValue;
        resolvedCount++;

        if (resolvedCount < Members.Count || Members.Count == 0) return;

        RecordRaidSummary();

        if (tracked) TrackedPartyRegistry.Instance?.RecordResolvedParty(this);
        TrackedPartyRegistry.Instance?.DeregisterActive(this);
    }

    /// <summary>On full resolution, hand a per-raid record to RunStats for the day-end summary.</summary>
    private void RecordRaidSummary()
    {
        int slain = 0, fled = 0, breachedCount = 0, stolen = 0, recovered = 0;
        foreach (var m in Members)
        {
            if (m.escaped) { fled++; stolen += m.lootValue; }
            else { if (m.breached) breachedCount++; else slain++; recovered += m.lootValue; }
        }

        RunStats.Instance?.RecordRaid(new RaidRecord
        {
            label = TrackedPartyRegistry.LabelFor(this),
            slain = slain,
            fled = fled,
            breached = breachedCount,
            stolen = stolen,
            recovered = recovered,
            notorietyDelta = notorietyDelta,
        });
    }
}

/// <summary>One member of a party, for formation and named-party tracking. Populated at spawn.</summary>
public class PartyMember
{
    public AdventurerType type;
    public CombatClass combatClass;
    public string name;
    public bool named;
    public bool escaped;
    public bool breached;
    public int lootValue;
    public bool resolved;
}