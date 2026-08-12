using System;
using System.Collections.Generic;

/// <summary>
/// DAY 30 — Procedural terrain feature data, persisted per floor.
///
/// DAY 31 PART 2 — aliveWildCount + cleared on ChamberData.
/// DAY 31 PART 3F — wildMonsters list on ChamberData captures per-monster
///   position, HP, and definition name. aliveWildCount remains as the
///   sentinel (-1 = never spawned). On load, WildMonsterController prefers
///   wildMonsters when present and falls back to a coarse re-roll using
///   aliveWildCount otherwise.
/// </summary>
[Serializable]
public class FloorFeatureSaveData
{
    public List<RiverData> rivers = new();
    public List<ChamberData> chambers = new();
    public List<int> revealedRiverIds = new();
    public List<int> revealedChamberIds = new();

    // Buried Age roads. Cells are NOT stored: a road is pure geometry, so the
    // polyline plus width plus the broken-end gap rebuilds it exactly, and one
    // shared rasteriser serves both generation and load. A floor-index-4 network
    // is tens of thousands of cells; persisting them would fatten every save for
    // nothing. Empty on floors without roads and on saves written before them.
    public List<RoadData> roads = new();

    // Reveal is per SEGMENT, not per road. Touching one end of an 800-cell trunk
    // must not unfog the whole floor. Segment ids are assigned in generation
    // order and are stable for a given seed.
    public List<int> revealedRoadSegmentIds = new();

    // Buried Age sites. Unlike roads, cells ARE stored: a site is a composed
    // plan rather than pure geometry, and re-deriving it on load would mean
    // pinning the builder's recipes forever -- an edit to a plan would silently
    // reshape every existing save. A floor's whole site layer is a few thousand
    // cells, which is chamber-scale and costs nothing. Empty on floors without
    // sites and on saves written before them.
    public List<SiteData> sites = new();

    // Reveal is per SITE, not per stretch. A floor holds a handful of set-pieces,
    // so a site comes into view entire, exactly as a chamber does.
    public List<int> revealedSiteIds = new();

    public CoreCavernData coreCavern;

    // Seeded surface entrance: tunnel through the bedrock rim + offshoot
    // chamberlets. Null on floors without one and on legacy saves.
    public EntranceCaveData entranceCave;

    // Player-built walls (canon 36). Saved additively -- the pavedRoadCells
    // precedent -- because nothing else records which solid cells the player
    // made: minedTiles carries the solidity, this list carries the Stone
    // retype so a built wall renders and re-mines as stone after a reload.
    public List<SerializableVector3Int> builtWallCells = new();

    // Den tunnels (canon 42). Cells are NOT stored, on the RoadData contract:
    // a tunnel is pure geometry, so the polyline plus the two widths rebuilds
    // it exactly and one shared rasteriser serves generation and load. Empty on
    // floors without a den and on saves written before them.
    public List<DenTunnelData> denTunnels = new();

    // Reveal is per SEGMENT, not per run: a run crossing half the floor would
    // otherwise hand over the network's whole shape from one touched cell, and
    // that shape is the clue there is a den at the end of it.
    public List<int> revealedDenTunnelSegmentIds = new();

    // The den's own hole (canon 42). Null on floors without a den and on every
    // save written before it -- including saves that already carry den TUNNELS,
    // since those shipped first. DenAnchor falls back to the old polyline origin
    // in exactly that case, so no migration runs.
    public DenCavityData denCavity;
}

