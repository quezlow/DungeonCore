using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The eight Buried Age site archetypes (canon 19). Order is SERIALISED into
/// saves as an int, so entries may be appended but never reordered or removed.
/// </summary>
public enum SiteArchetype
{
    SunkenPlaza = 0,
    CollapsedArchive = 1,
    Ossuary = 2,
    BrokenAqueduct = 3,
    HollowSanctum = 4,
    SealedGate = 5,
    GuardPost = 6,
    TollHouse = 7,

    /// <summary>The inhabited hold on the village floor. AUTHORED-ONLY: it has
    /// no procedural variants (VariantCountFor returns 0), sits in no floor's
    /// pool, and is placed solely by name through SiteFloorEntry.reserveVillage
    /// -- which is why useAllArchetypes deliberately does not sweep it in.</summary>
    DwarvenVillage = 8,

    // ---- The Church seals (canon 18/20/21) --------------------------------
    // NOT more Buried Age sites. Canon 21 draws the line: the Buried Age ruins
    // are the deep-faith's own -- welcoming, no desecration penalty -- while
    // these are Church seals laid OVER what that faith left, and hostile. Two
    // flavours of sacred underground on one axis of history.
    //
    // AUTHORED-ONLY, like DwarvenVillage: VariantCountFor returns 0, so a pool
    // holds exactly the hand-drawn plans. A seal is a made object, and
    // procedural jitter reads as a collapsed ruin -- which is the other
    // family's whole job.
    //
    // Opt-in per floor. BuildPlanPool's useAllArchetypes cap stops at
    // TollHouse, so floor index 4's all-archetypes roster cannot sweep these in;
    // they appear only where a floor entry names them in its pool.

    /// <summary>A ring of dressed stone around a capped plinth. No door and no
    /// window: nobody was ever meant to come in.</summary>
    ChurchSeal = 9,

    /// <summary>Slab graves, and one at the head still capped. What the Church
    /// buried here it did not intend to be dug up.</summary>
    SealedCrypt = 10,

    /// <summary>The chapel the sealing was administered from. Not a seal
    /// itself, which is why it anchors to a road -- nobody administers anything
    /// somewhere nobody can reach.</summary>
    WardChapel = 11,

    /// <summary>A spring taken over and capped. The oldest kind of holy site
    /// there is, and the one the Church had most reason to shut.</summary>
    BlessedSpring = 12,

    /// <summary>The vault the Church built around a DEAD CORE, on the oldest
    /// ground there is. One per dungeon, on floor index 4, placed by guarantee
    /// rather than by pool -- so it can neither be rolled twice nor displace a
    /// seal, exactly as the dwarven village works.
    ///
    /// Authored-only and enormous: the three plans run 75 by 75 at 2458 to 2884
    /// carved cells, against the largest village at 2588. Nothing else in the
    /// game is this size on purpose.</summary>
    DeadCoreVault = 13,
}

/// <summary>
/// Where an archetype wants to sit relative to the road layer.
///   Junction  -- on a crossroads. A plaza is a junction with pretensions.
///   AlongRoad -- straddling or beside the carriageway.
///   RoadEnd   -- at a broken or rim-bound end. The road stops here for a reason.
///   Crossing  -- across the road at an angle, cutting over it.
///   Free      -- anywhere in band; wants no road at all.
/// Anchors DEGRADE rather than fail: a floor with no roads resolves every
/// preference to Free, which is what puts the lone guard post on floor index 2.
/// </summary>
public enum SiteAnchor
{
    Free = 0,
    Junction = 1,
    AlongRoad = 2,
    RoadEnd = 3,
    Crossing = 4,
}

/// <summary>
/// Per-floor site authoring. One entry per floor index that should carry sites;
/// floors with no entry generate nothing and cost nothing.
///
/// WHY THE BAND EXISTS. Influence reach is COST-distance, not cells:
/// baseReach + (level - 1) * reachPerLevel + (day - 1) * reachPerDay, spent
/// against terrain resistance. Working the radial bands through, a plausible
/// late run reaches roughly 65 per cent of a deep floor's radius; covering the
/// whole of floor index 4 would take some six hundred in-game days. Reveal is
/// influence-touch only, so a site placed uniformly across the disc has a
/// better-than-even chance of never being seen at all. Sites are therefore
/// confined to a band, and the outer third of each disc is left empty --
/// which reads correctly anyway. That is past where anyone went.
/// </summary>
[Serializable]
public class SiteFloorEntry
{
    [Tooltip("Zero-based floor index. Floor 4 in the UI is index 3.")]
    [Min(0)] public int floorIndex = 4;

