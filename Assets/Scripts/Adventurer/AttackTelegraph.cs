using UnityEngine;

/// <summary>
/// Pre-attack telegraph: a brief windup during which the entity's sprites tint toward a
/// cue colour (a "charging" ramp), then the queued strike fires. Add to any attacker
/// alongside its sprites; DungeonMonster / DungeonAdventurer drive it — they call Begin
/// with the strike as a callback and Cancel if interrupted. Tints via SpriteRenderer.color
/// (a multiply, like DamageFlash), so it needs no shader — a stand-in for real windup
/// animation clips until those exist.
/// </summary>
public class AttackTelegraph : MonoBehaviour
{
    private SpriteRenderer[] renderers;
    private Color[] baseColors;

    private bool winding;
    private float elapsed;
    private float duration;
    private Color tint;
    private System.Action onComplete;

    public bool IsWinding => winding;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].color;
    }

    /// <summary>Begin a windup, then invoke the strike. A duration of 0 fires immediately,
    /// so callers can always route the hit through this.</summary>
    public void Begin(float dur, Color color, System.Action strike)
    {
        if (dur <= 0f) { strike?.Invoke(); return; }
        winding = true;
        elapsed = 0f;
        duration = dur;
        tint = color;
        onComplete = strike;
    }

    /// <summary>Abort an in-progress windup (interrupted) — no strike, colours restored.</summary>
    public void Cancel()
    {
        if (!winding) return;
        winding = false;
        onComplete = null;
        Restore();
    }

    private void Update()
    {
        if (!winding) return;

        elapsed += Time.deltaTime;
        Apply(Mathf.Clamp01(elapsed / duration));

        if (elapsed >= duration)
        {
            winding = false;
            Restore();
            var strike = onComplete;
            onComplete = null;
            strike?.Invoke();
        }
    }

    private void Apply(float k)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].color = Color.Lerp(baseColors[i], tint, k);
    }

    private void Restore()
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].color = baseColors[i];
    }
}