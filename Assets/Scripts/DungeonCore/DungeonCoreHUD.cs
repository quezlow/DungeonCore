using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonCoreHUD : MonoBehaviour
{
    [Header("Level Panel")]
    [SerializeField] private TextMeshProUGUI levelValueLabel;
    [SerializeField] private TextMeshProUGUI notorietyValueLabel;
    [SerializeField] private TMP_Text reputationValueLabel;
    [SerializeField] private GameObject levelUpButton;
    [SerializeField] private Image levelUpButtonImage;

    [Header("Mana Orb")]
    [SerializeField] private Image manaOrbFill;
    [SerializeField] private TextMeshProUGUI manaOrbPercent;
    [SerializeField] private TextMeshProUGUI manaOrbNumeric;
    [SerializeField] private TextMeshProUGUI manaRegenLabel;

    [Header("XP Orb")]
    [SerializeField] private Image xpOrbFill;
    [SerializeField] private TextMeshProUGUI xpOrbPercent;
    [SerializeField] private TextMeshProUGUI xpOrbNumeric;

    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 0.8f;      // cycles per second (lower = slower)
    [SerializeField] private float pulseMinAlpha = 0.15f;
    [SerializeField] private float pulseMaxAlpha = 0.40f;

    [Header("Gold")]   // label now sits on the Level Panel status board;
                       // the old Materials panel is the Pattern Codex
    [SerializeField] private TMP_Text goldValueLabel;

    [Header("Research")]
    [SerializeField] private TMP_Text researchValueLabel;

    [Header("Capacity")]
    [SerializeField] private TMP_Text capacityLabel;

    private Coroutine pulseCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (DungeonCore.Instance == null)
        {
            Debug.LogError("DungeonCoreHUD: DungeonCore.Instance is null. Is DungeonCore in the scene?");
            return;
        }

        // Carry the ceremony's affinity into the HUD: the mana orb wears the
        // element's colour chosen at selection.
        if (manaOrbFill != null)
            manaOrbFill.color = DungeonCore.ColorFor(DungeonCore.Instance.DungeonType);

        DungeonCore.Instance.OnManaChanged += HandleManaChanged;
        DungeonCore.Instance.OnXPChanged += HandleXPChanged;
        DungeonCore.Instance.OnLevelChanged += HandleLevelUp;
        DungeonCore.Instance.OnLevelUpAvailable += HandleLevelUpAvailable;
        DungeonCore.Instance.OnNotorietyChanged += HandleNotorietyChanged;
        DungeonCore.Instance.OnReputationChanged += HandleReputationChanged;
        DungeonCore.Instance.OnGoldChanged += HandleGoldChanged;
        RoomEffectCensus.OnCensusChanged += HandleCensusChanged;
        DungeonCore.Instance.OnResearchChanged += HandleResearchChanged;
        DungeonCore.Instance.OnCapacityChanged += HandleCapacityChanged;
        DungeonCore.Instance.OnManaRegenChanged += HandleManaRegenChanged;

        RefreshAll();
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance == null) return;

        DungeonCore.Instance.OnManaChanged -= HandleManaChanged;
        DungeonCore.Instance.OnXPChanged -= HandleXPChanged;
        DungeonCore.Instance.OnLevelChanged -= HandleLevelUp;
        DungeonCore.Instance.OnLevelUpAvailable -= HandleLevelUpAvailable;
        DungeonCore.Instance.OnNotorietyChanged -= HandleNotorietyChanged;
        DungeonCore.Instance.OnReputationChanged -= HandleReputationChanged;
        DungeonCore.Instance.OnGoldChanged -= HandleGoldChanged;
        RoomEffectCensus.OnCensusChanged -= HandleCensusChanged;
        DungeonCore.Instance.OnResearchChanged -= HandleResearchChanged;
        DungeonCore.Instance.OnCapacityChanged -= HandleCapacityChanged;
        DungeonCore.Instance.OnManaRegenChanged -= HandleManaRegenChanged;
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleManaChanged(float current, float max)
    {
        float pct = max > 0 ? current / max : 0f;
        manaOrbFill.fillAmount = pct;
        // Tint here, not once at Start: on load the affinity is restored a beat
        // after the HUD wakes, so a one-time Start tint shows the default. This
        // refreshes every mana tick and self-corrects within a frame of load.
        if (DungeonCore.Instance != null)
            manaOrbFill.color = DungeonCore.ColorFor(DungeonCore.Instance.DungeonType);
        manaOrbPercent.text = $"{Mathf.RoundToInt(pct * 100)}%";
        manaOrbNumeric.text = $"{Mathf.FloorToInt(current)} / {Mathf.FloorToInt(max)}";
    }

    private void HandleManaRegenChanged(float regenPerSecond)
    {
        if (manaRegenLabel != null)
            manaRegenLabel.text = $"+{regenPerSecond:0.0}/s";
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
        levelValueLabel.text = DungeonCore.Instance.LevelDisplayName;
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

    private void HandleReputationChanged(float reputation)
    {
        if (reputationValueLabel != null)
            reputationValueLabel.text = Mathf.FloorToInt(reputation).ToString();
    }

    private void HandleCapacityChanged(int used, int max)
    {
        if (capacityLabel != null)
            capacityLabel.text = $"{max - used}/{max}";
    }

    private void HandleGoldChanged(int gold)
    {
        if (goldValueLabel == null) return;
        int cap = RoomEffectCensus.GoldCap;
        goldValueLabel.text = cap == int.MaxValue
            ? "Gold: " + gold.ToString()
            : "Gold: " + gold.ToString() + " / " + cap.ToString();
    }

    private void HandleCensusChanged()
    {
        if (DungeonCore.Instance != null) HandleGoldChanged(DungeonCore.Instance.Gold);
    }

    private void HandleResearchChanged(int points)
    {
        if (researchValueLabel != null)
            researchValueLabel.text = "Research: " + points.ToString();
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
            if (!PauseController.IsGamePaused)
                t += Time.deltaTime * pulseSpeed;

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
        HandleManaRegenChanged(DungeonCore.Instance.CurrentManaRegen);
        HandleXPChanged(core.CurrentXP, core.XPToNextLevel);
        HandleLevelUp(core.DungeonLevel);
        HandleNotorietyChanged(core.Notoriety);
        HandleReputationChanged(core.Reputation);
        HandleCapacityChanged(core.UsedCapacity, core.MaxCapacity);
        HandleGoldChanged(core.Gold);
        HandleResearchChanged(core.Research);

        if (core.LevelUpAvailable)
            HandleLevelUpAvailable();
        else
            levelUpButton.SetActive(false);
    }
}