    [Header("Count")]
    [Tooltip("Inclusive range of sites rolled for this floor.")]
    [Min(0)] public int minSites = 9;
    [Min(0)] public int maxSites = 13;

    [Header("Placement band (fractions of floor radius)")]
    [Tooltip("Inner edge of the placement band. Clears the core cavern and the " +
             "starter claim so a site never generates on top of the player.")]
    [Range(0.02f, 0.9f)] public float bandInner = 0.15f;

    [Tooltip("Outer edge of the placement band. Past this the player's influence " +
             "realistically never arrives, so a site there is content that does " +
             "not exist. See the class summary for the arithmetic.")]
    [Range(0.05f, 1f)] public float bandOuter = 0.65f;

    [Tooltip("Minimum distance between site anchors, in cells. Kept well below the " +
             "average spacing the count implies, or placement starves and the floor " +
             "silently rolls fewer sites than authored.")]
    [Min(4)] public int minSpacing = 90;

    [Header("Size")]
    [Tooltip("Shortest and longest edge of a site's bounding box, in cells. The plan " +
             "is composed in local axis-aligned space at this scale and then rotated. " +
             "Cell count grows with the SQUARE of this, and a site reveals entire -- " +
             "a span of 62 puts some three thousand open cells on screen at once, " +
             "against roughly 100-200 for a cave chamber. Keep it near twice the " +
             "chamber box size, not five times it.")]
    [Min(8)] public int minSpan = 30;
    [Min(8)] public int maxSpan = 62;

    [Tooltip("How far in from the disc edge a site stops, in cells. Masonry is left " +
             "SOLID and merely retyped, so unlike a road it cannot be broken by the " +
             "bedrock rim -- but a site half-buried in unminable rim is unreadable. " +
             "Cover TerrainTypeMap's maxRingThickness plus the largest span.")]
    [Min(0)] public int rimMargin = 12;

    [Header("Roster")]
    [Tooltip("Use every archetype on this floor and ignore the pool list below. " +
             "This is an EXPLICIT toggle rather than 'an empty list means all', " +
             "because in the inspector an empty list is indistinguishable from one " +
             "you have not filled in yet -- which reads as a silent failure.")]
    public bool useAllArchetypes = false;

    [Tooltip("Archetypes eligible on this floor, used when useAllArchetypes is off. " +
             "The no-repeat rule works on the PLAN (archetype plus variant), not " +
             "the archetype, so a floor may hold two archives with different plans " +
             "but never the same plan twice.")]
    public List<SiteArchetype> pool = new List<SiteArchetype>();

    // -- The holy sub-quota (canon 18/20) ---------------------------------
    //
    // A SECOND pool, drawn by its own pass BEFORE the general fill and, unlike
    // the outpost and the village, OUTSIDE the general budget. The seals are not
    // Buried Age ruins competing for the same slots -- canon 21 draws exactly
    // that line -- so a floor carries so many ruins AND so many seals, and one
    // cannot starve the other. Floors 0 and 1 are seals only, which is why their
    // minSites and maxSites are now zero, and why Build's early returns had to
    // stop firing on an empty general pool.

    [Header("Holy sub-quota (canon 18/20)")]
    [Tooltip("Inclusive range of Church seals rolled for this floor, ON TOP of " +
             "minSites/maxSites rather than inside them. Zero on a floor with no " +
             "seals, and the pass is then skipped entirely.")]
    [Min(0)] public int minHolySites = 0;
    [Min(0)] public int maxHolySites = 0;

    [Tooltip("Church archetypes eligible on this floor. Separate from `pool` and " +
             "drawn by its own pass, so seals and ruins cannot displace one " +
             "another. Keep WardChapel OUT of a roadless floor's list: it anchors " +
             "AlongRoad and degrades to a free pick where there is no road, which " +
             "strands the chapel a sealing was administered from somewhere nobody " +
             "can reach.")]
    public List<SiteArchetype> holyPool = new List<SiteArchetype>();

    [Tooltip("Place the dwarven outpost on this floor. This GUARANTEES it: the " +
             "outpost is placed first, before the random roster, and a floor that " +
             "cannot fit it logs an error rather than quietly shipping without " +
             "dwarves. It used to be an opportunistic flag latched onto whichever " +
             "Sealed Gate the shuffle happened to produce, which meant a floor " +
             "rolling three sites from a five-archetype roster could finish with no " +
             "outpost at all.")]
    public bool reserveOutpost = false;

