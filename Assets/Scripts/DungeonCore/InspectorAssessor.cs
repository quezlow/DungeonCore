using System;
using UnityEngine;

/// <summary>
/// Drives the Guild's Inspector. Once the assault reaches the adventurer stage it
/// sends the first Inspector as a herald, then again on a fixed cadence to re-grade
/// (matched teams are held until that first assessment). If the player slays an
/// Inspector inside, the Guild takes the rank on the backend and sends a Hero
/// kill-team to investigate the disappearance; when that team departs, the grade is
/// revealed to the player. Dispatch state is persisted.
///
/// SCENE SETUP: put this on the persistent manager GameObject (alongside GradeSystem).
/// Uncheck the AdventurerSpawner's "Inspector Enabled" so this is the only source of
/// Inspectors.
/// </summary>
public class InspectorAssessor : MonoBehaviour
{
    public static InspectorAssessor Instance { get; private set; }

    [Header("Cadence")]
    [Tooltip("Days between scheduled inspections once the adventurer stage begins.")]
    [Min(1)][SerializeField] private int cadenceDays = 7;

    [Header("Slain-Inspector response")]
    [Tooltip("Guards in the Hero kill-team sent to investigate a slain Inspector.")]
    [Min(0)][SerializeField] private int investigationGuards = 3;

    private bool firstDispatched;
    private int cooldown;          // days until the next scheduled inspection
    private bool subscribed;

    private AdventurerParty investigation;   // runtime: the pending kill-team
    private bool investigationSeen;          // its members have been observed alive

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (subscribed || DayNightCycle.Instance == null) return;
        DayNightCycle.Instance.OnDayStarted += OnDawn;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= OnDawn;
        if (Instance == this) Instance = null;
    }

    private void OnDawn()
    {
        if (WaveStageController.Current != WaveStage.Adventurers) return;

        if (!firstDispatched)
        {
            Dispatch();
            firstDispatched = true;
            cooldown = cadenceDays;
            return;
        }

        if (cooldown > 0) { cooldown--; return; }
        Dispatch();
        cooldown = cadenceDays;
    }

    private void Dispatch() => AdventurerSpawner.Instance?.DispatchInspectorParty();

    /// <summary>Called from an Inspector's death. Take the rank on the backend and send a
    /// kill-team to investigate; the reveal waits for that team to depart.</summary>
    public void OnInspectorSlain()
    {
        GradeSystem.Instance?.AssessBackendOnly();

        if (investigation != null) return;   // one investigation at a time
        investigation = AdventurerSpawner.Instance?.DispatchInvestigationTeam(investigationGuards);
        investigationSeen = false;

        if (investigation == null)   // couldn't dispatch - don't strand the player
            GradeSystem.Instance?.RevealToPlayer();
    }

    private void Update()
    {
        if (investigation == null) return;

        if (investigation.LiveCount() > 0) { investigationSeen = true; return; }
        if (!investigationSeen) return;   // guard the frame before members register

        investigation = null;
        GradeSystem.Instance?.RevealToPlayer();
    }

    public InspectorAssessorSaveData GetSaveData()
        => new InspectorAssessorSaveData { firstDispatched = firstDispatched, cooldown = cooldown };

    public void RestoreFromSave(InspectorAssessorSaveData data)
    {
        if (data == null) return;
        firstDispatched = data.firstDispatched;
        cooldown = data.cooldown;
    }
}

[Serializable]
public class InspectorAssessorSaveData
{
    public bool firstDispatched;
    public int cooldown;
}