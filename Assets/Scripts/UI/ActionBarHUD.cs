using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Bottom-centre tabbed action bar.
///
/// FOUR FIXED TABS
///   Push   [C] — enters BuildMode.Push: hold LMB to channel influence toward the
///                cursor (constant mana/sec; terrain resistance sets creep speed).
///   Mine   [M] — enters BuildMode.Mine (tile digging / dig queue).
///   Build  [B] — toggles the Build sub-menu panel above the bar.
///                Sub-menu entries are data-driven; add PlaceTrap, PlaceFurniture,
///                PlaceStairs etc. to the buildEntries list in the Inspector as those
///                systems land — no code changes required.
///   Summon [V] — toggles BuildMode.PlaceSpawner. MonsterSelectionUI already shows/hides
///                itself via its own OnModeChanged subscription, so no direct panel
///                management is needed here.
///   Cast   [Q] — toggles the spell row (canon 38). A FIFTH tab, cloned from a sibling
///                at runtime, hidden until the core holds any working. Its row is a
///                third sub-menu built exactly like Mine's, off the same entry prefab.
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
///   M = Mine,  B = Build submenu toggle,  V = Summon toggle,  C = Push,
///   Esc = cancel to idle.
///
/// SCENE SETUP  (see wiring notes at the bottom of this file)
/// </summary>
public class ActionBarHUD : MonoBehaviour
{
    // ── Tab buttons (pre-placed in scene, assigned in Inspector) ──

    [Header("Tab Buttons")]
    [FormerlySerializedAs("claimTabButton")]
    [SerializeField] private Button pushTabButton;
    [SerializeField] private Button mineTabButton;
    [SerializeField] private Button buildTabButton;
    [SerializeField] private Button summonTabButton;

    // Not serialized: cloned from a sibling at runtime by EnsureCastTab.
    private Button castTabButton;

    // ── Build sub-menu ────────────────────────────────────────────

    [Header("Build Sub-menu")]
    [Tooltip("Panel that appears above the bar when the Build tab is active. " +
             "Set inactive by default in the scene.")]
    [SerializeField] private GameObject buildSubmenuPanel;

    [Tooltip("Parent transform inside the Build panel with a HorizontalLayoutGroup.")]
    [SerializeField] private Transform buildEntryContainer;
    [Tooltip("Container for the Mine gesture sub-menu (Single / Drag / Box). Entries are " +
             "instantiated at runtime from submenuEntryPrefab, exactly like the Build sub-menu.")]
    [SerializeField] private Transform mineEntryContainer;

    [Tooltip("Container for the spell row (canon 38). Same shape as mineEntryContainer: " +
             "a HorizontalLayoutGroup, inactive by default. Entries come from the shared " +
             "submenuEntryPrefab, so the row matches Build and Mine without extra art.")]
    [SerializeField] private Transform spellEntryContainer;

    [Tooltip("Optional. Description and stats for the working under the POINTER; hidden " +
             "when nothing is hovered, so the row stays a clean strip of names.")]
    [SerializeField] private TMP_Text spellDetailLabel;

    [Tooltip("Button prefab: Button component + TMP_Text child for the label.")]
    [SerializeField] private Button submenuEntryPrefab;

    [Header("Build Sub-menu Entries")]
    [Tooltip("Add entries here as new BuildMode values are introduced. " +
             "Existing entries: Entrance, Chest. Future: PlaceTrap, PlaceFurniture, PlaceStairs…")]
    [SerializeField]
    private List<BuildSubmenuEntry> buildEntries = new()
    {
        new() { label = "Chest",    mode = BuildMode.PlaceChest    },
        new() { label = "Furniture", mode = BuildMode.PlaceFurniture },
        new() { label = "Room",      mode = BuildMode.PlaceRoomAnchor },
        new() { label = "Trap", mode = BuildMode.PlaceTrap },
    };

    // ── Colours ───────────────────────────────────────────────────

