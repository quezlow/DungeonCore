using UnityEngine;

/// <summary>
/// The dungeon entrance. Placed by the player via DungeonBuildController (PlaceEntrance mode).
/// Only one entrance exists at a time. Adventurers spawn here and exit here.
/// </summary>
public class DungeonEntrance : MonoBehaviour
{
    public static DungeonEntrance Instance { get; private set; }

    // ── Public ────────────────────────────────────────────────────

    /// <summary>World-space position adventurers spawn at and retreat to.</summary>
    public Vector3 SpawnPosition => transform.position;

    /// <summary>The cell this entrance occupies (set on placement).</summary>
    public Vector3Int OccupiedCell { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ── Setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonBuildController after instantiation to record
    /// which cell this entrance sits on.
    /// </summary>
    public void Initialise(Vector3Int cell)
    {
        OccupiedCell = cell;
    }
}
