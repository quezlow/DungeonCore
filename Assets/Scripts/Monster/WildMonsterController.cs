using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DAY 31 PART 2 — Per-floor coordinator for wild cave monsters.
///
/// Lives as a sibling component on each floor's hierarchy alongside
/// TerrainFeatureGenerator, FeatureRevealController, and TileInfluenceManager.
/// Wired via FloorRoot.WildMonsterController.
///
/// HOW IT WORKS
///   - Subscribes (in Awake) to TerrainFeatureGenerator.OnChamberRevealed.
///   - When a chamber is revealed for the first time, rolls the wild monster
///     count (clamp(cellCount / divisor, min, max)), picks definitions from
///     the floor's wildMonsterPool, and instantiates them at random chamber
///     cells. Pool picks and spawn positions are deterministic per chamber
///     (seeded from floorSeed and chamberId).
///   - Subscribes to DungeonMonster.OnDied on every spawned wild monster.
///     On death, decrements ChamberData.aliveWildCount. When the count
///     reaches zero, marks the chamber cleared and fires a banner + SFX.
///
/// CHAMBER GATE OPENING
///   When the last wild monster in a chamber dies:
///     - features.MarkChamberCleared(chamberId) flips the cleared flag.
///     - TileInfluenceManager / DungeonBuildController will now allow
///       claim clicks on cells inside that chamber (gate logic checks
///       features.IsCellInUnclearedChamber).
///     - A "cavern cleared" banner pops via the shared FeatureAlertBanner.
///
/// PERSISTENCE
///   - aliveWildCount and cleared live on ChamberData (serialized via
///     FloorFeatureSaveData).
///   - On load, RestoreFromSave() walks every revealed-and-uncleared
///     chamber, respawns aliveWildCount monsters in that chamber. Exact
///     positions and HPs from before the save are not preserved — the
///     coarse "how many alive" count is. Called by DungeonSaveController
///     after feature data restore.
///
/// AGGRO OUTWARD
///   The spawn doesn't drive aggro — DungeonMonster's wild wander logic
///   handles the 30% chance per pick to walk to an adjacent owned cell.
///   This controller just spawns and counts.
/// </summary>
public class WildMonsterController : MonoBehaviour
{
    [Header("Alert (optional)")]
    [Tooltip("Banner used to show 'A cavern has been cleared on Floor N' when the last " +
             "wild monster in a chamber dies. Wire the same FeatureAlertBanner used by " +
             "FeatureRevealController so reveal and clear messages share the UI element.")]
    [SerializeField] private FeatureAlertBanner clearedBanner;

    [Header("SFX")]
    [Tooltip("SoundEffectLibrary key for the chamber-cleared sound. Missing clip is fine.")]
    [SerializeField] private string clearedSfxKey = "ChamberCleared";

    private FloorRoot floor;
    private TerrainFeatureGenerator features;

    // Tracks spawned wild monsters by chamber id so we know whose OnDied
    // belongs to which chamber even if the monster is destroyed externally.
    private readonly Dictionary<int, List<DungeonMonster>> spawnedPerChamber = new();
    private readonly Dictionary<DungeonMonster, int> monsterToChamber = new();

