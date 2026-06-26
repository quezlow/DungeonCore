using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre tabbed action bar.
///
/// THREE FIXED TABS
///   Mine   [M] — direct action, immediately enters BuildMode.Claim (tile-dig/expand).
///   Build  [B] — toggles the Build sub-menu panel above the bar.
///                Sub-menu entries are data-driven; add PlaceTrap, PlaceFurniture,
///                PlaceStairs etc. to the buildEntries list in the Inspector as those
///                systems land — no code changes required.
///   Summon [V] — toggles BuildMode.PlaceSpawner. MonsterSelectionUI already shows/hides
///                itself via its own OnModeChanged subscription, so no direct panel
///                management is needed here.
///
/// STATE OWNERSHIP
///   HandleModeChanged (driven by DungeonBuildController.OnModeChanged) is the single
///   source of truth for currentTab and highlights — it fires synchronously inside
///   every SetMode() call. Tab click handlers override currentTab afterwards only for
///   the "Build submenu open" state, which has no corresponding BuildMode value.
///
/// HIGHLIGHT CONVENTION
///   Active tab / active submenu entry  →  selectedColor  (gold, matches rest of HUD)
///   Inactive                           →  unselectedColor (dim white)
///
/// KEYBOARD SHORTCUTS
///   Shortcuts live here (not in DungeonBuildController) because tab-open state is a
///   UI concern. B and V were confirmed free — DungeonCameraController uses WASD and
///   arrow keys exclusively.
///   M = Mine,  B = Build submenu toggle,  V = Summon toggle,  Esc = back to Mine.
///
/// SCENE SETUP  (see wiring notes at the bottom of this file)
/// </summary>
public class ActionBarHUD : MonoBehaviour
{
    // ── Tab buttons (pre-placed in scene, assigned in Inspector) ──

    [Header("Tab Buttons")]
    [SerializeField] private Button claimTabButton;
    [SerializeField] private Button mineTabButton;
    [SerializeField] private Button buildTabButton;
    [SerializeField] private Button summonTabButton;

    // ── Build sub-menu ────────────────────────────────────────────

    [Header("Build Sub-menu")]
    [Tooltip("Panel that appears above the bar when the Build tab is active. " +
             "Set inactive by default in the scene.")]
    [SerializeField] private GameObject buildSubmenuPanel;

    [Tooltip("Parent transform inside the Build panel with a HorizontalLayoutGroup.")]
    [SerializeField] private Transform buildEntryContainer;

    [Tooltip("Button prefab: Button component + TMP_Text child for the label.")]
    [SerializeField] private Button submenuEntryPrefab;

    [Header("Build Sub-menu Entries")]
    [Tooltip("Add entries here as new BuildMode values are introduced. " +
             "Existing entries: Entrance, Chest. Future: PlaceTrap, PlaceFurniture, PlaceStairs…")]
    [SerializeField]
    private List<BuildSubmenuEntry> buildEntries = new()
    {
        new() { label = "Entrance", mode = BuildMode.PlaceEntrance },
        new() { label = "Chest",    mode = BuildMode.PlaceChest    },
        new() { label = "Furniture", mode = BuildMode.PlaceFurniture },
        new() { label = "Room",      mode = BuildMode.PlaceRoomAnchor },
        new() { label = "Trap", mode = BuildMode.PlaceTrap },
    };

    // ── Colours ───────────────────────────────────────────────────

    [Header("Colours")]
    [SerializeField] private Color selectedColor = new(0.82f, 0.68f, 0.27f, 1.00f); // gold
    [SerializeField] private Color unselectedColor = new(1.00f, 1.00f, 1.00f, 0.55f); // dim white

    // ── Internal state ────────────────────────────────────────────

    private enum ActiveTab { None, Mine, Build, Summon, Claim }
    private ActiveTab currentTab = ActiveTab.None;

