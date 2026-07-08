using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand inspector for a single adventurer — shows current HP, class, type,
/// intent, behaviour trait, and carried loot value, plus the adventurer's name if it
/// has one (from named-party tracking). Opened by AdventurerInspectController when the
/// player clicks an adventurer; live-refreshes while open and self-closes when the
/// inspected adventurer despawns or on Escape.
///
/// STUB GATE: the whole feature hides behind UnlockState.AdventurerStats, which is
/// locked by default and flipped on later by the "Study Adventurer Anatomy" research
/// node. Until then Show() is a no-op.
///
/// PREFAB / SCENE SETUP:
///   AdventurerStatsPanel (this script on a parent GameObject)
///   |-- Panel  (start active so Awake can self-hide)
///   |   |-- NameText   (TMP_Text  -> nameText)
///   |   |-- StatsText  (TMP_Text  -> statsText)
///   |   |-- HpFill     (Image, Image Type = Filled -> hpFill, optional)
///   |   |-- CloseButton (Button -> OnClick: OnCloseClicked)
/// </summary>
public class AdventurerStatsPanel : MonoBehaviour
{
    public static AdventurerStatsPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private Image hpFill;
    [Tooltip("Optional. Marks the inspected adventurer's party tracked, so it " +
             "returns like a named party. Label and interactability are code-driven.")]
    [SerializeField] private Button pinPartyButton;

    [Tooltip("Optional. Toggles the camera following the inspected adventurer.")]
    [SerializeField] private Toggle followButton;

    private DungeonAdventurer current;
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (followButton != null) followButton.onValueChanged.AddListener(OnFollowToggled);
        Hide();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Open the panel on an adventurer. No-op while the feature is locked.</summary>
    public void Show(DungeonAdventurer adv)
    {
        if (adv == null) return;
        if (!UnlockState.IsUnlocked(UnlockState.AdventurerStats)) return;

        if (current != null && current != adv)
            DungeonCameraController.Instance?.ClearFollowTargetIf(current.transform);
        current = adv;
        isOpen = true;
        if (panel != null) panel.SetActive(true);
        if (followButton != null)
        {
            followButton.gameObject.SetActive(true);
            followButton.SetIsOnWithoutNotify(false);
        }
        Refresh();
    }

    private void OnFollowToggled(bool on)
    {
        var cam = DungeonCameraController.Instance;
        if (cam == null || current == null) return;
        if (on) cam.SetFollowTarget(current.transform);
        else cam.ClearFollowTargetIf(current.transform);
    }

    public void OnCloseClicked() => Hide();

    /// <summary>Wired to the Pin Party button. Marks the inspected adventurer's
    /// party tracked — it persists and returns like a named party — and banners it.</summary>
    public void OnPinPartyClicked()
    {
        var p = current != null ? current.Party : null;
        if (p == null) return;
        if (p.HasNamedMember()) return;   // named parties are permanent nemeses

        if (p.tracked)
        {
            p.tracked = false;
            PartyBannerManager.Instance?.HideBanner(p);
        }
        else
        {
            p.tracked = true;
            PartyBannerManager.Instance?.ShowBanner(p);
        }
        RefreshPinButton();
    }

    private void RefreshPinButton()
    {
        if (pinPartyButton == null) return;
        var p = current != null ? current.Party : null;
        bool hasParty = p != null;
        pinPartyButton.gameObject.SetActive(hasParty);

        bool named = hasParty && p.HasNamedMember();
        pinPartyButton.interactable = hasParty && !named;

        var t = pinPartyButton.GetComponentInChildren<TMP_Text>(true);
        if (t != null)
            t.text = !hasParty ? "Pin Party"
                   : named ? "Named"
                   : p.tracked ? "Unpin Party"
                               : "Pin Party";
    }

    public void Hide()
    {
        if (current != null) DungeonCameraController.Instance?.ClearFollowTargetIf(current.transform);
        current = null;
        isOpen = false;
        if (panel != null) panel.SetActive(false);
        if (followButton != null) followButton.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen) return;
        if (current == null) { Hide(); return; }   // inspected adventurer despawned
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Hide(); return; }
        if (followButton != null)
            followButton.SetIsOnWithoutNotify(
                DungeonCameraController.Instance != null
                && DungeonCameraController.Instance.IsFollowing(current.transform));
        Refresh();
    }

    private void Refresh()
    {
        if (current == null) return;

        if (nameText != null)
        {
            string n = current.DisplayName;
            bool has = !string.IsNullOrEmpty(n);
            nameText.gameObject.SetActive(has);
            if (has) nameText.text = n;
        }

        float hp = current.CurrentHP, max = current.MaxHP;
        if (hpFill != null) hpFill.fillAmount = max > 0f ? Mathf.Clamp01(hp / max) : 0f;

        if (statsText != null)
            statsText.text =
                $"HP        {hp:0} / {max:0}\n" +
                $"Type      {current.Type}\n" +
                $"Class     {current.FlavorClassName}\n" +
                $"Affinity  {current.Affinity}\n" +
                $"Intent    {current.Intent}\n" +
                $"Trait     {current.Trait}\n" +
                $"Loot      {current.CarriedLootValue}g";

        RefreshPinButton();
    }
}