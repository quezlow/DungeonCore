using UnityEngine;

/// <summary>
/// STAGE 2 — Pure classification of a dungeon cell for the 3/4 cave wall
/// renderer. No painting; the only dependency is the TileInfluenceManager it
/// queries. Reused by the debug overlay now and by the real renderer in Stage 3.
///
/// Conventions (locked, matches the cap-mask spec):
///   N = +Y, E = +X, S = -Y, W = -X.
///   Cap mask bits: N=1, E=2, S=4, W=8; a bit is SET when that neighbour is SOLID.
///   SOLID = a cell that is NOT mined.  OPEN (floor) = a cell that IS mined.
///   SOUTH-FACING = solid AND its south neighbour is open. These cells get a
///   draped face; their southern neighbours are the floor the face hangs over.
/// </summary>
public enum CaveFace { None, Straight, CornerW, CornerE, Pillar, NubEast, NubWest, ColumnBottom }

public class CaveWallClassifier
{
    private readonly TileInfluenceManager influence;
    private readonly TerrainFeatureGenerator features;
    private readonly DungeonTerrain terrain;

    public CaveWallClassifier(TileInfluenceManager influence, TerrainFeatureGenerator features = null,
                              DungeonTerrain terrain = null)
    {
        this.influence = influence;
        this.features = features;
        this.terrain = terrain;
    }

    private static readonly Vector3Int N = new Vector3Int(0, 1, 0);
    private static readonly Vector3Int S = new Vector3Int(0, -1, 0);
    private static readonly Vector3Int E = new Vector3Int(1, 0, 0);
    private static readonly Vector3Int W = new Vector3Int(-1, 0, 0);

    /// Solid = an IN-DISC cell that is not mined and is neither water nor
    /// carriageway. Cells beyond the floor disc are OPEN AIR — the surface — so
    /// no wall ever paints on the apron, and the rim's outer edge is a true
    /// solid/open boundary where it borders revealed ground (the breach
    /// corners).
    ///
    /// ROADS join rivers in the exemption, and the asymmetry was a bug. Reveal
    /// is per SEGMENT, and UnfogRoadSegment calls MarkNaturalFloor on that one
    /// segment's cells, so the next stretch stayed un-mined and therefore
    /// SOLID. Solid rock touching open floor is precisely what the renderer
    /// frames, so it drew a cap and a face straight across the carriageway at
    /// every segment join. Water never showed it because its exemption does not
    /// depend on being discovered; the road's must not either. Note this is a
    /// framing exemption only -- it does not unfog anything, so per-segment
    /// reveal and its anti-layout-leak guarantee are untouched.
    ///
    /// One feature lookup covers both rather than two probes per call, and this
    /// is called four times per wall cell in CapMask alone.
    public bool IsSolid(Vector3Int cell)
    {
        if (influence == null) return false;
        if (influence.IsTileMined(cell)) return false;
        if (terrain != null && !terrain.IsWithinBounds(cell)) return false;
        if (features != null)
        {
            FeatureType f = features.GetFeatureAt(cell);
            if (f == FeatureType.River || f == FeatureType.Road) return false;
        }
        return true;
    }

    /// 16-mask over the four cardinal neighbours: N=1, E=2, S=4, W=8, set = solid.
    public int CapMask(Vector3Int cell)
    {
        int mask = 0;
        if (IsSolid(cell + N)) mask |= 1;
        if (IsSolid(cell + E)) mask |= 2;
        if (IsSolid(cell + S)) mask |= 4;
        if (IsSolid(cell + W)) mask |= 8;
        return mask;
    }

    /// A solid cell whose south neighbour is open. These get a draped face.
    public bool IsSouthFacing(Vector3Int cell)
        => IsSolid(cell) && !IsSolid(cell + S);

    /// Which face variant a south-facing cell takes, from its N/E/W neighbours.
    /// Returns None for cells that are not south-facing.
    public CaveFace FaceVariant(Vector3Int cell)
    {
        if (!IsSouthFacing(cell)) return CaveFace.None;

        bool n = IsSolid(cell + N);
        bool e = IsSolid(cell + E);
        bool w = IsSolid(cell + W);

        if (!e && !w) return n ? CaveFace.ColumnBottom : CaveFace.Pillar;           // 1_0_0 -> column bottom, 0_0_0 -> pillar
        if (e && w) return CaveFace.Straight;                         // 1_1_1 and 0_1_1
        if (e) return n ? CaveFace.CornerW : CaveFace.NubEast;   // E solid, W open
        return n ? CaveFace.CornerE : CaveFace.NubWest;                 // W solid, E open
    }
}