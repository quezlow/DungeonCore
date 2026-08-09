using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One hand-authored site plan, parsed from an ASCII grid. Sits alongside the
/// procedural recipes in AncientSiteBuilder rather than replacing them: an
/// authored plan is simply an extra VARIANT of its archetype, so it inherits
/// the whole placement layer -- band, anchors, spacing, disc clamp, rotation,
/// the walkability guard, save, reveal and terrain override -- unchanged.
/// </summary>
/// <summary>
/// What a plan says about its own doors. AUTHORING-TIME ONLY -- this never
/// reaches a save, so it carries no append-only obligation.
///
/// Three states rather than two, and the third is the one that earns its keep.
/// With only "none" and "marked", a plan nobody has annotated yet is
/// indistinguishable from a plan with genuinely nothing to annotate, and the
/// gate silently protects nothing. `Unmarked` says "not looked at", so the
/// report can keep a roll-call.
///
/// `Absent` is not a state an author can write. It means the header is missing,
/// and the validator FAILS on it, so the question cannot be dodged by omission.
/// </summary>
public enum DoorPolicy
{
    Absent = 0,
    Unmarked,
    None,
    Marked,
}

public class AuthoredSitePlan
{
    public string name = "";
    public string sourceName = "";
    public SiteArchetype archetype = SiteArchetype.GuardPost;

    /// <summary>From "@doors:". Absent unless the plan says otherwise, and the
    /// validator will not pass a plan that leaves it Absent.</summary>
    public DoorPolicy doorPolicy = DoorPolicy.Absent;

    /// <summary>Set by "@anchor_on: door". The placement then offsets the plan
    /// so a DECLARED DOOR lands on the anchor rather than the bounding-box
    /// centre, and rejects anchors the door does not face.
    ///
    /// Opt-in per plan, because it is wrong for most of them. A dwarven hold
    /// WANTS the road cutting through it -- that is what its four gates are for.
    /// A sealed vault wants the road arriving at its one entrance and stopping.
    /// </summary>
    public bool anchorOnDoor;

    /// <summary>Set by "@anchor_required: yes". TryPickAnchor normally degrades
    /// to a free pick when a preference cannot be met, which is right for a ruin
    /// and wrong for a building whose meaning IS its position: a Ward Chapel is
    /// the chapel a sealing was administered from, and a free pick strands it in
    /// open rock with nothing to explain it. A plan that refuses the fallback is
    /// SKIPPED, with a reason, rather than misplaced.</summary>
    public bool anchorRequired;

    /// <summary>Set only when the plan declared its own @anchor. Otherwise the
    /// archetype's fixed preference applies.</summary>
    public SiteAnchor anchorOverride = SiteAnchor.Free;
    public bool hasAnchorOverride;

    /// <summary>Cleared by "@rotate: no" for a plan whose orientation carries
    /// meaning. Costs variety, so it is off by default.</summary>
    public bool allowRotation = true;

    /// <summary>Cleared by "@general: no" for a plan that only a guarantee pass
    /// (reserveOutpost / reserveVillage) may place. Build strips such plans from
    /// the pool after the guarantees run, so the outpost's own hold can never
    /// appear as an ordinary dead ruin on a floor whose roster holds its
    /// archetype -- which floor index 4's all-archetypes roster otherwise
    /// would.</summary>
    public bool generalPool = true;

    /// <summary>Relation headers. Each names an ARCHETYPE rather than a plan:
    /// "a ward chapel near a sealed crypt" is archetype talk, and a plan
    /// rename must not silently break a relation. All of them resolve at
    /// PLACEMENT TIME against the sites already placed and the pools on
    /// offer, so none of them adds a save field.</summary>
    public SiteArchetype excludes;
    public bool hasExcludes;
    public SiteArchetype prefersNear;
    public bool hasPrefersNear;
    public SiteArchetype requiresNear;
    public bool hasRequiresNear;
    public SiteArchetype pair;
    public bool hasPair;

