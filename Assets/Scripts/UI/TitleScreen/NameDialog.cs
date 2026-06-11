using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Dungeon name entry dialog. Used by both new-game and rename flows.
///
/// PREFAB SETUP (inside UICanvas_Title):
///   NameDialog (this script) — full-screen blocker panel
///   └── Window (panel ~560 × 240)
///         ├── PromptLabel  (TMP_Text — "Name your dungeon")
///         ├── InputField   (TMP_InputField, char limit 32)
///         ├── OkButton     (Button)
///         └── CancelButton (Button)
/// </summary>
public class NameDialog : MonoBehaviour
{
    [SerializeField] private TMP_Text promptLabel;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button okButton;
    [SerializeField] private Button cancelButton;

    public const int MAX_NAME_LENGTH = 32;
    private const string ALLOWED_PUNCT = " -'.,";

    private Action<string> onSubmit;
    private Action onCancel;

    private void Awake()
    {
        inputField.characterLimit = MAX_NAME_LENGTH;
        inputField.onValidateInput = ValidateChar;
        okButton.onClick.AddListener(HandleOk);
        cancelButton.onClick.AddListener(HandleCancel);
        gameObject.SetActive(false);
    }

    public void Show(string initialText, string prompt,
                     Action<string> submit, Action cancel = null)
    {
        promptLabel.text = prompt;
        inputField.text = initialText ?? "";
        onSubmit = submit;
        onCancel = cancel;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        inputField.Select();
        inputField.ActivateInputField();
    }

    private char ValidateChar(string text, int charIndex, char addedChar)
    {
        if (char.IsLetterOrDigit(addedChar)) return addedChar;
        if (ALLOWED_PUNCT.IndexOf(addedChar) >= 0) return addedChar;
        return '\0';
    }

    private void HandleOk()
    {
        string trimmed = (inputField.text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) trimmed = "Unnamed Dungeon";
        gameObject.SetActive(false);
        onSubmit?.Invoke(trimmed);
    }

    private void HandleCancel()
    {
        gameObject.SetActive(false);
        onCancel?.Invoke();
    }
}