[Serializable]
public class RiverData
{
    public int id;
    public int width;
    public List<SerializableVector3Int> polyline = new();
    // Water-channel cells: fordable, un-mineable, painted with the water tile.
    public List<SerializableVector3Int> cells = new();
    // Dry floor banks eroded from the river's outer shell (walkable natural floor).
    // Empty on pre-bank saves, which keep behaving as all-water rivers.
    public List<SerializableVector3Int> bankCells = new();
    // Water cells OUTSIDE the disc: the surface continuation across the forest.
    // Deliberately separate from `cells` and `bankCells`. IsBedrock returns false
    // outside the disc, so the MarkNaturalFloor bedrock guard would not filter
    // these; folding them into bankCells would register thousands of forest cells
    // as mined dungeon floor, inflating regenPerTile mana and painting the
    // minimap. Never pass this list to MarkNaturalFloor or the dungeon water
    // painter. Empty on saves written before the surface extension.
    public List<SerializableVector3Int> surfaceCells = new();
    // Cells where the surface river crosses the pilgrim road at a near-square
    // angle: the ford. Kept so the ford art (and, later, the slow-ford movement
    // rule) can be applied to exactly these cells.
    public List<SerializableVector3Int> fordCells = new();
}

/// <summary>Appended only, never reordered -- these values serialise into saves.
///
/// Lane is a rail that exists so DeepRoadGraph stays CONNECTED through a site.
/// Its polyline is the authored lane walked gate to gate; RebuildRoadCells paints
/// nothing for it. Without it the two gates of a village 30 to 70 cells apart
/// cluster as separate nodes and the network is severed at every hold.</summary>
public enum RoadKind { Trunk = 0, Spur = 1, Lane = 2 }

[Serializable]
public class RoadData
{
    public int id;
    public RoadKind kind = RoadKind.Trunk;

    /// <summary>Carriageway width in cells.</summary>
    public int width = 5;

    /// <summary>Cells of centreline per reveal segment.</summary>
    public int segmentLength = 40;

    /// <summary>Centreline cells left uncarved at the far end. The polyline is
    /// built in full and these cells are simply never opened -- the same trick
    /// the resting pocket uses -- so a broken spur visibly stops rather than
    /// fading out. 0 on a road that runs its whole length.</summary>
    public int brokenGapCells;

    /// <summary>Floor centre and clamp radius captured AT GENERATION, so a later
    /// edit to the road profile can never change how an existing save rasterises.</summary>
    public SerializableVector3Int floorCentre;
    public int clampRadius;

    public List<SerializableVector3Int> polyline = new();
}


/// <summary>
/// One den tunnel (canon 42). Cells are NOT stored, on the RoadData contract
/// and for the same reason: a tunnel is pure geometry, so the polyline plus the
/// two widths rebuilds it exactly, and one shared rasteriser serves generation
/// and load so they can never disagree.
/// </summary>
[Serializable]
public class DenTunnelData
{
    public int id;

    /// <summary>The chamber this run reaches, or -1 when it ends in the rock.
    /// A dead end is content, not failure (canon 42).</summary>
    public int chamberId = -1;

    /// <summary>Section at the den mouth, tapering to tipWidth at the far end.</summary>
    public int width = 3;
    public int tipWidth = 2;

    /// <summary>Cells of centreline per reveal segment. A run comes into view a
    /// stretch at a time, never entire -- the road contract.</summary>
    public int segmentLength = 40;

    /// <summary>Floor centre and clamp radius captured AT GENERATION, so a later
    /// edit to the den profile can never change how an existing save
    /// rasterises. The RoadData precedent, and the reason it exists.</summary>
    public SerializableVector3Int floorCentre;
    public int clampRadius;

    public List<SerializableVector3Int> polyline = new List<SerializableVector3Int>();
}

/// <summary>One placed Buried Age site (canon 19).</summary>
[Serializable]
public class SiteData
{
    public int id;
    public SiteArchetype archetype;

    /// <summary>Which plan of that archetype this instance was built from. The
    /// no-repeat rule works on archetype PLUS variant, so a floor may hold two
    /// archives with different plans but never the same plan twice.</summary>
    public int variant;

    /// <summary>The authored plan's @name (empty for procedural plans). Added for
    /// the decor-prefab hook: AncientSiteProfile maps plan name to prefab, and a
    /// serialised name survives authored-list edits where (archetype, variant)
    /// arithmetic would not. Appended field: old saves load it as "".</summary>
    public string planName = "";