    [Header("Colours")]
    [SerializeField] private Color selectedColor = new(0.82f, 0.68f, 0.27f, 1.00f); // gold
    [SerializeField] private Color unselectedColor = new(1.00f, 1.00f, 1.00f, 0.55f); // dim white
    [Tooltip("Spell entry tint when the core cannot pay for it.")]
    [SerializeField] private Color spellUnaffordableColor = new(0.90f, 0.30f, 0.30f, 1.00f);

    // ── Internal state ────────────────────────────────────────────

    private enum ActiveTab { None, Mine, Build, Summon, Push, Cast }
    private ActiveTab currentTab = ActiveTab.None;

    /// <summary>Frame on which Esc was consumed as a cancel. The pause menu checks this
    /// so it doesn't also open on the same Esc press (order-safe across scripts).</summary>
    public static int LastCancelFrame { get; private set; } = -1;

    // Spawned entry buttons cached for re-highlighting.
    private readonly List<(BuildMode mode, Button button)> spawnedEntries = new();

    // The spell row, rebuilt whenever the castable roster changes.
    private readonly List<(SpellDefinition def, Button button)> spellEntries = new();
    private int selectedSpellIndex;
    private float spellRefreshTimer;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (DungeonBuildController.Instance == null)
        {
            Debug.LogError("ActionBarHUD: DungeonBuildController.Instance is null.");
            return;
        }

        // The scene's serialised copy of buildEntries predates the Wall entry,
        // and a code-default list never reaches an already-serialised scene --
        // so the entry is appended at runtime when absent rather than asking
        // for a hand edit in the Inspector (the silent-manual-step failure
        // mode: forget it once and the feature simply is not on the bar).
        bool hasWallEntry = false;
        for (int i = 0; i < buildEntries.Count; i++)
            if (buildEntries[i] != null && buildEntries[i].mode == BuildMode.BuildWall) { hasWallEntry = true; break; }
        if (!hasWallEntry)
            buildEntries.Add(new BuildSubmenuEntry { label = "Wall", mode = BuildMode.BuildWall });

        BuildSubmenuEntries();
        BuildMineSubmenuEntries();   // without this the mine sub-menu opens empty
        BuildSpellSubmenuEntries();
        HideBuildPanel();
        HideMinePanel();
        HideSpellPanel();

        pushTabButton?.onClick.AddListener(OnPushTabClicked);
        mineTabButton?.onClick.AddListener(OnMineTabClicked);
        buildTabButton?.onClick.AddListener(OnBuildTabClicked);
        summonTabButton?.onClick.AddListener(OnSummonTabClicked);

        // Keep highlights in sync with any mode change from any source
        // (shortcut, button, or post-placement revert inside BuildController).
        DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;

        // Sync visual state to whatever mode is already active.
        HandleModeChanged(DungeonBuildController.Instance.CurrentMode);

        EnsureCastTab();
        SpellBook.OnRosterChanged += HandleSpellRosterChanged;
        RefreshCastTabVisibility();

