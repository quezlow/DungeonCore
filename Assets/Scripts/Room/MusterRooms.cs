using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Static lookups answering "may this monster stand here?" for spawner
/// placement and respawn ticking. A muster ground is a valid room whose
/// spawnCategories list contains the monster's category. Bosses route by
/// room type instead: for placement, any Boss Room footprint that validates
/// once its own boss-spawner requirement is set aside (the boss being placed
/// is what completes the room); for respawn, a plainly valid Boss Room.
///
/// Spawners placed before this system carry no muster gate and are never
/// asked these questions (MonsterSpawner.MusterGated is false on them).
/// </summary>
public static class MusterRooms
{
    private static readonly List<RoomAnchor> anchorBuf = new();
    private static readonly StringBuilder nameBuf = new();

    /// <summary>True when the cell lies inside a room that musters the
    /// definition on this floor. forPlacement relaxes the Boss Room's own
    /// boss-spawner requirement so the first boss can ever be placed.</summary>
    public static bool IsMusterGround(FloorRoot floor, Vector3Int cell,
                                      MonsterDefinition def, bool forPlacement)
    {
        if (floor == null || floor.Entities == null || def == null) return false;
        bool isBoss = def is BossVariantDefinition;

        int n = floor.Entities.FillAll(anchorBuf);
        for (int i = 0; i < n; i++)
        {
            var anchor = anchorBuf[i];
            if (anchor == null || anchor.AssignedRoom == null) continue;

            if (isBoss)
            {
                if (!anchor.AssignedRoom.requiresBossSpawner) continue;
                if (forPlacement)
                {
                    var result = RoomValidator.Validate(
                        anchor.Footprint, anchor.AssignedRoom, ignoreBossSpawner: true);
                    if (result.IsValid && result.RoomTiles.Contains(cell)) return true;
                }
                else
                {
                    var tiles = anchor.IsValid ? anchor.GetRoomTiles() : null;
                    if (tiles != null && tiles.Contains(cell)) return true;
                }
                continue;
            }

            var cats = anchor.AssignedRoom.spawnCategories;
            if (cats == null || !cats.Contains(def.category)) continue;
            if (anchor.IsValid)
            {
                var roomTiles = anchor.GetRoomTiles();
                if (roomTiles != null && roomTiles.Contains(cell)) return true;
                continue;
            }
            // A Boss Room stands invalid until a boss is promoted within it;
            // placement is still legal so the hall can receive its tenant
            // (the spawner sits respawn-paused until the promotion).
            if (forPlacement && anchor.AssignedRoom.requiresBossSpawner)
            {
                var result = RoomValidator.Validate(
                    anchor.Footprint, anchor.AssignedRoom, ignoreBossSpawner: true);
                if (result.IsValid && result.RoomTiles.Contains(cell)) return true;
            }
        }
        return false;
    }

    /// <summary>Fills outBuf with the floor's anchors currently eligible to
    /// muster the definition (drives the placement highlight). Returns count.</summary>
    public static int FillEligibleAnchors(FloorRoot floor, MonsterDefinition def,
                                          List<RoomAnchor> outBuf)
    {
        outBuf.Clear();
        if (floor == null || floor.Entities == null || def == null) return 0;
        bool isBoss = def is BossVariantDefinition;

        int n = floor.Entities.FillAll(anchorBuf);
        for (int i = 0; i < n; i++)
        {
            var anchor = anchorBuf[i];
            if (anchor == null || anchor.AssignedRoom == null) continue;
            if (isBoss)
            {
                if (!anchor.AssignedRoom.requiresBossSpawner) continue;
                var result = RoomValidator.Validate(
                    anchor.Footprint, anchor.AssignedRoom, ignoreBossSpawner: true);
                if (result.IsValid) outBuf.Add(anchor);
                continue;
            }
            var cats = anchor.AssignedRoom.spawnCategories;
            if (cats == null || !cats.Contains(def.category)) continue;
            if (anchor.IsValid) { outBuf.Add(anchor); continue; }
            if (anchor.AssignedRoom.requiresBossSpawner
                && RoomValidator.Validate(anchor.Footprint, anchor.AssignedRoom,
                                          ignoreBossSpawner: true).IsValid)
                outBuf.Add(anchor);
        }
        return outBuf.Count;
    }

    /// <summary>Comma-joined names of the room types that muster the category,
    /// read from the registry the room picker uses. Empty string when the
    /// picker is absent or nothing is authored.</summary>
    public static string MusterRoomNames(MonsterCategory category)
    {
        var registry = RoomTypePickerUI.Instance != null
            ? RoomTypePickerUI.Instance.Registry : null;
        if (registry == null || registry.All == null) return "";

        nameBuf.Clear();
        for (int i = 0; i < registry.All.Count; i++)
        {
            var room = registry.All[i];
            if (room == null || room.spawnCategories == null) continue;
            if (!room.spawnCategories.Contains(category)) continue;
            if (nameBuf.Length > 0) nameBuf.Append(", ");
            nameBuf.Append(room.roomName);
        }
        return nameBuf.ToString();
    }
}
