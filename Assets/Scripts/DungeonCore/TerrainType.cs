/// <summary>
/// DAY 32 — Terrain types for the per-cell claim resistance system.
///
/// Resistance multipliers are looked up via TerrainResistanceTable
/// (a ScriptableObject) so balance can be tuned without code changes.
///
/// PLACEMENT NOTES
///   - Dirt / Sand / Stone / Granite are placed by TerrainTypeMap during
///     procgen (radial bands + random patches).
///   - Ruins is reserved for DAY 70 (Ruins & Structures Expansion).
///   - HolyGround is reserved for a future hand-placed mechanic.
/// </summary>
public enum TerrainType
{
    Dirt = 0,
    Sand = 1,
    Stone = 2,
    Granite = 3,
    Ruins = 4,
    HolyGround = 5,
}