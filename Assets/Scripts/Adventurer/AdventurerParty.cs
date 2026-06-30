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
}