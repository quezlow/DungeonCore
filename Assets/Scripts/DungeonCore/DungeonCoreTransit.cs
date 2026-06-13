using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Lives on the DungeonCore GameObject for the duration of a relocation.
/// Added by DungeonCore.Relocate(), removed when transit completes or is interrupted.
///
/// LIFECYCLE (30 seconds total)
///   t = 0..10s   — sprite pulses + fades out on the origin floor; pathfinding
///                  target is still the origin position.
///   t = 10..20s  — sprite hidden; core's logical position has snapped to the
///                  destination floor (CoreFloorIndex updated at t=10s).
///   t = 20..30s  — sprite fades in at the new position on the destination floor.
///   t = 30s      — transit complete; core fully solid; AdventurerSpawner resumes.
///
/// During the entire 30s window, any adventurer reaching the core's current
/// logical position triggers an INSTANT game-over (bypasses the normal
/// two-strike system — handled in DungeonCore.DestroyCore via IsInTransit).
/// </summary>
[RequireComponent(typeof(DungeonCore))]
public class DungeonCoreTransit : MonoBehaviour
{
    public const float FadeOutDuration = 10f;
    public const float InvisibleDuration = 10f;
    public const float FadeInDuration = 10f;
    public const float TotalDuration = FadeOutDuration + InvisibleDuration + FadeInDuration;

    // ── Pulse Settings ────────────────────────────────────────────
    [Header("Sprite Pulse (during fade-out)")]
    [SerializeField] private float pulseFrequency = 2f;       // pulses per second
    [SerializeField] private float pulseMinAlpha  = 0.3f;     // dip of each pulse

    // ── State ─────────────────────────────────────────────────────
    public bool IsActive    { get; private set; }
    public float Elapsed    { get; private set; }
    public float Remaining  => Mathf.Max(0f, TotalDuration - Elapsed);

    private FloorRoot destinationFloor;
    private Vector3Int destinationCell;
    private Vector3 originWorldPos;
    private Vector3 destinationWorldPos;
    private int originFloorIndex;
    private int destinationFloorIndex;

    private SpriteRenderer spriteRenderer;
    private DungeonCore core;

    // ── Events ────────────────────────────────────────────────────
    public static event Action OnTransitStarted;
    public static event Action OnTransitCompleted;

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Begins the relocation sequence. Called by DungeonCore.Relocate().
    /// The DungeonCoreTransit component should be added immediately before this.
    /// </summary>
    public void Begin(FloorRoot destFloor, Vector3Int destCell)
    {
        if (IsActive)
        {
            Debug.LogWarning("[DungeonCoreTransit] Begin called while already active — ignored.");
            return;
        }

        core = GetComponent<DungeonCore>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        destinationFloor = destFloor;
        destinationCell  = destCell;

        originFloorIndex      = FloorManager.Instance.CoreFloorIndex;
        originWorldPos        = transform.position;
        destinationWorldPos   = destFloor.TileInfluence.CellToWorld(destCell);
        destinationFloorIndex = destFloor.FloorIndex;

        IsActive = true;
        Elapsed  = 0f;

        OnTransitStarted?.Invoke();
        Debug.Log($"[DungeonCoreTransit] Begin: floor {originFloorIndex} → {destinationFloorIndex} at {destCell}");

        StartCoroutine(RunTransit());
    }

    // ── Coroutine ─────────────────────────────────────────────────

    private IEnumerator RunTransit()
    {
        // Phase 1: fade out on origin (with pulse).
        yield return RunPhase(FadeOutDuration, FadeOutTick);

        // Snap core to destination at t=10s.
        SnapToDestination();

        // Phase 2: invisible at destination.
        SetAlpha(0f);
        yield return RunPhase(InvisibleDuration, null);

        // Phase 3: fade in at destination.
        yield return RunPhase(FadeInDuration, FadeInTick);

        Complete();
    }

    private IEnumerator RunPhase(float duration, Action<float> onTick)
    {
        float phaseStart = Elapsed;
        float phaseEnd   = phaseStart + duration;

        while (Elapsed < phaseEnd)
        {
            if (!PauseController.IsGamePaused)
            {
                Elapsed += Time.deltaTime;
                float phaseT = Mathf.Clamp01((Elapsed - phaseStart) / duration);
                onTick?.Invoke(phaseT);
            }
            yield return null;
        }
    }

    private void FadeOutTick(float phaseT)
    {
        // Base alpha goes from 1 → 0 across the phase.
        float baseAlpha = 1f - phaseT;

        // Pulse modulation: sin wave between pulseMinAlpha and 1.
        float pulse = Mathf.Lerp(pulseMinAlpha, 1f,
            (Mathf.Sin(Elapsed * pulseFrequency * Mathf.PI * 2f) + 1f) * 0.5f);

        SetAlpha(baseAlpha * pulse);
    }

    private void FadeInTick(float phaseT)
    {
        SetAlpha(phaseT);
    }

    private void SetAlpha(float a)
    {
        if (spriteRenderer == null) return;
        var c = spriteRenderer.color;
        c.a = Mathf.Clamp01(a);
        spriteRenderer.color = c;
    }

    private void SnapToDestination()
    {
        // Reparent under destination floor for hierarchy clarity (core is global
        // singleton but it makes sense to keep it visually under its floor).
        // Actually — DungeonCore must remain at scene root per architecture rules.
        // So we only move the world position, not the parent.

        transform.position = destinationWorldPos;

        // Update the global core-floor tracker. From this moment, adventurers
        // pathfind toward the destination floor.
        FloorManager.Instance.SetCoreFloor(destinationFloorIndex);

        // Force all adventurers to refresh their paths immediately.
        ForceAllAdventurersRefresh();

        Debug.Log($"[DungeonCoreTransit] Core snapped to floor {destinationFloorIndex} at {destinationCell}");
    }

    private void Complete()
    {
        SetAlpha(1f);
        IsActive = false;

        // Mark this floor as having received the core — used by FloorManager
        // for the "must move core before placing more stairs" gate.
        FloorManager.Instance.MarkCoreRelocationComplete();

        OnTransitCompleted?.Invoke();
        Debug.Log("[DungeonCoreTransit] Transit complete.");

        Destroy(this);
    }

    private static void ForceAllAdventurersRefresh()
    {
        if (FloorManager.Instance == null) return;
        var buf = new System.Collections.Generic.List<DungeonAdventurer>();
        foreach (var floor in FloorManager.Instance.AllFloors)
        {
            if (floor?.Entities == null) continue;
            floor.Entities.FillAll(buf);
            for (int i = 0; i < buf.Count; i++) buf[i].ForceRefreshPath();
        }
    }
}
