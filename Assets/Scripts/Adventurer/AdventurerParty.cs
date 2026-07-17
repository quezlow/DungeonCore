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
    public bool tributeAssigned = false;               // one bearer per Pilgrim/Cultist party
    public readonly List<PartyMember> Members = new();

    /// <summary>True if any member is named. Named parties are permanent
    /// nemeses and cannot be unpinned.</summary>
    public bool HasNamedMember()
    {
        foreach (var m in Members) if (m != null && m.named) return true;
        return false;
    }
    public float notorietyDelta = 0f;                  // net notoriety this party caused (raid summary)
    private int resolvedCount = 0;
    private bool fractured = false;                    // morale breaks once per party

    // ── Party banner ────────────────────────────────────
    public bool hasBanner = false;             // guards one banner per party
    public int bannerColorIndex = -1;          // pinned-pool index (persisted); -1 = intent-coloured
    public string bannerLabelOverride;         // forces the banner text (e.g. a vengeance "House X"); null = default label
    public bool isClimax = false;              // the endgame climax host - resolving it ends the trial; never returns as a nemesis
    private readonly List<DungeonAdventurer> live = new();
    /// <summary>Live instances in this party (read-only) - used to grade-scale a fresh team.</summary>
    public IReadOnlyList<DungeonAdventurer> LiveMembers => live;

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
        FactionIntel.NotifyEncounter(AdventurerTypeInfo.FactionOf(type)); 
        return m;
    }

    // ── Save / restore (live persistence) ───────────────
    /// <summary>Captures the whole party — flags plus every roster member, with the
    /// live state of those still in the dungeon — for a mid-raid save.</summary>
    public LivePartySaveData CaptureSaveState()
    {
        var s = new LivePartySaveData
        {
            intent = (int)Intent,
            tracked = tracked,
            bannerColorIndex = bannerColorIndex,
            bannerLabelOverride = bannerLabelOverride,
            isClimax = isClimax,
            exitBonusApplied = exitBonusApplied,
            tributeAssigned = tributeAssigned,
            fractured = fractured,
            notorietyDelta = notorietyDelta,
        };
        foreach (var m in Members)
        {
            var rec = new LiveMemberSaveData
            {
                type = (int)m.type,
                combatClass = (int)m.combatClass,
                affinity = (int)m.affinity,
                trait = (int)m.trait,
                name = m.name,
                named = m.named,
                resolved = m.resolved,
                escaped = m.escaped,
                breached = m.breached,
                lootValue = m.lootValue,
                xp = m.xp,
                grudgeMonster = m.grudgeMonster,
            };
            if (!m.resolved)
            {
                var adv = FindLive(m);
                if (adv != null) { adv.CaptureLiveState(rec); rec.isLive = true; }
            }
            s.members.Add(rec);
        }
        return s;
    }

    private DungeonAdventurer FindLive(PartyMember m)
    {
        foreach (var a in live) if (a != null && a.Member == m) return a;
        return null;
    }

    /// <summary>Re-applies the party-level flags after construction (see AdventurerSpawner).</summary>
    public void ApplyRestoredState(LivePartySaveData s)
    {
        tracked = s.tracked;
        bannerColorIndex = s.bannerColorIndex;
        bannerLabelOverride = s.bannerLabelOverride;
        isClimax = s.isClimax;
        exitBonusApplied = s.exitBonusApplied;
        tributeAssigned = s.tributeAssigned;
        fractured = s.fractured;
        notorietyDelta = s.notorietyDelta;
    }

    /// <summary>Re-adds a member that had already died or fled at save time, so the roster
    /// size, morale threshold and raid summary stay correct.</summary>
    public void AddResolvedMember(LiveMemberSaveData rec)
    {
        var m = new PartyMember
        {
            type = (AdventurerType)rec.type,
            combatClass = (CombatClass)rec.combatClass,
            affinity = (DungeonType)rec.affinity,
            trait = (BehaviourTrait)rec.trait,
            name = rec.name,
            named = rec.named,
            xp = rec.xp,
            grudgeMonster = rec.grudgeMonster,
            resolved = true,
            escaped = rec.escaped,
            breached = rec.breached,
            lootValue = rec.lootValue,
        };
        Members.Add(m);
        resolvedCount++;
    }

    /// <summary>Raised once per member who leaves the dungeon alive. The
    /// camp-growth layer listens: escapees settle the surface camps.</summary>
    public static event System.Action<AdventurerParty, PartyMember> MemberEscaped;

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

        if (escaped) MemberEscaped?.Invoke(this, member);

        // A slain member may break a combat party's nerve. The fraction needed to
        // break varies with disposition — cowards bolt early, the bold hold on.
        if (!escaped && !breached) CheckMoraleFracture();
        if (!escaped && !breached) BanterLines.ReactPartyDeath(this, member);

        if (resolvedCount < Members.Count || Members.Count == 0) return;

        RecordRaidSummary();

        if (tracked) TrackedPartyRegistry.Instance?.RecordResolvedParty(this);
        TrackedPartyRegistry.Instance?.DeregisterActive(this);
        DungeonSaveController.Instance?.RequestAutosave();
        if (isClimax) EndgameClimax.Instance?.OnClimaxThreatResolved();
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

    // ── Morale ──────────────────────────────────────────
    /// <summary>A death may shatter a fighting party's resolve. Only Destroyer-intent
    /// parties fracture; the survivors (bar Heroes and the Suicidal) turn and flee.</summary>
    private void CheckMoraleFracture()
    {
        if (fractured) return;
        if (Intent != PartyIntent.Destroyer) return;   // only parties here to fight
        if (Members.Count == 0) return;

        int slain = 0;
        foreach (var m in Members)
            if (m.resolved && !m.escaped && !m.breached) slain++;

        if ((float)slain / Members.Count < FractureThreshold()) return;

        fractured = true;

        Vector3 pos = Vector3.zero;
        var lead = CurrentLead();
        if (lead != null) pos = lead.transform.position;

        foreach (var a in live)
        {
            if (a == null) continue;
            if (a.Type == AdventurerType.Hero || a.Type == AdventurerType.Suicidal) continue;
            a.ForceRetreat();
        }

        AlertsLog.Instance?.AddAlert("Their nerve breaks. The survivors turn and run.", pos, -1, AlertCategory.Combat);
    }

    /// <summary>The party's collective breaking point — the mean of its members'
    /// dispositions. Cowards pull it down; the aggressive push it up.</summary>
    private float FractureThreshold()
    {
        float sum = 0f; int n = 0;
        foreach (var m in Members) { sum += MoraleBreakFraction(m.trait); n++; }
        return n > 0 ? sum / n : 0.5f;
    }

    // Fraction of the party that must fall before a member of each disposition loses heart.
    private static float MoraleBreakFraction(BehaviourTrait t) => t switch
    {
        BehaviourTrait.Cowardly => 0.25f,
        BehaviourTrait.Cautious => 0.4f,
        BehaviourTrait.Aggressive => 0.7f,
        _ => 0.5f,   // Balanced (and any default)
    };
}

/// <summary>One member of a party, for formation and named-party tracking. Populated at spawn.</summary>
public class PartyMember
{
    public AdventurerType type;
    public CombatClass combatClass;
    public DungeonType affinity;
    public BehaviourTrait trait;   // this member's disposition (drives morale)
    public int xp;                 // cumulative kill XP (persisted via TrackedMember)
    public string name;
    public bool named;
    public bool escaped;
    public bool breached;
    public int lootValue;
    public bool resolved;
    public string grudgeMonster;   // worst offender this raid; carried home by survivors
}