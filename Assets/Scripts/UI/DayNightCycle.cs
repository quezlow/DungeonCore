using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the day/night cycle. Fires events on phase transitions.
/// Visual ambient shift uses a full-screen UI overlay panel that fades
/// to a dark blue tint at night (works with built-in render pipeline).
///
/// SCENE SETUP:
///   1. Add this script to any persistent GameObject (e.g. GameController)
///   2. In UICanvas_Dungeon, create a Panel:
///      - Stretch to fill the whole canvas (anchor: stretch/stretch)
///      - Image colour: (0, 10, 40, 0) — dark blue, fully transparent
///      - Raycast Target: OFF
///      - Name it "NightOverlay"
///   3. Assign the Panel's Image component to the overlayImage slot
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Durations (seconds)")]
    [SerializeField] private float dayDuration   = 180f;
    [SerializeField] private float nightDuration = 60f;

    [Header("Night Overlay (UI Image — full screen, Raycast Target OFF)")]
    [SerializeField] private Image overlayImage;

    [SerializeField] private Color overlayDay   = new Color(0f,   0.04f, 0.16f, 0f);    // transparent
    [SerializeField] private Color overlayNight = new Color(0f,   0.04f, 0.16f, 0.55f); // dark blue tint

    [SerializeField] private float transitionDuration = 5f;

    [Header("HUD")]
    [SerializeField] private DayNightHUD hud;

    // ── Events ────────────────────────────────────────────────────
    public event Action OnDayStarted;
    public event Action OnNightStarted;

    // ── State ─────────────────────────────────────────────────────
    public enum Phase { Day, Night }
    public Phase CurrentPhase { get; private set; } = Phase.Day;

    private float timer       = 0f;
    private float transitionT = 1f;
    private Color fromColour;
    private Color toColour;

    public float DayDuration   => dayDuration;
    public float NightDuration => nightDuration;
    public float PhaseProgress => CurrentPhase == Phase.Day
        ? timer / dayDuration
        : timer / nightDuration;

    public bool IsDay   => CurrentPhase == Phase.Day;
    public bool IsNight => CurrentPhase == Phase.Night;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        fromColour = overlayDay;
        toColour   = overlayDay;
        ApplyOverlay(overlayDay);
        OnDayStarted?.Invoke();
        hud?.Refresh(Phase.Day, 0f, dayDuration);
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        timer += Time.deltaTime;

        // Smooth overlay transition
        if (transitionT < 1f)
        {
            transitionT += Time.deltaTime / transitionDuration;
            ApplyOverlay(Color.Lerp(fromColour, toColour, Mathf.Clamp01(transitionT)));
        }

        float currentDuration = CurrentPhase == Phase.Day ? dayDuration : nightDuration;

        if (timer >= currentDuration)
        {
            timer = 0f;
            SwitchPhase();
        }

        hud?.Refresh(CurrentPhase, timer, currentDuration);
    }

    // ── Phase Switching ───────────────────────────────────────────

    private void SwitchPhase()
    {
        if (CurrentPhase == Phase.Day)
        {
            CurrentPhase = Phase.Night;
            StartTransition(overlayNight);
            OnNightStarted?.Invoke();
            Debug.Log("[DayNightCycle] Night has fallen.");
        }
        else
        {
            CurrentPhase = Phase.Day;
            StartTransition(overlayDay);
            OnDayStarted?.Invoke();
            Debug.Log("[DayNightCycle] Dawn has broken.");
        }
    }

    private void StartTransition(Color target)
    {
        fromColour  = overlayImage != null ? overlayImage.color : overlayDay;
        toColour    = target;
        transitionT = 0f;
    }

    private void ApplyOverlay(Color colour)
    {
        if (overlayImage != null)
            overlayImage.color = colour;
    }

    public DayNightSaveData GetSaveData() => new DayNightSaveData
    {
        phase = CurrentPhase,
        timer = this.timer
    };

    public void LoadSaveData(DayNightSaveData data)
    {
        CurrentPhase = data.phase;
        timer = data.timer;

        // Apply the correct overlay colour immediately with no transition.
        // transitionT = 1 tells Update() the lerp is already complete.
        Color targetColour = CurrentPhase == Phase.Night ? overlayNight : overlayDay;
        fromColour = targetColour;
        toColour = targetColour;
        transitionT = 1f;
        ApplyOverlay(targetColour);

        // Sync the HUD to the restored time.
        float currentDuration = CurrentPhase == Phase.Day ? dayDuration : nightDuration;
        hud?.Refresh(CurrentPhase, timer, currentDuration);

        // OnDayStarted / OnNightStarted are intentionally NOT fired here.
        // They already fired during Start() for the default Day phase.
        // Systems should read IsDay / IsNight directly rather than relying
        // solely on these events for state that persists across a load.
    }
}

[Serializable]
public class DayNightSaveData
{
    public DayNightCycle.Phase phase;
    public float timer;
}