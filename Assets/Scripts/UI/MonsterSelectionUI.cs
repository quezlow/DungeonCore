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
        for (int i = 0; i < registry.All.Count; i++)
        {
            selectedIndex = (selectedIndex - 1 + registry.All.Count) % registry.All.Count;
            if (AffinityAllowed(registry.All[selectedIndex])) break;
        }
        RefreshDisplay();
    }

    public void OnNextClicked()
    {
        if (registry.All == null || registry.All.Count == 0) return;
        for (int i = 0; i < registry.All.Count; i++)
        {
            selectedIndex = (selectedIndex + 1) % registry.All.Count;
            if (AffinityAllowed(registry.All[selectedIndex])) break;
        }
        RefreshDisplay();
    }

    // ── Display ───────────────────────────────────────────────────

    private void RefreshDisplay()
    {
        var def = Selected;
        if (def == null) return;

        bool rankLocked = DungeonCore.Instance != null
            && def.RequiredFlatLevel > DungeonCore.Instance.DungeonLevel;
        bool undiscovered = def.requiresDiscovery && !BestiaryState.Discovered(def.monsterName);
        bool techLocked = !string.IsNullOrEmpty(def.requiredTechKey)
            && !UnlockState.IsUnlocked(def.requiredTechKey);

        if (monsterIcon != null) monsterIcon.sprite = def.icon;
        if (monsterNameLabel != null) monsterNameLabel.text = def.monsterName;
        if (costLabel != null)
            costLabel.text = undiscovered
                ? "Slay one in the wild to learn it"
                : techLocked
                    ? "The core does not yet remember this shape"
                    : rankLocked
                        ? $"Unlocks at {LevelTierUtil.DisplayName(def.RequiredFlatLevel)}"
                        : $"Capacity: {def.CapacityCost}   Mana: {def.ManaCost:0}{MusterLine(def)}";
        if (descriptionLabel != null) descriptionLabel.text = def.description;
    }

    public void OnCloseClicked()
    {
        DungeonBuildController.Instance?.SetMode(BuildMode.None);
        // Hide() fires via HandleModeChanged when the mode leaves PlaceSpawner.
    }

    /// <summary>Where the monster may be placed, appended to the cost line.</summary>
    private static string MusterLine(MonsterDefinition def)
    {
        string rooms = def is BossVariantDefinition
            ? "Boss Room" : MusterRooms.MusterRoomNames(def.category);
        return string.IsNullOrEmpty(rooms) ? "" : $"\nMusters in: {rooms}";
    }

    /// <summary>
    /// True when the def serves the current core's type. Universal defs always
    /// pass; a typed def passes only when the core's DungeonType matches, so a
    /// Fire core never so much as sees the Tide Adept in the picker.
    /// </summary>
    private static bool AffinityAllowed(MonsterDefinition def)
        => def != null
        && def.AffinityMatches(DungeonCore.Instance != null
            ? DungeonCore.Instance.DungeonType : DungeonType.None);

    private void Show()
    {
        if (panel != null) panel.SetActive(true);

        // Snap off a wrong-affinity selection (e.g. after loading a different
        // core's save) and refresh lock states that may have changed while the
        // panel was closed.
        if (registry.All != null && registry.All.Count > 0
            && !AffinityAllowed(registry.All[selectedIndex]))
            OnNextClicked();
        else
            RefreshDisplay();
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }
}
