using System.Collections.Generic;
using UnityEngine;

public class Commands : MonoBehaviour
{
    [Header("Rogue Disarm Test")]
    [Tooltip("Assign the same TrapDefinitionRegistry used by DungeonSaveController.")]
    [SerializeField] private TrapDefinitionRegistry trapRegistry;
    [Tooltip("Name of the spike-trap definition to lay for the wall test.")]
    [SerializeField] private string spikeTrapName = "Spike Trap";
    [Tooltip("Where along the entrance->core approach to place the wall (0 = at core, 1 = at entrance).")]
    [SerializeField, Range(0.1f, 0.9f)] private float wallApproachFraction = 0.45f;

    [ContextMenu("Test Build No-Gap Trap Wall")]
    void TestBuildTrapWall()
    {
        var fm = FloorManager.Instance;
        var core = DungeonCore.Instance;
        if (fm == null || core == null) { Debug.LogWarning("[Commands] No FloorManager or DungeonCore in scene."); return; }
        if (trapRegistry == null) { Debug.LogWarning("[Commands] Assign a TrapDefinitionRegistry on the Commands component first."); return; }

        var spikeDef = trapRegistry.GetByName(spikeTrapName);
        if (spikeDef == null) { Debug.LogWarning($"[Commands] No trap definition named '{spikeTrapName}'."); return; }

        var floor = fm.GetFloor(fm.CoreFloorIndex);
        var influence = floor != null ? floor.TileInfluence : null;
        if (floor == null || influence == null) { Debug.LogWarning("[Commands] Core floor has no TileInfluence."); return; }

        Vector3 coreW = core.transform.position;
        Vector3 entW = DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : coreW + Vector3.down * 10f;
        Vector3 mid = Vector3.Lerp(coreW, entW, wallApproachFraction);

        Vector2 approach = (Vector2)(coreW - entW);
        if (approach.sqrMagnitude < 0.001f) approach = Vector2.up;
        approach.Normalize();
        Vector2 perp = new Vector2(-approach.y, approach.x);
        bool horizontal = Mathf.Abs(perp.x) >= Mathf.Abs(perp.y);
        Vector3Int step = horizontal ? new Vector3Int(1, 0, 0) : new Vector3Int(0, 1, 0);

        Vector3Int center = influence.WorldToCell(mid);
        int placed = LayIfWalkable(floor, influence, spikeDef, center);
        for (int dir = -1; dir <= 1; dir += 2)
        {
            for (int i = 1; i <= 12; i++)
            {
                Vector3Int cell = center + step * (dir * i);
                if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(cell))) break;
                placed += LayIfWalkable(floor, influence, spikeDef, cell);
            }
        }
        Debug.Log($"[Commands] Laid {placed} spike(s) as a no-gap wall centred on {center} ({(horizontal ? "horizontal" : "vertical")}). If 0, nudge wallApproachFraction.");
    }

    int LayIfWalkable(FloorRoot floor, TileInfluenceManager influence, TrapDefinition def, Vector3Int cell)
    {
        if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(cell))) return 0;
        if (floor.TrapRegistry != null && floor.TrapRegistry.GetTrapAt(cell) != null) return 0;
        DungeonBuildController.Instance.RestoreTrap(floor, def, cell, false, false);
        return 1;
    }

    [ContextMenu("Test Clear Traps On Core Floor")]
    void TestClearCoreFloorTraps()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[Commands] No FloorManager in scene."); return; }
        var floor = fm.GetFloor(fm.CoreFloorIndex);
        if (floor == null || floor.Entities == null) { Debug.LogWarning("[Commands] No core floor."); return; }
        var traps = floor.Entities.GetAll<TrapBase>();
        int n = 0;
        foreach (var t in traps) { if (t != null) { Destroy(t.gameObject); n++; } }
        Debug.Log($"[Commands] Destroyed {n} trap(s) on the core floor.");
    }

    [ContextMenu("Test Add XP")]
    void TestXP() => DungeonCore.Instance.AddXP(50f);

    [ContextMenu("Test Add Lots of XP")]
    void TestLotsXP() => DungeonCore.Instance.AddXP(500f);

    [ContextMenu("Test Add So Much XP")]
    void TestSoMuchXP() => DungeonCore.Instance.AddXP(10000f);

    [ContextMenu("Test Add Mana")]
    void TestAddMana() => DungeonCore.Instance.AddMana(20f);

    [ContextMenu("Test Refill Mana")]
    void TestRefillMana() => DungeonCore.Instance.AddMana(20000f);

    [ContextMenu("Test Remove Mana")]
    void TestRemoveMana() => DungeonCore.Instance.AddMana(-20f);

    [ContextMenu("Test Add Notoriety")]
    void TestNotoriety() => DungeonCore.Instance.AddNotoriety(10f);

    [ContextMenu("Test Toggle Scout Tier 1")]
    void TestToggleScout1()
    {
        UnlockState.Toggle("tech.scout_1");
        Debug.Log($"[Commands] scout_1 unlocked = {UnlockState.IsUnlocked("tech.scout_1")}");
    }

    [ContextMenu("Test Toggle Scout Tier 2")]
    void TestToggleScout2()
    {
        UnlockState.Toggle("tech.scout_2");
        Debug.Log($"[Commands] scout_2 unlocked = {UnlockState.IsUnlocked("tech.scout_2")}");
    }

    [ContextMenu("Test Toggle Scout Tier 3")]
    void TestToggleScout3()
    {
        UnlockState.Toggle("tech.scout_3");
        Debug.Log($"[Commands] scout_3 unlocked = {UnlockState.IsUnlocked("tech.scout_3")}");
    }

    [ContextMenu("Test Toggle Oracle Chamber Unlock")]
    void TestToggleOracle()
    {
        UnlockState.Toggle(UnlockState.OracleChamber);
        Debug.Log($"[Commands] Oracle Chamber unlocked = {UnlockState.IsUnlocked(UnlockState.OracleChamber)}");
    }

    [ContextMenu("Test Toggle Adventurer Stats Unlock")]
    void TestToggleAdventurerStats()
    {
        UnlockState.Toggle(UnlockState.AdventurerStats);
        Debug.Log($"[Commands] Adventurer Stats unlocked = {UnlockState.IsUnlocked(UnlockState.AdventurerStats)}");
    }

    [ContextMenu("Test Cycle Global Monster Aggression")]
    void TestCycleAggression()
    {
        int n = System.Enum.GetValues(typeof(MonsterAggression)).Length;
        MonsterAggressionSettings.Set((MonsterAggression)(((int)MonsterAggressionSettings.Global + 1) % n));
        Debug.Log($"[Commands] Global monster aggression = {MonsterAggressionSettings.Global}");
    }

    [ContextMenu("Test Force Pending Returns Due Now")]
    void TestForcePendingReturns()
    {
        var reg = TrackedPartyRegistry.Instance;
        if (reg == null) { Debug.Log("[Commands] No TrackedPartyRegistry in scene."); return; }
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        int n = 0;
        foreach (var p in reg.PendingParties) { p.returnDay = day; n++; }
        Debug.Log($"[Commands] {n} pending part(ies) marked due today (day {day}) — next party spawn deploys one.");
    }

    [ContextMenu("Test Grant Pending Survivors 400 XP")]
    void TestGrantPendingSurvivorXp()
    {
        var reg = TrackedPartyRegistry.Instance;
        if (reg == null) { Debug.Log("[Commands] No TrackedPartyRegistry in scene."); return; }
        int n = 0;
        foreach (var p in reg.PendingParties)
            foreach (var m in p.members)
                if (m.survived) { m.xp += 400; n++; }
        Debug.Log($"[Commands] Granted 400 XP to {n} pending survivor(s) — four levels at default tuning.");
    }

    [ContextMenu("Test Dispatch Hero Party")]
    void TestDispatchHero()
    {
        if (AdventurerSpawner.Instance == null) { Debug.Log("[Commands] No AdventurerSpawner in scene."); return; }
        AdventurerSpawner.Instance.DispatchHeroParty();
        Debug.Log("[Commands] Hero party dispatched.");
    }

    [ContextMenu("Test Print Faction Standings")]
    void TestPrintFactionStandings()
    {
        var fs = FactionSystem.Instance;
        if (fs == null) { Debug.Log("[Commands] No FactionSystem in scene."); return; }
        foreach (var f in FactionInfo.All)
            Debug.Log($"[Commands] {FactionInfo.DisplayName(f)} - live {fs.Standing(f):0.#} (tier {fs.Tier(f)}), " +
                      $"shown {fs.DisplayedStanding(f):0.#} (tier {fs.DisplayedTier(f)}).");
    }

    [ContextMenu("Test Anger Adventurers Guild (-25)")]
    void TestAngerGuild()
    {
        var fs = FactionSystem.Instance;
        if (fs == null) { Debug.Log("[Commands] No FactionSystem in scene."); return; }
        fs.AddStanding(FactionId.AdventurersGuild, -25f);
        Debug.Log($"[Commands] Guild standing now {fs.Standing(FactionId.AdventurersGuild):0.#} " +
                  $"(tier {fs.Tier(FactionId.AdventurersGuild)}).");
    }

    [ContextMenu("Test Print Dungeon Rating")]
    void TestPrintDungeonRating()
    {
        var r = DungeonRating.Instance;
        if (r == null) { Debug.Log("[Commands] No DungeonRating in scene."); return; }
        Debug.Log($"[Commands] Dungeon rating {r.CurrentRating:0.#} = capacity {r.CapacityInvested():0.#} " +
                  $"+ veterans {r.VeteranBonus():0.#} + day floor {r.DayFloor():0.#}.");
    }

    [Header("Invader Test")]
    [SerializeField] private MonsterDefinition testInvaderDef;

    [ContextMenu("Test Spawn Invader")]
    [ContextMenu("Test Spawn Invader")]
    void TestSpawnInvader()
    {
        if (testInvaderDef == null || testInvaderDef.prefab == null) { Debug.Log("[Commands] Assign Test Invader Def (a MonsterDefinition with a prefab) first."); return; }
        var floor = FloorManager.Instance?.GetFloor(0);
        Vector3 pos = DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;
        var monster = Instantiate(testInvaderDef.prefab, pos, Quaternion.identity);
        if (floor != null) monster.transform.SetParent(floor.transform, true);
        monster.InitialiseInvader(floor, testInvaderDef);
        Debug.Log($"[Commands] Spawned invader '{testInvaderDef.monsterName}' at the entrance.");
    }

    [ContextMenu("Test Discover Invader Type")]
    void TestDiscoverInvader()
    {
        if (testInvaderDef == null) { Debug.Log("[Commands] Assign Test Invader Def first."); return; }
        BestiaryState.Instance?.Discover(testInvaderDef.monsterName);
    }

    [ContextMenu("Test Print Bestiary")]
    void TestPrintBestiary()
    {
        if (BestiaryState.Instance == null) { Debug.Log("[Commands] No BestiaryState in scene."); return; }
        var all = BestiaryState.Instance.AllDiscovered;
        Debug.Log(all.Count == 0 ? "[Commands] Bestiary empty." : $"[Commands] Discovered: {string.Join(", ", all)}");
    }

    [ContextMenu("Test Print Wave Stage")]
    void TestPrintWaveStage()
    {
        Debug.Log($"[Commands] Wave stage: {WaveStageController.Current} (animals: {WaveStageController.AllowAnimals}, adventurers: {WaveStageController.AllowAdventurers}).");
    }

    [ContextMenu("Test Print Adventurer Affinities")]
    void TestPrintAffinities()
    {
        var floor = FloorManager.Instance?.GetFloor(0);
        if (floor?.Entities == null) { Debug.Log("[Commands] No floor."); return; }
        int n = 0;
        foreach (var a in floor.Entities.GetAll<DungeonAdventurer>())
        {
            if (a == null) continue;
            Debug.Log($"[Commands] {a.name}: affinity {a.Affinity}.");
            n++;
        }
        if (n == 0) Debug.Log("[Commands] No adventurers on floor 0.");
    }

    [ContextMenu("Test Print Alignment")]
    void TestPrintAlignment()
    {
        var al = AlignmentSystem.Instance;
        if (al == null) { Debug.Log("[Commands] No AlignmentSystem in scene."); return; }
        string band = al.Alignment <= -20f ? "dark" : al.Alignment >= 20f ? "good" : "neutral";
        Debug.Log($"[Commands] Alignment: {al.Alignment:0.#} ({band}).");
    }

    [ContextMenu("Test Shift Alignment Dark (-15)")]
    void TestShiftDark() => AlignmentSystem.Instance?.Shift(-15f);

    [ContextMenu("Test Shift Alignment Good (+15)")]
    void TestShiftGood() => AlignmentSystem.Instance?.Shift(15f);

    [ContextMenu("Test Dispatch Holy Order Strike")]
    void TestDispatchHolyOrderStrike()
    {
        if (HolyOrderStrike.Instance == null) { Debug.Log("[Commands] No HolyOrderStrike in scene."); return; }
        HolyOrderStrike.Instance.Fire();
        Debug.Log("[Commands] Holy Order strike dispatched.");
    }

    [ContextMenu("Test Spawn Commoner Party")]
    void TestSpawnCommonerParty()
    {
        if (AdventurerSpawner.Instance == null) { Debug.Log("[Commands] No AdventurerSpawner in scene."); return; }
        AdventurerSpawner.Instance.ForceSpawnCommonerParty();
        Debug.Log("[Commands] Commoner party spawned.");
    }

    [ContextMenu("Test Assess Now")]
    void TestAssessNow()
    {
        if (GradeSystem.Instance == null) { Debug.Log("[Commands] No GradeSystem in scene."); return; }
        GradeSystem.Instance.Assess();
        Debug.Log($"[Commands] Assessed: {GradeSystem.Instance.CurrentTierName} (rating {GradeSystem.Instance.AssessedRating:0}).");
    }

    [ContextMenu("Test Dispatch Inspector")]
    void TestDispatchInspector()
    {
        if (AdventurerSpawner.Instance == null) { Debug.Log("[Commands] No AdventurerSpawner in scene."); return; }
        AdventurerSpawner.Instance.DispatchInspectorParty();
        Debug.Log("[Commands] Inspector dispatched.");
    }

    // -- Floor generation & the deep roads -------------------------

    [Header("Floor Generation / Road Report")]
    [Tooltip("Floor index the headless road report runs against. Index 4 is the fifth floor.")]
    [SerializeField] private int roadReportFloorIndex = 4;
    [Tooltip("Assign the same RoadNetworkProfile wired on the floor template's " +
             "TerrainFeatureGenerator, or the report measures a different layout.")]
    [SerializeField] private RoadNetworkProfile roadReportProfile;
    [Tooltip("0 derives the floor seed from the live world seed, exactly as floor " +
             "creation does. Any other value overrides it for a one-off look.")]
    [SerializeField] private int roadReportSeedOverride = 0;
    [Tooltip("Keep in step with TerrainFeatureGenerator's exclusionRadiusFromCenter " +
             "or the report's roads will sit differently to the generated ones.")]
    [SerializeField] private int roadReportExclusionRadius = 8;
    [Tooltip("Edge length of the ASCII map printed by the road report.")]
    [SerializeField, Range(20, 100)] private int roadReportMapSize = 60;

    [ContextMenu("Test Generate All Floors")]
    void TestGenerateAllFloors()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[Commands] No FloorManager in scene."); return; }

        var coreFloor = fm.GetFloor(fm.CoreFloorIndex);
        var core = DungeonCore.Instance;
        Vector3Int cell = coreFloor != null && coreFloor.TileInfluence != null && core != null
            ? coreFloor.TileInfluence.WorldToCell(core.transform.position)
            : Vector3Int.zero;

        int max = fm.MaxAllowedFloorIndex;
        int start = fm.MaxFloorIndexCreated + 1;
        if (start > max) { Debug.Log($"[Commands] All {max + 1} floors already exist."); return; }

        for (int i = start; i <= max; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            fm.EnsureFloorExists(i, cell);
            sw.Stop();
            bool ok = fm.FloorExists(i);
            Debug.Log($"[Commands] Floor {i + 1} {(ok ? "created" : "FAILED")} in {sw.ElapsedMilliseconds} ms " +
                      $"(radius {(DungeonCore.Instance?.Progression != null ? DungeonCore.Instance.Progression.FloorRadius(i) : -1)}).");
            if (!ok) break;
        }

        Debug.LogWarning($"[Commands] Dev side effect: core relocation is now pending on floor " +
                         $"{fm.PendingCoreRelocationFloor + 1}. Stair placement stays blocked and " +
                         $"place-core mode stays armed until a core is placed or the run is reloaded.");

        int deepest = fm.MaxFloorIndexCreated;
        fm.SwitchToFloor(deepest);
        Debug.Log($"[Commands] Viewing floor {deepest + 1}. Select its TerrainFeatureGenerator and " +
                  $"use 'Reveal All Features (debug)' to see what generated.");
    }

    [ContextMenu("Test Road Report (headless)")]
    void TestRoadReport()
    {
        if (roadReportProfile == null)
        {
            Debug.LogWarning("[Commands] Assign Road Report Profile (a RoadNetworkProfile) first.");
            return;
        }

        int floorIdx = Mathf.Max(0, roadReportFloorIndex);
        var entry = roadReportProfile.GetEntry(floorIdx);
        if (entry == null || entry.mode == RoadMode.None)
        {
            Debug.Log($"[Commands] Road report: floor index {floorIdx} has no road entry (mode None). Nothing to build.");
            return;
        }

        int radius = DungeonCore.Instance?.Progression != null
            ? DungeonCore.Instance.Progression.FloorRadius(floorIdx)
            : 400;

        int worldSeed = DungeonSaveController.Instance != null ? DungeonSaveController.Instance.WorldSeed : 0;
        int seed = roadReportSeedOverride != 0
            ? roadReportSeedOverride
            : FloorManager.DeriveFloorSeed(worldSeed, floorIdx);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = RoadNetworkBuilder.Build(
            new System.Random(seed), Vector3Int.zero, radius, entry, roadReportExclusionRadius);

        var all = new HashSet<Vector3Int>();
        int trunks = 0, spurs = 0, broken = 0, segments = 0, longest = 0;
        long minDistSq = long.MaxValue, maxDistSq = 0;

        foreach (var road in result.roads)
        {
            if (road.kind == RoadKind.Trunk) trunks++; else spurs++;
            if (road.brokenGapCells > 0) broken++;

            var line = RoadNetworkBuilder.Centreline(road);
            if (line.Count > longest) longest = line.Count;
            segments += Mathf.CeilToInt(line.Count / (float)Mathf.Max(4, road.segmentLength));

            foreach (var c in RoadNetworkBuilder.Dilate(
                         line, road.width, road.floorCentre.ToVector3Int(), road.clampRadius))
            {
                all.Add(c);
                long d = (long)c.x * c.x + (long)c.y * c.y;
                if (d < minDistSq) minDistSq = d;
                if (d > maxDistSq) maxDistSq = d;
            }
        }
        sw.Stop();

        Debug.Log(
            $"[Commands] ROAD REPORT -- floor index {floorIdx} (floor {floorIdx + 1}), radius {radius}, " +
            $"seed {seed} ({(roadReportSeedOverride != 0 ? "override" : "derived")}), mode {entry.mode}.\n" +
            $"  roads {result.roads.Count} ({trunks} trunk, {spurs} spur, {broken} with a broken end), " +
            $"junctions {result.junctions.Count}, segments {segments}\n" +
            $"  carriageway {all.Count} cells, longest road {longest} centreline cells, " +
            $"reach {(minDistSq == long.MaxValue ? 0 : (int)Mathf.Sqrt(minDistSq))}..{(int)Mathf.Sqrt(maxDistSq)} from centre\n" +
            $"  built in {sw.Elapsed.TotalMilliseconds:0.0} ms, no floor instantiated.");

        if (result.roads.Count == 0)
        {
            Debug.LogWarning("[Commands] Road report produced nothing -- check junctionMinSpacing " +
                             "against the floor radius, and rimMargin against the disc size.");
            return;
        }

        Debug.Log(RoadAsciiMap(all, result.junctions, radius, Mathf.Max(20, roadReportMapSize)));
    }

    /// <summary>Downsamples the carriageway to a console-sized grid. '#' is road,
    /// '+' a junction, '.' open rock, ' ' outside the disc.</summary>
    string RoadAsciiMap(HashSet<Vector3Int> cells, List<Vector3Int> junctions, int radius, int size)
    {
        var grid = new char[size, size];
        float scale = (2f * radius) / size;

        for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                float wx = (gx + 0.5f) * scale - radius;
                float wy = (gy + 0.5f) * scale - radius;
                grid[gx, gy] = (wx * wx + wy * wy) <= (float)radius * radius ? '.' : ' ';
            }

        foreach (var c in cells)
        {
            int gx = Mathf.Clamp(Mathf.FloorToInt((c.x + radius) / scale), 0, size - 1);
            int gy = Mathf.Clamp(Mathf.FloorToInt((c.y + radius) / scale), 0, size - 1);
            grid[gx, gy] = '#';
        }

        if (junctions != null)
            foreach (var j in junctions)
            {
                int gx = Mathf.Clamp(Mathf.FloorToInt((j.x + radius) / scale), 0, size - 1);
                int gy = Mathf.Clamp(Mathf.FloorToInt((j.y + radius) / scale), 0, size - 1);
                grid[gx, gy] = '+';
            }

        var sb = new System.Text.StringBuilder();
        sb.Append("[Commands] Road map (").Append(size).Append('x').Append(size)
          .Append(", 1 char = ").Append(scale.ToString("0.0")).Append(" cells):\n");
        for (int gy = size - 1; gy >= 0; gy--)
        {
            for (int gx = 0; gx < size; gx++) sb.Append(grid[gx, gy]);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    [ContextMenu("Test Reveal All Features (active floor)")]
    void TestRevealAllFeatures()
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.FeatureGenerator == null) { Debug.Log("[Commands] Active floor has no feature generator."); return; }
        floor.FeatureGenerator.DebugRevealAll();
    }

    [ContextMenu("Test Print Feature Stats (all floors)")]
    void TestPrintFeatureStatsAllFloors()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.Log("[Commands] No FloorManager in scene."); return; }
        int n = 0;
        foreach (var floor in fm.AllFloors)
        {
            if (floor?.FeatureGenerator == null) continue;
            floor.FeatureGenerator.LogFeatureStats();
            n++;
        }
        if (n == 0) Debug.Log("[Commands] No floors with a feature generator.");
    }

    [ContextMenu("Test Spawn Adventurer Party")]
    void TestSpawnAdventurerParty()
    {
        if (AdventurerSpawner.Instance == null) { Debug.Log("[Commands] No AdventurerSpawner in scene."); return; }
        AdventurerSpawner.Instance.ForceSpawnParty();
        Debug.Log("[Commands] Adventurer party spawned (grade-scaled if assessed).");
    }
}