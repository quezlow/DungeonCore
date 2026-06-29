using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small UI panel for selecting a chest type before placing.
/// Shows the chest name, icon, mana cost (with affordability tint),
/// description, and stat lines.
/// Cycle through types with Prev/Next buttons.
///
/// Pattern follows FurnitureSelectionUI / TrapSelectionUI exactly.
/// Script lives on a stable parent GameObject; panel reference is toggled.
///
/// PREFAB / SCENE SETUP:
///   ChestSelectionUI (this script, on a parent GameObject)
///   ├── Panel
///   │   ├── ChestIcon       (Image)
///   │   ├── ChestNameLabel  (TMP_Text)
///   │   ├── ManaCostIcon    (Image — mana droplet sprite)
///   │   ├── ManaCostLabel   (TMP_Text — just the number)
///   │   ├── DescriptionLabel (TMP_Text)
///   │   ├── StatsLabel      (TMP_Text — multiline)
///   │   ├── PrevButton      (Button — wire OnClick → OnPrevClicked)
///   │   └── NextButton      (Button — wire OnClick → OnNextClicked)
/// </summary>
public class ChestSelectionUI : MonoBehaviour
{
    public static ChestSelectionUI Instance { get; private set; }

    [Header("Available Chest Types")]
    [SerializeField] private ChestDefinitionRegistry registry;

    [Header("UI References")]
    [SerializeField] private GameObject     panel;
    [SerializeField] private Image          chestIcon;
    [SerializeField] private TMP_Text       chestNameLabel;
    [SerializeField] private Image          manaCostIcon;
    [SerializeField] private TMP_Text       manaCostLabel;
    [SerializeField] private TMP_Text       descriptionLabel;
    [SerializeField] private TMP_Text       statsLabel;

    [Header("Affordability Colours")]
    [SerializeField] private Color affordableColor   = new(0.40f, 0.70f, 1.00f, 1f);
    [SerializeField] private Color unaffordableColor = new(0.90f, 0.30f, 0.30f, 1f);

    private int selectedIndex = 0;

    // Trap-variant chests live in the Traps carousel, so the chest picker shows treasure only.
    private readonly List<ChestDefinition> pool = new();
    private void BuildPool()
    {
        pool.Clear();
        if (registry != null && registry.All != null)
            foreach (var c in registry.All)
                if (c != null && !c.isTrapChest) pool.Add(c);
    }

    public ChestDefinition Selected =>
        pool.Count > 0 ? pool[Mathf.Clamp(selectedIndex, 0, pool.Count - 1)] : null;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void Start()
    {
        BuildPool();
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;

        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnManaChanged += HandleManaChanged;

        RefreshDisplay();
        PushSelectionToBuildController();
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;

        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnManaChanged -= HandleManaChanged;
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.PlaceChest)
        {
            Show();
            PushSelectionToBuildController();
        }
        else
            Hide();
    }

    private void HandleManaChanged(float currentMana, float maxMana)
    {
        UpdateAffordabilityVisual();
    }

    // ── Navigation ────────────────────────────────────────────────

    public void OnPrevClicked()
    {
        if (pool.Count == 0) return;
        selectedIndex = (selectedIndex - 1 + pool.Count) % pool.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    public void OnNextClicked()
    {
        if (pool.Count == 0) return;
        selectedIndex = (selectedIndex + 1) % pool.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    // ── Display ───────────────────────────────────────────────────

    private void RefreshDisplay()
    {
        var def = Selected;
        if (def == null) return;

        if (chestIcon        != null) chestIcon.sprite      = def.icon;
        if (chestNameLabel   != null) chestNameLabel.text   = def.chestName;
        if (manaCostLabel    != null) manaCostLabel.text    = def.manaCost.ToString("0");
        if (descriptionLabel != null) descriptionLabel.text = def.description;
        if (statsLabel       != null) statsLabel.text       = string.Join("\n", def.GetStatLines());

        UpdateAffordabilityVisual();
    }

    private void UpdateAffordabilityVisual()
    {
        var def = Selected;
        if (def == null) return;

        bool canAfford = DungeonCore.Instance != null
                      && DungeonCore.Instance.CurrentMana >= def.manaCost;

        Color tint = canAfford ? affordableColor : unaffordableColor;
        if (manaCostIcon  != null) manaCostIcon.color  = tint;
        if (manaCostLabel != null) manaCostLabel.color = tint;
    }

    public void OnCloseClicked()
    {
        DungeonBuildController.Instance?.SetMode(BuildMode.Claim);
    }

    private void PushSelectionToBuildController()
    {
        DungeonBuildController.Instance?.SetSelectedChest(Selected);
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
