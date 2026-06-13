using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum BuildMode
{
    Claim,
    Mine,
    PlaceEntrance,
    PlaceSpawner,
    PlaceChest,
    PlaceFurniture,
    PlaceRoomAnchor,
    PlaceTrap,
    PlaceStairs,
    PlaceCore,
    PlaceMonsterPatrol,        // DAY 31 PART 3D
    PlaceMonsterAttackTarget,  // DAY 31 PART 3D / 3E
}

public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    public void SetSelectedFurniture(FurnitureDefinition def) => selectedFurniture = def;
    public void SetSelectedTrap(TrapDefinition def) => selectedTrap = def;
    public void SetSelectedChest(ChestDefinition def) => selectedChest = def;

    [Header("Mana Costs")]
    [SerializeField] private float influenceClaimManaCost = 1f;
    [FormerlySerializedAs("claimManaCost")]
    [SerializeField] private float mineManaCost = 5f;

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
    private Vector3Int dragClaimLastCell;
    private bool dragClaimActive;
    private bool dragMineActive;

    // DAY 31 PART 3D — Spawner being edited during patrol/attack placement.
    private MonsterSpawner placementSpawner;

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
        // DAY 31 — Master pause-gate removed. Specific actions choose whether to honor
        // pause (active-pause pattern). Navigation, spawner selection, and command-UI
        // placement modes run during pause; gameplay-changing build modes do not.

        if (TryHandleStairClick()) return;

        if (CurrentMode == BuildMode.Claim)
        {
            if (TryHandleSpawnerOrMonsterClick()) return;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                && SpawnerSelectionController.Instance != null
                && SpawnerSelectionController.Instance.CurrentSelected != null)
            {
                SpawnerSelectionController.Instance.Deselect();
            }
        }

        if (CurrentMode == BuildMode.PlaceMonsterPatrol)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { CommitPatrolPlacement(); return; }
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                placementSpawner?.RemoveLastPatrolWaypoint();
                return;
            }
            HandlePatrolPlacement();
            return;
        }
        if (CurrentMode == BuildMode.PlaceMonsterAttackTarget)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { CancelAttackTargetPlacement(); return; }
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelAttackTargetPlacement();
                return;
            }
            HandleAttackTargetPlacement();
            return;
        }

        // Everything below is gameplay-changing and respects pause.
        if (PauseController.IsGamePaused) return;

        switch (CurrentMode)
        {
            case BuildMode.Claim: HandleClaimClick(); break;
            case BuildMode.Mine: HandleMineClick(); break;
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
        if (FloorManager.Instance == null) return;
        if (!FloorManager.Instance.CanPlaceCore) return;
        if (DungeonCore.Instance != null && DungeonCore.Instance.IsInTransit) return;

        int destIdx = FloorManager.Instance.PendingCoreRelocationFloor;
        FloorManager.Instance.SwitchToFloor(destIdx);
        SetMode(BuildMode.PlaceCore);
    }

    // ── DAY 31 PART 3D — Patrol / Attack Placement Entry ──────────

    public void BeginPatrolPlacement(MonsterSpawner spawner)
    {
        if (spawner == null) return;
        placementSpawner = spawner;
        SetMode(BuildMode.PlaceMonsterPatrol);
    }

    public void BeginAttackTargetPlacement(MonsterSpawner spawner)
    {
        if (spawner == null) return;
        placementSpawner = spawner;
        SetMode(BuildMode.PlaceMonsterAttackTarget);
    }

    private void CommitPatrolPlacement()
    {
        placementSpawner = null;
        SetMode(BuildMode.Claim);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    private void CancelAttackTargetPlacement()
    {
        placementSpawner = null;
        SetMode(BuildMode.Claim);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    private static T FindObjectByType<T>() where T : UnityEngine.Object
    {
        return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private TileInfluenceManager ActiveInfluence => FloorManager.Instance?.ActiveFloor?.TileInfluence;
    private TrapRegistry ActiveTrapRegistry => FloorManager.Instance?.ActiveFloor?.TrapRegistry;
    private FloorRoot ActiveFloor => FloorManager.Instance?.ActiveFloor;

    private bool IsCellValidForWaypoint(Vector3Int cell)
    {
        var influence = ActiveInfluence;
        if (influence == null) return false;
        if (influence.IsTileMined(cell)) return true;

        var features = ActiveFloor?.FeatureGenerator;
        if (features == null) return false;

        // Revealed chambers are valid (chamber assault use case for Attack-Here).
        var ftype = features.GetFeatureAt(cell);
        if (ftype == FeatureType.Chamber)
        {
            int chamberId = features.GetChamberId(cell);
            if (chamberId >= 0 && features.IsChamberRevealed(chamberId)) return true;
        }
        // Rivers excluded — fording mid-route is awkward.
        return false;
    }

    // ── Claim ─────────────────────────────────────────────────────

    private void HandleClaimClick()
    {
        if (!ClaimInputThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;

        // PHASE 5 — Strict claim-only. Mining moved to HandleMineClick (Mine mode).
        // Click on anything other than a claimable cell is silently ignored.
        if (!ActiveInfluence.IsTileClaimable(cell)) return;

        var features = ActiveFloor?.FeatureGenerator;
        if (features != null && features.IsCellInUnclearedChamber(cell))
        {
            Debug.Log("[BuildController] Cannot claim cavern — clear wild monsters first.");
            dragClaimActive = false;
            return;
        }

        float cost = influenceClaimManaCost;

        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(cost))
        {
            dragClaimActive = false;
            return;
        }
        ActiveInfluence.ClaimTile(cell);
    }

    private void HandleMineClick()
    {
        if (!MineInputThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;

        // Strict mine-only. Only claimed-not-mined cells with valid adjacency proceed.
        if (ActiveInfluence.IsTileMined(cell)) return;
        if (!ActiveInfluence.IsTileClaimed(cell)) return;

        if (!CanMineCell(cell))
        {
            Debug.Log("[BuildController] Cannot mine here — must be adjacent to existing mined area.");
            return;
        }

        float multiplier = ActiveFloor != null ? ActiveFloor.GetClaimCostMultiplier(cell) : 1f;
        float cost = mineManaCost * multiplier;

        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(cost))
        {
            dragMineActive = false;
            return;
        }
        ActiveInfluence.MineTile(cell);
    }

    /// <summary>
    /// PHASE 2 — Mirror of TileInfluenceManager.MineTile's adjacency check, used by
    /// HandleClaimClick to decide whether the click would succeed before charging
    /// mana. Keeping the logic mirrored here means we don't pay (spend mana) and
    /// then fail silently.
    /// </summary>
    private bool CanMineCell(Vector3Int cell)
    {
        var influence = ActiveInfluence;
        if (influence == null) return false;
        if (!influence.IsTileClaimed(cell)) return false;
        if (influence.IsTileMined(cell)) return false;

        // Core cell bypass — first mine has no neighbors.
        var terrain = ActiveFloor?.Terrain;
        if (terrain != null && cell == terrain.CoreCell) return true;

        var dirs = new[] { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        foreach (var d in dirs)
        {
            if (influence.IsTileMined(cell + d)) return true;
        }
        return false;
    }

    // ── Patrol placement (DAY 31 PART 3D) ─────────────────────────

    private void HandlePatrolPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (placementSpawner == null) { SetMode(BuildMode.Claim); return; }
        if (!IsCellValidForWaypoint(cell))
        {
            Debug.Log("[BuildController] Waypoint cell must be owned or in a revealed chamber.");
            return;
        }
        if (!placementSpawner.AddPatrolWaypoint(cell))
        {
            Debug.Log($"[BuildController] Cannot add waypoint (max {MonsterSpawner.MaxPatrolWaypoints} reached, or duplicate).");
        }
    }

    // ── Attack target placement (DAY 31 PART 3D / 3E) ─────────────

    private void HandleAttackTargetPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (placementSpawner == null) { SetMode(BuildMode.Claim); return; }
        if (!IsCellValidForWaypoint(cell))
        {
            Debug.Log("[BuildController] Attack target must be owned or in a revealed chamber.");
            return;
        }
        placementSpawner.SetAttackTarget(cell);
        placementSpawner = null;
        SetMode(BuildMode.Claim);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    // ── Entrance ──────────────────────────────────────────────────

    private void HandleEntrancePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileMined(cell)) return;
        if (entrancePrefab == null) return;

        if (DungeonEntrance.Instance != null) Destroy(DungeonEntrance.Instance.gameObject);
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) entrance.transform.SetParent(ActiveFloor.transform, true);
        entrance.Initialise(cell);
        SetMode(BuildMode.Claim);
    }

    private void HandleSpawnerPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        PlaceSpawner(cell);
    }

    private void PlaceSpawner(Vector3Int cell)
    {
        if (spawnerShellPrefab == null) return;
        var def = MonsterSelectionUI.Instance?.Selected;
        if (def == null) return;
        if (!DungeonCore.Instance.TrySpendCapacity(def.CapacityCost)) return;

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) spawner.transform.SetParent(ActiveFloor.transform, true);
        spawner.Initialise(def);
        SetMode(BuildMode.Claim);
    }

    private void HandleChestPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (selectedChest == null || selectedChest.prefab == null) return;
        if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost)) return;
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var chest = Instantiate(selectedChest.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) chest.transform.SetParent(ActiveFloor.transform, true);
        chest.Initialise(selectedChest);
        SetMode(BuildMode.Claim);
    }

    private void HandleFurniturePlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (selectedFurniture == null) return;
        if (selectedFurniture.blocksPathfinding && RoomValidator.WouldBlockDungeon(cell)) return;
        if (!DungeonCore.Instance.SpendMana(selectedFurniture.manaCost)) return;
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var piece = Instantiate(selectedFurniture.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) piece.transform.SetParent(ActiveFloor.transform, true);
        piece.Initialise(selectedFurniture, cell);
        RevalidateAllAnchors();
        SetMode(BuildMode.Claim);
    }

    private void HandleRoomAnchorPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (roomAnchorPrefab == null) return;
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) anchor.transform.SetParent(ActiveFloor.transform, true);
        anchor.Initialise(cell);
        SetMode(BuildMode.Claim);
    }

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (selectedTrap == null || selectedTrap.prefab == null) return;
        if (ActiveTrapRegistry != null && ActiveTrapRegistry.GetTrapAt(cell) != null) return;
        if (!DungeonCore.Instance.SpendMana(selectedTrap.manaCost)) return;

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var trap = Instantiate(selectedTrap.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) trap.transform.SetParent(ActiveFloor.transform, true);
        trap.Initialise(selectedTrap, cell);
        if (trap is WarningTrap warning) WarningTrapNameDialog.Instance?.Open(warning);
        SetMode(BuildMode.Claim);
    }

    private void HandleStairsPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (FloorManager.Instance == null) return;
        if (FloorManager.Instance.IsCoreRelocationPending) { SetMode(BuildMode.Claim); return; }
        if (FloorManager.Instance.ActiveFloorIndex >= FloorManager.Instance.MaxAllowedFloorIndex) { SetMode(BuildMode.Claim); return; }
        if (FloorManager.Instance.FloorHasDownStair(FloorManager.Instance.ActiveFloorIndex)) { SetMode(BuildMode.Claim); return; }
        if (DungeonCore.Instance == null || DungeonCore.Instance.StairCredits <= 0) { SetMode(BuildMode.Claim); return; }
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (stairsDefinition == null || stairsDefinition.prefab == null) return;
        if (!DungeonCore.Instance.SpendMana(stairsDefinition.manaCost)) return;
        if (!DungeonCore.Instance.TryConsumeStairCredit()) return;

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
    }

    private void HandlePlaceCoreMode()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileMined(cell)) return;
        if (FloorManager.Instance == null || !FloorManager.Instance.CanPlaceCore) { SetMode(BuildMode.Claim); return; }

        int destIdx = FloorManager.Instance.PendingCoreRelocationFloor;
        var destFloor = FloorManager.Instance.GetFloor(destIdx);
        if (destFloor == null) { SetMode(BuildMode.Claim); return; }
        if (FloorManager.Instance.ActiveFloorIndex != destIdx) { FloorManager.Instance.SwitchToFloor(destIdx); return; }

        DungeonCore.Instance.Relocate(destFloor, cell);
        SetMode(BuildMode.Claim);
    }

    // ── Restore (Save/Load) ───────────────────────────────────────

    public void RestoreEntrance(Vector3Int cell)
    {
        var floor = FloorManager.Instance?.GetFloor(0);
        RestoreEntrance(floor, cell);
    }

    public void RestoreEntrance(FloorRoot floor, Vector3Int cell)
    {
        if (entrancePrefab == null) return;
        if (DungeonEntrance.Instance != null) Destroy(DungeonEntrance.Instance.gameObject);
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var entrance = Instantiate(entrancePrefab, worldPos, Quaternion.identity);
        entrance.transform.SetParent(floor.transform, true);
        entrance.Initialise(cell);
    }

    public MonsterSpawner RestoreSpawner(FloorRoot floor, MonsterDefinition def, Vector3Int cell)
        => RestoreSpawner(floor, def, cell, SpawnerOrderMode.Wander, null, true, false, default, true);

    /// <summary>DAY 31 PART 3D — Full restore including patrol orders and attack target.
    /// PART 3 CLOSE-OUT — allowDefendCore added as a final parameter.</summary>
    public MonsterSpawner RestoreSpawner(FloorRoot floor, MonsterDefinition def, Vector3Int cell,
        SpawnerOrderMode orderMode, List<Vector3Int> patrolWaypoints, bool patrolLoop,
        bool hasAttackTarget, Vector3Int attackTargetCell, bool allowDefendCore)
    {
        if (spawnerShellPrefab == null) return null;
        if (floor?.TileInfluence == null) return null;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.transform.SetParent(floor.transform, true);
        spawner.Initialise(def);
        spawner.RestoreOrders(orderMode, patrolWaypoints, patrolLoop, hasAttackTarget, attackTargetCell, allowDefendCore);
        return spawner;
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
            var defRes = roomDefRegistry?.GetByName(roomName);
            if (defRes != null) anchor.SetRoomType(defRes);
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
        if (stairsDefinition == null || stairsDefinition.prefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var stairs = Instantiate(stairsDefinition.prefab, worldPos, Quaternion.identity);
        stairs.transform.SetParent(floor.transform, true);
        stairs.Initialise(cell, floor.FloorIndex, dir, stairsDefinition.upVariantSprite);
    }

    // ── Clicks ────────────────────────────────────────────────────

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

    /// DAY 31 PART 3 CLOSE-OUT — Renamed from TryHandleSpawnerClick because it
    /// now also routes monster clicks through to their owning spawner. Either
    /// surface yields the same selection target (the MonsterSpawner).
    /// Monster route is checked first so an overlapping monster wins over the
    /// spawner cell beneath it; spawner route still works for dead/respawning
    /// monsters where no monster collider is present.
    /// </summary>
    private bool TryHandleSpawnerOrMonsterClick()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        if (mainCamera == null) return false;

        Vector2 screenPos = mouse.position.ReadValue();
        Vector3 worldPos = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
        worldPos.z = 0f;

        var hits = Physics2D.OverlapPointAll(worldPos);

        // Pass 1 — monster route. Prefer this because the monster is what the
        // player can actually see; the spawner cell is often hidden beneath it.
        foreach (var col in hits)
        {
            if (col == null) continue;
            var monster = col.GetComponentInParent<DungeonMonster>();
            if (monster == null) continue;
            if (monster.Spawner == null) continue;  // wild monsters have no spawner

            var monsterFloor = monster.CurrentFloor;
            if (FloorManager.Instance != null && monsterFloor != FloorManager.Instance.ActiveFloor) continue;

            SpawnerSelectionController.Instance?.Select(monster.Spawner);
            return true;
        }

        // Pass 2 — spawner route. Catches the dead/respawning case.
        foreach (var col in hits)
        {
            if (col == null) continue;
            var spawner = col.GetComponentInParent<MonsterSpawner>();
            if (spawner == null) continue;

            var spawnerFloor = spawner.GetComponentInParent<FloorRoot>();
            if (FloorManager.Instance != null && spawnerFloor != FloorManager.Instance.ActiveFloor) continue;

            SpawnerSelectionController.Instance?.Select(spawner);
            return true;
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

    private bool ClaimInputThisFrame(out Vector3Int cell)
    {
        cell = default;
        var mouse = Mouse.current;
        if (mouse == null) return false;
        if (mouse.leftButton.wasReleasedThisFrame) dragClaimActive = false;

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

        if (pressed) { cell = newCell; dragClaimLastCell = newCell; dragClaimActive = true; return true; }
        if (held && dragClaimActive && newCell != dragClaimLastCell) { cell = newCell; dragClaimLastCell = newCell; return true; }
        return false;
    }

    private bool MineInputThisFrame(out Vector3Int cell)
    {
        cell = default;

        if (Mouse.current == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;

        bool pressed = Mouse.current.leftButton.isPressed;
        if (!pressed) { dragMineActive = false; return false; }

        // Worldspace click → cell. Mirrors what ClaimInputThisFrame does.
        if (ActiveInfluence == null || ActiveFloor == null) return false;
        Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mouseWorld.z = 0f;
        cell = ActiveInfluence.WorldToCell(mouseWorld);

        bool firstPress = Mouse.current.leftButton.wasPressedThisFrame;
        if (firstPress || dragMineActive)
        {
            dragMineActive = true;
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