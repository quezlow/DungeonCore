using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dungeon's hidden strength score. Combines the capacity currently invested
/// in monsters and traps, a bonus for battle-hardened (veteran) monsters, and a
/// day floor that rises since the seal broke, so the number advances even for a
/// passive dungeon. Computed fresh on read.
///
/// It is deliberately invisible to the player for now: the wildlife spawner reads
/// it to scale which animals arrive, and the later Assessor overhaul surfaces this
/// same number as the inspector's grade and matches adventurer teams to it.
///
/// SCENE SETUP: put this on a persistent manager GameObject (alongside the other
/// singletons, e.g. FactionSystem). No inspector references are required.
/// </summary>
public class DungeonRating : MonoBehaviour
{
    public static DungeonRating Instance { get; private set; }

    [Header("Rating Weights")]
    [Tooltip("Rating gained per day since the seal broke - the anti-stall floor.")]
    [SerializeField] private float floorPerDay = 8f;
    [Tooltip("A veteran monster adds this fraction of its capacity cost on top of the base - a battle-hardened dungeon reads as stronger.")]
    [Min(0f)][SerializeField] private float veteranBonusFraction = 0.5f;

    private static readonly List<DungeonMonster> _buf = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>The hidden strength: capacity invested + veteran bonus + day floor.</summary>
    public float CurrentRating => CapacityInvested() + VeteranBonus() + DayFloor();

    /// <summary>Capacity the dungeon is currently spending on monsters and traps.</summary>
    public float CapacityInvested() => DungeonCore.Instance != null ? DungeonCore.Instance.UsedCapacity : 0f;

    /// <summary>The rising baseline: floorPerDay for each day since the entrance was breached.</summary>
    public float DayFloor()
    {
        var cave = FloorManager.Instance?.GetFloor(0)?.FeatureGenerator?.EntranceCave;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        int breach = (cave != null && cave.discovered && cave.discoveredDay >= 0) ? cave.discoveredDay : 1;
        return Mathf.Max(0, day - breach) * floorPerDay;
    }

    /// <summary>Extra rating from veteran (battle-hardened) player monsters. Wild
    /// monsters never veteran, so this naturally counts only the dungeon's own.</summary>
    public float VeteranBonus()
    {
        if (veteranBonusFraction <= 0f || FloorManager.Instance == null) return 0f;
        float bonus = 0f;
        foreach (var floor in FloorManager.Instance.AllFloors)
        {
            if (floor?.Entities == null) continue;
            floor.Entities.FillAll(_buf);
            for (int i = 0; i < _buf.Count; i++)
            {
                var m = _buf[i];
                if (m == null || m.IsWild || !m.IsVeteran) continue;
                bonus += (m.Spawner != null ? m.Spawner.CapacityCost : 0) * veteranBonusFraction;
            }
        }
        return bonus;
    }
}