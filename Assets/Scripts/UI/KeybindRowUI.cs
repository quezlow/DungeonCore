using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the Controls list: action label, a button showing the current key
/// (click to rebind), and a small reset-to-default button. Populated by
/// KeybindControlsUI; this script only displays + forwards clicks.
/// </summary>
public class KeybindRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text actionLabel;
    [SerializeField] private Button keyButton;
    [SerializeField] private TMP_Text keyButtonLabel;
    [SerializeField] private Button resetButton;

    private GameAction action;
    private Action<GameAction> onRebind;
    private Action<GameAction> onReset;

    public GameAction Action => action;

    public void Bind(GameAction a, Action<GameAction> rebindCb, Action<GameAction> resetCb)
    {
        action = a;
        onRebind = rebindCb;
        onReset = resetCb;

        if (actionLabel != null) actionLabel.text = Keybinds.Label(a);

        if (keyButton != null)
        {
            keyButton.onClick.RemoveAllListeners();
            keyButton.onClick.AddListener(() => onRebind?.Invoke(action));
        }
        if (resetButton != null)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(() => onReset?.Invoke(action));
        }
        Refresh();
    }

    public void Refresh()
    {
        if (keyButtonLabel != null) keyButtonLabel.text = Keybinds.DisplayName(action);
    }

    /// <summary>Shows a placeholder while this row is capturing a new key.</summary>
    public void SetListening()
    {
        if (keyButtonLabel != null) keyButtonLabel.text = "…";
    }
}