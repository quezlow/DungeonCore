using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonCoreHUD : MonoBehaviour
{
    [Header("Level")]
    [SerializeField] private TextMeshProUGUI levelLabel;

    [Header("Mana")]
    [SerializeField] private Slider manaBar;
    [SerializeField] private TextMeshProUGUI manaLabel;

    [Header("XP")]
    [SerializeField] private Slider xpBar;
    [SerializeField] private TextMeshProUGUI xpLabel;

    [Header("Notoriety")]
    [SerializeField] private TextMeshProUGUI notorietyLabel;

    [Header("Level Up")]
    [SerializeField] private GameObject levelUpButton;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        if (DungeonCore.Instance == null)
        {
            Debug.LogError("DungeonCoreHUD: DungeonCore.Instance is null. Is DungeonCore in the scene?");
            return;
        }

        DungeonCore.Instance.OnManaChanged += HandleManaChanged;
        DungeonCore.Instance.OnXPChanged += HandleXPChanged;
        DungeonCore.Instance.OnLevelUp += HandleLevelUp;
        DungeonCore.Instance.OnLevelUpAvailable += HandleLevelUpAvailable;
        DungeonCore.Instance.OnNotorietyChanged += HandleNotorietyChanged;

        // Sync immediately with current state on enable
        RefreshAll();
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance == null) return;

        DungeonCore.Instance.OnManaChanged -= HandleManaChanged;
        DungeonCore.Instance.OnXPChanged -= HandleXPChanged;
        DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
        DungeonCore.Instance.OnLevelUpAvailable -= HandleLevelUpAvailable;
        DungeonCore.Instance.OnNotorietyChanged -= HandleNotorietyChanged;
    }

    // ── Event Handlers ───────────────────────────────────────────

    private void HandleManaChanged(float current, float max)
    {
        manaBar.value = max > 0 ? current / max : 0f;
        manaLabel.text = $"MANA  {Mathf.FloorToInt(current)} / {Mathf.FloorToInt(max)}";
    }

    private void HandleXPChanged(float current, float toNext)
    {
        xpBar.value = toNext > 0 ? current / toNext : 0f;
        xpLabel.text = $"XP  {Mathf.FloorToInt(current)} / {Mathf.FloorToInt(toNext)}";
    }

    private void HandleLevelUp(int newLevel)
    {
        levelLabel.text = $"LEVEL {newLevel}";
        levelUpButton.SetActive(false);
    }

    private void HandleLevelUpAvailable()
    {
        levelUpButton.SetActive(true);
    }

    private void HandleNotorietyChanged(float notoriety)
    {
        notorietyLabel.text = $"NOTORIETY  {Mathf.FloorToInt(notoriety)}";
    }

    // ── Level Up Button ──────────────────────────────────────────

    /// <summary>Wire this to the LevelUpButton's OnClick in the Inspector.</summary>
    public void OnLevelUpButtonClicked()
    {
        DungeonCore.Instance.ConfirmLevelUp();
    }

    // ── Initial Sync ─────────────────────────────────────────────

    private void RefreshAll()
    {
        var core = DungeonCore.Instance;

        HandleManaChanged(core.CurrentMana, core.MaxMana);
        HandleXPChanged(core.CurrentXP, core.XPToNextLevel);
        HandleLevelUp(core.DungeonLevel);
        HandleNotorietyChanged(core.Notoriety);

        levelUpButton.SetActive(core.LevelUpAvailable);
    }
}