    /// <summary>Target anchor separation for '@pair:', in cells. 24 is
    /// measured, not chosen: at 16 the partners' own footprints collide
    /// before spacing is ever tested (pair seating fell to 8-17 per cent in
    /// sim_site_relations), and 32 buys nothing over 24's 98-100.</summary>
    public int pairGap = 24;

    /// <summary>Absolute 'near' radius in cells; 0 derives 1.5x the floor's
    /// minSpacing. Also measured: nearest-neighbour distances under TooClose
    /// START at minSpacing, so any radius below it is unsatisfiable by
    /// construction, and 1.25x already dips under 98 per cent.</summary>
    public int nearRadius;

    /// <summary>Local cells, centred on the plan's bounding box.</summary>
    public readonly List<Vector2Int> floor = new List<Vector2Int>();
    public readonly List<Vector2Int> wall = new List<Vector2Int>();

    /// <summary>The heart cell, from the plan's single 'X': the altar, grave
    /// slab, capped font or seal-stone. SOLID -- it is added to `wall` as well
    /// as here, because desecration is UNSEALING and an open floor cell cannot
    /// be mined. This is a marker on a cell that is already masonry, not a
    /// third kind of cell.
    ///
    /// A list rather than a nullable so the parser can report "two hearts" as
    /// an authoring error instead of silently keeping the last one.</summary>
    public readonly List<Vector2Int> heart = new List<Vector2Int>();

    /// <summary>Raised platform floor, from the plan's '=' cells. Parsed into
    /// `floor` as well, so every existing consumer -- rendering, walkability,
    /// reveal, the drape -- sees ordinary open ground and needs no special case.
    ///
    /// The raise is REAL without any elevation system, because PlayerMovement is
    /// Rigidbody2D with collider-based blocking rather than tile pathing: the
    /// platform edge is masonry, which already stops the avatar, and the stairs
    /// are the only gap in it. This list exists so the raised floor sprites know
    /// their extent, and so the validator can prove the edge has no accidental
    /// hole in it.</summary>
    public readonly List<Vector2Int> platform = new List<Vector2Int>();

    /// <summary>Stair cells, from '^'. Floor, like the platform, and the only
    /// opening in its edge. Marked in the PLAN rather than left to the decor
    /// prefab so the geometry is the single source of truth: a prefab can be
    /// redrawn without anyone noticing the plan no longer agrees with it.</summary>
    public readonly List<Vector2Int> stairs = new List<Vector2Int>();

    /// <summary>Door cells, from '+'. Floor plus a marker, exactly as '=' and
    /// '^' are, so rendering, walkability, reveal and the drape all see ordinary
    /// open ground and no consumer needs a special case.
    ///
    /// DECLARED RATHER THAN INFERRED, and that is the whole design. The tightest
    /// structural definition of a door -- a floor run of two or fewer cells with
    /// masonry at BOTH ends -- returns 815 hits across the shipped plans, 213 in
    /// TheShrinehold alone, and every one of them is a stall gap, a grave row or
    /// column spacing rather than a door. A two-cell gap between grave slabs is
    /// structurally identical to a two-cell doorway. The first door-rule gate
    /// tried to tell them apart and failed twelve working plans doing it; this
    /// one does not guess.</summary>
    public readonly List<Vector2Int> door = new List<Vector2Int>();

    /// <summary>The door cells grouped into RUNS, each with its middle cell and
    /// the direction it opens outward.
    ///
    /// Computed once, here, because two things need it and they must not
    /// disagree: the validator's three-cell rule, and door anchoring's offset
    /// and heading test. A second run-finder in the editor layer is how the
    /// geometry that decides where a vault goes and the geometry that decides
    /// whether its door is legal drift apart.</summary>
    public readonly List<DoorRun> doorRuns = new List<DoorRun>();

    /// <summary>Lane cells, from '~': the route a road takes THROUGH this site.
    /// Floor plus a marker, so nothing downstream needs a special case.
    ///
    /// Authored rather than derived, for the same reason doors are. A road
    /// crossing a site used to punch its own hole and the site lost those cells;
    /// the lane says instead where the site EXPECTS a road, so the building
    /// keeps its shape and the road keeps its route. A site with no lane has no
    /// through-route: a road reaches its door and stops.</summary>
    public readonly List<Vector2Int> lane = new List<Vector2Int>();

