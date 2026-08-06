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

    [Header("Critical Severity")]
    [Tooltip("SoundEffectManager key played when a Critical alert is raised. "
           + "Leave EMPTY for silence -- an unassigned key must not throw or warn, "
           + "because the severity layer has to be usable before the sting exists.")]
    [SerializeField] private string criticalSfxKey = "";
    [Tooltip("Critical alerts also raise the feature discovery banner. That banner "
           + "is a static singleton, stays active in the hierarchy by design, and is "
           + "documented safe to Show() from outside its own flow -- which is why it "
           + "is reused rather than a second banner being wired by hand.")]
    [SerializeField] private bool criticalRaisesBanner = true;

    [Tooltip("Critical alerts wash the screen this colour. Defaults to the red "
           + "the climax beast's pushback already uses, so the two read as one "
           + "language rather than as two unrelated effects.")]
    [SerializeField] private Color criticalFlashColour = new Color(0.75f, 0.05f, 0.05f, 1f);

    [Tooltip("Seconds the flash takes to fade to nothing. Matches the climax "
           + "flash. Zero disables the flash entirely.")]
    [SerializeField, Min(0f)] private float criticalFlashSeconds = 0.45f;

    [Tooltip("Shortest gap between two FLASHES or two STINGS, in unscaled "
           + "seconds. Three Criticals can land in the same breath -- a wave "
           + "stage, a strike and a breach -- and three stacked flashes is worse "
           + "than one. Every alert still logs and still banners; only the two "
           + "loud channels are rate-limited.")]
    [SerializeField, Min(0f)] private float criticalLoudCooldown = 3f;

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

    /// <summary>Unscaled time the last flash or sting fired. Unscaled because a
    /// Critical can land while the game is paused, and a cooldown measured in
    /// scaled time would then never expire.</summary>
    private float lastLoudAt = -999f;

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

    private void OnEnable()
    {
        UnlockState.OnChanged += HandleUnlockChanged;
        ApplyGate();
    }

    private void OnDisable() => UnlockState.OnChanged -= HandleUnlockChanged;

    private void HandleUnlockChanged(string _) => ApplyGate();

    // The ticker records nothing until researched; it must also stay HIDDEN
    // until then, rather than showing an empty board.
    private void ApplyGate()
    {
        bool unlocked = UnlockState.IsUnlocked("tech.alerts");
        if (panel != null && panel.activeSelf != unlocked) panel.SetActive(unlocked);
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

    /// <summary>Raise an alert. Severity is OPTIONAL: pass nothing and it is
    /// derived from the category by AlertSeverityStyle.DefaultFor, so a caller
    /// only names a severity when it disagrees with its category -- which in
    /// practice means the short, hand-audited list of things that raise a
    /// banner.</summary>
    public void AddAlert(string message, Vector3 worldPos, int floorIndex,
                             AlertCategory category, AlertSeverity? severity = null)
    {
        var resolved = severity ?? AlertSeverityStyle.DefaultFor(category);

        // Gated behind the Ledger of Alarums research node. Until researched the
        // core keeps no account: nothing is recorded, tickered, or counted.
        //
        // CRITICAL IS THE ONE EXCEPTION, and it is deliberately PARTIAL. What the
        // Ledger sells is the account -- the ticker, the history, the unread
        // count, the ability to look back. It was never supposed to be selling
        // the alarm. Before this, a player who had not bought the node watched
        // the core go down with no banner, no flash and no sound, which is the
        // one moment in the game that cannot be allowed to pass quietly.
        //
        // So a Critical still raises its banner, flash and sting, and still
        // records NOTHING: no history row, no ticker row, no unread count, and
        // no OnAlertAdded. Nothing downstream can tell the difference between
        // this and the alert never having existed, which is what keeps the
        // research node worth buying.
        if (!UnlockState.IsUnlocked("tech.alerts"))
        {
            if (resolved == AlertSeverity.Critical)
                RaiseCritical(BuildEntry(message, worldPos, floorIndex, category, resolved));
            return;
        }

        var entry = BuildEntry(message, worldPos, floorIndex, category, resolved);
        AppendEntry(entry, fromLoad: false);
    }

    public void ClearHistory()
    {
        history.Clear();
        ClearTickerRows();

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
        ClearTickerRows();
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
                                  AlertCategory category, AlertSeverity severity)
    {
        var dn = DayNightCycle.Instance;

        // Resolved at the SINK rather than at the forty-odd call sites, most
        // of which take the -1 default. A -1 entry sent the click to the
        // single-argument PanTo, which wrote a floor-0 world position while a
        // deeper floor was active -- the confiner then slammed the camera to
        // that floor's edge and pinned it there. An alert raised while floor N
        // is active is about floor N; callers that know better still pass
        // their own index and are untouched.
        if (floorIndex < 0 && FloorManager.Instance != null)
            floorIndex = FloorManager.Instance.ActiveFloorIndex;

        return new AlertEntry
        {
            Message = message ?? "",
            WorldPos = worldPos,
            FloorIndex = floorIndex,
            Category = category,
            Severity = severity,
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

        if (!fromLoad && entry.Severity == AlertSeverity.Critical) RaiseCritical(entry);

        OnAlertAdded?.Invoke(entry);
    }

    /// <summary>The Critical beat: a banner and a sting on top of the row.
    ///
    /// FeatureAlertBanner rather than BossAlertBanner on purpose. The feature
    /// banner is a static singleton that never calls SetActive(false) on itself,
    /// so an external Show() cannot hit the activation-and-Awake ordering quirk
    /// that BossAlertBanner suffered when FeatureRevealController tried to drive
    /// it -- and it is already wired in the scene, so no prefab work is needed.
    ///
    /// Never fires from a load. Restoring a save replays no history, and a stack
    /// of banners for threats the player already answered would be worse than
    /// silence.</summary>
    private void RaiseCritical(AlertEntry entry)
    {
        // The banner is NOT rate-limited. It replaces its own text rather than
        // stacking, so a second Critical simply retitles it, and losing the more
        // recent of two messages would be worse than showing both in turn.
        if (criticalRaisesBanner && FeatureAlertBanner.Instance != null)
            FeatureAlertBanner.Instance.Show(entry.Message, entry.WorldPos, entry.FloorIndex);

        // The flash and the sting ARE, and share one window. Three Criticals in
        // the same breath is not hypothetical -- a wave stage, a Holy Order
        // strike and a core breach can all land on the same frame -- and three
        // washes of red on top of each other reads as a rendering fault rather
        // than as three pieces of bad news.
        bool loudAllowed = Time.unscaledTime - lastLoudAt >= criticalLoudCooldown;
        if (!loudAllowed) return;
        lastLoudAt = Time.unscaledTime;

        // Suppressed by the accessibility preference, and by that ALONE: the
        // sting and the banner still fire, because a player who cannot take a
        // full-screen flash still needs to be told the core is being broken.
        if (criticalFlashSeconds > 0f && !SettingsAccess.ReduceFlashing)
            ScreenFlash.Instance?.Flash(criticalFlashColour, criticalFlashSeconds);

        PlayCriticalSting();
    }

    /// <summary>The sting, guarded. SoundEffectManager.Play dereferences a
    /// static SoundEffectLibrary that is only assigned in the manager's own
    /// Awake, so calling it with no manager in the scene throws outright rather
    /// than failing quiet -- and an alert layer that can hard-error on a scene
    /// missing an audio object is worse than one that is silent. The key ships
    /// EMPTY and stays that way until a clip and a library entry exist.</summary>
    private void PlayCriticalSting()
    {
        if (string.IsNullOrEmpty(criticalSfxKey)) return;
        try
        {
            SoundEffectManager.Play(criticalSfxKey);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AlertsLog] Critical sting '" + criticalSfxKey +
                "' could not play: " + e.Message + ". Clear criticalSfxKey, or " +
                "add the clip to SoundEffectLibrary and put a SoundEffectManager " +
                "in the scene.");
        }
    }

    /// <summary>
    /// Drop every ticker row. Tracked rows are destroyed, then the container is swept for any
    /// untracked row-like children, otherwise a cleared ticker can be left showing blank rows.
    /// </summary>
    private void ClearTickerRows()
    {
        foreach (var b in tickerEntries)
            if (b != null) Destroy(b.gameObject);
        tickerEntries.Clear();

        if (tickerContainer == null) return;
        for (int i = tickerContainer.childCount - 1; i >= 0; i--)
        {
            var child = tickerContainer.GetChild(i);
            if (child.GetComponent<Button>() != null) Destroy(child.gameObject);
        }
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
    /// tinted by CATEGORY and the timestamp carries the SEVERITY marker and
    /// tint. One child = combined "[Day N · Phase] message".
    ///
    /// Severity lives on labels[0] because that is the label the shipped prefab
    /// leaves untinted -- category already owns labels[1], and adding a third
    /// child would be prefab work, which cannot be delivered by script. Info
    /// writes no tint at all, so an ordinary row keeps whatever colour the
    /// prefab authored.
    /// </summary>
    public static void BindButton(Button btn, AlertEntry entry)
    {
        if (btn == null || entry == null) return;

        var labels = btn.GetComponentsInChildren<TMP_Text>();
        string timestamp = entry.FormatTimestamp();

        string marker = AlertSeverityStyle.Marker(entry.Severity);

        if (labels.Length >= 2)
        {
            labels[0].text = marker + timestamp;
            if (AlertSeverityStyle.HasTint(entry.Severity))
                labels[0].color = AlertSeverityStyle.GetColor(entry.Severity);
            labels[1].text = entry.Message;
            labels[1].color = AlertCategoryStyle.GetColor(entry.Category);
        }
        else if (labels.Length == 1)
        {
            // One label has nowhere to put a severity tint without losing the
            // category colour, so the marker carries it alone.
            labels[0].text = $"{marker}[{timestamp}] {entry.Message}";
            labels[0].color = AlertCategoryStyle.GetColor(entry.Category);
        }

        Vector3 capturedPos = entry.WorldPos;
        int capturedFloor = entry.FloorIndex;

        // Saves written before the resolution above still carry -1 entries,
        // and AlertEntrySaveData.floorIndex is persisted, so they would keep
        // mis-panning forever. FloorRoot.WorldOriginY is floorIndex * -2000
        // and the widest disc is 600 cells, so the stored Y recovers the floor
        // unambiguously. No migration, and old alerts jump correctly.
        if (capturedFloor < 0)
            capturedFloor = Mathf.Max(0, Mathf.RoundToInt(capturedPos.y / -2000f));

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            DungeonCameraController.Instance?.PanTo(capturedPos, capturedFloor);
        });
    }

    private bool IsHistoryPanelOpen()
    {
        return AlertHistoryPanel.Instance != null && AlertHistoryPanel.Instance.IsOpen;
    }
}