    [Tooltip("Archetype the outpost is built from. SealedGate by design -- canon " +
             "19 makes the outpost the one Sealed Gate that is NOT sealed, so it " +
             "reads as the same institution the dead network is full of. Explicit " +
             "rather than hardcoded so a later arc can move it without a code edit.")]
    public SiteArchetype outpostArchetype = SiteArchetype.SealedGate;

    [Tooltip("Where the outpost anchors. AlongRoad, because the road must run " +
             "THROUGH it: anchors centre a plan on the sampled cell and the road " +
             "anchor list is a thinned CENTRELINE, so AlongRoad already puts the " +
             "site astride the carriageway. Do not use RoadEnd -- on a rim-to-rim " +
             "trunk the only ends are the two rim points, both outside the 0.15-0.65 " +
             "placement band, so it degrades to a free pick and strands the outpost " +
             "somewhere with no road at all.")]
    public SiteAnchor outpostAnchor = SiteAnchor.AlongRoad;

    [Tooltip("Place the guaranteed dwarven village on this floor. Same contract " +
             "as reserveOutpost: placed first on its own budget, counts toward " +
             "minSites/maxSites, and a floor that cannot fit it logs an error. " +
             "Keep it true on EXACTLY ONE floor -- DwarvenVillageController takes " +
             "the first revealed village it finds, exactly as the outpost does.")]
    public bool reserveVillage = false;

    [Tooltip("OPTIONAL PIN, empty on the shipped asset. Empty rolls seeded " +
             "among every authored DwarvenVillage plan on this profile -- add " +
             "a plan file with that archetype and it joins the rotation, zero " +
             "config. Set a plan's @name here to force that hold, for testing. " +
             "Either way the archetype sits in no pool, so the fill loop can " +
             "never serve or double-place a village.")]
    public string villagePlanName = "";

    [Tooltip("Floor index 4 only. Guarantees ONE DeadCoreVault: the Church's vault " +
             "around a dead core, on the oldest ground in the game. A guarantee " +
             "rather than a pool entry so it cannot be rolled twice or displace a " +
             "seal -- the same reasoning as reserveVillage.")]
    public bool reserveDeadCore = false;

    [Tooltip("Optional pin. Empty rolls among every authored DeadCoreVault plan; a " +
             "name narrows it to that one, for checking a specific vault without " +
             "unlisting the others. Three plans at 75 by 75 want looking at one at " +
             "a time.")]
    public string deadCorePlanName = "";
}

