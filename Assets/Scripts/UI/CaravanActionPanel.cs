using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The caravan's choice panel: Rob, Tax, or Let pass (canon 19, The Living
/// Holds). Opened only by clicking the wagon -- never pushed by a crossing,
/// which is the anti-spam decision of the arc: the first held crossing gets
/// the vignette, later crossings one alert, and the panel waits to be asked.
///
/// ONE VERB PER CARAVAN. Any of the three buttons settles the wagon for its
/// whole journey and the panel will not open for it again. Closing WITHOUT
/// choosing settles nothing: the wagon walks on and can be clicked later --
/// a misclick must never burn the decision.
///
/// Tax is live only while the wagon stands on a held road segment, checked at
/// open time against the caravan itself; Rob and Let pass are always live.
/// The wagon halts while the panel is open (the caravan is told on open and
/// on close) so the choice is never made against a moving target.
///
/// Scene-authored like MerchantShopUI, whose open/close body this mirrors,
/// and closed FIRST in PauseMenuController's central ESC chain -- the panel
/// is the most transient thing that can be open.
/// </summary>
public class CaravanActionPanel : MonoBehaviour
{
    public static CaravanActionPanel Instance { get; private set; }

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
    [SerializeField] private Button letPassButton;

    private DwarvenCaravanController caravan;
    private bool open;

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
        if (robButton != null) robButton.onClick.AddListener(() => Choose(CaravanVerb.Rob));
        if (taxButton != null) taxButton.onClick.AddListener(() => Choose(CaravanVerb.Tax));
        if (letPassButton != null) letPassButton.onClick.AddListener(() => Choose(CaravanVerb.LetPass));
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Open(DwarvenCaravanController from)
    {
        if (open || from == null || panelRoot == null) return;
        caravan = from;
        open = true;

        if (titleLabel != null) titleLabel.text = "Dwarven Caravan";
        if (cargoLabel != null) cargoLabel.text = "Cargo: " + caravan.Cargo + "g";
        if (robLabel != null) robLabel.text = "Rob - take " + caravan.Cargo + "g";

        bool held = caravan.OnHeldSegment;
        if (taxButton != null) taxButton.interactable = held;
        if (taxLabel != null)
            taxLabel.text = held
                ? "Tax - take " + caravan.TollAmount + "g"
                : "Tax - needs a held road";

        panelRoot.gameObject.SetActive(true);
        panelRoot.alpha = 1f;
        panelRoot.blocksRaycasts = true;
    }

    private void Choose(CaravanVerb verb)
    {
        var target = caravan;
        Close();
        target?.ApplyVerb(verb);
    }

    public void Close()
    {
        if (!open) return;
        open = false;

        // Release the halt; ApplyVerb re-releases harmlessly after a choice.
        caravan?.SetPanelHalt(false);
        caravan = null;

        if (panelRoot != null)
        {
            panelRoot.alpha = 0f;
            panelRoot.blocksRaycasts = false;
            panelRoot.gameObject.SetActive(false);
        }
    }

    /// <summary>Read by PauseMenuController's central ESC chain.</summary>
    public bool IsOpen => open;
}