    /// <summary>Keep-clear cells, from '-': floor the plan wants left EMPTY --
    /// the cell before a door, a sightline down a nave, an altar approach.
    /// Floor plus a marker, and consumers OPT IN: nothing reads it yet. The
    /// intended first consumer is paired placement's clearance test in the
    /// site relations arc, which is why the vocabulary ships ahead of the
    /// machinery.</summary>
    public readonly List<Vector2Int> keepClear = new List<Vector2Int>();

    /// <summary>Decor cells, from 'o': where this plan's decor piece stands.
    /// Marked in PLAN space so decor rotates with the plan -- the per-plan
    /// anchor prefab could not, which is what forced @rotate: no onto every
    /// prefab-decorated plan. Floor plus a marker; the piece spawns at the
    /// cell's world position with an UNROTATED transform, because props are
    /// authored front-view and a quarter-turned sprite reads wrong.</summary>
    public readonly List<Vector2Int> decor = new List<Vector2Int>();
}

/// <summary>One straight run of '+' cells: where it is, how long, and which way
/// it faces. `outward` is the perpendicular whose neighbouring cell is outside
/// the plan entirely -- a door opens onto the rock, not onto more building.</summary>
public struct DoorRun
{
    public Vector2Int mid;
    public Vector2Int outward;
    public int length;
}

