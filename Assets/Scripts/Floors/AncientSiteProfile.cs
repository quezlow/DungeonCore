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
/// A fresh asset already carries the shipped layout: index 2 gets the lone
/// guard post, index 3 the handful a maintained road keeps, index 4 the whole
/// roster.
/// </summary>
[CreateAssetMenu(fileName = "AncientSiteProfile", menuName = "Dungeon/Ancient Site Profile")]
public class AncientSiteProfile : ScriptableObject
{
    [SerializeField]
    private List<SiteFloorEntry> floors = new List<SiteFloorEntry>
    {
        // Floor index 2 -- radius 250, no road layer. One structure, alone, with
        // nothing around it. The road never got this high, or what did is gone.
        new SiteFloorEntry
        {
            floorIndex = 2,
            minSites = 1,
            maxSites = 2,
            bandInner = 0.15f,
            bandOuter = 0.65f,
            minSpacing = 60,
            minSpan = 16,
            maxSpan = 26,
            rimMargin = 12,
            pool = new List<SiteArchetype> { SiteArchetype.GuardPost },
        },

        // Floor index 3 -- radius 400, the living trunk. What a maintained road
        // still keeps: somewhere to file things, somewhere to charge for passage,
        // somewhere to stand watch, and the gate the road stops at.
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

        // Floor index 4 -- radius 600, the dead network. Everything, and denser
        // than floor 3 by design: deeper is older, and older is when it was whole.
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
            case SiteArchetype.Ossuary: return SiteAnchor.Free;
            case SiteArchetype.HollowSanctum: return SiteAnchor.Free;
            default: return SiteAnchor.Free;
        }
    }

    /// <summary>How many PROCEDURAL plan variants an archetype has. Hand-authored
    /// plans are counted separately and numbered above these, so raising this
    /// figure needs the matching case in AncientSiteBuilder.Compose first.</summary>
    public static int VariantCountFor(SiteArchetype a) => 3;

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
            default: return "a Buried Age ruin";
        }
    }
}
