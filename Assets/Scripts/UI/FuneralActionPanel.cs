using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The funeral procession's choice panel (canon 50): Desecrate, Tax, Let
/// pass, or Pay Respects. Mirrors CaravanActionPanel body for body -- opened
/// only by clicking the column, never pushed by a crossing; ONE VERB PER
/// PROCESSION; closing WITHOUT choosing settles nothing and the column walks
/// on, clickable later, because a misclick must never burn the decision.
///
/// Two gates the caravan's panel never needed:
/// - Tax is live only while the lead stands on a held road segment, the
///   caravan's own open-time test.
/// - PAY RESPECTS is live only when no death this procession carries was the
///   dungeon's doing. The button DISABLES rather than hides, and the label
///   says why: a hidden option reads as a bug, a refused one reads as a
///   judgement -- and the judgement is the beat.
///
/// The column halts while the panel is open (told on open and on close) so
/// the choice is never made against a moving target. Scene-authored by
/// duplicating the caravan panel and adding the fourth button (the guide has
/// the steps); closed beside CaravanActionPanel at the head of
/// PauseMenuController's central ESC chain -- the most transient thing that
/// can be open.
/// </summary>
public class FuneralActionPanel : MonoBehaviour
{
    public static FuneralActionPanel Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private CanvasGroup panelRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text cargoLabel;
    [SerializeField] private Button closeButton;

    [Header("Verbs")]
    [SerializeField] private Button robButton;
    [SerializeField] private TMP_Text robLabel;
    [SerializeField] private Button taxButton;
    [SerializeField] private TMP_Text taxLabel;
    [SerializeField] private Button respectsButton;
    [SerializeField] private TMP_Text respectsLabel;
    [SerializeField] private Button letPassButton;

    private DwarvenFuneralController funeral;
    private bool open;

    public bool IsOpen => open;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (robButton != null) robButton.onClick.AddListener(() => Choose(FuneralVerb.Rob));
        if (taxButton != null) taxButton.onClick.AddListener(() => Choose(FuneralVerb.Tax));
        if (respectsButton != null) respectsButton.onClick.AddListener(() => Choose(FuneralVerb.PayRespects));
        if (letPassButton != null) letPassButton.onClick.AddListener(() => Choose(FuneralVerb.LetPass));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(DwarvenFuneralController from)
    {
        if (from == null || panelRoot == null) return;
        funeral = from;
        open = true;

        if (titleLabel != null) titleLabel.text = "Funeral Procession";
        if (cargoLabel != null) cargoLabel.text = "Grave goods: " + funeral.Cargo + "g";
        if (robLabel != null) robLabel.text = "Desecrate - take " + funeral.Cargo + "g";

        // Both gates read the controller at OPEN time, the caravan's rule:
        // the column is halted, so the answer cannot go stale under the panel.
        bool held = funeral.OnHeldSegment;
        if (taxButton != null) taxButton.interactable = held;
        if (taxLabel != null)
            taxLabel.text = held
                ? "Take a toll - " + funeral.TollAmount + "g"
                : "Take a toll - not on your stone";

        bool allowed = funeral.RespectsAvailable;
        if (respectsButton != null) respectsButton.interactable = allowed;
        if (respectsLabel != null)
            respectsLabel.text = allowed
                ? "Pay respects"
                : "Pay respects - not for the hand that made it";

        panelRoot.gameObject.SetActive(true);
        panelRoot.alpha = 1f;
        panelRoot.blocksRaycasts = true;
    }

    private void Choose(FuneralVerb verb)
    {
        var target = funeral;
        // Settling the procession is acting on a thing standing on the board,
        // so it refuses while the world is held (canon 39). Refusing here
        // rather than in Close() matters: the panel must stay OPEN so the
        // choice survives the refusal, and the column stays halted meanwhile
        // -- a refusal must never burn the one verb any more than a misclick
        // does. The caravan panel's own contract, kept to the letter.
        if (target != null && PauseGate.RefuseAt(target.transform.position)) return;
        if (target == null && PauseGate.RefuseAtCore()) return;
        Close();
        target?.ApplyVerb(verb);
    }

    public void Close()
    {
        open = false;
        if (funeral != null) funeral.SetPanelHalt(false);
        funeral = null;
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
    }
}
