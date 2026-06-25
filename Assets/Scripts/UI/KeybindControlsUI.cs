using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls tab: spawns one KeybindRowUI per rebindable GameAction, captures new
/// bindings from the keyboard, rejects duplicates, and supports per-row + global reset.
/// Reads/writes through the static Keybinds registry. Lives on the Controls page, which
/// is only active while Settings is open (so the game is paused and gameplay input is
/// suppressed — capturing a key here won't also trigger Mine/Build/etc.).
/// </summary>
public class KeybindControlsUI : MonoBehaviour
{
    [SerializeField] private KeybindRowUI rowPrefab;
    [SerializeField] private Transform rowContainer;
    [SerializeField] private Button resetAllButton;
    [SerializeField] private TMP_Text statusLabel;

    private readonly List<KeybindRowUI> rows = new();
    private GameAction? listening;

    private void Awake()
    {
        BuildRows();
        if (resetAllButton != null)
            resetAllButton.onClick.AddListener(ResetAll);
    }

    private void OnEnable()
    {
        CancelListening();
        RefreshAll();
        SetStatus(string.Empty);
    }

    private void OnDisable() => CancelListening();

    private void BuildRows()
    {
        if (rowPrefab == null || rowContainer == null || rows.Count > 0) return;
        foreach (var a in Keybinds.All)
        {
            var row = Instantiate(rowPrefab, rowContainer);
            row.Bind(a, BeginRebind, ResetOne);
            rows.Add(row);
        }
    }

    private void BeginRebind(GameAction action)
    {
        listening = action;
        foreach (var r in rows)
            if (r.Action == action) r.SetListening();
        SetStatus($"Press a key for {Keybinds.Label(action)}…  (Esc to cancel)");
    }

    private void ResetOne(GameAction action)
    {
        CancelListening();
        Keybinds.ResetToDefault(action);
        RefreshAll();
        SetStatus($"{Keybinds.Label(action)} reset to {Keybinds.DisplayName(action)}.");
    }

    private void ResetAll()
    {
        CancelListening();
        Keybinds.ResetAll();
        RefreshAll();
        SetStatus("All controls reset to defaults.");
    }

    private void Update()
    {
        if (listening == null) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            CancelListening();
            SetStatus("Rebind cancelled.");
            return;
        }

        foreach (var kc in kb.allKeys)
        {
            if (!kc.wasPressedThisFrame) continue;
            Key candidate = kc.keyCode;
            if (candidate == Key.None || candidate == Key.Escape) continue;

            GameAction target = listening.Value;
            var conflict = Keybinds.ConflictingAction(candidate, target);
            if (conflict != null)
            {
                CancelListening();
                SetStatus($"{Keybinds.DisplayName(candidate)} is already bound to {Keybinds.Label(conflict.Value)}.");
                return;
            }

            Keybinds.Rebind(target, candidate);
            listening = null;
            RefreshAll();
            SetStatus($"{Keybinds.Label(target)} → {Keybinds.DisplayName(candidate)}");
            return;
        }
    }

    private void CancelListening()
    {
        listening = null;
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var r in rows) r.Refresh();
    }

    private void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
    }
}