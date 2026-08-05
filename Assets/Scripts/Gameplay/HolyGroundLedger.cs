using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unsealing: what it costs to break a Church seal, and what it gives back.
/// Canon 18/20 -- and the caller AlignmentSystem.Desecrate has waited for since
/// the alignment system shipped.
///
/// CLAIMING TEACHES, MINING DESECRATES, and the split is the whole design.
/// hallowed_stone pays out on CLAIM through PatternDiscovery, which has been
/// wired since the terrain system shipped and costs nothing. That matters more
/// than it looks: trap_blinding_flash is LIGHT affinity, so the core with the
/// most to lose from a low alignment is the one whose exclusive elemental trap
/// sits behind this pattern. Paying for it in alignment would have forced a
/// Light core to go dark to reach its own content. Hold the ground and you
/// learn the stone; break the stone and you answer for it.
///
/// THE PRICE. Half a point per hallowed cell mined, ten more when the HEART
/// goes. A whole seal runs to about thirty against killPeaceful at eight and a
/// Holy Order trigger wanting alignment at or below minus forty -- four peaceful
/// murders' worth for a seal, which is the register this belongs in. Nothing is
/// wired into HolyOrderStrike directly: it already reads alignment, and a
/// separate notoriety bump would be a second number saying the same thing.
///
/// THE HEART IS WHERE THE REWARD IS. Canon 20 has seals warding rebirth sites,
/// so breaking one hands back a buried discovery through
/// BuriedRemainsController.GrantExternalDiscovery -- an entry point whose own
/// doc comment named this arc long before it had a caller. The edges of a seal
/// cost and give nothing; the middle is the act.
///
/// Pure static, DwarvenSpoil's and DwarvenClaimLedger's pattern: no scene object
/// to wire, no singleton to race in OnEnable (canon Appendix D), statics reset
/// in ResetForNewGame. Driven by DIRECT CALLS from TileInfluenceManager's live
/// claim and mine paths, not subscriptions -- a handler that lost the
/// subscription race would fail silently and give the seals away free, which is
/// the bug class the appendix exists for. Save-restore claims and mines are
/// silent and never reach here, so a reload cannot re-bill or re-reward.
/// </summary>
public static class HolyGroundLedger
{
    /// <summary>Alignment per cell of hallowed ground mined.</summary>
    public const float AlignmentPerCell = 0.5f;

    /// <summary>Alignment on top when the heart itself goes. Weighted so the
    /// edges are cheap and the middle is the decision: a player who chews the
    /// corner off a seal has done something small, and a player who takes the
    /// altar has done the thing.</summary>
    public const float AlignmentForHeart = 10f;

    // One murmur ever. It is the wisp naming what the cold blue is, and it does
    // not bear repeating once named.
    private static bool touchMurmured;

    // The first seal broken anywhere gets the Warning; every one after is
    // Critical. Same ladder shape as the road, on the same severity layer.
    private static bool firstBreakDone;

    // Seals whose heart is already gone, as "floorIndex:siteId". Persisted so a
    // reload cannot pay the discovery out twice.
    private static readonly HashSet<string> brokenSeals = new HashSet<string>();

    public static bool TouchMurmured => touchMurmured;
    public static bool FirstBreakDone => firstBreakDone;
    public static int BrokenSealCount => brokenSeals.Count;
    public static List<string> BrokenSealsForSave() => new List<string>(brokenSeals);

    public static void ResetForNewGame()
    {
        touchMurmured = false;
        firstBreakDone = false;
        brokenSeals.Clear();
    }

    public static void RestoreFromSave(bool murmured, bool broken, List<string> seals)
    {
        touchMurmured = murmured;
        firstBreakDone = broken;
        brokenSeals.Clear();
        if (seals == null) return;
        foreach (var s in seals)
            if (!string.IsNullOrEmpty(s)) brokenSeals.Add(s);
    }

    /// <summary>The warning before the act. Fires the first time the player
    /// CLAIMS hallowed ground, which is the moment they have committed to the
    /// ground but not yet to the stone -- claiming costs nothing here, so there
    /// is still a free decision in front of them. Cheap to call per claim: it
    /// returns on a bool the moment it has spoken.</summary>
    public static void NotifyCellClaimed(FloorRoot floor, Vector3Int cell)
    {
        if (touchMurmured || floor == null) return;
        var map = floor.TerrainTypeMap;
        if (map == null || !map.HasHolySites || !map.IsHolyCell(cell)) return;

        touchMurmured = true;
        WispCompanion.Instance?.Speak("holy_first_touch");
    }

    /// <summary>The act. Called for every live dig; returns immediately on
    /// ground that is not hallowed, which is very nearly all of it.</summary>
    public static void NotifyCellMined(FloorRoot floor, Vector3Int cell)
    {
        if (floor == null) return;
        var map = floor.TerrainTypeMap;
        if (map == null || !map.HasHolySites) return;

        int siteId = map.HolySiteAt(cell);
        if (siteId == TerrainTypeMap.NoHoldingOwner) return;

        AlignmentSystem.Instance?.Desecrate(AlignmentPerCell);

        var features = floor.FeatureGenerator;
        var site = features != null ? features.GetSiteById(siteId) : null;
        if (site == null || site.heartCell == null) return;
        if (site.heartCell.ToVector3Int() != cell) return;

        string key = floor.FloorIndex + ":" + siteId;
        if (!brokenSeals.Add(key)) return;      // already paid out; a reload cannot repeat it

        AlignmentSystem.Instance?.Desecrate(AlignmentForHeart);

        Vector3 where = floor.TileInfluence != null
            ? floor.TileInfluence.CellToWorld(cell)
            : Vector3.zero;

        // Warning for the first ever, Critical for each after. The player has
        // been told once at ordinary volume before anything raises a banner.
        var severity = firstBreakDone ? AlertSeverity.Critical : AlertSeverity.Warning;
        firstBreakDone = true;

        AlertsLog.Instance?.AddAlert(
            DescribeBreak(site.archetype), where, floor.FloorIndex,
            AlertCategory.Threat, severity);

        WispCompanion.Instance?.Speak("holy_break");

        // Canon 20: the seals ward rebirth sites. Breaking one gives back what
        // it was drawn around, through the entry point whose doc comment named
        // this arc before it had a caller.
        BuriedRemainsController.Instance?.GrantExternalDiscovery(where, floor.FloorIndex);

        DeedsController.Instance?.NotifyMoment(CoreMemory.FirstDesecration);
        CoreMemory.Recall(CoreMemory.FirstDesecration);
    }

    private static string DescribeBreak(SiteArchetype a)
    {
        switch (a)
        {
            case SiteArchetype.SealedCrypt:
                return "The capping slab is off. Whatever they shut in here, they shut in here for a reason.";
            case SiteArchetype.WardChapel:
                return "The altar is broken open. Their rite was administered from this stone, and it is mine now.";
            case SiteArchetype.BlessedSpring:
                return "The cap is lifted and the water remembers. They stopped this spring; I have started it.";
            default:
                return "The seal-stone is broken. Whatever the Church bound here is no longer bound.";
        }
    }
}
