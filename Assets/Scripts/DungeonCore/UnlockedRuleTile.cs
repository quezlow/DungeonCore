using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// DAY 32 — RuleTile variant that allows external Tilemap.SetColor() calls
/// to survive RuleTile's neighbor-cascade refresh.
///
/// Background: RuleTile.RefreshTile re-evaluates the 8 surrounding cells
/// whenever any tile changes near them. Each refresh re-runs GetTileData,
/// which writes TileFlags.LockColor into the cell — locking the cell color
/// to the tile asset's default. That makes per-cell tinting unreliable on
/// RuleTile-based tilemaps.
///
/// This subclass strips LockColor from the tile data so SetColor takes effect
/// and persists across refreshes. All other RuleTile behavior is unchanged.
/// </summary>
[CreateAssetMenu(fileName = "UnlockedRuleTile", menuName = "Dungeon/Unlocked Rule Tile")]
public class UnlockedRuleTile : RuleTile
{
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        base.GetTileData(position, tilemap, ref tileData);
        tileData.flags &= ~TileFlags.LockColor;
    }
}