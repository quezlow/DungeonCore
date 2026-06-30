using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand panel listing each combat class and the gold it drops on death
/// (label, value, weight). Toggle with the configured key (default L).
/// Informational only — mirrors TrapPanel's rebuild-on-open pattern.
///
/// PREFAB / SCENE SETUP:
///   LootPanel (this script, on a parent GameObject)
///   |-- Panel
///   |   |-- TitleLabel  (TMP_Text — "Adventurer Loot")
///   |   |-- ScrollView
///   |   |   |-- Content (VerticalLayoutGroup — assigned to entryContainer)
///   |   |-- CloseButton (Button — wire OnClick -> OnCloseClicked)
///   EntryPrefab: Button with two TMP_Text children (line1 = class, line2 = drops).
///
/// Assign the same CombatClassDefinition assets the spawner uses to the Classes list.
/// </summary>
public class LootPanel : MonoBehaviour
{
    public static LootPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private Button entryPrefab;

    [Header("Data")]
    [Tooltip("The combat-class assets to display (assign the same ones the spawner uses).")]
    [SerializeField] private List<CombatClassDefinition> classes = new();

    [Header("Hotkey")]
    [SerializeField] private Key toggleKey = Key.L;

    private readonly List<Button> spawnedEntries = new();
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
            Debug.LogWarning("[LootPanel] entryContainer or entryPrefab not assigned.");
            return;
        }

        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();

        foreach (var c in classes)
        {
            if (c == null) continue;

            Button btn = Instantiate(entryPrefab, entryContainer);
            btn.gameObject.SetActive(true);

            var labels = btn.GetComponentsInChildren<TMP_Text>();
            string header = c.combatClass.ToString();
            string detail = BuildDetailLine(c);

            if (labels.Length >= 2) { labels[0].text = header; labels[1].text = detail; }
            else if (labels.Length == 1) labels[0].text = $"{header}\n{detail}";

            spawnedEntries.Add(btn);
        }
    }

    private string BuildDetailLine(CombatClassDefinition c)
    {
        if (c.classLoot == null || c.classLoot.Count == 0) return "No loot";

        var parts = new List<string>();
        foreach (var e in c.classLoot)
        {
            if (e == null) continue;
            parts.Add($"{e.label} {e.goldValue}g x{e.weight:0.#}");
        }
        return string.Join("   -   ", parts);
    }

    private void Show() { BuildEntries(); if (panel != null) panel.SetActive(true); isOpen = true; }
    private void Hide() { if (panel != null) panel.SetActive(false); isOpen = false; }
}