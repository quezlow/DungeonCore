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

        DungeonAdventurer nearest = null;
        if (coreFloor.Entities != null)
        {
            nearest = coreFloor.Entities.Nearest<DungeonAdventurer>(
                corePos, coreThreatRadius,
                adv => adv.State != AdventurerState.Retreating &&
                       adv.State != AdventurerState.UsingStairs);
        }

        bool wasThreatened = IsCoreThreatened;
        IsCoreThreatened = nearest != null;
        NearestThreat = nearest;

        if (IsCoreThreatened != wasThreatened)
        {
            OnThreatStateChanged?.Invoke(IsCoreThreatened);
            if (IsCoreThreatened) FireAlert(nearest);
        }
    }

    private void Clear()
    {
        if (!IsCoreThreatened) return;
        IsCoreThreatened = false;
        NearestThreat = null;
        OnThreatStateChanged?.Invoke(false);
    }

    private void FireAlert(DungeonAdventurer threat)
    {
        if (threat == null) return;
        int floorIdx = FloorManager.Instance != null
            ? FloorManager.Instance.CoreFloorIndex : 0;
        Vector3 pos = threat.transform.position;
        string msg = $"The core is under threat on Floor {floorIdx + 1}";

        Debug.LogWarning($"[CoreThreatMonitor] {msg}");
        AlertsLog.Instance?.AddAlert(msg, pos, floorIdx, AlertCategory.Threat);
    }
}