/// <summary>
/// Parses the authored-plan text format.
///
/// THE FORMAT
///   Lines beginning '@' are metadata: "@archetype: SealedGate", "@name: ...",
///   "@anchor: RoadEnd", "@rotate: no". Lines beginning '//' are comments.
///   Everything else is the grid, read top to bottom as NORTH to SOUTH:
///     '#'  masonry  -- stays solid rock, retyped to the family terrain
///     '.'  carved   -- open floor
///     '='  raised PLATFORM floor -- open ground, plus a marker. The
///          raise is real without any elevation system: draw the
///          platform's edge as masonry and it already blocks the
///          Rigidbody2D avatar and every tile-pathed walker.
///     '^'  STAIRS -- open ground, and the only gap you leave in that
///          edge. Three cells wide; everything has to path it.
///     'X'  the HEART -- masonry, AND the one cell carrying the
///          site's meaning: altar, grave slab, capped font,
///          seal-stone. Solid, because unsealing means mining it.
///          Exactly one per plan; two is a parse error.
///     '~'  a LANE -- open ground, plus a marker: the route a road
///          takes THROUGH the site, door to door. Where the lane is cut
///          through MASONRY it needs three cells, and five to match a
///          five-wide gate: a floor cell is walkable only when y+1 and
///          y+2 are also floor, so a one-wide east-west lane is buried
///          under the drape, and a one-wide north-south lane works
///          until the plan rotates, which is worse. Across OPEN floor
///          a narrow lane is fine, because the open ground supplies the
///          clearance. The validator does not check width at all -- it
///          walks the lane and tells you whether a road could. A site
///          with no lane has no through-route and a road stops at its
///          door.
///     '+'  a DOOR -- open ground, plus a marker. Declared rather
///          than inferred: a two-cell gap between grave slabs is
///          structurally identical to a two-cell doorway, so
///          nothing but the author can tell them apart. Every
///          declared door must be THREE cells in its run; two
///          seals. Only '+' cells are checked, so stall gaps and
///          grave rows are exempt by construction.
///     '-'  KEEP-CLEAR -- open ground, plus a marker: floor the plan
///          wants left empty. The cell before a door, a sightline
///          down a nave, an altar approach. Consumers OPT IN, and
///          nothing reads it yet; the intended first consumer is
///          paired placement's clearance test.
///     'o'  DECOR -- open ground, plus a marker: where this plan's
///          decor piece stands. Marked in the plan so it rotates
///          WITH the plan, which the anchor-prefab hook could not.
///     anything else (including space) is not part of the site at all.
///          This is LOAD-BEARING, not a fallthrough:
///          BrokenAqueduct_TheDrySpan is two separate reaches with a
///          void between them where the middle span fell, and the
///          void is spaces. Do not tighten this into an error.
///
///   '@doors:' is REQUIRED and takes one of three values:
///     unmarked  not yet annotated. Passes, and the report keeps a
///               roll-call of them.
///     none      genuinely has no doors. Passes silently.
///     marked    doors are drawn with '+' and the three-cell rule is
///               enforced on them.
///   A missing header FAILS, so "not filled in yet" can never be
///   mistaken for "nothing to fill in".
///
///   RELATION HEADERS, all optional, all naming an ARCHETYPE:
///     '@excludes: X'       hard and SYMMETRIC: this plan and archetype X
///                          never share a floor; whichever places first
///                          strips the other from both pools.
///     '@prefers_near: X'   soft: pick an anchor near a placed X when one
///                          exists, place normally when the bias fails.
///     '@requires_near: X'  hard: no placed X in radius means this attempt
///                          refuses, with its own named counter.
///     '@pair: X'           actively places a partner of archetype X beside
///                          this plan, drawn from the floor's own pools
///                          (never summoned from outside them) and riding
///                          extraPlaced. '@pair_gap: N' sizes the anchor
///                          separation (default 24); '@near_radius: N'
///                          overrides the near radius (default 1.5x the
///                          floor's minSpacing). Near relations bias FREE
///                          anchoring only -- on a road-anchored plan
///                          requires_near degrades to a post-filter, which
///                          the tag audit warns about.
///
///   TWO RULES THE GRID WILL NOT SHOW YOU, both learned by drawing
///   eight plans and failing seven:
///     - A wall recess must be THREE deep. Two leaves a two-cell run
///       pinched between wall above and wall below, and that seals
///       under the drape on some quarter turns.
///     - A free-standing heart needs THREE clear cells on all four
///       sides, so a chamber of at least seven by seven.
///   Dungeon Core / Validate Site Plans enforces both.
///
///   Rows may be ragged; short rows are simply short. The grid is centred on
///   its own bounding box automatically, so the author never thinks about
///   origins.
///
/// WHY TEXT RATHER THAN A SCENE
///   Authoring in a scene would need two tilemaps, an editor bake step reading
///   cellBounds, and a re-bake on every tweak -- none of which exists in the
///   project. A monospace text file needs no tooling at all, diffs cleanly in
///   git, and can be read at a glance. The headless report already draws sites
///   as ASCII, so the authoring format and the debug output match.
/// </summary>
public static class AncientSitePlanLibrary
{
    /// <summary>Parses one plan. Returns null and sets `error` on a bad file --
    /// authoring mistakes are reported, never silently swallowed.</summary>
    public static AuthoredSitePlan Parse(string text, string sourceName, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "file is empty";
            return null;
        }

        var plan = new AuthoredSitePlan { sourceName = sourceName, name = sourceName };
        bool archetypeSet = false;