    public SerializableVector3Int anchorCell;

    /// <summary>The heart cell for a Church seal -- altar, grave slab,
    /// capped font, seal-stone. Null on every other site and on every
    /// save written before the seals existed, which is exactly the
    /// "no heart" case, so no migration runs.</summary>
    public SerializableVector3Int heartCell;

    /// <summary>Carved interior -- natural floor on reveal.</summary>
    public List<SerializableVector3Int> cells = new();

    /// <summary>The masonry. Deliberately NOT carved: these cells stay solid
    /// rock and are retyped to the site's masonry terrain -- decided by
    /// TerrainFeatureGenerator.MasonryTypeFor: Ruins for every dead site,
    /// DwarvenMasonry for the living dwarven ones -- so they render as wall,
    /// cost that terrain's resistance, and pay out the ancient_masonry pattern when
    /// mined. Straight walls against organic chambers is the whole of what makes
    /// a site read as built rather than found.</summary>
    public List<SerializableVector3Int> ruinsCells = new();

    /// <summary>Carriageway cells this site yielded at placement, kept so the
    /// road can be PAVED where it runs through the room -- built around, not
    /// cut through. Appended field: old saves load it empty and keep the
    /// cut-through look until the floor regenerates.</summary>
    public List<SerializableVector3Int> pavedRoadCells = new();

    /// <summary>Decor cells from the plan's 'o' glyphs, in world space, written
    /// at placement. Saved rather than re-derived because SiteData keeps no
    /// rotation or mirror, so the plan asset alone cannot say where a rotated
    /// plan's cells landed. Appended field: old saves load it empty and simply
    /// spawn no pieces.</summary>
    public List<SerializableVector3Int> decorCells = new();

    /// <summary>The dwarven outpost. DwarvenOutpostController finds its site by
    /// this flag; placement guarantees at most one per floor.</summary>
    public bool reservedForOutpost;

    /// <summary>The dwarven village, same contract: DwarvenVillageController
    /// finds its site by this flag. APPENDED for JsonUtility -- older saves load
    /// it false, and no older save can contain a village anyway, because floor
    /// features persist rather than regenerate.</summary>
    public bool reservedForVillage;
}

[Serializable]
public class ChamberData
{
    public int id;
    public SerializableVector3Int centerCell;
    public List<SerializableVector3Int> cells = new();

    // DAY 31 PART 2 — sentinel + cleared flag.
    public int aliveWildCount = -1;
    public bool cleared = false;

    // DAY 31 PART 3F — per-monster snapshot.
    public List<WildMonsterSaveData> wildMonsters = new();
}

[Serializable]
public class WildMonsterSaveData
{
    public string monsterName;
    public SerializableVector3Int cell;
    public float currentHP;
}

public struct FeatureRef
{
    public FeatureType type;
    public int featureId;
}

// Appended only, never reordered: the values are serialised into saves as ints.
public enum FeatureType { None, River, Chamber, CoreCavern, RiverBank, EntranceCave, Road, AncientSite, DenTunnel, DenCavity }

/// <summary>
/// The hole a den lives in (canon 42). Cells ARE stored, and the contract is
/// the SITE's rather than the road's: a road or a tunnel is pure geometry that
/// a polyline rebuilds exactly, but a cavity is a cellular-automata carve, so
/// a later edit to the fill chance, the iteration count or the box size would
/// silently reshape every existing save -- moving walls the player has already
/// dug around. That is precisely the reason SiteData persists its cells.
///
/// TWO SETS, AND THE SECOND IS NOT REDUNDANT. `reserveCells` is the MAXIMUM
/// footprint, fixed at generation, so chambers and rivers negotiate around
/// ground the den has not opened yet -- the reservedCoreCells mechanism, and
/// the resting pocket's precedent of reserving stone that is never carved.
/// `cells` is what is actually open. An occupier writes the two identical and
/// never touches them again, because it never digs; an excavator opens more of
/// its reserve as it tiers.
///
/// Note what is NOT here: minedTiles. That records openness and carries no
/// identity, so it cannot answer "which cells are the cavity" -- and
/// TileInfluenceManager.LoadSaveData clears and rebuilds it from the save,
/// which is why ReassertOpenGround has to re-run afterwards. Every feature in
/// the game persists its own identity separately for that reason.
/// </summary>
[Serializable]
public class DenCavityData
{
    /// <summary>The den anchor: DenTunnelBuilder.Plan's chosen point, which is
    /// also where every run originates (Plan sets run.a = den for all of them),
    /// so the cavity seats against the whole network without any run moving.</summary>
    public SerializableVector3Int centreCell;

