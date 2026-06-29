using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Selection panel for placing a trap. Cycles trap types with Prev/Next, plus any
/// trap-variant chests (isTrapChest) listed after the traps — those place as chests.
/// Shown when BuildMode switches to PlaceTrap; hidden otherwise.
/// </summary>
public class TrapSelectionUI : MonoBehaviour
{
    public static TrapSelectionUI Instance { get; private set; }

    [Header("Available Trap Types")]
    [SerializeField] private TrapDefinitionRegistry registry;
    [Tooltip("Trap-variant chests (isTrapChest) are listed here too, after the traps.")]
    [SerializeField] private ChestDefinitionRegistry chestRegistry;

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Image trapIcon;
    [SerializeField] private TMP_Text trapNameLabel;
    [SerializeField] private Image manaCostIcon;
    [SerializeField] private TMP_Text manaCostLabel;
    [SerializeField] private TMP_Text descriptionLabel;
    [SerializeField] private TMP_Text statsLabel;

    [Header("Affordability Colours")]
    [SerializeField] private Color affordableColor = new(0.40f, 0.70f, 1.00f, 1f);
    [SerializeField] private Color unaffordableColor = new(0.90f, 0.30f, 0.30f, 1f);

    // An entry is either a trap OR a trap-chest (exactly one is set).
    private struct Entry { public TrapDefinition trap; public ChestDefinition chest; public bool IsChest => chest != null; }
    private readonly List<Entry> entries = new();
    private int selectedIndex = 0;

    private Entry Current => entries.Count > 0
        ? entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)]
        : default;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void Start()
    {
        BuildEntries();

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

    private void BuildEntries()
    {
        entries.Clear();
        if (registry != null && registry.All != null)
            foreach (var t in registry.All)
                if (t != null) entries.Add(new Entry { trap = t });
        if (chestRegistry != null && chestRegistry.All != null)
            foreach (var c in chestRegistry.All)
                if (c != null && c.isTrapChest) entries.Add(new Entry { chest = c });
    }

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.PlaceTrap) { Show(); PushSelectionToBuildController(); }
        else Hide();
    }

    private void HandleManaChanged(float currentMana, float maxMana) => UpdateAffordabilityVisual();

    public void OnPrevClicked()
    {
        if (entries.Count == 0) return;
        selectedIndex = (selectedIndex - 1 + entries.Count) % entries.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    public void OnNextClicked()
    {
        if (entries.Count == 0) return;
        selectedIndex = (selectedIndex + 1) % entries.Count;
        RefreshDisplay();
        PushSelectionToBuildController();
    }

    private void RefreshDisplay()
    {
        var e = Current;
        Sprite icon; string label; float cost; string desc; List<string> stats;
        if (e.IsChest)
        {
            if (e.chest == null) return;
            icon = e.chest.icon; label = e.chest.chestName; cost = e.chest.manaCost;
            desc = e.chest.description; stats = e.chest.GetStatLines();
        }
        else
        {
            if (e.trap == null) return;
            icon = e.trap.icon; label = e.trap.trapName; cost = e.trap.manaCost;
            desc = e.trap.description; stats = e.trap.GetStatLines();
        }

        if (trapIcon != null) trapIcon.sprite = icon;
        if (trapNameLabel != null) trapNameLabel.text = label;
        if (manaCostLabel != null) manaCostLabel.text = cost.ToString("0");
        if (descriptionLabel != null) descriptionLabel.text = desc;
        if (statsLabel != null) statsLabel.text = string.Join("\n", stats);

        UpdateAffordabilityVisual();
    }

    private void UpdateAffordabilityVisual()
    {
        var e = Current;
        float cost = e.IsChest ? (e.chest != null ? e.chest.manaCost : 0f)
                               : (e.trap != null ? e.trap.manaCost : 0f);
        bool canAfford = DungeonCore.Instance != null && DungeonCore.Instance.CurrentMana >= cost;
        Color tint = canAfford ? affordableColor : unaffordableColor;
        if (manaCostIcon != null) manaCostIcon.color = tint;
        if (manaCostLabel != null) manaCostLabel.color = tint;
    }

    public void OnCloseClicked() => DungeonBuildController.Instance?.SetMode(BuildMode.Claim);

    private void PushSelectionToBuildController()
    {
        var e = Current;
        var bc = DungeonBuildController.Instance;
        if (bc == null) return;
        if (e.IsChest) { bc.SetSelectedChest(e.chest); bc.SetSelectedTrap(null); }
        else { bc.SetSelectedTrap(e.trap); bc.SetSelectedChest(null); }
    }

    private void Show() { if (panel != null) panel.SetActive(true); }
    private void Hide() { if (panel != null) panel.SetActive(false); }
}