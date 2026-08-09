using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum BuildMode
{
    Push,
    Mine,
    PlaceEntrance,
    PlaceSpawner,
    PlaceChest,
    PlaceFurniture,
    PlaceRoomAnchor,
    PlaceTrap,
    PlaceStairs,
    PlaceCore,
    PlaceMonsterPatrol,        
    PlaceMonsterAttackTarget,
    PlaceMonsterPost,
    Demolish,
    BuildWall,
    None, 
}

// Registry tier (-90). Other components subscribe to this singleton's
// events from their own OnEnable under an `if (Instance != null)` guard.
// At default order that guard races this Awake, and the loser skips its
// subscription silently and forever -- which is how the minimap spent a
// whole session painting floor 0 and following nothing. See canon
// Appendix D, Execution Order Contract, before changing this.
[DefaultExecutionOrder(-90)]
public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    /// Raised by the marquee selector while a box-select drag is in progress, so
    /// push/mine input is suppressed for the duration of the drag.
    public bool SuppressBuildInput { get; set; }

    public void SetSelectedFurniture(FurnitureDefinition def) => selectedFurniture = def;
    public void SetSelectedTrap(TrapDefinition def) => selectedTrap = def;
    public void SetSelectedChest(ChestDefinition def) => selectedChest = def;

    [Header("Mana Costs")]
    [FormerlySerializedAs("claimManaCost")]
    [SerializeField] private float mineManaCost = 5f;

    [Header("Prefabs")]
    [SerializeField] private DungeonEntrance entrancePrefab;
    [SerializeField] private ChestDefinition selectedChest;
    [SerializeField] private MonsterSpawner spawnerShellPrefab;
    [Tooltip("Multipliers, tints and epithets for spawner promotion.")]
    [SerializeField] private PromotionTemplate promotionTemplate;
    [SerializeField] private RoomAnchor roomAnchorPrefab;
    [SerializeField] private FurnitureDefinition selectedFurniture;
    [SerializeField] private TrapDefinition selectedTrap;

    [Header("Stairs")]
    [SerializeField] private StairsDefinition stairsDefinition;

    public BuildMode CurrentMode { get; private set; } = BuildMode.None;
    public event Action<BuildMode> OnModeChanged;

    private Camera mainCamera;

    // Feature 3 — mine-target highlight (runtime overlay; no scene setup required).
    [Header("Mine Highlight")]
    [SerializeField] private Color mineHighlightColor = new Color(1f, 0.85f, 0.35f, 0.35f);
    private GameObject mineHighlightGO;
    private SpriteRenderer mineHighlightSR;

    // Built walls (canon 36). The click is the wall's visual BOTTOM: the solid
    // cell lands two north of it, so the rendered face falls exactly on the
    // clicked cell and the one above -- the three highlighted cells and the
    // three cells the pathfinder will refuse are the same three cells.
    [Header("Build Wall")]
    [Tooltip("Mana to raise one wall cell. Deliberately above the dig cost: unbuilding the world should not be cheaper than building it.")]
    [SerializeField] private float buildWallManaCost = 10f;
    [SerializeField] private Color wallGhostValidColor = new Color(1f, 1f, 1f, 0.45f);
    [SerializeField] private Color wallGhostInvalidColor = new Color(0.9f, 0.25f, 0.25f, 0.45f);
    private readonly SpriteRenderer[] wallGhost = new SpriteRenderer[3];   // 0 lower face, 1 upper face, 2 cap
    private TMPro.TextMeshPro wallGhostCostLabel;
    private Sprite wallGhostFallbackSprite;
    private CaveWallRenderer wallGhostRenderer;
    private FloorRoot wallGhostRendererFloor;
    private bool wallDragTracking;
    private Vector3Int wallDragLastCell;

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

    /// <summary>How a mine gesture is interpreted. Chosen from the Mine sub-menu and
    /// remembered across sessions; Single is the fallback when nothing was ever picked.</summary>
    public enum MineGesture { Single, Drag, Box }

    private const string MinePrefKey = "DCR.MineGesture";
    private static MineGesture mineGesture = MineGesture.Single;
    private static bool mineGestureLoaded;

    public static MineGesture CurrentMineGesture
    {
        get
        {
            if (!mineGestureLoaded)
            {
                int saved = PlayerPrefs.GetInt(MinePrefKey, (int)MineGesture.Single);
                mineGesture = System.Enum.IsDefined(typeof(MineGesture), saved)
                    ? (MineGesture)saved : MineGesture.Single;
                mineGestureLoaded = true;
            }
            return mineGesture;
        }
    }

    /// <summary>Fires when the mine gesture changes, so the action bar can restyle.</summary>
    public static event System.Action OnMineGestureChanged;

    public static void SetMineGesture(MineGesture g)
    {
        mineGesture = g;
        mineGestureLoaded = true;
        PlayerPrefs.SetInt(MinePrefKey, (int)g);
        PlayerPrefs.Save();
        OnMineGestureChanged?.Invoke();
    }

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
        UpdateBuildWallGhost();
        UpdateDigQueueOverlay();
        if (!PauseController.IsGamePaused) ProcessDigQueue();

        // DAY 31 — Master pause-gate removed. Specific actions choose whether to honor
        // pause (active-pause pattern). Navigation, spawner selection, and command-UI
        // placement modes run during pause; gameplay-changing build modes do not.

        if (TryHandleStairClick()) return;

        if (CurrentMode == BuildMode.None)
        {
            if (TryHandleSpawnerOrMonsterClick()) return;

            // Right-click with monsters selected = move/attack-here for the whole group.
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame
                && SpawnerSelectionController.Instance != null
                && SpawnerSelectionController.Instance.Count > 0
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                if (HoverCell(out Vector3Int moveCell) && IsCellValidForWaypoint(moveCell))
                    foreach (var s in SpawnerSelectionController.Instance.Selected)
                        if (s != null) s.SetAttackTarget(moveCell);
                return;
            }

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
                SetMode(BuildMode.None);
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
        if (CurrentMode == BuildMode.PlaceMonsterPost)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { CancelPostPlacement(); return; }
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                CancelPostPlacement();
                return;
            }
            HandlePostPlacement();
            return;
        }

        // Everything below is gameplay-changing and respects pause.
        if (PauseController.IsGamePaused) return;

        switch (CurrentMode)
        {
            case BuildMode.Push: HandlePushChannel(); break;
            case BuildMode.Mine: HandleMineInput(); break;
            case BuildMode.PlaceEntrance: HandleEntrancePlacement(); break;
            case BuildMode.PlaceSpawner: HandleSpawnerPlacement(); break;
            case BuildMode.PlaceChest: HandleChestPlacement(); break;
            case BuildMode.PlaceFurniture: HandleFurniturePlacement(); break;
            case BuildMode.PlaceRoomAnchor: HandleRoomAnchorPlacement(); break;
            case BuildMode.PlaceTrap: HandleTrapPlacement(); break;
            case BuildMode.PlaceStairs: HandleStairsPlacement(); break;
            case BuildMode.PlaceCore: HandlePlaceCoreMode(); break;
            case BuildMode.Demolish: HandleDemolish(); break;
            case BuildMode.BuildWall: HandleBuildWallInput(); break;
        }
    }

    public void SetMode(BuildMode mode)
    {
        if (mode == BuildMode.PlaceTrap && !UnlockState.IsUnlocked("tech.spike_trap"))
        {
            AlertsLog.Instance?.AddAlert(
                "The shape of iron teeth is not yet remembered.",
                DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : Vector3.zero,
                0, AlertCategory.Discovery);
            return;
        }
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        Debug.Log($"[BuildController] Mode → {mode}");
        OnModeChanged?.Invoke(mode);
        if (mode == BuildMode.PlaceSpawner) RefreshMusterHighlight();
        else ClearMusterHighlight();
    }

    public void SetModeToPush() => SetMode(BuildMode.Push);

    /// <summary>Legacy alias — kept so any Inspector-wired UnityEvents keep working.</summary>
    public void SetModeToClaim() => SetMode(BuildMode.Push);
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

    // ── Patrol / Attack Placement Entry ──────────

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

    public void BeginPostPlacement(MonsterSpawner spawner)
    {
        if (spawner == null) return;
        placementSpawner = spawner;
        SetMode(BuildMode.PlaceMonsterPost);
    }

    private void CommitPatrolPlacement()
    {
        placementSpawner = null;
        SetMode(BuildMode.None);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    private void CancelAttackTargetPlacement()
    {
        placementSpawner = null;
        SetMode(BuildMode.None);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    private void CancelPostPlacement()
    {
        placementSpawner = null;
        SetMode(BuildMode.None);
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

    // ── Push (influence channel) ────────────────────────────────

    /// <summary>Per-frame driver for Push mode. Input capture stays here (hover,
    /// hold, marquee suppression); the channel itself — pathing, progress, mana
    /// drain, preview line — lives in InfluenceChannel.</summary>
    private void HandlePushChannel()
    {
        var channel = InfluenceChannel.Instance;
        if (channel == null) return;

        if (SuppressBuildInput)
        {
            channel.Tick(ActiveFloor, null, false);
            return;
        }

        bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;
        Vector3Int? hover = HoverCell(out Vector3Int cell) ? cell : (Vector3Int?)null;
        channel.Tick(ActiveFloor, hover, held);
    }


    // ── Mine input: click mines one now · drag queues a swath · Shift+click queues one ──
    private bool mineBoxPainted;

    /// <summary>Queue every diggable cell in the rectangle spanned by two corners.
    /// EnqueueDig already rejects anything undiggable, so no filtering is needed here.</summary>
    private void EnqueueDigBox(Vector3Int a, Vector3Int b)
    {
        int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
        int y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                EnqueueDig(new Vector3Int(x, y, a.z));
    }

    /// <summary>Rectangle preview for Box mode. Reuses the room-drag quad pool and its
    /// CreateRoomPreviewQuad factory so the two rectangle gestures look identical; only
    /// the cell filter differs (diggable rock here, mined floor there).</summary>
    private void PaintMineBoxPreview(Vector3Int a, Vector3Int b)
    {
        var inf = ActiveInfluence;
        if (inf == null) { ClearRoomPreview(); return; }

        Vector3 o = inf.CellToWorld(Vector3Int.zero);
        float cw = Mathf.Abs(inf.CellToWorld(Vector3Int.right).x - o.x);
        float ch = Mathf.Abs(inf.CellToWorld(Vector3Int.up).y - o.y);

        int minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        int minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);

        int j = 0;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var cell = new Vector3Int(x, y, a.z);
                if (inf.IsTileMined(cell)) continue;   // already open: nothing to dig
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

    private void ClearMineBoxPreview() => ClearRoomPreview();

    private void HandleMineInput()
    {
        if (SuppressBuildInput) return;
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

        bool boxMode = CurrentMineGesture == MineGesture.Box;

        // Held + entered a new cell. Drag paints along the path; Box only tracks the
        // opposite corner and previews the rectangle, committing on release.
        if (mineTracking && mouse.leftButton.isPressed)
        {
            if (HoverCell(out Vector3Int c) && c != mineLastCell)
            {
                mineIsDrag = true;
                mineLastCell = c;
                if (boxMode) PaintMineBoxPreview(minePressCell, mineLastCell);
                else
                {
                    if (!mineBoxPainted) { mineBoxPainted = true; EnqueueDig(minePressCell); }
                    EnqueueDig(c);
                }
            }
            return;
        }

        // Release. A gesture that never moved is a click in every mode.
        if (mineTracking && mouse.leftButton.wasReleasedThisFrame)
        {
            if (!mineIsDrag)
            {
                if (mineShiftAtPress) EnqueueDig(minePressCell);
                else MineImmediate(minePressCell);
            }
            else if (boxMode)
            {
                ClearMineBoxPreview();
                EnqueueDigBox(minePressCell, mineLastCell);
            }
            mineTracking = false;
            mineIsDrag = false;
            mineBoxPainted = false;
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
        var feats = ActiveFloor?.FeatureGenerator;
        if (feats != null && feats.IsRiver(cell)) return;   // water is never dug — don't queue it
        var key = (FloorManager.Instance.ActiveFloorIndex, cell);
        if (!digQueued.Add(key)) return;
        digQueue.Add(key);
        digOverlayDirty = true;
    }

    /// <summary>
    /// PHASE 2 — Mirror of TileInfluenceManager.MineTile's adjacency check, used
    /// by the mine input path to decide whether a dig would succeed before
    /// charging mana. Keeping the logic mirrored here means we don't pay (spend
    /// mana) and then fail silently.
    /// </summary>
    private bool CanMineCell(Vector3Int cell)
    {
        var influence = ActiveInfluence;
        if (influence == null) return false;
        if (!influence.IsTileClaimed(cell)) return false;
        if (influence.IsTileMined(cell)) return false;

        // Rivers are water — claimable and fordable, but can't be excavated.
        var feats = ActiveFloor?.FeatureGenerator;
        if (feats != null && feats.IsRiver(cell)) return false;

        // Core cell bypass — first mine has no neighbors.
        var terrain = ActiveFloor?.Terrain;
        if (terrain != null && cell == terrain.CoreCell) return true;

        var dirs = new[] { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        foreach (var d in dirs)
        {
            if (influence.IsTileMined(cell + d)) return true;
            if (feats != null && feats.IsRiver(cell + d)) return true;   // water is an open frontier
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

    // -- Build wall (canon 36) ------------------------------------

    /// <summary>Click builds one wall column; holding and dragging paints a run,
    /// one column per cell entered (the mine drag pattern, without the queue --
    /// building is instant). No box gesture on purpose: a box would pour solid
    /// slabs, and a slab is a mistake you pay to dig back out.</summary>
    private void HandleBuildWallInput()
    {
        if (SuppressBuildInput) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            wallDragTracking = false;
            if (!overUI && HoverCell(out Vector3Int c))
            {
                wallDragTracking = true;
                wallDragLastCell = c;
                TryBuildWall(c);
            }
            return;
        }

        if (wallDragTracking && mouse.leftButton.isPressed)
        {
            if (HoverCell(out Vector3Int c) && c != wallDragLastCell)
            {
                wallDragLastCell = c;
                TryBuildWall(c);
            }
            return;
        }

        if (mouse.leftButton.wasReleasedThisFrame) wallDragTracking = false;
    }

    /// <summary>Attempt one wall column at the clicked (bottom) cell. Validation,
    /// then mana, then the actual un-mine -- money is only taken when the wall
    /// will definitely stand. The reachability poke is a DIRECT call (Appendix
    /// D): the watchdog is what starts the starvation clock, and a lost
    /// subscription would let a sealing wall pass silently.</summary>
    private void TryBuildWall(Vector3Int clickCell)
    {
        var inf = ActiveInfluence;
        if (inf == null) return;
        if (!CanBuildWallAt(clickCell, out Vector3Int target, out string reason))
        {
            RejectAt(clickCell, reason);
            return;
        }
        if (DungeonCore.Instance != null && !DungeonCore.Instance.SpendMana(buildWallManaCost))
        {
            RejectAt(clickCell, "Not enough mana");
            return;
        }
        inf.UnmineTile(target);
        ActiveFloor?.FeatureGenerator?.RegisterBuiltWall(target);
        ReachabilityDirector.Instance?.MarkDirty();
    }

    /// <summary>Whether a wall column may rise from this bottom cell, and why
    /// not when it may not. The three footprint cells (bottom, middle, solid
    /// target) must all be claimed open floor with nothing standing on or
    /// placed in them: the two lower cells become unwalkable overhang the
    /// moment the top one turns solid, so anything left inside would be
    /// stranded behind a face it cannot path out of.</summary>
    private bool CanBuildWallAt(Vector3Int clickCell, out Vector3Int target, out string reason)
    {
        var up = Vector3Int.up;
        target = clickCell + up + up;
        reason = null;
        var inf = ActiveInfluence;
        var floor = ActiveFloor;
        if (inf == null || floor == null) { reason = "No floor"; return false; }

        Vector3Int mid = clickCell + up;
        Vector3Int coreCell = floor.Terrain != null ? floor.Terrain.CoreCell : new Vector3Int(int.MinValue, 0, 0);

        Vector3Int[] cells = { clickCell, mid, target };
        foreach (var cell in cells)
        {
            if (!inf.IsTileMined(cell)) { reason = cell == target ? "Already solid there" : "Needs open floor"; return false; }
            if (!inf.IsTileClaimed(cell)) { reason = "Beyond your influence"; return false; }
            if (cell == coreCell) { reason = "The heart cannot be bricked in"; return false; }
            if (floor.FeatureGenerator != null && floor.FeatureGenerator.IsRiver(cell)) { reason = "Water takes no wall"; return false; }
            if (DungeonEntrance.Instance != null && DungeonEntrance.Instance.OccupiedCell == cell) { reason = "The doorway itself stands here"; return false; }

            var reg = floor.Entities;
            if (reg != null)
            {
                if (reg.GetAtCell<FurniturePiece>(cell) != null) { reason = "Blocked by furniture"; return false; }
                if (reg.GetAtCell<DungeonChest>(cell) != null) { reason = "Blocked by a chest"; return false; }
                if (reg.GetAtCell<TrapBase>(cell) != null) { reason = "Blocked by a trap"; return false; }
                if (reg.GetAtCell<DungeonStairs>(cell) != null) { reason = "Blocked by stairs"; return false; }
                if (reg.GetAtCell<MonsterSpawner>(cell) != null) { reason = "Blocked by a spawner"; return false; }
                if (reg.GetAtCell<RoomAnchor>(cell) != null) { reason = "Blocked by a room anchor"; return false; }
            }
        }

        // Registered room interiors refuse walls outright: a wall inside a
        // room would silently invalidate it, and the room machinery already
        // has a demolition path for players who mean it.
        _wallRoomBuf ??= new List<RoomAnchor>();
        floor.Entities?.FillAll(_wallRoomBuf);
        for (int i = 0; i < _wallRoomBuf.Count; i++)
        {
            var anchor = _wallRoomBuf[i];
            if (anchor == null || anchor.Footprint == null) continue;
            for (int j = 0; j < anchor.Footprint.Count; j++)
            {
                var f = anchor.Footprint[j];
                if (f == clickCell || f == mid || f == target) { reason = "Inside a room"; return false; }
            }
        }

        // Nothing living may stand in the footprint -- refusal, not shoving:
        // a push would need a safe destination search and a wall is not worth
        // teleporting a mercenary for.
        if (AnyEntityStandsIn(floor, inf, clickCell, mid, target)) { reason = "Someone stands there"; return false; }

        return true;
    }

    private List<RoomAnchor> _wallRoomBuf;
    private List<DungeonAdventurer> _wallAdvBuf;
    private List<DungeonMonster> _wallMonBuf;

    private bool AnyEntityStandsIn(FloorRoot floor, TileInfluenceManager inf, Vector3Int a, Vector3Int b, Vector3Int c)
    {
        if (floor.Entities == null) return false;
        _wallAdvBuf ??= new List<DungeonAdventurer>();
        _wallMonBuf ??= new List<DungeonMonster>();
        floor.Entities.FillAll(_wallAdvBuf);
        for (int i = 0; i < _wallAdvBuf.Count; i++)
        {
            var adv = _wallAdvBuf[i];
            if (adv == null) continue;
            var cell = inf.WorldToCell(adv.transform.position);
            if (cell == a || cell == b || cell == c) return true;
        }
        floor.Entities.FillAll(_wallMonBuf);
        for (int i = 0; i < _wallMonBuf.Count; i++)
        {
            var mon = _wallMonBuf[i];
            if (mon == null) continue;
            var cell = inf.WorldToCell(mon.transform.position);
            if (cell == a || cell == b || cell == c) return true;
        }
        return false;
    }

    /// <summary>Per-frame ghost: the three footprint cells carry translucent
    /// wall sprites (lower face, upper face, cap) pulled from the floor's own
    /// wall renderer so the ghost matches what will actually paint, tinted
    /// green-white when the column may rise and red when it may not, with the
    /// mana price floating over the cap (the hover cost preview, item p of
    /// the polish trio -- walls only for now). Falls back to flat quads when
    /// the renderer has no sliced sprites yet.</summary>
    private void UpdateBuildWallGhost()
    {
        if (CurrentMode != BuildMode.BuildWall || PauseController.IsGamePaused)
        {
            SetWallGhostVisible(false);
            return;
        }
        var inf = ActiveInfluence;
        if (inf == null || !HoverCell(out Vector3Int hover))
        {
            SetWallGhostVisible(false);
            return;
        }

        EnsureWallGhostAssets();
        RefreshWallGhostSprites();

        bool valid = CanBuildWallAt(hover, out Vector3Int target, out _);
        Color tint = valid ? wallGhostValidColor : wallGhostInvalidColor;

        Vector3Int up = Vector3Int.up;
        Vector3Int[] cells = { hover, hover + up, target };
        float cw = Mathf.Abs(inf.CellToWorld(hover + Vector3Int.right).x - inf.CellToWorld(hover).x);
        float ch = Mathf.Abs(inf.CellToWorld(hover + up).y - inf.CellToWorld(hover).y);

        for (int i = 0; i < 3; i++)
        {
            var sr = wallGhost[i];
            if (sr == null) continue;
            Vector3 w = inf.CellToWorld(cells[i]);
            sr.transform.position = new Vector3(w.x, w.y, 0f);
            // Sliced sprites carry their own pixel size; the flat fallback is a
            // one-pixel quad that must be scaled to the cell.
            sr.transform.localScale = sr.sprite == wallGhostFallbackSprite
                ? new Vector3(cw, ch, 1f) : Vector3.one;
            sr.color = tint;
            sr.enabled = true;
        }

        if (wallGhostCostLabel != null)
        {
            Vector3 capW = inf.CellToWorld(target);
            wallGhostCostLabel.transform.position = new Vector3(capW.x, capW.y + ch * 0.9f, 0f);
            wallGhostCostLabel.text = $"{buildWallManaCost:0} mana";
            wallGhostCostLabel.color = valid ? new Color(0.75f, 0.95f, 1f, 0.95f) : new Color(1f, 0.5f, 0.5f, 0.95f);
            wallGhostCostLabel.gameObject.SetActive(true);
        }
    }

    private void SetWallGhostVisible(bool visible)
    {
        for (int i = 0; i < 3; i++)
            if (wallGhost[i] != null) wallGhost[i].enabled = visible;
        if (!visible && wallGhostCostLabel != null) wallGhostCostLabel.gameObject.SetActive(false);
    }

    private void EnsureWallGhostAssets()
    {
        if (wallGhost[2] != null) return;

        var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        wallGhostFallbackSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        var parent = new GameObject("BuildWallGhost").transform;
        parent.SetParent(transform, false);
        string[] names = { "lowerFace", "upperFace", "cap" };
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject(names[i]);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = wallGhostFallbackSprite;
            // Above the real walls and floor, below the screen-space HUD --
            // the BuildFeedback sorting contract.
            sr.sortingLayerName = "WorldUI";
            sr.sortingOrder = 150;
            sr.enabled = false;
            wallGhost[i] = sr;
        }

        // The price label rides the TMP default font; a project without one
        // simply shows no price, exactly as BuildFeedback degrades.
        if (TMPro.TMP_Settings.defaultFontAsset != null)
        {
            var go = new GameObject("costLabel");
            go.transform.SetParent(parent, false);
            wallGhostCostLabel = go.AddComponent<TMPro.TextMeshPro>();
            wallGhostCostLabel.fontSize = 3f;
            wallGhostCostLabel.alignment = TMPro.TextAlignmentOptions.Center;
            wallGhostCostLabel.sortingLayerID = UnityEngine.SortingLayer.NameToID("WorldUI");
            wallGhostCostLabel.sortingOrder = 151;
            var rt = wallGhostCostLabel.rectTransform;
            rt.sizeDelta = new Vector2(4f, 1f);
            go.SetActive(false);
        }
    }

    /// <summary>Re-pull the ghost's sprites from the ACTIVE floor's wall
    /// renderer, once per floor change. The renderer slices its sheet lazily,
    /// so early frames may fall back to flat quads and pick up the real
    /// slices on a later hover -- cosmetic and self-healing.</summary>
    private void RefreshWallGhostSprites()
    {
        var floor = ActiveFloor;
        if (floor == null) return;
        if (wallGhostRendererFloor != floor || wallGhostRenderer == null)
        {
            wallGhostRenderer = floor.GetComponentInChildren<CaveWallRenderer>();
            wallGhostRendererFloor = floor;
        }
        Sprite cap = null, upper = null, lower = null;
        if (wallGhostRenderer != null)
            wallGhostRenderer.TryGetGhostColumnSprites(out cap, out upper, out lower);
        wallGhost[0].sprite = lower != null ? lower : wallGhostFallbackSprite;
        wallGhost[1].sprite = upper != null ? upper : wallGhostFallbackSprite;
        wallGhost[2].sprite = cap != null ? cap : wallGhostFallbackSprite;
    }

    // ── Patrol placement (DAY 31 PART 3D) ─────────────────────────

    private void HandlePatrolPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (placementSpawner == null) { SetMode(BuildMode.None); return; }
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
        if (placementSpawner == null) { SetMode(BuildMode.None); return; }
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
        SetMode(BuildMode.None);
        FindObjectByType<MonsterCommandUI>()?.OnPlacementCommitted();
    }

    // -- Post placement -------------------------------------------------

    private void HandlePostPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (placementSpawner == null) { SetMode(BuildMode.None); return; }
        if (!IsCellValidForWaypoint(cell))
        {
            RejectAt(cell, "Must be owned or a revealed chamber");
            return;
        }
        if (ActiveInfluence != null && ActiveInfluence.IsUnderOverhang(cell))
        {
            RejectAt(cell, "Blocked by a wall overhang");
            return;
        }
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.Count > 0)
        {
            foreach (var s in sel.Selected)
                if (s != null) s.SetPost(cell);
        }
        else
        {
            placementSpawner.SetPost(cell);
        }
        placementSpawner = null;
        SetMode(BuildMode.None);
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
        SetMode(BuildMode.None);
    }

    private void HandleSpawnerPlacement()
    {
        if (ActiveFloor != lastMusterFloor || MonsterSelectionUI.Instance?.Selected != lastMusterDef)
            RefreshMusterHighlight();
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        PlaceSpawner(cell);
    }

    private void PlaceSpawner(Vector3Int cell)
    {
        if (spawnerShellPrefab == null) return;
        var def = MonsterSelectionUI.Instance?.Selected;
        if (def != null && !string.IsNullOrEmpty(def.requiredTechKey)
            && !UnlockState.IsUnlocked(def.requiredTechKey))
        {
            AlertsLog.Instance?.AddAlert(
                "The core does not yet remember that shape.",
                transform.position, 0, AlertCategory.Discovery);
            return;
        }
        if (def == null) return;

        var core = DungeonCore.Instance;
        if (core == null) return;

        // Phase 3 closeout - spawners cost BOTH capacity and mana (per the roadmap).
        // Pre-check both so we never spend one resource and then fail on the other.
        if (def.RequiredFlatLevel > core.DungeonLevel) { RejectAt(cell, $"{def.monsterName} unlocks at {LevelTierUtil.DisplayName(def.RequiredFlatLevel)}"); return; }
        if (!def.AffinityMatches(core.DungeonType)) { RejectAt(cell, $"{def.monsterName} answers another core"); return; }
        if (def.requiresDiscovery && !BestiaryState.Discovered(def.monsterName)) { RejectAt(cell, $"{def.monsterName} - slay one in the wild to learn it"); return; }
        if (core.FreeCapacity < def.CapacityCost) { RejectAt(cell, "Monster capacity full"); return; }
        if (core.CurrentMana < def.ManaCost) { RejectAt(cell, "Not enough mana"); return; }

        // Muster rule: new spawners stand only inside a valid room that
        // accepts the monster's category (bosses: a Boss Room footprint that
        // validates once its own boss-spawner requirement is set aside).
        if (!MusterRooms.IsMusterGround(ActiveFloor, cell, def, true))
        {
            string rooms = def is BossVariantDefinition
                ? "Boss Room" : MusterRooms.MusterRoomNames(def.category);
            bool anyBuilt = MusterRooms.FillEligibleAnchors(ActiveFloor, def, musterAnchorBuf) > 0;
            RejectAt(cell, string.IsNullOrEmpty(rooms)
                ? "No muster ground -- designate the Core Chamber"
                : anyBuilt ? $"Musters only in: {rooms}"
                           : $"None standing -- build: {rooms}");
            return;
        }

        // Floor gate: no placing while intruders walk the active floor.
        if (FloorIntrusion.AnyOnFloor(ActiveFloor))
        {
            RejectAt(cell, "Intruders walk this floor");
            return;
        }

        core.TrySpendCapacity(def.CapacityCost);
        core.SpendMana(def.ManaCost);

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) spawner.transform.SetParent(ActiveFloor.transform, true);
        spawner.Initialise(def);
        spawner.MarkMusterGated();

        // Placing the boss is what completes a Boss Room -- revalidate so the
        // room flips valid the moment its spawner stands.
        if (def is BossVariantDefinition) RevalidateAllAnchors();
        SetMode(BuildMode.None);
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
        chest.Initialise(selectedChest, cell);
        SetMode(BuildMode.None);
    }

    /// <summary>
    /// One removal mode for everything the player builds ON the floor: furniture,
    /// traps and room anchors. Mana refunds follow each type's own rule (half the
    /// placement cost). Monster spawners are deliberately NOT handled here -- they
    /// keep their selection-driven removal in MonsterCommandUI, which also frees
    /// creature capacity and needs its own confirmation.
    ///
    /// Priority is furniture, then chests, then traps, then the room anchor, so
    /// clicking a crowded cell removes the thing sitting on top rather than the
    /// room beneath it.
    /// The mode is sticky: it stays armed until the player leaves it, because
    /// clearing a room means many clicks in a row.
    /// </summary>
    private void HandleDemolish()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;

        var floor = ActiveFloor;
        if (floor == null || floor.Entities == null) return;

        _demolishFurnitureBuf ??= new System.Collections.Generic.List<FurniturePiece>();
        floor.Entities.FillAll(_demolishFurnitureBuf);
        for (int i = 0; i < _demolishFurnitureBuf.Count; i++)
        {
            var piece = _demolishFurnitureBuf[i];
            if (piece == null || piece.OccupiedCell != cell) continue;
            piece.RemoveByPlayer();
            RevalidateAllAnchors();
            BuildFeedback.Reject(ActiveInfluence.CellToWorld(cell), "Removed");
            return;
        }

        _demolishChestBuf ??= new System.Collections.Generic.List<DungeonChest>();
        floor.Entities.FillAll(_demolishChestBuf);
        for (int i = 0; i < _demolishChestBuf.Count; i++)
        {
            var chest = _demolishChestBuf[i];
            if (chest == null || chest.OccupiedCell != cell) continue;
            chest.RemoveByPlayer();
            BuildFeedback.Reject(ActiveInfluence.CellToWorld(cell), "Removed");
            return;
        }

        _demolishTrapBuf ??= new System.Collections.Generic.List<TrapBase>();
        floor.Entities.FillAll(_demolishTrapBuf);
        for (int i = 0; i < _demolishTrapBuf.Count; i++)
        {
            var trap = _demolishTrapBuf[i];
            if (trap == null || trap.OccupiedCell != cell) continue;
            trap.RemoveByPlayer();
            BuildFeedback.Reject(ActiveInfluence.CellToWorld(cell), "Removed");
            return;
        }

        _demolishAnchorBuf ??= new System.Collections.Generic.List<RoomAnchor>();
        floor.Entities.FillAll(_demolishAnchorBuf);
        for (int i = 0; i < _demolishAnchorBuf.Count; i++)
        {
            var anchor = _demolishAnchorBuf[i];
            if (anchor == null) continue;
            var fp = anchor.Footprint;
            bool hit = false;
            for (int j = 0; j < fp.Count; j++) if (fp[j] == cell) { hit = true; break; }
            if (!hit) continue;
            anchor.RemoveByPlayer();
            BuildFeedback.Reject(ActiveInfluence.CellToWorld(cell), "Room dissolved");
            return;
        }
    }

    private System.Collections.Generic.List<FurniturePiece> _demolishFurnitureBuf;
    private System.Collections.Generic.List<DungeonChest> _demolishChestBuf;
    private System.Collections.Generic.List<TrapBase> _demolishTrapBuf;
    private System.Collections.Generic.List<RoomAnchor> _demolishAnchorBuf;

    public void SetModeToDemolish() => SetMode(BuildMode.Demolish);

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
        SetMode(BuildMode.None);
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

    // Muster-ground highlight while placing a spawner (reuses the preview quads).
    private readonly System.Collections.Generic.List<SpriteRenderer> musterHighlightPool = new();
    private readonly System.Collections.Generic.List<RoomAnchor> musterAnchorBuf = new();
    [SerializeField] private Color musterHighlightColor = new Color(0.83f, 0.65f, 0.15f, 0.22f);
    private FloorRoot lastMusterFloor;
    private MonsterDefinition lastMusterDef;
    private bool musterHighlightActive;

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
            SetMode(BuildMode.None);
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
        if (ActiveInfluence == null) { redesignateTarget = null; SetMode(BuildMode.None); return; }

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

        if (cells.Count == 0) { redesignateTarget = null; SetMode(BuildMode.None); return; }

        if (redesignateTarget != null)
        {
            redesignateTarget.SetFootprint(cells);   // keep its type, swap the footprint
            redesignateTarget = null;
            SetMode(BuildMode.None);
            return;
        }

        if (roomAnchorPrefab == null) { SetMode(BuildMode.None); return; }
        Vector3 worldPos = ActiveInfluence.CellToWorld(roomDragStart);
        var anchor = Instantiate(roomAnchorPrefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) anchor.transform.SetParent(ActiveFloor.transform, true);
        anchor.Initialise(roomDragStart);
        anchor.SetFootprint(cells);
        SetMode(BuildMode.None);
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

    // -- Muster-ground highlight (PlaceSpawner mode) ---------------------

    /// <summary>Tints every room on the active floor eligible to muster the
    /// selected monster. Self-heals each frame from HandleSpawnerPlacement:
    /// repaints when the floor or the picked definition changes.</summary>
    private void RefreshMusterHighlight()
    {
        var def = MonsterSelectionUI.Instance != null ? MonsterSelectionUI.Instance.Selected : null;
        var floor = ActiveFloor;
        lastMusterFloor = floor;
        lastMusterDef = def;
        musterHighlightActive = true;

        var inf = ActiveInfluence;
        int j = 0;
        if (def != null && floor != null && inf != null)
        {
            Vector3 o = inf.CellToWorld(Vector3Int.zero);
            float cw = Mathf.Abs(inf.CellToWorld(Vector3Int.right).x - o.x);
            float ch = Mathf.Abs(inf.CellToWorld(Vector3Int.up).y - o.y);

            MusterRooms.FillEligibleAnchors(floor, def, musterAnchorBuf);
            for (int a = 0; a < musterAnchorBuf.Count; a++)
            {
                var footprint = musterAnchorBuf[a].Footprint;
                if (footprint == null) continue;
                for (int c = 0; c < footprint.Count; c++)
                {
                    if (!inf.IsTileMined(footprint[c])) continue;
                    if (j >= musterHighlightPool.Count) musterHighlightPool.Add(CreateRoomPreviewQuad());
                    var sr = musterHighlightPool[j++];
                    Vector3 w = inf.CellToWorld(footprint[c]);
                    sr.transform.position = new Vector3(w.x, w.y, 0f);
                    sr.transform.localScale = new Vector3(cw, ch, 1f);
                    sr.color = musterHighlightColor;
                    sr.enabled = true;
                }
            }
        }
        for (; j < musterHighlightPool.Count; j++) musterHighlightPool[j].enabled = false;
    }

    private void ClearMusterHighlight()
    {
        if (!musterHighlightActive) return;
        musterHighlightActive = false;
        lastMusterFloor = null;
        lastMusterDef = null;
        for (int i = 0; i < musterHighlightPool.Count; i++) musterHighlightPool[i].enabled = false;
    }

    private void HandleTrapPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null || !ActiveInfluence.IsTileMined(cell)) return;
        // Trap chests are chosen from the Traps carousel but place as chests.
        if (selectedTrap == null && selectedChest != null)
        {
            if (selectedChest.prefab == null) return;
            if (!DungeonCore.Instance.SpendMana(selectedChest.manaCost)) { RejectAt(cell, "Not enough mana"); return; }
            Vector3 chestWorld = ActiveInfluence.CellToWorld(cell);
            var trapChest = Instantiate(selectedChest.prefab, chestWorld, Quaternion.identity);
            if (ActiveFloor != null) trapChest.transform.SetParent(ActiveFloor.transform, true);
            trapChest.Initialise(selectedChest, cell);
            SetMode(BuildMode.None);
            return;
        }

        if (selectedTrap == null || selectedTrap.prefab == null) return;
        // Backstops for the picker filters: another core's element, or a shape
        // not yet researched, is refused even if a stale selection slips through.
        if (selectedTrap.affinity != DungeonType.None && DungeonCore.Instance != null
            && selectedTrap.affinity != DungeonCore.Instance.DungeonType)
        { RejectAt(cell, "The core cannot hold that element's shape"); return; }
        if (!string.IsNullOrEmpty(selectedTrap.requiredTechKey)
            && !UnlockState.IsUnlocked(selectedTrap.requiredTechKey))
        { RejectAt(cell, "That shape is not yet remembered"); return; }
        if (ActiveTrapRegistry != null && ActiveTrapRegistry.GetTrapAt(cell) != null) { RejectAt(cell, "A trap is already here"); return; }
        if (DungeonCore.Instance.FreeCapacity < selectedTrap.capacityCost) { RejectAt(cell, "Trap capacity full"); return; }
        if (DungeonCore.Instance.CurrentMana < selectedTrap.manaCost) { RejectAt(cell, "Not enough mana"); return; }
        DungeonCore.Instance.TrySpendCapacity(selectedTrap.capacityCost);
        DungeonCore.Instance.SpendMana(selectedTrap.manaCost);

        Vector3 worldPos = ActiveInfluence.CellToWorld(cell);
        var trap = Instantiate(selectedTrap.prefab, worldPos, Quaternion.identity);
        if (ActiveFloor != null) trap.transform.SetParent(ActiveFloor.transform, true);
        trap.Initialise(selectedTrap, cell);
        if (trap is WarningTrap warning) WarningTrapNameDialog.Instance?.Open(warning);
        SetMode(BuildMode.None);
    }

    private void HandleStairsPlacement()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (FloorManager.Instance == null) return;
        if (FloorManager.Instance.IsCoreRelocationPending) { SetMode(BuildMode.None); return; }
        if (FloorManager.Instance.ActiveFloorIndex >= FloorManager.Instance.MaxAllowedFloorIndex) { RejectAt(cell, "Deepest floor reached"); SetMode(BuildMode.None); return; }
        if (FloorManager.Instance.FloorHasDownStair(FloorManager.Instance.ActiveFloorIndex)) { RejectAt(cell, "This floor already has stairs down"); SetMode(BuildMode.None); return; }
        if (DungeonCore.Instance == null || DungeonCore.Instance.StairCredits <= 0) { RejectAt(cell, "Level up before expanding deeper"); SetMode(BuildMode.None); return; }
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

        SetMode(BuildMode.None);
    }

    private void HandlePlaceCoreMode()
    {
        if (!LeftClickThisFrame(out Vector3Int cell)) return;
        if (ActiveInfluence == null) return;
        if (!ActiveInfluence.IsTileMined(cell)) return;
        if (FloorManager.Instance == null || !FloorManager.Instance.CanPlaceCore) { SetMode(BuildMode.None); return; }

        int destIdx = FloorManager.Instance.PendingCoreRelocationFloor;
        var destFloor = FloorManager.Instance.GetFloor(destIdx);
        if (destFloor == null) { SetMode(BuildMode.None); return; }
        if (FloorManager.Instance.ActiveFloorIndex != destIdx) { FloorManager.Instance.SwitchToFloor(destIdx); return; }

        DungeonCore.Instance.Relocate(destFloor, cell);
        SetMode(BuildMode.None);
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

    // -- Spawner promotion ---------------------------------------------------

    public PromotionTemplate Promotion => promotionTemplate;

    /// <summary>Per-floor rank census (the promotion limits: 1 boss, 2 sub-bosses).</summary>
    public int CountRankOnFloor(FloorRoot floor, PromotionRank rank, MonsterSpawner exclude)
    {
        if (floor?.Entities == null) return 0;
        promoSpawnerBuf.Clear();
        floor.Entities.FillAll(promoSpawnerBuf);
        int count = 0;
        for (int i = 0; i < promoSpawnerBuf.Count; i++)
        {
            var s = promoSpawnerBuf[i];
            if (s == null || s == exclude) continue;
            if (s.Rank == rank) count++;
        }
        return count;
    }
    private static readonly System.Collections.Generic.List<MonsterSpawner> promoSpawnerBuf = new();

    /// <summary>True when the spawner stands inside a Boss Room footprint that
    /// validates once its own boss requirement is set aside (rule A: boss rank
    /// exists only inside Boss Rooms).</summary>
    public bool IsOnBossGround(MonsterSpawner s)
    {
        var floor = s != null ? s.Floor : null;
        if (floor?.Entities == null || floor.TileInfluence == null) return false;
        Vector3Int cell = floor.TileInfluence.WorldToCell(s.transform.position);
        promoAnchorBuf.Clear();
        int n = floor.Entities.FillAll(promoAnchorBuf);
        for (int i = 0; i < n; i++)
        {
            var anchor = promoAnchorBuf[i];
            if (anchor == null || anchor.AssignedRoom == null) continue;
            if (!anchor.AssignedRoom.requiresBossSpawner) continue;
            var result = RoomValidator.Validate(
                anchor.Footprint, anchor.AssignedRoom, ignoreBossSpawner: true);
            if (result.IsValid && result.RoomTiles.Contains(cell)) return true;
        }
        return false;
    }
    private static readonly System.Collections.Generic.List<RoomAnchor> promoAnchorBuf = new();

    /// <summary>Gate check without paying: fills the reason and the mana price so
    /// the command panel can label its buttons honestly.</summary>
    public bool CanPromote(MonsterSpawner s, PromotionRank target,
                           out string reason, out float manaCost)
    {
        reason = ""; manaCost = 0f;
        if (s == null || s.Definition == null) { reason = "No spawner"; return false; }
        if (promotionTemplate == null) { reason = "No promotion template assigned"; return false; }
        if (s.IsTransient) { reason = "A moment-creature cannot rise"; return false; }
        if (target <= s.Rank) { reason = "Already risen"; return false; }
        if (target == PromotionRank.SubBoss && s.Rank != PromotionRank.None)
        { reason = "Already risen"; return false; }

        var core = DungeonCore.Instance;
        if (core == null) { reason = "No core"; return false; }

        var floor = s.Floor;
        if (target == PromotionRank.Boss)
        {
            if (!IsOnBossGround(s)) { reason = "Bosses rise only in a Boss Room"; return false; }
            if (CountRankOnFloor(floor, PromotionRank.Boss, s) >= 1)
            { reason = "This floor already has its boss"; return false; }
        }
        else if (CountRankOnFloor(floor, PromotionRank.SubBoss, s) >= 2)
        { reason = "This floor already has two sub-bosses"; return false; }

        manaCost = s.Definition.ManaCost
            * (promotionTemplate.ManaMult(target) - promotionTemplate.ManaMult(s.Rank));
        int baseCap = s.Definition.CapacityCost;
        int capDelta = promotionTemplate.TotalCapacityAt(baseCap, target)
                     - promotionTemplate.TotalCapacityAt(baseCap, s.Rank);
        if (core.FreeCapacity < capDelta) { reason = "Monster capacity full"; return false; }
        if (core.CurrentMana < manaCost) { reason = "Not enough mana"; return false; }
        return true;
    }

    /// <summary>Validate, pay, and promote. Rejection reasons toast at the
    /// spawner's cell through the standard reject path.</summary>
    public bool TryPromoteSpawner(MonsterSpawner s, PromotionRank target)
    {
        if (!CanPromote(s, target, out string reason, out float manaCost))
        {
            if (s != null && s.Floor?.TileInfluence != null)
                RejectAt(s.Floor.TileInfluence.WorldToCell(s.transform.position), reason);
            return false;
        }

        var core = DungeonCore.Instance;
        int baseCap = s.Definition.CapacityCost;
        int capDelta = promotionTemplate.TotalCapacityAt(baseCap, target)
                     - promotionTemplate.TotalCapacityAt(baseCap, s.Rank);
        core.TrySpendCapacity(capDelta);
        core.SpendMana(manaCost);

        string epithet = target == PromotionRank.Boss ? promotionTemplate.RollEpithet() : null;
        s.Promote(target, capDelta, epithet, promotionTemplate);

        // A boss completes its Boss Room; revalidate so the hall flips valid
        // (and its respawn hastening starts) the moment the tenant rises.
        if (target == PromotionRank.Boss) RevalidateAllAnchors();
        return true;
    }

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

    /// <summary>Spawn a transient minion (raised by a necromancer) at a cell: a spawner
    /// that holds no capacity, never respawns, and self-destructs when its monster dies.
    /// Returns the spawner (its monster spawns on the spawner's Start).</summary>
    public MonsterSpawner SpawnTransientMinion(FloorRoot floor, MonsterDefinition def, Vector3Int cell, float lifetime)
    {
        if (spawnerShellPrefab == null || def == null) return null;
        if (floor?.TileInfluence == null) return null;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.transform.SetParent(floor.transform, true);
        spawner.InitialiseTransient(def, lifetime);
        return spawner;
    }

    /// <summary>Crypt raise: a one-life risen hero at a sarcophagus cell. The caller has
    /// already paid mana and capacity; the spawner holds the capacity from here.</summary>
    public MonsterSpawner SpawnRaisedMinion(FloorRoot floor, MonsterDefinition def, Vector3Int cell, string risenName)
    {
        if (spawnerShellPrefab == null || def == null) return null;
        if (floor?.TileInfluence == null) return null;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var spawner = Instantiate(spawnerShellPrefab, worldPos, Quaternion.identity);
        spawner.transform.SetParent(floor.transform, true);
        spawner.InitialiseRaised(def);
        spawner.SetCustomName(risenName);
        return spawner;
    }

    public void RestoreChest(FloorRoot floor, ChestDefinition def, Vector3Int cell, bool isOpened)
    {
        if (def == null || def.prefab == null) return;
        if (floor?.TileInfluence == null) return;
        Vector3 worldPos = floor.TileInfluence.CellToWorld(cell);
        var chest = Instantiate(def.prefab, worldPos, Quaternion.identity);
        chest.transform.SetParent(floor.transform, true);
        chest.Initialise(def, cell);
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
                            bool isDisarmed = false, string warningLabel = "", bool hasLink = false,
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
        if (isDisarmed) trap.Disarm();
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