    // Spawned entry buttons cached for re-highlighting.
    private readonly List<(BuildMode mode, Button button)> spawnedEntries = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (DungeonBuildController.Instance == null)
        {
            Debug.LogError("ActionBarHUD: DungeonBuildController.Instance is null.");
            return;
        }

        BuildSubmenuEntries();
        HideBuildPanel();

        claimTabButton?.onClick.AddListener(OnClaimTabClicked);
        mineTabButton?.onClick.AddListener(OnMineTabClicked);
        buildTabButton?.onClick.AddListener(OnBuildTabClicked);
        summonTabButton?.onClick.AddListener(OnSummonTabClicked);

        // Keep highlights in sync with any mode change from any source
        // (shortcut, button, or post-placement revert inside BuildController).
        DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;

        // Sync visual state to whatever mode is already active.
        HandleModeChanged(DungeonBuildController.Instance.CurrentMode);

        // Keep the tab shortcut hints (e.g. "MINE (M)") in sync with Keybinds.
        Keybinds.OnRebind += RefreshShortcutLabels;
        RefreshShortcutLabels();
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        Keybinds.OnRebind -= RefreshShortcutLabels;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        if (Keybinds.WasPressed(GameAction.Mine)) OnMineTabClicked();
        if (Keybinds.WasPressed(GameAction.Build)) OnBuildTabClicked();
        if (Keybinds.WasPressed(GameAction.Summon)) OnSummonTabClicked();
        if (Keybinds.WasPressed(GameAction.Claim)) OnClaimTabClicked();

