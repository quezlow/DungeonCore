using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Spawned at the top of an attacked sprite. Arcs up and to one side,
/// then falls back down while fading out.
///
/// PREFAB SETUP:
///   FloatingDamageNumber (this script, Canvas — World Space, scale 0.01)
///   └── Text (TextMeshProUGUI — centre aligned, bold, no raycast target)
/// </summary>
[RequireComponent(typeof(Canvas))]
public class FloatingDamageNumber : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [SerializeField] private TextMeshProUGUI label;

    [Header("Arc")]
    [SerializeField] private float arcHeight = 1.0f;  // peak height above spawn
    [SerializeField] private float arcSpread = 0.6f;  // max horizontal offset at peak
    [SerializeField] private float lifetime = 1.1f;  // total duration in seconds

    [Header("Fade")]
    [Tooltip("Fraction of lifetime before fading begins (0.6 = fade starts at 60%).")]
    [SerializeField] private float fadeStartFraction = 0.6f;

    [Header("Colours")]
    [SerializeField] private Color colourMonsterDamage = new Color(1f, 0.35f, 0.35f); // red
    [SerializeField] private Color colourAdventDamage = new Color(1f, 0.85f, 0.2f);  // yellow
    [SerializeField] private Color colourHeal = new Color(0.4f, 1f, 0.4f);  // green

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingLayerName = "WorldUI";
        canvas.sortingOrder = 20;
        if (TryGetComponent<UnityEngine.UI.GraphicRaycaster>(out var gr))
            Destroy(gr);
    }

    // ── Public API ────────────────────────────────────────────────

    public enum DamageType { MonsterHit, AdventurerHit, Heal }

    /// <summary>Initialise and begin the arc animation.</summary>
    public void Initialise(float amount, DamageType type)
    {
        if (label == null)
        {
            Debug.LogError("FloatingDamageNumber: label is not assigned.");
            return;
        }

        label.text = type == DamageType.Heal
            ? $"+{Mathf.CeilToInt(amount)}"
            : Mathf.CeilToInt(amount).ToString();

        label.color = type switch
        {
            DamageType.MonsterHit => colourMonsterDamage,
            DamageType.AdventurerHit => colourAdventDamage,
            DamageType.Heal => colourHeal,
            _ => Color.white
        };

        // Random direction: left or right
        float direction = Random.value > 0.5f ? 1f : -1f;
        StartCoroutine(Arc(direction));
    }

    // ── Animation ─────────────────────────────────────────────────

    private IEnumerator Arc(float direction)
    {
        Vector3 startPos = transform.position;
        Color baseColor = label.color;

        // Compute the peak and landing positions
        // Peak is up and to one side; landing comes back down with same horizontal offset
        float horizontalOffset = arcSpread * direction;
        Vector3 peakPos = startPos + new Vector3(horizontalOffset, arcHeight, 0f);
        Vector3 landPos = startPos + new Vector3(horizontalOffset, -arcHeight * 0.3f, 0f);

        float elapsed = 0f;

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);

            // Quadratic bezier: start → peak → land
            // B(t) = (1-t)²·P0 + 2(1-t)t·P1 + t²·P2
            float oneMinusT = 1f - t;
            transform.position =
                (oneMinusT * oneMinusT) * startPos +
                (2f * oneMinusT * t) * peakPos +
                (t * t) * landPos;

            // Fade during the tail of the animation
            float fadeT = Mathf.InverseLerp(fadeStartFraction, 1f, t);
            label.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - fadeT);

            yield return null;
        }

        Destroy(gameObject);
    }
}