    private bool subscribed;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogError($"[WildMonsterController] No FloorRoot in parent of '{name}'.");
            return;
        }

        features = floor.FeatureGenerator;
        if (features == null)
        {
            Debug.LogError($"[WildMonsterController] Floor {floor.FloorIndex} has no TerrainFeatureGenerator.");
            return;
        }

        features.OnChamberRevealed += HandleChamberRevealed;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && features != null)
            features.OnChamberRevealed -= HandleChamberRevealed;
    }

    // ── Event Handler ─────────────────────────────────────────────

    /// <summary>
    /// Called by TerrainFeatureGenerator.RevealChamber on first reveal.
    /// Also called via WildMonsterController.RestoreFromSave when load
    /// re-enters revealed chambers. Idempotent on chambers that have
    /// already had their wild monsters spawned this session.
    /// </summary>
    private void HandleChamberRevealed(int chamberId)
    {
        var ch = features.GetChamberById(chamberId);
        if (ch == null) return;
        if (ch.cleared) return;

        // Already spawned in this session — don't double up.
        if (spawnedPerChamber.ContainsKey(chamberId)) return;

        // aliveWildCount semantics:
        //   -1 → never spawned; roll a fresh count.
        //   >0 → mid-session spawn count (from a save load).
        //   0  → no monsters but chamber not flagged cleared. Treat as needs-clear-mark.
        int count = ch.aliveWildCount;
        if (count < 0)
        {
            count = RollWildMonsterCount(ch);
            ch.aliveWildCount = count;
        }

        if (count <= 0)
        {
            // Edge case: empty pool or zero-roll. Clear immediately so we don't gate forever.
            ch.aliveWildCount = 0;
            features.MarkChamberCleared(chamberId);
            return;
        }

        SpawnWildMonstersInChamber(ch, count);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonSaveController on load, AFTER TerrainFeatureGenerator.LoadFromSave
    /// has restored reveal state. For each chamber that's revealed-but-not-cleared with
    /// aliveWildCount > 0, respawns that many wild monsters. Exact pre-save positions
    /// and HPs are not preserved — fresh chamber-random spawns at full HP.
    /// </summary>
    public void RestoreFromSave()
    {
        if (features == null || features.FeatureData == null) return;

        foreach (var ch in features.FeatureData.chambers)
        {
            if (ch.cleared) continue;
            if (!features.IsChamberRevealed(ch.id)) continue;
            if (ch.aliveWildCount <= 0) continue;
            if (spawnedPerChamber.ContainsKey(ch.id)) continue;

            SpawnWildMonstersInChamber(ch, ch.aliveWildCount);
        }
    }

    // ── Internals ─────────────────────────────────────────────────

    private int RollWildMonsterCount(ChamberData ch)
    {
        int divisor = Mathf.Max(1, features.WildMonsterCellDivisor);
        int target = ch.cells.Count / divisor;
        int count = Mathf.Clamp(target, features.WildMonsterMin, features.WildMonsterMax);
        // Final safety: never spawn more than the chamber has cells.
        return Mathf.Min(count, ch.cells.Count);
    }

    private void SpawnWildMonstersInChamber(ChamberData ch, int count)
    {
        var pool = features.WildMonsterPool;
        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning($"[WildMonsterController] Wild monster pool is empty on Floor {floor.FloorIndex}. " +
                             $"Chamber {ch.id} auto-cleared (no gate possible).");
            ch.aliveWildCount = 0;
            features.MarkChamberCleared(ch.id);
            return;
        }

        // Deterministic per-chamber RNG: floorSeed * 31 + chamberId.
        int floorSeed = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloorSeed(floor.FloorIndex)
            : 0;
        var rng = new System.Random(unchecked(floorSeed * 31 + ch.id));

        var influence = floor.TileInfluence;
        if (influence == null)
        {
            Debug.LogError("[WildMonsterController] No TileInfluence on floor — cannot spawn.");
            return;
        }

        var list = new List<DungeonMonster>(count);
        spawnedPerChamber[ch.id] = list;

        for (int i = 0; i < count; i++)
        {
            // Choose a definition.
            MonsterDefinition def = null;
            int defAttempts = 0;
            while (def == null && defAttempts < pool.Count + 1)
            {
                def = pool[rng.Next(pool.Count)];
                if (def == null || def.prefab == null) def = null;
                defAttempts++;
            }
            if (def == null)
            {
                Debug.LogWarning($"[WildMonsterController] Pool contains no usable MonsterDefinitions for chamber {ch.id}.");
                ch.aliveWildCount = list.Count;
                if (list.Count == 0) features.MarkChamberCleared(ch.id);
                return;
            }

            // Pick a spawn cell within the chamber.
            var spawnCell = ch.cells[rng.Next(ch.cells.Count)].ToVector3Int();
            Vector3 worldPos = influence.CellToWorld(spawnCell);

            // Instantiate. Parent under the floor so GetComponentInParent<FloorRoot> resolves.
            var monster = Instantiate(def.prefab, worldPos, Quaternion.identity);
            monster.transform.SetParent(floor.transform, true);

            // Wild-init BEFORE Start runs — needs the chamber cell list and floor ref.
            monster.InitialiseWild(ch.id, floor, ConvertCellsToList(ch.cells));

            // Subscribe to death so we can decrement count.
            monster.OnDied += HandleWildMonsterDied;

            list.Add(monster);
            monsterToChamber[monster] = ch.id;
        }

        Debug.Log($"[WildMonsterController] Spawned {list.Count} wild monsters in chamber {ch.id} on Floor {floor.FloorIndex}.");
    }

    private static List<Vector3Int> ConvertCellsToList(List<SerializableVector3Int> serialized)
    {
        var result = new List<Vector3Int>(serialized.Count);
        foreach (var sv in serialized) result.Add(sv.ToVector3Int());
        return result;
    }

    private void HandleWildMonsterDied(DungeonMonster m)
    {
        if (m == null) return;
        if (!monsterToChamber.TryGetValue(m, out int chamberId)) return;

        monsterToChamber.Remove(m);
        if (spawnedPerChamber.TryGetValue(chamberId, out var list))
            list.Remove(m);

        var ch = features.GetChamberById(chamberId);
        if (ch == null) return;

        ch.aliveWildCount = Mathf.Max(0, ch.aliveWildCount - 1);
        Debug.Log($"[WildMonsterController] Wild monster died in chamber {chamberId} (Floor {floor.FloorIndex}); " +
                  $"{ch.aliveWildCount} remaining.");

        if (ch.aliveWildCount <= 0 && !ch.cleared)
            ClearChamber(chamberId);
    }

    private void ClearChamber(int chamberId)
    {
        features.MarkChamberCleared(chamberId);

        int floorIdx = floor.FloorIndex;
        Vector3 worldPos = features.GetFeatureCenterWorld(FeatureType.Chamber, chamberId);
        string message = $"A cavern has been cleared on Floor {floorIdx + 1}";

        AlertsLog.Instance?.AddAlert(message, worldPos, floorIdx);

        if (clearedBanner != null)
            clearedBanner.Show(message, worldPos, floorIdx);

        SoundEffectManager.Play(clearedSfxKey);

        Debug.Log($"[WildMonsterController] Chamber {chamberId} cleared on Floor {floorIdx}.");
    }
}
