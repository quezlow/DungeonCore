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
            var party = p;
            bool named = party.HasNamedMember();
            AddRow("In dungeon:  " + TrackedPartyRegistry.LabelFor(party), party.tracked, named,
                   () =>
                   {
                       if (party.tracked)
                       {
                           party.tracked = false;
                           PartyBannerManager.Instance?.HideBanner(party);
                       }
                       else
                       {
                           party.tracked = true;
                           PartyBannerManager.Instance?.ShowBanner(party);
                       }
                       Refresh();
                   });
        }

        foreach (var rec in reg.PendingParties)
        {
            if (rec == null) continue;
            var record = rec;
            bool named = TrackedPartyRegistry.HasNamedMember(record);
            AddRow("Returning:  " + TrackedPartyRegistry.LabelFor(record), true, named,
                   named ? null
                         : () => { reg.ForgetPending(record); Refresh(); });
        }
    }

    private void AddRow(string label, bool pinned, bool named, System.Action onToggle)
    {
        var row = Instantiate(entryPrefab, entryContainer);
        row.SetActive(true);

        var btn = row.GetComponentInChildren<Button>();

        // Row label: the first TMP_Text that is NOT part of the button, so a
        // button with its own text child can never steal the row's label slot.
        TMP_Text rowText = null;
        foreach (var t in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (btn != null && t.transform.IsChildOf(btn.transform)) continue;
            rowText = t;
            break;
        }
        if (rowText != null) rowText.text = (pinned ? "[*] " : "[  ] ") + label;

        if (btn != null)
        {
            bool canToggle = !named && onToggle != null;
            btn.gameObject.SetActive(true);
            btn.interactable = canToggle;

            // Label the button in code so an empty prefab label can't hide it.
            var btnText = btn.GetComponentInChildren<TMP_Text>(true);
            if (btnText != null)
                btnText.text = named ? "Named" : (pinned ? "Unpin" : "Pin");

            if (canToggle) btn.onClick.AddListener(() => onToggle());
        }

        spawned.Add(row);
    }

    private void Refresh() { if (isOpen) BuildEntries(); }
    private void Show() { BuildEntries(); if (panel != null) panel.SetActive(true); isOpen = true; }
    private void Hide() { if (panel != null) panel.SetActive(false); isOpen = false; }
}