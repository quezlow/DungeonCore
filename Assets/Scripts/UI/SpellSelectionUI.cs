using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The spell picker: every castable working listed at once, one entry each, so
/// cooldowns across the whole set read at a glance mid-fight. Shown while
/// BuildMode.CastSpell is active.
///
/// SCENE-WIRED, by design decision. The panel, the entry prefab and the
/// container are yours to lay out; this component only fills and drives them.
/// It follows the ActionBarHUD sub-menu idiom exactly -- instantiate the
/// prefab per entry, set the first TMP_Text child as the label, set the second
/// Image child as the icon if one exists -- so an entry prefab that works for
/// the build sub-menu works here unchanged.
///
/// THE COST OF SCENE WIRING is that an unassigned slot renders an empty panel
/// and logs nothing. That failure has cost this project test cycles before, so
/// it is refused here: ValidateWiring names every missing slot at Awake, and
/// Commands -> Validate Spell Picker Wiring runs the same check on demand.
///
/// The roster rebuilds on SpellBook.OnRosterChanged, so a completed research
/// node or a god's grant appears without closing and reopening the panel.
///
/// SCENE SETUP:
///   panel           the root object to show and hide
///   entryPrefab     one Button, INACTIVE in the scene, with a TMP_Text child
///   entryContainer  the transform entries are instantiated under
///   detailLabel     optional; shows the selected working's text and stats
/// Number keys 1-9 select while the panel is open.
/// </summary>
public class SpellSelectionUI : MonoBehaviour
{
    public static SpellSelectionUI Instance { get; private set; }

    [Header("Wiring")]
    [Tooltip("Root object shown while cast mode is active.")]
    [SerializeField] private GameObject panel;
    [Tooltip("One Button, left INACTIVE in the scene. Cloned per castable working.")]
    [SerializeField] private Button entryPrefab;
    [Tooltip("Transform the cloned entries are parented under.")]
    [SerializeField] private Transform entryContainer;
    [Tooltip("Optional. Description and stats for the selected working.")]
    [SerializeField] private TMP_Text detailLabel;

    [Header("Entry Colours")]
    [SerializeField] private Color selectedColor = new Color(0.82f, 0.68f, 0.27f, 1.00f);
    [SerializeField] private Color unselectedColor = new Color(1.00f, 1.00f, 1.00f, 0.55f);
    [SerializeField] private Color unaffordableColor = new Color(0.90f, 0.30f, 0.30f, 1.00f);
    [Tooltip("Alpha multiplier applied while a working is still on cooldown.")]
    [Range(0.1f, 1f)][SerializeField] private float cooldownFade = 0.4f;

    [Header("Labels")]
    [Tooltip("Shown on an entry that is ready. {name} and {mana} are substituted.")]
    [SerializeField] private string readyFormat = "{name}  {mana}";
    [Tooltip("Shown on an entry still gathering. {name} and {seconds} are substituted.")]
    [SerializeField] private string cooldownFormat = "{name}  {seconds}s";

    private readonly List<SpellDefinition> entries = new List<SpellDefinition>();
    private readonly List<Button> buttons = new List<Button>();
    private int selectedIndex;
    private float refreshTimer;

