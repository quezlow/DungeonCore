using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Central controller for dungeon build modes.
///
/// CHANGES FROM PRE-DAY-27
///   - All TileInfluenceManager.Instance calls replaced with
///     FloorManager.Instance.ActiveFloor.TileInfluence (the player-viewed floor).
///   - TrapRegistry.Instance calls replaced with ActiveFloor.TrapRegistry.
///   - Placed objects parented under ActiveFloor so they belong to the correct floor.
///   - PlaceStairs BuildMode added (Day 27).
/// </summary>
public enum BuildMode
{
    Claim,
    PlaceEntrance,
    PlaceSpawner,
    PlaceChest,
    PlaceFurniture,
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
    [SerializeField] private ChestDefinition selectedChest;
    [SerializeField] private MonsterSpawner spawnerShellPrefab;
    [SerializeField] private RoomAnchor roomAnchorPrefab;
    [SerializeField] private FurnitureDefinition selectedFurniture;
    [SerializeField] private TrapDefinition selectedTrap;

    [Header("Stairs")]
    [SerializeField] private StairsDefinition stairsDefinition;

    // ── State ─────────────────────────────────────────────────────

    public BuildMode CurrentMode { get; private set; } = BuildMode.Claim;
    public event Action<BuildMode> OnModeChanged;

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
        if (TryHandleStairClick()) return;

