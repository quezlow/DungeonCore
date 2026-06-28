using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static utility. Called by RoomAnchor when the player assigns or changes a
/// room type, and by DungeonBuildController after furniture placement to re-validate
/// any anchor whose room contains the modified tile.
///
/// VALIDATION STEPS
///   1. Flood-fill from the anchor cell through owned tiles to find the room boundary.
///   2. Check tile count meets RoomDefinition.minTileCount.
///   3. Count FurniturePiece instances within the boundary, match against requirements.
///   4. (Day 28) If requiresBossSpawner, check that a MonsterSpawner with a
///      BossVariantDefinition exists within the room tiles on the active floor.
///   5. (Optional, called separately) Walkability check after furniture placement.
///
/// FLOOD-FILL BOUNDARY
///   The fill stops at undug (not owned) tiles. Owned tiles separated by a wall are
///   treated as a different room. The fill is capped at maxFloodFillTiles to prevent
///   a single open-plan dungeon from becoming one infinite room.
/// </summary>
public static class RoomValidator
{
    private const int MaxFloodFillTiles = 200;

    private static readonly Vector3Int[] Directions =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Public API ────────────────────────────────────────────────

    public static ValidationResult Validate(Vector3Int anchorCell, RoomDefinition roomDef)
    {
        if (roomDef == null)
            return ValidationResult.Fail("No room type assigned.");

        if (TileInfluenceManager.Instance == null)
            return ValidationResult.Fail("TileInfluenceManager not available.");

        // Step 1 — flood-fill to find room tiles.
        var roomTiles = FloodFill(anchorCell);

        // Step 2 — size check.
        if (roomTiles.Count < roomDef.minTileCount)
            return ValidationResult.Fail(
                $"{roomDef.roomName} requires at least {roomDef.minTileCount} tiles " +
                $"(found {roomTiles.Count}).");

        // Step 3 — furniture check.
        if (roomDef.requiredFurniture != null && roomDef.requiredFurniture.Count > 0)
        {
            var pieces = FindFurnitureInRoom(roomTiles);
            foreach (var req in roomDef.requiredFurniture)
            {
                if (req.furnitureType == null) continue;

                int count = 0;
                foreach (var p in pieces)
                    if (p.Definition == req.furnitureType) count++;

                if (count < req.minimumCount)
                    return ValidationResult.Fail(
                        $"{roomDef.roomName} requires {req.minimumCount}× " +
                        $"{req.furnitureType.furnitureName} (found {count}).");
            }
        }

        // Step 4 — boss spawner check (Day 28).
        if (roomDef.requiresBossSpawner)
        {
            if (!HasBossSpawnerInRoom(roomTiles))
                return ValidationResult.Fail(
                    $"{roomDef.roomName} requires a boss-variant monster spawner.");
        }

        return ValidationResult.Pass(roomTiles);
    }

    /// <summary>
    /// Footprint-based validation. The room IS the player-designated cells (not a
    /// flood-fill). Intersect the footprint with currently-mined tiles, then run the
    /// same size / furniture / boss checks against that set. No upper size cap.
    /// </summary>
    public static ValidationResult Validate(
        IReadOnlyList<Vector3Int> footprint, RoomDefinition roomDef)
    {
        if (roomDef == null)
            return ValidationResult.Fail("No room type assigned.");
                // Validate against the ACTIVE floor's influence. TileInfluenceManager.Instance
        // is reassigned by every floor's manager in Awake, so it can point at the wrong
        // floor; the footprint was captured against the active floor, so match that.
        var influence = FloorManager.Instance?.ActiveFloor?.TileInfluence;
        if (influence == null) influence = TileInfluenceManager.Instance;
        if (influence == null)
            return ValidationResult.Fail("TileInfluenceManager not available.");

        var roomTiles = new HashSet<Vector3Int>();
        if (footprint != null)
            for (int i = 0; i < footprint.Count; i++)
                if (influence.IsTileMined(footprint[i]))
                    roomTiles.Add(footprint[i]);

        if (roomTiles.Count < roomDef.minTileCount)
            return ValidationResult.Fail(
                $"{roomDef.roomName} requires at least {roomDef.minTileCount} tiles " +
                $"(found {roomTiles.Count}).");

        if (roomDef.requiredFurniture != null && roomDef.requiredFurniture.Count > 0)
        {
            var pieces = FindFurnitureInRoom(roomTiles);
            foreach (var req in roomDef.requiredFurniture)
            {
                if (req.furnitureType == null) continue;
                int count = 0;
                foreach (var p in pieces)
                    if (p.Definition == req.furnitureType) count++;
                if (count < req.minimumCount)
                    return ValidationResult.Fail(
                        $"{roomDef.roomName} requires {req.minimumCount}× " +
                        $"{req.furnitureType.furnitureName} (found {count}).");
            }
        }

        if (roomDef.requiresBossSpawner)
        {
            if (!HasBossSpawnerInRoom(roomTiles))
                return ValidationResult.Fail(
                    $"{roomDef.roomName} requires a boss-variant monster spawner.");
        }

        return ValidationResult.Pass(roomTiles);
    }

