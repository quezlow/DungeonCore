using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD scrolling alerts log. Currently used by WarningTrap; future systems
/// (intent system, boss alerts, etc.) can call AddAlert() the same way.
///
/// Each entry shows a timestamp and label. Clicking an entry pans the camera
/// to the world position where the alert originated. If a floor index is
/// provided, the camera will also switch to that floor before panning.
///
/// PREFAB / SCENE SETUP (attach to a parent GameObject under UICanvas_Dungeon):
///   AlertsLog (this script)
///   ├── Panel
///   │   └── ScrollView
///   │       └── Content      (VerticalLayoutGroup — assigned to entryContainer)
///
///   AlertEntryPrefab: a Button prefab with two TMP_Text children
///     (timestampLabel and messageLabel).
/// </summary>
public class AlertsLog : MonoBehaviour
{
    public static AlertsLog Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private Button entryPrefab;

    [Header("Settings")]
    [SerializeField] private int maxEntries = 30;
    [SerializeField] private bool autoScrollOnAdd = true;
    [SerializeField] private ScrollRect scrollRect;

    private readonly List<Button> spawnedEntries = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Adds a new entry to the log.
    /// Clicking the entry pans the camera to worldPos.
    /// If floorIndex >= 0, the camera also switches to that floor before panning.
    /// </summary>
    public void AddAlert(string message, Vector3 worldPos, int floorIndex = -1)
    {
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[AlertsLog] entryContainer or entryPrefab not assigned.");
            return;
        }

        while (spawnedEntries.Count >= maxEntries)
        {
            var oldest = spawnedEntries[0];
            spawnedEntries.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        Button btn = Instantiate(entryPrefab, entryContainer);
        btn.gameObject.SetActive(true);

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

        Vector3 captured = worldPos;
        int capturedFloor = floorIndex;
        btn.onClick.AddListener(() =>
        {
            if (capturedFloor >= 0)
                DungeonCameraController.Instance?.PanTo(captured, capturedFloor);
            else
                DungeonCameraController.Instance?.PanTo(captured);
        });

        spawnedEntries.Add(btn);

        if (autoScrollOnAdd && scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    public void ClearAlerts()
    {
        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();
    }
}