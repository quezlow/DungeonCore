using System.Collections.Generic;
using UnityEngine;

/// <summary>One placed site: an archetype, the plan variant it was built from,
/// its anchor, and the two cell sets that make it read as architecture.</summary>
/// <summary>
/// How a placed site met the road: which chord it answered to, the two gate
/// cells a road threads it by, and the authored lane between them in world
/// space.
///
/// Carried on the placed site rather than acted on at seating time because a
/// seat that succeeds can still be thrown out afterwards -- by the disc clamp,
/// the twelve-cell floor, or the walkability guard -- and a chord split for a
/// site that was never placed would leave the network cut around nothing.
/// </summary>
public class SiteChordSeat
{
    public int chordIndex = -1;
    public Vector3Int gateIn, gateOut;
    public Vector2Int inNormal, outNormal;
    public bool hasBothGates;

    /// <summary>A spur-class seat: the site stands off the chord and a spur is
    /// emitted from `takeoff` to `gateIn`, arriving along the door's normal.
    /// Mutually exclusive with hasBothGates.</summary>
    public bool spur;
    public Vector3Int takeoff;

    /// <summary>The lane walked gate to gate, in world cells.</summary>
    public List<Vector3Int> laneCells = new List<Vector3Int>();
}

public class AncientSitePlan
{
    public int id;

    /// <summary>Null on every procedural site and on any authored plan with no
    /// lane or only one usable gate.</summary>
    public SiteChordSeat chordSeat;
    public SiteArchetype archetype;
    public int variant;

    /// <summary>The authored plan's @name; "" for procedural plans. Carried into
    /// SiteData for the decor-prefab lookup.</summary>
    public string planName = "";

    public Vector3Int anchor;

    /// <summary>Carved interior. Registered as natural floor on reveal, exactly
    /// as a chamber is, so the wall renderer frames it and units can walk it.</summary>
    public List<Vector3Int> cells = new List<Vector3Int>();

    /// <summary>The masonry. Deliberately NOT carved -- these cells stay solid
    /// rock and are merely retyped to TerrainType.Ruins, so they render as wall,
    /// cost 8x to claim, and pay out the ancient_masonry pattern when mined.
    /// Straight walls against organic cellular-automata chambers is the whole of
    /// what makes a site read as built rather than found.</summary>
    public List<Vector3Int> ruinsCells = new List<Vector3Int>();

    /// <summary>The heart cell in WORLD space, once placed. Default
    /// (0,0,0) means the plan declared none -- every procedural recipe,
    /// and any authored plan without an 'X'. Callers test
    /// hasHeart rather than comparing against zero, because the origin
    /// is a legitimate cell on a floor centred there.</summary>
    public Vector3Int heartCell;
    public bool hasHeart;

    public bool reservedForOutpost;

    /// <summary>The guaranteed village, when SiteFloorEntry.reserveVillage placed
    /// this plan. Carried into SiteData so DwarvenVillageController can find its
    /// site the same way the outpost controller finds the hold.</summary>
    public bool reservedForVillage;

    /// <summary>Declared doors in WORLD space, once placed. Truncation reads
    /// these to leave the road a way in: a road that meets a site with a door
    /// runs TO the door rather than stopping at the footprint.
    ///
    /// Emitted by EVERY placement path now rather than by PlaceDeadCore alone.
    /// Truncation still reads only the vault's, because it is floor-index-4
    /// gated; the rest is data laid down for the routing half, which has to know
    /// where a laned site's other gates ended up without re-deriving the
    /// transform that put them there.</summary>
    public List<PlacedDoor> doors = new List<PlacedDoor>();
}

/// <summary>A door run after placement: where its middle sits, which way it
/// opens, and how wide it is. Rotation and mirroring are already applied.</summary>
public struct PlacedDoor
{
    public Vector3Int mid;
    public Vector2Int outward;
    public int halfWidth;
}

public class AncientSiteResult
{
    public List<AncientSitePlan> sites = new List<AncientSitePlan>();

    /// <summary>Why placement attempts were thrown away, so "no sites spawned"
    /// can be answered with a number instead of a guess. Every `continue` in the
    /// placement loop increments exactly one of these.</summary>
    public int wanted;
    public int planPoolSize;
    public int attempts;
    public int rejectedNoAnchor;
    public int rejectedTooClose;
    public int rejectedNullShape;
    public int rejectedTooSmall;
    public int rejectedUnwalkable;

    /// <summary>Attempts thrown away because the plan asked to be anchored on a
    /// door and the anchor answered to no chord -- a free in-band pick, or a
    /// junction node with no chord touching it.
    ///
    /// No longer a facing failure. The cone is gone: against a chord the site
    /// TURNS to face the road, so every bearing is servable and nothing is
    /// refused for pointing the wrong way. What is left is the case where there
    /// is no road to face at all.
    ///
    /// Its own counter rather than a share of rejectedTooClose, because the two
    /// have opposite fixes. A floor starving for spacing wants minSpacing or the
    /// band changed; a floor starving here wants more chords in the band.</summary>
    public int rejectedNoDoorHeading;

    /// <summary>Sites in `sites` that do NOT count against `wanted`: the holy
    /// sub-quota and the dead core vault.
    ///
    /// The fill loop's condition is `sites.Count - extraPlaced` against
    /// `wanted`, and this field is the whole reason it is not simply
    /// `sites.Count`. The guarantees add to `sites` BEFORE the loop reads its
    /// target, so floor index 4 reported thirteen sites INCLUDING its vault --
    /// the vault had silently displaced a ruin on the one floor authored to be
    /// full. The outpost and the village still count toward `wanted`
    /// deliberately: the gatehouse floor is authored as "the hold plus at most
    /// one ruin", and that reading depends on it.</summary>
    public int extraPlaced;

    // The holy pass keeps its own tallies. Folding them into the general ones
    // would make "this floor placed four seals of six" unanswerable: a floor can
    // fill its ruins and starve its seals, and that is exactly the failure worth
    // being able to see.
    public int holyWanted;
    public int holyPlanPoolSize;
    public int holyPlaced;
    public int holyAttempts;
    public int holyRejectedTooClose;
    public int holyRejectedNullShape;
    public int holyRejectedTooSmall;
    public int holyRejectedUnwalkable;
    public int holyRejectedNoDoorHeading;
    /// <summary>Whether this floor's guaranteed outpost actually landed. False on
    /// a floor that never asked for one; false AND loud on a floor that did.</summary>
    public bool outpostPlaced;

    /// <summary>Same contract for the guaranteed village.</summary>
    public bool villagePlaced;

    /// <summary>The @name of the hold the seeded roll chose -- the report
    /// prints it, so rotation variety is verifiable headlessly by stepping
    /// the report seed instead of walking the map.</summary>
    public string villagePlanPicked = "";

    /// <summary>Whether the guaranteed vault landed. False on a floor that never
    /// asked; false AND loud on a floor that did.</summary>
    public bool deadCorePlaced;

    /// <summary>Which vault the seeded roll chose, for the report.</summary>
    public string deadCorePlanPicked = "";
    public int inBandJunctions;
    public int inBandRoadCells;
    public int inBandRoadEnds;
    public string abortReason = "";

    /// <summary>Appended to the headless report so a missing outpost is visible
    /// there rather than only on screen, hundreds of days into a run.</summary>
    public string OutpostSummary() =>
        outpostPlaced ? "outpost: placed" : "outpost: NONE";

    /// <summary>Same shape for the village, so the site report prints both.
    /// Names the chosen hold: with several villages in rotation, this line is
    /// how variety gets verified.</summary>
    public string VillageSummary() =>
        villagePlaced
            ? "village: placed (" + villagePlanPicked + ")"
            : "village: NONE";

    public string Summary()
    {
        if (!string.IsNullOrEmpty(abortReason))
            return "aborted before placing: " + abortReason;
        return $"placed {sites.Count - extraPlaced}/{wanted} general (plus " +
               $"{extraPlaced} outside the budget, {sites.Count} in all) from a " +
               $"pool of {planPoolSize} plans in {attempts} attempts; rejected: " +
               $"no-anchor {rejectedNoAnchor}, too-close {rejectedTooClose}, " +
               $"null-shape {rejectedNullShape}, too-small {rejectedTooSmall}, " +
               $"unwalkable {rejectedUnwalkable}, " +
               $"no-door-heading {rejectedNoDoorHeading}. " +
               $"In band: {inBandJunctions} junctions, {inBandRoadCells} road samples, " +
               $"{inBandRoadEnds} road ends";
    }

    /// <summary>The holy pass, printed apart from the general one. A floor that
    /// rolled no seals says so rather than reporting zeroes that read as
    /// failures -- the same distinction SitePlacementSkip exists for.</summary>
    public string HolySummary() =>
        holyWanted == 0
            ? "holy: none asked for"
            : $"holy: placed {holyPlaced}/{holyWanted} from a pool of " +
              $"{holyPlanPoolSize} plans in {holyAttempts} attempts; rejected: " +
              $"too-close {holyRejectedTooClose}, null-shape {holyRejectedNullShape}, " +
              $"too-small {holyRejectedTooSmall}, unwalkable {holyRejectedUnwalkable}, " +
              $"no-door-heading {holyRejectedNoDoorHeading}";
}

/// <summary>
/// The Buried Age sites, as a PURE function of (seed, centre, radius, entry,
/// road anchors).
///
/// Nothing here touches a scene, a floor, a tilemap or a singleton -- the same
/// discipline RoadNetworkBuilder keeps, and for the same reason: it lets the
/// headless deep-floor report in Commands.cs generate and measure a whole
/// floor's sites without instantiating the floor, which matters on floor index
/// 4 where the terrain pass alone paints 2.26 million cells.
///
/// PLANS, NOT SHAPES
///   The no-repeat unit is the PLAN -- archetype plus variant -- not the
///   archetype. Eight archetypes at three variants is twenty-four plans, so
///   floor index 4's thirteen sites never repeat a plan even where an archetype
///   appears twice. Parametric jitter (span, rotation, mirror, breach count)
///   rides on top, so two instances of one plan are still not identical.
///
/// COMPOSITION
///   Every plan is composed in LOCAL axis-aligned integer space centred on the
///   origin, then rotated by a quarter turn and optionally mirrored before being
///   translated onto its anchor. Quarter turns and mirrors are exact integer
///   maps, so straight walls stay straight -- an arbitrary-angle rotation would
///   alias every wall into a staircase and lose the one read the sites have.
/// </summary>
public static class AncientSiteBuilder
{
    // ---- Public API ------------------------------------------------

