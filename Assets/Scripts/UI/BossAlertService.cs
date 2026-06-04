using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central routing point for boss-death notifications.
///
/// CONTRACT
///   MonsterSpawner.OnMonsterDied() calls NotifyBossDeath() when the dying
///   monster's definition is a BossVariantDefinition. This service:
///     1. Always pushes an entry to AlertsLog (with floor index for click-jump).
///     2. On the FIRST death of a given spawner instance, also shows the
///        banner or toast (depending on the player's setting).
///     3. (TODO) plays bossDeathSting via audio service.
///
/// SCENE SETUP
///   - Place a single BossAlertService GameObject under UICanvas_Dungeon (or
///     wherever your HUD root lives).
///   - Wire 'banner' to a BossAlertBanner GameObject (initially inactive).
///   - Wire 'toastPrefab' to a BossAlertToast prefab and 'toastContainer' to
///     a layout-group parent transform where toasts will be instantiated.
///
/// SETTINGS
///   Reads PlayerPrefs key "Settings.BossAlertMode":
///     0 = Banner (default)
///     1 = Toast
///   Use SettingsAccess.BossAlertMode = ... to set from a settings menu toggle.
/// </summary>
public class BossAlertService : MonoBehaviour
{
    public static BossAlertService Instance { get; private set; }

    public enum DisplayMode { Banner = 0, Toast = 1 }

    [Header("UI Targets")]
    [SerializeField] private BossAlertBanner banner;
    [SerializeField] private BossAlertToast toastPrefab;
    [SerializeField] private Transform toastContainer;

    [Header("Audio (TODO — wire when sound system exists)")]
    [SerializeField] private AudioClip bossDeathSting;

    /// <summary>Spawner instance IDs that have already shown a banner/toast.</summary>
    private readonly HashSet<EntityId> firstDeathShown = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Called by MonsterSpawner.OnMonsterDied when the dead monster was a boss.
    /// </summary>
    public void NotifyBossDeath(MonsterSpawner spawner, BossVariantDefinition bossDef,
                                int floorIndex, Vector3 worldPos)
    {
        string title = bossDef != null ? bossDef.GetBossTitle() : "Boss";
        string message = $"{title} on Floor {floorIndex + 1} has been defeated";

        // Always log — log entries include floor index so click jumps floors.
        AlertsLog.Instance?.AddAlert(message, worldPos, floorIndex);

        // Only show banner/toast for the FIRST death of this spawner instance.
        bool first = spawner != null && firstDeathShown.Add(spawner.GetEntityId());
        if (!first) return;

        var mode = SettingsAccess.BossAlertMode;
        if (mode == DisplayMode.Banner)
        {
            if (banner != null) banner.Show(message, worldPos, floorIndex);
            else Debug.LogWarning("[BossAlertService] Banner mode but no banner assigned.");
        }
        else
        {
            if (toastPrefab != null && toastContainer != null)
            {
                var t = Instantiate(toastPrefab, toastContainer);
                t.Show(message, worldPos, floorIndex);
            }
            else
            {
                Debug.LogWarning("[BossAlertService] Toast mode but prefab/container missing.");
            }
        }

        // TODO: route bossDeathSting through your audio service when sound is online.
        if (bossDeathSting != null)
        {
            // AudioService.Instance?.PlayOneShot(bossDeathSting);
        }
    }
}

/// <summary>
/// Single source of truth for the Boss Alert display mode preference.
/// Wire your settings menu's toggle to BossAlertMode.
/// </summary>
public static class SettingsAccess
{
    private const string Key_BossAlertMode = "Settings.BossAlertMode";

    public static BossAlertService.DisplayMode BossAlertMode
    {
        get => (BossAlertService.DisplayMode)PlayerPrefs.GetInt(Key_BossAlertMode, 0);
        set
        {
            PlayerPrefs.SetInt(Key_BossAlertMode, (int)value);
            PlayerPrefs.Save();
        }
    }
}