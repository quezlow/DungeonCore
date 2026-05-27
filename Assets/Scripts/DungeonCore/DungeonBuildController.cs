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
    // PlaceTrap, PlaceFurniture, etc. added in later sessions
}

public class DungeonBuildController : MonoBehaviour
{
    public static DungeonBuildController Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────

    [Header("Mana Costs")]
    [SerializeField] private float claimManaCost = 5f;

    [Header("Prefabs")]
    [SerializeField] private DungeonEntrance entrancePrefab;
    [SerializeField] private MonsterSpawner spawnerShellPrefab; // Shell only — no MonsterDefinition assigned

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