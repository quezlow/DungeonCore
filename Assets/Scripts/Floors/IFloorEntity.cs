using UnityEngine;

/// <summary>
/// Opt-in interface for entities that occupy a stable tile cell and want to be
/// queryable by cell via FloorEntityRegistry.GetAtCell<T>.
///
/// Mobile entities (DungeonMonster, DungeonAdventurer) do NOT implement this —
/// they move continuously and a cell index would be stale every frame.
/// Placed entities (FurniturePiece, DungeonStairs, RoomAnchor, TrapBase, etc.)
/// implement it by exposing their OccupiedCell field.
///
/// Implementers should also be MonoBehaviours so they can be registered in
/// FloorEntityRegistry alongside their other type buckets.
/// </summary>
public interface IFloorEntity
{
    Vector3Int OccupiedCell { get; }
}