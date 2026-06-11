using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Dungeon type selection dialog. 3×2 grid of buttons.
/// Replaces with the proper tutorial/ceremony flow later (roadmap Days 50+).
///
/// PREFAB SETUP:
///   TypePickerDialog (this script) — full-screen blocker
///   └── Window (panel)
///         ├── PromptLabel
///         ├── Grid (Grid Layout Group, 3 columns)
///         │     ├── FireButton  WaterButton  EarthButton
///         │     └── AirButton   LightButton  DarkButton
///         └── CancelButton
/// </summary>
public class TypePickerDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text promptLabel;
    [SerializeField] private Button fireButton;
    [SerializeField] private Button waterButton;
    [SerializeField] private Button earthButton;
    [SerializeField] private Button airButton;
    [SerializeField] private Button lightButton;
    [SerializeField] private Button darkButton;
    [SerializeField] private Button cancelButton;

    private Action<DungeonType> onPick;
    private Action onCancel;

    private void Awake()
    {
        fireButton.onClick.AddListener(() => Pick(DungeonType.Fire));
        waterButton.onClick.AddListener(() => Pick(DungeonType.Water));
        earthButton.onClick.AddListener(() => Pick(DungeonType.Earth));
        airButton.onClick.AddListener(() => Pick(DungeonType.Air));
        lightButton.onClick.AddListener(() => Pick(DungeonType.Light));
        darkButton.onClick.AddListener(() => Pick(DungeonType.Dark));
        cancelButton.onClick.AddListener(HandleCancel);
        gameObject.SetActive(false);
    }

    public void Show(string prompt, Action<DungeonType> pick, Action cancel = null)
    {
        promptLabel.text = prompt;
        onPick = pick;
        onCancel = cancel;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    private void Pick(DungeonType t)
    {
        gameObject.SetActive(false);
        onPick?.Invoke(t);
    }

    private void HandleCancel()
    {
        gameObject.SetActive(false);
        onCancel?.Invoke();
    }
}