/// <summary>
/// The one authored asset for the Buried Age sites. Holds a per-floor entry
/// list; a floor without an entry has no sites.
///
/// The floor template prefab is SHARED across the deep floors, so per-floor
/// settings cannot live on the generator's own inspector -- they live here, on
/// one asset the shared prefab references. Same reasoning as
/// RoadNetworkProfile, and deliberately a SEPARATE asset from it: floor index 2
/// carries a site and no road at all, so folding sites into the road entries
/// would mean authoring a road entry whose only job is to declare no road.
///
/// A fresh asset already carries the shipped layout: index 2 the gatehouse
/// floor (the guaranteed outpost plus at most one ruin), index 3 the village
/// floor (the guaranteed village plus the handful a living road keeps), index
/// 4 the whole roster.
/// </summary>
[CreateAssetMenu(fileName = "AncientSiteProfile", menuName = "Dungeon/Ancient Site Profile")]
public class AncientSiteProfile : ScriptableObject
{
    [SerializeField]
    private List<SiteFloorEntry> floors = new List<SiteFloorEntry>
    {
        // Floor index 2 -- radius 250, the living trunk and the gatehouse that
        // holds it. This is the DWARVEN floor.
        //
        // It carried a single lonely GuardPost until the floor plan was corrected;
        // the whole trunk-and-outpost configuration moved down here from index 3.
        // Every proportional figure was rescaled by 250/400 on the way, because a
        // layout authored for a 400-radius floor is 60 per cent too large here.
        //
        // bandInner is 0.30 rather than the 0.15 the other floors use, and the
        // reason is spatial crowding, NOT the core reservation: exclusionRadius-
        // FromCenter is only 8, so even at 0.15 the gatehouse would clear it.
        // The actual problem is that 0.15 puts the inner edge 37 cells out, and
        // this hold is 39 cells across -- on a 250-radius floor that drops a
        // landmark practically on the player's doorstep, overlapping the arrival
        // area the up-stairs open into. 0.30 moves the inner edge to 75, which
        // leaves the hold a clear half-width of open floor between it and the
        // core. Measured, not guessed: at 0.30 the gatehouse spans radius 52 to
        // 185 against a usable disc of 238.
        //
        // minSites/maxSites are 1-2 and the GUARANTEED OUTPOST COUNTS TOWARD THEM
        // (PlaceOutpost adds to result.sites before the fill loop reads its
        // target). So this floor is the gatehouse plus at most one ruin, which is
        // the intent: the Buried Age sites ramp on the floors BELOW this one.
        new SiteFloorEntry
        {
            floorIndex = 2,
            minSites = 1,
            maxSites = 2,
            bandInner = 0.30f,
            bandOuter = 0.65f,
            minSpacing = 70,
            minSpan = 13,
            maxSpan = 21,
            rimMargin = 12,
            reserveOutpost = true,
            pool = new List<SiteArchetype>
            {
                SiteArchetype.CollapsedArchive,
                SiteArchetype.SealedGate,
                SiteArchetype.GuardPost,
                SiteArchetype.HollowSanctum,
                SiteArchetype.TollHouse,
            },
        },

        // Floor index 3 -- radius 400, the village floor. These are the numbers
        // the floor carried BEFORE the correction moved the gatehouse down to
        // index 2 -- authored for this radius, and simply restored.
        // SealedGate left the pool on purpose: sealed gates read as the dead
        // eras below, and keeping the archetype here would have put the
        // outpost's own authored plan into this floor's general pool as a
        // pickable ruin. The village counts toward minSites/maxSites exactly
        // as the outpost does, so the floor is the village plus 2-4 ruins.
        new SiteFloorEntry
        {
            floorIndex = 3,
            minSites = 3,
            maxSites = 5,
            bandInner = 0.15f,
            bandOuter = 0.65f,
            minSpacing = 110,
            minSpan = 20,
            maxSpan = 34,
            rimMargin = 12,
            reserveVillage = true,
            pool = new List<SiteArchetype>
            {
                SiteArchetype.CollapsedArchive,
                SiteArchetype.GuardPost,
                SiteArchetype.HollowSanctum,
                SiteArchetype.TollHouse,
            },
        },

        // Floor index 4 -- radius 600, the dead network. Everything, and the densest
        // floor by design: deeper is older, and older is when it was whole.
        new SiteFloorEntry
        {
            floorIndex = 4,
            minSites = 9,
            maxSites = 13,
            bandInner = 0.15f,
            bandOuter = 0.65f,
            minSpacing = 90,
            minSpan = 22,
            maxSpan = 40,
            rimMargin = 12,
            useAllArchetypes = true,
            pool = new List<SiteArchetype>(),
        },
    };

    [Header("Hand-authored plans")]
    [Tooltip("ASCII grid plans drawn by hand. Each becomes an extra VARIANT of the " +
             "archetype it declares, so it competes in the same no-repeat pool as the " +
             "procedural recipes and inherits the whole placement layer. A plan whose " +
             "archetype is not in a floor's roster is simply never picked on that floor. " +
             "Validate them with 'Dungeon Core / Validate Site Plans' before playing.")]
    [SerializeField] private List<TextAsset> authoredPlans = new List<TextAsset>();

    // Parsed once and held. ScriptableObject fields survive between generations
    // but are dropped on domain reload, so an edited plan is picked up on the
    // next script compile without a manual clear.
    private List<AuthoredSitePlan> parsedPlans;

    // -- Site decor prefabs (canon 19: the decor-prefab hook) ----------------

    [System.Serializable]
    public class SiteDecorEntry
    {
        [Tooltip("The plan's @name, e.g. 'The Ten Thousand Quiet'. Plans using " +
                 "'prefab' must be @rotate: no (Validate Site Plans enforces it); " +
                 "plans using 'piecePrefab' may rotate freely.")]
        public string planName;

        [Tooltip("Visual dressing only: platforms, stairs, clutter on carved floor. " +
                 "No walls, no floors, no colliders -- the plan keeps driving terrain, " +
                 "fog, mining and pathfinding. Spawned at the site anchor on reveal.")]
        public GameObject prefab;

        [Tooltip("Per-cell dressing: one piece instanced at EVERY 'o' cell of the " +
                 "plan, at the cell's world position, transform unrotated. Unlike " +
                 "'prefab' this does NOT require @rotate: no -- the cells were " +
                 "emitted through the plan's own placement transform, so the " +
                 "positions already rotated with it.")]
        public GameObject piecePrefab;
    }

    [SerializeField] private List<SiteDecorEntry> siteDecor = new List<SiteDecorEntry>();

    public IReadOnlyList<SiteDecorEntry> SiteDecor => siteDecor;

    public GameObject GetDecorPrefab(string planName)
    {
        if (string.IsNullOrEmpty(planName) || siteDecor == null) return null;
        foreach (var e in siteDecor)
            if (e != null && e.prefab != null && e.planName == planName) return e.prefab;
        return null;
    }

