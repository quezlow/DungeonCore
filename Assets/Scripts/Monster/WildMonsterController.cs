using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DAY 31 PART 2 — Per-floor coordinator for wild cave monsters.
///
/// DAY 31 PART 3F — High-fidelity save/load.
///   At save time, DungeonSaveController calls GetSaveDataForChamber(id) which
///   walks currently-alive wild monsters and snapshots them (cell + HP + def name).
///   On load, RestoreFromSave prefers ChamberData.wildMonsters when non-empty;
///   if absent, falls back to a coarse re-roll using ChamberData.aliveWildCount.
/// </summary>
public class WildMonsterController : MonoBehaviour
{
    [Header("Alert (optional)")]
    [SerializeField] private FeatureAlertBanner clearedBanner;

    [Header("SFX")]
    [SerializeField] private string clearedSfxKey = "ChamberCleared";

    private FloorRoot floor;
    private TerrainFeatureGenerator features;

    private readonly Dictionary<int, List<DungeonMonster>> spawnedPerChamber = new();
    private readonly Dictionary<DungeonMonster, int> monsterToChamber = new();
    private bool subscribed;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null) { Debug.LogError($"[WildMonsterController] No FloorRoot on '{name}'."); return; }
        features = floor.FeatureGenerator;
        if (features == null) { Debug.LogError($"[WildMonsterController] No FeatureGenerator on Floor {floor.FloorIndex}."); return; }
        features.OnChamberRevealed += HandleChamberRevealed;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && features != null)
            features.OnChamberRevealed -= HandleChamberRevealed;
    }

    private void HandleChamberRevealed(int chamberId)
    {
        var ch = features.GetChamberById(chamberId);
        if (ch == null) return;
        if (ch.cleared) return;
        if (spawnedPerChamber.ContainsKey(chamberId)) return;

        int count = ch.aliveWildCount;
        if (count < 0)
        {
            count = RollWildMonsterCount(ch);
            ch.aliveWildCount = count;
        }

        if (count <= 0)
        {
            ch.aliveWildCount = 0;
            features.MarkChamberCleared(chamberId);
            return;
        }

        SpawnWildMonstersInChamber(ch, count);
    }

    public void RestoreFromSave()
    {
        if (features == null || features.FeatureData == null) return;

        foreach (var ch in features.FeatureData.chambers)
        {
            if (ch.cleared) continue;
            if (!features.IsChamberRevealed(ch.id)) continue;
            if (spawnedPerChamber.ContainsKey(ch.id)) continue;

            // DAY 31 PART 3F — Prefer high-fidelity per-monster snapshot.
            if (ch.wildMonsters != null && ch.wildMonsters.Count > 0)
            {
                RestoreWildMonstersFromSnapshot(ch);
            }
            else if (ch.aliveWildCount > 0)
            {
                // Coarse fallback for very old saves.
                SpawnWildMonstersInChamber(ch, ch.aliveWildCount);
            }
        }
    }

    // DAY 31 PART 3F — Snapshot capture for save.
    public List<WildMonsterSaveData> GetSaveDataForChamber(int chamberId)
    {
        var result = new List<WildMonsterSaveData>();
        if (!spawnedPerChamber.TryGetValue(chamberId, out var list)) return result;

        foreach (var m in list)
        {
            if (m == null) continue;
            var influence = floor?.TileInfluence;
            if (influence == null) continue;
            Vector3Int cell = influence.WorldToCell(m.transform.position);

            string defName = ResolveDefinitionName(m);

            result.Add(new WildMonsterSaveData
            {
                monsterName = defName,
                cell = SerializableVector3Int.From(cell),
                currentHP = m.CurrentHP
            });
        }
        return result;
    }

    private string ResolveDefinitionName(DungeonMonster m)
    {
        // DAY 31 — Direct lookup via the wildDefinition back-reference.
        // No more prefab-name heuristic.
        return m.WildDefinition != null ? m.WildDefinition.monsterName : "";
    }

    private int RollWildMonsterCount(ChamberData ch)
    {
        int divisor = Mathf.Max(1, features.WildMonsterCellDivisor);
        int target = ch.cells.Count / divisor;
        int count = Mathf.Clamp(target, features.WildMonsterMin, features.WildMonsterMax);
        return Mathf.Min(count, ch.cells.Count);
    }

    private void SpawnWildMonstersInChamber(ChamberData ch, int count)
    {
        var pool = features.WildMonsterPool;
        if (pool == null || pool.Count == 0)
        {
            ch.aliveWildCount = 0;
            features.MarkChamberCleared(ch.id);
            return;
        }

        int floorSeed = FloorManager.Instance != null ? FloorManager.Instance.GetFloorSeed(floor.FloorIndex) : 0;
        var rng = new System.Random(unchecked(floorSeed * 31 + ch.id));

        var influence = floor.TileInfluence;
        if (influence == null) return;

        var list = new List<DungeonMonster>(count);
        spawnedPerChamber[ch.id] = list;

        for (int i = 0; i < count; i++)
        {
            MonsterDefinition def = pool[rng.Next(pool.Count)];
            if (def == null || def.prefab == null) continue;

            var spawnCell = ch.cells[rng.Next(ch.cells.Count)].ToVector3Int();
            Vector3 worldPos = influence.CellToWorld(spawnCell);

            var monster = Instantiate(def.prefab, worldPos, Quaternion.identity);
            monster.transform.SetParent(floor.transform, true);
            monster.InitialiseWild(ch.id, floor, ConvertCellsToList(ch.cells), def);
            monster.OnDied += HandleWildMonsterDied;

            list.Add(monster);
            monsterToChamber[monster] = ch.id;
        }
    }

    // DAY 31 PART 3F — Restore from per-monster snapshot.
    private void RestoreWildMonstersFromSnapshot(ChamberData ch)
    {
        var influence = floor.TileInfluence;
        if (influence == null) return;

        var list = new List<DungeonMonster>(ch.wildMonsters.Count);
        spawnedPerChamber[ch.id] = list;

        foreach (var snap in ch.wildMonsters)
        {
            var def = LookupWildDefinition(snap.monsterName);
            if (def == null || def.prefab == null) continue;

            Vector3 worldPos = influence.CellToWorld(snap.cell.ToVector3Int());
            var monster = Instantiate(def.prefab, worldPos, Quaternion.identity);
            monster.transform.SetParent(floor.transform, true);
            monster.InitialiseWild(ch.id, floor, ConvertCellsToList(ch.cells), def);
            monster.OnDied += HandleWildMonsterDied;
            monster.SetCurrentHP(snap.currentHP);

            list.Add(monster);
            monsterToChamber[monster] = ch.id;
        }
        // DAY 31 — Fallback for legacy/bad snapshots: if zero entries resolved, fall
        // through to a coarse re-roll so the chamber isn't left without its gate.
        if (list.Count == 0 && ch.aliveWildCount > 0)
        {
            Debug.LogWarning($"[WildMonsterController] Chamber {ch.id} snapshot had no resolvable " +
                             $"definitions ({ch.wildMonsters.Count} entries). Falling back to coarse re-roll.");
            spawnedPerChamber.Remove(ch.id);
            SpawnWildMonstersInChamber(ch, ch.aliveWildCount);
            return;
        }

        ch.aliveWildCount = list.Count;
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

        if (ch.aliveWildCount <= 0 && !ch.cleared)
            ClearChamber(chamberId);
    }

    private void ClearChamber(int chamberId)
    {
        features.MarkChamberCleared(chamberId);
        int floorIdx = floor.FloorIndex;
        Vector3 worldPos = features.GetFeatureCenterWorld(FeatureType.Chamber, chamberId);
        string message = $"A cavern has been cleared on Floor {floorIdx + 1}";
        AlertsLog.Instance?.AddAlert(message, worldPos, floorIdx, AlertCategory.Discovery);
        if (clearedBanner != null) clearedBanner.Show(message, worldPos, floorIdx);
        SoundEffectManager.Play(clearedSfxKey);
    }

    /// <summary>
    /// DAY 31 — Wild monsters are authored into TerrainFeatureGenerator.WildMonsterPool,
    /// not the global MonsterDefinitionRegistry. The pool is the authoritative source
    /// for wild definitions. Falls back to the global registry for any cross-listed defs.
    /// </summary>
    private MonsterDefinition LookupWildDefinition(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var pool = features?.WildMonsterPool;
        if (pool != null)
        {
            foreach (var def in pool)
            {
                if (def == null) continue;
                if (def.monsterName == name) return def;
            }
        }

        var registry = DungeonSaveController.Instance?.GetMonsterRegistry();
        return registry != null ? registry.GetByName(name) : null;
    }
}