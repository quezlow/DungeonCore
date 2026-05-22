using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonCoreHUD : MonoBehaviour
{
    [Header("Level Panel")]
    [SerializeField] private TextMeshProUGUI levelValueLabel;
    [SerializeField] private TextMeshProUGUI notorietyValueLabel;
    [SerializeField] private GameObject levelUpButton;
    [SerializeField] private Image levelUpButtonImage;

    [Header("Mana Orb")]
    [SerializeField] private Image manaOrbFill;
    [SerializeField] private TextMeshProUGUI manaOrbPercent;
    [SerializeField] private TextMeshProUGUI manaOrbNumeric;

    [Header("XP Orb")]
    [SerializeField] private Image xpOrbFill;
    [SerializeField] private TextMeshProUGUI xpOrbPercent;
    [SerializeField] private TextMeshProUGUI xpOrbNumeric;

    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 0.8f;      // cycles per second (lower = slower)
    [SerializeField] private float pulseMinAlpha = 0.15f;
    [SerializeField] private float pulseMaxAlpha = 0.40f;

    private Coroutine pulseCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────

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

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleManaChanged(float current, float max)
    {
        float pct = max > 0 ? current / max : 0f;
        manaOrbFill.fillAmount = pct;
        manaOrbPercent.text = $"{Mathf.RoundToInt(pct * 100)}%";
        manaOrbNumeric.text = $"{Mathf.FloorToInt(current)} / {Mathf.FloorToInt(max)}";
    }

    private void HandleXPChanged(float current, float toNext)
    {
        float pct = toNext > 0 ? current / toNext : 0f;
        xpOrbFill.fillAmount = pct;
        xpOrbPercent.text = $"{Mathf.RoundToInt(pct * 100)}%";
        xpOrbNumeric.text = $"{Mathf.FloorToInt(current)} / {Mathf.FloorToInt(toNext)}";
    }

    private void HandleLevelUp(int newLevel)
    {
        levelValueLabel.text = newLevel.ToString();
        StopLevelUpPulse();
    }

    private void HandleLevelUpAvailable()
    {
        levelUpButton.SetActive(true);
        StartLevelUpPulse();
    }

    private void HandleNotorietyChanged(float notoriety)
    {
        notorietyValueLabel.text = Mathf.FloorToInt(notoriety).ToString();
    }

    // ── Level Up Button ───────────────────────────────────────────

    /// <summary>Wire to LevelUpButton's OnClick in the Inspector.</summary>
    public void OnLevelUpButtonClicked()
    {
        DungeonCore.Instance.ConfirmLevelUp();
    }

    // ── Pulse ─────────────────────────────────────────────────────

    private void StartLevelUpPulse()
    {
        if (pulseCoroutine != null) StopCoroutine(pulseCoroutine);
        pulseCoroutine = StartCoroutine(PulseRoutine());
    }

    private void StopLevelUpPulse()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }

        levelUpButton.SetActive(false);

        // Reset button to base alpha so it's clean if re-shown later
        if (levelUpButtonImage != null)
        {
            Color c = levelUpButtonImage.color;
            levelUpButtonImage.color = new Color(c.r, c.g, c.b, pulseMinAlpha);
        }
    }

    private IEnumerator PulseRoutine()
    {
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime * pulseSpeed;
            // Sine wave: 0→1→0 smooth cycle
            float alpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f);
            if (levelUpButtonImage != null)
            {
                Color c = levelUpButtonImage.color;
                levelUpButtonImage.color = new Color(c.r, c.g, c.b, alpha);
            }
            yield return null;
        }
    }

    // ── Initial Sync ──────────────────────────────────────────────

    private void RefreshAll()
    {
        var core = DungeonCore.Instance;

        HandleManaChanged(core.CurrentMana, core.MaxMana);
        HandleXPChanged(core.CurrentXP, core.XPToNextLevel);
        HandleLevelUp(core.DungeonLevel);
        HandleNotorietyChanged(core.Notoriety);

        if (core.LevelUpAvailable)
            HandleLevelUpAvailable();
        else
            levelUpButton.SetActive(false);
    }
}