    /// <summary>
    /// Places this floor's sites. Road anchor lists may be empty or null; every
    /// anchor preference DEGRADES to a free in-band pick rather than failing,
    /// which is what puts the lone guard post on the road-less floor index 2.
    /// </summary>
    public static AncientSiteResult Build(
        System.Random rng, Vector3Int centre, int radius,
        SiteFloorEntry entry, int coreExclusionRadius,
        RoadPlan roadPlan,
        IReadOnlyList<AuthoredSitePlan> authoredPlans = null)
    {
        var result = new AncientSiteResult();
        if (rng == null || entry == null)
        {
            result.abortReason = "no rng or no floor entry";
            return result;
        }

        int usable = Mathf.Max(0, radius - Mathf.Max(0, entry.rimMargin));
        if (usable <= coreExclusionRadius)
        {
            result.abortReason = $"usable radius {usable} (radius {radius} minus rimMargin " +
                                 $"{entry.rimMargin}) is inside the core exclusion {coreExclusionRadius}";
            return result;
        }

        int inner = Mathf.Max(coreExclusionRadius + 2, Mathf.RoundToInt(radius * Mathf.Clamp01(entry.bandInner)));
        int outer = Mathf.Min(usable, Mathf.RoundToInt(radius * Mathf.Clamp01(entry.bandOuter)));
        if (outer <= inner)
        {
            result.abortReason = $"placement band is empty: inner {inner} >= outer {outer} " +
                                 $"(radius {radius}, bandInner {entry.bandInner}, bandOuter {entry.bandOuter})";
            return result;
        }

        int want = Mathf.Max(0, RandomRange(rng, entry.minSites, entry.maxSites));
        result.wanted = want;

        int holyWant = Mathf.Max(0, RandomRange(rng, entry.minHolySites, entry.maxHolySites));
        result.holyWanted = holyWant;

        var plans = BuildPlanPool(rng, entry, authoredPlans);
        result.planPoolSize = plans.Count;

        var holyPlans = BuildHolyPlanPool(rng, entry, authoredPlans);
        result.holyPlanPoolSize = holyPlans.Count;

        // THE ABORT IS NOW ABOUT THE WHOLE FLOOR, and it had to become so.
        //
        // These were two separate early returns -- one on `want == 0`, one on an
        // empty plan pool -- and BOTH sat ahead of PlaceOutpost, PlaceVillage and
        // PlaceDeadCore. Harmless while every floor rolled ruins. Fatal the
        // moment floor index 0's ChurchSeal moved into holyPool: its general pool
        // was then empty AND its general count zero, so the floor returned before
        // the holy pass and before its guarantees, and shipped nothing at all.
        //
        // A guarantee an unrelated roster can skip is not a guarantee. The test
        // is now whether there is anything to place by ANY route, and it names
        // all three when it fires.
        bool anyGeneral = want > 0 && plans.Count > 0;
        bool anyHoly = holyWant > 0 && holyPlans.Count > 0;
        bool anyGuarantee = entry.reserveOutpost || entry.reserveVillage || entry.reserveDeadCore;
        if (!anyGeneral && !anyHoly && !anyGuarantee)
        {
            result.abortReason =
                $"nothing to place: general rolled {want} from minSites {entry.minSites} / " +
                $"maxSites {entry.maxSites} against a pool of {plans.Count}; holy rolled " +
                $"{holyWant} from minHolySites {entry.minHolySites} / maxHolySites " +
                $"{entry.maxHolySites} against a pool of {holyPlans.Count}; and the floor " +
                "reserves no outpost, village or vault. Check useAllArchetypes, pool " +
                "and holyPool.";
            return result;
        }

        var anchorsUsed = new List<Vector3Int>();
        int minSpacingSq = Mathf.Max(1, entry.minSpacing) * Mathf.Max(1, entry.minSpacing);

        // THE GUARANTEES GO DOWN LARGEST FIRST, and the ordering is measured
        // rather than assumed. From the authored plans on disk: the vault is
        // 75x75 at 5625 cells of footprint, the largest village 61x61 at 3721,
        // the outpost 39x23 at 897. The biggest set-piece is the one least
        // likely to find room once the floor has been chewed up by anchors and
        // spacing, so it picks first and the rest fit around it.
        //
        // No floor on the shipped profile carries two guarantees -- index 2 the
        // outpost, 3 the village, 4 the vault -- so today this is rng-NEUTRAL:
        // only the source order of the three tests changes and no floor executes
        // two of them. It becomes load-bearing the moment a floor wants a hold
        // AND a vault, which is precisely when nobody would think to check.

        // The vault. At 75 cells across it is the least likely thing in the game
        // to find room later, so nothing goes before it.
        if (entry.reserveDeadCore)
            PlaceDeadCore(rng, entry, centre, inner, outer, usable,
                          roadPlan,
                          authoredPlans, anchorsUsed, minSpacingSq, result);

        // The village, selected BY NAME from the authored set rather than
        // through the pool -- its archetype sits in no roster, so the fill loop
        // can never serve it and there is no pool bookkeeping to do on success.
        if (entry.reserveVillage)
            PlaceVillage(rng, entry, centre, inner, outer, usable,
                         roadPlan,
                         authoredPlans, anchorsUsed, minSpacingSq, result);

        // The outpost, which must precede the fill loop for its own reason as
        // well as for size. The old rule latched reservedForOutpost onto
        // whichever Sealed Gate the shuffled pool happened to serve, and that
        // failed two ways on the gatehouse floor: the roster holds five
        // archetypes and the floor rolls three to five sites, so a run could
        // finish with no Sealed Gate and therefore no dwarves at all; and the
        // Sealed Gate's own RoadEnd preference resolves, on a rim-to-rim trunk
        // with no broken ends, to the two rim endpoints -- both outside the
        // placement band -- so it degraded to a free pick and put the outpost
        // nowhere near the road it is supposed to hold.
        if (entry.reserveOutpost)
            PlaceOutpost(rng, entry, centre, inner, outer, usable,
                         roadPlan,
                         plans, anchorsUsed, minSpacingSq, result);

        // How much of each anchor source actually falls inside the placement band.
        // A source can be large and still be useless: road ENDS sit at the rim by
        // definition and the band stops at 65 per cent of the radius, so most of
        // them are out of bounds before spacing is ever considered.
        //
        // Hoisted above the holy pass so a floor whose seals starved still
        // reports its anchor sources, which is the first question about one.
        // Counted off the PLAN. The old counts described a thinned sample of
        // drawn cells, which is not what anchoring reads any more.
        result.inBandJunctions = CountNodesInBand(roadPlan, centre, inner, outer);
        result.inBandRoadCells = CountChordsInBand(roadPlan, centre, inner, outer);
        result.inBandRoadEnds = CountFreeEndsInBand(roadPlan, centre, inner, outer);

        // THE HOLY PASS. Before the general fill, on its own pool, its own
        // attempt budget and its own counters.
        if (anyHoly)
        {
            var holy = Fill(rng, entry, centre, inner, outer, usable,
                            roadPlan,
                            holyPlans, holyWant, HolyAttemptsPerSite,
                            anchorsUsed, minSpacingSq, result, true);
            result.holyPlaced = holy.placed;
            result.holyAttempts = holy.attempts;
            result.holyRejectedTooClose = holy.rejectedTooClose;
            result.holyRejectedNullShape = holy.rejectedNullShape;
            result.holyRejectedTooSmall = holy.rejectedTooSmall;
            result.holyRejectedUnwalkable = holy.rejectedUnwalkable;
            result.holyRejectedNoDoorHeading = holy.rejectedNoDoorHeading;

            // Loud on a shortfall against the MINIMUM rather than against the
            // roll. The seals are this arc's content, and a floor quietly
            // shipping two of five is a failure that would otherwise only be
            // found by walking the map.
            if (holy.placed < entry.minHolySites)
                Debug.LogWarning("[AncientSiteBuilder] Floor " + entry.floorIndex +
                    " placed " + holy.placed + " holy site(s) against a minimum of " +
                    entry.minHolySites + " (rolled " + holyWant + ") from a pool of " +
                    holyPlans.Count + ". " + result.HolySummary() +
                    ". The likeliest cause is minSpacing against the placement " +
                    "band -- check the site report.");
        }

        // Guarantee-only plans (authored "@general: no") never reach the fill
        // loop. A guarantee that took one already removed it; this strips
        // whatever no guarantee consumed -- the outpost's hold sitting in floor
        // index 4's all-archetypes roster being the case that motivated it.
        plans.RemoveAll(p => p.authored != null && !p.authored.generalPool);

        // THE GENERAL FILL, whose target counts the guarantees but not the
        // extras.
        var general = Fill(rng, entry, centre, inner, outer, usable,
                           roadPlan,
                           plans, want, GeneralAttemptsPerSite,
                           anchorsUsed, minSpacingSq, result, false);
        result.attempts = general.attempts;
        result.rejectedTooClose = general.rejectedTooClose;
        result.rejectedNullShape = general.rejectedNullShape;
        result.rejectedTooSmall = general.rejectedTooSmall;
        result.rejectedUnwalkable = general.rejectedUnwalkable;
        result.rejectedNoDoorHeading = general.rejectedNoDoorHeading;

        // LAST, once every site that is going to be placed has been. Splitting
        // as each site seated would have cut chords for placements that were
        // still to be thrown out.
        SplitChordsForSites(roadPlan, result);

        return result;
    }

    /// <summary>Attempts per wanted site in the general fill. Unchanged.</summary>
    private const int GeneralAttemptsPerSite = 12;

    /// <summary>Attempts per wanted site in the holy pass, and DOUBLE the general
    /// figure on purpose. The gatehouse floor asks for an outpost plus three or
    /// four seals plus a ruin inside an annulus of 75 to 162 cells at a minimum
    /// spacing of 70 -- six anchors in a band that holds perhaps ten, and the
    /// tail of that is genuinely hard to sample. A holy attempt is also cheap
    /// next to a general one: every seal is an authored plan at a fixed size, so
    /// a rejected attempt never runs Compose.</summary>
    private const int HolyAttemptsPerSite = 24;

    /// <summary>One pass of the placement loop's tallies. Returned rather than
    /// written straight onto the result, because the two passes keep separate
    /// counters and the loop body should not have to know which one it is
    /// running as.</summary>
    private struct FillTally
    {
        public int attempts;
        public int placed;
        public int rejectedTooClose;
        public int rejectedNullShape;
        public int rejectedTooSmall;
        public int rejectedUnwalkable;
        public int rejectedNoDoorHeading;
    }

    /// <summary>
    /// The placement loop, run once per pool. Extracted from Build verbatim when
    /// the holy sub-quota landed: duplicating a hundred lines of anchor,
    /// transform, clamp and walkability handling would have guaranteed the two
    /// copies drifted apart.
    ///
    /// THE TWO PASSES COUNT PROGRESS DIFFERENTLY, which is all `countsAsExtra`
    /// decides:
    ///
    ///   general (false) -- `result.sites.Count - result.extraPlaced` against
    ///       `want`. The guarantees added to `sites` BEFORE this runs, and the
    ///       outpost and the village are MEANT to count against the target: the
    ///       gatehouse floor is authored as "the hold plus at most one ruin".
    ///       Subtracting `extraPlaced` removes only the seals and the vault,
    ///       which are not ruins and must not displace one. Floor index 4
    ///       previously reported thirteen sites including its vault.
    ///
    ///   holy (true) -- its own placed count, because `sites` already holds
    ///       whatever the guarantees put there and a shared count would have the
    ///       outpost finishing the seal quota.
    /// </summary>
    private static FillTally Fill(
        System.Random rng, SiteFloorEntry entry, Vector3Int centre,
        int inner, int outer, int usable,
        RoadPlan roadPlan,
        List<PlanRef> plans, int want, int attemptsPerSite,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        AncientSiteResult result, bool countsAsExtra)
    {
        var tally = new FillTally();

        // Guarded rather than assumed: the body divides by plans.Count, and
        // floors 0 and 1 now legitimately reach here with a general pool of
        // zero.
        if (plans == null || plans.Count == 0 || want <= 0) return tally;

        int planCursor = 0;
        int maxAttempts = want * Mathf.Max(1, attemptsPerSite);

        while (tally.attempts < maxAttempts)
        {
            int progress = countsAsExtra
                ? tally.placed
                : result.sites.Count - result.extraPlaced;
            if (progress >= want) break;

            tally.attempts++;

            // The plan pool is walked in shuffled order and only wraps once it is
            // exhausted, so a floor exhausts every distinct plan before repeating.
            var plan = plans[planCursor % plans.Count];
            planCursor++;

            // An authored plan may declare its own anchor; otherwise the
            // archetype's fixed preference stands.
            var anchorKind = plan.authored != null && plan.authored.hasAnchorOverride
                ? plan.authored.anchorOverride
                : AncientSiteProfile.AnchorFor(plan.archetype);
            if (!TryPickAnchor(rng, anchorKind, centre, inner, outer,
                               roadPlan, out int chordIndex,
                               anchorsUsed, minSpacingSq, out var anchor,
                               plan.authored != null && plan.authored.anchorRequired))
            {
                // The sampler already exhausted its budget looking for somewhere
                // both in band and clear of the sites already placed, so this is a
                // genuinely full floor rather than one unlucky draw.
                tally.rejectedTooClose++;
                continue;
            }

            // An authored plan is drawn at a fixed size and ignores span entirely
            // -- that IS the point of hand-authoring it. Procedural recipes still
            // scale, so the two kinds sit side by side on the same floor.
            LocalPlan site;
            if (plan.authored != null)
            {
                site = FromAuthored(plan.authored);
            }
            else
            {
                int span = Mathf.Max(8, RandomRange(rng, entry.minSpan, entry.maxSpan));
                site = Compose(rng, plan.archetype, plan.variant, span);
            }
            if (site == null)
            {
                tally.rejectedNullShape++;
                continue;
            }

            bool rotatable = plan.authored == null || plan.authored.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            // DOOR ANCHORING, on the general path at last. This loop never read
            // the flag before, so "@anchor_on: door" on any plan the fill could
            // serve did nothing at all -- the same silent no-op as anchorRequired
            // being honoured inside TryPickAnchor and passed by nobody. A plan
            // that declares no anchorable run comes back with placeAt == anchor
            // and every procedural placement is unchanged.
            if (!TryDoorAnchor(rng, site, ref rot, mirror, rotatable, anchor, centre, inner, outer,
                               roadPlan, chordIndex, anchorsUsed, minSpacingSq,
                               out var placeAt, out bool headingFault, out var seat))
            {
                if (headingFault) tally.rejectedNoDoorHeading++;
                else tally.rejectedTooClose++;
                continue;
            }

            var placed = new AncientSitePlan
            {
                archetype = plan.archetype,
                variant = plan.variant,
                planName = plan.authored != null ? plan.authored.name : "",
                anchor = placeAt,
            };

            long clampSq = (long)usable * usable;
            EmitTransformed(site.floor, placeAt, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(site.wall, placeAt, rot, mirror, centre, clampSq, placed.ruinsCells);
            EmitDoorRuns(plan.authored, placeAt, rot, mirror, placed);

            // The heart rides the SAME transform as the masonry it is part
            // of. Computing it independently would drift the moment a
            // rotation or the disc clamp treated it differently from the
            // wall set, and a seal whose heart is not where the altar is
            // drawn is a bug nobody would see until they mined it.
            if (site.heart.Count > 0)
            {
                var heartOut = new List<Vector3Int>();
                EmitTransformed(site.heart, placeAt, rot, mirror, centre, clampSq, heartOut);
                if (heartOut.Count > 0)
                {
                    placed.heartCell = heartOut[0];
                    placed.hasHeart = true;
                }
            }

            // A site reduced to a handful of cells by the disc clamp is not a site.
            if (placed.cells.Count < 12)
            {
                tally.rejectedTooSmall++;
                continue;
            }

            // And a site nobody can walk into is not a site either. A wall's face
            // renders TWO cells tall and drapes over the open floor south of it;
            // those cells are solid to the pathfinder, so a room needs three cells
            // of interior height before a single cell is walkable and about five to
            // be usable. Checked AFTER rotation, because the drape is always in
            // world +Y -- the same plan can be fine on one quarter turn and
            // impassable on another. Failing here does NOT advance the plan cursor,
            // so the loop re-rolls the rotation and tries the same plan again.
            if (CountWalkable(placed.cells) < MinWalkableCells)
            {
                tally.rejectedUnwalkable++;
                continue;
            }

            placed.chordSeat = seat;
            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(placeAt);
            tally.placed++;

            // The holy pass and the vault sit OUTSIDE the general budget, which
            // is what this counter buys: without it a seal placed here would
            // move the general loop's own target and cost the floor a ruin.
            if (countsAsExtra) result.extraPlaced++;
            // Cursor already advanced at the top of the attempt.
        }

        return tally;
    }

    /// <summary>
    /// Places the guaranteed dwarven outpost, ahead of everything else, and takes
    /// its plan out of the pool so the general loop cannot serve it twice.
    ///
    /// Prefers a hand-authored plan of the outpost archetype when one exists -- the
    /// procedural Sealed Gate recipes were composed to read as SEALED, which is
    /// exactly wrong for the one gate that is open. Falls back to a procedural
    /// variant rather than failing, because a plainly-shaped outpost beats none.
    ///
    /// Its attempt budget is its own and generous: this is a set-piece the whole
    /// dwarven arc hangs off, and spending a few hundred rejected anchors to land
    /// it is free next to a run that silently has no dwarves in it.
    /// </summary>
    private static void PlaceOutpost(
        System.Random rng, SiteFloorEntry entry, Vector3Int centre,
        int inner, int outer, int usable,
        RoadPlan roadPlan,
        List<PlanRef> plans, List<Vector3Int> anchorsUsed, int minSpacingSq,
        AncientSiteResult result)
    {
        // Authored plans of the outpost archetype first, then procedural ones.
        var candidates = new List<PlanRef>();
        foreach (var p in plans)
            if (p.archetype == entry.outpostArchetype && p.authored != null)
                candidates.Add(p);
        foreach (var p in plans)
            if (p.archetype == entry.outpostArchetype && p.authored == null)
                candidates.Add(p);
        if (candidates.Count == 0)
        {
            Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
                " asked for a guaranteed outpost but its plan pool holds no " +
                entry.outpostArchetype + " at all. Check the floor's roster.");
            return;
        }

        long clampSq = (long)usable * usable;

        // Counted locally rather than onto the result, because Build overwrites
        // result.rejected* from the general fill's tally AFTER this runs. The
        // figure rides the failure message instead, which is where anyone
        // reading about a missing outpost already is.
        int headingRejects = 0;

        for (int attempt = 0; attempt < 240; attempt++)
        {
            var plan = candidates[attempt % candidates.Count];

            if (!TryPickAnchor(rng, entry.outpostAnchor, centre, inner, outer,
                               roadPlan, out int chordIndex,
                               anchorsUsed, minSpacingSq, out var anchor))
                continue;

            LocalPlan shape = plan.authored != null
                ? FromAuthored(plan.authored)
                : Compose(rng, plan.archetype, plan.variant,
                          Mathf.Max(8, RandomRange(rng, entry.minSpan, entry.maxSpan)));
            if (shape == null) continue;

            bool rotatable = plan.authored == null || plan.authored.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            if (!TryDoorAnchor(rng, shape, ref rot, mirror, rotatable, anchor, centre, inner, outer,
                               roadPlan, chordIndex, anchorsUsed, minSpacingSq,
                               out var placeAt, out bool headingFault, out var seat))
            {
                if (headingFault) headingRejects++;
                continue;
            }

            var placed = new AncientSitePlan
            {
                archetype = plan.archetype,
                variant = plan.variant,
                planName = plan.authored != null ? plan.authored.name : "",
                anchor = placeAt,
                reservedForOutpost = true,
            };
            EmitTransformed(shape.floor, placeAt, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(shape.wall, placeAt, rot, mirror, centre, clampSq, placed.ruinsCells);
            EmitDoorRuns(plan.authored, placeAt, rot, mirror, placed);

            if (placed.cells.Count < 12) continue;
            if (CountWalkable(placed.cells) < MinWalkableCells) continue;

            placed.chordSeat = seat;
            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(placeAt);
            plans.Remove(plan);
            result.outpostPlaced = true;
            return;
        }

        Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
            " failed to place its guaranteed outpost in 240 attempts. That floor " +
            "will have no dwarves. Most likely cause: the placement band holds no " +
            "road cells -- check inBandRoadCells in the site report. " +
            headingRejects + " of those attempts died on the door heading, which " +
            "points at the hold's gates rather than at the band.");
    }

