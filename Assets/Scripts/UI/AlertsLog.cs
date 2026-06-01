using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD scrolling alerts log. Currently used by WarningTrap; future systems
/// (intent system, boss alerts, etc.) can call AddAlert() the same way.
///
/// Each entry shows a timestamp and label. Clicking an entry pans the camera
/// to the world position where the alert originated.
///
/// PREFAB / SCENE SETUP (attach to a parent GameObject under UICanvas_Dungeon):
///   AlertsLog (this script)
///   ├── Panel
///   │   └── ScrollView
///   │       └── Content      (VerticalLayoutGroup — assigned to entryContainer)
///
///   AlertEntryPrefab: a Button prefab with two TMP_Text children
///     (timestampLabel and messageLabel).
///
/// The log is visible on screen at all times. Day 21 lesson — leave the
/// GameObject and panel ACTIVE at edit time so subscriptions fire.
/// </summary>
public class AlertsLog : MonoBehaviour
{
    public static AlertsLog Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform  entryContainer;
    [SerializeField] private Button     entryPrefab;

    [Header("Settings")]
    [Tooltip("Maximum entries kept in the log. Older entries are pruned.")]
    [SerializeField] private int maxEntries = 30;

    [Tooltip("Auto-scroll to newest entry on add.")]
    [SerializeField] private bool autoScrollOnAdd = true;
    [SerializeField] private ScrollRect scrollRect;

    private readonly List<Button> spawnedEntries = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Adds a new entry to the log.
    /// Clicking the entry pans the camera to worldPos.
    /// </summary>
    public void AddAlert(string message, Vector3 worldPos)
    {
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[AlertsLog] entryContainer or entryPrefab not assigned.");
            return;
        }

        // Prune oldest if at cap.
        while (spawnedEntries.Count >= maxEntries)
        {
            var oldest = spawnedEntries[0];
            spawnedEntries.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        Button btn = Instantiate(entryPrefab, entryContainer);
        btn.gameObject.SetActive(true);

        // Set entry labels. Expects two TMP_Texts: first is timestamp, second is message.
        // If only one is found, write the full line into it.
        var labels = btn.GetComponentsInChildren<TMP_Text>();
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss");

        if (labels.Length >= 2)
        {
            labels[0].text = timestamp;
            labels[1].text = message;
        }
        else if (labels.Length == 1)
        {
            labels[0].text = $"[{timestamp}] {message}";
        }

        Vector3 captured = worldPos; // closure capture
        btn.onClick.AddListener(() =>
        {
            DungeonCameraController.Instance?.PanTo(captured);
        });

        spawnedEntries.Add(btn);

        if (autoScrollOnAdd && scrollRect != null)
        {
            // Force layout rebuild then scroll to bottom (newest = visible).
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    /// <summary>Clears the log.</summary>
    public void ClearAlerts()
    {
        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();
    }
}