        // Esc (cancel) stays hard-bound.
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) CancelToIdle();
    }

    // ── Tab click handlers ────────────────────────────────────────

    /// <summary>Mine tab: immediate Claim mode, close any open sub-menu.</summary>
    private void OnMineTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideBuildPanel();
        DungeonBuildController.Instance.SetMode(BuildMode.Mine);

        // SetMode is a no-op if already Mine (HandleModeChanged won't fire),
        // so force the visual state explicitly as a fallback.
        currentTab = ActiveTab.Mine;
        UpdateTabHighlights();
    }

    private void OnClaimTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideBuildPanel();
        DungeonBuildController.Instance.SetMode(BuildMode.Claim);

        // SetMode is a no-op if already Claim (HandleModeChanged won't fire),
        // so force the visual state explicitly as a fallback.
        currentTab = ActiveTab.Claim;
        UpdateTabHighlights();
    }

    /// <summary>Build tab: toggle the Build sub-menu. Entering Claim first clears any
    /// active placement mode (e.g. PlaceSpawner) so mode state stays clean.</summary>
    private void OnBuildTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        bool wasOpen = currentTab == ActiveTab.Build;

        // Step 1 — clear any placement mode. If already Claim this is a no-op and
        //          HandleModeChanged will NOT fire, so currentTab is unchanged here.
        DungeonBuildController.Instance.SetMode(BuildMode.Claim);

        // Step 2 — close the panel regardless (re-opened below if toggling on).
        HideBuildPanel();

        if (!wasOpen)
        {
            // Toggle ON — override currentTab to Build (may overwrite Mine set by
            // HandleModeChanged above if mode actually changed in Step 1).
            currentTab = ActiveTab.Build;
            UpdateTabHighlights();
            ShowBuildPanel();
        }
        else
        {
            currentTab = ActiveTab.Claim;
            UpdateTabHighlights();
        }
    }

    /// <summary>Summon tab: toggle PlaceSpawner. MonsterSelectionUI reacts automatically.</summary>
    private void OnSummonTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        bool wasOpen = currentTab == ActiveTab.Summon;

        HideBuildPanel(); // close Build panel if it happened to be open

        if (!wasOpen)
            DungeonBuildController.Instance.SetMode(BuildMode.PlaceSpawner);
        else
            DungeonBuildController.Instance.SetMode(BuildMode.Claim);

        // HandleModeChanged fires synchronously inside SetMode and sets currentTab +
        // calls UpdateTabHighlights — nothing more needed here.
    }

    /// <summary>Esc: cancel any active mode and return to idle (nothing highlighted).</summary>
    private void CancelToIdle()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideBuildPanel();
        DungeonBuildController.Instance.SetMode(BuildMode.Claim);
        // PHASE 5 — Claim is the new "idle" active state; SetMode above triggers
        // HandleModeChanged which sets currentTab = ActiveTab.Claim. No fallback
        // override needed here, but kept for symmetry with the tab-click handlers.
        currentTab = ActiveTab.Claim;
        UpdateTabHighlights();
    }

    // ── Mode sync ─────────────────────────────────────────────────

    /// <summary>
    /// Fires on every BuildController mode change (including post-placement Claim revert).
    /// This is the single authoritative writer of currentTab for all non-Build-submenu states.
    /// </summary>
    private void HandleModeChanged(BuildMode mode)
    {
        switch (mode)
        {
            case BuildMode.Claim:
                // PHASE 5 — Claim is now its own active mode (formerly the idle default).
                // Post-placement revert lands here; Claim tab gets highlighted.
                currentTab = ActiveTab.Claim;
                HideBuildPanel();
                break;

            case BuildMode.Mine:                                    
                currentTab = ActiveTab.Mine;
                HideBuildPanel();
                break;

            case BuildMode.PlaceSpawner:
                currentTab = ActiveTab.Summon;
                HideBuildPanel();
                break;

            case BuildMode.PlaceEntrance:
            case BuildMode.PlaceChest:
                // Launched from the Build sub-menu — keep Build tab lit.
                // The panel was already closed by OnSubmenuEntryClicked.
                currentTab = ActiveTab.Build;
                break;

            // Future Build sub-menu modes (PlaceTrap, PlaceFurniture, etc.) should
            // be added here with the same pattern as PlaceEntrance/PlaceChest.
            case BuildMode.PlaceFurniture:
            case BuildMode.PlaceRoomAnchor:
            case BuildMode.PlaceTrap:
                currentTab = ActiveTab.Build;
                break;

        }

        UpdateTabHighlights();
        UpdateSubmenuHighlights(mode);
    }

    // ── Build sub-menu construction ───────────────────────────────

    private void BuildSubmenuEntries()
    {
        if (buildEntryContainer == null || submenuEntryPrefab == null)
        {
            Debug.LogWarning("ActionBarHUD: buildEntryContainer or submenuEntryPrefab not assigned. " +
                             "Build sub-menu will be empty.");
            return;
        }

        foreach (var entry in buildEntries)
        {
            Button btn = Instantiate(submenuEntryPrefab, buildEntryContainer);
            btn.gameObject.SetActive(true);

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = entry.label;

            if (entry.icon != null)
            {
                // If the entry has an icon sprite, apply it to the button's Image.
                // Relies on a second Image child being present — skip gracefully if absent.
                var images = btn.GetComponentsInChildren<Image>();
                if (images.Length > 1) images[1].sprite = entry.icon;
            }

            BuildMode captured = entry.mode; // avoid closure capture bug
            btn.onClick.AddListener(() => OnSubmenuEntryClicked(captured));

            spawnedEntries.Add((entry.mode, btn));
        }
    }

    private void OnSubmenuEntryClicked(BuildMode mode)
    {
        SpawnerSelectionController.Instance?.Deselect();

        // Close the panel immediately — the player's next click is a placement click,
        // not a further sub-menu interaction.
        HideBuildPanel();

        // currentTab is set to Build in HandleModeChanged for these modes.
        DungeonBuildController.Instance.SetMode(mode);
    }

    // ── Build panel visibility ────────────────────────────────────

    private void ShowBuildPanel()
    {
        if (buildSubmenuPanel != null) buildSubmenuPanel.SetActive(true);
    }

    private void HideBuildPanel()
    {
        if (buildSubmenuPanel != null) buildSubmenuPanel.SetActive(false);
    }

    // ── Highlight helpers ─────────────────────────────────────────

    private void RefreshShortcutLabels()
    {
        SetTabLabel(claimTabButton, "CLAIM", GameAction.Claim);
        SetTabLabel(mineTabButton, "MINE", GameAction.Mine);
        SetTabLabel(buildTabButton, "BUILD", GameAction.Build);
        SetTabLabel(summonTabButton, "SUMMON", GameAction.Summon);
    }

    private void SetTabLabel(Button btn, string label, GameAction action)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = $"{label} ({Keybinds.DisplayName(action)})";
    }

    private void UpdateTabHighlights()
    {
        SetButtonColor(claimTabButton, currentTab == ActiveTab.Claim);
        SetButtonColor(mineTabButton, currentTab == ActiveTab.Mine);
        SetButtonColor(buildTabButton, currentTab == ActiveTab.Build);
        SetButtonColor(summonTabButton, currentTab == ActiveTab.Summon);
    }

    private void UpdateSubmenuHighlights(BuildMode activeMode)
    {
        foreach (var (mode, btn) in spawnedEntries)
            SetButtonColor(btn, mode == activeMode);
    }

    private void SetButtonColor(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? selectedColor : unselectedColor;
    }
}

