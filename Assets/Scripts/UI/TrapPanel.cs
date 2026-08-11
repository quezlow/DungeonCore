using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand panel listing all placed traps with damage, cooldown, link target,
/// and flagged status. Toggle visibility with the T key.
///
/// Clicking any entry pans the camera to that trap (mirrors AlertsLog pattern).
///
/// REBUILD POLICY
///   The list is rebuilt on every Open() so newly placed/destroyed traps appear
///   correctly. If you find the list refreshing too aggressively as traps come
///   and go, switch to subscribing on TrapRegistry change events.
///
/// PREFAB / SCENE SETUP:
///   TrapPanel (this script, on a parent GameObject)
///   ├── Panel
///   │   ├── TitleLabel  (TMP_Text — "Placed Traps")
///   │   ├── ScrollView
///   │   │   └── Content (VerticalLayoutGroup — assigned to entryContainer)
///   │   └── CloseButton (Button — wire OnClick → OnCloseClicked)
///
///   EntryPrefab: Button with two TMP_Text children (line1 = name+cell, line2 = stats).
/// </summary>
public class TrapPanel : MonoBehaviour
{
    public static TrapPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private Button entryPrefab;

    [Header("Hotkey")]
    [Tooltip("Key that toggles panel visibility.")]

    private readonly List<Button> spawnedEntries = new();
    private bool isOpen = false;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
        TrapBase.OnTrapsChanged += HandleTrapsChanged;
    }

    private void OnDestroy()
    {
        TrapBase.OnTrapsChanged -= HandleTrapsChanged;
        if (Instance == this) Instance = null;
    }

    private void HandleTrapsChanged()
    {
        if (isOpen) BuildEntries();
    }

    private void Update()
    {
        // No pause gate. Opening the trap readout is navigation and pause
        // permits it (canon 39). The old gate was inverted: the hotkey refused
        // while held, yet a panel already open kept every button live.
        if (Keybinds.WasPressed(GameAction.ToggleTraps))
            Toggle();
    }

    // ── Public API ────────────────────────────────────────────────

    public void Toggle()
    {
        if (isOpen) Hide();
        else Show();
    }

    public void OnCloseClicked() => Hide();

    // ── Build ─────────────────────────────────────────────────────

    private void BuildEntries()
    {
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[TrapPanel] entryContainer or entryPrefab not assigned.");
            return;
        }

        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();

        // Aggregate traps across all floors — panel is multi-floor by design.
        var all = new System.Collections.Generic.List<TrapBase>();
        if (FloorManager.Instance != null)
        {
            var buf = new System.Collections.Generic.List<TrapBase>();
            foreach (var floor in FloorManager.Instance.AllFloors)
            {
                if (floor?.Entities == null) continue;
                floor.Entities.FillAll(buf);
                all.AddRange(buf);
            }
        }

        foreach (var trap in all)
        {
            if (trap.Definition == null) continue;

            Button btn = Instantiate(entryPrefab, entryContainer);
            btn.gameObject.SetActive(true);

            var labels = btn.GetComponentsInChildren<TMP_Text>();
            string header = trap.Definition.trapName;
            string detail = BuildDetailLine(trap);

            if (labels.Length >= 2)
            {
                labels[0].text = header;
                labels[1].text = detail;
            }
            else if (labels.Length == 1)
            {
                labels[0].text = $"{header}\n{detail}";
            }

            Vector3 captured = trap.transform.position;
            btn.onClick.AddListener(() => DungeonCameraController.Instance?.PanTo(captured));

            // Trap removal now routes through the Demolish build mode alongside
            // furniture and room anchors, so the panel is read-only. If the entry
            // prefab still carries a RemoveButton child, hide it rather than wire it.
            var removeBtn = btn.transform.Find("RemoveButton");
            if (removeBtn != null) removeBtn.gameObject.SetActive(false);

            spawnedEntries.Add(btn);
        }
    }

    private string BuildDetailLine(TrapBase trap)
    {
        var def = trap.Definition;
        var parts = new List<string>();

        // Damage / cooldown for damaging traps
        if (def.behaviour == TrapDefinition.TrapBehaviour.SpikeTrap ||
            def.behaviour == TrapDefinition.TrapBehaviour.Pitfall)
        {
            parts.Add($"Dmg: {def.damage:0}");
            parts.Add($"CD: {def.cooldown:0.#}s");
        }
        else if (def.behaviour == TrapDefinition.TrapBehaviour.Warning)
        {
            parts.Add("Warning Alert");
        }
        else if (def.behaviour == TrapDefinition.TrapBehaviour.PressurePlate)
        {
            if (trap is PressurePlateTrap plate)
            {
                parts.Add($"Radius: {plate.TriggerRadius:0.#}");
                parts.Add(plate.HasLink ? "Linked" : "Unlinked");
            }
        }

        parts.Add($"Cap: {def.capacityCost}");
        if (trap.IsFlagged)
            parts.Add("FLAGGED");

        return string.Join("  ·  ", parts);
    }

    // ── Visibility ────────────────────────────────────────────────

    private void Show()
    {
        BuildEntries();
        if (panel != null) panel.SetActive(true);
        isOpen = true;
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        isOpen = false;
    }
}