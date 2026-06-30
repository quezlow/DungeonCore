using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows a "call off the Hero" button while an Inspector escalation is pending, and
/// hides it otherwise. Wire it to a Button placed on the HUD; the button greys out
/// when the player can't afford the bribe.
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
        var esc = InspectorEscalation.Instance;
        bool show = esc != null && esc.DispatchPending;

        if (bribeButton != null && bribeButton.gameObject.activeSelf != show)
            bribeButton.gameObject.SetActive(show);
        if (!show) return;

        bool affordable = DungeonCore.Instance != null && DungeonCore.Instance.Gold >= esc.BribeCost;
        if (bribeButton != null) bribeButton.interactable = affordable;
        if (label != null) label.text = $"Call off the Hero ({esc.BribeCost}g)";
    }

    private void OnBribeClicked() => InspectorEscalation.Instance?.TryBribe();
}