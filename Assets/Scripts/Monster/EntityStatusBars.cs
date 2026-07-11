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

    [Header("Nameplate Colours")]
    [Tooltip("Colour of a player-named (non-veteran) monster's name.")]
    [SerializeField] private Color namedColour = new Color(0.91f, 0.87f, 0.78f, 1f);
    [Tooltip("Colour of the veteran star and the name beside it.")]
    [SerializeField] private Color veteranColour = new Color(1f, 0.83f, 0.4f, 1f);

    [Header("Intent Label (optional)")]
    [Tooltip("Separate label above the bars used to show a Pilgrim / Gift-Giver / " +
             "Destroyer intent badge once the Oracle Chamber is unlocked. Assign on " +
             "the adventurer status-bars prefab; leave empty on monster prefabs.")]
    [SerializeField] private TMP_Text intentLabel;

    [Header("Offset above entity pivot")]
    [Tooltip("Fallback / minimum offset. The bars ride the tracked entity's sprite " +
             "top when that stands higher, so tall sprites lift the bars automatically.")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.7f, 0f);
    [Tooltip("Clearance above the sprite's top edge when riding sprite bounds.")]
    [SerializeField] private float spriteClearance = 0.25f;

    [Header("Stubs — enable when stats are implemented")]
    [SerializeField] private bool showStamina = false;
    [SerializeField] private bool showMana = false;

    // ── State ─────────────────────────────────────────────────────

    private Canvas barsCanvas;
    private Transform trackedEntity;
    private SpriteRenderer trackedSprite;
    private Color defaultLabelColour = Color.white;   // the prefab's own label colour (boss styling)

    private const string VeteranStar = "\u2605";

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
        if (bossLabel != null)
        {
            defaultLabelColour = bossLabel.color;   // remember the prefab's boss styling
            bossLabel.gameObject.SetActive(false);
        }
        if (intentLabel != null) intentLabel.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        // Remembered Sight gate: the whole world-space canvas is the bars.
        if (barsCanvas == null) barsCanvas = GetComponent<Canvas>();
        bool sight = UnlockState.IsUnlocked("tech.status_bars");
        if (barsCanvas != null && barsCanvas.enabled != sight) barsCanvas.enabled = sight;
        if (!sight) return;
        if (trackedEntity == null) { Destroy(gameObject); return; }
        transform.position = AnchoredPosition();
    }

    // ── Public API ────────────────────────────────────────────────

    public void Initialise(Transform entity)
    {
        trackedEntity = entity;
        trackedSprite = entity != null ? entity.GetComponentInChildren<SpriteRenderer>() : null;
        transform.position = AnchoredPosition();
    }

    /// <summary>Entity position + offset, riding the sprite's top edge when the
    /// sprite stands taller than the fallback offset. Bounds are world-space, so
    /// scale, flips, and animation frames are all accounted for.</summary>
    private Vector3 AnchoredPosition()
    {
        Vector3 basePos = trackedEntity.position;
        float y = basePos.y + worldOffset.y;
        if (trackedSprite != null && trackedSprite.sprite != null)
            y = Mathf.Max(y, trackedSprite.bounds.max.y + spriteClearance);
        return new Vector3(basePos.x + worldOffset.x, y, basePos.z + worldOffset.z);
    }

    /// <summary>— show/hide the stamina + mana bars per entity (only entities
    /// that actually use a resource get its bar). Overrides the prefab defaults.</summary>
    public void ConfigureResourceBars(bool stamina, bool mana)
    {
        showStamina = stamina;
        showMana = mana;
        if (staminaBar != null) staminaBar.gameObject.SetActive(showStamina);
        if (manaBar != null) manaBar.gameObject.SetActive(showMana);
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

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — Show or hide the veteran star above the bars.
    /// Reuses the bossLabel TMP_Text slot since boss and veteran are mutually
    /// exclusive (boss monsters cannot be promoted to veteran).
    /// </summary>
    public void SetVeteranLabel(bool show)
    {
        if (bossLabel == null) return;
        if (show)
        {
            bossLabel.text = "★";
            bossLabel.color = new Color(1f, 0.83f, 0.4f, 1f);  // veteran gold
            bossLabel.gameObject.SetActive(true);
        }
        else
        {
            bossLabel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The monster nameplate. One label, four states:
    ///   boss    - the custom name if renamed, else the boss title (keeps the prefab's styling)
    ///   veteran - star + custom name, or star + type name, in veteran gold
    ///   named   - just the custom name, in the named colour
    ///   plain   - hidden
    /// </summary>
    public void SetMonsterLabel(string bossTitle, bool isVeteran, string customName, string typeName)
    {
        if (bossLabel == null) return;

        bool named = !string.IsNullOrEmpty(customName);

        if (!string.IsNullOrEmpty(bossTitle))
        {
            bossLabel.color = defaultLabelColour;
            bossLabel.text = named ? customName : bossTitle;
            bossLabel.gameObject.SetActive(true);
            return;
        }

        if (isVeteran)
        {
            string shown = named ? customName : typeName;
            bossLabel.color = veteranColour;
            bossLabel.text = string.IsNullOrEmpty(shown) ? VeteranStar : $"{VeteranStar} {shown}";
            bossLabel.gameObject.SetActive(true);
            return;
        }

        if (named)
        {
            bossLabel.color = namedColour;
            bossLabel.text = customName;
            bossLabel.gameObject.SetActive(true);
            return;
        }

        bossLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// Show or hide the party-intent badge above the bars. Pass
    /// null/empty to hide. Uses the dedicated intentLabel slot so it never
    /// collides with the boss / veteran label.
    /// </summary>
    public void SetIntentLabel(string text, Color colour)
    {
        if (intentLabel == null) return;
        if (string.IsNullOrEmpty(text))
        {
            intentLabel.gameObject.SetActive(false);
        }
        else
        {
            intentLabel.text = text;
            intentLabel.color = colour;
            intentLabel.gameObject.SetActive(true);
        }
    }
}