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

    [Tooltip("How far out dwarven holdings announce themselves, in cells.\n\n" +
             "Every other feature can be blundered into for free. Dwarven ground " +
             "is the one place where arriving and CLAIMING are the same gesture " +
             "and the claim costs standing, so a warning that lands on contact is " +
             "not a warning at all -- the granite needs to be on screen before the " +
             "frontier reaches it.\n\n" +
             "0 restores reveal on contact.")]
    [SerializeField, Min(0)] private int dwarvenWarnRangeCells = 4;

    /// <summary>The banner that can actually be shown.
    ///
    /// The serialized field above is only usable on floor 0. Every deeper floor
    /// comes from Instantiate(floorTemplatePrefab), and a prefab cannot hold a
    /// reference to a scene object, so on those floors the field points at the
    /// PREFAB ASSET: no parent, never activeInHierarchy, and StartCoroutine
    /// throws on it. scene.IsValid() is the definitive difference between an
    /// asset and a live object, so the field is only trusted when it passes
    /// that test.</summary>
    private FeatureAlertBanner Banner
    {
        get
        {
            if (FeatureAlertBanner.Instance != null) return FeatureAlertBanner.Instance;
            if (discoveryBanner != null && discoveryBanner.gameObject.scene.IsValid())
                return discoveryBanner;
            return null;
        }
    }

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
        TryWarnOfDwarvenGroundNear(cell);
    }

    /// <summary>Reveals dwarven holdings from a few cells out instead of on
    /// contact, so the granite is on screen before the frontier arrives.
    ///
    /// Routed back through TryRevealFeatureAtCell rather than reimplemented, so
    /// the one-alert-per-floor rule for roads, the per-site alert, the archetype
    /// display names and the wisp's Buried Age lines all keep working. Masonry
    /// is not in the feature lookup, so a probe landing on a wall resolves the
    /// site through the holdings registry and reveals it from one of its carved
    /// cells instead.</summary>
    private void TryWarnOfDwarvenGroundNear(Vector3Int cell)
    {
        int r = dwarvenWarnRangeCells;
        if (r <= 0 || floor == null || features == null) return;

        var map = floor.TerrainTypeMap;
        if (map == null || !map.HasHoldings) return;

        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                var probe = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                int owner = map.HoldingOwnerAt(probe);
                if (owner == TerrainTypeMap.NoHoldingOwner) continue;

                if (TerrainTypeMap.OwnerIsRoad(owner))
                {
                    // Carriageway is in the feature lookup, so the probe cell
                    // itself is enough.
                    TryRevealFeatureAtCell(probe, silent: false);
                    continue;
                }

                if (features.IsSiteRevealed(owner)) continue;
                var s = features.GetSiteById(owner);
                if (s == null || s.cells == null || s.cells.Count == 0) continue;
                TryRevealFeatureAtCell(s.cells[0].ToVector3Int(), silent: false);
            }
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

            // Roads reveal per STRETCH, not per road. featureId is a segment id,
            // so a trunk running rim to rim comes into view a stretch at a time
            // rather than laying the whole floor out from one touched cell.
            case FeatureType.Road:
                if (features.IsRoadSegmentRevealed(fref.featureId)) return;
                // ONE alert per floor, not one per stretch. Rivers and chambers can
                // afford an alert each because a floor holds a handful of them; a
                // floor holds EIGHTY-FIVE road segments by construction, and a banner
                // every forty cells of influence turns the discovery into noise.
                bool firstOnThisFloor = features.RevealedRoadSegmentCount == 0;
                features.RevealRoadSegment(fref.featureId);
                FireAlert(FeatureType.Road, fref.featureId, "An ancient road has been revealed",
                          silent || !firstOnThisFloor);
                break;

            // One alert per SITE. Roads collapse to one alert per floor because a
            // floor holds eighty-five stretches; a floor holds a handful of sites
            // and each is a set-piece, so each one speaks.
            case FeatureType.AncientSite:
                if (features.IsSiteRevealed(fref.featureId)) return;
                bool firstSiteOnFloor = features.RevealedSiteCount == 0;
                var site = features.GetSiteById(fref.featureId);
                features.RevealSite(fref.featureId);
                FireAlert(FeatureType.AncientSite, fref.featureId,
                          "The dark opens onto " + (site != null
                              ? AncientSiteProfile.DisplayName(site.archetype)
                              : "a Buried Age ruin"),
                          silent);
                if (!silent) SpeakForSite(site, firstSiteOnFloor);
                break;

            case FeatureType.EntranceCave:
                if (features.IsEntranceDiscovered) return;
                features.MarkEntranceDiscovered();
                FireEntranceAlert(silent);
                break;
        }
    }

    /// <summary>
    /// The wisp's two lines about the Buried Age. Both are authored once = true,
    /// so the shipped spoken-line save field does the remembering and this needs
    /// no state of its own.
    ///
    /// The Sealed Gate line is gated on CoreMemory.Lived rather than on a deed
    /// flag, and is deliberately NOT a memory echo: canon 34 records that the
    /// player died at an OPENED SEAL regardless of what they did that last day,
    /// so the memory belongs to every lived core and to no particular flag.
    /// </summary>
    private void SpeakForSite(SiteData site, bool firstOnFloor)
    {
        var wisp = WispCompanion.Instance;
        if (wisp == null) return;

        if (site != null && site.archetype == SiteArchetype.SealedGate && CoreMemory.Lived)
        {
            wisp.Speak("site_sealed_gate");
            return;
        }
        if (firstOnFloor) wisp.Speak("site_first");
    }

    private void FireEntranceAlert(bool silent)
    {
        if (floor == null || features == null || features.EntranceCave == null) return;
        Vector3 worldPos = features.GetFeatureCenterWorld(FeatureType.EntranceCave, 0);
        string message = "The seal is broken. Air moves through the deep — they will come.";

        AlertsLog.Instance?.AddAlert(message, worldPos, floor.FloorIndex, AlertCategory.Discovery);

        if (silent) return;
        var banner = Banner;
        if (banner != null)
            banner.Show(message, worldPos, floor.FloorIndex);
        SoundEffectManager.Play(revealSfxKey);
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
        var banner = Banner;
        if (banner != null)
            banner.Show(message, worldPos, floorIdx);

        SoundEffectManager.Play(revealSfxKey);
    }
}