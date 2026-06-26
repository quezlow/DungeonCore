using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Ambient title-screen critter. On Start it picks a random non-boss monster from the
/// registry and shows that monster's body sprite on this UI Image. It wanders left/right
/// along the bottom of the screen, pausing now and then; when the cursor comes within
/// detectionRadius it follows the cursor's X (staying on the ground). The sprite flips to
/// face its travel direction.
///
/// SETUP: this Image should be a child of the title Canvas, anchored BOTTOM-CENTRE, with
/// its Y set so the monster's feet rest on the ground strip. Movement is horizontal only;
/// the Y you set is preserved.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class TitleScreenMonster : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private MonsterDefinitionRegistry registry;
    [SerializeField] private Image image;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private float targetHeight = 170f;   // monster height in canvas units

    [Header("Movement (canvas units / sec)")]
    [SerializeField] private float wanderSpeed = 55f;
    [SerializeField] private float followSpeed = 150f;
    [SerializeField] private float detectionRadius = 280f;
    [SerializeField] private float stopGap = 28f;
    [SerializeField] private float edgePadding = 70f;

    [Header("Wander pauses (sec)")]
    [SerializeField] private float minWalk = 1.5f;
    [SerializeField] private float maxWalk = 4f;
    [SerializeField] private float minPause = 0.6f;
    [SerializeField] private float maxPause = 2f;

    [Header("Facing")]
    [Tooltip("Enable if the source art faces LEFT by default.")]
    [SerializeField] private bool spriteFacesLeft = false;

    private RectTransform rt;
    private int dir = 1;
    private bool paused;
    private float phaseTimer;
    private float baseScaleX = 1f;

    private void Awake()
    {
        rt = (RectTransform)transform;
        if (image == null) image = GetComponent<Image>();
        float sx = Mathf.Abs(rt.localScale.x);
        baseScaleX = sx < 0.0001f ? 1f : sx;
    }

    private void Start()
    {
        PickRandomMonster();
        dir = Random.value < 0.5f ? -1 : 1;
        paused = false;
        phaseTimer = Random.Range(minWalk, maxWalk);
    }

    private void PickRandomMonster()
    {
        if (registry == null || image == null) return;

        var pool = new List<MonsterDefinition>();
        foreach (var d in registry.All)
            if (d != null && !(d is BossVariantDefinition)) pool.Add(d);
        if (pool.Count == 0) return;

        var def = pool[Random.Range(0, pool.Count)];

        Sprite s = null;
        if (def.prefab != null)
        {
            var sr = def.prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) s = sr.sprite;
        }
        if (s == null) s = def.icon;
        if (s == null) return;

        image.sprite = s;
        image.preserveAspect = true;
        float aspect = s.rect.height > 0.01f ? s.rect.width / s.rect.height : 1f;
        rt.sizeDelta = new Vector2(targetHeight * aspect, targetHeight);
    }

    private void Update()
    {
        if (canvasRect == null) return;

        float halfW = canvasRect.rect.width * 0.5f;
        float left = -halfW + edgePadding;
        float right = halfW - edgePadding;

        Vector2 monLocal = canvasRect.InverseTransformPoint(rt.position);

        Vector2 cur = default;
        bool haveCursor = Mouse.current != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, Mouse.current.position.ReadValue(), null, out cur);

        float move = 0f;

        if (haveCursor && Vector2.Distance(monLocal, cur) <= detectionRadius)
        {
            float delta = Mathf.Clamp(cur.x, left, right) - monLocal.x;
            if (Mathf.Abs(delta) > stopGap)
            {
                move = Mathf.Sign(delta) * followSpeed * Time.unscaledDeltaTime;
                dir = delta >= 0f ? 1 : -1;
            }
        }
        else
        {
            phaseTimer -= Time.unscaledDeltaTime;
            if (phaseTimer <= 0f)
            {
                paused = !paused;
                phaseTimer = paused ? Random.Range(minPause, maxPause) : Random.Range(minWalk, maxWalk);
                if (!paused && Random.value < 0.5f) dir = -dir;
            }
            if (monLocal.x <= left) dir = 1;
            else if (monLocal.x >= right) dir = -1;
            if (!paused) move = dir * wanderSpeed * Time.unscaledDeltaTime;
        }

        if (Mathf.Abs(move) > 0.0001f)
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + move, rt.anchoredPosition.y);

        ApplyFacing();
    }

    private void ApplyFacing()
    {
        float sign = (dir >= 0) ? 1f : -1f;
        if (spriteFacesLeft) sign = -sign;
        var ls = rt.localScale;
        ls.x = baseScaleX * sign;
        rt.localScale = ls;
    }
}