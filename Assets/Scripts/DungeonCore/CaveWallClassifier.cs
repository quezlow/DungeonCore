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
public enum CaveFace { None, Straight, CornerW, CornerE, Pillar, NubEast, NubWest }

public class CaveWallClassifier
{
    private readonly TileInfluenceManager influence;

    public CaveWallClassifier(TileInfluenceManager influence)
    {
        this.influence = influence;
    }

    private static readonly Vector3Int N = new Vector3Int(0, 1, 0);
    private static readonly Vector3Int S = new Vector3Int(0, -1, 0);
    private static readonly Vector3Int E = new Vector3Int(1, 0, 0);
    private static readonly Vector3Int W = new Vector3Int(-1, 0, 0);

    /// Solid = not mined. (Open / floor = mined.)
    public bool IsSolid(Vector3Int cell) => influence != null && !influence.IsTileMined(cell);

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

        if (!e && !w) return CaveFace.Pillar;                          // 1_0_0 and 0_0_0
        if (e && w) return CaveFace.Straight;                         // 1_1_1 and 0_1_1
        if (e) return n ? CaveFace.CornerW : CaveFace.NubEast;   // E solid, W open
        return n ? CaveFace.CornerE : CaveFace.NubWest;                 // W solid, E open
    }
}
