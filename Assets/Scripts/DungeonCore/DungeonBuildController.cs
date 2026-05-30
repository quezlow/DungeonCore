using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Central controller for dungeon build modes.
/// All mouse-click placement logic routes through here.
/// TileInfluenceManager no longer handles its own click input.
/// </summary>
public enum BuildMode
{
    Claim,          // Default — click claimable tiles to expand influence
    PlaceEntrance,  // Click an owned tile to place the dungeon entrance
    PlaceSpawner,   // Click an owned tile to place a monster spawner
    PlaceChest,     // Click an owned tile to place a treasure chest
    PlaceFurniture, // PlaceTrap, PlaceFurniture, etc. added in later sessions
    PlaceRoomAnchor,
    PlaceTrap,
}

public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    public void SetSelectedFurniture(FurnitureDefinition def) => selectedFurniture = def;
    public void SetSelectedTrap(TrapDefinition def) => selectedTrap = def;

    // ── Inspector ─────────────────────────────────────────────────

    [Header("Mana Costs")]
    [SerializeField] private float claimManaCost = 5f;

    [Header("Prefabs")]
    [SerializeField] private DungeonEntrance entrancePrefab;
    [SerializeField] private DungeonChest chestPrefab;
    [SerializeField] private MonsterSpawner spawnerShellPrefab; // Shell only — no MonsterDefinition assigned
    [SerializeField] private RoomAnchor roomAnchorPrefab;
    [SerializeField] private FurnitureDefinition selectedFurniture; // set by BuildSubmenu
    [SerializeField] private TrapDefinition selectedTrap;


    // ── State ─────────────────────────────────────────────────────

    public BuildMode CurrentMode { get; private set; } = BuildMode.Claim;

    /// <summary>Fires whenever the active build mode changes.</summary>
    public event Action<BuildMode> OnModeChanged;

    // ── Internal ──────────────────────────────────────────────────

    private Camera mainCamera;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        switch (CurrentMode)
        {
            case BuildMode.Claim:
                HandleClaimClick();
                break;
            case BuildMode.PlaceEntrance:
                HandleEntrancePlacement();
                break;
            case BuildMode.PlaceSpawner:
                HandleSpawnerPlacement();
                break;
            case BuildMode.PlaceChest:
                HandleChestPlacement();
                break;
            case BuildMode.PlaceFurniture:
                HandleFurniturePlacement();
                break;
            case BuildMode.PlaceRoomAnchor:
                HandleRoomAnchorPlacement();
                break;
            case BuildMode.PlaceTrap:
                HandleTrapPlacement();
                break;
        }
    }

    // ── Mode Switching ────────────────────────────────────────────

    public void SetMode(BuildMode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        Debug.Log($"[BuildController] Mode set to: {mode}");
        OnModeChanged?.Invoke(mode);
    }

    // Convenience wrappers for UI button OnClick events
    public void SetModeToClaim() => SetMode(BuildMode.Claim);
    public void SetModeToPlaceEntrance() => SetMode(BuildMode.PlaceEntrance);
    public void SetModeToPlaceSpawner() => SetMode(BuildMode.PlaceSpawner);
    public void SetModeToPlaceChest() => SetMode(BuildMode.PlaceChest);

    // ── Claim Mode ────────────────────────────────────────────────

    private void HandleClaimClick()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;
        if (!TileInfluenceManager.Instance.IsTileClaimable(cell)) return;

        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(claimManaCost))
        {
            Debug.Log("[BuildController] Not enough mana to claim tile.");
            return;
        }

        TileInfluenceManager.Instance.ClaimTile(cell);
    }

    // ── Entrance Placement ────────────────────────────────────────

    private void HandleEntrancePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;

        if (!TileInfluenceManager.Instance.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Entrance must be placed on an owned tile.");
            return;
        }

        if (entrancePrefab == null)
        {
            Debug.LogError("[BuildController] entrancePrefab is not assigned.");
            return;
        }

        if (DungeonEntrance.Instance != null)
            Destroy(DungeonEntrance.Instance.gameObject);

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        entrance.Initialise(cell);

        Debug.Log($"[BuildController] Entrance placed at cell {cell}.");
        SetMode(BuildMode.Claim);
    }

    // ── Spawner Placement ─────────────────────────────────────────

    private void HandleSpawnerPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;

        if (!TileInfluenceManager.Instance.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Spawner must be placed on an owned tile.");
            return;
        }

        PlaceSpawner(cell);
    }

    private void HandleChestPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;

        if (!TileInfluenceManager.Instance.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Chest must be placed on an owned tile.");
            return;
        }

        if (chestPrefab == null)
        {
            Debug.LogError("[BuildController] chestPrefab is not assigned.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        Instantiate(chestPrefab, worldPos, Quaternion.identity);

        Debug.Log($"[BuildController] Chest placed at cell {cell}.");
        SetMode(BuildMode.Claim);
    }

    private void PlaceSpawner(Vector3Int cell)
    {
        if (spawnerShellPrefab == null)
        {
            Debug.LogError("[BuildController] spawnerShellPrefab is not assigned.");
            return;
        }

        var def = MonsterSelectionUI.Instance?.Selected;
        if (def == null)
        {
            Debug.LogError("[BuildController] No monster type selected in MonsterSelectionUI.");
            return;
        }

        if (!DungeonCore.Instance.TrySpendCapacity(def.capacityCost))
        {
            Debug.Log($"[BuildController] Not enough capacity for {def.monsterName} " +
                      $"(costs {def.capacityCost}, free: {DungeonCore.Instance.FreeCapacity}).");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.Initialise(def);

        Debug.Log($"[BuildController] {def.monsterName} spawner placed. " +
                  $"Capacity: {DungeonCore.Instance.MaxCapacity - DungeonCore.Instance.UsedCapacity}/{DungeonCore.Instance.MaxCapacity}");
        SetMode(BuildMode.Claim);
    }

    private void HandleFurniturePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (!TileInfluenceManager.Instance.IsTileOwned(cell)) return;

        if (selectedFurniture == null)
        {
            Debug.LogWarning("[BuildController] No furniture type selected.");
            return;
        }

        if (selectedFurniture.blocksPathfinding && RoomValidator.WouldBlockDungeon(cell))
        {
            Debug.Log("[BuildController] Placement rejected — would block room path.");
            return;
        }

        if (!DungeonCore.Instance.SpendMana(selectedFurniture.manaCost))
        {
            Debug.Log("[BuildController] Not enough mana to place furniture.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var piece = Instantiate(selectedFurniture.prefab, worldPos, Quaternion.identity);
        piece.Initialise(selectedFurniture, cell);

        RevalidateAllAnchors();
        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Placed {selectedFurniture.furnitureName} at {cell}.");
    }

    private void HandleRoomAnchorPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (!TileInfluenceManager.Instance.IsTileOwned(cell)) return;

        if (roomAnchorPrefab == null)
        {
            Debug.LogError("[BuildController] roomAnchorPrefab not assigned.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        anchor.Initialise(cell);

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Room anchor placed at {cell}. Click it to assign a room type.");
    }

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;

        if (!TileInfluenceManager.Instance.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Trap must be placed on an owned tile.");
            return;
        }

        if (selectedTrap == null || selectedTrap.prefab == null)
        {
            Debug.LogWarning("[BuildController] No trap type selected.");
            return;
        }

        // Reject if another trap is already on this cell.
        if (TrapRegistry.Instance != null && TrapRegistry.Instance.GetTrapAt(cell) != null)
        {
            Debug.Log("[BuildController] A trap already exists on this tile.");
            return;
        }

        if (!DungeonCore.Instance.SpendMana(selectedTrap.manaCost))
        {
            Debug.Log("[BuildController] Not enough mana to place trap.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var trap = Instantiate(selectedTrap.prefab, worldPos, Quaternion.identity);
        trap.Initialise(selectedTrap, cell);

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Placed {selectedTrap.trapName} at {cell}.");
    }

    // ── Restore (called by DungeonSaveController on load) ────────────

    public void RestoreEntrance(Vector3Int cell)
    {
        if (entrancePrefab == null)
        {
            Debug.LogError("[BuildController] entrancePrefab not assigned — cannot restore entrance.");
            return;
        }

        if (DungeonEntrance.Instance != null)
            Destroy(DungeonEntrance.Instance.gameObject);

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        entrance.Initialise(cell);
    }

    public void RestoreSpawner(MonsterDefinition def, Vector3Int cell)
    {
        if (spawnerShellPrefab == null)
        {
            Debug.LogError("[BuildController] spawnerShellPrefab not assigned — cannot restore spawner.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.Initialise(def);
        // Capacity is already restored from DungeonCoreSaveData — do not call TrySpendCapacity here.
    }

    public void RestoreChest(Vector3Int cell, bool isOpened)
    {
        if (chestPrefab == null)
        {
            Debug.LogError("[BuildController] chestPrefab not assigned — cannot restore chest.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var chest = Instantiate(chestPrefab, worldPos, Quaternion.identity);
        if (isOpened) chest.SetOpened(true);
    }

    public void RestoreFurniture(FurnitureDefinition def, Vector3Int cell)
    {
        if (def?.prefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var piece = Instantiate(def.prefab, worldPos, Quaternion.identity);
        piece.Initialise(def, cell);
    }

    public void RestoreRoomAnchor(Vector3Int cell, string roomName,
                                  FurnitureDefinitionRegistry furnitureRegistry,
                                  RoomDefinitionRegistry roomDefRegistry)
    {
        if (roomAnchorPrefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        anchor.Initialise(cell);

        if (!string.IsNullOrEmpty(roomName))
        {
            var def = roomDefRegistry?.GetByName(roomName);
            if (def != null) anchor.SetRoomType(def);
        }
    }

    public void RestoreTrap(TrapDefinition def, Vector3Int cell, bool isFlagged)
    {
        if (def == null || def.prefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var trap = Instantiate(def.prefab, worldPos, Quaternion.identity);
        trap.Initialise(def, cell);
        if (isFlagged) trap.Flag();
    }

    private void RevalidateAllAnchors()
    {
        var anchors = FindObjectsByType<RoomAnchor>(FindObjectsInactive.Exclude);
        foreach (var a in anchors)
            a.Revalidate();
    }


    // ── Shared Input Helper ───────────────────────────────────────

    private bool LeftClickThisFrame(out Vector3Int cell)
    {
        cell = default;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, 0f));

        if (TileInfluenceManager.Instance == null) return false;
        cell = TileInfluenceManager.Instance.WorldToCell(worldPos);
        return true;
    }
}