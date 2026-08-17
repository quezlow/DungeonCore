using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The deep pilgrimage's choice panel (canon 51): Rob, Tax, Let pass, or
/// Bless. Mirrors FuneralActionPanel body for body -- opened only by
/// clicking the column, never pushed by a crossing; ONE VERB PER
/// PILGRIMAGE; closing WITHOUT choosing settles nothing and the column
/// walks on, clickable later, because a misclick must never burn the
/// decision.
///
/// Tax is live only while the lead stands on a held road segment, the
/// caravan's own open-time test. BLESS is always live: unlike Pay Respects
/// there is no implication to gate on -- one verb per party already blocks
/// rob-then-bless, and the director's cadence bounds the standing faucet.
///
/// The column halts while the panel is open (told on open and on close) so
/// the choice is never made against a moving target. Scene-authored by
/// duplicating the funeral panel and relabelling the fourth button (the
/// guide has the steps); closed beside FuneralActionPanel at the head of
/// PauseMenuController's central ESC chain -- the most transient thing that
/// can be open.
/// </summary>
public class PilgrimActionPanel : MonoBehaviour
{
    public static PilgrimActionPanel Instance { get; private set; }

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
    [SerializeField] private Button blessButton;
    [SerializeField] private TMP_Text blessLabel;
    [SerializeField] private Button letPassButton;

    private DwarvenPilgrimageController pilgrimage;
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
        if (robButton != null) robButton.onClick.AddListener(() => Choose(PilgrimVerb.Rob));
        if (taxButton != null) taxButton.onClick.AddListener(() => Choose(PilgrimVerb.Tax));
        if (blessButton != null) blessButton.onClick.AddListener(() => Choose(PilgrimVerb.Bless));
        if (letPassButton != null) letPassButton.onClick.AddListener(() => Choose(PilgrimVerb.LetPass));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(DwarvenPilgrimageController from)
    {
        if (from == null || panelRoot == null) return;
        pilgrimage = from;
        open = true;

        if (titleLabel != null) titleLabel.text = "Deep Pilgrimage";
        if (cargoLabel != null) cargoLabel.text = "Offerings: " + pilgrimage.Cargo + "g";
        if (robLabel != null) robLabel.text = "Rob - take " + pilgrimage.Cargo + "g";

        // The held gate reads the controller at OPEN time, the caravan's
        // rule: the column is halted, so the answer cannot go stale under
        // the panel.
        bool held = pilgrimage.OnHeldSegment;
        if (taxButton != null) taxButton.interactable = held;
        if (taxLabel != null)
            taxLabel.text = held
                ? "Take a toll - " + pilgrimage.TollAmount + "g"
                : "Take a toll - not on your stone";

        if (blessButton != null) blessButton.interactable = true;
        if (blessLabel != null) blessLabel.text = "Give a blessing";

        panelRoot.gameObject.SetActive(true);
        panelRoot.alpha = 1f;
        panelRoot.blocksRaycasts = true;
    }

    private void Choose(PilgrimVerb verb)
    {
        var target = pilgrimage;
        // Settling the pilgrimage is acting on a thing standing on the
        // board, so it refuses while the world is held (canon 39). Refusing
        // here rather than in Close() matters: the panel must stay OPEN so
        // the choice survives the refusal, and the column stays halted
        // meanwhile -- a refusal must never burn the one verb any more than
        // a misclick does. The caravan panel's own contract, kept.
        if (target != null && PauseGate.RefuseAt(target.transform.position)) return;
        if (target == null && PauseGate.RefuseAtCore()) return;
        Close();
        target?.ApplyVerb(verb);
    }

    public void Close()
    {
        open = false;
        if (pilgrimage != null) pilgrimage.SetPanelHalt(false);
        pilgrimage = null;
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
    }
}
