using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls game speed via Time.timeScale.
/// Integrates with PauseController — pausing always overrides time scale,
/// and unpausing restores the last selected speed.
///
/// Also owns "hitstop" (a brief impact freeze): it is the single owner of
/// Time.timeScale, so the freeze lives here. See DoKillHitstop / DoBossHitstop
/// (Phase 3 closeout #8).
///
/// SCENE SETUP:
///   Attach to any persistent GameObject (e.g. GameController).
///   Wire four UI buttons to the public methods:
///     SetPaused()  -> pause
///     SetNormal()  -> 1x
///     SetDouble()  -> 2x
///     SetQuintuple() -> 5x
///   Optionally assign the four button references to highlight the active speed.
/// </summary>
public class TimeScaleController : MonoBehaviour
{
    public static TimeScaleController Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Speed Buttons (optional — for active highlight)")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button doubleButton;
    [SerializeField] private Button quintupleButton;

    [Header("Active Button Colours")]
    [SerializeField] private Color activeColour = new Color(1f, 0.85f, 0.2f);  // gold
    [SerializeField] private Color inactiveColour = new Color(0.4f, 0.4f, 0.4f); // grey

    [Header("Hitstop (Phase 3 closeout #8)")]
    [Tooltip("Freeze length on a normal combat kill, in seconds.")]
    [SerializeField] private float killHitstopSeconds = 0.05f;
    [Tooltip("Freeze length on a boss death, in seconds.")]
    [SerializeField] private float bossHitstopSeconds = 0.15f;

    // ── State ─────────────────────────────────────────────────────
    private float selectedScale = 1f; // last scale chosen by player (not counting pause)

    public float SelectedScale => selectedScale;

    private Coroutine hitstopRoutine;
    private float hitstopEndUnscaled;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Sync with whatever PauseController state we start in
        if (PauseController.IsGamePaused)
            ApplyScale(0f);
        else
            ApplyScale(selectedScale);

        RefreshButtons();
    }

    // ── Public API (wire to UI button OnClick) ────────────────────

    private void Update()
    {
        if (PauseMenuController.IsMenuOpen) return;
        // A speed key during a divine audience would un-pause the beat under the
        // overlay and let the world run behind a black screen (canon 19A).
        if (DivineAudienceUI.IsPlaying) return;
        if (Keybinds.IsTextInputActive()) return;
        if (Keybinds.WasPressed(GameAction.SpeedPause)) SetPaused();
        else if (Keybinds.WasPressed(GameAction.SpeedNormal)) SetNormal();
        else if (Keybinds.WasPressed(GameAction.SpeedDouble)) SetDouble();
        else if (Keybinds.WasPressed(GameAction.SpeedQuintuple)) SetQuintuple();
    }

    public void SetPaused()
    {
        // Use PauseController so the rest of the game knows we're paused
        PauseController.Instance?.PauseGame();
        ApplyScale(0f);
        RefreshButtons();
    }

    public void SetNormal()
    {
        selectedScale = 1f;
        ResumeToPreviousScale();
    }

    public void SetDouble()
    {
        selectedScale = 2f;
        ResumeToPreviousScale();
    }

    public void SetQuintuple()
    {
        selectedScale = 5f;
        ResumeToPreviousScale();
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Called by PauseController when the game is unpaused externally
    /// (e.g. via the pause menu) — restores the last selected speed.
    /// </summary>
    public void OnGameUnpaused()
    {
        ApplyScale(selectedScale);
        RefreshButtons();
    }

    private void ResumeToPreviousScale()
    {
        // If we were paused, unpause first
        if (PauseController.IsGamePaused)
            PauseController.Instance?.UnpauseGame();

        ApplyScale(selectedScale);
        RefreshButtons();
    }

    private void ApplyScale(float scale)
    {
        Time.timeScale = scale;
    }

    // ── Hitstop (brief impact freeze) ─────────────────────────────

    public void DoKillHitstop() => Hitstop(killHitstopSeconds);
    public void DoBossHitstop() => Hitstop(bossHitstopSeconds);

    /// <summary>
    /// Brief time freeze for impact, then restores the player's selected speed.
    /// No-op while paused. Overlapping calls extend (not stack) the freeze, capped
    /// at the longest single request. Uses unscaled time so it un-freezes even
    /// though scaled time is stopped.
    /// </summary>
    public void Hitstop(float seconds)
    {
        if (seconds <= 0f) return;
        if (PauseController.IsGamePaused) return;   // don't fight a real pause

        float end = Time.unscaledTime + seconds;
        if (end > hitstopEndUnscaled) hitstopEndUnscaled = end;

        if (hitstopRoutine == null)
            hitstopRoutine = StartCoroutine(HitstopRoutine());
    }

    private System.Collections.IEnumerator HitstopRoutine()
    {
        Time.timeScale = 0f;
        while (Time.unscaledTime < hitstopEndUnscaled)
            yield return null;

        // Restore — respect pause if something paused us during the freeze.
        Time.timeScale = PauseController.IsGamePaused ? 0f : selectedScale;
        hitstopRoutine = null;
    }

    // ── Button Highlight ──────────────────────────────────────────

    private void RefreshButtons()
    {
        bool isPaused = PauseController.IsGamePaused || Time.timeScale == 0f;

        SetButtonColour(pauseButton, isPaused);
        SetButtonColour(normalButton, !isPaused && selectedScale == 1f);
        SetButtonColour(doubleButton, !isPaused && selectedScale == 2f);
        SetButtonColour(quintupleButton, !isPaused && selectedScale == 5f);
    }

    private void SetButtonColour(Button btn, bool isActive)
    {
        if (btn == null) return;
        Color c = isActive ? activeColour : inactiveColour;

        // Setting image.color directly does NOT hold: Unity's Selectable rewrites
        // the target graphic from its ColorBlock on every hover, press, select and
        // deselect, so a direct tint survives only until the next state change --
        // which is why the running speed lost its mark the moment you clicked away.
        // Drive the resting states instead, so the tint IS the button's normal look.
        var cb = btn.colors;
        cb.normalColor = c;
        cb.selectedColor = c;
        cb.disabledColor = c;
        cb.highlightedColor = c * 1.15f;   // keep a little hover lift
        cb.pressedColor = c * 0.85f;
        btn.colors = cb;

        // The target graphic keeps its own base colour; the ColorBlock multiplies
        // over it, so leave it white or the two tints compound.
        var img = btn.targetGraphic as Image;
        if (img != null) img.color = Color.white;

        // Selectable only re-applies its tint on the next state change; bounce the
        // component so the new resting colour shows at once rather than on hover.
        if (btn.isActiveAndEnabled) { btn.enabled = false; btn.enabled = true; }
    }
}