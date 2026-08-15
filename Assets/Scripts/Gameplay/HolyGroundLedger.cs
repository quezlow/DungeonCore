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
/// THE PRICE, AND IT IS HEART-ONLY. Ten when a seal's heart goes, twenty-five
/// for the dead core vault, and NOTHING at all for the ground around them.
///
/// The per-cell bill shipped and was wrong by roughly four times, which is worth
/// recording because the measurement is not obvious from the call site. A site's
/// carved interior is opened by MarkNaturalFloor on REVEAL, which bypasses the
/// mine path entirely -- so only MASONRY was ever billable, and a 21-cell seal
/// carries 200 to 280 masonry cells. At half a point each that is minus 100 to
/// minus 140 to clear ONE seal, against a Holy Order trigger wanting minus
/// forty, with some fourteen seals in a run. The old comment's "about thirty"
/// described a seal nobody could actually mine.
///
/// Ten against killPeaceful at eight reads correctly instead: taking an altar is
/// a shade worse than killing a pilgrim, and four altars bring the Order. The
/// edges are free now, which is what the weighting always claimed. Nothing is
/// wired into HolyOrderStrike directly: it already reads alignment, and a
/// separate notoriety bump would be a second number saying the same thing.
///
/// THE HEART IS WHERE THE REWARD IS. Canon 20 has seals warding rebirth sites,
/// so breaking one hands back a buried discovery through
/// BuriedRemainsController.GrantExternalDiscovery -- an entry point whose own
/// doc comment named this arc long before it had a caller. The edges of a seal
/// cost and give nothing; the middle is the act.
///
/// AND THE VAULT PAYS PROPERLY. On top of that discovery the dead core vault
/// hands back sixty research and a full level of XP. One per dungeon, seventy-
/// five cells across, on the oldest ground in the game, and built -- canon 20 --
/// around a dead core, which is to say around what the player is. The XP grant
/// is XPToNextLevel, the WHOLE threshold rather than the remainder, so a player
/// near the top of the bar banks the overflow instead of being shortchanged for
/// having earned it.
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
    /// <summary>Alignment when an ordinary seal's heart goes -- the ONLY charge
    /// there is. The per-cell constant was DELETED rather than zeroed, so that
    /// nobody can turn it back on without reading why it went: site interiors
    /// never reach the mine path, so it only ever billed masonry, at four times
    /// the intended weight. See the class summary for the arithmetic.</summary>
    public const float AlignmentForHeart = 10f;

    /// <summary>The dead core vault, priced as the larger act it is. Still
    /// short of three peaceful murders (killPeaceful is eight), because there
    /// is exactly one vault in a dungeon and breaking it is content rather
    /// than a trap.</summary>
    public const float AlignmentForVaultHeart = 25f;

    /// <summary>Research handed back with the vault's heart. Sized against the
    /// tree it spends into rather than picked round: LitanyofGraves at 60 is the
    /// dearest node in the game and TheSearingGlance -- tier 3, and the Light
    /// core's own trap -- is 30. So this is one top node, or two tier-3 ones.
    /// The buried-remains duplicate fallback pays 10, which is the register a
    /// whole vault has to clear by a distance.</summary>
    public const int VaultResearchPoints = 60;

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

        // Nothing is charged here. Hallowed ground is free to hold AND free to
        // chew; the whole bill is at the heart, which is the cell this method
        // is really waiting for.
        var features = floor.FeatureGenerator;
        var site = features != null ? features.GetSiteById(siteId) : null;
        if (site == null || site.heartCell == null) return;
        if (site.heartCell.ToVector3Int() != cell) return;

        string key = floor.FloorIndex + ":" + siteId;
        if (!brokenSeals.Add(key)) return;      // already paid out; a reload cannot repeat it

        // The vault is not a seal. It is priced, announced and rewarded apart
        // from one, and this bool is the single place that decision is taken.
        bool isVault = site.archetype == SiteArchetype.DeadCoreVault;

        AlignmentSystem.Instance?.Desecrate(
            isVault ? AlignmentForVaultHeart : AlignmentForHeart);

        Vector3 where = floor.TileInfluence != null
            ? floor.TileInfluence.CellToWorld(cell)
            : Vector3.zero;

        // Warning for the first seal ever, Critical for each after -- the player
        // has been told once at ordinary volume before anything raises a banner.
        // The VAULT is always Critical: there is one in the dungeon, and it is
        // not something to learn about from a quiet row in the log.
        var severity = (isVault || firstBreakDone)
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;
        firstBreakDone = true;

        AlertsLog.Instance?.AddAlert(
            DescribeBreak(site.archetype), where, floor.FloorIndex,
            AlertCategory.Threat, severity);

        WispCompanion.Instance?.Speak(isVault ? "holy_break_vault" : "holy_break");

        // Canon 20: the seals ward rebirth sites. Breaking one gives back what
        // it was drawn around, through the entry point whose doc comment named
        // this arc before it had a caller.
        BuriedRemainsController.Instance?.GrantExternalDiscovery(where, floor.FloorIndex);

        if (isVault)
        {
            var core = DungeonCore.Instance;
            if (core != null)
            {
                core.AddResearch(VaultResearchPoints);

                // XPToNextLevel is the FULL threshold for the current level, not
                // the remainder, so this grants exactly one level's worth however
                // far up the bar the player already stood. That is deliberate:
                // paying the remainder would have given a player at ninety per
                // cent a tenth of what it gave one at zero, for the same act.
                //
                // Nothing is lost to the overflow. CheckLevelUp neither loops nor
                // levels -- it raises LevelUpAvailable and the player confirms --
                // and ConfirmLevelUp subtracts exactly one threshold before
                // re-checking against the next, so the surplus is banked.
                core.AddXP(core.XPToNextLevel);
            }

            // THE TEETH. Canon 42: breaking the vault heart escalates floor
            // index 4 saturation. Until now this branch was the largest reward
            // in the game against a price of -25 alignment and nothing else.
            //
            // It lands HERE rather than anywhere earlier in the method because
            // brokenSeals has already refused a repeat by this point, so the
            // escalation cannot be re-triggered by re-mining the cell or by
            // reloading onto it.
            DeadCoreSaturation.Instance?.NotifyVaultHeartBroken();
        }

        DeedsController.Instance?.NotifyMoment(CoreMemory.FirstDesecration);
        CoreMemory.Recall(CoreMemory.FirstDesecration);
    }

    private static string DescribeBreak(SiteArchetype a)
    {
        switch (a)
        {
            case SiteArchetype.DeadCoreVault:
                return "The vault stone is broken. A core lies dead beneath it, "
                     + "and everything it learned before the end is mine.";
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
