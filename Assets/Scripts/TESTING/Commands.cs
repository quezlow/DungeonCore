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

    [ContextMenu("Validate Execution Order Contract")]
    private void ValidateExecutionOrderContract()
    {
        // Canon Appendix D. Every manager singleton whose events are
        // subscribed to from another component's OnEnable must sit in the
        // registry tier, so its Awake has set Instance before any
        // default-order OnEnable runs. This project has no MonoManager.asset,
        // so the attribute is the whole story and reflection sees everything.
        var required = new System.Type[]
        {
            typeof(FloorManager),
            typeof(DungeonBuildController),
            typeof(SpawnerSelectionController),
            typeof(DayNightCycle),
            typeof(DungeonCore),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ExecutionOrder] Registry tier must be <= " + REGISTRY_TIER_MAX + ".");
        int bad = 0;
        foreach (var t in required)
        {
            var attrs = t.GetCustomAttributes(typeof(DefaultExecutionOrder), false);
            if (attrs.Length == 0)
            {
                sb.AppendLine("  MISSING  " + t.Name + " has no DefaultExecutionOrder.");
                bad++;
                continue;
            }
            int order = ((DefaultExecutionOrder)attrs[0]).order;
            if (order > REGISTRY_TIER_MAX)
            {
                sb.AppendLine("  TOO LATE " + t.Name + " at " + order + ".");
                bad++;
            }
            else
            {
                sb.AppendLine("  ok       " + t.Name + " at " + order + ".");
            }
        }
        sb.AppendLine(bad == 0
            ? "PASS -- every registry singleton is early enough."
            : "FAIL -- " + bad + " singleton(s) can lose the subscription race.");
        if (bad == 0) Debug.Log(sb.ToString());
        else Debug.LogError(sb.ToString());
    }

    // DungeonCore sits at -20 and is early enough for a default-order OnEnable,
    // so the tier is a ceiling rather than an exact value.
    private const int REGISTRY_TIER_MAX = -20;

    [ContextMenu("Validate Reveal Consistency")]
    private void ValidateRevealConsistency()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[RevealCheck] No FloorManager in scene."); return; }

        var sb = new System.Text.StringBuilder();
        bool anyFail = false;
        foreach (var floor in fm.AllFloors)
        {
            if (floor == null || floor.FeatureGenerator == null) continue;
            string report = floor.FeatureGenerator.BuildRevealConsistencyReport();
            if (report.Contains("FAIL")) anyFail = true;
            sb.Append(report);
        }
        if (anyFail) Debug.LogError(sb.ToString());
        else Debug.Log(sb.ToString());
    }

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

    [ContextMenu("Test Toggle Mutation Tier 1")]
    void TestToggleMutation1()
    {
        UnlockState.Toggle(MonsterMastery.TierOneKey);
        Debug.Log($"[Commands] mutation_1 unlocked = {UnlockState.IsUnlocked(MonsterMastery.TierOneKey)}");
    }

    [ContextMenu("Test Toggle Mutation Tier 2")]
    void TestToggleMutation2()
    {
        UnlockState.Toggle(MonsterMastery.TierTwoKey);
        Debug.Log($"[Commands] mutation_2 unlocked = {UnlockState.IsUnlocked(MonsterMastery.TierTwoKey)}");
    }

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
    [Tooltip("Assign the same AncientSiteProfile wired on the floor template's " +
             "TerrainFeatureGenerator. Leave null to report roads only.")]
    [SerializeField] private AncientSiteProfile siteReportProfile;

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

        // Per-stage breakdown, so a slow floor points at a culprit instead of
        // inviting a guess. Restored afterwards -- this is a live-game code path.
        bool prevTimingFlag = FloorRoot.LogBootstrapTimings;
        FloorRoot.LogBootstrapTimings = true;

        int max = fm.MaxAllowedFloorIndex;
        int start = fm.MaxFloorIndexCreated + 1;
        if (start > max) { Debug.Log($"[Commands] All {max + 1} floors already exist."); return; }

        for (int i = start; i <= max; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            fm.EnsureFloorExists(i, cell);
            sw.Stop();
            bool ok = fm.FloorExists(i);

            // Two radii, deliberately. The TABLE radius is what the progression asset
            // says the floor should be; the ACTUAL radius is what DungeonTerrain
            // resolved and painted. Logging only the first is how a floor generating
            // at the wrong size hides -- chambers and rivers have fixed counts, so
            // they never look wrong at any radius.
            int tableRadius = DungeonCore.Instance?.Progression != null
                ? DungeonCore.Instance.Progression.FloorRadius(i) : -1;
            var created = fm.GetFloor(i);
            int actualRadius = created?.Terrain != null ? created.Terrain.CurrentRadius : -1;

            Debug.Log($"[Commands] Floor {i + 1} {(ok ? "created" : "FAILED")} in {sw.ElapsedMilliseconds} ms " +
                      $"(table radius {tableRadius}, ACTUAL radius {actualRadius}).");
            if (ok && tableRadius >= 0 && actualRadius >= 0 && actualRadius != tableRadius)
                Debug.LogError($"[Commands] RADIUS MISMATCH on floor {i + 1}: painted {actualRadius}, " +
                               $"table says {tableRadius}. Everything generated on this floor is the " +
                               $"wrong size. Check DungeonTerrain.RadiusForThisFloor and fallbackRadius.");
            if (!ok) break;
        }

        FloorRoot.LogBootstrapTimings = prevTimingFlag;

        Debug.LogWarning($"[Commands] Dev side effect: core relocation is now pending on floor " +
                         $"{fm.PendingCoreRelocationFloor + 1}. Stair placement stays blocked and " +
                         $"place-core mode stays armed until a core is placed or the run is reloaded.");

        int deepest = fm.MaxFloorIndexCreated;
        fm.SwitchToFloor(deepest);
        Debug.Log($"[Commands] Viewing floor {deepest + 1}. Select its TerrainFeatureGenerator and " +
                  $"use 'Reveal All Features (debug)' to see what generated.");
    }

    [Tooltip("Kerb radius the road report fillets junctions at. Mirror the value on " +
             "TerrainFeatureGenerator.junctionFilletRadius or the report stops " +
             "describing the network the game builds. 0 reports the raw square meeting.")]
    [SerializeField, Range(0, 8)] private int roadReportFilletRadius = 3;

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

        // JUNCTION SHAPING, measured on the same terms the generator applies it.
        // Both call RoadNetworkBuilder.JunctionNodes, so a report that disagreed
        // with the game about a node would be a real bug rather than a reporting
        // quirk -- which is the point of measuring it here at all.
        int rawCarriageway = all.Count;
        var reportNodes = RoadNetworkBuilder.JunctionNodes(
            result.roads, TerrainFeatureGenerator.RoadJunctionMergeRadius);
        int filleted = 0;
        if (reportNodes.Count > 0 && result.roads.Count > 0 && roadReportFilletRadius > 0)
        {
            var r0 = result.roads[0];
            var fill = RoadNetworkBuilder.FilletJunctions(
                all, reportNodes, r0.width, roadReportFilletRadius,
                r0.floorCentre.ToVector3Int(), r0.clampRadius, null);
            foreach (var c in fill) all.Add(c);
            filleted = fill.Count;
        }
        sw.Stop();

        Debug.Log(
            $"[Commands] ROAD REPORT -- floor index {floorIdx} (floor {floorIdx + 1}), radius {radius}, " +
            $"seed {seed} ({(roadReportSeedOverride != 0 ? "override" : "derived")}), mode {entry.mode}.\n" +
            $"  roads {result.roads.Count} ({trunks} trunk, {spurs} spur, {broken} with a broken end), " +
            $"junctions {result.junctions.Count}, segments {segments}\n" +
            $"  carriageway {all.Count} cells ({rawCarriageway} raw + {filleted} junction fillet " +
            $"over {reportNodes.Count} derived nodes at radius {roadReportFilletRadius}), " +
            $"longest road {longest} centreline cells, " +
            $"reach {(minDistSq == long.MaxValue ? 0 : (int)Mathf.Sqrt(minDistSq))}..{(int)Mathf.Sqrt(maxDistSq)} from centre\n" +
            $"  built in {sw.Elapsed.TotalMilliseconds:0.0} ms, no floor instantiated.");

        if (result.roads.Count == 0)
        {
            Debug.LogWarning("[Commands] Road report produced nothing -- check junctionMinSpacing " +
                             "against the floor radius, and rimMargin against the disc size.");
            return;
        }

        // Sites ride the same headless report. They consume the SAME System.Random
        // immediately after the roads do, exactly as GenerateNew orders them, so a
        // report seeded with the floor seed reproduces the in-game layout on any
        // floor without a core cavern or entrance cave -- which is every floor
        // below the first.
        var siteResult = new AncientSiteResult();
        var siteEntry = siteReportProfile != null ? siteReportProfile.GetEntry(floorIdx) : null;
        if (siteEntry != null)
        {
            var junctions = result.junctions;
            var centrelines = new List<Vector3Int>();
            var ends = new List<Vector3Int>();
            foreach (var road in result.roads)
            {
                var line = RoadNetworkBuilder.Centreline(road);
                for (int i = 0; i < line.Count; i += 12) centrelines.Add(line[i]);
                if (line.Count > 0) ends.Add(line[line.Count - 1]);
            }

            siteResult = AncientSiteBuilder.Build(
                new System.Random(seed), Vector3Int.zero, radius, siteEntry,
                roadReportExclusionRadius, junctions, centrelines, ends,
                siteReportProfile.GetAuthoredPlans());

            int floorCells = 0, masonry = 0;
            var tally = new Dictionary<SiteArchetype, int>();
            foreach (var s in siteResult.sites)
            {
                floorCells += s.cells.Count;
                masonry += s.ruinsCells.Count;
                tally.TryGetValue(s.archetype, out int had);
                tally[s.archetype] = had + 1;
            }

            var roster = new System.Text.StringBuilder();
            foreach (var kv in tally) roster.Append(kv.Key).Append(" x").Append(kv.Value).Append("  ");

            int authoredUsed = 0;
            // Per-archetype, because variant counts differ now: the village's
            // authored plan is variant 0 of a zero-procedural archetype.
            foreach (var s in siteResult.sites)
                if (s.variant >= AncientSiteProfile.VariantCountFor(s.archetype)) authoredUsed++;

            Debug.Log(
                $"[Commands] SITE REPORT -- floor index {floorIdx}, band " +
                $"{siteEntry.bandInner:0.00}..{siteEntry.bandOuter:0.00} of radius {radius} " +
                $"(cells {Mathf.RoundToInt(radius * siteEntry.bandInner)}.." +
                $"{Mathf.RoundToInt(radius * siteEntry.bandOuter)}).\n" +
                $"  sites {siteResult.sites.Count} (authored {siteEntry.minSites}..{siteEntry.maxSites}), " +
                $"carved {floorCells} cells, masonry {masonry} cells\n" +
                $"  roster: {roster}\n" +
                $"  plans: {siteResult.sites.Count - authoredUsed} procedural, " +
                $"{authoredUsed} hand-authored\n" +
                $"  {siteResult.OutpostSummary()}, {siteResult.VillageSummary()}" +
                (siteEntry.reserveOutpost && !siteResult.outpostPlaced ? "  <-- MISSING" : "") +
                (siteEntry.reserveVillage && !siteResult.villagePlaced ? "  <-- MISSING" : ""));

            if (siteResult.sites.Count < siteEntry.minSites)
                Debug.LogWarning("[Commands] Site report placed fewer than the authored minimum -- " +
                                 "check minSpacing against the band area, and rimMargin against maxSpan.");
        }

        Debug.Log(RoadAsciiMap(all, result.junctions, siteResult.sites, radius,
                               Mathf.Max(20, roadReportMapSize)));
    }

    /// <summary>Downsamples the carriageway to a console-sized grid. '#' is road,
    /// '+' a junction, 'o' a site's carved floor, 'O' its masonry, '.' open rock,
    /// ' ' outside the disc. Sites are drawn last so a ruin standing on a road is
    /// visible rather than hidden under the carriageway.</summary>
    string RoadAsciiMap(HashSet<Vector3Int> cells, List<Vector3Int> junctions,
                        List<AncientSitePlan> sites, int radius, int size)
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

        if (sites != null)
            foreach (var s in sites)
            {
                foreach (var c in s.cells)
                {
                    int gx = Mathf.Clamp(Mathf.FloorToInt((c.x + radius) / scale), 0, size - 1);
                    int gy = Mathf.Clamp(Mathf.FloorToInt((c.y + radius) / scale), 0, size - 1);
                    grid[gx, gy] = 'o';
                }
                foreach (var c in s.ruinsCells)
                {
                    int gx = Mathf.Clamp(Mathf.FloorToInt((c.x + radius) / scale), 0, size - 1);
                    int gy = Mathf.Clamp(Mathf.FloorToInt((c.y + radius) / scale), 0, size - 1);
                    grid[gx, gy] = 'O';
                }
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

    /// <summary>Headless proof of the caravan's geometry (The Living Holds):
    /// rim ends and bearings on both dwarven floors, the bearing pairing, the
    /// anchor snaps, each leg's cell count with the speed the authored days
    /// derive, and how many segments along the route are currently held. Run
    /// after Test Generate All Floors. A missing route prints a loud FAIL --
    /// the point is a defect in seconds, not on screen in minutes.</summary>
    /// <summary>What the ladder WOULD charge for the claimed carriageway on a
    /// leg. Reported rather than read back off the ledger because the ledger
    /// keeps no running total: standing is the accumulator, and it has other
    /// contributors.</summary>
    static float StandingCostOf(int cells) => cells * DwarvenClaimLedger.StandingPerCell;

    /// <summary>Every seal on the loaded floors, what it has cost so far and
    /// whether its heart is still in place. Reported rather than inferred from
    /// alignment, which has half a dozen other contributors and cannot answer
    /// "did that seal actually register" on its own.</summary>
    /// <summary>Where the sites actually went, and why any that did not go
    /// anywhere did not.
    ///
    /// AncientSiteResult has carried per-stage rejection counters and in-band
    /// anchor counts since the site system shipped, and GenerateSites threw the
    /// whole object away after copying the sites out -- so "no sites on that
    /// floor" has only ever been answerable by guessing. It is not any more.
    ///
    /// Reads the LIVE floors, so it describes the dungeon in front of you rather
    /// than a fresh headless roll. Counters are only available for a floor
    /// generated this session: a floor restored from a save never ran the
    /// placement loop, and the report says so rather than printing zeroes that
    /// look like rejections.</summary>
    [ContextMenu("Log Site Placement")]
    void LogSitePlacement()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[Commands] No FloorManager."); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append("[Commands] SITE PLACEMENT\n");

        for (int i = 0; i < 8; i++)
        {
            var floor = fm.GetFloor(i);
            if (floor == null) continue;
            var features = floor.FeatureGenerator;
            if (features == null) continue;

            var core = floor.Terrain != null ? floor.Terrain.CoreCell : Vector3Int.zero;
            sb.Append("  floor index ").Append(i)
              .Append(": ").Append(features.SiteCount).Append(" site(s), ")
              .Append(features.RevealedSiteCount).Append(" revealed\n");

            var diag = features.LastSitePlacement;
            if (diag == null)
            {
                // Four different silences used to print the same sentence. They
                // do not any more.
                switch (features.LastSitePlacementSkip)
                {
                    case SitePlacementSkip.NoProfileAssigned:
                        sb.Append("    !! NO SITE PROFILE assigned on this floor's ")
                          .Append("TerrainFeatureGenerator. Placement never ran.\n");
                        break;
                    case SitePlacementSkip.NoFloor:
                        sb.Append("    !! FloorRoot was null when placement ran -- an ")
                          .Append("execution order fault, see canon Appendix D.\n");
                        break;
                    case SitePlacementSkip.NoEntryForFloor:
                        sb.Append("    no SiteFloorEntry for floor index ").Append(i)
                          .Append(" on the profile. Placement ran and correctly did ")
                          .Append("nothing; add an entry if this floor should carry sites.\n");
                        break;
                    default:
                        sb.Append("    placement never ran on this floor in this session ")
                          .Append("(restored from a save, or generated before this build). ")
                          .Append("Counters are recorded only where GenerateSites executed.\n");
                        break;
                }
            }
            else
            {
                sb.Append("    wanted ").Append(diag.wanted)
                  .Append(", got ").Append(diag.sites.Count)
                  .Append(", plan pool ").Append(diag.planPoolSize)
                  .Append(", attempts ").Append(diag.attempts).Append('\n');
                sb.Append("    rejected: noAnchor ").Append(diag.rejectedNoAnchor)
                  .Append(", tooClose ").Append(diag.rejectedTooClose)
                  .Append(", nullShape ").Append(diag.rejectedNullShape)
                  .Append(", tooSmall ").Append(diag.rejectedTooSmall)
                  .Append(", unwalkable ").Append(diag.rejectedUnwalkable).Append('\n');
                sb.Append("    anchors in band: junctions ").Append(diag.inBandJunctions)
                  .Append(", roadCells ").Append(diag.inBandRoadCells)
                  .Append(", roadEnds ").Append(diag.inBandRoadEnds).Append('\n');

                // A pool of zero is the one failure that looks identical to "this
                // floor was not meant to have sites", so it is called out rather
                // than left as a number among numbers.
                if (diag.planPoolSize == 0)
                    sb.Append("    !! PLAN POOL EMPTY -- the floor entry's pool names ")
                      .Append("archetypes with no authored plan and no procedural variant.\n");
            }

            for (int id = 0; id < features.SiteCount; id++)
            {
                var s = features.GetSiteById(id);
                if (s == null) continue;
                var a = s.anchorCell != null ? s.anchorCell.ToVector3Int() : Vector3Int.zero;
                int dx = a.x - core.x, dy = a.y - core.y;
                int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
                sb.Append("    site ").Append(s.id).Append(' ').Append(s.archetype)
                  .Append(" '").Append(s.planName).Append("' at ").Append(a)
                  .Append(", ").Append(dist).Append(" cells from core, ")
                  .Append(s.cells != null ? s.cells.Count : 0).Append(" carved, ")
                  .Append(features.IsSiteRevealed(s.id) ? "revealed" : "unfound");
                if (TerrainFeatureGenerator.IsHolySite(s)) sb.Append("  [HOLY]");
                sb.Append('\n');
            }
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Log Holy Ground State")]
    void LogHolyGroundState()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[Commands] No FloorManager."); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append("[Commands] HOLY GROUND -- alignment ")
          .Append(AlignmentSystem.Instance != null
              ? AlignmentSystem.Instance.Alignment.ToString("0.0") : "n/a")
          .Append(", murmured ").Append(HolyGroundLedger.TouchMurmured)
          .Append(", seals broken ").Append(HolyGroundLedger.BrokenSealCount)
          .Append('\n');

        for (int i = 0; i < 8; i++)
        {
            var floor = fm.GetFloor(i);
            var features = floor != null ? floor.FeatureGenerator : null;
            var map = floor != null ? floor.TerrainTypeMap : null;
            if (features == null || map == null || !map.HasHolySites) continue;

            int holyCells = map.HolySites.Count, mined = 0, claimed = 0;
            foreach (var kv in map.HolySites)
            {
                if (floor.TileInfluence == null) break;
                if (floor.TileInfluence.IsTileClaimed(kv.Key)) claimed++;
                if (floor.TileInfluence.IsTileMined(kv.Key)) mined++;
            }
            sb.Append("  floor index ").Append(i).Append(": ")
              .Append(holyCells).Append(" hallowed cells, ")
              .Append(claimed).Append(" claimed, ").Append(mined).Append(" mined (")
              .Append((mined * HolyGroundLedger.AlignmentPerCell).ToString("0.0"))
              .Append(" alignment spent on cells alone)\n");

            // Site ids are assigned sequentially as sites are appended,
            // so id doubles as the index. There is no list accessor and
            // adding one for a diagnostic would widen the surface for
            // nothing -- SiteCount plus GetSiteById is the shipped pair.
            for (int id = 0; id < features.SiteCount; id++)
            {
                var site = features.GetSiteById(id);
                if (!TerrainFeatureGenerator.IsHolySite(site)) continue;
                bool heartGone = site.heartCell != null && floor.TileInfluence != null
                    && floor.TileInfluence.IsTileMined(site.heartCell.ToVector3Int());
                sb.Append("    site ").Append(site.id).Append(' ')
                  .Append(site.archetype).Append(" '").Append(site.planName).Append("' ")
                  .Append(features.IsSiteRevealed(site.id) ? "revealed" : "unfound")
                  .Append(site.heartCell == null ? ", NO HEART (authoring fault)"
                                                : heartGone ? ", heart BROKEN" : ", heart intact")
                  .Append('\n');
            }
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Test Caravan Route Report")]
    void TestCaravanRouteReport()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.Log("[Commands] No FloorManager in scene."); return; }

        FloorRoot gateFloor = null, villageFloor = null;
        SiteData outpost = null, village = null;
        foreach (var floor in fm.AllFloors)
        {
            var f = floor?.FeatureGenerator;
            if (f == null || !f.HasGenerated) continue;
            if (f.GetOutpostSite() != null) { gateFloor = floor; outpost = f.GetOutpostSite(); }
            if (f.GetVillageSite() != null) { villageFloor = floor; village = f.GetVillageSite(); }
        }
        if (gateFloor == null || villageFloor == null)
        {
            Debug.Log("[Commands] Caravan report FAIL: need both dwarven floors generated (outpost "
                + (gateFloor != null) + ", village " + (villageFloor != null)
                + "). Run Test Generate All Floors first.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Commands] Caravan route report - gatehouse floor "
            + gateFloor.FloorIndex + ", village floor " + villageFloor.FloorIndex + ".");

        var gGraph = DeepRoadGraph.Build(gateFloor.FeatureGenerator.FeatureData.roads);
        var vGraph = DeepRoadGraph.Build(villageFloor.FeatureGenerator.FeatureData.roads);
        DumpCaravanRims(sb, "gatehouse", gGraph);
        DumpCaravanRims(sb, "village", vGraph);

        var gRims = DeepRoadGraph.RimEnds(gGraph);
        var vRims = DeepRoadGraph.RimEnds(vGraph);
        if (gRims.Count == 0 || vRims.Count == 0)
        {
            sb.AppendLine("FAIL: a floor exposes no rim ends - no route can exist.");
            Debug.Log(sb.ToString());
            return;
        }

        float best = float.MaxValue;
        var gPick = gRims[0];
        var vPick = vRims[0];
        foreach (var a in gRims)
            foreach (var b in vRims)
            {
                float d = DeepRoadGraph.BearingDelta(a.bearingDegrees, b.bearingDegrees);
                if (d < best) { best = d; gPick = a; vPick = b; }
            }
        sb.AppendLine("pairing: gate end " + gPick.walkTerminus + " <-> village end "
            + vPick.walkTerminus + " (bearing delta " + best.ToString("0.0") + " deg).");

        bool okO = DeepRoadGraph.NearestWalkCell(gGraph, outpost.anchorCell.ToVector3Int(),
            out int oRail, out int oIdx);
        bool okV = DeepRoadGraph.NearestWalkCell(vGraph, village.anchorCell.ToVector3Int(),
            out int vRail, out int vIdx);
        sb.AppendLine("anchor snaps: outpost "
            + (okO ? gGraph.rails[oRail].walk[oIdx].ToString() : "FAIL")
            + ", village " + (okV ? vGraph.rails[vRail].walk[vIdx].ToString() : "FAIL") + ".");
        if (!okO || !okV) { Debug.Log(sb.ToString()); return; }

        var gateRoute = DeepRoadGraph.Route(gGraph, oRail, oIdx,
            gPick.railIndex, CaravanTerminusIndex(gGraph, gPick));
        var villageRoute = DeepRoadGraph.Route(vGraph, vPick.railIndex,
            CaravanTerminusIndex(vGraph, vPick), vRail, vIdx);

        var days = DwarvenCaravanController.AuthoredDays();
        float walkDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.DayDuration : 180f;
        DumpCaravanLeg(sb, "gate leg", gateRoute, days.gateLeg, walkDay, gateFloor);
        DumpCaravanLeg(sb, "village leg", villageRoute, days.villageLeg, walkDay, villageFloor);
        sb.AppendLine("transit " + days.transit + "d each way, dwell " + days.dwell
            + "d - calendar time, nothing on screen to camp.");
        Debug.Log(sb.ToString());
    }

    static int CaravanTerminusIndex(DeepRoadGraph.Graph g, DeepRoadGraph.RimEnd rim)
    {
        var rail = g.rails[rim.railIndex];
        return rail.walk[0] == rim.walkTerminus ? 0 : rail.walk.Count - 1;
    }

    static void DumpCaravanRims(System.Text.StringBuilder sb, string name, DeepRoadGraph.Graph g)
    {
        var rims = DeepRoadGraph.RimEnds(g);
        sb.Append(name + ": " + g.rails.Count + " rails, " + rims.Count + " rim end(s)");
        foreach (var r in rims)
            sb.Append(" [" + r.walkTerminus.x + "," + r.walkTerminus.y + " @ "
                + r.bearingDegrees.ToString("0") + " deg]");
        sb.AppendLine(".");
    }

    static void DumpCaravanLeg(System.Text.StringBuilder sb, string name,
        System.Collections.Generic.List<Vector3Int> route, float authoredDays,
        float walkDaySeconds, FloorRoot floor)
    {
        if (route == null || route.Count < 2)
        {
            sb.AppendLine(name + ": FAIL - no route (graph disconnected?).");
            return;
        }
        float len = DeepRoadGraph.PathLength(route);
        float speed = len / Mathf.Max(1f, authoredDays * walkDaySeconds);
        int heldCount = 0, segCount = 0;
        var seen = new System.Collections.Generic.HashSet<int>();
        var features = floor.FeatureGenerator;
        foreach (var c in route)
            if (features.TryGetFeatureRef(c, out var fref) && fref.type == FeatureType.Road
                && seen.Add(fref.featureId))
            {
                segCount++;
                if (features.IsRoadSegmentHeld(fref.featureId)) heldCount++;
            }
        // Diagnostics before fixes. A stretch that reads UNHELD with almost
        // every cell claimed is the frayed seam or the junction fillet handing
        // a corner to a neighbouring segment, and the raw counts say so at a
        // glance instead of costing a test cycle to guess at.
        int roadCells = 0, roadClaimed = 0;
        var counted = new System.Collections.Generic.HashSet<int>();
        foreach (var c in route)
            if (features.TryGetFeatureRef(c, out var fr) && fr.type == FeatureType.Road
                && counted.Add(fr.featureId))
            {
                var cells = features.RoadSegmentCells(fr.featureId);
                if (cells == null) continue;
                roadCells += cells.Count;
                for (int i = 0; i < cells.Count; i++)
                    if (floor.TileInfluence.IsTileClaimed(cells[i])) roadClaimed++;
            }

        sb.AppendLine(name + ": " + route.Count + " cells, " + len.ToString("0")
            + " units, " + authoredDays + "d -> " + speed.ToString("0.00")
            + " u/s; " + segCount + " segments crossed, " + heldCount + " held; "
            + roadClaimed + "/" + roadCells + " carriageway cells claimed ("
            + (StandingCostOf(roadClaimed)).ToString("0.0") + " standing if billed).");
    }
}