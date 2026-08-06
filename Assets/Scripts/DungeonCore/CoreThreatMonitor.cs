using UnityEngine;
using static DungeonAdventurer;

/// <summary>
/// DAY 31 PART 3 CLOSE-OUT — Polls for adventurer threats to the dungeon core
/// and exposes a single IsCoreThreatened flag for monsters to read cheaply.
///
/// PLACEMENT
///   Add this component to the DungeonCore GameObject (sits alongside
///   DungeonCore.cs). Singleton — only one instance per scene.
///
/// THREAT DEFINITION
///   Any DungeonAdventurer within CoreThreatRadius world units of the core's
///   transform.position, on the core's current floor, whose state is NOT
///   Retreating or UsingStairs.
///
/// EVENTS
///   OnThreatStateChanged(bool) — fires on transitions only. Connect alert
///   banners and SFX here, NOT on every poll tick.
/// </summary>
public class CoreThreatMonitor : MonoBehaviour
{
    public static CoreThreatMonitor Instance { get; private set; }

    [Header("Tuning")]
    [Tooltip("Seconds between threat polls. 0.5 is a sensible default — " +
             "reactive enough for the player to see monsters respond, cheap enough " +
             "that the cost is negligible.")]
    [SerializeField, Min(0.05f)] private float pollInterval = 0.5f;

    [Tooltip("World-unit radius around the core within which a non-retreating " +
             "adventurer counts as a threat. Tune for floor scale.")]
    [SerializeField, Min(0f)] private float coreThreatRadius = 10f;

    public bool IsCoreThreatened { get; private set; }
    public DungeonAdventurer NearestThreat { get; private set; }
    public float CoreThreatRadius => coreThreatRadius;

    public event System.Action<bool> OnThreatStateChanged;

    private float pollTimer;
    private bool visitorsPresent;
    private bool invaderNearCore;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        pollTimer -= Time.deltaTime;
        if (pollTimer > 0f) return;
        pollTimer = pollInterval;
        Poll();
    }

    private void Poll()
    {
        if (DungeonCore.Instance == null) { Clear(); return; }
        if (FloorManager.Instance == null) { Clear(); return; }

        Vector3 corePos = DungeonCore.Instance.transform.position;
        int coreFloorIndex = FloorManager.Instance.CoreFloorIndex;
        var coreFloor = FloorManager.Instance.GetFloor(coreFloorIndex);
        if (coreFloor == null) { Clear(); return; }

        DungeonAdventurer nearestThreat = null;
        DungeonAdventurer nearestVisitor = null;
        DungeonMonster nearestInvader = null;
        if (coreFloor.Entities != null)
        {
            // Only core-breaching goals (Mercenary / Hero / Suicidal) count as a threat.
            nearestThreat = coreFloor.Entities.Nearest<DungeonAdventurer>(
                corePos, coreThreatRadius,
                adv => adv.State != AdventurerState.Retreating &&
                       adv.State != AdventurerState.UsingStairs &&
                       adv.ThreatensCore);

            // Peaceful arrivals near the core (pilgrims, cultists, observers, looters).
            nearestVisitor = coreFloor.Entities.Nearest<DungeonAdventurer>(
                corePos, coreThreatRadius,
                adv => adv.State != AdventurerState.Retreating &&
                       adv.State != AdventurerState.UsingStairs &&
                       !adv.ThreatensCore);

            // Invaders are MONSTERS, not adventurers, and this monitor scanned
            // only adventurers -- so a wild or climax beast marching on the core
            // was invisible here and the first the player heard of it was
            // DungeonMonster calling DestroyCore at invaderBreachDistance. Same
            // radius as an adventurer threat, because it is the same question.
            nearestInvader = coreFloor.Entities.Nearest<DungeonMonster>(
                corePos, coreThreatRadius,
                m => m != null && m.IsInvader);
        }

        bool wasThreatened = IsCoreThreatened;
        IsCoreThreatened = nearestThreat != null;
        NearestThreat = nearestThreat;

        if (IsCoreThreatened != wasThreatened)
        {
            OnThreatStateChanged?.Invoke(IsCoreThreatened);
            if (IsCoreThreatened) FireThreatAlert(nearestThreat);
        }

        // DELIBERATELY OUTSIDE IsCoreThreatened, which is not merely a report:
        // DungeonMonster reads it to decide whether to rally to the core, and
        // NearestThreat is typed DungeonAdventurer. Folding invaders into it
        // would change monster behaviour and the field's type in one go. This
        // watch raises an alert and nothing else; whether the garrison should
        // turn out for a beast as well as for a Destroyer is a separate call.
        bool hadInvader = invaderNearCore;
        invaderNearCore = nearestInvader != null;
        if (invaderNearCore && !hadInvader) FireInvaderAlert(nearestInvader);

        bool hadVisitors = visitorsPresent;
        visitorsPresent = nearestVisitor != null;
        if (visitorsPresent && !hadVisitors) FireVisitorAlert(nearestVisitor);
    }

    private void Clear()
    {
        visitorsPresent = false;

        // Reset with the rest, or a floor change would leave the flag latched
        // and the next invader to arrive would raise no alert at all.
        invaderNearCore = false;

        if (!IsCoreThreatened) return;
        IsCoreThreatened = false;
        NearestThreat = null;
        OnThreatStateChanged?.Invoke(false);
    }

    private void FireThreatAlert(DungeonAdventurer threat)
    {
        if (threat == null) return;
        int floorIdx = FloorManager.Instance != null
            ? FloorManager.Instance.CoreFloorIndex : 0;
        Vector3 pos = threat.transform.position;
        string msg = $"The core is under threat on Floor {floorIdx + 1}";

        Debug.LogWarning($"[CoreThreatMonitor] {msg}");
        AlertsLog.Instance?.AddAlert(msg, pos, floorIdx, AlertCategory.Threat,
                                     AlertSeverity.Critical);
    }

    /// <summary>A beast is inside the core's radius. Critical, on the same footing
    /// as a Destroyer arriving, because it ends the same way: DungeonMonster calls
    /// DestroyCore the moment it closes to invaderBreachDistance.</summary>
    private void FireInvaderAlert(DungeonMonster invader)
    {
        if (invader == null) return;
        int floorIdx = FloorManager.Instance != null
            ? FloorManager.Instance.CoreFloorIndex : 0;
        Vector3 pos = invader.transform.position;
        string msg = $"A beast has reached the core on Floor {floorIdx + 1}";

        Debug.LogWarning($"[CoreThreatMonitor] {msg}");
        AlertsLog.Instance?.AddAlert(msg, pos, floorIdx, AlertCategory.Threat,
                                     AlertSeverity.Critical);
    }

    // Peaceful approach — pilgrims, cultists, scholars, inspectors. Not a danger.
    private void FireVisitorAlert(DungeonAdventurer visitor)
    {
        if (visitor == null) return;
        int floorIdx = FloorManager.Instance != null
            ? FloorManager.Instance.CoreFloorIndex : 0;
        Vector3 pos = visitor.transform.position;
        string msg = $"Peaceful visitors near the core on Floor {floorIdx + 1}";

        Debug.Log($"[CoreThreatMonitor] {msg}");
        AlertsLog.Instance?.AddAlert(msg, pos, floorIdx, AlertCategory.Discovery);
    }
}