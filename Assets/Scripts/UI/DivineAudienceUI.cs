using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The tier-up divine audience (canon 19A): the screen goes black, the god of the
/// core's own type fades in over it, and it speaks -- about the power the core has
/// been siphoning out of it, and the knowing that comes attached.
///
/// Presentation is a full-screen OVERLAY in the dungeon scene, not a scene load and
/// not a staged vignette. A scene load would have to rebuild every live system for a
/// two-minute beat, and a puppet vignette (the First Blood idiom) needs a body: these
/// gods have none. An opaque overlay also means no camera work at all -- nothing is
/// visible behind it, so there is nothing to frame.
///
/// The clock is stopped for the duration and every wait runs on UNSCALED time. The
/// prior speed is restored exactly (via PauseController.UnpauseGame, which replays
/// the player's own selected scale) rather than forced to 1x -- a player who was
/// running at 5x should not be quietly demoted by a cutscene. A game already paused
/// when the audience begins stays paused when it ends.
///
/// SCENE SETUP: put this component on the persistent manager GameObject and assign
/// the Divine Audience Script asset. With no component or no script assigned there is
/// no audience and NO error at the level-up itself -- the wisp-asset lesson -- so
/// "Print Divine Audience Script" in Commands is the check, and Play logs a named
/// warning the first time it is asked to run without a script.
/// </summary>
public class DivineAudienceUI : MonoBehaviour
{
    public static DivineAudienceUI Instance { get; private set; }

    /// <summary>True while an audience is on screen. Read by the input owners that would
    /// otherwise act through the overlay: the speed keys (which would un-pause the beat),
    /// the journal toggle, and Esc (which the audience owns while it plays).</summary>
    public static bool IsPlaying { get; private set; }

    [Header("Data")]
    [Tooltip("The asset holding every god, every line. Without it there is no audience.")]
    [SerializeField] private DivineAudienceScript script;

    [Header("Type")]
    [Tooltip("Optional. Unassigned falls back to the TMP default font.")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float nameFontSize = 54f;
    [SerializeField] private float epithetFontSize = 26f;
    [SerializeField] private float bodyFontSize = 32f;

    [Header("Pacing (all unscaled - the clock is stopped)")]
    [SerializeField] private float blackoutSeconds = 1.1f;
    [SerializeField] private float manifestSeconds = 2.2f;
    [SerializeField] private float lineFadeSeconds = 0.5f;
    [Tooltip("Shortest a beat can be on screen before a press advances it. Stops one " +
             "impatient double-click from eating two lines.")]
    [SerializeField] private float minBeatSeconds = 0.45f;

    [Header("Fallback manifestation (used when a god has no backdrop sprite)")]
    [Tooltip("Fraction of screen height the glow fills.")]
    [SerializeField] private float glowScale = 0.85f;
    [SerializeField] private float glowPulseSeconds = 4.5f;

    private Canvas canvas;
    private CanvasGroup group;
    private Image blackout;
    private Image manifestation;
    private TMP_Text nameLabel;
    private TMP_Text epithetLabel;
    private TMP_Text bodyLabel;
    private TMP_Text hintLabel;

    private bool playing;
    private bool skipRequested;
    private bool pausedByUs;
    private bool warnedNoScript;

    private static Sprite builtInGlow;

    /// <summary>The script asset, so the journal's lore page renders from the same source
    /// the audience spoke from rather than keeping a second reference to drift.</summary>
    public DivineAudienceScript Script => script;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildOverlay();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        // A destroyed overlay mid-audience must not leave the clock stopped and the
        // static flag stuck true - both are global state that nothing else would clear.
        if (playing) ReleaseClock();
        IsPlaying = false;
        Instance = null;
    }

    // -- Overlay construction (the ScreenFlash precedent: no scene wiring) ---------

    private void BuildOverlay()
    {
        var canvasGo = new GameObject("DivineAudienceOverlay");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above ScreenFlash (32760), so a climax flash cannot paint over a god.
        canvas.sortingOrder = 32761;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // The raycaster is what makes the blackout actually BLOCK the world: the build
        // controller and the selection paths all honour EventSystem.IsPointerOverGameObject,
        // which sees nothing without a GraphicRaycaster on this canvas.
        canvasGo.AddComponent<GraphicRaycaster>();

        group = canvasGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        blackout = MakeImage(canvasGo.transform, "Blackout", Color.black, raycast: true);
        Stretch(blackout.rectTransform);

        manifestation = MakeImage(canvasGo.transform, "Manifestation", Color.clear, raycast: false);
        manifestation.preserveAspect = true;
        var mrt = manifestation.rectTransform;
        mrt.anchorMin = new Vector2(0.5f, 0.5f);
        mrt.anchorMax = new Vector2(0.5f, 0.5f);
        mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.anchoredPosition = new Vector2(0f, 90f);
        mrt.sizeDelta = new Vector2(1080f * glowScale, 1080f * glowScale);

        nameLabel = MakeLabel(canvasGo.transform, "DeityName", nameFontSize, FontStyles.Bold);
        Anchor(nameLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f),
               new Vector2(1400f, 70f));