        switch (CurrentMode)
        {
            case BuildMode.Claim: HandleClaimClick(); break;
            case BuildMode.PlaceEntrance: HandleEntrancePlacement(); break;
            case BuildMode.PlaceSpawner: HandleSpawnerPlacement(); break;
            case BuildMode.PlaceChest: HandleChestPlacement(); break;
            case BuildMode.PlaceFurniture: HandleFurniturePlacement(); break;
            case BuildMode.PlaceRoomAnchor: HandleRoomAnchorPlacement(); break;
            case BuildMode.PlaceTrap: HandleTrapPlacement(); break;
            case BuildMode.PlaceStairs: HandleStairsPlacement(); break;
        }
    }

    // ── Mode Switching ────────────────────────────────────────────

    public void SetMode(BuildMode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        Debug.Log($"[BuildController] Mode → {mode}");
        OnModeChanged?.Invoke(mode);
    }

    public void SetModeToClaim() => SetMode(BuildMode.Claim);
    public void SetModeToPlaceEntrance() => SetMode(BuildMode.PlaceEntrance);
    public void SetModeToPlaceSpawner() => SetMode(BuildMode.PlaceSpawner);
    public void SetModeToPlaceChest() => SetMode(BuildMode.PlaceChest);

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>Returns the active floor's TileInfluenceManager (player-viewed floor).</summary>
    private TileInfluenceManager ActiveInfluence
        => FloorManager.Instance?.ActiveFloor?.TileInfluence;

    /// <summary>Returns the active floor's TrapRegistry.</summary>
    private TrapRegistry ActiveTrapRegistry
        => FloorManager.Instance?.ActiveFloor?.TrapRegistry;

    /// <summary>Returns the active FloorRoot.</summary>
    private FloorRoot ActiveFloor
        => FloorManager.Instance?.ActiveFloor;

    // ── Claim ─────────────────────────────────────────────────────

    // ── Stair Click ───────────────────────────────────────────────

    private bool TryHandleStairClick()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        if (mainCamera == null) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        worldPos.z = 0f;

        // Find any DungeonStairs whose collider contains the click point.
        var allStairs = FindObjectsByType<DungeonStairs>(FindObjectsInactive.Include);
        foreach (var stair in allStairs)
        {
            var col = stair.GetComponent<Collider2D>();
            if (col == null) continue;

            // Only consider stairs on the currently viewed floor — otherwise
            // a Floor 1 stair at (0,0) would match clicks on Floor 2's (0,-2000).
            if (FloorManager.Instance != null && stair.FloorIndex != FloorManager.Instance.ActiveFloorIndex)
                continue;

            if (col.OverlapPoint(worldPos))
            {
                FloorManager.Instance?.SwitchToFloor(stair.LinkedFloorIndex);
                return true;
            }
        }

        return false;
    }

    private void HandleClaimClick()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;

        Debug.Log($"[Claim] cell={cell} claimable={ActiveInfluence.IsTileClaimable(cell)} owned={ActiveInfluence.IsTileOwned(cell)} ownedCount={ActiveInfluence.OwnedTileCount} floorIdx={FloorManager.Instance.ActiveFloorIndex}");

        if (!ActiveInfluence.IsTileClaimable(cell)) return;

        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(claimManaCost))
        {
            Debug.Log("[BuildController] Not enough mana to claim tile.");
            return;
        }

        ActiveInfluence.ClaimTile(cell);
    }

    // ── Entrance ──────────────────────────────────────────────────

    private void HandleEntrancePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Entrance must be placed on an owned tile.");
            return;
        }
        if (entrancePrefab == null) { Debug.LogError("[BuildController] entrancePrefab not assigned."); return; }

        if (DungeonEntrance.Instance != null)
            Destroy(DungeonEntrance.Instance.gameObject);

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            entrance.transform.SetParent(ActiveFloor.transform, true);
        entrance.Initialise(cell);

        Debug.Log($"[BuildController] Entrance placed at {cell}.");
        SetMode(BuildMode.Claim);
    }

    // ── Spawner ───────────────────────────────────────────────────

    private void HandleSpawnerPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Spawner must be placed on an owned tile.");
            return;
        }
        PlaceSpawner(cell);
    }

    private void PlaceSpawner(Vector3Int cell)
    {
        if (spawnerShellPrefab == null) { Debug.LogError("[BuildController] spawnerShellPrefab not assigned."); return; }

        var def = MonsterSelectionUI.Instance?.Selected;
        if (def == null) { Debug.LogError("[BuildController] No monster selected."); return; }

        if (!DungeonCore.Instance.TrySpendCapacity(def.capacityCost))
        {
            Debug.Log($"[BuildController] Not enough capacity for {def.monsterName}.");
            return;
        }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            spawner.transform.SetParent(ActiveFloor.transform, true);
        spawner.Initialise(def);

        SetMode(BuildMode.Claim);
    }

    // ── Chest ─────────────────────────────────────────────────────

    private void HandleChestPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Chest must be placed on an owned tile.");
            return;
        }
        if (selectedChest == null || selectedChest.prefab == null) { Debug.LogWarning("[BuildController] No chest selected."); return; }
        if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var chest = Instantiate(selectedChest.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            chest.transform.SetParent(ActiveFloor.transform, true);
        chest.Initialise(selectedChest);

        SetMode(BuildMode.Claim);
    }

    // ── Furniture ─────────────────────────────────────────────────

    private void HandleFurniturePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell)) return;
        if (selectedFurniture == null) { Debug.LogWarning("[BuildController] No furniture selected."); return; }
        if (selectedFurniture.blocksPathfinding && RoomValidator.WouldBlockDungeon(cell)) { Debug.Log("[BuildController] Would block path."); return; }
        if (!DungeonCore.Instance.SpendMana(selectedFurniture.manaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var piece = Instantiate(selectedFurniture.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            piece.transform.SetParent(ActiveFloor.transform, true);
        piece.Initialise(selectedFurniture, cell);

        RevalidateAllAnchors();
        SetMode(BuildMode.Claim);
    }

    // ── Room Anchor ───────────────────────────────────────────────

    private void HandleRoomAnchorPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell)) return;
        if (roomAnchorPrefab == null) { Debug.LogError("[BuildController] roomAnchorPrefab not assigned."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            anchor.transform.SetParent(ActiveFloor.transform, true);
        anchor.Initialise(cell);

        SetMode(BuildMode.Claim);
    }

    // ── Trap ──────────────────────────────────────────────────────

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Trap must be placed on an owned tile.");
            return;
        }
        if (selectedTrap == null || selectedTrap.prefab == null) { Debug.LogWarning("[BuildController] No trap selected."); return; }
        if (ActiveTrapRegistry != null && ActiveTrapRegistry.GetTrapAt(cell) != null) { Debug.Log("[BuildController] Trap already here."); return; }
        if (!DungeonCore.Instance.SpendMana(selectedTrap.manaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var trap = Instantiate(selectedTrap.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            trap.transform.SetParent(ActiveFloor.transform, true);
        trap.Initialise(selectedTrap, cell);

        if (trap is WarningTrap warning)
            WarningTrapNameDialog.Instance?.Open(warning);

        SetMode(BuildMode.Claim);
    }

    // ── Stairs ────────────────────────────────────────────────────

    private void HandleStairsPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Stairs must be placed on an owned tile.");
            return;
        }
        if (stairsDefinition == null || stairsDefinition.prefab == null) { Debug.LogError("[BuildController] stairsDefinition not assigned."); return; }
        if (FloorManager.Instance == null) { Debug.LogError("[BuildController] FloorManager not found."); return; }
        if (!DungeonCore.Instance.SpendMana(stairsDefinition.manaCost)) { Debug.Log("[BuildController] Not enough mana for stairs."); return; }

        int currentFloorIndex = FloorManager.Instance.ActiveFloorIndex;
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);

        // Place Down stair on current floor.
        var downStairs = Instantiate(stairsDefinition.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null)
            downStairs.transform.SetParent(ActiveFloor.transform, true);
        downStairs.Initialise(cell, currentFloorIndex, DungeonStairs.Direction.Down,
                              stairsDefinition.upVariantSprite);

        // Ensure the next floor exists (creates it if needed, seeded at this cell).
        int nextFloorIndex = currentFloorIndex + 1;
        FloorManager.Instance.EnsureFloorExists(nextFloorIndex, cell);

        // Place matching Up stair on the next floor.
        var nextFloor = FloorManager.Instance.GetFloor(nextFloorIndex);
        if (nextFloor?.TileInfluence != null)
        {
            // The next floor is offset by -2000 Y, but shares the same local cell grid.
            // Compute world position via the next floor's own tilemap.
            Vector3 upPos = nextFloor.TileInfluence.CellToWorld(cell);

            var upStairs = Instantiate(stairsDefinition.prefab, upPos, Quaternion.identity);
            upStairs.transform.SetParent(nextFloor.transform, true);
            upStairs.Initialise(cell, nextFloorIndex, DungeonStairs.Direction.Up,
                                stairsDefinition.upVariantSprite);
        }

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Stairs placed: floor {currentFloorIndex} ↔ {nextFloorIndex} at cell {cell}.");
    }

    // ── Restore (Save/Load) ───────────────────────────────────────

    public void RestoreEntrance(Vector3Int cell)
    {
        if (entrancePrefab == null) { Debug.LogError("[BuildController] entrancePrefab not assigned."); return; }
        if (DungeonEntrance.Instance != null) Destroy(DungeonEntrance.Instance.gameObject);

        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) entrance.transform.SetParent(ActiveFloor.transform, true);
        entrance.Initialise(cell);
    }

    public void RestoreSpawner(MonsterDefinition def, Vector3Int cell)
    {
        if (spawnerShellPrefab == null) { Debug.LogError("[BuildController] spawnerShellPrefab not assigned."); return; }
        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) spawner.transform.SetParent(ActiveFloor.transform, true);
        spawner.Initialise(def);
    }

    public void RestoreChest(ChestDefinition def, Vector3Int cell, bool isOpened)
    {
        if (def == null || def.prefab == null) return;
        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var chest = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) chest.transform.SetParent(ActiveFloor.transform, true);
        chest.Initialise(def);
        if (isOpened) chest.SetOpened(true);
    }

    public void RestoreFurniture(FurnitureDefinition def, Vector3Int cell)
    {
        if (def?.prefab == null) return;
        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var piece = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) piece.transform.SetParent(ActiveFloor.transform, true);
        piece.Initialise(def, cell);
    }

    public void RestoreRoomAnchor(Vector3Int cell, string roomName,
                                  FurnitureDefinitionRegistry furnitureRegistry,
                                  RoomDefinitionRegistry roomDefRegistry)
    {
        if (roomAnchorPrefab == null) return;
        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) anchor.transform.SetParent(ActiveFloor.transform, true);
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
        var influence = ActiveInfluence;
        if (influence == null) return;
        Vector3 worldPos = influence.CellToWorld(cell);
        var trap = Instantiate(def.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) trap.transform.SetParent(ActiveFloor.transform, true);
        trap.Initialise(def, cell);
        if (trap is WarningTrap warning && !string.IsNullOrEmpty(warningLabel))
            warning.SetWarningLabel(warningLabel);
        if (trap is PressurePlateTrap plate && hasLink)
            plate.SetLink(linkedCell);
        if (isFlagged) trap.Flag();
    }

    // ── Shared Input Helper ───────────────────────────────────────

    private bool LeftClickThisFrame(out Vector3Int cell)
    {
        cell = default;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        Debug.Log($"[Click] mouse pressed, overUI={EventSystem.current?.IsPointerOverGameObject()}");

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

        Debug.Log($"[Click] screenPos={screenPos}, worldPos={worldPos}, cameraPos={mainCamera.transform.position}");

        var influence = ActiveInfluence;
        if (influence == null) { Debug.Log("[Click] ActiveInfluence is null"); return false; }
        cell = influence.WorldToCell(worldPos);
        Debug.Log($"[Click] cell={cell}, activeFloor={FloorManager.Instance.ActiveFloorIndex}");
        return true;
    }

    private void RevalidateAllAnchors()
    {
        var anchors = FindObjectsByType<RoomAnchor>(FindObjectsInactive.Exclude);
        foreach (var a in anchors) a.Revalidate();
    }
}