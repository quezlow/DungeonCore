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
        var pieces = Object.FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude);
        foreach (var piece in pieces)
        {
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

    private static List<FurniturePiece> FindFurnitureInRoom(HashSet<Vector3Int> roomTiles)
    {
        var result = new List<FurniturePiece>();
        var allPieces = Object.FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude);

        foreach (var piece in allPieces)
            if (roomTiles.Contains(piece.OccupiedCell))
                result.Add(piece);

        return result;
    }

    /// <summary>
    /// Day 28. Returns true if any MonsterSpawner with a BossVariantDefinition
    /// sits within the room tiles. Spawners are filtered to the active floor to
    /// match the flood-fill's scope (TileInfluenceManager.Instance is per-floor).
    /// </summary>
    private static bool HasBossSpawnerInRoom(HashSet<Vector3Int> roomTiles)
    {
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return false;

        var activeFloor = FloorManager.Instance != null ? FloorManager.Instance.ActiveFloor : null;
        var spawners = Object.FindObjectsByType<MonsterSpawner>(FindObjectsInactive.Exclude);

        foreach (var s in spawners)
        {
            if (!(s.Definition is BossVariantDefinition)) continue;

            // Restrict to spawners on the same floor as the flood-fill.
            if (activeFloor != null)
            {
                var sFloor = s.GetComponentInParent<FloorRoot>();
                if (sFloor != activeFloor) continue;
            }

            var cell = influence.WorldToCell(s.transform.position);
            if (roomTiles.Contains(cell)) return true;
        }

        return false;
    }
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