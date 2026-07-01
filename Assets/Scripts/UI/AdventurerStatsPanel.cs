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

    private DungeonAdventurer current;
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Open the panel on an adventurer. No-op while the feature is locked.</summary>
    public void Show(DungeonAdventurer adv)
    {
        if (adv == null) return;
        if (!UnlockState.IsUnlocked(UnlockState.AdventurerStats)) return;

        current = adv;
        isOpen = true;
        if (panel != null) panel.SetActive(true);
        Refresh();
    }

    public void OnCloseClicked() => Hide();

    public void Hide()
    {
        current = null;
        isOpen = false;
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen) return;
        if (current == null) { Hide(); return; }   // inspected adventurer despawned
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Hide(); return; }
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
                $"Class     {current.Class}\n" +
                $"Intent    {current.Intent}\n" +
                $"Trait     {current.Trait}\n" +
                $"Loot      {current.CarriedLootValue}g";
    }
}