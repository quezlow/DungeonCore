using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small popup that appears immediately after a warning trap is placed.
/// Player types a name (e.g. "North Corridor") and the dialog calls
/// SetWarningLabel on the trap. Cancelling assigns "Unnamed Warning".
///
/// PREFAB / SCENE SETUP (attach to a parent GameObject under UICanvas_Dungeon):
///   WarningTrapNameDialog (this script)
///   ├── Panel
///   │   ├── PromptLabel    (TMP_Text — "Name this warning trap:")
///   │   ├── NameInput      (TMP_InputField)
///   │   ├── ConfirmButton  (Button — wire OnClick → OnConfirmClicked)
///   │   └── CancelButton   (Button — wire OnClick → OnCancelClicked)
///
/// Panel is inactive by default. Open() activates and shows.
/// </summary>
public class WarningTrapNameDialog : MonoBehaviour
{
    public static WarningTrapNameDialog Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject     panel;
    [SerializeField] private TMP_InputField nameInput;

    private WarningTrap targetTrap;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Opens the dialog for the given warning trap. Player input is applied
    /// via SetWarningLabel on confirm; cancel keeps the default label.
    /// </summary>
    public void Open(WarningTrap trap)
    {
        targetTrap = trap;

        if (panel != null) panel.SetActive(true);

        if (nameInput != null)
        {
            nameInput.text = "";
            nameInput.Select();
            nameInput.ActivateInputField();
        }
    }

    // ── Buttons (wired via Inspector OnClick) ─────────────────────

    public void OnConfirmClicked()
    {
        if (targetTrap != null && nameInput != null)
            targetTrap.SetWarningLabel(nameInput.text);

        Hide();
    }

    public void OnCancelClicked()
    {
        // Leave the trap with its default label.
        Hide();
    }

    // ── Internal ──────────────────────────────────────────────────

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        targetTrap = null;
    }
}
