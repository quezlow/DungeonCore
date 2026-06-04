using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space status bars displayed above any dungeon entity (monster or adventurer).
///
/// HP bar is fully functional.
/// Stamina and Mana bars are stubbed — visible in the prefab but hidden by default
/// until those stats are implemented on the owning entity.
///
/// DAY 28 — Optional boss label sits above the bars. Hidden by default;
/// DungeonMonster calls SetBossLabel() in Start() when the monster is a boss.
///
/// PREFAB SETUP:
///   EntityStatusBars (this script, Canvas — World Space, scale ~0.01)
///   └── BarsPanel (RectTransform, vertical layout group)
///       ├── BossLabel   (TMP_Text — assigned to bossLabel, hidden by default)
///       ├── HPBar       (Slider, interactable OFF)
///       │   └── Fill (Image — green)
///       ├── StaminaBar  (Slider, interactable OFF) — initially hidden
///       │   └── Fill (Image — yellow)
///       └── ManaBar     (Slider, interactable OFF) — initially hidden
///           └── Fill (Image — blue)
///
/// Attach this to an entity by calling Initialise() from their Start().
/// </summary>
[RequireComponent(typeof(Canvas))]
public class EntityStatusBars : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────

    [Header("Bar References")]
    [SerializeField] private Slider hpBar;
    [SerializeField] private Slider staminaBar; // stubbed
    [SerializeField] private Slider manaBar;    // stubbed

    [Header("Boss Label (optional)")]
    [Tooltip("If assigned, SetBossLabel() will populate and show this. " +
             "Leave unassigned on non-boss-capable entity prefabs.")]
    [SerializeField] private TMP_Text bossLabel;

    [Header("Offset above entity pivot")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.7f, 0f);

    [Header("Stubs — enable when stats are implemented")]
    [SerializeField] private bool showStamina = false;
    [SerializeField] private bool showMana = false;

    // ── State ─────────────────────────────────────────────────────

    private Transform trackedEntity;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;       // ← unlock per-canvas sorting
        canvas.sortingLayerName = "WorldUI"; // ← needs to exist in Project Settings
        canvas.sortingOrder = 10;

        if (TryGetComponent<GraphicRaycaster>(out var raycaster))
            Destroy(raycaster);

        if (staminaBar != null) staminaBar.gameObject.SetActive(showStamina);
        if (manaBar != null) manaBar.gameObject.SetActive(showMana);
        if (bossLabel != null) bossLabel.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (trackedEntity == null) return;
        transform.position = trackedEntity.position + worldOffset;
    }

    // ── Public API ────────────────────────────────────────────────

    public void Initialise(Transform entity)
    {
        trackedEntity = entity;
        transform.position = entity.position + worldOffset;
    }

    public void SetHP(float current, float max)
    {
        if (hpBar == null) return;
        hpBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    public void SetStamina(float current, float max)
    {
        if (staminaBar == null || !showStamina) return;
        staminaBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    public void SetMana(float current, float max)
    {
        if (manaBar == null || !showMana) return;
        manaBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    /// <summary>
    /// Show or hide the boss title above the bars. Pass empty/null to hide.
    /// Called by DungeonMonster.Start() when the monster has a BossVariantDefinition.
    /// </summary>
    public void SetBossLabel(string text)
    {
        if (bossLabel == null) return;

        if (string.IsNullOrEmpty(text))
        {
            bossLabel.gameObject.SetActive(false);
        }
        else
        {
            bossLabel.text = text;
            bossLabel.gameObject.SetActive(true);
        }
    }
}