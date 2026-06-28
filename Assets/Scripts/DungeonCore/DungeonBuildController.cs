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

    // Feature 3 — mine-target highlight (runtime overlay; no scene setup required).
    [Header("Mine Highlight")]
    [SerializeField] private Color mineHighlightColor = new Color(1f, 0.85f, 0.35f, 0.35f);
    private GameObject mineHighlightGO;
    private SpriteRenderer mineHighlightSR;

    // ── Auto-dig queue (runtime overlay; no scene setup) ──────────
    [Header("Dig Queue")]
    [SerializeField] private float digTicksPerSecond = 10f;
    [SerializeField] private Color digQueueColor = new Color(0.35f, 0.7f, 1f, 0.28f);
    private readonly List<(int floor, Vector3Int cell)> digQueue = new();
    private readonly HashSet<(int floor, Vector3Int cell)> digQueued = new();
    private float digTickTimer;
    private bool digOverlayDirty;
    private int lastOverlayFloor = int.MinValue;
    private readonly List<SpriteRenderer> digOverlayPool = new();
    private Sprite digOverlaySprite;
    private Transform digOverlayParent;

    // Mine-gesture state (click vs drag).
    private Vector3Int minePressCell;
    private Vector3Int mineLastCell;
    private bool mineTracking;
    private bool mineIsDrag;
    private bool mineShiftAtPress;

    private System.Collections.Generic.List<DungeonStairs> _stairClickBuf;

    // DAY 31 PART 3D — Spawner being edited during patrol/attack placement.
    private MonsterSpawner placementSpawner;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildMineHighlight();
        BuildDigOverlayAssets();
    }

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        UpdateMineHighlight();
        UpdateDigQueueOverlay();
        if (!PauseController.IsGamePaused) ProcessDigQueue();

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
                && SpawnerSelectionController.Instance.CurrentSelected != null
                && !ShiftHeld())
            {
                SpawnerSelectionController.Instance.Deselect();
            }
        }

        if (CurrentMode == BuildMode.PlaceMonsterPatrol)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                redesignateTarget = null; roomTracking = false;
                ClearRoomPreview();
                SetMode(BuildMode.Claim);
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
            case BuildMode.Mine: HandleMineInput(); break;
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
        dragClaimActive = false;
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

    /// <summary>Phase 3 closeout (#6) - feedback for a rejected build action at a cell.</summary>
    private void RejectAt(Vector3Int cell, string reason)
    {
        Vector3 world;
        var inf = ActiveInfluence;
        if (inf != null) world = inf.CellToWorld(cell);
        else if (mainCamera != null && Mouse.current != null)
        {
            world = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            world.z = 0f;
        }
        else world = Vector3.zero;
        BuildFeedback.Reject(world, reason);
    }

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
            RejectAt(cell, "Clear the wild monsters first");
            dragClaimActive = false;
            return;
        }

        float cost = influenceClaimManaCost;

        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(cost))
        {
            RejectAt(cell, "Not enough mana");
            dragClaimActive = false;
            return;
        }
        ActiveInfluence.ClaimTile(cell);
    }

    // ── Mine input: click mines one now · drag queues a swath · Shift+click queues one ──
    private void HandleMineInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Right-click clears the whole dig queue.
        if (mouse.rightButton.wasPressedThisFrame) { ClearDigQueue(); return; }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Press: start tracking; defer the mine-vs-queue decision until release/first move.
        if (mouse.leftButton.wasPressedThisFrame)
        {
            mineTracking = false;
            mineIsDrag = false;
            if (!overUI && HoverCell(out Vector3Int c))
            {
                mineTracking = true;
                minePressCell = c;
                mineLastCell = c;
                mineShiftAtPress = IsShiftHeld();
            }
            return;
        }

        // Held + entered a new cell → it's a drag: queue the press cell, then each new cell.
        if (mineTracking && mouse.leftButton.isPressed)
        {
            if (HoverCell(out Vector3Int c) && c != mineLastCell)
            {
                if (!mineIsDrag) { mineIsDrag = true; EnqueueDig(minePressCell); }
                EnqueueDig(c);
                mineLastCell = c;
            }
            return;
        }

        // Release with no movement → a click: Shift queues one, otherwise mine one now.
        if (mineTracking && mouse.leftButton.wasReleasedThisFrame)
        {
            if (!mineIsDrag)
            {
                if (mineShiftAtPress) EnqueueDig(minePressCell);
                else MineImmediate(minePressCell);
            }
            mineTracking = false;
            mineIsDrag = false;
        }
    }

    // Immediate single-tile mine (the old click behavior), used for a plain click.
    private void MineImmediate(Vector3Int rawCell)
    {
        var inf = ActiveInfluence;
        if (inf == null) return;
        if (!ResolveMineTarget(rawCell, out Vector3Int cell)) return;
        if (inf.IsTileMined(cell)) return;
        if (!inf.IsTileClaimed(cell)) return;
        if (!CanMineCell(cell))
        {
            RejectAt(cell, "Must be next to a mined tile");
            return;
        }
        float cost = mineManaCost * (ActiveFloor != null ? ActiveFloor.GetClaimCostMultiplier(cell) : 1f);
        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(cost))
        {
            RejectAt(cell, "Not enough mana");
            return;
        }
        inf.MineTile(cell);
    }

    // Adds a hovered cell's wall target to the queue (claimed + unmined; adjacency
    // is checked later, at dig time, so interior cells can be queued ahead).
    private void EnqueueDig(Vector3Int rawCell)
    {
        var inf = ActiveInfluence;
        if (inf == null || FloorManager.Instance == null) return;
        if (!ResolveMineTarget(rawCell, out Vector3Int cell)) return;
        if (inf.IsTileMined(cell) || !inf.IsTileClaimed(cell)) return;
        var key = (FloorManager.Instance.ActiveFloorIndex, cell);
        if (!digQueued.Add(key)) return;
        digQueue.Add(key);
        digOverlayDirty = true;
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

    // ── Feature 3: cap/face click remap + mine-target highlight ───

    /// <summary>
    /// Resolves a clicked/hovered cell to the wall it represents: the cell itself if it's
    /// a claimed, unmined wall (cap click); otherwise the wall whose south face is drawn
    /// over it — one cell north (upper face) or two north (lower face). False if the cell
    /// maps to no claimed, unmined wall.
    /// </summary>
    private bool ResolveMineTarget(Vector3Int c, out Vector3Int target)
    {
        target = c;
        var inf = ActiveInfluence;
        if (inf == null) return false;

        // Cap click: the cell itself is a claimed, unmined wall.
        if (inf.IsTileClaimed(c) && !inf.IsTileMined(c)) { target = c; return true; }

        // Face click: an open (mined) cell a wall's south face is draped over.
        if (inf.IsTileMined(c))
        {
            Vector3Int up1 = c + Vector3Int.up;          // wall whose UPPER face sits at c
            if (inf.IsTileClaimed(up1) && !inf.IsTileMined(up1)) { target = up1; return true; }

            Vector3Int up2 = up1 + Vector3Int.up;        // wall whose LOWER face sits at c
            if (inf.IsTileMined(up1) && inf.IsTileClaimed(up2) && !inf.IsTileMined(up2)) { target = up2; return true; }
        }
        return false;
    }

    private bool HoverCell(out Vector3Int cell)
    {
        cell = default;
        var inf = ActiveInfluence;
        if (Mouse.current == null || inf == null || mainCamera == null) return false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return false;
        Vector3 w = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        w.z = 0f;
        cell = inf.WorldToCell(w);
        return true;
    }

    private void BuildMineHighlight()
    {
        mineHighlightGO = new GameObject("MineTargetHighlight");
        mineHighlightSR = mineHighlightGO.AddComponent<SpriteRenderer>();
        var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        mineHighlightSR.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        mineHighlightSR.color = mineHighlightColor;
        mineHighlightSR.sortingLayerName = "AdjacentHighlight";
        mineHighlightSR.sortingOrder = 100;
        mineHighlightSR.enabled = false;
    }

    /// <summary>
    /// Each frame: a translucent fill over the wall the cursor would mine (cap plus any
    /// visible south-face cells), shown only in Mine mode over a valid mineable target.
    /// </summary>
    private void UpdateMineHighlight()
    {
        if (mineHighlightSR == null) return;
        if (CurrentMode != BuildMode.Mine || PauseController.IsGamePaused)
        {
            mineHighlightSR.enabled = false;
            return;
        }

        var inf = ActiveInfluence;
        if (inf == null
            || !HoverCell(out Vector3Int hover)
            || !ResolveMineTarget(hover, out Vector3Int target)
            || !CanMineCell(target))
        {
            mineHighlightSR.enabled = false;
            return;
        }

        // Footprint height: cap, plus visible south-face cells (each present only when the
        // cell below it is open/mined).
        Vector3Int s = Vector3Int.down;
        int n = 1;
        if (inf.IsTileMined(target + s)) { n = 2; if (inf.IsTileMined(target + s + s)) n = 3; }

        Vector3 topW = inf.CellToWorld(target);
        Vector3 botW = inf.CellToWorld(target + s * (n - 1));
        float cellH = Mathf.Abs(inf.CellToWorld(target + s).y - topW.y);
        float cellW = Mathf.Abs(inf.CellToWorld(target + Vector3Int.right).x - topW.x);

        mineHighlightGO.transform.position = new Vector3((topW.x + botW.x) * 0.5f, (topW.y + botW.y) * 0.5f, 0f);
        mineHighlightGO.transform.localScale = new Vector3(cellW, cellH * n, 1f);
        mineHighlightSR.color = mineHighlightColor;
        mineHighlightSR.enabled = true;
    }

    // ── Patrol placement (DAY 31 PART 3D) ─────────────────────────

    private void HandlePatrolPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (placementSpawner == null) { SetMode(BuildMode.Claim); return; }
        if (!IsCellValidForWaypoint(cell))
        {
            Debug.Log("[BuildController] Waypoint cell must be owned or in a revealed chamber.");
            RejectAt(cell, "Must be owned or a revealed chamber");
            return;
        }
        if (ActiveInfluence != null && ActiveInfluence.IsUnderOverhang(cell))
        {
            Debug.Log("[BuildController] Waypoint cell is under a wall overhang — not walkable.");
            RejectAt(cell, "Blocked by a wall overhang");
            return;
        }
        if (!placementSpawner.AddPatrolWaypoint(cell))
        {
            Debug.Log($"[BuildController] Cannot add waypoint (max {MonsterSpawner.MaxPatrolWaypoints} reached, or duplicate).");
            RejectAt(cell, "Max waypoints reached");
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
            RejectAt(cell, "Must be owned or a revealed chamber");
            return;
        }
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.Count > 0)
        {
            foreach (var s in sel.Selected)
                if (s != null) s.SetAttackTarget(cell);
        }
        else
        {
            placementSpawner.SetAttackTarget(cell);
        }
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

        var core = DungeonCore.Instance;
        if (core == null) return;

        // Phase 3 closeout - spawners cost BOTH capacity and mana (per the roadmap).
        // Pre-check both so we never spend one resource and then fail on the other.
        if (core.FreeCapacity < def.CapacityCost) { RejectAt(cell, "Monster capacity full"); return; }
        if (core.CurrentMana < def.ManaCost) { RejectAt(cell, "Not enough mana"); return; }

        core.TrySpendCapacity(def.CapacityCost);
        core.SpendMana(def.ManaCost);

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
        if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost)) { RejectAt(cell, "Not enough mana"); return; }
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
        if (selectedFurniture.blocksPathfinding && RoomValidator.WouldBlockDungeon(cell)) { RejectAt(cell, "Would block the dungeon path"); return; }
        if (!DungeonCore.Instance.SpendMana(selectedFurniture.manaCost)) { RejectAt(cell, "Not enough mana"); return; }
        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var piece = Instantiate(selectedFurniture.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) piece.transform.SetParent(ActiveFloor.transform, true);
        piece.Initialise(selectedFurniture, cell);
        RevalidateAllAnchors();
        SetMode(BuildMode.Claim);
    }

    private Vector3Int roomDragStart;
    private Vector3Int roomDragLast;
    private bool roomTracking;
    private RoomAnchor redesignateTarget;   // set by BeginRoomRedesignate; null = create new
    private System.Collections.Generic.List<RoomAnchor> _roomAnchorBuf;

    // Live drag-preview overlay (pooled quads, runtime-built — mirrors the dig overlay).
    private readonly System.Collections.Generic.List<SpriteRenderer> roomPreviewPool = new();
    private Transform roomPreviewParent;
    [SerializeField] private Color roomPreviewColor = new Color(0.83f, 0.65f, 0.15f, 0.35f);

    /// Called by RoomAnchor on right-click — re-drag an existing room's footprint.
    public void BeginRoomRedesignate(RoomAnchor anchor)
    {
        redesignateTarget = anchor;
        SetMode(BuildMode.PlaceRoomAnchor);
    }

    private void HandleRoomAnchorPlacement()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            redesignateTarget = null; roomTracking = false;
            SetMode(BuildMode.Claim);
            return;
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Press on mined ground → start the rectangle (this cell is also the anchor spot).
        if (mouse.leftButton.wasPressedThisFrame)
        {
            roomTracking = false;
            ClearRoomPreview();
            if (!overUI && HoverCell(out Vector3Int c)
                && ActiveInfluence != null && ActiveInfluence.IsTileMined(c))
            {
                roomTracking = true;
                roomDragStart = c;
                roomDragLast = c;
                PaintRoomPreview(roomDragStart, roomDragLast);
            }
            return;
        }

        // Held → track the opposite corner; repaint the preview only when it changes.
        if (roomTracking && mouse.leftButton.isPressed)
        {
            if (HoverCell(out Vector3Int c) && c != roomDragLast)
            {
                roomDragLast = c;
                PaintRoomPreview(roomDragStart, roomDragLast);
            }
            return;
        }

        // Release → build the footprint.
        if (roomTracking && mouse.leftButton.wasReleasedThisFrame)
        {
            roomTracking = false;
            ClearRoomPreview();
            CommitRoomFootprint(roomDragStart, roomDragLast);
            return;
        }
    }

    private void CommitRoomFootprint(Vector3Int a, Vector3Int b)
    {
        if (ActiveInfluence == null) { redesignateTarget = null; SetMode(BuildMode.Claim); return; }

        int minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        int minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);

        // Tiles already claimed by another room are skipped — overlap is blocked.
        var claimed = CollectOtherRoomFootprints(redesignateTarget);

        var cells = new System.Collections.Generic.List<Vector3Int>();
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var cell = new Vector3Int(x, y, a.z);
                if (ActiveInfluence.IsTileMined(cell) && !claimed.Contains(cell))
                    cells.Add(cell);
            }

        if (cells.Count == 0) { redesignateTarget = null; SetMode(BuildMode.Claim); return; }

        if (redesignateTarget != null)
        {
            redesignateTarget.SetFootprint(cells);   // keep its type, swap the footprint
            redesignateTarget = null;
            SetMode(BuildMode.Claim);
            return;
        }

        if (roomAnchorPrefab == null) { SetMode(BuildMode.Claim); return; }
        Vector3 worldPos = ActiveInfluence.CellToWorld(roomDragStart);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) anchor.transform.SetParent(ActiveFloor.transform, true);
        anchor.Initialise(roomDragStart);
        anchor.SetFootprint(cells);
        SetMode(BuildMode.Claim);
        RoomTypePickerUI.Instance?.Open(anchor);
    }

    private System.Collections.Generic.HashSet<Vector3Int> CollectOtherRoomFootprints(RoomAnchor exclude)
    {
        var set = new System.Collections.Generic.HashSet<Vector3Int>();
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return set;

        _roomAnchorBuf ??= new System.Collections.Generic.List<RoomAnchor>();
        floor.Entities.FillAll(_roomAnchorBuf);
        for (int i = 0; i < _roomAnchorBuf.Count; i++)
        {
            var anchor = _roomAnchorBuf[i];
            if (anchor == null || anchor == exclude) continue;
            var fp = anchor.Footprint;
            for (int j = 0; j < fp.Count; j++) set.Add(fp[j]);
        }
        return set;
    }

    private SpriteRenderer CreateRoomPreviewQuad()
    {
        var go = new GameObject("RoomPreviewCell");
        if (roomPreviewParent != null) go.transform.SetParent(roomPreviewParent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = digOverlaySprite;            // reuse the 1×1 white sprite
        sr.sortingLayerName = "AdjacentHighlight";
        sr.sortingOrder = 95;                    // above dig (90), under hover (100)
        sr.enabled = false;
        return sr;
    }

    // Lights up exactly the cells CommitRoomFootprint would take: mined tiles in the
    // rectangle, minus any already claimed by another room.
    private void PaintRoomPreview(Vector3Int a, Vector3Int b)
    {
        var inf = ActiveInfluence;
        if (inf == null) { ClearRoomPreview(); return; }

        Vector3 o = inf.CellToWorld(Vector3Int.zero);
        float cw = Mathf.Abs(inf.CellToWorld(Vector3Int.right).x - o.x);
        float ch = Mathf.Abs(inf.CellToWorld(Vector3Int.up).y - o.y);

        int minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        int minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);

        var claimed = CollectOtherRoomFootprints(redesignateTarget);

        int j = 0;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var cell = new Vector3Int(x, y, a.z);
                if (!inf.IsTileMined(cell) || claimed.Contains(cell)) continue;
                if (j >= roomPreviewPool.Count) roomPreviewPool.Add(CreateRoomPreviewQuad());
                var sr = roomPreviewPool[j++];
                Vector3 w = inf.CellToWorld(cell);
                sr.transform.position = new Vector3(w.x, w.y, 0f);
                sr.transform.localScale = new Vector3(cw, ch, 1f);
                sr.color = roomPreviewColor;
                sr.enabled = true;
            }
        for (; j < roomPreviewPool.Count; j++) roomPreviewPool[j].enabled = false;
    }

    private void ClearRoomPreview()
    {
        for (int i = 0; i < roomPreviewPool.Count; i++) roomPreviewPool[i].enabled = false;
    }

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (selectedTrap == null || selectedTrap.prefab == null) return;
        if (ActiveTrapRegistry != null && ActiveTrapRegistry.GetTrapAt(cell) != null) { RejectAt(cell, "A trap is already here"); return; }
        if (!DungeonCore.Instance.SpendMana(selectedTrap.manaCost)) { RejectAt(cell, "Not enough mana"); return; }

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
        if (FloorManager.Instance.ActiveFloorIndex >= FloorManager.Instance.MaxAllowedFloorIndex) { RejectAt(cell, "Deepest floor reached"); SetMode(BuildMode.Claim); return; }
        if (FloorManager.Instance.FloorHasDownStair(FloorManager.Instance.ActiveFloorIndex)) { RejectAt(cell, "This floor already has stairs down"); SetMode(BuildMode.Claim); return; }
        if (DungeonCore.Instance == null || DungeonCore.Instance.StairCredits <= 0) { RejectAt(cell, "Level up before expanding deeper"); SetMode(BuildMode.Claim); return; }
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        if (stairsDefinition == null || stairsDefinition.prefab == null) return;
        if (!DungeonCore.Instance.SpendMana(stairsDefinition.manaCost)) { RejectAt(cell, "Not enough mana"); return; }
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
                                  RoomDefinitionRegistry roomDefRegistry, int tier = 1,
                                  System.Collections.Generic.List<SerializableVector3Int> footprint = null)
    {
        if (roomAnchorPrefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        anchor.transform.SetParent(floor.transform, true);
        anchor.Initialise(cell);

        if (footprint != null && footprint.Count > 0)
        {
            var cells = new System.Collections.Generic.List<Vector3Int>(footprint.Count);
            for (int i = 0; i < footprint.Count; i++) cells.Add(footprint[i].ToVector3Int());
            anchor.SetFootprint(cells);
        }

        if (!string.IsNullOrEmpty(roomName))
        {
            var defRes = roomDefRegistry?.GetByName(roomName);
            if (defRes != null) anchor.SetRoomType(defRes);
        }

        // Flood-fill-era saves have no footprint — seed one so the room keeps its
        // old extent. New saves always carry an explicit footprint.
        if (footprint == null || footprint.Count == 0)
            anchor.MigrateFootprintFromFloodFill();
        anchor.SetTier(tier);
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

        var activeFloor = FloorManager.Instance?.ActiveFloor;
        if (activeFloor?.Entities == null) return false;

        var stairBuf = _stairClickBuf ??= new System.Collections.Generic.List<DungeonStairs>();
        activeFloor.Entities.FillAll(stairBuf);

        for (int i = 0; i < stairBuf.Count; i++)
        {
            var stair = stairBuf[i];
            var col = stair.GetComponent<Collider2D>();
            if (col == null) continue;
            if (col.OverlapPoint(worldPos))
            {
                FloorManager.Instance?.SwitchToFloorAnimated(stair.LinkedFloorIndex);
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

            HandleSpawnerClicked(monster.Spawner);
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

            HandleSpawnerClicked(spawner);
            return true;
        }
        return false;
    }

    private static bool ShiftHeld()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    private MonsterSpawner lastClickedSpawner;
    private float lastClickTime;
    private const float DoubleClickWindow = 0.30f;
    private System.Collections.Generic.List<MonsterSpawner> _sameTypeBuf;

    // Single-click selects/toggles (Part 1); a second click on the same spawner
    // within the window selects all of that type on the active floor.
    private void HandleSpawnerClicked(MonsterSpawner spawner)
    {
        if (spawner == null) return;

        bool isDouble = spawner == lastClickedSpawner
            && Time.unscaledTime - lastClickTime <= DoubleClickWindow;
        lastClickedSpawner = spawner;
        lastClickTime = Time.unscaledTime;

        if (isDouble) { SelectSameType(spawner); return; }

        if (ShiftHeld()) SpawnerSelectionController.Instance?.Toggle(spawner);
        else SpawnerSelectionController.Instance?.Select(spawner);
    }

    private void SelectSameType(MonsterSpawner template)
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null || template == null) return;

        var def = template.Definition;
        _sameTypeBuf ??= new System.Collections.Generic.List<MonsterSpawner>();
        floor.Entities.FillAll(_sameTypeBuf);

        var same = new System.Collections.Generic.List<MonsterSpawner>();
        for (int i = 0; i < _sameTypeBuf.Count; i++)
            if (_sameTypeBuf[i] != null && _sameTypeBuf[i].Definition == def)
                same.Add(_sameTypeBuf[i]);

        SpawnerSelectionController.Instance?.SelectSet(same, ShiftHeld());
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

    // ── Dig-queue processing ──────────────────────────────────────
    private enum DigResult { Dug, Blocked, Stalled }

    // Digs queued cells on the active floor over time; spends mana per tile and
    // pauses (no burst) when mana runs out, resuming as it regenerates.
    private void ProcessDigQueue()
    {
        if (digQueue.Count == 0) { digTickTimer = 0f; return; }
        float interval = digTicksPerSecond > 0f ? 1f / digTicksPerSecond : 0.1f;
        digTickTimer += Time.deltaTime;
        int safety = 0;
        while (digTickTimer >= interval && digQueue.Count > 0 && safety < 8)
        {
            if (TryDigOneQueued() == DigResult.Dug) { digTickTimer -= interval; safety++; continue; }
            digTickTimer = 0f;   // blocked/stalled — wait, don't bank ticks
            break;
        }
    }

    private DigResult TryDigOneQueued()
    {
        var inf = ActiveInfluence;
        var floor = ActiveFloor;
        if (inf == null || floor == null || FloorManager.Instance == null) return DigResult.Stalled;
        int active = FloorManager.Instance.ActiveFloorIndex;

        for (int i = 0; i < digQueue.Count; i++)
        {
            var ord = digQueue[i];
            if (ord.floor != active) continue;   // off-floor cells pause until you return
            Vector3Int cell = ord.cell;

            if (inf.IsTileMined(cell) || !inf.IsTileClaimed(cell)) { RemoveDigAt(i); i--; continue; }
            if (!CanMineCell(cell)) continue;     // not on the mined frontier yet

            float cost = mineManaCost * floor.GetClaimCostMultiplier(cell);
            if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(cost))
                return DigResult.Blocked;          // out of mana — wait for regen

            inf.MineTile(cell);
            RemoveDigAt(i);
            return DigResult.Dug;
        }
        return DigResult.Stalled;
    }

    private void RemoveDigAt(int i)
    {
        digQueued.Remove(digQueue[i]);
        digQueue.RemoveAt(i);
        digOverlayDirty = true;
    }

    private void ClearDigQueue()
    {
        if (digQueue.Count == 0) return;
        digQueue.Clear();
        digQueued.Clear();
        digOverlayDirty = true;
    }

    // ── Dig-queue overlay (pooled translucent quads over queued cells) ──
    private void BuildDigOverlayAssets()
    {
        var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        digOverlaySprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        digOverlayParent = new GameObject("DigQueueOverlay").transform;
        roomPreviewParent = new GameObject("RoomPreviewOverlay").transform;
    }

    private SpriteRenderer CreateDigOverlayQuad()
    {
        var go = new GameObject("DigQueueCell");
        if (digOverlayParent != null) go.transform.SetParent(digOverlayParent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = digOverlaySprite;
        sr.sortingLayerName = "AdjacentHighlight";
        sr.sortingOrder = 90;   // under the hover highlight (100)
        sr.enabled = false;
        return sr;
    }

    // Rebuilt only when the queue or active floor changes; draws active-floor cells.
    private void UpdateDigQueueOverlay()
    {
        int active = FloorManager.Instance != null ? FloorManager.Instance.ActiveFloorIndex : int.MinValue;
        if (active != lastOverlayFloor) { digOverlayDirty = true; lastOverlayFloor = active; }
        if (!digOverlayDirty) return;
        digOverlayDirty = false;

        var inf = ActiveInfluence;
        float cw = 1f, ch = 1f;
        if (inf != null)
        {
            Vector3 o = inf.CellToWorld(Vector3Int.zero);
            cw = Mathf.Abs(inf.CellToWorld(Vector3Int.right).x - o.x);
            ch = Mathf.Abs(inf.CellToWorld(Vector3Int.up).y - o.y);
        }

        int j = 0;
        if (inf != null)
        {
            for (int i = 0; i < digQueue.Count; i++)
            {
                if (digQueue[i].floor != active) continue;
                if (j >= digOverlayPool.Count) digOverlayPool.Add(CreateDigOverlayQuad());
                var sr = digOverlayPool[j++];
                Vector3 w = inf.CellToWorld(digQueue[i].cell);
                sr.transform.position = new Vector3(w.x, w.y, 0f);
                sr.transform.localScale = new Vector3(cw, ch, 1f);
                sr.color = digQueueColor;
                sr.enabled = true;
            }
        }
        for (; j < digOverlayPool.Count; j++) digOverlayPool[j].enabled = false;
    }

    private static bool IsShiftHeld()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    private void RevalidateAllAnchors()
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return;

        var buf = _anchorRevalidateBuf ??= new System.Collections.Generic.List<RoomAnchor>();
        floor.Entities.FillAll(buf);
        for (int i = 0; i < buf.Count; i++) buf[i].Revalidate();
    }
    private System.Collections.Generic.List<RoomAnchor> _anchorRevalidateBuf;
}