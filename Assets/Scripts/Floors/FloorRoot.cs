using UnityEngine;

/// <summary>
/// Component placed on the root GameObject of each floor's hierarchy.
/// Holds the floor's index and references to its per-floor managers.
/// FloorManager finds all FloorRoots in the scene to manage floor switching.
///
/// SETUP
///   Floor 1 (the player's starting setup):
///     - Create an empty GameObject named "Floor1Root" at the scene root.
///     - Add this FloorRoot component, set FloorIndex = 0.
///     - Assign the existing TileInfluenceManager / TrapRegistry from the scene.
///     - The existing managers do NOT need to be reparented under Floor1Root.
///
///   FloorTemplate prefab (used to instantiate Floors 2, 3, ...):
///     - Self-contained prefab with its own Grid + tilemaps + per-floor managers.
///     - The prefab's FloorRoot has its references pre-wired internally.
///     - FloorManager assigns the runtime FloorIndex on instantiation.
/// </summary>
public class FloorRoot : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Set 0 for Floor 1. Floors created at runtime have their index assigned by FloorManager.")]
    [SerializeField] private int floorIndex = 0;

    [Header("Per-Floor Managers")]
    [SerializeField] private TileInfluenceManager tileInfluence;
    [SerializeField] private TrapRegistry trapRegistry;

    [Header("Starter Content (Floor 2+ only)")]
    [Tooltip("Tiles to claim automatically when this floor is first created. " +
             "Used to give newly-created floors a small starter area for orientation.")]
    [SerializeField]
    private Vector3Int[] starterClaimedTiles = new[]
    {
        new Vector3Int(-1, -1, 0), new Vector3Int(0, -1, 0), new Vector3Int(1, -1, 0),
        new Vector3Int(-1,  0, 0), new Vector3Int(0,  0, 0), new Vector3Int(1,  0, 0),
        new Vector3Int(-1,  1, 0), new Vector3Int(0,  1, 0), new Vector3Int(1,  1, 0),
    };

    // ── Properties ────────────────────────────────────────────────

    public int FloorIndex => floorIndex;
    public TileInfluenceManager TileInfluence => tileInfluence;
    public TrapRegistry TrapReg => trapRegistry;

    public bool HasBeenPopulated { get; private set; } = false;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void OnEnable()
    {
        FloorManager.Instance?.RegisterFloor(this);
    }

    private void OnDestroy()
    {
        // Unregister only on permanent destruction. Floors that are temporarily
        // deactivated (because the player switched view to another floor) must
        // stay in the dictionary so the player can switch back to them.
        FloorManager.Instance?.UnregisterFloor(this);
    }

    // ── Setup ─────────────────────────────────────────────────────

    /// <summary>Called by FloorManager when this floor is created at runtime.</summary>
    public void SetFloorIndex(int index) => floorIndex = index;

    /// <summary>
    /// Claims the starter tiles defined in starterClaimedTiles. Called once
    /// when a new floor is created to give it a small visible area.
    /// </summary>
    public void PopulateStarterArea()
    {
        if (HasBeenPopulated) return;
        if (tileInfluence == null) return;

        foreach (var cell in starterClaimedTiles)
            tileInfluence.ForceClaimTile(cell);

        HasBeenPopulated = true;
    }
}