// ── Data ──────────────────────────────────────────────────────────────────────

[Serializable]
public class BuildSubmenuEntry
{
    [Tooltip("Label shown on the sub-menu button.")]
    public string label;

    [Tooltip("Optional icon sprite. Leave null to show label-only.")]
    public Sprite icon;

    [Tooltip("The BuildMode this entry activates.")]
    public BuildMode mode;
}

/*
 * ── SCENE WIRING NOTES (Day 19) ─────────────────────────────────────────────
 *
 * All work goes inside the ActionBar container reserved in Day 18 (bottom-centre,
 * ~600×72 RectTransform). No other scene objects need editing.
 *
 * 1. TAB BUTTONS
 *    Under ActionBar, add a HorizontalLayoutGroup child named "TabRow".
 *    Create three Button children: MineTab, BuildTab, SummonTab.
 *    Each needs a TMP_Text child (label) and optionally a hotkey-hint TMP_Text.
 *    Label text: "Mine [M]", "Build [B]", "Summon [V]".
 *
 * 2. BUILD SUB-MENU PANEL
 *    Directly above the ActionBar (anchored just above it, e.g. y offset +80),
 *    add a panel named "BuildSubmenu" with a HorizontalLayoutGroup.
 *    Set it inactive (☐ checked off in the Inspector) by default.
 *    Entries are instantiated at runtime from submenuEntryPrefab.
 *
 * 3. SUBMENU ENTRY PREFAB
 *    Create a Button prefab with a TMP_Text child. Keep it in your prefabs folder.
 *    Do not add it as a scene instance — ActionBarHUD.BuildSubmenuEntries() instantiates it.
 *
 * 4. ACTIONBARHUD COMPONENT
 *    Add ActionBarHUD to the ActionBar root (or any persistent manager object).
 *    Wire Inspector fields:
 *      Mine Tab Button   → MineTab button
 *      Build Tab Button  → BuildTab button
 *      Summon Tab Button → SummonTab button
 *      Build Submenu Panel → BuildSubmenu panel
 *      Build Entry Container → BuildSubmenu's layout child (or the panel itself)
 *      Submenu Entry Prefab  → your button prefab
 *    buildEntries is pre-populated with Entrance and Chest — add more as systems land.
 *
 * 5. CLEAN UP TEMP BUTTONS
 *    Remove the "PlaceSpawner_TEMP" and any other prototype-era mode buttons from
 *    CoreStatsPanel — they're superseded by the action bar.
 *
 * 6. MONSTERSELECTIONUI
 *    No changes. It already shows/hides via its own OnModeChanged subscription.
 *    Position it above the ActionBar (or wherever it currently sits) — it will
 *    appear/disappear correctly when the Summon tab is toggled.
 * ─────────────────────────────────────────────────────────────────────────────
 */