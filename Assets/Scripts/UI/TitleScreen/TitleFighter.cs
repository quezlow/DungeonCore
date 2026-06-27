using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A title-screen diorama actor (monster OR adventurer): a UI Image with simple HP,
/// horizontal movement, facing, an attack lunge, a hit flash, and a fade-out death.
/// It has NO autonomous AI of its own — TitleEncounterDirector drives it as a puppet.
///
/// SETUP: put this on a UI Image anchored BOTTOM-CENTRE (pivot 0.5, 0) under the title
/// canvas, with its Y set to the ground line. Movement is horizontal; the Y is preserved.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TitleFighter : MonoBehaviour
{
    [SerializeField] private Image image;
    [Tooltip("Enable if the source art faces LEFT by default.")]
    [SerializeField] private bool spriteFacesLeft = false;
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.5f, 0.5f, 1f);
    [SerializeField] private float hitFlashTime = 0.09f;
    [SerializeField] private float lungeDistance = 16f;
    [SerializeField] private float lungeTime = 0.11f;

    private RectTransform rt;
    private float currentHP = 30f;
    private float attackDamage = 6f;
    private int facing = 1;
    private float baseScaleMag = 1f;
    private Coroutine flashCo;

    public float AttackDamage => attackDamage;
    public bool IsDead => currentHP <= 0f;
    public float X => rt.anchoredPosition.x;
    public float HalfWidth => rt.sizeDelta.x * 0.5f;

    private void Awake()
    {
        rt = (RectTransform)transform;
        if (image == null) image = GetComponent<Image>();
        float sx = Mathf.Abs(rt.localScale.x);
        baseScaleMag = sx < 0.0001f ? 1f : sx;
    }

    /// <summary>Reset for a fresh appearance: sprite, height (aspect-preserved), HP, damage.</summary>
    public void Configure(Sprite sprite, float height, float hp, float damage)
    {
        currentHP = hp;
        attackDamage = damage;
        if (image != null)
        {
            var c = image.color; c.a = 1f; image.color = c;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                float aspect = sprite.rect.height > 0.01f ? sprite.rect.width / sprite.rect.height : 1f;
                rt.sizeDelta = new Vector2(height * aspect, height);
            }
        }
    }

    public void Show() { gameObject.SetActive(true); }
    public void Hide() { gameObject.SetActive(false); }
    public void SetX(float x) { rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y); }

    public void Face(int dir)
    {
        if (dir != 0) facing = dir > 0 ? 1 : -1;
        float sign = facing >= 0 ? 1f : -1f;
        if (spriteFacesLeft) sign = -sign;
        var ls = rt.localScale;
        ls.x = baseScaleMag * sign;
        rt.localScale = ls;
    }

    public void FaceToward(float x) { Face(x >= X ? 1 : -1); }

    /// <summary>Step toward targetX at speed; faces travel direction. Returns true when within eps.</summary>
    public bool StepToward(float targetX, float speed, float dt, float eps = 4f)
    {
        float x = rt.anchoredPosition.x;
        float d = targetX - x;
        if (Mathf.Abs(d) <= eps) return true;
        float step = Mathf.Sign(d) * speed * dt;
        if (Mathf.Abs(step) >= Mathf.Abs(d)) x = targetX; else x += step;
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        Face(d >= 0f ? 1 : -1);
        return Mathf.Abs(targetX - rt.anchoredPosition.x) <= eps;
    }

    /// <summary>Apply damage. Flashes if it survives. Returns true if this hit was lethal.</summary>
    public bool TakeHit(float dmg)
    {
        currentHP -= dmg;
        bool dead = IsDead;
        if (!dead && gameObject.activeInHierarchy)
        {
            if (flashCo != null) StopCoroutine(flashCo);
            flashCo = StartCoroutine(Flash());
        }
        return dead;
    }

    private IEnumerator Flash()
    {
        if (image == null) yield break;
        float a = image.color.a;
        image.color = new Color(hitFlashColor.r, hitFlashColor.g, hitFlashColor.b, a);
        yield return new WaitForSecondsRealtime(hitFlashTime);
        if (image != null) { var w = Color.white; w.a = image.color.a; image.color = w; }
        flashCo = null;
    }

    public IEnumerator Lunge(int dir)
    {
        Vector2 start = rt.anchoredPosition;
        Vector2 peak = start + new Vector2(dir * lungeDistance, 0f);
        float t = 0f;
        while (t < lungeTime)
        {
            t += Time.unscaledDeltaTime;
            rt.anchoredPosition = Vector2.Lerp(start, peak, t / lungeTime);
            yield return null;
        }
        t = 0f;
        while (t < lungeTime)
        {
            t += Time.unscaledDeltaTime;
            rt.anchoredPosition = Vector2.Lerp(peak, start, t / lungeTime);
            yield return null;
        }
        rt.anchoredPosition = start;
    }

    public IEnumerator FadeOut()
    {
        if (image == null) { Hide(); yield break; }
        float t = 0f;
        Color c = image.color;
        float a0 = c.a;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(a0, 0f, t / fadeDuration);
            image.color = c;
            yield return null;
        }
        c.a = 0f; image.color = c;
        Hide();
    }
}