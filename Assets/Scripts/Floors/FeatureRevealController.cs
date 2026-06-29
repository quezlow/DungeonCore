using UnityEngine;

/// <summary>
/// DAY 31 PART 1 — Per-floor feature reveal coordinator.
///
/// Lives as a sibling component on each floor's hierarchy alongside
/// TerrainFeatureGenerator and TileInfluenceManager. Wired via FloorRoot.
///
/// HOW IT WORKS
///   - Subscribes (in Awake) to TileInfluenceManager.OnTileBecameClaimable.
///   - When a cell enters the claimable ring (4-neighbour of an owned tile),
///     looks it up in TerrainFeatureGenerator. If the cell belongs to a
///     not-yet-revealed feature, reveals the WHOLE feature (per-feature
///     granularity — one cell touched reveals all cells), paints the debug
///     overlay, fires an AlertsLog entry, optionally shows a one-shot banner
///     for the FIRST discovery on this floor, and plays an SFX.
///   - Idempotent: re-firing the event for an already-revealed feature is a
///     no-op. Safe to receive events during save load.
///
/// CATCH-UP
///   RunInitialCatchup(silent) iterates the influence manager's current
///   claimable set and reveals features that already touch it. Called by:
///     - DungeonSaveController.InitializeNewGame() after Floor 0 features
///       are generated on a new game (silent: true — no alerts, no SFX).
///   For loaded saves, reveal state is restored from FloorFeatureSaveData
///   directly inside TerrainFeatureGenerator.LoadFromSave().
///
/// BANNER
///   Uses FeatureAlertBanner (a separate script from BossAlertBanner). The
///   feature banner is purpose-built to stay active in the hierarchy so we
///   can call Show() without the activation/Awake quirks that crashed
///   BossAlertBanner.Show() when called from outside its expected flow.
///
/// ALERT ROUTING
///   - AlertsLog.AddAlert(...) is called for every reveal (silent or not) so
///     the player can click-jump back to discoveries. The 'silent' parameter
///     gates the FeatureAlertBanner pop and the SFX so initial catch-ups
///     are quiet.
/// </summary>
public class FeatureRevealController : MonoBehaviour
{
    [Header("Alert (optional)")]
    [Tooltip("If assigned, the banner pops on each non-silent reveal. " +
             "Leave null to skip banner; AlertsLog entries still fire.")]
    [SerializeField] private FeatureAlertBanner discoveryBanner;

    [Header("SFX")]
    [Tooltip("SoundEffectLibrary key to play on a non-silent reveal. " +
             "Missing clip is fine — SoundEffectManager.Play() is null-safe.")]
    [SerializeField] private string revealSfxKey = "FeatureReveal";

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private TerrainFeatureGenerator features;
    private bool subscribed;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogError($"[FeatureRevealController] No FloorRoot in parent of '{name}'.");
            return;
        }

        influence = floor.TileInfluence;
        features = floor.FeatureGenerator;

        if (influence == null)
        {
            Debug.LogError($"[FeatureRevealController] Floor {floor.FloorIndex} has no TileInfluenceManager.");
            return;
        }
        if (features == null)
        {
            Debug.LogError($"[FeatureRevealController] Floor {floor.FloorIndex} has no TerrainFeatureGenerator.");
            return;
        }

        influence.OnTileBecameClaimable += HandleTileBecameClaimable;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && influence != null)
            influence.OnTileBecameClaimable -= HandleTileBecameClaimable;
    }

    // ── Event Handler ─────────────────────────────────────────────

    private void HandleTileBecameClaimable(Vector3Int cell)
    {
        TryRevealFeatureAtCell(cell, silent: false);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Scans the current claimable set and reveals any features touching it.
    /// Called by DungeonSaveController.InitializeNewGame() after Floor 0
    /// feature generation. Use silent: true to suppress banner and SFX so
    /// pre-existing claimable cells don't all fire discovery banners at once.
    /// </summary>
    public void RunInitialCatchup(bool silent)
    {
        if (influence == null || features == null) return;
        if (!features.HasGenerated) return;

        foreach (var cell in influence.GetClaimableTilesSnapshot())
            TryRevealFeatureAtCell(cell, silent: silent);
    }

    // ── Internals ─────────────────────────────────────────────────

    private void TryRevealFeatureAtCell(Vector3Int cell, bool silent)
    {
        if (features == null || !features.HasGenerated) return;
        if (!features.TryGetFeatureRef(cell, out var fref)) return;

        switch (fref.type)
        {
            case FeatureType.RiverBank:
            case FeatureType.River:
                if (features.IsRiverRevealed(fref.featureId)) return;
                features.RevealRiver(fref.featureId);
                FireAlert(FeatureType.River, fref.featureId, "An underground river has been revealed", silent);
                break;

            case FeatureType.Chamber:
                if (features.IsChamberRevealed(fref.featureId)) return;
                features.RevealChamber(fref.featureId);
                FireAlert(FeatureType.Chamber, fref.featureId, "A cavern has been revealed", silent);
                break;
        }
    }

    private void FireAlert(FeatureType type, int featureId, string baseMessage, bool silent)
    {
        if (floor == null) return;
        int floorIdx = floor.FloorIndex;
        Vector3 worldPos = features.GetFeatureCenterWorld(type, featureId);
        string message = $"{baseMessage} on Floor {floorIdx + 1}";

        // Log entry — fires for both silent and noisy reveals so click-jump
        // history is complete.
        AlertsLog.Instance?.AddAlert(message, worldPos, floorIdx, AlertCategory.Discovery);

        if (silent) return;

        // Feature banner stays active in the hierarchy by design (see
        // FeatureAlertBanner), so Show() never hits the activation issue
        // that BossAlertBanner suffered when called from here.
        if (discoveryBanner != null)
            discoveryBanner.Show(message, worldPos, floorIdx);

        SoundEffectManager.Play(revealSfxKey);
    }
}