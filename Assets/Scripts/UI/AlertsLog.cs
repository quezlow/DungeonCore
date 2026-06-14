using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// HUD alerts system.
///
/// TWO SURFACES
///   1. The ticker — small always-on panel under UICanvas_Dungeon, anchored
///      top-right. Holds the last `tickerVisibleCount` entries. Ephemeral:
///      new entries push older ones off (FIFO).
///   2. The history — in-memory ring buffer of `historyCapacity` entries.
///      Browsed via AlertHistoryPanel (hotkey L). Persisted with the save
///      file, trimmed to `historyPersistCount` on save.
///
/// API
///   AddAlert(message, worldPos, floorIndex, category) — preferred.
///   AddAlert(message, worldPos, floorIndex)           — deprecated wrapper;
///     defaults to AlertCategory.System and logs a warning.
///
/// CLICK-JUMP
///   Each entry captures worldPos + floorIndex. Click → DungeonCameraController.
///   PanTo(pos, floor) which switches floors before panning. If floorIndex is
///   -1, only the camera pans (no floor switch).
///
/// UNREAD COUNTER
///   Incremented on every new alert while AlertHistoryPanel is closed.
///   Reset to 0 when the panel opens (or via MarkAllRead).
///   Persisted with the save.
///
/// SAVE / LOAD
///   GetSaveData() — last historyPersistCount entries + unreadCount.
///   RestoreFromSave(...) — called from DungeonSaveController after the
///     floor restore passes complete. Ticker is rebuilt from the tail of
///     restored history so the player sees continuity.
///
/// PREFAB / SCENE SETUP — unchanged from prior version. The serialized field
/// previously named `entryContainer` has been renamed to `tickerContainer`;
/// FormerlySerializedAs preserves the Inspector reference automatically.
/// </summary>
public class AlertsLog : MonoBehaviour
{
    public static AlertsLog Instance { get; private set; }

    [Header("Ticker UI")]
    [SerializeField] private GameObject panel;
    [FormerlySerializedAs("entryContainer")]
    [SerializeField] private Transform tickerContainer;
    [SerializeField] private Button entryPrefab;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private bool autoScrollOnAdd = true;

    [Header("Capacity")]
    [Tooltip("How many entries the ticker keeps alive before evicting oldest.")]
    [SerializeField, Min(1)] private int tickerVisibleCount = 12;

    [Tooltip("How many entries the in-memory history ring keeps.")]
    [SerializeField, Min(1)] private int historyCapacity = 200;

    [Tooltip("How many entries persist with the save file (tail of history).")]
    [SerializeField, Min(0)] private int historyPersistCount = 100;

    // ── Data ──────────────────────────────────────────────────────

    private readonly List<AlertEntry> history = new();
    private readonly List<Button> tickerEntries = new();
    private int unreadCount = 0;

    // ── Events ────────────────────────────────────────────────────

    public event Action<AlertEntry> OnAlertAdded;
    public event Action OnHistoryCleared;
    public event Action<int> OnUnreadChanged;

    // ── Read-only views ───────────────────────────────────────────

    public IReadOnlyList<AlertEntry> History => history;
    public int UnreadCount => unreadCount;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// DEPRECATED. Logs a warning. Defaults to AlertCategory.System. Will be
    /// removed once all callers are migrated to the 4-arg signature.
    /// </summary>
    public void AddAlert(string message, Vector3 worldPos, int floorIndex = -1)
    {
        Debug.LogWarning("[AlertsLog] Deprecated 3-arg AddAlert called — " +
                         "pass an AlertCategory.");
        AddAlert(message, worldPos, floorIndex, AlertCategory.System);
    }

    public void AddAlert(string message, Vector3 worldPos, int floorIndex,
                         AlertCategory category)
    {
        var entry = BuildEntry(message, worldPos, floorIndex, category);
        AppendEntry(entry, fromLoad: false);
    }

    public void ClearHistory()
    {
        history.Clear();
        foreach (var b in tickerEntries)
            if (b != null) Destroy(b.gameObject);
        tickerEntries.Clear();

        bool hadUnread = unreadCount > 0;
        unreadCount = 0;
        if (hadUnread) OnUnreadChanged?.Invoke(0);
        OnHistoryCleared?.Invoke();
    }

    public void MarkAllRead()
    {
        if (unreadCount == 0) return;
        unreadCount = 0;
        OnUnreadChanged?.Invoke(0);
    }

