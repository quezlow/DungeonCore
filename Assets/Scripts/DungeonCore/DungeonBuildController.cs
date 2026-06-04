using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Central controller for dungeon build modes.
///
/// DAY 27 + SAVE
///   - All Restore* methods now take an explicit FloorRoot so save/load
///     places objects on the correct floor regardless of current ActiveFloor.
///   - Live placement methods still use ActiveFloor.
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
    PlaceCore,
}

public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    public void SetSelectedFurniture(FurnitureDefinition def) => selectedFurniture = def;
    public void SetSelectedTrap(TrapDefinition def) => selectedTrap = def;
    public void SetSelectedChest(ChestDefinition def) => selectedChest = def;

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

    public BuildMode CurrentMode { get; private set; } = BuildMode.Claim;
    public event Action<BuildMode> OnModeChanged;

    private Camera mainCamera;

// Drag-claim state — tracks the last cell visited during a held-drag claim
// so we don't re-attempt the same cell every frame, and so we only fire
// once per new cell as the mouse moves.
private Vector3Int dragClaimLastCell;
private bool dragClaimActive;

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
            case BuildMode.PlaceCore: HandlePlaceCoreMode(); break;
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

    public void SetModeToPlaceCore()
    {
        if (FloorManager.Instance == null) { Debug.LogError("[BuildController] No FloorManager."); return; }
        if (!FloorManager.Instance.CanPlaceCore) { Debug.Log("[BuildController] Place Core not available."); return; }
        if (DungeonCore.Instance != null && DungeonCore.Instance.IsInTransit) { Debug.Log("[BuildController] Core already in transit."); return; }

        int destIdx = FloorManager.Instance.PendingCoreRelocationFloor;
        FloorManager.Instance.SwitchToFloor(destIdx);
        SetMode(BuildMode.PlaceCore);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private TileInfluenceManager ActiveInfluence => FloorManager.Instance?.ActiveFloor?.TileInfluence;
    private TrapRegistry ActiveTrapRegistry => FloorManager.Instance?.ActiveFloor?.TrapRegistry;
    private FloorRoot ActiveFloor => FloorManager.Instance?.ActiveFloor;

    // ── Claim ─────────────────────────────────────────────────────

    private void HandleClaimClick()
    {
        if (!ClaimInputThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileClaimable(cell)) return;
        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(claimManaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }
        ActiveInfluence.ClaimTile(cell);
    }

    // ── Entrance ──────────────────────────────────────────────────

    private void HandleEntrancePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell)) { Debug.Log("[BuildController] Entrance must be on owned tile."); return; }
        if (entrancePrefab == null) { Debug.LogError("[BuildController] entrancePrefab not assigned."); return; }

        if (DungeonEntrance.Instance != null) Destroy(DungeonEntrance.Instance.gameObject);

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) entrance.transform.SetParent(ActiveFloor.transform, true);
        entrance.Initialise(cell);
        SetMode(BuildMode.Claim);
    }

    // ── Spawner ───────────────────────────────────────────────────

    private void HandleSpawnerPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell)) { Debug.Log("[BuildController] Spawner must be on owned tile."); return; }
        PlaceSpawner(cell);
    }

    private void PlaceSpawner(Vector3Int cell)
    {
        if (spawnerShellPrefab == null) { Debug.LogError("[BuildController] spawnerShellPrefab not assigned."); return; }
        var def = MonsterSelectionUI.Instance?.Selected;
        if (def == null) { Debug.LogError("[BuildController] No monster selected."); return; }
        if (!DungeonCore.Instance.TrySpendCapacity(def.CapacityCost)) { Debug.Log($"[BuildController] Not enough capacity."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) spawner.transform.SetParent(ActiveFloor.transform, true);
        spawner.Initialise(def);
        SetMode(BuildMode.Claim);
    }

    // ── Chest ─────────────────────────────────────────────────────

    private void HandleChestPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell)) { Debug.Log("[BuildController] Chest must be on owned tile."); return; }
        if (selectedChest == null || selectedChest.prefab == null) { Debug.LogWarning("[BuildController] No chest selected."); return; }
        if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var chest = Instantiate(selectedChest.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) chest.transform.SetParent(ActiveFloor.transform, true);
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
        if (ActiveFloor != null) piece.transform.SetParent(ActiveFloor.transform, true);
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
        if (ActiveFloor != null) anchor.transform.SetParent(ActiveFloor.transform, true);
        anchor.Initialise(cell);
        SetMode(BuildMode.Claim);
    }

    // ── Trap ──────────────────────────────────────────────────────

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell)) { Debug.Log("[BuildController] Trap must be on owned tile."); return; }
        if (selectedTrap == null || selectedTrap.prefab == null) { Debug.LogWarning("[BuildController] No trap selected."); return; }
        if (ActiveTrapRegistry != null && ActiveTrapRegistry.GetTrapAt(cell) != null) { Debug.Log("[BuildController] Trap already here."); return; }
        if (!DungeonCore.Instance.SpendMana(selectedTrap.manaCost)) { Debug.Log("[BuildController] Not enough mana."); return; }

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var trap = Instantiate(selectedTrap.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) trap.transform.SetParent(ActiveFloor.transform, true);
        trap.Initialise(selectedTrap, cell);

        if (trap is WarningTrap warning)
            WarningTrapNameDialog.Instance?.Open(warning);

        SetMode(BuildMode.Claim);
    }

    // ── Stairs ────────────────────────────────────────────────────

    // REPLACE the existing HandleStairsPlacement() in DungeonBuildController.cs
    // with this version. No other methods in DungeonBuildController need to change.

    private void HandleStairsPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;

        if (FloorManager.Instance == null) { Debug.LogError("[BuildController] FloorManager not found."); return; }

        // Gate 1: core relocation pending.
        if (FloorManager.Instance.IsCoreRelocationPending)
        {
            Debug.Log("[BuildController] Cannot place stairs — relocate the core first.");
            SetMode(BuildMode.Claim);
            return;
        }

        // Gate 2: already at max floor depth.
        if (FloorManager.Instance.ActiveFloorIndex >= FloorManager.Instance.MaxAllowedFloorIndex)
        {
            Debug.Log("[BuildController] Cannot place stairs — this is the deepest floor.");
            SetMode(BuildMode.Claim);
            return;
        }

        // Gate 3: this floor already has a Down stair.
        if (FloorManager.Instance.FloorHasDownStair(FloorManager.Instance.ActiveFloorIndex))
        {
            Debug.Log("[BuildController] Cannot place stairs — this floor already has a Down stair.");
            SetMode(BuildMode.Claim);
            return;
        }

        // Gate 4: need a stair credit (granted by tier-up).
        if (DungeonCore.Instance == null || DungeonCore.Instance.StairCredits <= 0)
        {
            Debug.Log("[BuildController] Cannot place stairs — no stair credit available (tier up first).");
            SetMode(BuildMode.Claim);
            return;
        }

        if (ActiveInfluence == null || !ActiveInfluence.IsTileOwned(cell))
        {
            Debug.Log("[BuildController] Stairs must be on owned tile.");
            return;
        }
        if (stairsDefinition == null || stairsDefinition.prefab == null) { Debug.LogError("[BuildController] stairsDefinition not assigned."); return; }
        if (!DungeonCore.Instance.SpendMana(stairsDefinition.manaCost))
        {
            Debug.Log("[BuildController] Not enough mana for stairs.");
            return;
        }

        // All gates passed — consume the credit and place.
        if (!DungeonCore.Instance.TryConsumeStairCredit())
        {
            // Shouldn't happen given the gate check above, but defensive.
            Debug.LogWarning("[BuildController] Stair credit vanished between check and consume.");
            return;
        }

        int currentFloorIndex = FloorManager.Instance.ActiveFloorIndex;
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);

        var downStairs = Instantiate(stairsDefinition.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) downStairs.transform.SetParent(ActiveFloor.transform, true);
        downStairs.Initialise(cell, currentFloorIndex, DungeonStairs.Direction.Down, stairsDefinition.upVariantSprite);

        int nextFloorIndex = currentFloorIndex + 1;
        FloorManager.Instance.EnsureFloorExists(nextFloorIndex, cell);

        var nextFloor = FloorManager.Instance.GetFloor(nextFloorIndex);
        if (nextFloor?.TileInfluence != null)
        {
            Vector3 upPos = nextFloor.TileInfluence.CellToWorld(cell);
            var upStairs = Instantiate(stairsDefinition.prefab, upPos, Quaternion.identity);
            upStairs.transform.SetParent(nextFloor.transform, true);
            upStairs.Initialise(cell, nextFloorIndex, DungeonStairs.Direction.Up, stairsDefinition.upVariantSprite);
        }

        SetMode(BuildMode.Claim);
        Debug.Log($"[BuildController] Stairs placed: floor {currentFloorIndex} ↔ {nextFloorIndex}. Credits remaining: {DungeonCore.Instance.StairCredits}.");
    }

    // ── Place Core ────────────────────────────────────────────────

    private void HandlePlaceCoreMode()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileOwned(cell)) { Debug.Log("[BuildController] Core must be on owned tile."); return; }
        if (FloorManager.Instance == null || !FloorManager.Instance.CanPlaceCore)
        {
            Debug.Log("[BuildController] Place Core not available.");
            SetMode(BuildMode.Claim);
            return;
        }

        int destIdx = FloorManager.Instance.PendingCoreRelocationFloor;
        var destFloor = FloorManager.Instance.GetFloor(destIdx);
        if (destFloor == null) { Debug.LogError($"[BuildController] Pending floor {destIdx} not found."); SetMode(BuildMode.Claim); return; }

        if (FloorManager.Instance.ActiveFloorIndex != destIdx)
        {
            FloorManager.Instance.SwitchToFloor(destIdx);
            return;
        }

        DungeonCore.Instance.Relocate(destFloor, cell);
        SetMode(BuildMode.Claim);
    }

    // ── Restore (Save/Load) ───────────────────────────────────────
    // All restore methods now take an explicit FloorRoot. The legacy single-arg
    // signatures still exist as compatibility shims that route through ActiveFloor.

    public void RestoreEntrance(Vector3Int cell)
    {
        // Entrance is Floor 0 only.
        var floor = FloorManager.Instance?.GetFloor(0);
        RestoreEntrance(floor, cell);
    }

    public void RestoreEntrance(FloorRoot floor, Vector3Int cell)
    {
        if (entrancePrefab == null) { Debug.LogError("[BuildController] entrancePrefab not assigned."); return; }
        if (DungeonEntrance.Instance != null) Destroy(DungeonEntrance.Instance.gameObject);
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        entrance.transform.SetParent(floor.transform, true);
        entrance.Initialise(cell);
    }

    public void RestoreSpawner(FloorRoot floor, MonsterDefinition def, Vector3Int cell)
    {
        if (spawnerShellPrefab == null) { Debug.LogError("[BuildController] spawnerShellPrefab not assigned."); return; }
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.transform.SetParent(floor.transform, true);
        spawner.Initialise(def);
    }

    public void RestoreChest(FloorRoot floor, ChestDefinition def, Vector3Int cell, bool isOpened)
    {
        if (def == null || def.prefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var chest = Instantiate(def.prefab, worldPos, Quaternion.identity);
        chest.transform.SetParent(floor.transform, true);
        chest.Initialise(def);
        if (isOpened) chest.SetOpened(true);
    }

    public void RestoreFurniture(FloorRoot floor, FurnitureDefinition def, Vector3Int cell)
    {
        if (def?.prefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var piece = Instantiate(def.prefab, worldPos, Quaternion.identity);
        piece.transform.SetParent(floor.transform, true);
        piece.Initialise(def, cell);
    }

    public void RestoreRoomAnchor(FloorRoot floor, Vector3Int cell, string roomName,
                                  FurnitureDefinitionRegistry furnitureRegistry,
                                  RoomDefinitionRegistry roomDefRegistry)
    {
        if (roomAnchorPrefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        anchor.transform.SetParent(floor.transform, true);
        anchor.Initialise(cell);
        if (!string.IsNullOrEmpty(roomName))
        {
            var def = roomDefRegistry?.GetByName(roomName);
            if (def != null) anchor.SetRoomType(def);
        }
    }

    public void RestoreTrap(FloorRoot floor, TrapDefinition def, Vector3Int cell, bool isFlagged,
                            string warningLabel = "", bool hasLink = false,
                            Vector3Int linkedCell = default)
    {
        if (def == null || def.prefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var trap = Instantiate(def.prefab, worldPos, Quaternion.identity);
        trap.transform.SetParent(floor.transform, true);
        trap.Initialise(def, cell);
        if (trap is WarningTrap warning && !string.IsNullOrEmpty(warningLabel))
            warning.SetWarningLabel(warningLabel);
        if (trap is PressurePlateTrap plate && hasLink)
            plate.SetLink(linkedCell);
        if (isFlagged) trap.Flag();
    }

    public void RestoreStairs(FloorRoot floor, Vector3Int cell, DungeonStairs.Direction dir)
    {
        if (stairsDefinition == null || stairsDefinition.prefab == null) { Debug.LogError("[BuildController] stairsDefinition not assigned."); return; }
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var stairs = Instantiate(stairsDefinition.prefab, worldPos, Quaternion.identity);
        stairs.transform.SetParent(floor.transform, true);
        stairs.Initialise(cell, floor.FloorIndex, dir, stairsDefinition.upVariantSprite);
    }

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

        var allStairs = FindObjectsByType<DungeonStairs>(FindObjectsInactive.Include);
        foreach (var stair in allStairs)
        {
            var col = stair.GetComponent<Collider2D>();
            if (col == null) continue;
            if (FloorManager.Instance != null && stair.FloorIndex != FloorManager.Instance.ActiveFloorIndex) continue;
            if (col.OverlapPoint(worldPos))
            {
                FloorManager.Instance?.SwitchToFloor(stair.LinkedFloorIndex);
                return true;
            }
        }
        return false;
    }

    private bool LeftClickThisFrame(out Vector3Int cell)
    {
        cell = default;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

        var influence = ActiveInfluence;
        if (influence == null) return false;
        cell = influence.WorldToCell(worldPos);
        return true;
    }

    /// <summary>
    /// Drag-aware claim input. Returns true on the initial click AND on every
    /// frame the mouse moves to a new cell while left-button is held. Used only
    /// by Claim mode — placement modes still use single-click LeftClickThisFrame.
    /// </summary>
    private bool ClaimInputThisFrame(out Vector3Int cell)
    {
        cell = default;
        var mouse = Mouse.current;
        if (mouse == null) return false;

        // Reset on button release so the next press starts a new drag.
        if (mouse.leftButton.wasReleasedThisFrame)
            dragClaimActive = false;

        bool pressed = mouse.leftButton.wasPressedThisFrame;
        bool held = mouse.leftButton.isPressed;
        if (!pressed && !held) return false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        if (mainCamera == null) return false;

        var influence = ActiveInfluence;
        if (influence == null) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        Vector3Int newCell = influence.WorldToCell(worldPos);

        // Initial press: always counts.
        if (pressed)
        {
            cell = newCell;
            dragClaimLastCell = newCell;
            dragClaimActive = true;
            return true;
        }

        // Hold-drag: only fire when the mouse has moved to a different cell.
        if (held && dragClaimActive && newCell != dragClaimLastCell)
        {
            cell = newCell;
            dragClaimLastCell = newCell;
            return true;
        }

        return false;
    }

    private void RevalidateAllAnchors()
    {
        var anchors = FindObjectsByType<RoomAnchor>(FindObjectsInactive.Exclude);
        foreach (var a in anchors) a.Revalidate();
    }
}