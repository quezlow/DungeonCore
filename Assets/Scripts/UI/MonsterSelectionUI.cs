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

    [Header("Roster List")]
    [Tooltip("Parent for the generated rows. Give it a VerticalLayoutGroup plus a " +
             "ContentSizeFitter with Vertical Fit = Preferred Size, and put it inside a " +
             "ScrollRect: the roster runs to a hundred entries.")]
    [SerializeField] private Transform      rosterContainer;
    [Tooltip("Row template: a Button with at least one TMP_Text child. Two labels are " +
             "used when present (name, then the status line). Keep it INACTIVE in the " +
             "scene -- rows are instantiated from it at runtime.")]
    [SerializeField] private Button         rosterRowPrefab;
    [Tooltip("Optional group header template: any object with a TMP_Text. Inactive in " +
             "the scene. A group with no visible rows never spawns its header.")]
    [SerializeField] private GameObject     rosterHeaderPrefab;

    [Header("Row Colours")]
    [SerializeField] private Color availableColour    = Color.white;
    [SerializeField] private Color unaffordableColour = new Color(0.85f, 0.45f, 0.45f, 1f);
    [SerializeField] private Color lockedColour       = new Color(0.55f, 0.55f, 0.62f, 1f);

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

    /// <summary>Superseded by the roster list. Kept as a no-op so existing Prev/Next
    /// button wiring in the scene does not throw; delete the buttons when convenient.</summary>
    public void OnPrevClicked_Legacy() { }

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

    // -- Roster list --

    /// <summary>Row states, in the order they sort within a group.</summary>
    private enum RosterState
    {
        Available = 0,      // researched, rank met, affordable
        Unaffordable = 1,   // unlocked but short on mana or capacity
        ResearchNeeded = 2, // rank met, tech node not taken
        Mystery = 3,        // next rank up (or undiscovered): shown as ???
        RankLocked = 4,     // researched early: named, after the ???s
        Hidden = 5          // too far out to show at all
    }

    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    private static bool RankMet(MonsterDefinition def)
        => DungeonCore.Instance == null
        || def.RequiredFlatLevel <= DungeonCore.Instance.DungeonLevel;

    private static bool TechMet(MonsterDefinition def)
        => string.IsNullOrEmpty(def.requiredTechKey)
        || UnlockState.IsUnlocked(def.requiredTechKey);

    private static bool Discovered(MonsterDefinition def)
        => !def.requiresDiscovery || BestiaryState.Discovered(def.monsterName);

    /// <summary>True when the def's rank is exactly one flat level above the core's.
    /// This is the ??? teaser band: one step ahead is a goal, further out is noise.</summary>
    private static bool IsNextRank(MonsterDefinition def)
    {
        if (DungeonCore.Instance == null) return false;
        return def.RequiredFlatLevel == DungeonCore.Instance.DungeonLevel + 1;
    }

    private static bool Affordable(MonsterDefinition def)
    {
        var core = DungeonCore.Instance;
        if (core == null) return true;
        return core.CurrentMana >= def.ManaCost
            && core.UsedCapacity + def.CapacityCost <= core.MaxCapacity;
    }

    private static RosterState StateOf(MonsterDefinition def)
    {
        // Never discovered: a mystery at any rank, so the Bestiary hunt stays a goal
        // rather than a spoiler.
        if (!Discovered(def)) return RosterState.Mystery;

        bool rank = RankMet(def);
        bool tech = TechMet(def);

        if (rank && tech)
            return Affordable(def) ? RosterState.Available : RosterState.Unaffordable;

        // Rank reached but the shape is not remembered yet: name it and say why.
        if (rank) return RosterState.ResearchNeeded;

        // Researched ahead of rank: the player earned the reveal, so it is named and
        // sits after the mystery rows.
        if (tech) return RosterState.RankLocked;

        // Not researched and not yet at rank: tease only the next step up.
        return IsNextRank(def) ? RosterState.Mystery : RosterState.Hidden;
    }

    private string StatusLine(MonsterDefinition def, RosterState state)
    {
        switch (state)
        {
            case RosterState.Mystery:
                return !Discovered(def)
                    ? "Slay one in the wild to learn it"
                    : "Not yet within your reach";
            case RosterState.ResearchNeeded:
                return "Research required";
            case RosterState.RankLocked:
                return $"Unlocks at {LevelTierUtil.DisplayName(def.RequiredFlatLevel)}";
            case RosterState.Unaffordable:
                return $"Capacity: {def.CapacityCost}   Mana: {def.ManaCost:0}";
            default:
                return $"Capacity: {def.CapacityCost}   Mana: {def.ManaCost:0}";
        }
    }

    private Color ColourFor(RosterState state)
    {
        switch (state)
        {
            case RosterState.Available:    return availableColour;
            case RosterState.Unaffordable: return unaffordableColour;
            default:                       return lockedColour;
        }
    }

    /// <summary>Rebuilds every row. Cheap enough to run on open and on any unlock,
    /// and far simpler than diffing a hundred rows against changed lock state.</summary>
    private void RebuildRoster()
    {
        foreach (var go in spawnedRows) if (go != null) Destroy(go);
        spawnedRows.Clear();

        if (rosterContainer == null || rosterRowPrefab == null || registry?.All == null) return;

        AddGroup("Creatures", d => !(d is BossVariantDefinition) && !(d is SubBossVariantDefinition));
        AddGroup("Sub-Bosses", d => d is SubBossVariantDefinition);
        // Both variant types derive straight from MonsterDefinition, not from each
        // other, so these three predicates are mutually exclusive as written.
        AddGroup("Bosses", d => d is BossVariantDefinition);
    }

    private void AddGroup(string title, System.Func<MonsterDefinition, bool> belongs)
    {
        var rows = new List<(MonsterDefinition def, RosterState state)>();
        foreach (var def in registry.All)
        {
            if (def == null || !belongs(def)) continue;
            if (!AffinityAllowed(def)) continue;          // wrong core type: not even a ???
            var state = StateOf(def);
            if (state == RosterState.Hidden) continue;
            rows.Add((def, state));
        }
        if (rows.Count == 0) return;                       // no header for an empty group

        rows.Sort((a, b) =>
        {
            int s = a.state.CompareTo(b.state);
            if (s != 0) return s;
            int l = a.def.RequiredFlatLevel.CompareTo(b.def.RequiredFlatLevel);
            if (l != 0) return l;
            return string.Compare(a.def.monsterName, b.def.monsterName,
                                  System.StringComparison.Ordinal);
        });

        if (rosterHeaderPrefab != null)
        {
            var head = Instantiate(rosterHeaderPrefab, rosterContainer);
            head.SetActive(true);
            var ht = head.GetComponentInChildren<TMP_Text>();
            if (ht != null) ht.text = title;
            spawnedRows.Add(head);
        }

        foreach (var (def, state) in rows) SpawnRow(def, state);
    }

    private void SpawnRow(MonsterDefinition def, RosterState state)
    {
        Button btn = Instantiate(rosterRowPrefab, rosterContainer);
        btn.gameObject.SetActive(true);
        spawnedRows.Add(btn.gameObject);

        bool mystery = state == RosterState.Mystery;
        string label = mystery ? "???" : def.monsterName;

        var labels = btn.GetComponentsInChildren<TMP_Text>();
        if (labels.Length >= 2)
        {
            labels[0].text = label;
            labels[1].text = StatusLine(def, state);
        }
        else if (labels.Length == 1)
        {
            labels[0].text = mystery ? label : $"{label}  -  {StatusLine(def, state)}";
        }
        foreach (var t in labels) t.color = ColourFor(state);

        // Only a genuinely placeable row is clickable. Unaffordable stays clickable so
        // the player can select it and watch the cost, matching how build entries behave.
        bool selectable = state == RosterState.Available || state == RosterState.Unaffordable;
        btn.interactable = selectable;

        var captured = def;
        if (selectable)
            btn.onClick.AddListener(() => SelectFromRoster(captured));

        // Hover drives the detail pane. A mystery row shows its hint without naming
        // the creature.
        var hover = btn.gameObject.AddComponent<MonsterRosterRowHover>();
        hover.Bind(this, def, mystery);
    }

    private void SelectFromRoster(MonsterDefinition def)
    {
        // registry.All is IReadOnlyList, which has no IndexOf -- scan for the slot.
        for (int i = 0; i < registry.All.Count; i++)
            if (registry.All[i] == def) { selectedIndex = i; break; }
        RefreshDisplay();
        // Selection closes the picker: the next click is a placement click.
        Hide();
    }

    /// <summary>Detail pane preview on hover. Does not change the selection.</summary>
    public void PreviewRow(MonsterDefinition def, bool mystery)
    {
        if (def == null) return;
        var state = StateOf(def);

        if (monsterIcon != null)
        {
            monsterIcon.sprite = mystery ? null : def.icon;
            monsterIcon.enabled = !mystery;
        }
        if (monsterNameLabel != null)
            monsterNameLabel.text = mystery ? "???" : def.monsterName;
        if (costLabel != null)
            costLabel.text = mystery
                ? StatusLine(def, state)
                : $"{StatusLine(def, state)}{MusterLine(def)}";
        if (descriptionLabel != null)
            descriptionLabel.text = mystery
                ? "Something the core has not yet learned to shape."
                : def.description;
    }

    /// <summary>Restores the detail pane to the current selection when a hover ends.</summary>
    public void ClearPreview() => RefreshDisplay();

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
        RebuildRoster();

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