    // ── Save / Load ───────────────────────────────────────────────

    public List<AlertEntrySaveData> GetSaveData()
    {
        int start = Mathf.Max(0, history.Count - historyPersistCount);
        var list = new List<AlertEntrySaveData>(history.Count - start);
        for (int i = start; i < history.Count; i++)
            list.Add(history[i].ToSaveData());
        return list;
    }

    public int GetUnreadCountForSave() => unreadCount;

    /// <summary>
    /// Replaces the in-memory history with the saved tail and restores the
    /// unread count. Rebuilds the ticker from the tail so the player sees
    /// continuity on reload.
    /// </summary>
    public void RestoreFromSave(List<AlertEntrySaveData> data, int restoredUnread)
    {
        // Clear without firing OnHistoryCleared — this is a load, not a player action.
        history.Clear();
        foreach (var b in tickerEntries)
            if (b != null) Destroy(b.gameObject);
        tickerEntries.Clear();
        unreadCount = 0;

        if (data != null)
        {
            foreach (var d in data)
            {
                var entry = AlertEntry.FromSaveData(d);
                history.Add(entry);
            }

            // Hydrate the ticker from the tail.
            int start = Mathf.Max(0, history.Count - tickerVisibleCount);
            for (int i = start; i < history.Count; i++)
                AddTickerRow(history[i]);
        }

        unreadCount = Mathf.Max(0, restoredUnread);
        OnUnreadChanged?.Invoke(unreadCount);
    }

    // ── Internals ─────────────────────────────────────────────────

    private AlertEntry BuildEntry(string message, Vector3 worldPos, int floorIndex,
                                  AlertCategory category)
    {
        var dn = DayNightCycle.Instance;
        return new AlertEntry
        {
            Message = message ?? "",
            WorldPos = worldPos,
            FloorIndex = floorIndex,
            Category = category,
            InGameDay = dn != null ? dn.CurrentDay : 1,
            Phase = dn != null ? dn.CurrentPhase : DayNightCycle.Phase.Day,
            RealTime = DateTime.Now,
        };
    }

    private void AppendEntry(AlertEntry entry, bool fromLoad)
    {
        history.Add(entry);
        while (history.Count > historyCapacity) history.RemoveAt(0);

        AddTickerRow(entry);

        if (!fromLoad && !IsHistoryPanelOpen())
        {
            unreadCount++;
            OnUnreadChanged?.Invoke(unreadCount);
        }

        OnAlertAdded?.Invoke(entry);
    }

    private void AddTickerRow(AlertEntry entry)
    {
        if (tickerContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[AlertsLog] tickerContainer or entryPrefab not assigned.");
            return;
        }

        while (tickerEntries.Count >= tickerVisibleCount)
        {
            var oldest = tickerEntries[0];
            tickerEntries.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        Button btn = Instantiate(entryPrefab, tickerContainer);
        btn.gameObject.SetActive(true);
        BindButton(btn, entry);
        tickerEntries.Add(btn);

        if (autoScrollOnAdd && scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    /// <summary>
    /// Shared button-row populator used by the ticker and AlertHistoryPanel.
    /// Two TMP_Text children = (timestamp, message); the message text is
    /// tinted by category. One child = combined "[Day N · Phase] message".
    /// </summary>
    public static void BindButton(Button btn, AlertEntry entry)
    {
        if (btn == null || entry == null) return;

        var labels = btn.GetComponentsInChildren<TMP_Text>();
        string timestamp = entry.FormatTimestamp();

        if (labels.Length >= 2)
        {
            labels[0].text = timestamp;
            labels[1].text = entry.Message;
            labels[1].color = AlertCategoryStyle.GetColor(entry.Category);
        }
        else if (labels.Length == 1)
        {
            labels[0].text = $"[{timestamp}] {entry.Message}";
            labels[0].color = AlertCategoryStyle.GetColor(entry.Category);
        }

        Vector3 capturedPos = entry.WorldPos;
        int capturedFloor = entry.FloorIndex;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            if (capturedFloor >= 0)
                DungeonCameraController.Instance?.PanTo(capturedPos, capturedFloor);
            else
                DungeonCameraController.Instance?.PanTo(capturedPos);
        });
    }

    private bool IsHistoryPanelOpen()
    {
        return AlertHistoryPanel.Instance != null && AlertHistoryPanel.Instance.IsOpen;
    }
}