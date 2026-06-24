using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Phase 3 closeout — Group B (#6). Central feedback for REJECTED build actions.
/// Call BuildFeedback.Reject(worldPos, reason) from any failed validation branch.
/// Flashes a translucent red marker + small shake at the spot, plays a denied
/// SFX, and (only if a TMP font is assigned) shows a short floating reason label.
///
/// SETUP
///   - Add ONE instance to the persistent managers GameObject.
///   - Add a sound group whose name matches deniedSfxName to your SoundEffectLibrary.
///   - Assigning a TMP font is OPTIONAL - without it flash + shake + SFX still fire.
///
/// The cooldown stops drag-claim / drag-mine (held sweeps) from spamming flashes.
/// Timing uses unscaled time so feedback still animates while the game is paused.
/// </summary>
public class BuildFeedback : MonoBehaviour
{
    public static BuildFeedback Instance { get; private set; }

    [Header("SFX")]
    [Tooltip("Sound group name in your SoundEffectLibrary. Missing = silent, no crash.")]
    [SerializeField] private string deniedSfxName = "BuildDenied";

    [Header("Flash")]
    [SerializeField] private Color flashColor = new Color(0.9f, 0.15f, 0.15f, 0.6f);
    [SerializeField] private float flashDuration = 0.28f;
    [SerializeField] private float shakeMagnitude = 0.08f;
    [Tooltip("Top-most sorting layer so feedback draws over walls, floor and monsters (still below the screen-space HUD).")]
    [SerializeField] private string sortingLayer = "WorldUI";
    [SerializeField] private int sortingOrder = 200;

    [Header("Reason label (optional)")]
    [Tooltip("Assign a TMP font to show a floating reason. Leave empty to skip the label.")]
    [SerializeField] private TMP_FontAsset reasonFont;
    [SerializeField] private float reasonFontSize = 3f;
    [SerializeField] private Color reasonColor = new Color(1f, 0.4f, 0.4f);

    [Header("Spam guard")]
    [SerializeField] private float cooldown = 0.22f;

    private GameObject flashGO;
    private SpriteRenderer flashSR;
    private Coroutine flashRoutine;
    private float lastRejectTime = -999f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildFlashSprite();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildFlashSprite()
    {
        flashGO = new GameObject("BuildRejectFlash");
        flashGO.transform.SetParent(transform, false);
        flashSR = flashGO.AddComponent<SpriteRenderer>();
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        flashSR.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        flashSR.color = flashColor;
        flashSR.sortingLayerName = sortingLayer;
        flashSR.sortingOrder = sortingOrder;
        flashSR.enabled = false;
    }

    /// <summary>Static entry point. worldPos = where the rejected action was aimed.</summary>
    public static void Reject(Vector3 worldPos, string reason = null)
    {
        if (Instance == null) return;
        Instance.DoReject(worldPos, reason);
    }

    private void DoReject(Vector3 worldPos, string reason)
    {
        if (Time.unscaledTime - lastRejectTime < cooldown) return;
        lastRejectTime = Time.unscaledTime;

        SoundEffectManager.Play(deniedSfxName);

        if (flashSR != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine(worldPos));
        }

        if (reasonFont != null && !string.IsNullOrEmpty(reason))
            SpawnReasonLabel(worldPos, reason);
    }

    private IEnumerator FlashRoutine(Vector3 worldPos)
    {
        flashSR.enabled = true;
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - (t / flashDuration);              // 1 -> 0
            var c = flashColor; c.a = flashColor.a * k;
            flashSR.color = c;
            Vector2 jitter = Random.insideUnitCircle * (shakeMagnitude * k);
            flashGO.transform.position = worldPos + new Vector3(jitter.x, jitter.y, 0f);
            yield return null;
        }
        flashSR.enabled = false;
        flashRoutine = null;
    }

    private void SpawnReasonLabel(Vector3 worldPos, string reason)
    {
        var go = new GameObject("BuildRejectReason");
        go.transform.position = worldPos + new Vector3(0f, 0.6f, 0f);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = reasonFont;
        tmp.text = reason;
        tmp.fontSize = reasonFontSize;
        tmp.color = reasonColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.sortingLayerID = SortingLayer.NameToID(sortingLayer);
        tmp.sortingOrder = sortingOrder + 1;
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(8f, 1.5f);
        StartCoroutine(FloatAndFade(go.transform, tmp));
    }

    private IEnumerator FloatAndFade(Transform t, TMP_Text tmp)
    {
        float dur = 0.9f, e = 0f;
        Vector3 start = t.position;
        Color baseCol = tmp.color;
        while (e < dur)
        {
            e += Time.unscaledDeltaTime;
            float k = e / dur;
            t.position = start + new Vector3(0f, 0.5f * k, 0f);
            tmp.color = new Color(baseCol.r, baseCol.g, baseCol.b, 1f - k);
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }
}