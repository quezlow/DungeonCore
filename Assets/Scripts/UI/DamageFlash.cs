using System.Collections;
using UnityEngine;

/// <summary>
/// Brief colour-tint flash when an entity takes damage (Phase 3 closeout #8).
/// Add to a monster or adventurer prefab; it finds every SpriteRenderer beneath
/// it automatically and is driven by DungeonMonster / DungeonAdventurer.TakeDamage
/// via GetComponent&lt;DamageFlash&gt;()?.Flash().
///
/// Tints through SpriteRenderer.color (a multiply), so it works with the built-in
/// sprite material and needs no shader. A saturated colour reads clearly. For a
/// true white-silhouette flash, swap to a flash material inside DoFlash() instead.
/// </summary>
public class DamageFlash : MonoBehaviour
{
    [Tooltip("Tint at the peak of the flash. Multiplies the sprite, so a saturated " +
             "colour (e.g. red) reads clearly against the default material.")]
    [SerializeField] private Color flashColor = new Color(1f, 0.35f, 0.35f, 1f);

    [Tooltip("Total flash time in seconds. Unscaled, so it's consistent at any game speed.")]
    [SerializeField] private float flashDuration = 0.12f;

    private SpriteRenderer[] renderers;
    private Color[] baseColors;
    private Coroutine running;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].color;
    }

    public void Flash()
    {
        if (renderers == null || renderers.Length == 0) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(DoFlash());
    }

    private IEnumerator DoFlash()
    {
        float t = 0f;
        while (t < flashDuration)
        {
            float k = 1f - (t / flashDuration);   // ease from flashColor back to base
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].color = Color.Lerp(baseColors[i], flashColor, k);
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].color = baseColors[i];
        running = null;
    }
}