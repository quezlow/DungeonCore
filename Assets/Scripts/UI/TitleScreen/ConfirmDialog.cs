using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Reusable yes/no confirmation dialog. Used by overwrite,
/// delete, and quit flows.
///
/// PREFAB SETUP (inside UICanvas_Title):
///   ConfirmDialog (this script) — full-screen blocker panel with Image (raycast target ON)
///   └── Window (panel ~520 × 240, centred)
///         ├── MessageLabel (TMP_Text)
///         ├── ConfirmButton (Button + TMP child)
///         └── CancelButton  (Button + TMP child)
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text messageLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private TMP_Text cancelLabel;

    private Action onConfirm;
    private Action onCancel;

    private void Awake()
    {
        confirmButton.onClick.AddListener(HandleConfirm);
        cancelButton.onClick.AddListener(HandleCancel);
        gameObject.SetActive(false);
    }

    public void Show(string message, Action confirm, Action cancel = null,
                     string confirmText = "Confirm", string cancelText = "Cancel")
    {
        messageLabel.text = message;
        if (confirmLabel != null) confirmLabel.text = confirmText;
        if (cancelLabel != null) cancelLabel.text = cancelText;
        onConfirm = confirm;
        onCancel = cancel;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    private void HandleConfirm()
    {
        gameObject.SetActive(false);
        onConfirm?.Invoke();
    }

    private void HandleCancel()
    {
        gameObject.SetActive(false);
        onCancel?.Invoke();
    }
}