        // Keep the tab shortcut hints (e.g. "MINE (M)") in sync with Keybinds.
        Keybinds.OnRebind += RefreshShortcutLabels;
        RefreshShortcutLabels();
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        Keybinds.OnRebind -= RefreshShortcutLabels;
        SpellBook.OnRosterChanged -= HandleSpellRosterChanged;
    }

    private void Update()
    {
        if (NameDialog.IsOpen || WarningTrapNameDialog.IsOpen) return;

        // Cast mode may be ENTERED while the world is held: the picker and the
        // radius ghost are read-only, and Call to Arms is an order, which pause
        // has always permitted. The cast itself is gated per spell inside
        // DungeonBuildController.HandleSpellCast (canon 38). This is why the
        // dialog guard now runs FIRST -- typing must still swallow the key.
        if (Keybinds.WasPressed(GameAction.Cast)) OnCastTabClicked();
        HandleSpellNumberKeys();

        // Cooldowns and mana both move on their own, and the row stays open while
        // the world is held -- so this repaint sits ABOVE the pause gate and runs
        // on unscaled time. A scaled timer would freeze the readout at exactly the
        // moment the player is reading it to plan the next cast.
        if (SpellPanelOpen)
        {
            spellRefreshTimer -= Time.unscaledDeltaTime;
            if (spellRefreshTimer <= 0f) { spellRefreshTimer = 0.25f; RefreshSpellEntries(); }
        }

        if (PauseController.IsGamePaused) return;

        if (Keybinds.WasPressed(GameAction.Mine)) OnMineTabClicked();
        if (Keybinds.WasPressed(GameAction.Build)) OnBuildTabClicked();
        if (Keybinds.WasPressed(GameAction.Summon)) OnSummonTabClicked();
        if (Keybinds.WasPressed(GameAction.Push)) OnPushTabClicked();

        // Esc (cancel) stays hard-bound. Only treat it as a cancel when a tool is
        // active or something is selected; otherwise leave Esc for the pause menu.
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            var build = DungeonBuildController.Instance;
            bool somethingToCancel =
                (build != null && build.CurrentMode != BuildMode.None)
                || (SpawnerSelectionController.Instance != null
                    && SpawnerSelectionController.Instance.CurrentSelected != null);
            if (somethingToCancel)
            {
                CancelToIdle();
                LastCancelFrame = Time.frameCount;
            }
        }
    }

    // ── Tab click handlers ────────────────────────────────────────

    /// <summary>Mine tab: enter Mine mode, close any open sub-menu.</summary>
    private void OnMineTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();

        // Mirrors OnBuildTabClicked. Whether the sub-menu is open -- not which tab is
        // lit -- is the toggle state, because Mine can be entered by hotkey or by a
        // post-placement revert without the panel ever having opened.
        bool wasOpen = MinePanelOpen;

        HideBuildPanel();
        HideMinePanel();

        if (wasOpen)
        {
            // Toggle OFF: leave mine mode entirely, matching Build's second click.
            DungeonBuildController.Instance.SetMode(BuildMode.None);
            currentTab = ActiveTab.None;
            UpdateTabHighlights();
            return;
        }

        DungeonBuildController.Instance.SetMode(BuildMode.Mine);

        // SetMode is a no-op if already Mine (HandleModeChanged won't fire),
        // so force the visual state explicitly as a fallback.
        currentTab = ActiveTab.Mine;
        UpdateTabHighlights();
        ShowMinePanel();
    }

    /// <summary>Builds the three mine-gesture entries. Mirrors BuildSubmenuEntries;
    /// the selected gesture is remembered in PlayerPrefs by the build controller, so
    /// the sub-menu only has to reflect it.</summary>
    private void BuildMineSubmenuEntries()
    {
        if (mineEntryContainer == null || submenuEntryPrefab == null) return;

        var gestures = new[]
        {
            (DungeonBuildController.MineGesture.Single, "Single"),
            (DungeonBuildController.MineGesture.Drag,   "Drag"),
            (DungeonBuildController.MineGesture.Box,    "Box"),
        };

        foreach (var (gesture, label) in gestures)
        {
            Button btn = Instantiate(submenuEntryPrefab, mineEntryContainer);
            btn.gameObject.SetActive(true);

            var text = btn.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;

            var captured = gesture;
            btn.onClick.AddListener(() =>
            {
                DungeonBuildController.SetMineGesture(captured);
                DungeonBuildController.Instance?.SetMode(BuildMode.Mine);
                currentTab = ActiveTab.Mine;
                UpdateTabHighlights();
                HideMinePanel();
            });
        }

        HideMinePanel();
    }

    private void OnPushTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideBuildPanel();
        HideMinePanel();
        DungeonBuildController.Instance.SetMode(BuildMode.Push);

        // SetMode is a no-op if already Push (HandleModeChanged won't fire),
        // so force the visual state explicitly as a fallback.
        currentTab = ActiveTab.Push;
        UpdateTabHighlights();
    }

    /// <summary>Build tab: toggle the Build sub-menu. Dropping to idle (None) first
    /// clears any active placement mode (e.g. PlaceSpawner) so mode state stays clean.</summary>
    private void OnBuildTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideMinePanel();
        bool wasOpen = currentTab == ActiveTab.Build;

        // Step 1 — clear any placement mode back to idle. If already None this is a
        //          no-op and HandleModeChanged will NOT fire, so currentTab is unchanged.
        DungeonBuildController.Instance.SetMode(BuildMode.None);

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
            currentTab = ActiveTab.None;
            UpdateTabHighlights();
        }
    }

    /// <summary>Summon tab: toggle PlaceSpawner. MonsterSelectionUI reacts automatically.</summary>
    private void OnSummonTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        bool wasOpen = currentTab == ActiveTab.Summon;

        HideBuildPanel(); // close Build panel if it happened to be open
        HideMinePanel();

        if (!wasOpen)
            DungeonBuildController.Instance.SetMode(BuildMode.PlaceSpawner);
        else
            DungeonBuildController.Instance.SetMode(BuildMode.None);

        // HandleModeChanged fires synchronously inside SetMode and sets currentTab +
        // calls UpdateTabHighlights — nothing more needed here.
    }

    /// <summary>Esc: cancel any active mode and return to idle (nothing highlighted).</summary>
    private void CancelToIdle()
    {
        SpawnerSelectionController.Instance?.Deselect();
        HideBuildPanel();
        HideMinePanel();   // Esc closes the gesture sub-menu with everything else
        DungeonBuildController.Instance.SetMode(BuildMode.None);
        // PHASE 5+ — None is the idle select-and-command state; SetMode above triggers
        // HandleModeChanged, which sets currentTab = ActiveTab.None (no tab lit).
        currentTab = ActiveTab.None;
        UpdateTabHighlights();
    }

    // ── Mode sync ─────────────────────────────────────────────────

    /// <summary>
    /// Fires on every BuildController mode change (including post-placement Claim revert).
    /// This is the single authoritative writer of currentTab for all non-Build-submenu states.
    /// </summary>
    private void HandleModeChanged(BuildMode mode)
    {
        // The spell row closes on ANY mode that is not CastSpell. Done here rather
        // than in each arm because HandleModeChanged is the single source of truth
        // for panel state, and a new BuildMode added later would otherwise leave
        // the row hanging open over an unrelated tool.
        if (mode != BuildMode.CastSpell) HideSpellPanel();

        switch (mode)
        {
            case BuildMode.None:
                currentTab = ActiveTab.None;   // idle: select & command, no tab lit
                HideBuildPanel();
                HideMinePanel();
                break;

            case BuildMode.Push:
                // Push is the influence channel (formerly Claim). Panels now close
                // to None, so only deliberate tab/hotkey entry lands here.
                currentTab = ActiveTab.Push;
                HideBuildPanel();
                HideMinePanel();
                break;

            case BuildMode.Mine:                                    
                currentTab = ActiveTab.Mine;
                HideBuildPanel();
                break;

            case BuildMode.PlaceSpawner:
                currentTab = ActiveTab.Summon;
                HideBuildPanel();
                HideMinePanel();
                break;

            case BuildMode.CastSpell:
                currentTab = ActiveTab.Cast;
                HideBuildPanel();
                HideMinePanel();
                ShowSpellPanel();
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
            case BuildMode.Demolish:
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
            if (!EntryAvailable(entry.mode)) continue;   // hide stairs/relocate until possible

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
        HideMinePanel();

        // currentTab is set to Build in HandleModeChanged for these modes.
        DungeonBuildController.Instance.SetMode(mode);
    }

    // ── Build panel visibility ────────────────────────────────────

    private void ShowBuildPanel()
    {
        // Rebuild each open so availability re-checks: stairs and relocate only
        // appear when they are actually possible.
        RebuildBuildEntries();
        if (buildSubmenuPanel != null) buildSubmenuPanel.SetActive(true);
    }

    // True when a build entry should be offered right now.
    private static bool EntryAvailable(BuildMode mode)
    {
        var core = DungeonCore.Instance;
        var fm = FloorManager.Instance;
        switch (mode)
        {
            case BuildMode.PlaceStairs:
                return core != null && core.StairCredits > 0
                    && fm != null && !fm.FloorHasDownStair(fm.ActiveFloorIndex);
            case BuildMode.PlaceCore:
                return fm != null && fm.CanPlaceCore;
            default:
                return true;   // always-available entries
        }
    }

    private void RebuildBuildEntries()
    {
        foreach (var (_, btn) in spawnedEntries)
            if (btn != null) Destroy(btn.gameObject);
        spawnedEntries.Clear();
        BuildSubmenuEntries();
    }

    private void HideBuildPanel()
    {
        if (buildSubmenuPanel != null) buildSubmenuPanel.SetActive(false);
    }

    // -- Spell row (canon 38) --------------------------------------

    /// <summary>
    /// Rebuilds the spell row from SpellBook. Mirrors BuildMineSubmenuEntries,
    /// with one difference that matters: the mine gestures are a fixed three and
    /// are built once, whereas the castable roster GROWS at runtime as research
    /// completes and gods hand workings down -- so this is also wired to
    /// SpellBook.OnRosterChanged and destroys before it rebuilds.
    /// </summary>
    private void BuildSpellSubmenuEntries()
    {
        if (spellEntryContainer == null || submenuEntryPrefab == null) return;

        for (int i = 0; i < spellEntries.Count; i++)
            if (spellEntries[i].button != null) Destroy(spellEntries[i].button.gameObject);
        spellEntries.Clear();

        var defs = new List<SpellDefinition>();
        SpellBook.FillAvailable(defs);

        for (int i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            if (def == null) continue;

            Button btn = Instantiate(submenuEntryPrefab, spellEntryContainer);
            btn.gameObject.SetActive(true);
            btn.name = "SpellEntry_" + def.id;

            if (def.icon != null)
            {
                var images = btn.GetComponentsInChildren<Image>(true);
                if (images.Length > 1) images[1].sprite = def.icon;
            }

            int captured = i;                      // avoid the closure capture bug
            btn.onClick.AddListener(() => SelectSpell(captured));
            AddSpellHover(btn, captured);

            spellEntries.Add((def, btn));
        }

        if (selectedSpellIndex >= spellEntries.Count) selectedSpellIndex = 0;
        RefreshSpellEntries();
        PushSelectedSpell();
    }

    /// <summary>
    /// Pointer enter/exit on one entry, driving the detail label. An EventTrigger
    /// is added at runtime rather than required on the shared prefab, because
    /// that prefab is also the Build and Mine entry and must stay as it is.
    /// </summary>
    private void AddSpellHover(Button btn, int index)
    {
        var trigger = btn.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btn.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowSpellDetail(index));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideSpellDetail());
        trigger.triggers.Add(exit);
    }

    /// <summary>The detail label follows the POINTER, not the selection: it appears
    /// on hover and goes away on exit, so the row stays a clean strip of names when
    /// nothing is being inspected.</summary>
    private void ShowSpellDetail(int index)
    {
        if (spellDetailLabel == null) return;
        if (index < 0 || index >= spellEntries.Count) return;
        var def = spellEntries[index].def;
        if (def == null) return;
        spellDetailLabel.text = def.description + "\n" + def.StatLine();
        spellDetailLabel.gameObject.SetActive(true);
    }

    private void HideSpellDetail()
    {
        if (spellDetailLabel != null) spellDetailLabel.gameObject.SetActive(false);
    }

    private void SelectSpell(int index)
    {
        if (spellEntries.Count == 0) return;
        selectedSpellIndex = Mathf.Clamp(index, 0, spellEntries.Count - 1);
        RefreshSpellEntries();
        PushSelectedSpell();
    }

    private void PushSelectedSpell()
    {
        SpellDefinition def = selectedSpellIndex >= 0 && selectedSpellIndex < spellEntries.Count
            ? spellEntries[selectedSpellIndex].def : null;
        DungeonBuildController.Instance?.SetSelectedSpell(def);
    }

    /// <summary>
    /// Labels, tints and cooldown readouts. Called on a timer rather than per
    /// frame, and on UNSCALED time -- the row stays open while the world is held,
    /// so a scaled timer would freeze the readout at exactly the moment the
    /// player is reading it to plan.
    /// </summary>
    private void RefreshSpellEntries()
    {
        float mana = DungeonCore.Instance != null ? DungeonCore.Instance.CurrentMana : 0f;

        for (int i = 0; i < spellEntries.Count; i++)
        {
            var def = spellEntries[i].def;
            var btn = spellEntries[i].button;
            if (def == null || btn == null) continue;

            bool ready = SpellBook.IsReady(def);
            bool afford = mana >= def.manaCost;
            float left = SpellBook.CooldownRemaining(def);

            var img = btn.targetGraphic as Image;
            if (img == null) img = btn.GetComponent<Image>();
            if (img != null)
            {
                Color c = !afford ? spellUnaffordableColor
                        : (i == selectedSpellIndex ? selectedColor : unselectedColor);
                if (!ready) c.a *= 0.4f;
                img.color = c;
            }

            var text = btn.GetComponentInChildren<TMP_Text>();
            if (text == null) continue;

            // "(1)" matches the tab convention ("MINE (M)") so the number key is
            // read the same way everywhere on the bar. Only the first nine get
            // one -- there is no tenth key to press.
            string slot = i < 9 ? " (" + (i + 1) + ")" : string.Empty;
            text.text = left > 0.05f
                ? def.displayName + slot + "  " + left.ToString("0.#") + "s"
                : def.displayName + slot + "  " + def.manaCost.ToString("0");
        }
    }

    /// <summary>Number keys select while the row is open. Guarded on the row being
    /// open so the digits stay free everywhere else -- HotbarController also reads
    /// Digit1-9, and although it is not in the dungeon scene today, a future one
    /// must not fight this.</summary>
    private void HandleSpellNumberKeys()
    {
        if (!SpellPanelOpen || spellEntries.Count == 0) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        int n = Mathf.Min(9, spellEntries.Count);
        for (int i = 0; i < n; i++)
            if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { SelectSpell(i); return; }
    }

    private void HideSpellPanel()
    {
        if (spellEntryContainer != null) spellEntryContainer.gameObject.SetActive(false);
        HideSpellDetail();
    }

    private void ShowSpellPanel()
    {
        if (spellEntryContainer != null) spellEntryContainer.gameObject.SetActive(true);
        RefreshSpellEntries();
    }

    private bool SpellPanelOpen =>
        spellEntryContainer != null && spellEntryContainer.gameObject.activeSelf;

    /// <summary>Names every unassigned slot the spell row needs. A container left
    /// blank opens an empty row and reports nothing on its own, which is the
    /// failure this project has paid test cycles for. Null when whole.</summary>
    public string ValidateSpellRowWiring()
    {
        var faults = new List<string>();
        if (spellEntryContainer == null)
            faults.Add("spellEntryContainer is not assigned -- the spell row can never show.");
        if (submenuEntryPrefab == null)
            faults.Add("submenuEntryPrefab is not assigned -- no entries can be made.");
        else if (submenuEntryPrefab.GetComponentInChildren<TMP_Text>(true) == null)
            faults.Add("submenuEntryPrefab has no TMP_Text child -- entries will be unlabelled.");
        if (spellDetailLabel == null)
            faults.Add("spellDetailLabel is not assigned (optional -- hover shows nothing).");

        if (faults.Count == 0) return null;
        return "[ActionBarHUD] spell row wiring incomplete on '" + name + "':\n  "
             + string.Join("\n  ", faults);
    }

    private void HideMinePanel()
    {
        if (mineEntryContainer != null) mineEntryContainer.gameObject.SetActive(false);
    }

    private void ShowMinePanel()
    {
        if (mineEntryContainer != null) mineEntryContainer.gameObject.SetActive(true);
    }

    private bool MinePanelOpen =>
        mineEntryContainer != null && mineEntryContainer.gameObject.activeSelf;

    // ── Highlight helpers ─────────────────────────────────────────

    /// <summary>Toggles the spell row, mirroring the Mine tab exactly: whether the
    /// ROW is open is the toggle state, not which tab is lit, because cast mode can
    /// be entered by hotkey without the row ever having opened.</summary>
    private void OnCastTabClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
        bool wasOpen = SpellPanelOpen;

        HideBuildPanel();
        HideMinePanel();

        if (wasOpen)
        {
            DungeonBuildController.Instance.SetMode(BuildMode.None);
            currentTab = ActiveTab.None;
            UpdateTabHighlights();
            return;
        }

        DungeonBuildController.Instance.SetMode(BuildMode.CastSpell);

        // SetMode is a no-op if already CastSpell (HandleModeChanged will not fire),
        // so force the visual state explicitly, as the Mine tab does.
        currentTab = ActiveTab.Cast;
        UpdateTabHighlights();
        ShowSpellPanel();
    }

    /// <summary>A completed node or a god's grant changes the roster mid-run, so the
    /// row is rebuilt and the tab re-checked together.</summary>
    private void HandleSpellRosterChanged()
    {
        BuildSpellSubmenuEntries();
        RefreshCastTabVisibility();
    }

    /// <summary>
    /// The CAST tab is CLONED from an existing tab at runtime rather than added
    /// to the scene. The tab row is a HorizontalLayoutGroup, and the four tab
    /// Buttons carry no persistent onClick calls (verified against
    /// Dungeon_Level_0 -- every listener is wired here in Start), so a clone
    /// arrives inert and takes only the listener given to it. A scene edit
    /// would be a manual step, and a forgotten manual step means the feature
    /// simply is not on the bar -- the same failure the Wall entry above dodges.
    /// </summary>
    private void EnsureCastTab()
    {
        if (castTabButton != null) return;
        var donor = summonTabButton != null ? summonTabButton : mineTabButton;
        if (donor == null || donor.transform.parent == null) return;

        castTabButton = Instantiate(donor, donor.transform.parent);
        castTabButton.name = "CastTab";
        castTabButton.onClick.RemoveAllListeners();   // defensive: a future Inspector wiring
        castTabButton.onClick.AddListener(OnCastTabClicked);
        castTabButton.gameObject.SetActive(true);
    }

    /// <summary>The tab appears once the core holds ANY working -- not once the
    /// Sorcery trunk is researched. A god's grant at a tier-up must be castable
    /// by a core that never took the trunk, or the audience hands over a power
    /// with no way to reach it.</summary>
    private void RefreshCastTabVisibility()
    {
        if (castTabButton == null) return;
        bool show = SpellBook.AnySpellKnown;
        if (castTabButton.gameObject.activeSelf != show)
            castTabButton.gameObject.SetActive(show);
        if (show) SetTabLabel(castTabButton, "CAST", GameAction.Cast);
    }

    private void RefreshShortcutLabels()
    {
        SetTabLabel(castTabButton, "CAST", GameAction.Cast);
        SetTabLabel(pushTabButton, "PUSH", GameAction.Push);
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
        SetButtonColor(pushTabButton, currentTab == ActiveTab.Push);
        SetButtonColor(mineTabButton, currentTab == ActiveTab.Mine);
        SetButtonColor(buildTabButton, currentTab == ActiveTab.Build);
        SetButtonColor(summonTabButton, currentTab == ActiveTab.Summon);
        SetButtonColor(castTabButton, currentTab == ActiveTab.Cast);
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