        epithetLabel = MakeLabel(canvasGo.transform, "DeityEpithet", epithetFontSize, FontStyles.Italic);
        Anchor(epithetLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -212f),
               new Vector2(1400f, 40f));

        bodyLabel = MakeLabel(canvasGo.transform, "AudienceBody", bodyFontSize, FontStyles.Normal);
        Anchor(bodyLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 300f),
               new Vector2(1320f, 260f));

        hintLabel = MakeLabel(canvasGo.transform, "AdvanceHint", 20f, FontStyles.Italic);
        Anchor(hintLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 70f),
               new Vector2(900f, 34f));
        hintLabel.color = new Color(0.62f, 0.60f, 0.56f, 1f);
        hintLabel.text = "click to continue      esc to withdraw";

        canvasGo.SetActive(false);
    }

    private Image MakeImage(Transform parent, string name, Color colour, bool raycast)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = colour;
        img.raycastTarget = raycast;
        return img;
    }

    private TMP_Text MakeLabel(Transform parent, string name, float size, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.color = new Color(0.93f, 0.90f, 0.84f, 1f);
        tmp.text = string.Empty;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    // -- Playing ------------------------------------------------------------------

    /// <summary>Hold the audience for a tier. Returns false when it cannot or should not
    /// run, and the caller loses nothing by ignoring that: the level-up itself has already
    /// happened. Force replays one that is already held (the preview command).</summary>
    public bool Play(LevelTier tier, bool force = false)
    {
        if (playing) return false;

        if (script == null)
        {
            if (!warnedNoScript)
            {
                warnedNoScript = true;
                Debug.LogWarning("[DivineAudience] No script asset assigned - the gods stay silent. "
                               + "Assign one on this component (see Print Divine Audience Script).");
            }
            return false;
        }

        var core = DungeonCore.Instance;
        if (core == null) return false;
        if (!force && DivineAudienceLedger.IsHeld(tier)) return false;

        List<DivineAudienceScript.Beat> beats = script.Compose(core.DungeonType, tier);
        if (beats.Count == 0)
        {
            Debug.LogWarning("[DivineAudience] Nothing to speak for " + core.DungeonType + " at "
                           + tier + ":\n  " + (script.Validate() ?? "tier script or deity row missing."));
            return false;
        }

        // Marked on arrival, not on completion. See DivineAudienceLedger.
        if (!force) DivineAudienceLedger.MarkHeld(tier);

        StartCoroutine(Run(script.DeityFor(core.DungeonType), beats));
        return true;
    }

    /// <summary>Withdraw from the audience early. The record stands; the god came.</summary>
    public void Skip() => skipRequested = true;

    private IEnumerator Run(DivineAudienceScript.Deity god, List<DivineAudienceScript.Beat> beats)
    {
        playing = true;
        IsPlaying = true;
        skipRequested = false;

        HoldClock();

        Color tint = god != null && god.overrideTint
            ? god.tint
            : DungeonCore.ColorFor(DungeonCore.Instance != null
                ? DungeonCore.Instance.DungeonType
                : DungeonType.None);

        if (god != null && god.backdrop != null)
        {
            manifestation.sprite = god.backdrop;
            manifestation.color = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
            // No art yet: a slow radial pulse in the affinity colour. The presence line
            // is what actually describes the god, which is why it is written as
            // description rather than speech.
            manifestation.sprite = BuiltInGlow();
            manifestation.color = new Color(tint.r, tint.g, tint.b, 0f);
        }

        nameLabel.text = string.Empty;
        epithetLabel.text = string.Empty;
        bodyLabel.text = string.Empty;
        nameLabel.color = new Color(tint.r, tint.g, tint.b, 0f);
        SetAlpha(epithetLabel, 0f);
        SetAlpha(bodyLabel, 0f);
        SetAlpha(hintLabel, 0f);

        canvas.gameObject.SetActive(true);
        group.alpha = 0f;
        blackout.color = Color.black;

        // Into the dark first, alone. The god arrives out of nothing.
        yield return FadeGroup(0f, 1f, blackoutSeconds);
        if (!skipRequested) yield return WaitUnscaled(0.35f);

        Coroutine pulse = StartCoroutine(PulseManifestation());
        yield return FadeImage(manifestation, 0f, god != null && god.backdrop != null ? 1f : 0.75f,
                               manifestSeconds);

        for (int i = 0; i < beats.Count && !skipRequested; i++)
        {
            DivineAudienceScript.Beat beat = beats[i];

            // The name card appears with the god's first SPOKEN beat, never over the
            // presence line: what the player is looking at has not introduced itself yet.
            if (!beat.presence && god != null && string.IsNullOrEmpty(nameLabel.text))
            {
                nameLabel.text = (god.deityName ?? string.Empty).ToUpperInvariant();
                epithetLabel.text = god.epithet ?? string.Empty;
                StartCoroutine(FadeText(nameLabel, 0f, 1f, lineFadeSeconds));
                StartCoroutine(FadeText(epithetLabel, 0f, 1f, lineFadeSeconds));
            }

            yield return FadeText(bodyLabel, GetAlpha(bodyLabel), 0f, i == 0 ? 0f : lineFadeSeconds * 0.6f);

            bodyLabel.text = beat.text;
            bodyLabel.fontStyle = beat.presence ? FontStyles.Italic : FontStyles.Normal;
            bodyLabel.color = beat.presence
                ? new Color(0.72f, 0.70f, 0.66f, 0f)
                : new Color(0.93f, 0.90f, 0.84f, 0f);

            yield return FadeText(bodyLabel, 0f, 1f, lineFadeSeconds);
            if (i == 0) StartCoroutine(FadeText(hintLabel, 0f, 1f, lineFadeSeconds));

            yield return WaitForAdvance();
        }

        yield return FadeGroup(group.alpha, 0f, blackoutSeconds * 0.8f);

        if (pulse != null) StopCoroutine(pulse);
        canvas.gameObject.SetActive(false);
        manifestation.sprite = null;
        manifestation.color = Color.clear;   // a null sprite with alpha renders a white quad

        ReleaseClock();
        playing = false;
        IsPlaying = false;
        skipRequested = false;
    }

    // -- Clock --------------------------------------------------------------------

    private void HoldClock()
    {
        // A game already paused stays paused afterwards: the player chose that, and an
        // audience is not a reason to start the world moving again behind the fade.
        pausedByUs = !PauseController.IsGamePaused;
        if (pausedByUs) TimeScaleController.Instance?.SetPaused();
    }

    private void ReleaseClock()
    {
        if (!pausedByUs) return;
        pausedByUs = false;
        // UnpauseGame replays the player's own selected scale. SetNormal would silently
        // demote anyone who was running at 2x or 5x when they pressed level-up.
        if (PauseController.Instance != null) PauseController.Instance.UnpauseGame();
        else TimeScaleController.Instance?.OnGameUnpaused();
    }

    // -- Input --------------------------------------------------------------------

    private IEnumerator WaitForAdvance()
    {
        float t = 0f;
        while (!skipRequested)
        {
            t += Time.unscaledDeltaTime;
            if (t >= minBeatSeconds && AdvancePressed()) yield break;
            yield return null;
        }
    }

    private static bool AdvancePressed()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

        var kb = Keyboard.current;
        if (kb == null) return false;
        return kb.spaceKey.wasPressedThisFrame
            || kb.enterKey.wasPressedThisFrame
            || kb.numpadEnterKey.wasPressedThisFrame;
    }

    // -- Fades (all unscaled: the clock is stopped) --------------------------------

    private IEnumerator FadeGroup(float from, float to, float seconds)
    {
        if (seconds <= 0f) { group.alpha = to; yield break; }
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
            yield return null;
        }
        group.alpha = to;
    }

    private IEnumerator FadeImage(Image img, float from, float to, float seconds)
    {
        if (img == null) yield break;
        if (seconds <= 0f) { SetImageAlpha(img, to); yield break; }
        float t = 0f;
        while (t < seconds && !skipRequested)
        {
            t += Time.unscaledDeltaTime;
            SetImageAlpha(img, Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        SetImageAlpha(img, to);
    }

    private IEnumerator FadeText(TMP_Text label, float from, float to, float seconds)
    {
        if (label == null) yield break;
        if (seconds <= 0f) { SetAlpha(label, to); yield break; }
        float t = 0f;
        while (t < seconds && !skipRequested)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(label, Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        SetAlpha(label, to);
    }

    private IEnumerator PulseManifestation()
    {
        // Breath, not animation. Scale only - alpha belongs to the fades above, which
        // would otherwise fight this loop for the same channel.
        RectTransform rt = manifestation.rectTransform;
        Vector3 baseScale = Vector3.one;
        float t = 0f;
        while (true)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f + 0.035f * Mathf.Sin(t * (Mathf.PI * 2f) / Mathf.Max(0.5f, glowPulseSeconds));
            rt.localScale = baseScale * k;
            yield return null;
        }
    }

    private static void SetAlpha(TMP_Text label, float a)
    {
        Color c = label.color;
        label.color = new Color(c.r, c.g, c.b, a);
    }

    private static float GetAlpha(TMP_Text label) => label.color.a;

    private static void SetImageAlpha(Image img, float a)
    {
        Color c = img.color;
        img.color = new Color(c.r, c.g, c.b, a);
    }

    /// <summary>A soft radial disc, generated once (the DungeonProjectile bolt precedent).
    /// Stands in for a god until the six manifestation illustrations exist.</summary>
    private static Sprite BuiltInGlow()
    {
        if (builtInGlow != null) return builtInGlow;
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f, radius = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(1f - d / radius);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * a));   // steep falloff: a core, not a disc
            }
        tex.Apply();
        builtInGlow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return builtInGlow;
    }
}