        var rows = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("//")) continue;

            if (trimmed.StartsWith("@"))
            {
                int colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                string key = trimmed.Substring(1, colon - 1).Trim().ToLowerInvariant();
                string val = trimmed.Substring(colon + 1).Trim();

                switch (key)
                {
                    case "archetype":
                        if (!TryParseArchetype(val, out var a))
                        {
                            error = "unknown @archetype '" + val + "'";
                            return null;
                        }
                        plan.archetype = a;
                        archetypeSet = true;
                        break;
                    case "name":
                        plan.name = val;
                        break;
                    case "anchor":
                        if (!TryParseAnchor(val, out var an))
                        {
                            error = "unknown @anchor '" + val + "'";
                            return null;
                        }
                        plan.anchorOverride = an;
                        plan.hasAnchorOverride = true;
                        break;
                    case "rotate":
                        plan.allowRotation = !(val.ToLowerInvariant() == "no"
                                            || val.ToLowerInvariant() == "false"
                                            || val == "0");
                        break;
                    case "general":
                        plan.generalPool = !(val.ToLowerInvariant() == "no"
                                          || val.ToLowerInvariant() == "false"
                                          || val == "0");
                        break;
                    case "anchor_required":
                        plan.anchorRequired = !(val.ToLowerInvariant() == "no"
                                             || val.ToLowerInvariant() == "false"
                                             || val == "0");
                        break;
                    case "anchor_on":
                        switch (val.ToLowerInvariant())
                        {
                            case "door": plan.anchorOnDoor = true; break;
                            case "centre":
                            case "center": plan.anchorOnDoor = false; break;
                            default:
                                error = "unknown @anchor_on '" + val +
                                        "' -- expected door or centre";
                                return null;
                        }
                        break;
                    case "doors":
                        // A wrong value is an ERROR rather than a silent fall
                        // back to Absent. Absent already fails the validator, so
                        // a typo would be caught either way -- but "unknown
                        // @doors 'markd'" sends the author to the right line,
                        // and "missing @doors header" sends them looking for a
                        // line that is already there.
                        switch (val.ToLowerInvariant())
                        {
                            case "unmarked": plan.doorPolicy = DoorPolicy.Unmarked; break;
                            case "none": plan.doorPolicy = DoorPolicy.None; break;
                            case "marked": plan.doorPolicy = DoorPolicy.Marked; break;
                            default:
                                error = "unknown @doors '" + val +
                                        "' -- expected unmarked, none or marked";
                                return null;
                        }
                        break;
                    case "excludes":
                        if (!TryParseArchetype(val, out var ex))
                        {
                            error = "unknown @excludes '" + val + "'";
                            return null;
                        }
                        plan.excludes = ex;
                        plan.hasExcludes = true;
                        break;
                    case "prefers_near":
                        if (!TryParseArchetype(val, out var pn))
                        {
                            error = "unknown @prefers_near '" + val + "'";
                            return null;
                        }
                        plan.prefersNear = pn;
                        plan.hasPrefersNear = true;
                        break;
                    case "requires_near":
                        if (!TryParseArchetype(val, out var rq))
                        {
                            error = "unknown @requires_near '" + val + "'";
                            return null;
                        }
                        plan.requiresNear = rq;
                        plan.hasRequiresNear = true;
                        break;
                    case "pair":
                        if (!TryParseArchetype(val, out var pr))
                        {
                            error = "unknown @pair '" + val + "'";
                            return null;
                        }
                        plan.pair = pr;
                        plan.hasPair = true;
                        break;
                    case "pair_gap":
                        if (!int.TryParse(val, out var pg) || pg < 2)
                        {
                            error = "bad @pair_gap '" + val +
                                    "' -- a cell count of 2 or more";
                            return null;
                        }
                        plan.pairGap = pg;
                        break;
                    case "near_radius":
                        if (!int.TryParse(val, out var nr) || nr < 1)
                        {
                            error = "bad @near_radius '" + val +
                                    "' -- a cell count of 1 or more";
                            return null;
                        }
                        plan.nearRadius = nr;
                        break;
                }
                continue;
            }

            // Leading blank lines are skipped so a file may breathe between its
            // header and its grid; blanks inside the grid are meaningful rows.
            if (trimmed.Length == 0 && rows.Count == 0) continue;
            rows.Add(raw);
        }

        while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
            rows.RemoveAt(rows.Count - 1);

        if (!archetypeSet)
        {
            error = "missing '@archetype:' header";
            return null;
        }

        var floor = new List<Vector2Int>();
        var wall = new List<Vector2Int>();
        var heart = new List<Vector2Int>();
        var platform = new List<Vector2Int>();
        var stairs = new List<Vector2Int>();
        var door = new List<Vector2Int>();
        var lane = new List<Vector2Int>();
        var keepClear = new List<Vector2Int>();
        var decor = new List<Vector2Int>();
        for (int r = 0; r < rows.Count; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                // Top of the file is NORTH, so the row index runs down -Y.
                if (row[c] == '#') wall.Add(new Vector2Int(c, -r));
                else if (row[c] == '.') floor.Add(new Vector2Int(c, -r));
                else if (row[c] == '=')
                {
                    // Floor AND platform, the same trick 'X' uses for
                    // masonry: downstream sees ordinary open ground.
                    var pf = new Vector2Int(c, -r);
                    floor.Add(pf);
                    platform.Add(pf);
                }
                else if (row[c] == '^')
                {
                    var sc = new Vector2Int(c, -r);
                    floor.Add(sc);
                    stairs.Add(sc);
                }
                else if (row[c] == '+')
                {
                    var dc = new Vector2Int(c, -r);
                    floor.Add(dc);
                    door.Add(dc);
                }
                else if (row[c] == '~')
                {
                    var lc = new Vector2Int(c, -r);
                    floor.Add(lc);
                    lane.Add(lc);
                }
                else if (row[c] == '-')
                {
                    var kc = new Vector2Int(c, -r);
                    floor.Add(kc);
                    keepClear.Add(kc);
                }
                else if (row[c] == 'o')
                {
                    var oc = new Vector2Int(c, -r);
                    floor.Add(oc);
                    decor.Add(oc);
                }
                else if (row[c] == 'X')
                {
                    // Masonry AND heart. Downstream sees an ordinary
                    // solid cell, so rendering, resistance and the
                    // pattern payout need no special case; the heart
                    // list is only a marker on it.
                    var h = new Vector2Int(c, -r);
                    wall.Add(h);
                    heart.Add(h);
                }
            }
        }

        if (floor.Count == 0)
        {
            error = "grid has no carved cells ('.') -- a site with no interior is a rock";
            return null;
        }

        // Centre on the bounding box of the WHOLE plan, masonry included, so a
        // plan with a heavy wall on one side does not sit off its own anchor.
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in floor)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        foreach (var p in wall)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        var offset = new Vector2Int((minX + maxX) / 2, (minY + maxY) / 2);

        if (heart.Count > 1)
        {
            error = "grid has " + heart.Count
                  + " heart cells ('X'); exactly one is allowed";
            return null;
        }

        var wallSet = new HashSet<Vector2Int>();
        foreach (var p in wall) wallSet.Add(p - offset);
        foreach (var p in heart) plan.heart.Add(p - offset);
        foreach (var p in platform) plan.platform.Add(p - offset);
        foreach (var p in stairs) plan.stairs.Add(p - offset);
        foreach (var p in door) plan.door.Add(p - offset);
        foreach (var p in lane) plan.lane.Add(p - offset);
        foreach (var p in keepClear) plan.keepClear.Add(p - offset);
        foreach (var p in decor) plan.decor.Add(p - offset);

        foreach (var p in floor)
        {
            var q = p - offset;
            // Masonry wins a collision, matching the procedural composer.
            if (!wallSet.Contains(q)) plan.floor.Add(q);
        }
        plan.wall.AddRange(wallSet);

        // LAST, and the order is the whole point. This reads plan.floor AND
        // plan.wall to decide which side of a door is outside the building, so
        // it cannot run until both are populated -- and plan.floor is filled
        // twenty lines above this, not where the door cells were offset. Called
        // there, it saw an empty floor list, judged both sides of every edge
        // door to be outside, gave every run a zero normal, and turned door
        // anchoring into a silent no-op.
        BuildDoorRuns(plan, wallSet);

        // Loud, because the failure it catches is invisible. A plan that asks
        // for door anchoring and offers no usable normal does not break, does
        // not throw and does not look wrong in the editor -- it just quietly
        // places on its centre like every other site, which is exactly what it
        // was doing before the feature existed. The validator catches this too,
        // but only when someone runs it.
        if (plan.anchorOnDoor)
        {
            bool usable = false;
            foreach (var run in plan.doorRuns)
                if (run.outward != Vector2Int.zero) { usable = true; break; }
            if (!usable)
                Debug.LogWarning("[AncientSitePlanLibrary] '" + plan.sourceName +
                    "' is @anchor_on: door but no door run faces outward. Door " +
                    "anchoring will not engage and the plan will place on its " +
                    "centre. A door must open onto rock -- check it sits on the " +
                    "plan's edge rather than an interior wall.");
        }

        return plan;
    }

    /// <summary>
    /// Groups '+' cells into maximal straight runs and works out which way each
    /// one faces.
    ///
    /// A run's axis is decided by whether it has a horizontal neighbour: a door
    /// in a vertical wall runs vertically, one in a horizontal wall runs
    /// horizontally, and a single cell belongs to neither and is measured as a
    /// run of one -- which the validator then fails, correctly, because a
    /// one-cell door is the worst version of the fault the rule exists for.
    ///
    /// `outward` is the perpendicular whose neighbour is in NEITHER floor nor
    /// wall, i.e. outside the plan. If both sides are outside, or neither is,
    /// the run gets a zero normal and door anchoring will not use it -- better
    /// an unusable run than a vault pointed at its own interior.
    /// </summary>
    private static void BuildDoorRuns(AuthoredSitePlan plan, HashSet<Vector2Int> wallSet)
    {
        plan.doorRuns.Clear();
        if (plan.door.Count == 0) return;

        var doors = new HashSet<Vector2Int>(plan.door);
        var inside = new HashSet<Vector2Int>(wallSet);
        foreach (var f in plan.floor) inside.Add(f);

        var counted = new HashSet<Vector2Int>();
        foreach (var cell in plan.door)
        {
            if (counted.Contains(cell)) continue;

            bool horiz = doors.Contains(new Vector2Int(cell.x + 1, cell.y))
                      || doors.Contains(new Vector2Int(cell.x - 1, cell.y));
            var step = horiz ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            var perp = horiz ? new Vector2Int(0, 1) : new Vector2Int(1, 0);

            var start = cell;
            while (doors.Contains(start - step)) start -= step;

            int len = 0;
            var c = start;
            while (doors.Contains(c))
            {
                counted.Add(c);
                len++;
                c += step;
            }

            var mid = start + step * (len / 2);

            bool plusOutside = !inside.Contains(mid + perp) && !doors.Contains(mid + perp);
            bool minusOutside = !inside.Contains(mid - perp) && !doors.Contains(mid - perp);
            var outward = Vector2Int.zero;
            if (plusOutside && !minusOutside) outward = perp;
            else if (minusOutside && !plusOutside) outward = -perp;

            plan.doorRuns.Add(new DoorRun { mid = mid, outward = outward, length = len });
        }
    }

    /// <summary>Parses a whole set, logging and skipping anything malformed.
    /// One bad file never costs the others.</summary>
    public static List<AuthoredSitePlan> LoadAll(IEnumerable<TextAsset> assets)
    {
        var result = new List<AuthoredSitePlan>();
        if (assets == null) return result;

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            var plan = Parse(asset.text, asset.name, out string error);
            if (plan == null)
            {
                Debug.LogWarning("[AncientSitePlan] '" + asset.name + "' skipped: " + error);
                continue;
            }

            Debug.Log($"PARSED PLAN: {asset.name} archetype={plan.archetype} name={plan.sourceName} floor={plan.floor.Count}");

            result.Add(plan);
        }

        return result;
    }

    public static bool TryParseArchetype(string s, out SiteArchetype value)
    {
        value = SiteArchetype.GuardPost;
        if (string.IsNullOrEmpty(s)) return false;
        string k = s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        // The cap must track the enum's TAIL so every value parses -- unlike
        // BuildPlanPool's useAllArchetypes cap, which deliberately stops at
        // TollHouse so an authored-only archetype is opted in per floor and
        // never swept in by "all".
        for (int i = 0; i <= (int)SiteArchetype.DeadCoreVault; i++)
        {
            var candidate = (SiteArchetype)i;
            if (candidate.ToString().ToLowerInvariant() == k)
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }

    public static bool TryParseAnchor(string s, out SiteAnchor value)
    {
        value = SiteAnchor.Free;
        if (string.IsNullOrEmpty(s)) return false;
        string k = s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        for (int i = 0; i <= (int)SiteAnchor.Crossing; i++)
        {
            var candidate = (SiteAnchor)i;
            if (candidate.ToString().ToLowerInvariant() == k)
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }
}
