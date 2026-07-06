using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the single HUD "bribe" button for both escalation sources it can pay off:
/// an Inspector's pending Hero dispatch (a short, real-time fuse - takes priority)
/// and the Mercenary Company's active ultimatum. Shows the button while either is
/// live, greys it out when the player can't afford the cost, and routes the click
/// to whichever source is currently offered so the label and the payment always
/// agree. A successful bribe flips that source's state, so the button hides itself
/// on the next frame.
///
/// SCENE SETUP: unchanged - one Button on the HUD wired to this component's
/// bribeButton + label fields. No second button is needed.
/// </summary>
public class BribePromptUI : MonoBehaviour
{
    [SerializeField] private Button bribeButton;
    [SerializeField] private TMP_Text label;

    private void Awake()
    {
        if (bribeButton != null)
        {
            bribeButton.onClick.AddListener(OnBribeClicked);
            bribeButton.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Inspector's pending Hero takes priority - it runs on a short real-time
        // fuse; fall through to the mercenary ultimatum only when no Hero is pending.
        var esc = InspectorEscalation.Instance;
        if (esc != null && esc.DispatchPending)
        {
            ShowPrompt(esc.BribeCost, $"Call off the Hero ({esc.BribeCost}g)");
            return;
        }

        var merc = MercenaryContract.Instance;
        if (merc != null && merc.CanBribe)
        {
            ShowPrompt(merc.BribeCost, $"Pay off the mercenaries ({merc.BribeCost}g)");
            return;
        }

        HidePrompt();
    }

    private void ShowPrompt(int cost, string text)
    {
        if (bribeButton != null && !bribeButton.gameObject.activeSelf)
            bribeButton.gameObject.SetActive(true);

        bool affordable = DungeonCore.Instance != null && DungeonCore.Instance.Gold >= cost;
        if (bribeButton != null) bribeButton.interactable = affordable;
        if (label != null) label.text = text;
    }

    private void HidePrompt()
    {
        if (bribeButton != null && bribeButton.gameObject.activeSelf)
            bribeButton.gameObject.SetActive(false);
    }

    // Route the click to the same source the prompt is currently offering, so the
    // gold paid matches the label shown.
    private void OnBribeClicked()
    {
        var esc = InspectorEscalation.Instance;
        if (esc != null && esc.DispatchPending) { esc.TryBribe(); return; }

        var merc = MercenaryContract.Instance;
        if (merc != null && merc.CanBribe) merc.TryBribe();
    }
}