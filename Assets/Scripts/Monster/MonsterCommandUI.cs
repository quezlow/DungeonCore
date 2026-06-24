using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 31 PART 3D — Command panel for a placed MonsterSpawner.
///
/// Shown automatically when SpawnerSelectionController has a selection; hidden otherwise.
/// The panel sits in the same screen location as the build sub-menu (above the ActionBar)
/// but is a separate GameObject — see scene setup notes in the HTML guide.
///
/// EXPOSED ACTIONS (wire onClick / onValueChanged in Inspector)
///   OnWanderClicked        — sets order to Wander, clears any waypoints/attack target
///   OnPatrolClicked        — enters PlaceMonsterPatrol mode (append-style waypoint placement)
///   OnAttackHereClicked    — enters PlaceMonsterAttackTarget mode (single click sets target)
///   OnDefendClicked        — STUB for future, logs
///   OnLoopToggleChanged    — true = Loop, false = Hold-at-Final
///   OnClearOrdersClicked   — wipe all orders, return to Wander
///   OnCloseClicked         — deselect, hide panel
/// </summary>
public class MonsterCommandUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text monsterNameLabel;
    [SerializeField] private TMP_Text statusLabel;

    [Header("Controls")]
    [SerializeField] private Toggle loopToggle;
    [SerializeField] private Button wanderButton;
    [SerializeField] private Button patrolButton;
    [SerializeField] private Button attackHereButton;
    [SerializeField] private Button defendButton;
    [SerializeField] private Button clearOrdersButton;
    [SerializeField] private Button closeButton;

    [Header("Removal (Phase 3 closeout #1)")]
    [SerializeField] private ConfirmDialog confirmDialog;

    private MonsterSpawner current;

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void Start()
    {
        if (SpawnerSelectionController.Instance != null)
            SpawnerSelectionController.Instance.OnSelectionChanged += HandleSelectionChanged;

        // DAY 31 — Code-side toggle subscription. Avoids Inspector wiring mishaps
        // (Static-bool variant fires with a fixed value regardless of toggle state).
        if (loopToggle != null)
            loopToggle.onValueChanged.AddListener(OnLoopToggleChanged);
    }

    private void OnDestroy()
    {
        if (SpawnerSelectionController.Instance != null)
            SpawnerSelectionController.Instance.OnSelectionChanged -= HandleSelectionChanged;
        if (loopToggle != null)
            loopToggle.onValueChanged.RemoveListener(OnLoopToggleChanged);
        Unsubscribe(current);
    }

    private void HandleSelectionChanged(MonsterSpawner spawner)
    {
        Unsubscribe(current);
        current = spawner;
        Subscribe(current);

        if (spawner == null) Hide();
        else { Show(); RefreshDisplay(); }
    }

    private void Subscribe(MonsterSpawner s)
    {
        if (s != null) s.OnOrdersChanged += RefreshDisplay;
    }

    private void Unsubscribe(MonsterSpawner s)
    {
        if (s != null) s.OnOrdersChanged -= RefreshDisplay;
    }

    private void Show()
    {
        if (panel != null) panel.SetActive(true);
        RefreshDisplay();
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void RefreshDisplay()
    {
        if (current == null) return;
        if (monsterNameLabel != null)
            monsterNameLabel.text = current.Definition != null ? current.Definition.monsterName : "Monster";

        if (statusLabel != null)
            statusLabel.text = BuildStatusText(current);

        if (loopToggle != null)
            loopToggle.SetIsOnWithoutNotify(current.PatrolLoop);

        // DAY 31 PART 3 CLOSE-OUT — defend button label reflects current permission.
        if (defendButton != null)
        {
            var defendLabel = defendButton.GetComponentInChildren<TMP_Text>();
            if (defendLabel != null)
                defendLabel.text = current.AllowDefendCore ? "Defend: ON" : "Defend: OFF";
        }
    }

    private static string BuildStatusText(MonsterSpawner s)
    {
        string baseText;
        if (s.HasAttackTarget)
            baseText = $"Attack-Here at {s.AttackTargetCell.x},{s.AttackTargetCell.y}";
        else if (s.OrderMode == SpawnerOrderMode.Patrol && s.PatrolWaypoints.Count > 0)
            baseText = $"Patrol · {(s.PatrolLoop ? "Loop" : "Hold-at-Final")} · {s.PatrolWaypoints.Count}/{MonsterSpawner.MaxPatrolWaypoints} waypoints";
        else
            baseText = "Wander";

        return s.AllowDefendCore ? baseText : baseText + " · Defend OFF";
    }

    // ── Button hooks (wire in Inspector) ──────────────────────────

    public void OnWanderClicked()
    {
        if (current == null) return;
        current.ClearAllOrders();
    }

    public void OnPatrolClicked()
    {
        if (current == null) return;
        current.SetOrderMode(SpawnerOrderMode.Patrol);
        DungeonBuildController.Instance?.BeginPatrolPlacement(current);
        // The command UI hides automatically on mode change to PlaceMonsterPatrol? No —
        // PlaceMonsterPatrol is in the keep-selection list. UI stays open during placement.
        // Hide it manually for less clutter while clicking:
        Hide();
    }

    public void OnAttackHereClicked()
    {
        if (current == null) return;
        DungeonBuildController.Instance?.BeginAttackTargetPlacement(current);
        Hide();
    }

    public void OnDefendClicked()
    {
        if (current == null) return;
        current.SetAllowDefendCore(!current.AllowDefendCore);
    }

    public void OnLoopToggleChanged(bool isLoop)
    {
        Debug.Log($"Loop toggle: {isLoop}");
        if (current == null) return;
        current.SetPatrolLoop(isLoop);
    }

    public void OnClearOrdersClicked()
    {
        if (current == null) return;
        current.ClearAllOrders();
    }

    /// <summary>Phase 3 closeout (#1) - wire a "Remove" button's onClick here.</summary>
    public void OnRemoveClicked()
    {
        if (current == null) return;

        // In-combat gate (locked decision): block while the live monster is fighting.
        if (current.HasLiveMonster && current.SpawnedMonster != null && current.SpawnedMonster.IsInCombat)
        {
            BuildFeedback.Reject(current.transform.position, "Can't remove while in combat");
            return;
        }

        var spawner = current;   // capture: the dialog callback fires later
        int refund = spawner.Definition != null ? Mathf.RoundToInt(spawner.Definition.ManaCost * 0.5f) : 0;
        string msg = $"Remove this spawner? Refunds {refund} mana, frees {spawner.CapacityCost} capacity.";
        if (confirmDialog != null)
            confirmDialog.Show(msg, () => spawner.RemoveByPlayer(), null, "Remove", "Cancel");
        else
            spawner.RemoveByPlayer();   // fallback if no dialog is wired
    }

    public void OnCloseClicked()
    {
        SpawnerSelectionController.Instance?.Deselect();
    }

    /// <summary>Called by DungeonBuildController when patrol/attack placement commits.</summary>
    public void OnPlacementCommitted()
    {
        if (SpawnerSelectionController.Instance?.CurrentSelected != null) Show();
    }
}