    /// <summary>Open ground. Occupier: the whole hole, fixed. Excavator: what
    /// has been dug so far, growing inside reserveCells.</summary>
    public List<SerializableVector3Int> cells = new List<SerializableVector3Int>();

    /// <summary>The maximum footprint, reserved at generation and never
    /// re-rolled. Ordinary rock to the player, who may mine it first and keep
    /// it -- which is the race, and the resting place's own trick.</summary>
    public List<SerializableVector3Int> reserveCells = new List<SerializableVector3Int>();

    /// <summary>True once influence has touched it. The cavity reveals ENTIRE,
    /// on the chamber rule rather than the tunnel rule.</summary>
    public bool revealed;

    /// <summary>Which den kind carved it, as an int (appended only). Read by the
    /// report so a floor can be checked without resolving its profile.</summary>
    public int kind;
}

[Serializable]
public class CoreCavernData
{
    public SerializableVector3Int centerCell;
    public List<SerializableVector3Int> cells = new();
    public List<TunnelData> tunnels = new();
}

[Serializable]
public class TunnelData
{
    /// <summary>Outward bearing from the cavern, in degrees (debug / tuning only).</summary>
    public float angleDegrees;
    public List<SerializableVector3Int> cells = new();
}

[Serializable]
public class EntranceCaveData
{
    /// <summary>Cell at the outer disc edge where the tunnel meets the surface.
    /// The DungeonEntrance object stands here; adventurers spawn here.</summary>
    public SerializableVector3Int mouthCell;

    /// <summary>Bearing from the floor centre to the mouth, in degrees.
    /// The apron reads this to lay the pilgrim road.</summary>
    public float angleDegrees;

    /// <summary>Every carved cell: tunnel + offshoot chamberlets, mouth included.</summary>
    public List<SerializableVector3Int> cells = new();

    /// <summary>True once the player's influence has touched the cave.
    /// Gates the discovery alert and the compass HUD.</summary>
    public bool discovered;

    /// <summary>Interior cell a few steps down the tunnel where the (invisible)
    /// entrance stands and parties spawn — deep enough that spawn scatter always
    /// lands on carved floor, never the apron.</summary>
    public SerializableVector3Int spawnCell;
    public bool hasSpawnCell;

    /// <summary>Day the seal broke. The first wave arrives the day after;
    /// -1 = not yet discovered.</summary>
    public int discoveredDay = -1;

    /// <summary>The resting place (canon 34): a pocket rolled exactly like an
    /// offshoot chamberlet and then deliberately NOT carved. These cells never
    /// enter `cells`, so they are never revealed and never marked natural floor
    /// -- they stay ordinary stone, indistinguishable from the rock around them,
    /// one cell off the tunnel the player walks down every day. Reserved at
    /// generation so rivers and chambers cannot claim the space.
    /// Empty on saves written before the resting place existed, in which case
    /// that dungeon simply has no body to find.</summary>
    public List<SerializableVector3Int> restCells = new();

    /// <summary>The cell the remains lie in: the pocket's centre. Mining it is
    /// what finds them.</summary>
    public SerializableVector3Int restCell;
    public bool hasRest;

    /// <summary>Set once the player has descended and the wisp has admitted the
    /// pocket exists; and once the stone has actually been opened.</summary>
    public bool restArmed;
    public bool restFound;
}