    /// <summary>The per-cell decor piece for a plan, or null. Parallel to
    /// GetDecorPrefab rather than merged with it: a plan may carry both an
    /// anchor prefab and a cell piece, and the two spawn independently.</summary>
    public GameObject GetDecorPiece(string planName)
    {
        if (string.IsNullOrEmpty(planName) || siteDecor == null) return null;
        foreach (var e in siteDecor)
            if (e != null && e.piecePrefab != null && e.planName == planName) return e.piecePrefab;
        return null;
    }

    public IReadOnlyList<SiteFloorEntry> Floors => floors;

    /// <summary>The parsed authored plans, loading them on first use.</summary>
    public List<AuthoredSitePlan> GetAuthoredPlans()
    {
        if (parsedPlans == null)
            parsedPlans = AncientSitePlanLibrary.LoadAll(authoredPlans);
        return parsedPlans;
    }

    /// <summary>Drops the cache so the next call re-reads the text assets. Used by
    /// the editor validator so a plan can be corrected and re-checked without a
    /// script recompile.</summary>
    public void InvalidateAuthoredPlans() => parsedPlans = null;

    /// <summary>The entry for a floor, or null if that floor carries no sites.</summary>
    public SiteFloorEntry GetEntry(int floorIndex)
    {
        if (floors == null) return null;
        foreach (var e in floors)
            if (e != null && e.floorIndex == floorIndex)
                return e;
        return null;
    }

    /// <summary>Where an archetype wants to sit. Fixed per archetype rather than
    /// authored, because the relation IS the archetype: an aqueduct that does not
    /// cross anything is a wall, and a toll house away from a road is a cottage.</summary>
    public static SiteAnchor AnchorFor(SiteArchetype a)
    {
        switch (a)
        {
            case SiteArchetype.SunkenPlaza: return SiteAnchor.Junction;
            case SiteArchetype.CollapsedArchive: return SiteAnchor.AlongRoad;
            case SiteArchetype.BrokenAqueduct: return SiteAnchor.Crossing;
            case SiteArchetype.SealedGate: return SiteAnchor.RoadEnd;
            case SiteArchetype.GuardPost: return SiteAnchor.AlongRoad;
            case SiteArchetype.TollHouse: return SiteAnchor.AlongRoad;
            case SiteArchetype.DwarvenVillage: return SiteAnchor.AlongRoad;
            case SiteArchetype.Ossuary: return SiteAnchor.Free;
            case SiteArchetype.HollowSanctum: return SiteAnchor.Free;
            default: return SiteAnchor.Free;
        }
    }

    /// <summary>How many PROCEDURAL plan variants an archetype has. Hand-authored
    /// plans are counted separately and numbered above these, so raising this
    /// figure needs the matching case in AncientSiteBuilder.Compose first.
    /// ZERO marks an AUTHORED-ONLY archetype: BuildPlanPool adds no procedural
    /// refs for it, so the archetype exists purely as its hand-drawn plans.</summary>
    public static int VariantCountFor(SiteArchetype a) => a switch
    {
        SiteArchetype.DwarvenVillage => 0,
        // Authored-only for the same reason the village is: a seal
        // is a made object, and procedural jitter reads as a
        // collapsed ruin, which is the Buried Age's job.
        SiteArchetype.ChurchSeal => 0,
        SiteArchetype.SealedCrypt => 0,
        SiteArchetype.WardChapel => 0,
        SiteArchetype.BlessedSpring => 0,
        // Authored-only like the rest of the Church family, and more so:
        // there is one vault per dungeon and all three plans are drawn.
        SiteArchetype.DeadCoreVault => 0,
        _ => 3,
    };

    /// <summary>Player-facing name, used by the discovery alert.</summary>
    public static string DisplayName(SiteArchetype a)
    {
        switch (a)
        {
            case SiteArchetype.SunkenPlaza: return "a sunken plaza";
            case SiteArchetype.CollapsedArchive: return "a collapsed archive";
            case SiteArchetype.Ossuary: return "an ossuary";
            case SiteArchetype.BrokenAqueduct: return "a broken aqueduct";
            case SiteArchetype.HollowSanctum: return "a hollow sanctum";
            case SiteArchetype.SealedGate: return "a sealed gate";
            case SiteArchetype.GuardPost: return "an abandoned guard post";
            case SiteArchetype.TollHouse: return "a ruined toll house";
            case SiteArchetype.DwarvenVillage: return "a dwarven village";
            default: return "a Buried Age ruin";
        }
    }
}