    /// <summary>Public flood-fill from a cell — seeds footprints for old saves.</summary>
    public static HashSet<Vector3Int> FloodFillRoom(Vector3Int origin) => FloodFill(origin);

    public static bool WouldBlockRoom(Vector3Int anchorCell, Vector3Int blockedCell)
    {
        if (TileInfluenceManager.Instance == null) return false;

        var excludeSet = new HashSet<Vector3Int> { blockedCell };
        var reachable = FloodFill(anchorCell, excludeSet);
        var original = FloodFill(anchorCell);

        foreach (var tile in original)
        {
            if (tile == blockedCell) continue;
            if (!reachable.Contains(tile)) return true;
        }

        return false;
    }

    public static bool WouldBlockDungeon(Vector3Int blockedCell)
    {
        if (TileInfluenceManager.Instance == null) return false;
        if (DungeonCore.Instance == null) return false;

        Vector3Int coreCell = TileInfluenceManager.Instance.WorldToCell(
            DungeonCore.Instance.transform.position);

        if (blockedCell == coreCell) return true;

        var alreadyBlocked = GetBlockedFurnitureCells();
        var originalReachable = FloodFill(coreCell, alreadyBlocked);

        var proposedBlocked = new HashSet<Vector3Int>(alreadyBlocked) { blockedCell };
        var afterReachable = FloodFill(coreCell, proposedBlocked);

        foreach (var tile in originalReachable)
        {
            if (tile == blockedCell) continue;
            if (!afterReachable.Contains(tile)) return true;
        }

        return false;
    }

    private static HashSet<Vector3Int> GetBlockedFurnitureCells()
    {
        var set = new HashSet<Vector3Int>();
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return set;

        var buf = _furnitureBuf ??= new List<FurniturePiece>();
        floor.Entities.FillAll(buf);
        for (int i = 0; i < buf.Count; i++)
        {
            var piece = buf[i];
            if (piece.Definition != null && piece.Definition.blocksPathfinding)
                set.Add(piece.OccupiedCell);
        }
        return set;
    }

    // ── Internal ──────────────────────────────────────────────────

    private static HashSet<Vector3Int> FloodFill(
        Vector3Int origin, HashSet<Vector3Int> excludeCells = null)
    {
        var visited = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();

        if (!TileInfluenceManager.Instance.IsTileMined(origin)) return visited;
        if (excludeCells != null && excludeCells.Contains(origin)) return visited;

        visited.Add(origin);
        queue.Enqueue(origin);

        while (queue.Count > 0 && visited.Count < MaxFloodFillTiles)
        {
            var current = queue.Dequeue();

            foreach (var dir in Directions)
            {
                var next = current + dir;
                if (visited.Contains(next)) continue;
                if (excludeCells != null && excludeCells.Contains(next)) continue;
                if (!TileInfluenceManager.Instance.IsTileMined(next)) continue;

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return visited;
    }

    public static List<FurniturePiece> FindFurnitureInRoom(HashSet<Vector3Int> roomTiles)
    {
        var result = new List<FurniturePiece>();
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return result;

        var buf = _furnitureBuf ??= new List<FurniturePiece>();
        floor.Entities.FillAll(buf);
        for (int i = 0; i < buf.Count; i++)
        {
            var piece = buf[i];
            if (roomTiles.Contains(piece.OccupiedCell)) result.Add(piece);
        }
        return result;
    }
    private static List<FurniturePiece> _furnitureBuf;

    /// <summary>
    /// Day 28. Returns true if any MonsterSpawner with a BossVariantDefinition
    /// sits within the room tiles. Spawners are filtered to the active floor to
    /// match the flood-fill's scope (TileInfluenceManager.Instance is per-floor).
    /// </summary>
    public static bool HasBossSpawnerInRoom(HashSet<Vector3Int> roomTiles)
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return false;

        var influence = floor.TileInfluence != null ? floor.TileInfluence : TileInfluenceManager.Instance;
        if (influence == null) return false;

        var buf = _spawnerBuf ??= new List<MonsterSpawner>();
        floor.Entities.FillAll(buf);
        for (int i = 0; i < buf.Count; i++)
        {
            var s = buf[i];
            if (!(s.Definition is BossVariantDefinition)) continue;
            var cell = influence.WorldToCell(s.transform.position);
            if (roomTiles.Contains(cell)) return true;
        }
        return false;
    }
    private static List<MonsterSpawner> _spawnerBuf;
}

internal static class RoomValidatorExtensions
{
    public static void Enqueue(this HashSet<Vector3Int> visited,
                               Vector3Int cell, Queue<Vector3Int> queue)
    {
        visited.Add(cell);
        queue.Enqueue(cell);
    }
}

public class ValidationResult
{
    public bool IsValid;
    public string FailReason;
    public HashSet<Vector3Int> RoomTiles;

    public static ValidationResult Pass(HashSet<Vector3Int> tiles) =>
        new() { IsValid = true, RoomTiles = tiles };

    public static ValidationResult Fail(string reason) =>
        new() { IsValid = false, FailReason = reason };
}