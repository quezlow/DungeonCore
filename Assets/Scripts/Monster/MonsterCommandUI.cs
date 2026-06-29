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

    [Header("Aggression Stance")]
    [Tooltip("A single button that cycles Global -> Defensive -> Normal -> Aggressive. " +
             "Its label (assign the button's child TMP_Text) shows the current stance. " +
             "Applies to every selected spawner.")]
    [SerializeField] private Button aggressionButton;
    [SerializeField] private TMP_Text aggressionLabel;

    [Header("Aggression Stance Colours (Day 35)")]
    [Tooltip("Label tint per stance — tune freely.")]
    [SerializeField] private Color stanceGlobalColor = new Color(0.60f, 0.60f, 0.62f);  // dim grey
    [SerializeField] private Color stanceDefensiveColor = new Color(0.55f, 0.72f, 0.92f);  // steel
    [SerializeField] private Color stanceNormalColor = new Color(0.92f, 0.90f, 0.82f);  // parchment
    [SerializeField] private Color stanceAggressiveColor = new Color(0.85f, 0.27f, 0.27f);  // red
    [SerializeField] private Color stanceMixedColor = new Color(0.72f, 0.60f, 0.86f);  // muted violet

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

        // Code-side toggle subscription. Avoids Inspector wiring mishaps
        // (Static-bool variant fires with a fixed value regardless of toggle state).
        if (loopToggle != null)
            loopToggle.onValueChanged.AddListener(OnLoopToggleChanged);

        if (aggressionButton != null)
            aggressionButton.onClick.AddListener(OnAggressionClicked);
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

        int selCount = SpawnerSelectionController.Instance != null
            ? SpawnerSelectionController.Instance.Count : 1;
        if (patrolButton != null) patrolButton.interactable = selCount <= 1;

        if (monsterNameLabel != null)
            monsterNameLabel.text = selCount > 1
                ? $"{selCount} monsters"
                : (current.Definition != null ? current.Definition.monsterName : "Monster");

        if (statusLabel != null)
            statusLabel.text = BuildStatusText(current);

        if (loopToggle != null)
            loopToggle.SetIsOnWithoutNotify(current.PatrolLoop);

        // Defend button label reflects current permission.
        if (defendButton != null)
        {
            var defendLabel = defendButton.GetComponentInChildren<TMP_Text>();
            if (defendLabel != null)
                defendLabel.text = current.AllowDefendCore ? "Defend: ON" : "Defend: OFF";
        }

        // Aggression-stance button face shows the current (or "Mixed") stance + colour.
        if (aggressionLabel != null)
        {
            aggressionLabel.text = AggressionDisplay();
            aggressionLabel.color = StanceColor();
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

    private void ForEachSelected(System.Action<MonsterSpawner> action)
    {
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.Count > 0)
        {
            foreach (var s in sel.Selected) if (s != null) action(s);
        }
        else if (current != null) action(current);
    }

    public void OnWanderClicked()
    {
        if (current == null) return;
        ForEachSelected(s => s.ClearAllOrders());
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
        bool newValue = !current.AllowDefendCore;
        ForEachSelected(s => s.SetAllowDefendCore(newValue));
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
        ForEachSelected(s => s.ClearAllOrders());
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

    public void OnAggressionClicked()
    {
        if (current == null) return;
        var next = (MonsterStance)(((int)current.AggressionStance + 1) % 4);
        ForEachSelected(s => s.SetAggressionStance(next));
    }

    private string AggressionDisplay()
    {
        if (current == null) return "";
        var first = current.AggressionStance;
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.Count > 1)
            foreach (var s in sel.Selected)
                if (s != null && s.AggressionStance != first) return "Mixed";
        return StanceText(first);
    }

    private static string StanceText(MonsterStance s) => s switch
    {
        MonsterStance.Defensive => "Defensive",
        MonsterStance.Normal => "Normal",
        MonsterStance.Aggressive => "Aggressive",
        _ => "Global",
    };

    // DAY 35 — label colour matching the displayed stance (or Mixed across a selection).
    private Color StanceColor()
    {
        if (current == null) return stanceGlobalColor;
        var first = current.AggressionStance;
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.Count > 1)
            foreach (var s in sel.Selected)
                if (s != null && s.AggressionStance != first) return stanceMixedColor;
        return first switch
        {
            MonsterStance.Defensive => stanceDefensiveColor,
            MonsterStance.Normal => stanceNormalColor,
            MonsterStance.Aggressive => stanceAggressiveColor,
            _ => stanceGlobalColor,
        };
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