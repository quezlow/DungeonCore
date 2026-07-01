using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand panel listing the parties currently in the dungeon and the tracked
/// parties awaiting return. Each active party shows a Pin button: pinning marks it
/// tracked, so it persists and returns like a named party even without a Hero.
/// Toggle with the configured key (default K). Mirrors LootPanel's rebuild-on-open.
///
/// PREFAB / SCENE SETUP:
///   KnownPartiesPanel (this script on a parent GameObject)
///   |-- Panel
///   |   |-- TitleLabel  (TMP_Text — "Known Parties")
///   |   |-- ScrollView -> Content (VerticalLayoutGroup — assigned to entryContainer)
///   |   |-- CloseButton (Button — wire OnClick -> OnCloseClicked)
///   EntryPrefab: a row GameObject with a TMP_Text label child and a Button child (pin).
/// </summary>
public class KnownPartiesPanel : MonoBehaviour
{
    public static KnownPartiesPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private GameObject entryPrefab;

    [Header("Hotkey")]
    [SerializeField] private Key toggleKey = Key.K;

    private readonly List<GameObject> spawned = new();
    private bool isOpen = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();
    }

    public void Toggle() { if (isOpen) Hide(); else Show(); }
    public void OnCloseClicked() => Hide();

    private void BuildEntries()
    {
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[KnownPartiesPanel] entryContainer or entryPrefab not assigned.");
            return;
        }

        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();

        var reg = TrackedPartyRegistry.Instance;
        if (reg == null) return;

        foreach (var p in reg.ActiveParties)
        {
            if (p == null) continue;
            AddRow("In dungeon:  " + TrackedPartyRegistry.LabelFor(p), p.tracked,
                   () => { p.tracked = true; PartyBannerManager.Instance?.ShowBanner(p); Refresh(); });
        }

        foreach (var rec in reg.PendingParties)
        {
            if (rec == null) continue;
            AddRow("Returning:  " + TrackedPartyRegistry.LabelFor(rec), true, null);
        }
    }

    private void AddRow(string label, bool pinned, System.Action onPin)
    {
        var row = Instantiate(entryPrefab, entryContainer);
        row.SetActive(true);

        var text = row.GetComponentInChildren<TMP_Text>();
        if (text != null) text.text = (pinned ? "[*] " : "[  ] ") + label;

        var btn = row.GetComponentInChildren<Button>();
        if (btn != null)
        {
            btn.interactable = onPin != null && !pinned;
            if (onPin != null) btn.onClick.AddListener(() => onPin());
        }

        spawned.Add(row);
    }

    private void Refresh() { if (isOpen) BuildEntries(); }
    private void Show() { BuildEntries(); if (panel != null) panel.SetActive(true); isOpen = true; }
    private void Hide() { if (panel != null) panel.SetActive(false); isOpen = false; }
}