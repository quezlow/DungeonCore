using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small HUD button that opens AlertHistoryPanel and shows an unread badge.
///
/// PREFAB / SCENE SETUP (under UICanvas_Dungeon, near the ticker)
///   AlertHudButton (this script)
///   ├── Button (Image + Button — click opens panel)
///   └── BadgeRoot          (GameObject, initially inactive)
///       └── BadgeLabel     (TMP_Text — shows "1" .. "99+")
///
/// SUBSCRIPTION
///   Subscribes to AlertsLog.OnUnreadChanged. Falls back to polling once in
///   Start in case the event was missed during scene init.
/// </summary>
public class AlertHudButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TMP_Text badgeLabel;

    [Header("Badge")]
    [Tooltip("Cap displayed on the badge. Higher counts render as 'N+'.")]
    [SerializeField, Min(1)] private int badgeCap = 99;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OnClicked);
        if (badgeRoot != null) badgeRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (AlertsLog.Instance != null)
        {
            AlertsLog.Instance.OnUnreadChanged -= HandleUnreadChanged;
            AlertsLog.Instance.OnUnreadChanged += HandleUnreadChanged;
        }
    }

    private void OnDisable()
    {
        if (AlertsLog.Instance != null)
            AlertsLog.Instance.OnUnreadChanged -= HandleUnreadChanged;
    }

    private void Start()
    {
        // Pull initial state once; the event may have fired before OnEnable
        // hooked up during scene load.
        if (AlertsLog.Instance != null)
            HandleUnreadChanged(AlertsLog.Instance.UnreadCount);
    }

    private void OnClicked()
    {
        if (AlertHistoryPanel.Instance != null) AlertHistoryPanel.Instance.Toggle();
    }

    private void HandleUnreadChanged(int count)
    {
        if (badgeRoot == null) return;
        if (count <= 0)
        {
            badgeRoot.SetActive(false);
            return;
        }

        badgeRoot.SetActive(true);
        if (badgeLabel != null)
            badgeLabel.text = count > badgeCap ? $"{badgeCap}+" : count.ToString();
    }
}