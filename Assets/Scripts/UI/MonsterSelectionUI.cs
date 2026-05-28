using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small UI panel for selecting a monster type before placing a spawner.
/// Shows the monster name, icon, capacity cost, and description.
/// Cycle through types with Prev/Next buttons or by clicking type buttons.
///
/// PREFAB / SCENE SETUP (attach to a Panel under UICanvas_Dungeon):
///   MonsterSelectionUI (this script)
///   ├── MonsterIcon       (Image)
///   ├── MonsterNameLabel  (TMP_Text)
///   ├── CostLabel         (TMP_Text)
///   ├── DescriptionLabel  (TMP_Text)
///   ├── PrevButton        (Button)
///   └── NextButton        (Button)
///
/// Wire PrevButton.OnClick → OnPrevClicked()
/// Wire NextButton.OnClick → OnNextClicked()
/// Panel is hidden by default; shown when BuildMode switches to PlaceSpawner.
/// </summary>
public class MonsterSelectionUI : MonoBehaviour
{
    public static MonsterSelectionUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Available Monster Types")]
    [SerializeField] private MonsterDefinitionRegistry registry;

    [Header("UI References")]
    [SerializeField] private GameObject     panel;
    [SerializeField] private Image          monsterIcon;
    [SerializeField] private TMP_Text       monsterNameLabel;
    [SerializeField] private TMP_Text       costLabel;
    [SerializeField] private TMP_Text       descriptionLabel;

    // ── State ─────────────────────────────────────────────────────
    private int selectedIndex = 0;

    // ── Public ────────────────────────────────────────────────────

    /// <summary>The currently selected monster definition.</summary>
    public MonsterDefinition Selected =>
        registry.All != null && registry.All.Count > 0
            ? registry.All[selectedIndex]
            : null;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Hide();
    }

    private void Start()
    {
        // Listen for build mode changes
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;

        RefreshDisplay();
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
    }

    // ── Mode Handling ─────────────────────────────────────────────

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.PlaceSpawner)
            Show();
        else
            Hide();
    }

    // ── Navigation ────────────────────────────────────────────────

    public void OnPrevClicked()
    {
        if (registry.All == null || registry.All.Count == 0) return;
        selectedIndex = (selectedIndex - 1 + registry.All.Count) % registry.All.Count;
        RefreshDisplay();
    }

    public void OnNextClicked()
    {
        if (registry.All == null || registry.All.Count == 0) return;
        selectedIndex = (selectedIndex + 1) % registry.All.Count;
        RefreshDisplay();
    }

    // ── Display ───────────────────────────────────────────────────

    private void RefreshDisplay()
    {
        var def = Selected;
        if (def == null) return;

        if (monsterIcon      != null) monsterIcon.sprite     = def.icon;
        if (monsterNameLabel != null) monsterNameLabel.text  = def.monsterName;
        if (costLabel        != null) costLabel.text         = $"Capacity: {def.capacityCost}";
        if (descriptionLabel != null) descriptionLabel.text  = def.description;
    }

    public void OnCloseClicked()
    {
        DungeonBuildController.Instance?.SetMode(BuildMode.Claim);
        // Hide() is called automatically via HandleModeChanged when mode switches to Claim
    }

    private void Show()
    {
        if (panel != null) panel.SetActive(true);
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
