using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The refugee exodus's choice panel (canon 52): Rob or Let pass, and
/// nothing else. Mirrors PilgrimActionPanel with two buttons removed rather
/// than two buttons greyed out -- a Tax the fiction cannot hold and a
/// Shelter the game cannot honour (canon 49's binned D3) would both be
/// worse present-and-dead than absent.
///
/// Opened only by clicking the column, never pushed by a crossing; ONE VERB
/// PER EXODUS; closing WITHOUT choosing settles nothing and the column walks
/// on, clickable later, because a misclick must never burn the decision. The
/// column halts while the panel is open so the choice is never made against
/// a moving target.
///
/// Scene-authored by duplicating the pilgrim panel and deleting the toll and
/// blessing buttons; closed beside the other road panels at the head of
/// PauseMenuController's central ESC chain.
/// </summary>
public class RefugeeActionPanel : MonoBehaviour
{
    public static RefugeeActionPanel Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private CanvasGroup panelRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text cargoLabel;
    [SerializeField] private Button closeButton;

    [Header("Verbs")]
    [SerializeField] private Button robButton;
    [SerializeField] private TMP_Text robLabel;
    [SerializeField] private Button letPassButton;

    private DwarvenRefugeeController exodus;
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
        if (robButton != null) robButton.onClick.AddListener(() => Choose(RefugeeVerb.Rob));
        if (letPassButton != null) letPassButton.onClick.AddListener(() => Choose(RefugeeVerb.LetPass));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(DwarvenRefugeeController from)
    {
        if (from == null || panelRoot == null) return;
        exodus = from;
        open = true;

        // The title carries the weight the verb list cannot: the last
        // exodus off an abandoned hold is a different moment from the first.
        if (titleLabel != null)
            titleLabel.text = exodus.LastOfThem ? "The Last of the Hold" : "Refugees";
        if (cargoLabel != null) cargoLabel.text = "Carrying: " + exodus.Carried + "g";
        if (robLabel != null) robLabel.text = "Rob - take " + exodus.Carried + "g";

        panelRoot.gameObject.SetActive(true);
        panelRoot.alpha = 1f;
        panelRoot.blocksRaycasts = true;
    }

    private void Choose(RefugeeVerb verb)
    {
        var target = exodus;
        // Settling is acting on a thing standing on the board, so it refuses
        // while the world is held (canon 39). Refusing HERE rather than in
        // Close() keeps the panel open so the choice survives the refusal --
        // a refusal must never burn the one verb any more than a misclick.
        if (target != null && PauseGate.RefuseAt(target.transform.position)) return;
        if (target == null && PauseGate.RefuseAtCore()) return;
        Close();
        target?.ApplyVerb(verb);
    }

    public void Close()
    {
        open = false;
        if (exodus != null) exodus.SetPanelHalt(false);
        exodus = null;
        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
    }
}
