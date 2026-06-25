using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wave 2a — Three-choice quit dialog: Save and Quit / Quit without Saving / Cancel.
/// ConfirmDialog only offers two buttons, so the quit-with-save-prompt uses this.
/// Leave the object ENABLED in the scene; it wires its buttons then self-hides in Awake.
///
/// SCENE SETUP (inside UICanvas_Dungeon):
///   QuitDialog (this script) — full-screen blocker Image (raycast target ON)
///     Board (Board_Window)
///       MessageLabel  (TMP_Text)
///       SaveQuitButton (Button_Wood + TMP child)
///       QuitButton     (Button_Wood + TMP child)
///       CancelButton   (Button_Wood + TMP child)
/// </summary>
public class SaveQuitDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text messageLabel;
    [SerializeField] private Button saveQuitButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button cancelButton;

    private Action onSaveAndQuit;
    private Action onQuitNoSave;
    private Action onCancel;

    private void Awake()
    {
        saveQuitButton.onClick.AddListener(HandleSaveQuit);
        quitButton.onClick.AddListener(HandleQuit);
        cancelButton.onClick.AddListener(HandleCancel);
        gameObject.SetActive(false);
    }

    public void Show(string message, Action onSaveAndQuit, Action onQuitNoSave, Action onCancel = null)
    {
        if (messageLabel != null) messageLabel.text = message;
        this.onSaveAndQuit = onSaveAndQuit;
        this.onQuitNoSave = onQuitNoSave;
        this.onCancel = onCancel;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    private void HandleSaveQuit() { gameObject.SetActive(false); onSaveAndQuit?.Invoke(); }
    private void HandleQuit() { gameObject.SetActive(false); onQuitNoSave?.Invoke(); }
    private void HandleCancel() { gameObject.SetActive(false); onCancel?.Invoke(); }
}