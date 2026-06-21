using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// STAGE 2 — A plain Tile that strips TileFlags.LockColor (same idea as your
/// UnlockedRuleTile) so Tilemap.SetColor() takes effect per cell. The wall
/// debug overlay paints one of these everywhere and tints each cell by hand.
/// </summary>
[CreateAssetMenu(fileName = "UnlockedTile", menuName = "Dungeon/Unlocked Tile")]
public class UnlockedTile : Tile
{
    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        base.GetTileData(position, tilemap, ref tileData);
        tileData.flags &= ~TileFlags.LockColor;
    }
}