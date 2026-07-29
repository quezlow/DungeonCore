using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small panel opened by clicking a captive held in a Prison cell. Shows who they
/// are, how long the dark has left them, and the three verbs. Interrogate greys out
/// once that banner is already read; Esc closes it via the PauseMenuController ladder.
///
/// SCENE SETUP: a small panel under the HUD canvas (inactive by default) with two
/// TMP labels, three verb buttons and a Close button; wire all six references plus
/// the panel root. Button clicks are bound in code.
/// </summary>
public class PrisonerPanelUI : MonoBehaviour
{
    public static PrisonerPanelUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text detailLabel;
    [SerializeField] private Button releaseButton;
    [SerializeField] private Button executeButton;
    [SerializeField] private Button interrogateButton;
    [SerializeField] private Button closeButton;

    private FurniturePiece piece;
    private Prisoner prisoner;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        Instance = this;
        if (releaseButton != null) releaseButton.onClick.AddListener(OnReleaseClicked);
        if (executeButton != null) executeButton.onClick.AddListener(OnExecuteClicked);
        if (interrogateButton != null) interrogateButton.onClick.AddListener(OnInterrogateClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Open(FurniturePiece cell, Prisoner held)
    {
        if (cell == null || held == null || panelRoot == null) return;
        piece = cell;
        prisoner = held;
        Refresh();
        panelRoot.SetActive(true);
    }

    public void Close()
    {
        piece = null;
        prisoner = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Refresh()
    {
        var prison = PrisonController.Instance;
        if (prisoner == null || prison == null) return;

        if (nameLabel != null)
            nameLabel.text = prisoner.CaptiveName + ", " + prisoner.ClassName + ", held in the dark.";

        if (detailLabel != null)
        {
            string fate = prison.StarveDays > 0
                ? "Starves in " + Mathf.Max(0, prison.StarveDays - prisoner.DaysHeld) + " day(s)."
                : "They will keep as long as I care to hold them.";
            detailLabel.text = fate
                + "\nRelease: notoriety -" + prison.ReleaseNotoriety.ToString("0")
                + ".   Execute: notoriety +" + prison.ExecuteNotoriety.ToString("0")
                + ", and a corpse for the stone.";
        }

        if (interrogateButton != null)
            interrogateButton.interactable = !FactionIntel.IntelKnown(prisoner.Faction);
    }

    private void OnReleaseClicked()
    {
        if (PrisonController.Instance != null && PrisonController.Instance.Release(piece)) Close();
    }

    private void OnExecuteClicked()
    {
        if (PrisonController.Instance != null && PrisonController.Instance.Execute(piece)) Close();
    }

    private void OnInterrogateClicked()
    {
        if (PrisonController.Instance == null) return;
        PrisonController.Instance.Interrogate(piece);
        Refresh();
    }
}