    /// <summary>
    /// Places the guaranteed dwarven village: the same first-and-loud contract as
    /// PlaceOutpost, with one deliberate difference -- the plan comes from the
    /// authored set, never the pool. Every authored DwarvenVillage plan is a
    /// candidate and one is rolled seeded, so playthroughs rotate holds; a
    /// non-empty villagePlanName pins the roll to one plan instead (testing).
    /// The archetype belongs to no roster, so the general loop can never serve
    /// it and there is nothing to remove from the pool on success.
    /// </summary>
    private static void PlaceVillage(
        System.Random rng, SiteFloorEntry entry, Vector3Int centre,
        int inner, int outer, int usable,
        RoadPlan roadPlan,
        IReadOnlyList<AuthoredSitePlan> authoredPlans,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        AncientSiteResult result)
    {
        var candidates = new List<AuthoredSitePlan>();
        if (authoredPlans != null)
            foreach (var p in authoredPlans)
                if (p != null && p.archetype == SiteArchetype.DwarvenVillage)
                    candidates.Add(p);
        // Optional pin: a non-empty villagePlanName narrows the roll to that
        // one plan, for testing a specific hold without unlisting the others.
        if (!string.IsNullOrEmpty(entry.villagePlanName))
            candidates.RemoveAll(p => p.name != entry.villagePlanName);
        if (candidates.Count == 0)
        {
            Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
                " asked for a guaranteed village but no authored DwarvenVillage " +
                "plan is available" +
                (string.IsNullOrEmpty(entry.villagePlanName) ? "" :
                 " matching villagePlanName '" + entry.villagePlanName + "'") +
                ". Check the profile's authoredPlans list" +
                (string.IsNullOrEmpty(entry.villagePlanName) ? "." :
                 " and the plan's @name header."));
            return;
        }

        int pick = rng.Next(candidates.Count);
        AuthoredSitePlan plan = candidates[pick];

        var anchorKind = plan.hasAnchorOverride
            ? plan.anchorOverride
            : AncientSiteProfile.AnchorFor(SiteArchetype.DwarvenVillage);

        long clampSq = (long)usable * usable;

        // Local, for the same reason PlaceOutpost's is: Build overwrites
        // result.rejected* from the general fill after this returns.
        int headingRejects = 0;