    private SpellDefinition Current =>
        entries.Count > 0 ? entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)] : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        ValidateWiring(true);
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
        SpellBook.OnRosterChanged += HandleRosterChanged;
        Rebuild();
        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        SpellBook.OnRosterChanged -= HandleRosterChanged;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (panel == null || !panel.activeSelf) return;

        HandleNumberKeys();

        // Cooldown and affordability both move on their own. A quarter-second
        // repaint on UNSCALED time is cheaper than an event per mana tick, and
        // keeps counting while the clock is stopped -- the panel stays open
        // during pause so a shot can be lined up against a frozen board.
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = 0.25f;
        RefreshVisuals();
    }

    private void HandleNumberKeys()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (NameDialog.IsOpen || WarningTrapNameDialog.IsOpen) return;

        var keys = new[]
        {
            UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Digit2,
            UnityEngine.InputSystem.Key.Digit3, UnityEngine.InputSystem.Key.Digit4,
            UnityEngine.InputSystem.Key.Digit5, UnityEngine.InputSystem.Key.Digit6,
            UnityEngine.InputSystem.Key.Digit7, UnityEngine.InputSystem.Key.Digit8,
            UnityEngine.InputSystem.Key.Digit9,
        };
        for (int i = 0; i < keys.Length && i < entries.Count; i++)
            if (kb[keys[i]].wasPressedThisFrame) { Select(i); return; }
    }

    // -- Wiring validation ---------------------------------------------------

    /// <summary>Names every unassigned slot. A scene-wired panel with a blank
    /// reference renders empty and reports nothing, which is the exact failure
    /// this project has paid test cycles for; this refuses to be silent.
    /// Returns null when the wiring is whole.</summary>
    public string ValidateWiring(bool logIfBroken)
    {
        var faults = new List<string>();
        if (panel == null) faults.Add("panel is not assigned -- the picker can never show.");
        if (entryPrefab == null) faults.Add("entryPrefab is not assigned -- no entries can be made.");
        else if (entryPrefab.GetComponentInChildren<TMP_Text>(true) == null)
            faults.Add("entryPrefab has no TMP_Text child -- entries will be unlabelled.");
        if (entryContainer == null) faults.Add("entryContainer is not assigned -- entries have nowhere to go.");
        if (detailLabel == null) faults.Add("detailLabel is not assigned (optional -- no description will show).");

        if (faults.Count == 0) return null;
        string report = "[SpellSelectionUI] wiring incomplete on '" + name + "':\n  "
                      + string.Join("\n  ", faults);
        if (logIfBroken) Debug.LogWarning(report);
        return report;
    }

    // -- Roster --------------------------------------------------------------

    private void HandleRosterChanged()
    {
        var kept = Current;
        Rebuild();
        if (kept != null)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] == kept) { selectedIndex = i; break; }
        RefreshVisuals();
        PushSelection();
    }

    private void Rebuild()
    {
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i] != null) Destroy(buttons[i].gameObject);
        buttons.Clear();

        SpellBook.FillAvailable(entries);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entries.Count - 1));

        if (entryPrefab == null || entryContainer == null)
        {
            RefreshVisuals();
            PushSelection();
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var def = entries[i];
            Button btn = Instantiate(entryPrefab, entryContainer);
            btn.gameObject.SetActive(true);
            btn.name = "SpellEntry_" + (def != null ? def.id : i.ToString());

            // A clone carries the prefab's Inspector-wired onClick with it, and
            // the build sub-menu prefab is shared -- so clear before adding.
            btn.onClick.RemoveAllListeners();

            if (def != null && def.icon != null)
            {
                var images = btn.GetComponentsInChildren<Image>(true);
                if (images.Length > 1) images[1].sprite = def.icon;
            }

            int captured = i;                        // avoid the closure-capture bug
            btn.onClick.AddListener(() => Select(captured));
            buttons.Add(btn);
        }

        RefreshVisuals();
        PushSelection();
    }

    private void Select(int index)
    {
        selectedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, entries.Count - 1));
        RefreshVisuals();
        PushSelection();
    }

    private void PushSelection() => DungeonBuildController.Instance?.SetSelectedSpell(Current);

    private void RefreshVisuals()
    {
        float mana = DungeonCore.Instance != null ? DungeonCore.Instance.CurrentMana : 0f;

        for (int i = 0; i < buttons.Count && i < entries.Count; i++)
        {
            var def = entries[i];
            var btn = buttons[i];
            if (def == null || btn == null) continue;

            bool ready = SpellBook.IsReady(def);
            bool afford = mana >= def.manaCost;
            float left = SpellBook.CooldownRemaining(def);

            var img = btn.targetGraphic as Image;
            if (img == null) img = btn.GetComponent<Image>();
            if (img != null)
            {
                Color c = !afford ? unaffordableColor
                        : (i == selectedIndex ? selectedColor : unselectedColor);
                if (!ready) c.a *= cooldownFade;
                img.color = c;
            }

            var label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = left > 0.05f
                    ? cooldownFormat.Replace("{name}", def.displayName)
                                    .Replace("{seconds}", left.ToString("0.#"))
                    : readyFormat.Replace("{name}", def.displayName)
                                 .Replace("{mana}", def.manaCost.ToString("0"));
        }

        if (detailLabel != null)
        {
            var d = Current;
            detailLabel.text = d == null
                ? "The core remembers no workings yet."
                : d.description + "\n" + d.StatLine();
        }
    }

    // -- Visibility ----------------------------------------------------------

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.CastSpell)
        {
            if (panel != null) panel.SetActive(true);
            RefreshVisuals();
            PushSelection();
        }
        else if (panel != null) panel.SetActive(false);
    }
}
