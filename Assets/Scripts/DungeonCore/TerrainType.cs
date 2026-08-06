/// <summary>
/// DAY 32 — Terrain types for the per-cell claim resistance system.
///
/// Resistance multipliers are looked up via TerrainResistanceTable
/// (a ScriptableObject) so balance can be tuned without code changes.
///
/// PLACEMENT NOTES
///   - Dirt / Sand / Stone / Granite are placed by TerrainTypeMap during
///     procgen (radial bands + random patches).
///   - Ruins and DwarvenMasonry are placed on Buried Age site masonry by
///     TerrainFeatureGenerator.MasonryTypeFor: DwarvenMasonry for the living
///     dwarven structures (village hold, gatehouse outpost), Ruins for every
///     dead site.
///   - HolyGround is placed on the four Church seal archetypes and the dead
///     core vault by that same MasonryTypeFor call -- masonry AND carved
///     interior, unlike a Buried Age site, which retypes its walls only.
///
/// Values serialise by int into assets (resistance table, wall families):
/// APPEND new members only; reordering or removal corrupts them silently.
/// </summary>
public enum TerrainType
{
    Dirt = 0,
    Sand = 1,
    Stone = 2,
    Granite = 3,
    Ruins = 4,
    HolyGround = 5,
    Bedrock = 6,
    DwarvenMasonry = 7,
}