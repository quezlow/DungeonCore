using System.Collections.Generic;
using UnityEngine;

/// <summary>One placed site: an archetype, the plan variant it was built from,
/// its anchor, and the two cell sets that make it read as architecture.</summary>
public class AncientSitePlan
{
    public int id;
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

    public bool reservedForOutpost;

    /// <summary>The guaranteed village, when SiteFloorEntry.reserveVillage placed
    /// this plan. Carried into SiteData so DwarvenVillageController can find its
    /// site the same way the outpost controller finds the hold.</summary>
    public bool reservedForVillage;
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
    /// <summary>Whether this floor's guaranteed outpost actually landed. False on
    /// a floor that never asked for one; false AND loud on a floor that did.</summary>
    public bool outpostPlaced;

    /// <summary>Same contract for the guaranteed village.</summary>
    public bool villagePlaced;

    /// <summary>The @name of the hold the seeded roll chose -- the report
    /// prints it, so rotation variety is verifiable headlessly by stepping
    /// the report seed instead of walking the map.</summary>
    public string villagePlanPicked = "";
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
        return $"placed {sites.Count}/{wanted} from a pool of {planPoolSize} plans " +
               $"in {attempts} attempts; rejected: no-anchor {rejectedNoAnchor}, " +
               $"too-close {rejectedTooClose}, null-shape {rejectedNullShape}, " +
               $"too-small {rejectedTooSmall}, unwalkable {rejectedUnwalkable}. " +
               $"In band: {inBandJunctions} junctions, {inBandRoadCells} road samples, " +
               $"{inBandRoadEnds} road ends";
    }
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
        IReadOnlyList<Vector3Int> junctions,
        IReadOnlyList<Vector3Int> roadCells,
        IReadOnlyList<Vector3Int> roadEnds,
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
        if (want == 0)
        {
            result.abortReason = $"rolled 0 sites from minSites {entry.minSites} / maxSites {entry.maxSites}";
            return result;
        }

        var plans = BuildPlanPool(rng, entry, authoredPlans);
        result.planPoolSize = plans.Count;
        if (plans.Count == 0)
        {
            result.abortReason = "plan pool is EMPTY -- the floor entry's roster resolved to no " +
                                 "archetypes at all. Check useAllArchetypes / the pool list.";
            return result;
        }

        var anchorsUsed = new List<Vector3Int>();
        int minSpacingSq = Mathf.Max(1, entry.minSpacing) * Mathf.Max(1, entry.minSpacing);

        // The outpost goes down FIRST and on purpose. The old rule latched
        // reservedForOutpost onto whichever Sealed Gate the shuffled pool happened
        // to serve, which failed two different ways on the gatehouse floor: the roster
        // holds five archetypes and the floor rolls three to five sites, so a run
        // could finish with no Sealed Gate and therefore no dwarves at all; and the
        // Sealed Gate's own RoadEnd preference resolves, on a rim-to-rim trunk with
        // no broken ends, to the two rim endpoints -- both outside the placement
        // band -- so it degraded to a free pick and put the outpost nowhere near the
        // road it is supposed to hold.
        if (entry.reserveOutpost)
            PlaceOutpost(rng, entry, centre, inner, outer, usable,
                         junctions, roadCells, roadEnds,
                         plans, anchorsUsed, minSpacingSq, result);

        // The village rides the same guarantee, selected BY NAME from the
        // authored set rather than through the pool -- its archetype sits in no
        // roster, so the fill loop can never serve it and there is no pool
        // bookkeeping to do on success.
        if (entry.reserveVillage)
            PlaceVillage(rng, entry, centre, inner, outer, usable,
                         junctions, roadCells, roadEnds,
                         authoredPlans, anchorsUsed, minSpacingSq, result);

        // Guarantee-only plans (authored "@general: no") never reach the fill
        // loop. A guarantee that took one already removed it; this strips
        // whatever no guarantee consumed -- the outpost's hold sitting in floor
        // index 4's all-archetypes roster being the case that motivated it.
        plans.RemoveAll(p => p.authored != null && !p.authored.generalPool);

        int planCursor = 0;
        int attempts = 0;
        int maxAttempts = want * 12;

        // How much of each anchor source actually falls inside the placement band.
        // A source can be large and still be useless: road ENDS sit at the rim by
        // definition and the band stops at 65 per cent of the radius, so most of
        // them are out of bounds before spacing is ever considered.
        result.inBandJunctions = CountInBand(junctions, centre, inner, outer);
        result.inBandRoadCells = CountInBand(roadCells, centre, inner, outer);
        result.inBandRoadEnds = CountInBand(roadEnds, centre, inner, outer);

        while (result.sites.Count < want && attempts < maxAttempts)
        {
            attempts++;
            result.attempts = attempts;

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
                               junctions, roadCells, roadEnds,
                               anchorsUsed, minSpacingSq, out var anchor))
            {
                // The sampler already exhausted its budget looking for somewhere
                // both in band and clear of the sites already placed, so this is a
                // genuinely full floor rather than one unlucky draw.
                result.rejectedTooClose++;
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
                result.rejectedNullShape++;
                continue;
            }

            bool rotatable = plan.authored == null || plan.authored.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            var placed = new AncientSitePlan
            {
                archetype = plan.archetype,
                variant = plan.variant,
                planName = plan.authored != null ? plan.authored.name : "",
                anchor = anchor,
            };

            long clampSq = (long)usable * usable;
            EmitTransformed(site.floor, anchor, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(site.wall, anchor, rot, mirror, centre, clampSq, placed.ruinsCells);

            // A site reduced to a handful of cells by the disc clamp is not a site.
            if (placed.cells.Count < 12)
            {
                result.rejectedTooSmall++;
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
                result.rejectedUnwalkable++;
                continue;
            }

            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(anchor);
            // Cursor already advanced at the top of the attempt.
        }

        return result;
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
        IReadOnlyList<Vector3Int> junctions,
        IReadOnlyList<Vector3Int> roadCells,
        IReadOnlyList<Vector3Int> roadEnds,
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

        for (int attempt = 0; attempt < 240; attempt++)
        {
            var plan = candidates[attempt % candidates.Count];

            if (!TryPickAnchor(rng, entry.outpostAnchor, centre, inner, outer,
                               junctions, roadCells, roadEnds,
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

            var placed = new AncientSitePlan
            {
                archetype = plan.archetype,
                variant = plan.variant,
                planName = plan.authored != null ? plan.authored.name : "",
                anchor = anchor,
                reservedForOutpost = true,
            };
            EmitTransformed(shape.floor, anchor, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(shape.wall, anchor, rot, mirror, centre, clampSq, placed.ruinsCells);

            if (placed.cells.Count < 12) continue;
            if (CountWalkable(placed.cells) < MinWalkableCells) continue;

            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(anchor);
            plans.Remove(plan);
            result.outpostPlaced = true;
            return;
        }

        Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
            " failed to place its guaranteed outpost in 240 attempts. That floor " +
            "will have no dwarves. Most likely cause: the placement band holds no " +
            "road cells -- check inBandRoadCells in the site report.");
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
        IReadOnlyList<Vector3Int> junctions,
        IReadOnlyList<Vector3Int> roadCells,
        IReadOnlyList<Vector3Int> roadEnds,
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

        for (int attempt = 0; attempt < 240; attempt++)
        {
            if (!TryPickAnchor(rng, anchorKind, centre, inner, outer,
                               junctions, roadCells, roadEnds,
                               anchorsUsed, minSpacingSq, out var anchor))
                continue;

            LocalPlan shape = FromAuthored(plan);
            if (shape == null) continue;

            bool rotatable = plan.allowRotation;
            int rot = rotatable ? rng.Next(0, 4) : 0;
            bool mirror = rotatable && rng.Next(0, 2) == 0;

            var placed = new AncientSitePlan
            {
                archetype = SiteArchetype.DwarvenVillage,
                // The candidate index, persisted through SiteData.variant as a
                // breadcrumb for which hold this world rolled.
                variant = pick,
                planName = plan.name,
                anchor = anchor,
                reservedForVillage = true,
            };
            EmitTransformed(shape.floor, anchor, rot, mirror, centre, clampSq, placed.cells);
            EmitTransformed(shape.wall, anchor, rot, mirror, centre, clampSq, placed.ruinsCells);

            if (placed.cells.Count < 12) continue;
            if (CountWalkable(placed.cells) < MinWalkableCells) continue;

            placed.id = result.sites.Count;
            result.sites.Add(placed);
            anchorsUsed.Add(anchor);
            result.villagePlaced = true;
            result.villagePlanPicked = plan.name;
            return;
        }

        Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +
            " failed to place its guaranteed village in 240 attempts. That floor " +
            "will have no dwarves at home. Most likely cause: the placement band " +
            "holds no road cells -- check inBandRoadCells in the site report.");
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
        foreach (var c in authored.floor)
            if (!p.wall.Contains(c)) p.floor.Add(c);
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
        IReadOnlyList<Vector3Int> junctions,
        IReadOnlyList<Vector3Int> roadCells,
        IReadOnlyList<Vector3Int> roadEnds,
        List<Vector3Int> anchorsUsed, int minSpacingSq,
        out Vector3Int anchor)
    {
        IReadOnlyList<Vector3Int> source = null;
        switch (kind)
        {
            case SiteAnchor.Junction: source = junctions; break;
            case SiteAnchor.AlongRoad: source = roadCells; break;
            case SiteAnchor.Crossing: source = roadCells; break;
            case SiteAnchor.RoadEnd: source = roadEnds; break;
        }

        if (source != null && source.Count > 0)
        {
            // Sample rather than scan: a floor's thinned centreline runs to
            // hundreds of cells and this is called on every placement attempt.
            for (int i = 0; i < 64; i++)
            {
                var c = source[rng.Next(0, source.Count)];
                if (!InBand(c, centre, inner, outer)) continue;
                if (TooClose(c, anchorsUsed, minSpacingSq)) continue;
                anchor = c;
                return true;
            }
        }

        // Degrade to Free. A preference that cannot be met is not a failure --
        // it is a floor where that kind of road does not exist, or one whose
        // roads are already lined with ruins.
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

    private static void EmitTransformed(
        HashSet<Vector2Int> local, Vector3Int anchor, int rot, bool mirror,
        Vector3Int floorCentre, long clampSq, List<Vector3Int> into)
    {
        var seen = new HashSet<Vector3Int>();
        foreach (var p in local)
        {
            int x = mirror ? -p.x : p.x;
            int y = p.y;

            int rx, ry;
            switch (rot & 3)
            {
                case 1: rx = -y; ry = x; break;
                case 2: rx = -x; ry = -y; break;
                case 3: rx = y; ry = -x; break;
                default: rx = x; ry = y; break;
            }

            var c = new Vector3Int(anchor.x + rx, anchor.y + ry, 0);
            long dx = c.x - floorCentre.x, dy = c.y - floorCentre.y;
            if (dx * dx + dy * dy > clampSq) continue;
            if (seen.Add(c)) into.Add(c);
        }
    }

    /// <summary>Fewest walkable cells a placed site may have. Below this the ruin
    /// reads as a room and behaves as a wall, which is worse than not generating it.</summary>
    private const int MinWalkableCells = 16;

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
