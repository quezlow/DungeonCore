using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space status bars displayed above any dungeon entity (monster or adventurer).
///
/// HP bar is fully functional.
/// Stamina and Mana bars are stubbed — visible in the prefab but hidden by default
/// until those stats are implemented on the owning entity.
///
/// PREFAB SETUP:
///   EntityStatusBars (this script, Canvas — World Space, scale ~0.01)
///   └── BarsPanel (RectTransform, vertical layout group)
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

    [Header("Offset above entity pivot")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.7f, 0f);

    [Header("Stubs — enable when stats are implemented")]
    [SerializeField] private bool showStamina = false;
    [SerializeField] private bool showMana = false;

    // ── State ─────────────────────────────────────────────────────

    private Transform trackedEntity;
    private Canvas canvas;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Make sure the canvas doesn't receive raycasts or interfere with input
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        if (TryGetComponent<GraphicRaycaster>(out var raycaster))
            Destroy(raycaster);

        // Apply stub visibility
        if (staminaBar != null) staminaBar.gameObject.SetActive(showStamina);
        if (manaBar != null) manaBar.gameObject.SetActive(showMana);
    }

    private void LateUpdate()
    {
        if (trackedEntity == null) return;

        // Hide rendering when the tracked entity is inactive (its floor is hidden).
        // Use Canvas.enabled instead of GameObject.SetActive so this script keeps
        // running and can re-enable when the entity returns to view.
        bool entityActive = trackedEntity.gameObject.activeInHierarchy;
        if (canvas.enabled != entityActive)
            canvas.enabled = entityActive;

        if (!entityActive) return;

        transform.position = trackedEntity.position + worldOffset;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Call from the owning entity's Start() to attach and initialise bars.
    /// </summary>
    public void Initialise(Transform entity)
    {
        trackedEntity = entity;
        transform.position = entity.position + worldOffset;
    }

    /// <summary>Update HP bar. Pass current and max values.</summary>
    public void SetHP(float current, float max)
    {
        if (hpBar == null) return;
        hpBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    /// <summary>
    /// Update Stamina bar. No-op until showStamina is enabled and
    /// stamina is implemented on the entity.
    /// </summary>
    public void SetStamina(float current, float max)
    {
        if (staminaBar == null || !showStamina) return;
        staminaBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    /// <summary>
    /// Update Mana bar. No-op until showMana is enabled and
    /// mana is implemented on the entity.
    /// </summary>
    public void SetMana(float current, float max)
    {
        if (manaBar == null || !showMana) return;
        manaBar.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }
}