using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small UI panel for selecting a furniture type before placing.
/// Shows the furniture name, icon, mana cost (with affordability tint),
/// description, and stat lines.
/// Cycle through types with Prev/Next buttons.
///
/// PREFAB / SCENE SETUP (attach to a parent GameObject under UICanvas_Dungeon):
///   FurnitureSelectionUI (this script)
///   ├── Panel
///   │   ├── FurnitureIcon       (Image)
///   │   ├── FurnitureNameLabel  (TMP_Text)
///   │   ├── ManaCostIcon        (Image  — mana droplet sprite)
///   │   ├── ManaCostLabel       (TMP_Text — just the number)
///   │   ├── DescriptionLabel    (TMP_Text)
///   │   ├── StatsLabel          (TMP_Text — multiline)
///   │   ├── PrevButton          (Button — wire OnClick → OnPrevClicked)
///   │   └── NextButton          (Button — wire OnClick → OnNextClicked)
///
/// Wire PrevButton.OnClick → OnPrevClicked()
/// Wire NextButton.OnClick → OnNextClicked()
/// Optional close button.OnClick → OnCloseClicked()
/// Panel is hidden by default; shown when BuildMode switches to PlaceFurniture.
/// </summary>
public class FurnitureSelectionUI : MonoBehaviour
{
    public static FurnitureSelectionUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Available Furniture Types")]
    [SerializeField] private FurnitureDefinitionRegistry registry;

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Image furnitureIcon;
    [SerializeField] private TMP_Text furnitureNameLabel;
    [SerializeField] private Image manaCostIcon;
    [SerializeField] private TMP_Text manaCostLabel;
    [SerializeField] private TMP_Text descriptionLabel;
    [SerializeField] private TMP_Text statsLabel;

    [Header("Affordability Colours")]
    [SerializeField] private Color affordableColor = new(0.40f, 0.70f, 1.00f, 1f); // light blue
    [SerializeField] private Color unaffordableColor = new(0.90f, 0.30f, 0.30f, 1f); // red

    // ── State ─────────────────────────────────────────────────────
    private int selectedIndex = 0;

    // ── Public ────────────────────────────────────────────────────

    /// <summary>The currently selected furniture definition.</summary>
    public FurnitureDefinition Selected =>
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

        // Listen for mana changes so the affordability tint updates live
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

    // ── Mode Handling ─────────────────────────────────────────────

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.PlaceFurniture)
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
        if (registry.All == null || registry.All.Count == 0) return;
        selectedIndex = (selectedIndex - 1 + registry.All.Count) % registry.All.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    public void OnNextClicked()
    {
        if (registry.All == null || registry.All.Count == 0) return;
        selectedIndex = (selectedIndex + 1) % registry.All.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    // ── Display ───────────────────────────────────────────────────

    private void RefreshDisplay()
    {
        var def = Selected;
        if (def == null) return;

        if (furnitureIcon != null) furnitureIcon.sprite = def.icon;
        if (furnitureNameLabel != null) furnitureNameLabel.text = def.furnitureName;
        if (manaCostLabel != null) manaCostLabel.text = def.manaCost.ToString("0");
        if (descriptionLabel != null) descriptionLabel.text = def.description;
        if (statsLabel != null) statsLabel.text = string.Join("\n", def.GetStatLines());

        UpdateAffordabilityVisual();
    }

    private void UpdateAffordabilityVisual()
    {
        var def = Selected;
        if (def == null) return;

        bool canAfford = DungeonCore.Instance != null
                      && DungeonCore.Instance.CurrentMana >= def.manaCost;

        Color tint = canAfford ? affordableColor : unaffordableColor;
        if (manaCostIcon != null) manaCostIcon.color = tint;
        if (manaCostLabel != null) manaCostLabel.color = tint;
    }

    public void OnCloseClicked()
    {
        DungeonBuildController.Instance?.SetMode(BuildMode.Claim);
        // Hide() is called automatically via HandleModeChanged when mode switches to Claim
    }

    private void PushSelectionToBuildController()
    {
        DungeonBuildController.Instance?.SetSelectedFurniture(Selected);
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