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
    PlaceStairs,
}

public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    public void SetSelectedFurniture(FurnitureDefinition def) => selectedFurniture = def;
    public void SetSelectedTrap(TrapDefinition def) => selectedTrap = def;
    public void SetSelectedChest(ChestDefinition def) => selectedChest = def;

    // ── Inspector ─────────────────────────────────────────────────

    [Header("Mana Costs")]
    [SerializeField] private float claimManaCost = 5f;

    [Header("Prefabs")]
    [SerializeField] private DungeonEntrance entrancePrefab;
    [SerializeField] private ChestDefinition selectedChest;   // set by ChestSelectionUI
    [SerializeField] private MonsterSpawner spawnerShellPrefab; // Shell only — no MonsterDefinition assigned
    [SerializeField] private RoomAnchor roomAnchorPrefab;
    [SerializeField] private FurnitureDefinition selectedFurniture; // set by BuildSubmenu
    [SerializeField] private TrapDefinition selectedTrap;

    [Header("Stairs (single definition, no picker UI)")]
    [SerializeField] private StairsDefinition stairsDefinition;



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
            case BuildMode.PlaceStairs:
                HandleStairsPlacement();
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

        if (selectedChest == null || selectedChest.prefab == null)
        {
            Debug.LogWarning("[BuildController] No chest type selected.");
            return;
        }

        if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost))
        {
            Debug.Log("[BuildController] Not enough mana to place chest.");
            return;
        }

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var chest = Instantiate(selectedChest.prefab, worldPos, Quaternion.identity);
        chest.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        chest.Initialise(selectedChest);

        Debug.Log($"[BuildController] Placed {selectedChest.chestName} at {cell}.");
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
        spawner.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
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
        piece.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
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
        anchor.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
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
        trap.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        trap.Initialise(selectedTrap, cell);

        // Warning trap: prompt the player to name it.
        if (trap is WarningTrap warning)
            WarningTrapNameDialog.Instance?.Open(warning);

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Placed {selectedTrap.trapName} at {cell}.");
    }

    private void HandleStairsPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (TileInfluenceManager.Instance == null) return;

        if (!TileInfluenceManager.Instance.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Stairs must be placed on an owned tile.");
            return;
        }

        if (stairsDefinition == null || stairsDefinition.prefab == null)
        {
            Debug.LogError("[BuildController] stairsDefinition not assigned.");
            return;
        }

        if (FloorManager.Instance == null)
        {
            Debug.LogError("[BuildController] FloorManager.Instance is null.");
            return;
        }

        if (FloorManager.Instance.ActiveFloor == null)
        {
            Debug.LogError("[BuildController] No active floor — is Floor1Root set up in the scene with a FloorRoot component?");
            return;
        }

        if (!DungeonCore.Instance.SpendMana(stairsDefinition.manaCost))
        {
            Debug.Log("[BuildController] Not enough mana to place stairs.");
            return;
        }

        int currentFloorIndex = FloorManager.Instance.ActiveFloorIndex;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);

        // 1. Place Down stairs on current floor.
        var downStairs = Instantiate(stairsDefinition.prefab, worldPos, Quaternion.identity);
        // Parent to active floor's hierarchy so they deactivate together.
        downStairs.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        downStairs.Initialise(cell, currentFloorIndex, DungeonStairs.Direction.Down,
                               stairsDefinition.upVariantSprite);

        // 2. Ensure the next floor exists without switching to it. Pass the
        //    stair cell so the starter area is centered on the stair location.
        int nextFloorIndex = currentFloorIndex + 1;
        FloorManager.Instance.EnsureFloorExists(nextFloorIndex, cell);

        // 3. Place matching Up stairs on next floor (at same cell coordinates).
        var nextFloor = FloorManager.Instance.GetFloor(nextFloorIndex);
        if (nextFloor != null && nextFloor.TileInfluence != null)
        {
            // Force-claim the destination cell so the Up stair sits on owned tile.
            nextFloor.TileInfluence.ForceClaimTile(cell);

            // Workaround: Floor 2's CellToWorld currently returns (0,0,0) for any
            // input cell despite identical Grid settings. Since both floors share
            // a coordinate grid (same cells map to same world positions), use the
            // Down stair's worldPos directly for the Up stair as well.
            Vector3 upPos = worldPos;

            var upStairs = Instantiate(stairsDefinition.prefab, upPos, Quaternion.identity);
            upStairs.transform.SetParent(nextFloor.transform, true);
            upStairs.Initialise(cell, nextFloorIndex, DungeonStairs.Direction.Up,
                                 stairsDefinition.upVariantSprite);
        }

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Down stairs placed at {cell} on floor {currentFloorIndex}. " +
                  $"Up stairs placed on floor {nextFloorIndex}.");
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
        if (FloorManager.Instance?.ActiveFloor != null)
            entrance.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
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
        if (FloorManager.Instance?.ActiveFloor != null)
            spawner.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        spawner.Initialise(def);
        // Capacity is already restored from DungeonCoreSaveData — do not call TrySpendCapacity here.
    }

    public void RestoreChest(ChestDefinition def, Vector3Int cell, bool isOpened)
    {
        if (def == null || def.prefab == null) return;

        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var chest = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (FloorManager.Instance?.ActiveFloor != null)
            chest.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        chest.Initialise(def);
        if (isOpened) chest.SetOpened(true);
    }



    public void RestoreFurniture(FurnitureDefinition def, Vector3Int cell)
    {
        if (def?.prefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var piece = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (FloorManager.Instance?.ActiveFloor != null)
            piece.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        piece.Initialise(def, cell);
    }

    public void RestoreRoomAnchor(Vector3Int cell, string roomName,
                                  FurnitureDefinitionRegistry furnitureRegistry,
                                  RoomDefinitionRegistry roomDefRegistry)
    {
        if (roomAnchorPrefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (FloorManager.Instance?.ActiveFloor != null)
            anchor.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        anchor.Initialise(cell);

        if (!string.IsNullOrEmpty(roomName))
        {
            var def = roomDefRegistry?.GetByName(roomName);
            if (def != null) anchor.SetRoomType(def);
        }
    }

    public void RestoreTrap(TrapDefinition def, Vector3Int cell, bool isFlagged,
                        string warningLabel = "", bool hasLink = false,
                        Vector3Int linkedCell = default)
    {
        if (def == null || def.prefab == null) return;
        Vector3 worldPos = TileInfluenceManager.Instance.CellToWorld(cell);
        var trap = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (FloorManager.Instance?.ActiveFloor != null)
            trap.transform.SetParent(FloorManager.Instance.ActiveFloor.transform, true);
        trap.Initialise(def, cell);

        if (trap is WarningTrap warning && !string.IsNullOrEmpty(warningLabel))
            warning.SetWarningLabel(warningLabel);

        if (trap is PressurePlateTrap plate && hasLink)
            plate.SetLink(linkedCell);

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