        for (int attempt = 0; attempt < 240; attempt++)
        {
            if (!TryPickAnchor(rng, anchorKind, centre, inner, outer,
                               roadPlan, out int chordIndex,
                               anchorsUsed, minSpacingSq, out var anchor))
                continue;

            LocalPlan shape = FromAuthored(plan);
            if (shape == null) continue;

            bool rotatable = plan.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            if (!TryDoorAnchor(rng, shape, ref rot, mirror, rotatable, anchor, centre, inner, outer,
                               roadPlan, chordIndex, anchorsUsed, minSpacingSq,
                               out var placeAt, out bool headingFault, out var seat))
            {
                if (headingFault) headingRejects++;
                continue;
            }

            var placed = new AncientSitePlan
            {
                archetype = SiteArchetype.DwarvenVillage,
                // The candidate index, persisted through SiteData.variant as a
                // breadcrumb for which hold this world rolled.
                variant = pick,
                planName = plan.name,
                anchor = placeAt,
                reservedForVillage = true,
            };
            EmitTransformed(shape.floor, placeAt, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(shape.wall, placeAt, rot, mirror, centre, clampSq, placed.ruinsCells);
            EmitDoorRuns(plan, placeAt, rot, mirror, placed);

            if (placed.cells.Count < 12) continue;
            if (CountWalkable(placed.cells) < MinWalkableCells) continue;

            placed.chordSeat = seat;
            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(placeAt);
            result.villagePlaced = true;
            result.villagePlanPicked = plan.name;
            return;
        }

        Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
            " failed to place its guaranteed village in 240 attempts. That floor " +
            "will have no dwarves at home. Most likely cause: the placement band " +
            "holds no road cells -- check inBandRoadCells in the site report. " +
            headingRejects + " of those attempts died on the door heading, which " +
            "points at the hold's gates rather than at the band.");
    }

    /// <summary>
    /// The guaranteed vault. PlaceVillage's shape almost exactly, with ONE
    /// difference that matters: it emits the HEART.
    ///
    /// The guarantee paths emit floor and wall and stop; only the general fill
    /// loop carries the heart across. A vault placed here without that would
    /// report NO HEART and could never be unsealed -- there would be no stone to
    /// break, and nothing in the game would say so until someone dug to the
    /// middle of a seventy-five cell vault and found it inert. The heart rides
    /// the SAME transform as the masonry it sits in, for the same reason it does
    /// in the fill loop: computing it separately lets it drift the moment a
    /// rotation or the disc clamp treats it differently.
    /// </summary>
    private static void PlaceDeadCore(
        System.Random rng, SiteFloorEntry entry, Vector3Int centre,
        int inner, int outer, int usable,
        RoadPlan roadPlan,
        IReadOnlyList<AuthoredSitePlan> authoredPlans,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        AncientSiteResult result)
    {
        var candidates = new List<AuthoredSitePlan>();
        if (authoredPlans != null)
            foreach (var p in authoredPlans)
                if (p != null && p.archetype == SiteArchetype.DeadCoreVault)
                    candidates.Add(p);
        if (!string.IsNullOrEmpty(entry.deadCorePlanName))
            candidates.RemoveAll(p => p.name != entry.deadCorePlanName);
        if (candidates.Count == 0)
        {
            Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
                " asked for a guaranteed dead core vault but no authored " +
                "DeadCoreVault plan is available" +
                (string.IsNullOrEmpty(entry.deadCorePlanName) ? "" :
                 " matching deadCorePlanName '" + entry.deadCorePlanName + "'") +
                ". Check the profile's authoredPlans list" +
                (string.IsNullOrEmpty(entry.deadCorePlanName) ? "." :
                 " and the plan's @name header."));
            return;
        }

        int pick = rng.Next(candidates.Count);
        AuthoredSitePlan plan = candidates[pick];

        var anchorKind = plan.hasAnchorOverride
            ? plan.anchorOverride
            : AncientSiteProfile.AnchorFor(SiteArchetype.DeadCoreVault);

        long clampSq = (long)usable * usable;

        // Local, for the same reason PlaceOutpost's is: Build overwrites
        // result.rejected* from the general fill after this returns.
        int headingRejects = 0;

        for (int attempt = 0; attempt < 240; attempt++)
        {
            if (!TryPickAnchor(rng, anchorKind, centre, inner, outer,
                               roadPlan, out int chordIndex,
                               anchorsUsed, minSpacingSq, out var anchor))
                continue;

            LocalPlan shape = FromAuthored(plan);
            if (shape == null) continue;

            bool rotatable = plan.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            // DOOR ANCHORING. The block that used to sit here is now
            // TryDoorAnchor, shared with the three paths that were silently
            // ignoring the flag. The vault's behaviour is unchanged: all three
            // authored plans declare exactly one outward-facing run, so the
            // helper's conditional rng draw never fires for it.
            if (!TryDoorAnchor(rng, shape, ref rot, mirror, rotatable, anchor, centre, inner, outer,
                               roadPlan, chordIndex, anchorsUsed, minSpacingSq,
                               out var placeAt, out bool headingFault, out var seat))
            {
                if (headingFault) headingRejects++;
                continue;
            }

            var placed = new AncientSitePlan
            {
                archetype = SiteArchetype.DeadCoreVault,
                variant = pick,
                planName = plan.name,
                anchor = placeAt,
            };
            EmitTransformed(shape.floor, placeAt, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(shape.wall, placeAt, rot, mirror, centre, clampSq, placed.ruinsCells);
            EmitDoorRuns(plan, placeAt, rot, mirror, placed);

            if (shape.heart.Count > 0)
            {
                var heartOut = new List<Vector3Int>();
                EmitTransformed(shape.heart, placeAt, rot, mirror, centre, clampSq, heartOut);
                if (heartOut.Count > 0)
                {
                    placed.heartCell = heartOut[0];
                    placed.hasHeart = true;
                }
            }

            if (placed.cells.Count < 12) continue;
            if (CountWalkable(placed.cells) < MinWalkableCells) continue;

            // A vault whose heart fell outside the clamp disc is a vault that
            // cannot be unsealed. Reject the placement rather than ship an inert
            // one -- there are 240 attempts and only one vault per dungeon.
            if (!placed.hasHeart) continue;

            placed.chordSeat = seat;
            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(placeAt);

            // The vault does NOT count against the general budget, unlike the
            // outpost and the village. Floor index 4 reported thirteen sites
            // INCLUDING the vault, which meant the vault had displaced a ruin on
            // the densest floor in the game -- the one floor authored to be full.
            // The other two keep counting deliberately: their floors are authored
            // as "the hold plus a couple".
            result.extraPlaced++;
            result.deadCorePlaced = true;
            result.deadCorePlanPicked = plan.name;
            return;
        }

        Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
            " failed to place its guaranteed dead core vault in 240 attempts. " +
            "That dungeon has no vault at all. A vault is 75 cells across, so the " +
            "likeliest cause is a placement band too narrow to hold it -- check " +
            "bandInner and bandOuter against the floor radius in the site report. " +
            headingRejects + " of those attempts died on the door heading, which " +
            "points at the road layout rather than at the band.");
    }

    /// <summary>
    /// Builds one plan's local cells for inspection, without placing anything.
    /// Used by the editor preview window so plan geometry can be checked without
    /// entering play mode or generating a floor.
    /// </summary>
    public static void PreviewPlan(
        SiteArchetype archetype, int variant, int span, int seed,
        out List<Vector2Int> floorCells, out List<Vector2Int> wallCells)
    {
        floorCells = new List<Vector2Int>();
        wallCells = new List<Vector2Int>();

        var plan = Compose(new System.Random(seed), archetype, variant, span);
        if (plan == null) return;

        foreach (var c in plan.floor) floorCells.Add(c);
        foreach (var c in plan.wall) wallCells.Add(c);
    }

    // ---- Plan pool -------------------------------------------------

    private struct PlanRef
    {
        public SiteArchetype archetype;
        public int variant;

        /// <summary>Null for a procedural recipe. Set for a hand-drawn plan, which
        /// is then used verbatim instead of calling Compose.</summary>
        public AuthoredSitePlan authored;
    }

    /// <summary>Converts a parsed ASCII plan into the same local shape the
    /// procedural composer produces, so everything downstream is identical.</summary>
    private static LocalPlan FromAuthored(AuthoredSitePlan authored)
    {
        if (authored == null) return null;
        var p = new LocalPlan();
        foreach (var c in authored.wall) p.wall.Add(c);
        foreach (var c in authored.heart) p.heart.Add(c);
        foreach (var c in authored.floor)
            if (!p.wall.Contains(c)) p.floor.Add(c);

        // EVERY run with a usable outward normal, not merely the first. Which
        // one a placement uses is decided against the road's local heading, and
        // this function still has no heading and should not learn about one --
        // the choice moved to TryDoorAnchor, which is where the heading lives.
        // A run with a zero normal is skipped rather than kept: it faces its own
        // interior, and anchoring on it would point the building inward.
        // A LANE IS A DECLARATION THAT THE ROAD COMES THROUGH.
        //
        // This used to read `if (authored.anchorOnDoor)`, and only four plans in
        // the whole set carry `@anchor_on: door` -- the three DeadCoreVault
        // plans and GuardPost_TheColdWatch. Not one LANED plan does. So every
        // village and the outpost arrived at TryDoorAnchor with an empty
        // doorAnchors list, returned from its first line, never seated a gate on
        // the chord and never split anything: the road was drawn straight
        // through the middle of the hold, mining its masonry on the way. The
        // vault looked right for the same reason in reverse -- it was the only
        // archetype the directive reached.
        //
        // A plan that drew a '~' has said where it expects a road. That is the
        // same statement `@anchor_on: door` makes, made in the tilemap instead
        // of the header, and it is honoured here.
        foreach (var c in authored.lane) p.lane.Add(c);
        foreach (var c in authored.door) p.door.Add(c);

        // EVERY run with a usable outward normal, not merely the first. Which
        // one a placement uses is decided against the chord's direction, and
        // this function still has no direction and should not learn about one --
        // the choice lives in TryDoorAnchor. A run with a zero normal is skipped
        // rather than kept: it faces its own interior, and anchoring on it would
        // point the building inward.
        if (authored.anchorOnDoor || p.lane.Count > 0)
        {
            foreach (var run in authored.doorRuns)
            {
                if (run.outward == Vector2Int.zero) continue;
                p.doorAnchors.Add(run);
            }
        }

        // Only the HEADER makes it mandatory. A laned plan that finds no chord
        // is placed unthreaded and keeps clear of the roads instead; a plan that
        // declared @anchor_on: door and finds no chord is refused, because that
        // directive is the whole of what it asked for.
        p.requireDoorAnchor = authored.anchorOnDoor;

        return p.floor.Count == 0 ? null : p;
    }

    private static List<PlanRef> BuildPlanPool(
        System.Random rng, SiteFloorEntry entry,
        IReadOnlyList<AuthoredSitePlan> authoredPlans)
    {
        var roster = new List<SiteArchetype>();
        if (entry.useAllArchetypes)
        {
            for (int i = 0; i <= (int)SiteArchetype.TollHouse; i++)
                roster.Add((SiteArchetype)i);
        }
        else if (entry.pool != null)
        {
            foreach (var a in entry.pool)
                if (!roster.Contains(a)) roster.Add(a);
        }

        return BuildPlanPoolFrom(rng, roster, authoredPlans);
    }

    /// <summary>
    /// The floor's HOLY pool: the Church seals, drawn by their own pass.
    ///
    /// Separate from the general roster and never merged into it, because the
    /// two are placed against separate quotas -- see
    /// AncientSiteResult.extraPlaced. There is deliberately no
    /// useAllArchetypes equivalent: sweeping every Church archetype onto a floor
    /// is exactly the mistake WardChapel makes visible, since it anchors
    /// AlongRoad and degrades to a free pick on a floor with no roads.
    /// </summary>
    private static List<PlanRef> BuildHolyPlanPool(
        System.Random rng, SiteFloorEntry entry,
        IReadOnlyList<AuthoredSitePlan> authoredPlans)
    {
        var roster = new List<SiteArchetype>();
        if (entry.holyPool != null)
        {
            foreach (var a in entry.holyPool)
            {
                // Warned, not skipped. The list is named for what belongs in it
                // and an ordinary ruin listed here is almost certainly a slip --
                // but silently dropping an authored entry is the ambiguity this
                // project refuses everywhere else, so it is placed AND said.
                //
                // TerrainFeatureGenerator.IsHolyArchetype is called rather than a
                // second predicate written here: canon 20 makes that method the
                // ONE place the terrain override, the holy registry and the
                // desecration layer agree on what a seal is, and a fourth opinion
                // is how they would come to disagree. It is a pure static and
                // touches no scene, so this class stays scene-free.
                if (!TerrainFeatureGenerator.IsHolyArchetype(a))
                    Debug.LogWarning("[AncientSiteBuilder] Floor " + entry.floorIndex +
                        " lists " + a + " in holyPool, which is not a Church " +
                        "archetype. It will be placed by the holy pass and counted " +
                        "against the holy quota, which is probably not what was " +
                        "meant -- move it to `pool` instead.");
                if (!roster.Contains(a)) roster.Add(a);
            }
        }

        var pool = BuildPlanPoolFrom(rng, roster, authoredPlans);

        // A "@general: no" plan belongs to a guarantee pass and to nothing else.
        // No Church plan carries the flag today; stripping it here means one
        // authored later for a guarantee cannot quietly become a rollable seal.
        pool.RemoveAll(p => p.authored != null && !p.authored.generalPool);
        return pool;
    }

    /// <summary>Builds and shuffles a plan pool from an explicit roster. Shared
    /// by the general and the holy pools, so the no-repeat rule, the authored-plan
    /// variant numbering and the Fisher-Yates shuffle exist exactly once.</summary>
    private static List<PlanRef> BuildPlanPoolFrom(
        System.Random rng, List<SiteArchetype> roster,
        IReadOnlyList<AuthoredSitePlan> authoredPlans)
    {
        var pool = new List<PlanRef>();
        foreach (var a in roster)
        {
            // Zero is meaningful now: an AUTHORED-ONLY archetype contributes no
            // procedural refs at all, so a roster naming one gets exactly its
            // hand-drawn plans and never a null Compose.
            int variants = AncientSiteProfile.VariantCountFor(a);
            for (int v = 0; v < variants; v++)
                pool.Add(new PlanRef { archetype = a, variant = v });
        }

        // Hand-authored plans join the SAME pool as extra variants, numbered above
        // the procedural ones. They therefore inherit the no-repeat rule for free:
        // a floor exhausts every distinct plan, drawn or generated, before it
        // repeats any of them. A plan whose archetype is not in this floor's roster
        // is skipped, which is what keeps Sealed Gates off floor index 2.
        if (authoredPlans != null)
        {
            var used = new Dictionary<SiteArchetype, int>();
            foreach (var authored in authoredPlans)
            {
                if (authored == null) continue;
                if (!roster.Contains(authored.archetype)) continue;

                used.TryGetValue(authored.archetype, out int n);
                used[authored.archetype] = n + 1;

                pool.Add(new PlanRef
                {
                    archetype = authored.archetype,
                    variant = AncientSiteProfile.VariantCountFor(authored.archetype) + n,
                    authored = authored,
                });
            }
        }

        // Fisher-Yates. Shuffling the PLAN pool rather than picking at random is
        // what guarantees no plan repeats until every one has been used.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }

    // ---- Anchors ---------------------------------------------------

    /// <summary>
    /// Finds an anchor that is in band AND clear of the sites already placed.
    ///
    /// Spacing is tested HERE rather than by the caller. It used to return the
    /// first in-band candidate and let the placement loop discard the attempt on
    /// a collision, which gave every attempt exactly one chance at spacing -- and
    /// six of the eight archetypes anchor onto roads, so candidates cluster along
    /// the carriageway and collide constantly. Floor index 4 placed four sites of
    /// ten that way, throwing away 116 attempts. Sampling against both conditions
    /// at once fixed it without touching any tunable.
    /// </summary>
    private static bool TryPickAnchor(
        System.Random rng, SiteAnchor kind, Vector3Int centre, int inner, int outer,
        RoadPlan roadPlan, out int chordIndex,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        out Vector3Int anchor, bool requireAnchor = false)
    {
        chordIndex = -1;

        // ANCHORS COME FROM THE PLAN, not from drawn cells. A chord is a
        // straight segment with an exact direction and two named ends, so a
        // point on it is exact and its heading needs no estimating -- which is
        // what let TryRoadHeading, the stride-12 sample and the whole
        // undecimated-centreline copy come out. The old sampler resolved a
        // heading at roughly one anchor in twenty and read as a facing rule
        // while behaving as a density rule.
        if (roadPlan != null && roadPlan.valid && roadPlan.chords.Count > 0)
        {
            for (int i = 0; i < 64; i++)
            {
                int ci = rng.Next(0, roadPlan.chords.Count);
                var chord = roadPlan.chords[ci];
                if (chord == null) continue;

                Vector3Int c;
                switch (kind)
                {
                    case SiteAnchor.Junction:
                        if (roadPlan.nodes.Count == 0) continue;
                        int ni = rng.Next(0, roadPlan.nodes.Count);
                        c = roadPlan.nodes[ni];
                        ci = ChordTouchingNode(roadPlan, ni);
                        break;

                    case SiteAnchor.RoadEnd:
                        // A free end -- a spur's stub or a rim trunk's rim end.
                        // nodeB == -1 is exactly that, with no cell scan.
                        if (chord.nodeB >= 0 && chord.nodeA >= 0) continue;
                        c = chord.nodeB < 0 ? chord.b : chord.a;
                        break;

                    default:
                        // AlongRoad and Crossing: a point ON the chord, kept far
                        // enough from either end to leave an approach stub.
                        c = PointOnChord(rng, chord);
                        if (c == chord.a && c == chord.b) continue;
                        break;
                }

                if (!InBand(c, centre, inner, outer)) continue;
                if (TooClose(c, anchorsUsed, minSpacingSq)) continue;
                anchor = c;
                chordIndex = ci;
                return true;
            }
        }

        // Degrade to Free. A preference that cannot be met is not a failure --
        // it is a floor where that kind of road does not exist, or one whose
        // roads are already lined with ruins.
        //
        // UNLESS the plan says otherwise. See AuthoredSitePlan.anchorRequired:
        // for a building whose meaning is its position, being placed anywhere is
        // worse than not being placed at all.
        if (requireAnchor) { anchor = default; chordIndex = -1; return false; }

        for (int i = 0; i < 96; i++)
        {
            int dx = rng.Next(-outer, outer + 1);
            int dy = rng.Next(-outer, outer + 1);
            var c = new Vector3Int(centre.x + dx, centre.y + dy, 0);
            if (!InBand(c, centre, inner, outer)) continue;
            if (TooClose(c, anchorsUsed, minSpacingSq)) continue;
            anchor = c;
            return true;
        }

        anchor = default;
        return false;
    }

    /// <summary>A point along a chord, at least GateMinStub back from either end
    /// so both an ingress and an egress approach have room. Returns the chord's
    /// own end when it is too short to seat anything, which the caller drops.</summary>
    private static Vector3Int PointOnChord(System.Random rng, RoadChord chord)
    {
        double dx = chord.b.x - chord.a.x, dy = chord.b.y - chord.a.y;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        int stub = RoadNetworkBuilder.GateMinStub;
        if (len < 2 * stub + 1) return chord.a;

        double lo = stub / len, hi = 1.0 - stub / len;
        double t = lo + rng.NextDouble() * (hi - lo);
        return new Vector3Int(
            chord.a.x + (int)System.Math.Round(dx * t),
            chord.a.y + (int)System.Math.Round(dy * t), 0);
    }

    /// <summary>Any chord touching a node, so a junction anchor still knows which
    /// way the road runs. -1 when the node is isolated, which the seating step
    /// treats as no chord at all rather than as a failure.</summary>
    private static int ChordTouchingNode(RoadPlan plan, int nodeIndex)
    {
        for (int i = 0; i < plan.chords.Count; i++)
        {
            var c = plan.chords[i];
            if (c != null && (c.nodeA == nodeIndex || c.nodeB == nodeIndex)) return i;
        }
        return -1;
    }

    /// <summary>The exact unit direction of a chord. No estimation, which is the
    /// whole point of seating against the plan.</summary>
    private static bool ChordDirection(RoadPlan plan, int chordIndex, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (plan == null || chordIndex < 0 || chordIndex >= plan.chords.Count) return false;
        var c = plan.chords[chordIndex];
        if (c == null) return false;
        var v = new Vector2(c.b.x - c.a.x, c.b.y - c.a.y);
        if (v.sqrMagnitude < 1e-6f) return false;
        dir = v.normalized;
        return true;
    }


    /// <summary>
    /// The exit gate and the authored lane between the two, or nothing.
    ///
    /// The exit is the run whose normal best AGREES with the chord's direction,
    /// as the entry is the one that best opposes it. The corridor is
    /// (lane | door) and walkable: a gate is drawn '+', not '~', so a lane-only
    /// corridor has no cell at the threshold and every route refuses at its own
    /// front door -- sixteen plans failed identically on that before it was a
    /// bug in the test rather than in sixteen plans.
    /// </summary>
    private static bool TryLaneThrough(
        LocalPlan shape, int rot, bool mirror, Vector2 chordDir,
        DoorRun entryRun, Vector2Int gateIn,
        out Vector2Int gateOut, out Vector2Int outNormal,
        out List<Vector2Int> lane)
    {
        gateOut = default;
        outNormal = default;
        lane = null;
        if (shape.lane.Count == 0) return false;

        // The exit run: most ALIGNED with travel, and never the entry run.
        bool haveExit = false;
        float bestScore = float.NegativeInfinity;
        DoorRun exitRun = default;
        foreach (var run in shape.doorAnchors)
        {
            if (run.mid == entryRun.mid && run.outward == entryRun.outward) continue;
            var o = RotateLocal(run.outward, rot, mirror);
            if (o == Vector2Int.zero) continue;
            float score = Vector2.Dot(chordDir, new Vector2(o.x, o.y).normalized);
            if (score > bestScore) { bestScore = score; exitRun = run; haveExit = true; }
        }
        if (!haveExit) return false;
        if (!TryGateCell(shape, exitRun, rot, mirror, out gateOut)) return false;

        var inNormal = RotateLocal(entryRun.outward, rot, mirror);
        outNormal = RotateLocal(exitRun.outward, rot, mirror);
        if (gateOut == gateIn) return false;

        var floorR = new HashSet<Vector2Int>();
        foreach (var c in shape.floor) floorR.Add(RotateLocal(c, rot, mirror));

        var corridor = new HashSet<Vector2Int>();
        foreach (var c in shape.lane) corridor.Add(RotateLocal(c, rot, mirror));
        foreach (var c in shape.door) corridor.Add(RotateLocal(c, rot, mirror));

        // Drape the corridor, with BOTH approaches carved. A cell reads as wall
        // when the cell one or two north of it is unmined.
        var walkable = new HashSet<Vector2Int>();
        foreach (var c in corridor)
        {
            var up1 = new Vector2Int(c.x, c.y + 1);
            var up2 = new Vector2Int(c.x, c.y + 2);
            if (Open(floorR, up1, gateIn, inNormal, gateOut, outNormal)
                && Open(floorR, up2, gateIn, inNormal, gateOut, outNormal))
                walkable.Add(c);
        }
        if (!walkable.Contains(gateIn) || !walkable.Contains(gateOut)) return false;

        // Breadth first, four-connected: a road does not cut corners.
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int> { gateIn };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(gateIn);
        bool reached = false;
        while (queue.Count > 0 && !reached)
        {
            var c = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                var n = new Vector2Int(
                    c.x + (d == 0 ? 1 : d == 1 ? -1 : 0),
                    c.y + (d == 2 ? 1 : d == 3 ? -1 : 0));
                if (!walkable.Contains(n) || !seen.Add(n)) continue;
                prev[n] = c;
                if (n == gateOut) { reached = true; break; }
                queue.Enqueue(n);
            }
        }
        if (!reached) return false;

        var back = new List<Vector2Int>();
        var at = gateOut;
        while (at != gateIn)
        {
            back.Add(at);
            at = prev[at];
        }
        back.Add(gateIn);
        back.Reverse();
        lane = back;
        return true;
    }

    /// <summary>Open ground: the plan's own floor, or either gate's approach.</summary>
    private static bool Open(
        HashSet<Vector2Int> floorR, Vector2Int at,
        Vector2Int gateIn, Vector2Int inNormal,
        Vector2Int gateOut, Vector2Int outNormal)
        => floorR.Contains(at)
        || OnApproach(at, gateIn, inNormal)
        || OnApproach(at, gateOut, outNormal);

    /// <summary>
    /// Splits every chord a laned site was seated on: the road runs in to the
    /// gate, threads the authored lane, and leaves by the far gate. Nothing is
    /// subtracted and nothing is truncated, because no road is ever drawn
    /// through a building.
    ///
    /// ONE SITE PER CHORD. Splitting replaces the chord in place and appends
    /// two more, so a second site holding an index into the original would be
    /// measuring against a segment that no longer reaches it. A second site on
    /// the same chord keeps its seat and simply is not threaded -- the road
    /// passes its door instead of entering it.
    /// </summary>
    private static void SplitChordsForSites(RoadPlan plan, AncientSiteResult result)
    {
        if (plan == null || !plan.valid || result == null) return;

        var done = new HashSet<int>();
        foreach (var site in result.sites)
        {
            var seat = site.chordSeat;
            if (seat == null || !seat.hasBothGates) continue;
            if (seat.chordIndex < 0 || seat.chordIndex >= plan.chords.Count) continue;
            if (!done.Add(seat.chordIndex)) continue;

            var chord = plan.chords[seat.chordIndex];
            if (chord == null) continue;

            var ingress = new RoadChord
            {
                a = chord.a, b = seat.gateIn,
                nodeA = chord.nodeA, nodeB = -1,
                kind = chord.kind, width = chord.width, brokenGapCells = 0,
            };
            ingress.waypoints.AddRange(RoadNetworkBuilder.ApproachWaypoints(
                seat.gateIn, new Vector2(seat.inNormal.x, seat.inNormal.y), chord.a));

            // The broken gap belongs to the FAR end, so it rides the egress. An
            // ingress that inherited it would stop the road short of the gate it
            // was drawn to reach.
            var egress = new RoadChord
            {
                a = seat.gateOut, b = chord.b,
                nodeA = -1, nodeB = chord.nodeB,
                kind = chord.kind, width = chord.width,
                brokenGapCells = chord.brokenGapCells,
            };
            var tail = RoadNetworkBuilder.ApproachWaypoints(
                seat.gateOut, new Vector2(seat.outNormal.x, seat.outNormal.y), chord.b);
            tail.Reverse();     // ApproachWaypoints runs end-to-gate; this chord runs gate-to-end
            egress.waypoints.AddRange(tail);

            // The rail through the site. Every lane cell is a waypoint: they are
            // adjacent, so the polyline IS the route and Bresenham between two
            // neighbours cannot wander off it. RebuildRoadCells paints nothing
            // for a Lane -- it exists so DeepRoadGraph stays connected gate to
            // gate, and the site already drew this ground.
            var rail = new RoadChord
            {
                a = seat.gateIn, b = seat.gateOut,
                nodeA = -1, nodeB = -1,
                kind = RoadKind.Lane, width = 1, brokenGapCells = 0,
            };
            for (int i = 1; i < seat.laneCells.Count - 1; i++)
                rail.waypoints.Add(seat.laneCells[i]);

            plan.chords[seat.chordIndex] = ingress;
            plan.chords.Add(egress);
            plan.chords.Add(rail);
        }

        // SPURS, after the threading splits so their chord indices were not
        // moved out from under them by an earlier laned split -- one site per
        // chord is already enforced by `done` across both passes.
        foreach (var site in result.sites)
        {
            var seat = site.chordSeat;
            if (seat == null || !seat.spur) continue;
            if (seat.chordIndex < 0 || seat.chordIndex >= plan.chords.Count) continue;
            if (!done.Add(seat.chordIndex)) continue;

            var chord = plan.chords[seat.chordIndex];
            if (chord == null) continue;

            // Spur width matched to the DOOR, not the trunk. A five-wide
            // carriageway centred on a three-cell door paints one jamb cell
            // each side; a door-wide spur paints exactly the doorway. The door
            // is found by its outward normal and proximity to the gate --
            // EmitDoorRuns records mids in world space, and the gate is the
            // walkable run cell nearest that mid, so they sit within the run's
            // own length of each other. Held odd because Dilate spreads
            // (w - 1) / 2 either side.
            int doorWidth = 3;
            int bestDist = int.MaxValue;
            foreach (var d in site.doors)
            {
                if (d.outward != seat.inNormal) continue;
                int dist = Mathf.Abs(d.mid.x - seat.gateIn.x)
                         + Mathf.Abs(d.mid.y - seat.gateIn.y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    doorWidth = Mathf.Max(3, d.halfWidth * 2 + 1);
                }
            }
            int spurWidth = Mathf.Min(chord.width, doorWidth);
            if ((spurWidth & 1) == 0) spurWidth -= 1;

            var spur = new RoadChord
            {
                a = seat.takeoff, b = seat.gateIn,
                nodeA = -1, nodeB = -1,
                kind = RoadKind.Spur, width = Mathf.Max(1, spurWidth),
                brokenGapCells = 0,
            };

            // The take-off becomes a NODE by splitting the host there -- unless
            // it already IS an end, where a split would mint a near-zero chord.
            // DeepRoadGraph clusters raw endpoints at radius 6, so a take-off
            // within two cells of an end shares that end's node for free.
            bool nearA = Near(seat.takeoff, chord.a, 2);
            bool nearB = Near(seat.takeoff, chord.b, 2);
            if (!nearA && !nearB)
            {
                var first = new RoadChord
                {
                    a = chord.a, b = seat.takeoff,
                    nodeA = chord.nodeA, nodeB = -1,
                    kind = chord.kind, width = chord.width, brokenGapCells = 0,
                };
                var second = new RoadChord
                {
                    a = seat.takeoff, b = chord.b,
                    nodeA = -1, nodeB = chord.nodeB,
                    kind = chord.kind, width = chord.width,
                    brokenGapCells = chord.brokenGapCells,
                };
                plan.chords[seat.chordIndex] = first;
                plan.chords.Add(second);
            }
            plan.chords.Add(spur);
        }
    }

    /// <summary>Chebyshev proximity, for take-off-at-end detection.</summary>
    private static bool Near(Vector3Int a, Vector3Int b, int r)
        => Mathf.Abs(a.x - b.x) <= r && Mathf.Abs(a.y - b.y) <= r;


    /// <summary>How many of a source's cells fall in the placement band. Reported
    /// so a starved floor names the anchor source that dried up, instead of only
    /// saying that something did.</summary>
    private static int CountInBand(
        IReadOnlyList<Vector3Int> source, Vector3Int centre, int inner, int outer)
    {
        if (source == null) return 0;
        int n = 0;
        foreach (var c in source)
            if (InBand(c, centre, inner, outer)) n++;
        return n;
    }

    /// <summary>Junction nodes inside the placement band.</summary>
    private static int CountNodesInBand(
        RoadPlan plan, Vector3Int centre, int inner, int outer)
    {
        if (plan == null || !plan.valid) return 0;
        int n = 0;
        foreach (var node in plan.nodes)
            if (InBand(node, centre, inner, outer)) n++;
        return n;
    }

    /// <summary>Chords with at least one end in the placement band. Not a cell
    /// count -- a chord is one seatable thing however long it is drawn.</summary>
    private static int CountChordsInBand(
        RoadPlan plan, Vector3Int centre, int inner, int outer)
    {
        if (plan == null || !plan.valid) return 0;
        int n = 0;
        foreach (var c in plan.chords)
            if (c != null && (InBand(c.a, centre, inner, outer)
                              || InBand(c.b, centre, inner, outer))) n++;
        return n;
    }

    /// <summary>Free chord ends -- spur stubs and rim ends -- inside the band.</summary>
    private static int CountFreeEndsInBand(
        RoadPlan plan, Vector3Int centre, int inner, int outer)
    {
        if (plan == null || !plan.valid) return 0;
        int n = 0;
        foreach (var c in plan.chords)
        {
            if (c == null) continue;
            if (c.nodeA < 0 && InBand(c.a, centre, inner, outer)) n++;
            if (c.nodeB < 0 && InBand(c.b, centre, inner, outer)) n++;
        }
        return n;
    }

    private static bool InBand(Vector3Int cell, Vector3Int centre, int inner, int outer)
    {
        long dx = cell.x - centre.x, dy = cell.y - centre.y;
        long d = dx * dx + dy * dy;
        return d >= (long)inner * inner && d <= (long)outer * outer;
    }

    private static bool TooClose(Vector3Int candidate, List<Vector3Int> used, int minSpacingSq)
    {
        foreach (var u in used)
        {
            long dx = candidate.x - u.x, dy = candidate.y - u.y;
            if (dx * dx + dy * dy < minSpacingSq) return true;
        }
        return false;
    }

    // ---- Composition -----------------------------------------------

    /// <summary>A plan under construction, in local cells centred on the origin.
    /// A cell in `wall` is masonry; a cell in `floor` is carved interior. Wall
    /// wins where the two collide, because a wall drawn over a floor is a wall
    /// built in a room, which is the whole idea of an internal partition.</summary>
    private class LocalPlan
    {
        public readonly HashSet<Vector2Int> floor = new HashSet<Vector2Int>();
        public readonly HashSet<Vector2Int> wall = new HashSet<Vector2Int>();

        /// <summary>The heart, for authored plans that declare one.
        /// Empty for every procedural recipe -- a composed ruin has no
        /// single cell that means anything.</summary>
        public readonly HashSet<Vector2Int> heart = new HashSet<Vector2Int>();

        /// <summary>Every run this plan may be anchored on: the declared doors
        /// with a usable outward normal, and only when the plan asked for door
        /// anchoring. Empty for every procedural recipe, and empty for an
        /// authored plan that did not ask -- which is what keeps the whole
        /// placement layer behaving exactly as it did.
        ///
        /// A LIST rather than the single run this held before, and the
        /// crossroads is the reason. Taking whichever run was scanned first is
        /// right for a vault, which has one door by design; on a plan with two
        /// or four gates it means the building can only ever be entered from the
        /// side that happened to be scanned first, so a four-gated hold would
        /// take a road at its north gate and nowhere else.</summary>
        public readonly List<DoorRun> doorAnchors = new List<DoorRun>();

        /// <summary>The authored lane: where the plan EXPECTS a road to run
        /// through it, gate to gate. Empty for every procedural recipe and for
        /// any authored plan that drew no '~'. A site with no lane has no
        /// through-route and a road stops at its door.</summary>
        public readonly HashSet<Vector2Int> lane = new HashSet<Vector2Int>();

        /// <summary>Declared door cells. The lane corridor needs them: a gate is
        /// drawn '+', not '~', so a lane-only corridor has no cell at the
        /// threshold and every route refuses at its own front door.</summary>
        public readonly HashSet<Vector2Int> door = new HashSet<Vector2Int>();

        /// <summary>Set by `@anchor_on: door`. A plan with a lane also fills
        /// doorAnchors, but only this makes meeting a road MANDATORY.</summary>
        public bool requireDoorAnchor;

        public void Floor(int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    floor.Add(new Vector2Int(x, y));
        }

        public void Wall(int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    wall.Add(new Vector2Int(x, y));
        }

        /// <summary>A wall ring of the given thickness with a carved interior.</summary>
        public void Room(int x0, int y0, int w, int h, int t)
        {
            if (w <= 2 * t || h <= 2 * t) { Wall(x0, y0, w, h); return; }
            Wall(x0, y0, w, h);
            for (int x = x0 + t; x < x0 + w - t; x++)
                for (int y = y0 + t; y < y0 + h - t; y++)
                {
                    var p = new Vector2Int(x, y);
                    wall.Remove(p);
                    floor.Add(p);
                }
        }

        /// <summary>Open ground with regular column stubs. A colonnade is the
        /// cheapest thing that says "this was built" at a glance -- but only if the
        /// columns are close enough together to read as a grid. Spaced too widely
        /// they read as scattered rubble on a field, which is what a span-62 plaza
        /// looked like.</summary>
        public void Colonnade(int x0, int y0, int w, int h, int spacing, int colSize)
        {
            Floor(x0, y0, w, h);
            int s = Mathf.Max(2, spacing);
            int cs = Mathf.Max(1, colSize);
            for (int x = x0 + s; x < x0 + w - cs; x += s)
                for (int y = y0 + s; y < y0 + h - cs; y += s)
                    Wall(x, y, cs, cs);
        }

        /// <summary>Cuts a rectangle out of the plan entirely -- neither floor nor
        /// masonry. Used to stop a plan filling its whole bounding box: an open
        /// corner is unexcavated rock, and rock is what a ruin should mostly be
        /// surrounded by.</summary>
        public void Clear(int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                {
                    var p = new Vector2Int(x, y);
                    floor.Remove(p);
                    wall.Remove(p);
                }
        }

        /// <summary>Cuts the four corners off a rectangle, turning a square room
        /// into a chamfered one. Applied to wall and floor alike so the room
        /// keeps its shape rather than growing bald corners.</summary>
        public void Chamfer(int cx, int cy, int extent, int cut)
        {
            floor.RemoveWhere(p => Mathf.Abs(p.x - cx) + Mathf.Abs(p.y - cy) > extent + cut);
            wall.RemoveWhere(p => Mathf.Abs(p.x - cx) + Mathf.Abs(p.y - cy) > extent + cut);
        }

        /// <summary>Knocks gaps in the masonry. Nothing down here is intact, and
        /// a breach is also what lets the interior be walked into rather than
        /// only mined into.
        ///
        /// Callers pass a count sized for a LARGE plan; ScaleBreach trims it for a
        /// small one, because three holes of radius two in a 26-cell building is
        /// most of its outer wall.</summary>
        public void Breach(System.Random rng, int count, int size)
        {
            if (wall.Count == 0 || count <= 0) return;
            var list = new List<Vector2Int>(wall);
            for (int i = 0; i < count; i++)
            {
                var seed = list[rng.Next(0, list.Count)];
                int r = Mathf.Max(1, size);
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        var p = new Vector2Int(seed.x + dx, seed.y + dy);
                        if (wall.Remove(p)) floor.Add(p);
                    }
            }
        }
    }

    /// <summary>Trims a breach count for small plans. The authored figures suit a
    /// large building; on a small one the same holes remove most of the wall.</summary>
    private static int ScaleBreach(int count, int span)
    {
        if (span >= 34) return count;
        if (span >= 24) return Mathf.Max(1, (count * 2) / 3);
        return Mathf.Max(1, count / 2);
    }

    private static LocalPlan Compose(System.Random rng, SiteArchetype archetype, int variant, int span)
    {
        var p = new LocalPlan();
        int s = Mathf.Max(10, span);
        int half = s / 2;
        int t = s >= 40 ? 2 : 1;          // wall thickness scales with the building
        int lo = -half;

        switch (archetype)
        {
            // -- The junction. A plaza is a crossroads that grew ambitious. ----
            case SiteArchetype.SunkenPlaza:
                if (variant == 0)
                {
                    // Columns on a tighter grid, and the four corners taken out, so
                    // the plaza reads as a colonnaded square rather than a field.
                    p.Colonnade(lo, lo, s, s, Mathf.Max(4, s / 9), t);
                    p.Wall(lo, lo, s, t); p.Wall(lo, half - t, s, t);
                    p.Wall(lo, lo, t, s); p.Wall(half - t, lo, t, s);
                    int corner = Mathf.Max(2, s / 6);
                    p.Clear(lo, lo, corner, corner);
                    p.Clear(half - corner, lo, corner, corner);
                    p.Clear(lo, half - corner, corner, corner);
                    p.Clear(half - corner, half - corner, corner, corner);
                }
                else if (variant == 1)
                {
                    int arm = Mathf.Max(6, s / 3);
                    p.Room(lo, -arm / 2, s, arm, t);
                    p.Room(-arm / 2, lo, arm, s, t);
                }
                else
                {
                    p.Room(lo, lo, s, s, t);
                    int plinth = Mathf.Max(3, s / 5);
                    p.Wall(-plinth / 2, -plinth / 2, plinth, plinth);
                    // Four short spurs off the plinth: a hall this size needs
                    // something between the plinth and the outer wall.
                    int spur = Mathf.Max(2, s / 5);
                    p.Wall(-t / 2, plinth / 2, Mathf.Max(1, t), spur);
                    p.Wall(-t / 2, -plinth / 2 - spur, Mathf.Max(1, t), spur);
                    p.Wall(plinth / 2, -t / 2, spur, Mathf.Max(1, t));
                    p.Wall(-plinth / 2 - spur, -t / 2, spur, Mathf.Max(1, t));
                }
                p.Breach(rng, ScaleBreach(3 + rng.Next(0, 3), s), t);
                break;

            // -- Archives sit on roads, because archives sit on roads. ---------
            case SiteArchetype.CollapsedArchive:
                if (variant == 0)
                {
                    int h = Mathf.Max(8, (s * 3) / 5);
                    p.Room(lo, -h / 2, s, h, t);
                    int rows = 3;
                    for (int i = 1; i <= rows; i++)
                    {
                        int x = lo + (s * i) / (rows + 1);
                        int cut = i == 2 ? h / 3 : 0;    // one stack row already down
                        p.Wall(x, -h / 2 + t + cut, t, h - 2 * t - cut);
                    }
                }
                else if (variant == 1)
                {
                    int spine = Mathf.Max(4, s / 7);
                    p.Room(lo, -spine / 2, s, spine, t);
                    int wing = Mathf.Max(7, s / 3);
                    p.Room(lo + s / 6, spine / 2, wing, wing, t);
                    p.Room(lo + s / 6, -spine / 2 - wing, wing, wing, t);
                }
                else
                {
                    int h = Mathf.Max(8, s / 2);
                    p.Room(lo, -h / 2, s, h, t);
                    p.Wall(lo + (s * 2) / 3, -h / 2, s / 3, h);   // the roof came down
                }
                p.Breach(rng, ScaleBreach(2 + rng.Next(0, 3), s), t);
                break;

            // -- Cells, and what was kept in them. -----------------------------
            case SiteArchetype.Ossuary:
                if (variant == 0)
                {
                    int corr = Mathf.Max(3, s / 8);
                    p.Floor(lo, -corr / 2, s, corr);
                    int niche = Mathf.Max(6, s / 7);
                    for (int x = lo + 2; x + niche < half; x += niche + t)
                    {
                        p.Room(x, corr / 2, niche, niche, t);
                        p.Room(x, -corr / 2 - niche, niche, niche, t);
                    }
                }
                else if (variant == 1)
                {
                    int inner = Mathf.Max(8, s / 2);
                    p.Room(-inner / 2, -inner / 2, inner, inner, t);
                    int niche = Mathf.Max(6, s / 8);
                    for (int i = 0; i < 4; i++)
                    {
                        int off = inner / 2;
                        if (i == 0) p.Room(-niche / 2, off, niche, niche, t);
                        if (i == 1) p.Room(-niche / 2, -off - niche, niche, niche, t);
                        if (i == 2) p.Room(off, -niche / 2, niche, niche, t);
                        if (i == 3) p.Room(-off - niche, -niche / 2, niche, niche, t);
                    }
                }
                else
                {
                    int corr = Mathf.Max(3, s / 9);
                    int gap = Mathf.Max(6, s / 4);
                    p.Floor(lo, gap / 2, s, corr);
                    p.Floor(lo, -gap / 2 - corr, s, corr);
                    int niche = Mathf.Max(6, s / 8);
                    for (int x = lo + 2; x + niche < half; x += niche + t)
                        p.Room(x, -niche / 2, niche, niche, t);
                }
                p.Breach(rng, ScaleBreach(2 + rng.Next(0, 2), s), 1);
                break;

            // -- It crosses the road. It carried water once; it is dry now. ----
            case SiteArchetype.BrokenAqueduct:
                {
                    // Five, not three. A wall's rendered face is TWO cells tall and
                    // drapes over the open floor south of it, and those cells are not
                    // walkable (TileInfluenceManager.IsUnderOverhang). A three-wide
                    // channel with walls both sides therefore keeps one walkable row at
                    // best, and none at all in half of the rotations.
                    int chan = 5;
                    int pier = Mathf.Max(5, s / 7);
                    if (variant == 0)
                    {
                        p.Floor(lo, -chan / 2, s, chan);
                        p.Wall(lo, -chan / 2 - t, s, t);
                        p.Wall(lo, chan / 2, s, t);
                        for (int x = lo; x < half; x += pier) p.Wall(x, -chan / 2 - t - 1, t, chan + 2 * t + 2);
                        int missing = lo + s / 2;
                        for (int x = missing; x < missing + pier; x++)
                            for (int y = -chan; y <= chan; y++)
                            { var q = new Vector2Int(x, y); p.wall.Remove(q); p.floor.Remove(q); }
                    }
                    else if (variant == 1)
                    {
                        p.Floor(lo, -chan / 2, s / 2 + chan, chan);
                        p.Wall(lo, -chan / 2 - t, s / 2, t);
                        p.Wall(lo, chan / 2, s / 2, t);
                        p.Floor(-chan / 2, -chan / 2, chan, half);
                        p.Wall(-chan / 2 - t, -chan / 2, t, half);
                        p.Wall(chan / 2, -chan / 2, t, half);
                        for (int x = lo; x < 0; x += pier) p.Wall(x, -chan / 2 - t - 1, t, chan + 2 * t + 2);
                    }
                    else
                    {
                        int stub = Mathf.Max(6, s / 3);
                        p.Floor(lo, -chan / 2, stub, chan);
                        p.Wall(lo, -chan / 2 - t, stub, t);
                        p.Wall(lo, chan / 2, stub, t);
                        p.Floor(half - stub, -chan / 2, stub, chan);
                        p.Wall(half - stub, -chan / 2 - t, stub, t);
                        p.Wall(half - stub, chan / 2, stub, t);
                        for (int x = lo; x < lo + stub; x += pier) p.Wall(x, -chan / 2 - t - 1, t, chan + 2 * t + 2);
                        for (int x = half - stub; x < half; x += pier) p.Wall(x, -chan / 2 - t - 1, t, chan + 2 * t + 2);
                    }
                    p.Breach(rng, ScaleBreach(1 + rng.Next(0, 3), s), t);
                }
                break;

            // -- The deep faith kept its own rooms. ----------------------------
            case SiteArchetype.HollowSanctum:
                if (variant == 0)
                {
                    p.Room(lo, lo, s, s, t);
                    int mid = Mathf.Max(8, (s * 2) / 3);
                    p.Room(-mid / 2, -mid / 2, mid, mid, t);
                    int core = Mathf.Max(3, s / 6);
                    p.Wall(-core / 2, -core / 2, core, core);   // still sealed
                    // Radial partitions across the ambulatory, so the ring between
                    // the two walls is divided rather than one continuous corridor.
                    int gapRing = (s - mid) / 2;
                    p.Wall(-t / 2, mid / 2, Mathf.Max(1, t), gapRing);
                    p.Wall(-t / 2, -mid / 2 - gapRing, Mathf.Max(1, t), gapRing);
                }
                else if (variant == 1)
                {
                    int w = Mathf.Max(10, (s * 3) / 4);
                    p.Room(-w / 2, -w / 2, w, w, t);
                    int apse = Mathf.Max(5, s / 4);
                    p.Room(-apse / 2, w / 2 - t, apse, apse, t);
                }
                else
                {
                    p.Room(lo, lo, s, s, t);
                    p.Chamfer(0, 0, half, half / 3);
                }
                p.Breach(rng, ScaleBreach(2 + rng.Next(0, 2), s), t);
                break;

            // -- Where the road stops, and why. --------------------------------
            case SiteArchetype.SealedGate:
                {
                    int thick = Mathf.Max(3, s / 8);
                    if (variant == 0)
                    {
                        p.Wall(lo, -thick / 2, s, thick);
                        int court = Mathf.Max(6, s / 3);
                        p.Floor(-court / 2, -thick / 2 - court, court, court);
                        int portal = Mathf.Max(3, s / 7);
                        p.Wall(-portal / 2, -thick / 2 - 1, portal, thick + 2);
                    }
                    else if (variant == 1)
                    {
                        p.Wall(lo, -thick / 2, s, thick);
                        int tower = Mathf.Max(6, s / 5);
                        p.Room(lo, -tower / 2, tower, tower, t);
                        p.Room(half - tower, -tower / 2, tower, tower, t);
                    }
                    else
                    {
                        p.Wall(lo, -thick * 2, s, thick);
                        p.Wall(lo, thick, s, thick);
                        p.Floor(lo, -thick, s, thick * 2);
                    }
                    p.Breach(rng, ScaleBreach(1 + rng.Next(0, 2), s), 1);
                }
                break;

            // -- Somebody stood here and watched the road. ---------------------
            case SiteArchetype.GuardPost:
                {
                    int house = Mathf.Max(7, s / 2);
                    if (variant == 0)
                    {
                        p.Room(-house / 2, -house / 2, house, house, t);
                        int yard = Mathf.Max(9, (s * 3) / 4);
                        p.Wall(-yard / 2, -yard / 2, yard, t);
                        p.Wall(-yard / 2, -yard / 2, t, yard);
                        p.Wall(yard / 2 - t, -yard / 2, t, yard);
                    }
                    else if (variant == 1)
                    {
                        int gap = Mathf.Max(4, s / 5);
                        p.Room(-gap / 2 - house, -house / 2, house, house, t);
                        p.Room(gap / 2, -house / 2, house, house, t);
                    }
                    else
                    {
                        p.Room(-house / 2, -house / 2, house, house, t);
                        p.Wall(house / 2, -t / 2 - 1, Mathf.Max(4, s / 3), Mathf.Max(1, t));
                    }
                    p.Breach(rng, ScaleBreach(1 + rng.Next(0, 2), s), 1);
                }
                break;

            // -- They were rich because they held the gate. --------------------
            case SiteArchetype.TollHouse:
                {
                    int house = Mathf.Max(8, s / 3);
                    if (variant == 0)
                    {
                        p.Room(lo, -house / 2, house, house, t);
                        p.Room(half - house, -house / 2, house, house, t);
                        p.Wall(lo + house, -t, s - 2 * house, Mathf.Max(1, t));
                    }
                    else if (variant == 1)
                    {
                        int w = Mathf.Max(9, (s * 2) / 3);
                        p.Room(-w / 2, -house / 2, w, house, t);
                        int arch = Mathf.Max(3, s / 8);
                        p.Floor(-arch / 2, -house / 2 - t, arch, house + 2 * t);
                    }
                    else
                    {
                        p.Room(-house / 2, -house / 2, house, house, t);
                        int yard = Mathf.Max(10, (s * 3) / 4);
                        p.Wall(-yard / 2, -yard / 2, yard, t);
                        p.Wall(-yard / 2, yard / 2 - t, yard, t);
                        p.Wall(-yard / 2, -yard / 2, t, yard);
                    }
                    p.Breach(rng, ScaleBreach(2 + rng.Next(0, 2), s), t);
                }
                break;

            default:
                return null;
        }

        // Wall wins any collision: a partition drawn through a room is a
        // partition, not a hole in the floor set.
        p.floor.RemoveWhere(c => p.wall.Contains(c));
        return p.floor.Count == 0 ? null : p;
    }

    // ---- Transform and emit ----------------------------------------

    /// <summary>The plan-space transform, extracted so that EVERY consumer turns
    /// a local cell into a world offset the same way. Door anchoring needs the
    /// rotated position of one cell without emitting anything, and a second copy
    /// of this switch is how the door would end up a quarter turn from the
    /// building it belongs to.</summary>
    private static Vector2Int RotateLocal(Vector2Int p, int rot, bool mirror)
    {
        int x = mirror ? -p.x : p.x;
        int y = p.y;
        switch (rot & 3)
        {
            case 1: return new Vector2Int(-y, x);
            case 2: return new Vector2Int(-x, -y);
            case 3: return new Vector2Int(y, -x);
            default: return new Vector2Int(x, y);
        }
    }

    private static void EmitTransformed(
        HashSet<Vector2Int> local, Vector3Int anchor, int rot, bool mirror,
        Vector3Int floorCentre, long clampSq, List<Vector3Int> into)
    {
        var seen = new HashSet<Vector3Int>();
        foreach (var p in local)
        {
            var r = RotateLocal(p, rot, mirror);
            int rx = r.x, ry = r.y;

            var c = new Vector3Int(anchor.x + rx, anchor.y + ry, 0);
            long dx = c.x - floorCentre.x, dy = c.y - floorCentre.y;
            if (dx * dx + dy * dy > clampSq) continue;
            if (seen.Add(c)) into.Add(c);
        }
    }

    /// <summary>Fewest walkable cells a placed site may have. Below this the ruin
    /// reads as a room and behaves as a wall, which is worse than not generating it.</summary>
    private const int MinWalkableCells = 16;

    /// <summary>How far off the door's outward normal a road's local heading may
    /// be before the anchor is rejected, as a cosine.
    ///
    /// THIRTY DEGREES, and the cone is chosen WITH the door corridor rather
    /// than on its own -- the two only work as a pair. A road arriving steeply
    /// drifts out of the corridor before it reaches the door and is cut anyway.
    /// Measured, worst-case distance from the road's surviving end to the door:
    ///
    ///     cone 45, corridor +/-1 .. 5.7    cone 30, corridor +/-1 .. 4.5
    ///     cone 45, corridor +/-2 .. 5.7    cone 30, corridor +/-2 .. 0.0
    ///     cone 45, corridor +/-3 .. 0.0    cone 20, corridor +/-1 .. 0.0
    ///
    /// 30 with +/-2 reaches every time at a corridor exactly one trunk wide.
    /// 45 would need +/-3, which is wider than any authored door and would eat
    /// jamb beyond it; 20 buys nothing more and throws away anchors. Acceptance
    /// falls from about half of all bearings to about a third, which is ample
    /// against PlaceDeadCore's 240 attempts.</summary>

    /// <summary>Puts the plan's declared door runs into world space on the
    /// placed site, rotation and mirroring applied. Every OUTWARD-FACING run is
    /// emitted, not merely the anchored one, so a road arriving at any door of a
    /// multi-door site connects to it.</summary>
    private static void EmitDoorRuns(
        AuthoredSitePlan plan, Vector3Int placeAt, int rot, bool mirror,
        AncientSitePlan placed)
    {
        if (plan == null || plan.doorRuns == null) return;
        foreach (var run in plan.doorRuns)
        {
            if (run.outward == Vector2Int.zero) continue;
            var mid = RotateLocal(run.mid, rot, mirror);
            var outward = RotateLocal(run.outward, rot, mirror);
            placed.doors.Add(new PlacedDoor
            {
                mid = new Vector3Int(placeAt.x + mid.x, placeAt.y + mid.y, 0),
                outward = outward,
                halfWidth = Mathf.Max(0, run.length / 2),
            });
        }
    }

    /// <summary>
    /// Shifts a plan so one of its declared doors lands on the anchor, or
    /// refuses the anchor outright.
    ///
    /// EXTRACTED, because this used to live inside PlaceDeadCore alone. Fill,
    /// PlaceOutpost and PlaceVillage never read the flag, so "@anchor_on: door"
    /// on any plan those paths could serve was a silent no-op -- the plan simply
    /// placed on its centre like every other site, which is exactly what it did
    /// before the feature existed. Same class of fault as anchorRequired being
    /// honoured inside TryPickAnchor and passed by nobody.
    ///
    /// A plan declaring no anchorable run returns true with placeAt == anchor,
    /// so every procedural path is unchanged by construction rather than by
    /// inspection.
    ///
    /// ROTATION AND MIRRORING ARE INPUTS. Every caller draws them from rng
    /// before reaching here, and moving those draws inside would change every
    /// world for a given seed. rng is touched here ONLY when more than one run
    /// qualifies -- which is why the vault is bit-identical: all three authored
    /// DeadCoreVault plans declare exactly one outward-facing run, so the draw
    /// never fires on that path.
    ///
    /// headingFault separates the two ways this can fail. True means the door
    /// gate refused -- no heading, or no gate facing one. False means the SHIFT
    /// left the band or collided with a placed site, which is a spacing problem
    /// wearing a door's clothes and belongs in the spacing counter.
    /// </summary>
    private static bool TryDoorAnchor(
        System.Random rng, LocalPlan shape, ref int rot, bool mirror, bool rotatable,
        Vector3Int anchor, Vector3Int centre, int inner, int outer,
        RoadPlan roadPlan, int chordIndex,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        out Vector3Int placeAt, out bool headingFault, out SiteChordSeat seat)
    {
        placeAt = anchor;
        headingFault = false;
        // Assigned FIRST and on every path. An out parameter has to be
        // definitely assigned before every return, and there is no compiler in
        // the delivery container to say so.
        seat = null;
        if (shape == null) return true;

        // KEEP CLEAR, for anything that cannot take a road through it. Nothing
        // subtracts a carriageway out of a site any more, so a chord crossing a
        // site no longer costs the site its cells quietly -- it MINES the
        // masonry it crosses and leaves the site claiming walls that are not
        // there. A site that cannot be threaded must therefore not be crossed at
        // all.
        if (shape.doorAnchors.Count == 0)
            return FootprintClearsChords(shape, placeAt, rot, mirror,
                                         roadPlan, chordIndex);

        // THE HEADING IS EXACT NOW. It is the chord's own direction, not a least
        // squares fit over nearby cells, so it resolves at every anchor a chord
        // produced instead of at about one in twenty. That is what let
        // TryRoadHeading and the undecimated centreline copy come out.
        if (!ChordDirection(roadPlan, chordIndex, out var heading))
        {
            // No chord to answer to. A plan that DECLARED @anchor_on: door is
            // refused -- that directive is the whole of what it asked for. A
            // plan that merely drew a lane is placed where it is and keeps clear
            // of the roads instead, which is what the five Free-anchored laned
            // plans have always effectively been.
            if (shape.requireDoorAnchor)
            {
                headingFault = true;
                return false;
            }
            return FootprintClearsChords(shape, placeAt, rot, mirror,
                                         roadPlan, chordIndex);
        }

        // ROTATION IS CHOSEN, NOT ROLLED, and the choice is SIGNED.
        //
        // The first port of this scored runs by Mathf.Abs of the dot -- the old
        // cone era's "a road has no forward" reasoning -- and that is wrong
        // here, because the two ends of a seated chord are not interchangeable:
        // the ingress arrives from chord.a and the egress leaves for chord.b. An
        // entry gate whose normal agrees WITH travel faces away from a, so the
        // road drawn from a to it crosses the building to arrive. Measured over
        // every plan at 24 bearings: the undirected pick chose a wrong-facing
        // entry on half of them, which is the road through the middle of the
        // village on floor 3.
        //
        // So: the ENTRY is the run most OPPOSED to travel, the exit -- chosen
        // later by TryLaneThrough with the same signed test -- is the most
        // aligned, and a rotation is scored on the SPREAD between its best pair,
        // exactly as the sim's turn_to_face measured green. Orientations are
        // ranked and tried in order, and the first whose entry gate yields a
        // walkable cell wins, so a buried gate falls through to the next-best
        // turn instead of failing the anchor.
        //
        // A plan with ONE usable run cannot oppose anything; it is the SPUR
        // class -- the road does not pass through it, a spur comes out to meet
        // its door -- and its rotation is scored to put that door PERPENDICULAR
        // to the chord, so the spur tees off square.
        //
        // The caller still ROLLS rot and mirror before reaching here and those
        // draws are untouched, so every procedural path -- which has no
        // doorAnchors and returned above -- keeps its stream position exactly.
        // DoorRun is a STRUCT (AncientSitePlanLibrary), so there is no null to
        // mean "nothing chosen yet" and a separate flag has to carry it.
        int usableTotal = 0;
        foreach (var run in shape.doorAnchors)
            if (RotateLocal(run.outward, 0, mirror) != Vector2Int.zero) usableTotal++;
        bool spurClass = usableTotal < 2 || shape.lane.Count == 0;

        int bestRot = rot;
        DoorRun bestRun = default;
        bool haveRun = false;
        var gate = Vector2Int.zero;
        var triedRots = new int[4] { -1, -1, -1, -1 };

        for (int rank = 0; rank < 4 && !haveRun; rank++)
        {
            // The rank-th best orientation, found by scanning; four entries at
            // most, so a sort buys nothing over a scan per rank.
            float bestScore = float.NegativeInfinity;
            int pickRot = -1;
            DoorRun pickRun = default;
            bool picked = false;

            for (int step = 0; step < 4; step++)
            {
                int tryRot = rotatable ? step : rot;

                float entryDot = float.PositiveInfinity;
                float exitDot = float.NegativeInfinity;
                DoorRun entryRun = default;
                bool haveEntry = false;
                foreach (var run in shape.doorAnchors)
                {
                    var outward = RotateLocal(run.outward, tryRot, mirror);
                    if (outward == Vector2Int.zero) continue;
                    var outv = new Vector2(outward.x, outward.y).normalized;
                    float d = Vector2.Dot(heading, outv);
                    if (d < entryDot) { entryDot = d; entryRun = run; haveEntry = true; }
                    if (d > exitDot) exitDot = d;
                }
                if (!haveEntry) { if (!rotatable) break; continue; }

                // Spur class: one door, teed off square. Threading class: the
                // widest opposed spread.
                float score = spurClass
                    ? -Mathf.Abs(entryDot)
                    : exitDot - entryDot;

                // Strictly-greater keeps the tie order stable across ranks.
                if (score > bestScore)
                {
                    // Skip orientations already tried at earlier ranks.
                    bool seen = false;
                    for (int r2 = 0; r2 < rank && !seen; r2++)
                        if (triedRots[r2] == tryRot) seen = true;
                    if (!seen)
                    {
                        bestScore = score;
                        pickRot = tryRot;
                        pickRun = entryRun;
                        picked = true;
                    }
                }
                if (!rotatable) break;
            }

            if (!picked) break;
            triedRots[rank] = pickRot;

            if (TryGateCell(shape, pickRun, pickRot, mirror, out gate))
            {
                bestRot = pickRot;
                bestRun = pickRun;
                haveRun = true;
            }
            if (!rotatable) break;
        }

        if (!haveRun)
        {
            headingFault = true;
            return false;
        }

        rot = bestRot;

        // THE GATE IS A CELL, NOT THE RUN'S MIDDLE.
        //
        // In a vertical wall the drape runs ALONG the door, so a three-cell door
        // has only its southernmost cell walkable: y+2 is the wall above the
        // run. Measured across every plan and rotation: 216 door runs, 34 with a
        // buried middle, and ZERO with no walkable cell at all. The walkable
        // cell nearest the middle was chosen by the ranked selection above.
        var inNormal = RotateLocal(bestRun.outward, bestRot, mirror);

        if (spurClass)
        {
            // THE SPUR CLASS: the road does not pass through this site, so its
            // gate must not sit ON the chord -- that is what ground the vault's
            // jambs away, a carriageway drawn tangentially along the door face.
            // The site STANDS OFF along its door's outward normal, far enough
            // that every cell of it clears the carriageway, and a spur is
            // emitted later from the take-off to the door, arriving square
            // along the normal by construction.
            if (!TrySpurStandoff(shape, bestRot, mirror, anchor, inNormal, gate,
                                 roadPlan, chordIndex, out placeAt))
                return false;

            long sdx = placeAt.x - centre.x, sdy = placeAt.y - centre.y;
            long sDistSq = sdx * sdx + sdy * sdy;
            if (sDistSq < (long)inner * inner || sDistSq > (long)outer * outer)
                return false;
            if (TooClose(placeAt, anchorsUsed, minSpacingSq)) return false;

            // No along-chord clamp: the standoff already guarantees the chord
            // cannot touch the footprint, whatever their projections overlap.
            if (!FootprintClearsChords(shape, placeAt, bestRot, mirror,
                                       roadPlan, chordIndex)) return false;

            seat = new SiteChordSeat
            {
                chordIndex = chordIndex,
                gateIn = new Vector3Int(placeAt.x + gate.x, placeAt.y + gate.y, 0),
                inNormal = inNormal,
                spur = true,
                takeoff = anchor,
            };
            return true;
        }

        placeAt = new Vector3Int(anchor.x - gate.x, anchor.y - gate.y, 0);

        // RE-VALIDATE. TryPickAnchor vetted the CHORD point, and the building now
        // sits tens of cells away from it -- thirty-seven on a village -- so
        // every test it passed describes somewhere the site is not.
        long dx = placeAt.x - centre.x, dy = placeAt.y - centre.y;
        long distSq = dx * dx + dy * dy;
        if (distSq < (long)inner * inner || distSq > (long)outer * outer) return false;
        if (TooClose(placeAt, anchorsUsed, minSpacingSq)) return false;

        // THE FOOTPRINT MUST CLEAR BOTH OCCUPIED CHORD ENDS. Clamping on the
        // gates instead is what put centrelines on masonry, and a FREE end --
        // nodeA or nodeB at -1 -- is exempt, or nothing could ever seat at a
        // road's end: the whole point of a SealedGate is to sit where the road
        // stops, and the old both-ends clamp refused every such seat and sent
        // the gates to the free-scatter fallback.
        if (!FootprintClearsChordEnds(shape, placeAt, bestRot, mirror,
                                      roadPlan, chordIndex)) return false;

        // THE SEAT IS RECORDED, NOT ACTED ON. This placement can still be thrown
        // out afterwards -- by the disc clamp, the twelve-cell floor or the
        // walkability guard -- and a chord split for a site that was never
        // placed would leave the network cut around nothing.
        seat = new SiteChordSeat
        {
            chordIndex = chordIndex,
            gateIn = new Vector3Int(placeAt.x + gate.x, placeAt.y + gate.y, 0),
            inNormal = inNormal,
        };

        if (TryLaneThrough(shape, bestRot, mirror, heading, bestRun, gate,
                           out var gateOutLocal, out var outNormal, out var lane))
        {
            seat.gateOut = new Vector3Int(
                placeAt.x + gateOutLocal.x, placeAt.y + gateOutLocal.y, 0);
            seat.outNormal = outNormal;
            foreach (var c in lane)
                seat.laneCells.Add(new Vector3Int(placeAt.x + c.x, placeAt.y + c.y, 0));
            seat.hasBothGates = seat.laneCells.Count >= 2;
        }

        // And clear of every OTHER chord. The one this site answered to is
        // exempt: a laned site splits it at its own gates. A second chord
        // crossing the building was never asked for by anything.
        if (!FootprintClearsChords(shape, placeAt, bestRot, mirror,
                                   roadPlan, chordIndex)) return false;

        return true;
    }

    /// <summary>
    /// Seats a spur-class site off the chord: the gate at the smallest standoff
    /// along its outward normal at which EVERY cell of the site -- masonry
    /// included -- clears the host chord's carriageway by a cell.
    ///
    /// Exact, per cell, against the segment. The bounding-circle test used for
    /// OTHER chords is conservative and cheap because it runs against the whole
    /// plan; this runs against one segment, once per successful seat, and the
    /// vault's cross shape would pay about ten cells of unnecessary standoff to
    /// a circle. Search is outward from the smallest plausible distance; a site
    /// that cannot clear within MaxStandoff refuses the anchor.
    /// </summary>
    private static bool TrySpurStandoff(
        LocalPlan shape, int rot, bool mirror, Vector3Int anchor, Vector2Int normal,
        Vector2Int gate, RoadPlan plan, int chordIndex, out Vector3Int placeAt)
    {
        placeAt = default;
        if (normal == Vector2Int.zero) return false;
        if (plan == null || chordIndex < 0 || chordIndex >= plan.chords.Count) return false;
        var chord = plan.chords[chordIndex];
        if (chord == null) return false;

        // The gate cell chosen for this run, in rotated local space; the site is
        // placed so that cell lands at anchor + normal * D.
        var cells = new List<Vector2Int>();
        foreach (var c in shape.floor) cells.Add(RotateLocal(c, rot, mirror));
        foreach (var c in shape.wall) cells.Add(RotateLocal(c, rot, mirror));

        double clear = chord.width * 0.5 + 1.0;
        for (int d = MinStandoff; d <= MaxStandoff; d++)
        {
            // MINUS the outward normal. Outward points from the door TOWARD
            // the road, so the gate steps back from the take-off along it and
            // the building -- which extends behind the door, opposite outward
            // -- moves further from the chord with every cell of standoff.
            // With the sign the other way the building straddles the chord and
            // only clears by being pushed entirely through to the far side,
            // which the stand-in measured as vaults seating 0 of 40 and a
            // guard post needing a 20-cell spur.
            var gateWorld = new Vector3Int(
                anchor.x - normal.x * d, anchor.y - normal.y * d, 0);
            bool ok = true;
            foreach (var c in cells)
            {
                // World cell = gateWorld + (rotated local cell - rotated gate
                // cell): the site is placed so the chosen gate cell lands
                // exactly at gateWorld.
                double px = gateWorld.x + c.x - gate.x;
                double py = gateWorld.y + c.y - gate.y;
                if (PointToSegment(px, py, chord.a, chord.b) < clear)
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
            {
                placeAt = new Vector3Int(
                    gateWorld.x - gate.x, gateWorld.y - gate.y, 0);
                return true;
            }
        }
        return false;
    }

    /// <summary>The nearest a spur gate may sit to its chord. Below this the
    /// jambs are inside the carriageway whatever the footprint does.</summary>
    private const int MinStandoff = 4;

    /// <summary>Past this a spur reads as its own road, not an approach; a site
    /// that cannot clear within it refuses the anchor and retries elsewhere.</summary>
    private const int MaxStandoff = 24;

    /// <summary>
    /// The cell a road meets a gate at: the walkable run cell nearest the run's
    /// middle, in ROTATED local space.
    ///
    /// Walkability is judged with the approach already carved outside the gate,
    /// which is what the engine will see. Judging the plan on its own instead
    /// makes every cell outside the footprint a drape source and marks every
    /// north-facing gate buried -- TileInfluenceManager.DrapesFrom returns false
    /// for any MINED cell, and the road about to be carved outside that gate is
    /// mined. That error reported seven of sixteen laned plans as unplaceable.
    /// </summary>
    private static bool TryGateCell(
        LocalPlan shape, DoorRun run, int rot, bool mirror, out Vector2Int gate)
    {
        gate = default;
        int len = Mathf.Max(1, run.length);

        // BUILT IN ROTATED SPACE, from the rotated middle and the rotated
        // normal. Building it unrotated and turning each cell afterwards is not
        // the same run when the length is EVEN: `mid - step * (len / 2)` is not
        // symmetric about the middle, so the two constructions sit one cell
        // apart. Caught by diffing the chosen cell against the sim over all 216
        // runs -- one disagreed, and it was a four-cell door.
        var normal = RotateLocal(run.outward, rot, mirror);
        var midR = RotateLocal(run.mid, rot, mirror);
        var step = normal.x != 0
            ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
        var startR = midR - step * (len / 2);

        // The floor set, rotated once.
        var floorR = new HashSet<Vector2Int>();
        foreach (var c in shape.floor) floorR.Add(RotateLocal(c, rot, mirror));

        bool found = false;
        int bestDist = int.MaxValue;
        for (int k = 0; k < len; k++)
        {
            var cell = startR + step * k;
            if (!GateCellWalkable(floorR, cell, normal)) continue;

            int d = Mathf.Abs(cell.x - midR.x) + Mathf.Abs(cell.y - midR.y);
            if (d < bestDist) { bestDist = d; gate = cell; found = true; }
        }
        return found;
    }

    /// <summary>The drape, as TileInfluenceManager.IsUnderOverhang applies it: a
    /// cell is blocked when the cell one or two NORTH of it is unmined. Floor
    /// cells are mined, and so is the carriageway carved outside the gate.</summary>
    private static bool GateCellWalkable(
        HashSet<Vector2Int> floorR, Vector2Int cell, Vector2Int normal)
    {
        return MinedAt(floorR, cell, normal, new Vector2Int(cell.x, cell.y + 1))
            && MinedAt(floorR, cell, normal, new Vector2Int(cell.x, cell.y + 2));
    }

    /// <summary>True when a cell will be open ground: inside the plan's floor, or
    /// on the approach the road is about to carve outside this gate. The
    /// approach is treated as one trunk wide either side of its centre, which is
    /// what Dilate will paint.</summary>
    private static bool MinedAt(
        HashSet<Vector2Int> floorR, Vector2Int gate, Vector2Int normal, Vector2Int at)
        => floorR.Contains(at) || OnApproach(at, gate, normal);

    /// <summary>True where the road about to be carved outside a gate will open
    /// ground: out along the gate's normal, one trunk wide. One definition,
    /// shared by the threshold test and the lane corridor, so the two cannot
    /// drift apart.</summary>
    private static bool OnApproach(Vector2Int at, Vector2Int gate, Vector2Int normal)
    {
        int along = (at.x - gate.x) * normal.x + (at.y - gate.y) * normal.y;
        if (along <= 0 || along > ApproachProbeCells) return false;

        int across = normal.x != 0 ? at.y - gate.y : at.x - gate.x;
        return Mathf.Abs(across) <= ApproachProbeHalfWidth;
    }

    /// <summary>How far out to assume carved approach when testing a threshold.
    /// Only the two cells north of a gate are ever asked about, so anything past
    /// two gives the same answer; four is chosen for legibility.</summary>
    private const int ApproachProbeCells = 4;

    /// <summary>Half a trunk. Dilate paints the carriageway this far either side
    /// of the centreline.</summary>
    private const int ApproachProbeHalfWidth = 2;

    /// <summary>
    /// True when the seated footprint is clear of every chord except the one it
    /// answered to.
    ///
    /// The exempt chord is the point: a laned site SPLITS the chord it seats on,
    /// so the road arrives at a gate rather than crossing the building, and an
    /// unlaned doored site is meant to have the road reach its door. Any other
    /// chord passing through the footprint was asked for by nothing, and since
    /// nothing subtracts a carriageway out of a site any more it would mine the
    /// masonry it crossed and leave the site claiming walls that are not there.
    ///
    /// Measured on the bounding circle, which is conservative in the safe
    /// direction -- it can refuse a placement that would have fitted, never
    /// accept one that would not. Cost of the rule, sampling in-band positions
    /// against 20 planned networks per floor: the worst case is the largest
    /// village on floor 2 at 54 per cent of positions clear, and everything else
    /// sits between 85 and 100.
    /// </summary>
    private static bool FootprintClearsChords(
        LocalPlan shape, Vector3Int placeAt, int rot, bool mirror,
        RoadPlan plan, int exceptChordIndex)
    {
        if (plan == null || !plan.valid) return true;

        if (!FootprintBounds(shape, out int minX, out int minY,
                             out int maxX, out int maxY)) return true;

        // The bounding circle of the rotated box, about its own centre. A
        // quarter turn maps an axis-aligned box to an axis-aligned box, so the
        // radius is the same whichever way it is turned.
        double cx = placeAt.x + (minX + maxX) * 0.5;
        double cy = placeAt.y + (minY + maxY) * 0.5;
        if (rot == 1 || rot == 3)
        {
            cx = placeAt.x - (minY + maxY) * 0.5;
            cy = placeAt.y + (minX + maxX) * 0.5;
        }
        double hx = (maxX - minX) * 0.5, hy = (maxY - minY) * 0.5;
        double radius = System.Math.Sqrt(hx * hx + hy * hy);

        for (int i = 0; i < plan.chords.Count; i++)
        {
            if (i == exceptChordIndex) continue;
            var c = plan.chords[i];
            if (c == null || c.kind == RoadKind.Lane) continue;
            if (PointToSegment(cx, cy, c.a, c.b) < radius + c.width * 0.5 + 1.0) return false;
        }
        return true;
    }

    /// <summary>Shortest distance from a point to a segment.</summary>
    private static double PointToSegment(double px, double py, Vector3Int a, Vector3Int b)
    {
        double vx = b.x - a.x, vy = b.y - a.y;
        double wx = px - a.x, wy = py - a.y;
        double len2 = vx * vx + vy * vy;
        double t = len2 <= 0.0 ? 0.0 : (wx * vx + wy * vy) / len2;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;
        double dx = a.x + vx * t - px, dy = a.y + vy * t - py;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The plan's local bounding box over floor and masonry.</summary>
    private static bool FootprintBounds(
        LocalPlan shape, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = int.MaxValue; minY = int.MaxValue;
        maxX = int.MinValue; maxY = int.MinValue;
        foreach (var c in shape.floor)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        foreach (var c in shape.wall)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        return minX <= maxX;
    }

    /// <summary>
    /// True when the seated footprint leaves a full approach stub at BOTH ends of
    /// the chord it answers to.
    ///
    /// Measured on the bounding box corners rather than on every cell: a 90
    /// degree rotation maps an axis-aligned box to an axis-aligned box, so the
    /// extreme projections onto the chord are always corners, and a village is
    /// 3721 cells against 240 placement attempts.
    /// </summary>
    private static bool FootprintClearsChordEnds(
        LocalPlan shape, Vector3Int placeAt, int rot, bool mirror,
        RoadPlan plan, int chordIndex)
    {
        if (plan == null || chordIndex < 0 || chordIndex >= plan.chords.Count) return true;
        var chord = plan.chords[chordIndex];
        if (chord == null) return true;

        double dx = chord.b.x - chord.a.x, dy = chord.b.y - chord.a.y;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1.0) return true;
        double ux = dx / len, uy = dy / len;

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in shape.floor)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        foreach (var c in shape.wall)
        {
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        if (minX > maxX) return true;

        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var corner = new Vector2Int((i & 1) == 0 ? minX : maxX,
                                        (i & 2) == 0 ? minY : maxY);
            var r = RotateLocal(corner, rot, mirror);
            double s = (placeAt.x + r.x - chord.a.x) * ux
                     + (placeAt.y + r.y - chord.a.y) * uy;
            if (s < lo) lo = s;
            if (s > hi) hi = s;
        }

        // A FREE end is exempt -- nodeA or nodeB at -1. A SealedGate exists to
        // sit where the road stops, and requiring stub room past a free end
        // refused every RoadEnd seat and sent the gates to the free-scatter
        // fallback: the floor 4 log showed two road ends in band and every gate
        // placed far from them.
        int stub = RoadNetworkBuilder.GateMinStub;
        bool clearA = chord.nodeA < 0 || lo >= stub;
        bool clearB = chord.nodeB < 0 || hi <= len - stub;
        return clearA && clearB;
    }


    /// <summary>
    /// Cells a unit could actually stand on, under the same rule the pathfinder
    /// uses: a mined cell is blocked if there is unmined rock one OR two cells
    /// north of it, because that is the wall face draping down over it.
    ///
    /// Deliberately CONSERVATIVE -- anything outside this site's own carved set is
    /// treated as rock, including a neighbouring road or chamber that happens to be
    /// mined. A site that passes in isolation passes in context.
    /// </summary>
    private static int CountWalkable(List<Vector3Int> cells)
    {
        var set = new HashSet<Vector3Int>(cells);
        int n = 0;
        foreach (var c in cells)
        {
            if (!set.Contains(new Vector3Int(c.x, c.y + 1, 0))) continue;
            if (!set.Contains(new Vector3Int(c.x, c.y + 2, 0))) continue;
            n++;
        }
        return n;
    }

    private static int RandomRange(System.Random rng, int min, int max)
    {
        if (max < min) (min, max) = (max, min);
        return rng.Next(min, max + 1);
    }
}
