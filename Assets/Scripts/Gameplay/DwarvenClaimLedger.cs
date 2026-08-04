using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The price of taking dwarven ground -- rungs 2 and 3 of canon 19's warning
/// ladder, and the standing penalty the whole ladder was waiting on.
///
/// WHY IT IS PER CELL. Canon asked for claiming that is "segmented, never
/// binary", so that standing loss scales with how much is taken and a two-tile
/// corridor grab stays viable. The obvious implementation was to bill on
/// TerrainFeatureGenerator.IsRoadSegmentHeld, which the toll already uses -- and
/// it is the wrong test for a penalty. A reveal segment is about forty centreline
/// cells widened to five, near enough two hundred cells, while InfluenceChannel
/// is a swelling boundary with corridorHalfWidth 6: a push aimed across a road
/// takes roughly a dozen cells and stops. Billing on the held test would have
/// produced a ladder that fired for nothing short of a deliberate two-hundred-cell
/// campaign, which is binary claiming wearing a segment's clothes. Per cell, the
/// crossing costs about -0.6 standing and a whole stretch costs -10, which is the
/// scaling canon actually described.
///
/// WHY THE COURTYARD COSTS THE SAME. A living hold spans three claim costs --
/// masonry at 9x, paved carriageway at 8x, carved interior at 3x -- and the road
/// runs THROUGH the outpost rather than past it, so the cheapest ground in the
/// hold is reachable without ever paying 9x. Rung 1 is therefore quiet exactly
/// where the granite shouts loudest, and the ladder would have been inverted:
/// told first, then not felt. The standing bill is per DWARVEN CELL and takes no
/// interest in which of the three terrains it is, so the courtyard costs what the
/// wall costs even though it digs easier. The holdings registry already maps a
/// living site's whole footprint to the site, so this is one probe, not a
/// three-way test.
///
/// THE ONE FREE WARNING. Canon: the player gets one free warning before anything
/// irreversible. The FIRST dwarven cell ever claimed, on any floor, costs no
/// standing at all and raises the Warning alert instead. Every cell after it
/// bills. Free-per-floor and free-per-segment were both rejected -- either would
/// make a wide, shallow grab free forever, which is precisely the play the
/// penalty exists to price.
///
/// Pure static, DwarvenSpoil's pattern: no scene object to wire, no singleton to
/// race in OnEnable (canon Appendix D), and statics reset in ResetForNewGame.
/// Driven from TileInfluenceManager.ClaimTile's non-silent path, alongside
/// PatternDiscovery.NotifyTerrainClaimed -- a direct call rather than a
/// subscription, which is the shipped precedent for exactly this and is immune
/// to the subscription race.
/// </summary>
public static class DwarvenClaimLedger
{
    /// <summary>Standing the Deep Holds lose per cell of their ground taken.
    ///
    /// Sized against the numbers already in play rather than picked. They start
    /// at +15 and Tier 1 sits at -20, so there are 35 points of headroom; a whole
    /// two-hundred-cell stretch costs -10, which is three and a half stretches
    /// from a clean start to the tier that silences the road. A robbery is -25
    /// and stays the sharper act, which it should: taking a wagon is a crime,
    /// taking the road is a policy.</summary>
    public const float StandingPerCell = 0.05f;

    // The free warning is spent once per dungeon, not once per floor: a floor
    // gate would hand out a second free lesson on every descent.
    private static bool freeWarningSpent;

    // Rung 2 speaks once ever. It is the wisp explaining what the resistance the
    // player has already felt actually means, and it does not bear repeating.
    private static bool pressureWarned;

    // Owners already alerted about, as "floorIndex:ownerKey". A road segment and
    // a site both number from zero and the registry separates them by sign, so
    // the floor index has to be in the key or two floors would share one entry.
    private static readonly HashSet<string> alertedOwners = new HashSet<string>();

    public static bool FreeWarningSpent => freeWarningSpent;
    public static bool PressureWarned => pressureWarned;
    public static int AlertedOwnerCount => alertedOwners.Count;

    /// <summary>Every owner alerted about, for the save and for the report.</summary>
    public static List<string> AlertedOwnersForSave() => new List<string>(alertedOwners);

    public static void ResetForNewGame()
    {
        freeWarningSpent = false;
        pressureWarned = false;
        alertedOwners.Clear();
    }

    public static void RestoreFromSave(bool warned, bool pressure, List<string> owners)
    {
        freeWarningSpent = warned;
        pressureWarned = pressure;
        alertedOwners.Clear();
        if (owners == null) return;
        foreach (var o in owners)
            if (!string.IsNullOrEmpty(o)) alertedOwners.Add(o);
    }

    /// <summary>Rung 2. The push accrues PRESSURE on a frontier cell before it
    /// claims it, so this is the one signal in the system that lands while the
    /// decision is still reversible. Called from InfluenceChannel; cheap enough
    /// to call per frame because it returns on a bool the moment it has spoken
    /// once.</summary>
    public static void NotifyPressureOnHoldings()
    {
        if (pressureWarned) return;
        pressureWarned = true;
        WispCompanion.Instance?.Speak("road_claim_warn");
    }

    /// <summary>Rung 3, and the bill. Called for every non-silent claim; returns
    /// immediately on ground that is not dwarven, which is almost all of it.</summary>
    public static void NotifyCellClaimed(FloorRoot floor, Vector3Int cell)
    {
        if (floor == null) return;
        var map = floor.TerrainTypeMap;
        if (map == null || !map.HasHoldings) return;

        int owner = map.HoldingOwnerAt(cell);
        if (owner == TerrainTypeMap.NoHoldingOwner) return;

        string key = floor.FloorIndex + ":" + owner;
        bool firstOfThisOwner = !alertedOwners.Contains(key);
        Vector3 where = floor.TileInfluence != null
            ? floor.TileInfluence.CellToWorld(cell)
            : Vector3.zero;

        if (!freeWarningSpent)
        {
            // The one free warning. No standing is taken and no ratchet can fire
            // from it, so the player has been told before anything irreversible
            // -- which is the whole promise of the ladder.
            freeWarningSpent = true;
            alertedOwners.Add(key);
            AlertsLog.Instance?.AddAlert(
                DescribeFirst(owner), where, floor.FloorIndex,
                AlertCategory.Threat, AlertSeverity.Warning);
            WispCompanion.Instance?.Speak("road_claim_first");
            return;
        }

        FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -StandingPerCell);

        if (!firstOfThisOwner) return;
        alertedOwners.Add(key);
        AlertsLog.Instance?.AddAlert(
            DescribeRepeat(owner), where, floor.FloorIndex,
            AlertCategory.Threat, AlertSeverity.Critical);
    }

    // One alert per OWNER, never per cell. A stretch is two hundred cells and a
    // hold is more; alerting per cell would bury the ticker under the very act
    // the ticker is meant to make legible.
    private static string DescribeFirst(int ownerKey)
        => TerrainTypeMap.OwnerIsRoad(ownerKey)
            ? "I have set my edge on their road. They have not answered yet -- but they will, "
              + "and every stone after this one is counted."
            : "I have set my edge inside their hold. They have not answered yet -- but they will, "
              + "and every stone after this one is counted.";

    private static string DescribeRepeat(int ownerKey)
        => TerrainTypeMap.OwnerIsRoad(ownerKey)
            ? "Another stretch of the deep road is mine. The Holds keep ledgers, and this one is open."
            : "I press into a hold that is still lived in. The Holds keep ledgers, and this one is open.";
}
