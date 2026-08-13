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

    /// <summary>Chambers the DUNGEON has wounded something in. NOT persisted, on
    /// dungeonDealtDamage's own documented precedent: a reload part-way through a
    /// clear asks for one more blow, which is a fair price for not adding a save
    /// field to a flag that lives for the length of one fight.</summary>
    private readonly HashSet<int> contestedChambers = new HashSet<int>();

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

    /// <summary>The largest 4-connected region of the chamber's recorded
    /// cells. Old saves may carry sealed islets from pre-fix generation;
    /// everything that spawns or restores stays in the open body.</summary>
    private List<Vector3Int> OpenChamberCells(ChamberData ch)
    {
        var remaining = new HashSet<Vector3Int>();
        foreach (var sv in ch.cells) remaining.Add(sv.ToVector3Int());
        var best = new List<Vector3Int>();
        var region = new List<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        while (remaining.Count > 0)
        {
            region.Clear();
            Vector3Int seed = default;
            foreach (var c0 in remaining) { seed = c0; break; }
            remaining.Remove(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                region.Add(c);
                foreach (var n in new[] {
                    new Vector3Int(c.x + 1, c.y, 0), new Vector3Int(c.x - 1, c.y, 0),
                    new Vector3Int(c.x, c.y + 1, 0), new Vector3Int(c.x, c.y - 1, 0) })
                    if (remaining.Remove(n)) queue.Enqueue(n);
            }
            if (region.Count > best.Count) best = new List<Vector3Int>(region);
        }
        return best;
    }

    /// <summary>The ONE wild type this chamber fields, or null if none is
    /// eligible.
    ///
    /// A CHAMBER IS ONE SPECIES. The draw used to run per body, which read as a
    /// menagerie sharing a cave and -- with a two-entry pool -- averaged every
    /// chamber on the floor to the same encounter. One species per chamber is
    /// legible on sight and puts the variety BETWEEN a floor's caves instead of
    /// inside each one.
    ///
    /// The type has to be chosen BEFORE the count, because the count is clamped
    /// by that type's own band. So this is called from RollWildMonsterCount and
    /// from SpawnWildMonstersInChamber, and the two agree BY CONSTRUCTION rather
    /// than by being kept in step: the RNG is seeded from the floor seed and the
    /// chamber id, and this is its FIRST draw, so a fresh instance always answers
    /// the same. That is also what keeps the coarse re-roll on the load path
    /// consistent with the count persisted at reveal.</summary>
    private MonsterDefinition PickChamberWildType(ChamberData ch)
    {
        var pool = features.WildMonsterPool;
        if (pool == null || pool.Count == 0) return null;

        // Depth banding: a definition below its minimum wild floor never rolls
        // here, so one shared template pool can carry deep-floor wilds without
        // them surfacing in floor-0 chambers.
        var depthPool = new List<MonsterDefinition>(pool.Count);
        for (int p = 0; p < pool.Count; p++)
            if (pool[p] != null && floor.FloorIndex >= pool[p].minWildFloor)
                depthPool.Add(pool[p]);
        if (depthPool.Count == 0) return null;

        int floorSeed = FloorManager.Instance != null ? FloorManager.Instance.GetFloorSeed(floor.FloorIndex) : 0;
        var rng = new System.Random(unchecked(floorSeed * 31 + ch.id));
        return depthPool[rng.Next(depthPool.Count)];
    }

    /// <summary>How many bodies this chamber holds. Chamber size still drives the
    /// target through the shared cell divisor; the CLAMP is now the chosen type's
    /// own band rather than one global pair.
    ///
    /// The global wildMonsterMin and wildMonsterMax were RETIRED rather than kept
    /// as an outer clamp. Kept, they would have silently capped a band of eight
    /// back to six, and a band that is quietly overridden reads in the inspector
    /// exactly like a band that does not work.</summary>
    private int RollWildMonsterCount(ChamberData ch)
    {
        var def = PickChamberWildType(ch);
        if (def == null) return 0;

        int divisor = Mathf.Max(1, features.WildMonsterCellDivisor);
        int target = ch.cells.Count / divisor;
        int lo = Mathf.Max(1, def.wildCountMin);
        int hi = Mathf.Max(lo, def.wildCountMax);
        int count = Mathf.Clamp(target, lo, hi);
        return Mathf.Min(count, ch.cells.Count);
    }

    private void SpawnWildMonstersInChamber(ChamberData ch, int count)
    {
        // One species per chamber -- see PickChamberWildType. This resolves to the
        // same definition the count was clamped against, including on the coarse
        // re-roll the load path falls back to.
        var def = PickChamberWildType(ch);
        if (def == null || def.prefab == null)
        {
            ch.aliveWildCount = 0;
            features.MarkChamberCleared(ch.id);
            return;
        }

        // A SEPARATE seed from the type draw, so the species a chamber gets and
        // the cells its bodies stand on are not one number read twice.
        int floorSeed = FloorManager.Instance != null ? FloorManager.Instance.GetFloorSeed(floor.FloorIndex) : 0;
        var rng = new System.Random(unchecked(floorSeed * 31 + ch.id + 7919));

        var influence = floor.TileInfluence;
        if (influence == null) return;

        var list = new List<DungeonMonster>(count);
        spawnedPerChamber[ch.id] = list;

        // Saves from before the connectivity fix can carry sealed islets in
        // ch.cells; confine spawns to the chamber's open body.
        var openCells = OpenChamberCells(ch);
        if (openCells.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            var spawnCell = openCells[rng.Next(openCells.Count)];
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

        var openSnap = OpenChamberCells(ch);
        var openSet = new HashSet<Vector3Int>(openSnap);

        foreach (var snap in ch.wildMonsters)
        {
            var def = LookupWildDefinition(snap.monsterName);
            if (def == null || def.prefab == null) continue;

            var snapCell = snap.cell.ToVector3Int();
            if (!openSet.Contains(snapCell) && openSnap.Count > 0)
                snapCell = openSnap[Mathf.Abs(snapCell.x + snapCell.y) % openSnap.Count];
            Vector3 worldPos = influence.CellToWorld(snapCell);
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

        // Record the dungeon's hand BEFORE the clear test, because the killing
        // blow arrives through here and a flag set afterwards would be too late.
        // Same wound-not-killing-blow test the bestiary and the den ledger use,
        // for the same stated reason: a creature your monsters wore down should
        // still count when something else takes the last hit.
        if (m.DungeonDealtDamage) contestedChambers.Add(chamberId);

        var ch = features.GetChamberById(chamberId);
        if (ch == null) return;
        ch.aliveWildCount = Mathf.Max(0, ch.aliveWildCount - 1);

        if (ch.aliveWildCount <= 0 && !ch.cleared)
            ClearChamber(chamberId);
    }

    /// <summary>Mark the chamber cleared, and ANNOUNCE it only if the dungeon had
    /// a hand in it.
    ///
    /// A CHAMBER CLEARED WITHOUT THE DUNGEON'S HAND GOES SILENT. Wild-versus-wild
    /// hostility is by tribe now, so a den's people can empty a cave the player
    /// never touched and may never have seen -- and a Discovery alert, a banner
    /// and a sound for that is the game congratulating the player on somebody
    /// else's work, on a floor they are probably not even looking at.
    ///
    /// THE CLEARED STATE STILL STANDS. The chamber really is empty, nothing
    /// respawns, and the player finds it quiet when they eventually arrive. Only
    /// the announcement is withheld, which is the same split canon 42 already
    /// draws for a den: adventurers can empty one, but only the dungeon's own
    /// hand collects on it.</summary>
    private void ClearChamber(int chamberId)
    {
        features.MarkChamberCleared(chamberId);
        if (!contestedChambers.Remove(chamberId)) return;

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