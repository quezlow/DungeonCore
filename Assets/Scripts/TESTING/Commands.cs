using System.Collections.Generic;
using UnityEngine;

public class Commands : MonoBehaviour
{
    [Header("Rogue Disarm Test")]
    [Tooltip("Assign the same TrapDefinitionRegistry used by DungeonSaveController.")]
    [SerializeField] private TrapDefinitionRegistry trapRegistry;
    [Tooltip("Name of the spike-trap definition to lay for the wall test.")]
    [SerializeField] private string spikeTrapName = "Spike Trap";
    [Header("Den Tunnels")]
    [Tooltip("The DenTunnelProfile asset. Only the headless report reads it.")]
    [SerializeField] private DenTunnelProfile denTunnelProfile;
    [Tooltip("Seeds per floor for the headless den tunnel report.")]
    [SerializeField, Min(1)] private int denReportSeeds = 2000;

    [Tooltip("Where along the entrance->core approach to place the wall (0 = at core, 1 = at entrance).")]
    [SerializeField, Range(0.1f, 0.9f)] private float wallApproachFraction = 0.45f;

    [ContextMenu("Validate Spell Picker Wiring")]
    private void ValidateSpellPickerWiring()
    {
        var bar = FindAnyObjectByType<ActionBarHUD>();
        if (bar == null)
        {
            Debug.LogWarning("[SpellRow] No ActionBarHUD in the scene. "
                + "Cast mode has nowhere to draw.");
            return;
        }
        string faults = bar.ValidateSpellRowWiring();
        if (faults == null) Debug.Log("[SpellRow] Wiring is whole.");
        else Debug.LogWarning(faults);
    }

    /// <summary>The pause register (canon 39). Prints the live hold state and
    /// every player-reachable action with its ruling, so drift is visible in one
    /// place instead of being rediscovered by sweeping eleven files. A new
    /// action belongs in this table the day it is written.</summary>
    /// <summary>
    /// Headless den report: what every den currently holds, and who is out.
    ///
    /// Exists because a den's whole state is invisible in play by design -- the
    /// hoard is a number, the held tomes and spoil rarities are strings in a save
    /// entry, and the contest flag decides whether clearing pays out at all. A
    /// ledger that quietly earns nothing looks exactly like a ledger that is
    /// working, which is the failure the dawn-tick log was added to catch and this
    /// is the same argument one level up.
    ///
    /// Prints the population split as well as the totals, because residents and
    /// foragers are different numbers and a den stealing far off its tuned share is
    /// diagnosed by which of the two is wrong.
    /// </summary>
    [ContextMenu("Print Den Report")]
    private void PrintDenReport()
    {
        var den = DenController.Instance;
        if (den == null) { Debug.Log("[DenReport] No DenController in the scene."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[DenReport]");

        int dens = 0;
        foreach (var entry in den.AllDens)
        {
            dens++;
            int tier = den.TierOf(entry.floorIndex);
            sb.AppendLine("  floor " + entry.floorIndex + "  " + (DenKind)entry.kind
                + "  tier " + tier + (entry.cleared ? "  CLEARED" : ""));
            sb.AppendLine("    hoard " + entry.hoard.ToString("0")
                + "   stolen purse " + entry.stolenHoard.ToString("0")
                + "   stolen lifetime " + entry.stolenTotal.ToString("0")
                + "   raids " + entry.raidsLaunched
                + "   next raid in " + entry.raidCountdown.ToString("0") + "d");
            sb.AppendLine("    awakened day " + entry.awakenedDay
                + "   foraging " + (den.MayForageAny(entry.floorIndex) ? "yes" : "no")
                + "   contested " + (entry.contested ? "yes" : "NO -- clearing will not pay out"));
            sb.AppendLine("    population budget " + den.PopulationBudget(entry.floorIndex)
                + "   at the face " + den.DiggerBudget(entry.floorIndex)
                + "   of which abroad " + den.ScavengerBudget(entry.floorIndex)
                + "   target share " + (den.TargetStealShare(entry.floorIndex) * 100f).ToString("0") + "%");
            sb.AppendLine("    held tomes " + entry.heldNodeKeys.Count
                + "   held spoil rarities " + entry.heldSpoilRarities.Count
                + "   remains taken " + entry.remainsTaken);
        }
        if (dens == 0) sb.AppendLine("  No dens registered. No floor above 0 has been created yet,");
        sb.AppendLine("");

        // Live bodies, walked from the scene rather than the ledger, so the two can
        // be compared: a mismatch means the population loop is not keeping up, or
        // bodies are being destroyed by something that is not a death.
        var bodies = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
        int scavengers = 0, laden = 0, haul = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsDenScavenger) continue;
            scavengers++;
            if (bodies[i].CarriedHaul > 0) { laden++; haul += bodies[i].CarriedHaul; }
        }
        sb.AppendLine("  live den bodies in scene: " + scavengers
            + "   carrying: " + laden + "   gold in transit: " + haul);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].IsDenScavenger) continue;
            sb.AppendLine("    floor " + bodies[i].DenFloorIndex
                + "  haul " + bodies[i].CarriedHaul
                + "  at " + bodies[i].transform.position);
        }

        Debug.Log(sb.ToString());
    }

    [ContextMenu("Print Pause Audit")]
    private void PrintPauseAudit()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[PauseAudit] world " + (PauseGate.Held ? "HELD" : "running")
            + "; timeScale " + Time.timeScale.ToString("0.##"));
        sb.AppendLine("  rule: pause permits DECIDING (selection, navigation, orders,");
        sb.AppendLine("        ledger-only commitments). It forbids ACTING (anything that");
        sb.AppendLine("        reaches an entity on the board or a cell of the tilemap).");
        sb.AppendLine("  -- permitted while held ------------------------------------");
        sb.AppendLine("    selection, deselection, camera, floor change, stair click");
        sb.AppendLine("    orders: patrol, attack-here, post, right-click move");
        sb.AppendLine("    research: browse AND commit (pre-paid, refunded, day-clocked)");
        sb.AppendLine("    trade: browse AND buy (a ledger swap)");
        sb.AppendLine("    openers: trap panel, crypt corpse, caravan wagon, room anchor");
        sb.AppendLine("    ghosts and the hover cost preview, every mode");
        sb.AppendLine("  -- forbidden while held ------------------------------------");
        sb.AppendLine("    mine, dig queue drain, build wall, demolish");
        sb.AppendLine("    place: entrance, spawner, chest, furniture, room anchor,");
        sb.AppendLine("           trap, stairs, core");
        sb.AppendLine("    crypt raise, prisoner release/execute/interrogate");
        sb.AppendLine("    caravan rob/tax/let-pass, bribe, room retype/delete/upgrade");
        sb.AppendLine("    spells, EXCEPT those flagged castableWhilePaused");

        int orders = 0, held = 0;
        var spells = SpellBook.All;
        for (int i = 0; i < spells.Count; i++)
        {
            var s = spells[i];
            if (s == null) continue;
            if (s.castableWhilePaused) { orders++; sb.AppendLine("      pause-legal spell: " + s.displayName); }
            else held++;
        }
        sb.AppendLine("  spells: " + orders + " pause-legal, " + held + " held");
        Debug.Log(sb.ToString());
    }

    [Header("Spell Charges")]
    [Tooltip("Spell id to bank charges of. Blank picks the first working this "
           + "core does NOT hold permanently, which is the interesting case.")]
    [SerializeField] private string chargeSpellId = "";
    [Tooltip("How many castings each Grant adds.")]
    [SerializeField, Min(1)] private int chargeGrantCount = 3;

    /// <summary>Banks castings so the charge substrate is testable before any
    /// scroll content exists (canon 41).</summary>
    [ContextMenu("Grant Spell Charges")]
    private void GrantSpellCharges()
    {
        string id = chargeSpellId;
        if (string.IsNullOrEmpty(id))
        {
            var all = SpellBook.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || SpellBook.HeldPermanently(s)) continue;
                id = s.id;
                break;
            }
        }
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[SpellCharges] Nothing to grant: this core holds every "
                + "authored working permanently. Name a spell id explicitly to "
                + "bank charges of one it already owns.");
            return;
        }
        SpellCharges.Grant(id, chargeGrantCount);
        Debug.Log("[SpellCharges] Banked " + chargeGrantCount + " x '" + id
            + "'. Now holding " + SpellCharges.CountFor(id) + ".");
    }

    [ContextMenu("Clear Spell Charges")]
    private void ClearSpellCharges()
    {
        SpellCharges.Clear();
        Debug.Log("[SpellCharges] Ledger cleared.");
    }

    [ContextMenu("Print Spell Charges")]
    private void PrintSpellCharges()
    {
        var sb = new System.Text.StringBuilder();
        var core = DungeonCore.Instance;
        sb.AppendLine("[SpellCharges] core type "
            + (core != null ? core.DungeonType.ToString() : "none")
            + "; any banked " + SpellCharges.AnyHeld);
        var all = SpellBook.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            int n = SpellCharges.CountFor(s);
            bool perm = SpellBook.HeldPermanently(s);
            bool heard = SpellBook.IsHeardOf(s);
            if (n == 0 && !perm && !heard) continue;
            sb.AppendLine("  " + s.displayName
                + "  [" + s.id + "]"
                + (perm ? "  HELD" : "  charges " + n)
                + (heard ? "  heard-of" : string.Empty)
                + (SpellBook.IsAligned(s) ? "  aligned" : "  off-affinity (reach and hold reduced)")
                + "  radius " + SpellBook.EffectiveRadius(s).ToString("0.##"));
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Print Spell State")]
    private void PrintSpellState()
    {
        var sb = new System.Text.StringBuilder();
        var core = DungeonCore.Instance;
        sb.AppendLine("[Spells] core type " + (core != null ? core.DungeonType.ToString() : "none")
            + "; mana " + (core != null ? core.CurrentMana.ToString("0") : "-")
            + "; any known " + SpellBook.AnySpellKnown);
        var all = SpellBook.All;
        if (all.Count == 0)
            sb.AppendLine("  NO SPELL ASSETS. Run Dungeon Core -> Generate Spell Content "
                + "-- SpellBook loads from Resources/Spells and an empty folder is silent.");
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            sb.AppendLine("  " + (SpellBook.IsAvailable(s) ? "HELD    " : "withheld")
                + "  " + s.displayName
                + "  [" + s.effect + "]"
                + "  affinity " + s.affinity
                + "  key '" + s.requiredUnlockKey + "'"
                + (string.IsNullOrEmpty(s.requiredUnlockKey)
                    ? "" : (UnlockState.IsUnlocked(s.requiredUnlockKey) ? " (set)" : " (unset)"))
                + "  mana " + s.manaCost.ToString("0")
                + "  cd " + s.cooldownSeconds.ToString("0.#") + "s"
                + (SpellBook.IsReady(s) ? " ready"
                    : " in " + SpellBook.CooldownRemaining(s).ToString("0.#") + "s")
                + (s.castableWhilePaused ? "  pause-legal" : ""));
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Test Grant Affinity Working (Silver tier)")]
    private void GrantAffinityT1() => ToggleAffinityTier(".t1");

    [ContextMenu("Test Deepen Affinity Working (Gold tier)")]
    private void GrantAffinityT2() => ToggleAffinityTier(".t2");

    [ContextMenu("Test Deepen Affinity Working (Diamond tier)")]
    private void GrantAffinityT3() => ToggleAffinityTier(".t3");

    /// <summary>Toggles the current core's own working at one tier, resolved from
    /// the spell assets rather than a hardcoded table -- so this cannot drift out
    /// of step with the roster the way a second list would.</summary>
    private void ToggleAffinityTier(string suffix)
    {
        var core = DungeonCore.Instance;
        if (core == null) { Debug.LogWarning("[Spells] No core."); return; }
        var all = SpellBook.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null || s.affinity != core.DungeonType) continue;
            if (string.IsNullOrEmpty(s.deepeningKeyBase)) continue;
            UnlockState.Toggle(s.deepeningKeyBase + suffix);
            Debug.Log("[Spells] " + s.displayName + " " + s.deepeningKeyBase + suffix
                + " -> " + UnlockState.IsUnlocked(s.deepeningKeyBase + suffix)
                + "; now tier " + SpellBook.TierOf(s)
                + ", radius " + SpellBook.EffectiveRadius(s).ToString("0.##")
                + ", duration " + SpellBook.EffectiveDuration(s).ToString("0.##") + "s");
            return;
        }
        Debug.LogWarning("[Spells] No affinity working for " + core.DungeonType
            + ". Run Dungeon Core -> Generate Spell Content.");
    }

    [ContextMenu("Test Toggle Sorcery Trunk (First Spark)")]
    private void ToggleFirstSpark() => UnlockState.Toggle("tech.first_spark");

    [ContextMenu("Test Toggle All Neutral Spells")]
    private void ToggleAllNeutralSpells()
    {
        UnlockState.Toggle("tech.first_spark");
        UnlockState.Toggle("tech.drawn_breath");
        UnlockState.Toggle("tech.call_to_arms");
    }

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

    [ContextMenu("Print Chest Tier Stats")]
    void TestPrintChestTierStats() => ChestRegistry.PrintTierStats();

    [ContextMenu("Print Appeal Ledger")]
    void TestPrintAppealLedger() => DungeonAppealLedger.PrintAppeal();

    [ContextMenu("Print World Events")]
    void TestPrintWorldEvents() => WorldEventDirector.PrintState();

    [Header("Divine Audiences")]
    [Tooltip("Which audience Play Divine Audience forces. Bronze holds none.")]
    [SerializeField] private LevelTier previewAudienceTier = LevelTier.Silver;

    // The writing IS the feature, so it must be readable without four tier-ups:
    // this composes every god at every tier, tokens substituted, exactly as spoken.
    // The lore page shows only what has been HEARD, so "my line is missing" is
    // ambiguous by design: never spoken, or filed somewhere unexpected. This says
    // which, and names any id whose prefix no map entry claims.
    [ContextMenu("Print Wisp Lore Page")]
    void TestPrintWispLore()
    {
        var wisp = WispCompanion.Instance;
        if (wisp == null) { Debug.Log("[WispLore] No WispCompanion in scene."); return; }
        var script = wisp.Script;
        if (script == null) { Debug.Log("[WispLore] WispCompanion has no script asset assigned."); return; }

        WispLoreIndex.Tally(script, wisp, out int heard, out int total);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[WispLore] temperament " + wisp.Personality + "; "
                      + heard + " of " + total + " one-shot sayings gathered.");

        foreach (var group in WispLoreIndex.Gather(script, wisp))
        {
            sb.AppendLine("  == " + group.title + " (" + group.lines.Count + ")");
            foreach (string text in group.lines) sb.AppendLine("    " + text);
        }

        var unmapped = new System.Collections.Generic.List<string>();
        var repeatable = 0;
        foreach (var line in script.lines)
        {
            if (line == null || string.IsNullOrEmpty(line.id)) continue;
            if (!line.once) { repeatable++; continue; }
            string prefix = WispLoreIndex.PrefixOf(line.id);
            if (!WispLoreIndex.IsMapped(line.id) && !unmapped.Contains(prefix)) unmapped.Add(prefix);
        }
        sb.AppendLine("  unmapped prefixes -> Other sayings: "
                      + (unmapped.Count == 0 ? "none" : string.Join(", ", unmapped)));
        sb.AppendLine("  repeatable lines (never gatherable): " + repeatable + ".");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Print Divine Audience Script")]
    void TestPrintDivineAudiences()
    {
        var ui = DivineAudienceUI.Instance;
        if (ui == null) { Debug.Log("[Audience] No DivineAudienceUI in scene - no audience will ever play."); return; }
        var script = ui.Script;
        if (script == null) { Debug.Log("[Audience] DivineAudienceUI has no script asset assigned."); return; }

        string faults = script.Validate();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Audience] " + (faults == null ? "script whole." : "FAULTS:\n  " + faults));
        sb.AppendLine("held so far: " + DivineAudienceLedger.HeldCount + " of "
                      + DivineAudienceScript.AudienceTiers.Length + ".");

        var types = new DungeonType[] { DungeonType.Fire, DungeonType.Water, DungeonType.Air,
                                       DungeonType.Earth, DungeonType.Dark, DungeonType.Light };
        foreach (var type in types)
        {
            var god = script.DeityFor(type);
            sb.AppendLine("");
            sb.AppendLine("== " + type + ": " + (god != null ? god.deityName + ", " + god.epithet : "(no deity row)"));
            foreach (LevelTier tier in DivineAudienceScript.AudienceTiers)
            {
                sb.AppendLine("  -- " + tier + " --");
                foreach (var beat in script.Compose(type, tier))
                    sb.AppendLine("    " + (beat.presence ? "[seen] " : "") + beat.text);
            }
        }
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Play Divine Audience")]
    void TestPlayDivineAudience()
    {
        var ui = DivineAudienceUI.Instance;
        if (ui == null) { Debug.Log("[Audience] No DivineAudienceUI in scene."); return; }
        if (!ui.Play(previewAudienceTier, force: true))
            Debug.Log("[Audience] Refused - no script, no core, or one already playing.");
    }

    [ContextMenu("Print Sightseer Draw")]
    void TestPrintSightseerDraw()
        => Debug.Log($"[Sightseer] novel species alive: {DungeonMonster.NovelSpeciesCount}, "
                   + $"trophy fame: {RoomEffectCensus.TrophyFame:0.0}, "
                   + $"alignment: {(AlignmentSystem.Instance != null ? AlignmentSystem.Instance.Alignment : 0f):0.#}.");

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

    /// <summary>Every wild definition the floors can roll, and whether it has
    /// been discovered. Built for the attribution gate: "the unlock did not
    /// fire" and "the beast never spawned" look identical from the picker, and
    /// a per-floor list separates them in one glance.</summary>
    [ContextMenu("Print Bestiary Sources")]
    void PrintBestiarySources()
    {
        if (FloorManager.Instance == null) { Debug.Log("[Commands] No FloorManager."); return; }

        var rows = new System.Collections.Generic.List<string>();
        int known = 0, total = 0;

        foreach (var floor in FloorManager.Instance.AllFloors)
        {
            var pool = floor?.FeatureGenerator?.WildMonsterPool;
            if (pool == null || pool.Count == 0) continue;
            for (int i = 0; i < pool.Count; i++)
            {
                var def = pool[i];
                if (def == null) continue;
                // The same definition sits in several floors' pools; report it
                // per floor anyway, because minWildFloor makes "which floor can
                // actually roll this" the question being asked.
                bool rolls = floor.FloorIndex >= def.minWildFloor;
                bool got = BestiaryState.Discovered(def.monsterName);
                total++;
                if (got) known++;
                rows.Add($"  F{floor.FloorIndex + 1}  {def.monsterName,-24} "
                       + $"{(got ? "DISCOVERED" : "unknown")}"
                       + (rolls ? "" : $"  (never rolls here: minWildFloor {def.minWildFloor})"));
            }
        }

        Debug.Log(rows.Count == 0
            ? "[Commands] No wild monster pools on any floor."
            : $"[Commands] Bestiary sources ({known}/{total} discovered):\n"
              + string.Join("\n", rows));
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
        // Plan and rasterise separately so the site half of the report can seat
        // against the same chords the game does. Build() is still the wrapper
        // over exactly this pair, so the drawn roads are unchanged.
        var reportRng = new System.Random(seed);
        var reportPlan = RoadNetworkBuilder.Plan(
            reportRng, Vector3Int.zero, radius, entry, roadReportExclusionRadius);
        var result = RoadNetworkBuilder.Rasterise(reportRng, reportPlan);

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
            // The PLAN, not the drawn roads. The builder seats against chords
            // now, so feeding it cells derived from the raster would report a
            // layout the game does not produce -- which is exactly the failure
            // the old comment here was guarding against, one layer down.
            siteResult = AncientSiteBuilder.Build(
                new System.Random(seed), Vector3Int.zero, radius, siteEntry,
                roadReportExclusionRadius, reportPlan,
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

    /// <summary>Runs the REAL DenTunnelBuilder across many seeds and prints what
    /// it produces, without entering play mode or generating a floor. Built
    /// before the generator that will call it, because the shape it implements
    /// was chosen by measurement (Tools/sim_den_tunnels.py) and a C# builder
    /// that disagrees with the sim it was written from has a bug in it. The
    /// figures to expect, at the shipped profile: floor index 1 about 2.1
    /// chamber links, 0.9 dead ends, no link at all on 5.8 per cent of seeds,
    /// 508 cells; floor index 2 about 3.3 links, 0.7 dead ends, 0.9 per cent,
    /// 1107 cells.</summary>
    /// <summary>Does a run actually BREACH the chamber it links, or merely touch
    /// it? The pathfinder walks 4-NEIGHBOURS only and Bresenham takes diagonal
    /// steps, so "the tunnel reaches the chamber" and "something can walk from
    /// one into the other" are different claims, and only the second one matters.
    /// Flood-fills the union and checks the second. Measured at 831/831 and
    /// 959/959 when this shipped; anything less is a regression, and the three
    /// things that carry it are all quietly undoable by a tidy-up: the centreline
    /// runs to the chamber CENTRE rather than its edge, consecutive dilations
    /// OVERLAP so a 2-wide tip stays 4-connected across a diagonal step, and
    /// handing the overlap back to the chamber severs nothing.</summary>
    /// <summary>Every den in the run, with its hoard, tier and raid timing.
    /// Built because a ledger earning nothing looks exactly like a ledger
    /// earning slowly: the stolen/dug column separates them at a glance, and the
    /// share it implies is the number to check against
    /// Tools/sim_den_growth.py when a den feels wrong in play.</summary>
    [ContextMenu("Print Den Ledger")]
    void PrintDenLedger()
    {
        if (DenController.Instance == null) { Debug.Log("[Commands] No DenController in the scene."); return; }

        var sb = new System.Text.StringBuilder();
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        sb.AppendLine($"[Commands] Den ledger -- day {day}");
        sb.AppendLine("floor  kind        tribe    tier  hoard    next tier  stolen   earned   rem  raids  tgt%  dug    left   tunnel      find  pop  out  dig  work  lost  state");

        bool any = false;
        foreach (var den in DenController.Instance.AllDens)
        {
            any = true;
            int tier = DenController.Instance.TierOf(den.floorIndex);
            float next = tier < DenController.MaxTier
                ? DenController.ThresholdFor(tier + 1) : 0f;
            string state = den.cleared ? "CLEARED"
                : (day - den.awakenedDay < 5 ? $"grace ({5 - (day - den.awakenedDay)}d)" : "active");
            // Geometry beside the ledger, because coupled income means the two
            // are the same statement and a disagreement between them is the
            // fault this pass exists to make visible. `left` is raw unopened
            // reserve: a den can stall with it above zero when the player
            // claimed the remaining cells first, which is the race working.
            var denFloor = FloorManager.Instance != null
                ? FloorManager.Instance.GetFloor(den.floorIndex) : null;
            var denFeatures = denFloor != null ? denFloor.FeatureGenerator : null;
            string left = denFeatures != null && denFeatures.HasDenCavity
                ? denFeatures.DenCavityGrowthHeadroom.ToString() : "-";
            // The den's tribe is read off the AUTHORED definition rather than off
            // a live body, so it still prints for a den standing empty at dawn --
            // and a profile whose scavengerDefinition was never assigned shows as
            // "-", which is the fault that would otherwise look exactly like a
            // den that simply has not spawned yet.
            var denEntry = denFeatures != null ? denFeatures.DenProfileEntry : null;
            var denDef = denEntry != null ? denEntry.scavengerDefinition : null;
            string tribe = denDef != null ? denDef.tribe.ToString() : "-";

            // The DIG, beside the hole, because they are two budgets on one
            // den and the whole reason the tunnel is additive is that they
            // must not be read as one. A trailing * means the diggings have
            // stopped -- cap spent, or every remains on the floor taken --
            // which is otherwise indistinguishable from a den digging slowly.
            int digCap = denEntry != null ? denEntry.exploratoryCellCap : 0;
            string tunnel = digCap <= 0 ? "-"
                : (denFeatures.DenExploratoryCellCount + "/" + digCap
                   + (den.digStopped ? "*" : ""));

            // Bodies, and what they are doing. work is the count holding a work
            // site: zero until stage 2 sets one, and the line that will say so.
            int pop = 0, work = 0;
            if (denFloor != null && denFloor.Entities != null)
            {
                var bodies = denFloor.Entities.GetAll<DungeonMonster>();
                for (int b = 0; b < bodies.Count; b++)
                {
                    if (bodies[b] == null || bodies[b].DenFloorIndex != den.floorIndex) continue;
                    pop++;
                    if (bodies[b].HasDenWorkSite) work++;
                }
            }

            sb.AppendLine($"{den.floorIndex,-6} {(DenKind)den.kind,-11} {tribe,-8} {tier,-5} "
                        + $"{den.hoard,-8:F0} {(next > 0f ? next.ToString("F0") : "max"),-10} "
                        + $"{den.stolenHoard,-8:F0} {den.stolenTotal,-8:F0} {den.remainsTaken,-4} {den.raidsLaunched,-6} {DenController.Instance.TargetStealShare(den.floorIndex) * 100f,-5:F0} {den.cellsDug,-6} {left,-6} {tunnel,-11} {den.digFinds,-5} "
                        + $"{pop,-4} {DenController.Instance.ScavengerBudget(den.floorIndex),-4} {DenController.Instance.DiggerBudget(den.floorIndex),-4} {work,-5} {den.deathsNotByDungeon,-5} {state}");

            // THE HOARD INVARIANT, and the one line that catches stage 2b's
            // characteristic failure. An excavator's hoard is the geometry's
            // own account of itself -- cells opened times spoil, plus a lump
            // per remains taken -- and NOTHING else may pay into it. A stolen
            // coin credited there, or a raid cut, uncouples the ledger from
            // the hole in silence: the tier climbs, the pile grows, and Den
            // Cavity Report goes on passing because its assertion is a STATIC
            // bound that never looks at a live den. Asserted rather than
            // assumed, because canon 42 twice records a constant that agreed
            // with its sim and was wrong anyway -- a check that compares
            // values and not liveness is not a check.
            //
            // Relative epsilon: this ledger is a float and accumulates over
            // forty-odd partial days, which canon already records putting a
            // den on a hoard of 1399.9999999999998.
            if ((DenKind)den.kind == DenKind.Excavator && !den.cleared)
            {
                float expected = den.cellsDug * DenController.Instance.SpoilPerCell
                               + den.remainsTaken * DenController.Instance.RemainsLump;
                if (Mathf.Abs(den.hoard - expected) > Mathf.Max(0.5f, expected * 0.0001f))
                    sb.AppendLine($"       !! hoard {den.hoard:F1} against {expected:F1} expected "
                                + $"({den.cellsDug} cells x {DenController.Instance.SpoilPerCell:F1} spoil "
                                + $"+ {den.remainsTaken} remains x {DenController.Instance.RemainsLump:F0}) "
                                + "-- something outside the dig is paying into hoard. Theft and "
                                + "raid cuts belong in stolenHoard, which tier cannot see.");
            }
        }

        if (!any)
            sb.AppendLine("  (no dens registered -- only floors CREATED since the profile "
                        + "was assigned carry one)");
        Debug.Log(sb.ToString());
    }

    /// <summary>Who is hostile to whom, off the LIVE rule, plus what is actually
    /// standing on each floor.
    ///
    /// BUILT BECAUSE CROSS-TRIBE HOSTILITY IS INVISIBLE UNTIL TWO THINGS HAPPEN
    /// TO BE NEAR EACH OTHER, on a floor nobody may be watching. A predicate
    /// reading None on both sides looks exactly like peace, and a tribe left
    /// unset on one definition is a silent no-op -- the den simply never fights
    /// anything, for ever, and no other surface would show it.
    ///
    /// Drives DungeonMonster.AreHostile directly rather than restating the rule.
    /// A matrix that reimplemented the test would confirm itself and nothing
    /// else, which is the shape this project has already been bitten by.</summary>
    [ContextMenu("Print Tribe Matrix")]
    void PrintTribeMatrix()
    {
        var sb = new System.Text.StringBuilder();

        // THE ALLEGIANCE MATRIX FIRST, because it is now the outer rule and the
        // tribe matrix is one cell of it. Driven off the same AreHostile the
        // combat layer calls, so a cell that reads peace here is a cell no
        // monster will fight in. Tribes are held at None so this table shows the
        // allegiance axis alone.
        //
        // STATE-DEPENDENT, DELIBERATELY: the Dungeon/Faction cell reads live
        // standing, so running this after a robbery shows a different table than
        // running it before. That is the readout doing its job.
        sb.AppendLine("[Commands] Allegiance matrix -- off DungeonMonster.AreHostile (canon 44)");
        var sides = (MonsterAllegiance[])System.Enum.GetValues(typeof(MonsterAllegiance));
        sb.Append("  ".PadRight(14));
        for (int c = 0; c < sides.Length; c++) sb.Append(sides[c].ToString().PadRight(9));
        sb.AppendLine();
        for (int r = 0; r < sides.Length; r++)
        {
            sb.Append(("  " + sides[r]).PadRight(14));
            for (int c = 0; c < sides.Length; c++)
                sb.Append((DungeonMonster.AreHostile(
                               sides[r], (int)MonsterTribe.None, FactionId.Dwarves,
                               sides[c], (int)MonsterTribe.None, FactionId.Dwarves)
                           ? "FIGHT" : "peace").PadRight(9));
            sb.AppendLine();
        }
        var fsys = FactionSystem.Instance;
        sb.AppendLine("  Deep Holds tier now: "
            + (fsys != null ? fsys.Tier(FactionId.Dwarves).ToString() : "no FactionSystem")
            + "   at war: " + FactionSystem.AtWarWithDungeon(FactionId.Dwarves));
        sb.AppendLine();

        sb.AppendLine("[Commands] Tribe matrix -- WILD against WILD, off DungeonMonster.AreHostile");
        sb.AppendLine("  (the dungeon's own fight every wild body regardless: that is allegiance, not tribe)");

        var tribes = (MonsterTribe[])System.Enum.GetValues(typeof(MonsterTribe));
        sb.Append("  ".PadRight(14));
        for (int c = 0; c < tribes.Length; c++) sb.Append(tribes[c].ToString().PadRight(9));
        sb.AppendLine();
        for (int r = 0; r < tribes.Length; r++)
        {
            sb.Append(("  " + tribes[r]).PadRight(14));
            for (int c = 0; c < tribes.Length; c++)
                sb.Append((DungeonMonster.AreHostile(
                               MonsterAllegiance.Wild, (int)tribes[r], FactionId.Dwarves,
                               MonsterAllegiance.Wild, (int)tribes[c], FactionId.Dwarves)
                           ? "FIGHT" : "peace").PadRight(9));
            sb.AppendLine();
        }

        // Every definition that can actually reach a floor: the shared chamber
        // pool and each den's authored population. A tribe is only as good as
        // what carries it, so the roster prints beside the rule.
        sb.AppendLine();
        sb.AppendLine("Definitions in play      tribe     wild count");
        var seen = new System.Collections.Generic.HashSet<MonsterDefinition>();
        if (FloorManager.Instance != null)
        {
            foreach (var f in FloorManager.Instance.AllFloors)
            {
                var fg = f != null ? f.FeatureGenerator : null;
                if (fg == null) continue;
                if (fg.WildMonsterPool != null)
                    foreach (var d in fg.WildMonsterPool)
                        if (d != null) seen.Add(d);
                var pe = fg.DenProfileEntry;
                if (pe != null && pe.scavengerDefinition != null) seen.Add(pe.scavengerDefinition);
            }
        }
        if (seen.Count == 0)
            sb.AppendLine("  (no floors created yet -- nothing to list)");
        foreach (var d in seen)
            sb.AppendLine($"  {d.monsterName,-23}{d.tribe,-10}{d.wildCountMin}-{d.wildCountMax}");

        // Live bodies, so a matrix that says FIGHT can be checked against two
        // things that are genuinely on the same floor.
        sb.AppendLine();
        sb.AppendLine("Live NON-DUNGEON bodies by floor, allegiance and tribe");
        bool anyBody = false;
        if (FloorManager.Instance != null)
        {
            foreach (var f in FloorManager.Instance.AllFloors)
            {
                if (f == null || f.Entities == null) continue;
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                var bodies = f.Entities.GetAll<DungeonMonster>();
                for (int b = 0; b < bodies.Count; b++)
                {
                    var m = bodies[b];
                    // The player's own are the only ones skipped. A faction body
                    // is not wild and the old filter dropped it, which would have
                    // made the one thing this readout exists to check invisible.
                    if (m == null || m.ServesDungeon) continue;
                    string key = m.Allegiance == MonsterAllegiance.Faction
                        ? m.Allegiance + "/" + m.Faction
                        : m.Allegiance + "/" + m.Tribe;
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                    anyBody = true;
                }
                if (counts.Count == 0) continue;
                sb.Append($"  floor {f.FloorIndex}: ");
                foreach (var kv in counts) sb.Append($"{kv.Key} x{kv.Value}   ");
                sb.AppendLine();
            }
        }
        if (!anyBody) sb.AppendLine("  (none alive)");

        // The number that answers whether the tribe rule is eating the occupier
        // theft curve. Read it beside the ledger's lost column.
        sb.AppendLine();
        sb.AppendLine($"Cross-tribe target acquisitions this session: {DungeonMonster.CrossTribeEngagements}");
        sb.AppendLine("  (acquisitions that exist ONLY because the tribes differ -- both sides wild)");
        Debug.Log(sb.ToString());
    }

    /// <summary>Zero the cross-tribe counter, so a run can be measured from a
    /// known point rather than from whenever the domain last reloaded. Statics
    /// survive entering play mode when reload is disabled, which is exactly the
    /// setup in which a stale total reads as a fresh one.</summary>
    /// <summary>The diggings, leg by leg.
    ///
    /// BUILT BECAUSE A STALLED DIG AND A SLOW ONE LOOK IDENTICAL ON SCREEN, and
    /// this arc has already paid for that lesson twice -- once when an
    /// excavator capped at tier 3 in silence, and once when den tunnels shipped
    /// absent from every diagnostic surface and read as a generator that had
    /// done nothing. A leg that is boxed in by the player's own claim is the
    /// race WORKING; a leg that never started is a fault; the two are one line
    /// apart here and indistinguishable anywhere else.</summary>
    [ContextMenu("Print Den Dig")]
    void PrintDenDig()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Commands] Den diggings");

        if (FloorManager.Instance == null)
        {
            Debug.Log("[Commands] No FloorManager in the scene.");
            return;
        }

        bool any = false;
        foreach (var f in FloorManager.Instance.AllFloors)
        {
            var fg = f != null ? f.FeatureGenerator : null;
            if (fg == null) continue;
            var pe = fg.DenProfileEntry;
            if (pe == null || pe.exploratoryCellCap <= 0) continue;
            any = true;

            var data = fg.FeatureData;
            int legs = 0, generated = 0;
            if (data != null && data.denTunnels != null)
                foreach (var t in data.denTunnels)
                {
                    if (t == null) continue;
                    if (t.exploratory) legs++; else generated++;
                }

            sb.AppendLine($"  floor {f.FloorIndex}: {generated} generated runs, {legs} legs, "
                        + $"{fg.DenExploratoryCellCount}/{pe.exploratoryCellCap} cells cut "
                        + $"at section {pe.exploratoryWidth}, budget x{pe.exploratoryBudget:F1}");
            sb.AppendLine($"    reveal: {fg.RevealedDenTunnelSegmentCount} of "
                        + $"{fg.DenTunnelSegmentCount} stretches");

            var brc = BuriedRemainsController.Instance;
            int onFloor = brc != null ? brc.SiteCountFor(f) : -1;
            int untaken = brc != null ? brc.UntakenRemainsOn(f).Count : -1;
            sb.AppendLine($"    remains: {fg.DenTakenRemainsCount} taken of "
                        + $"{(onFloor < 0 ? "?" : onFloor.ToString())} on the floor, "
                        + $"{(untaken < 0 ? "?" : untaken.ToString())} still buried, "
                        + $"{fg.DenRemainsMarkerCount} markers standing");
            if (onFloor == 0)
                sb.AppendLine("    !! this floor has NO buried remains at all, so the "
                            + "contested-discovery beat cannot fire here. Expected on "
                            + "some seeds -- GetBuriedSites takes only Stone and Granite.");
            if (!fg.DenRemainsMarkerPrefabAssigned)
                sb.AppendLine("    !! no remains marker prefab assigned on the profile, so "
                            + "a robbed remains leaves no visible hole. The wisp still "
                            + "speaks; the lasting record does not exist.");

            var ledger = DenController.Instance;
            if (ledger != null)
                foreach (var den in ledger.AllDens)
                {
                    if (den.floorIndex != f.FloorIndex) continue;
                    sb.AppendLine($"    ledger: heading {den.digHeadingDegrees:F0} deg, "
                                + $"carry {den.tunnelCarry:F2}, finds {den.digFinds}, "
                                + $"{(den.digStopped ? "STOPPED" : "digging")}");
                }
        }

        if (!any)
            sb.AppendLine("  (no floor carries a dig -- only an Excavator with a non-zero "
                        + "exploratoryCellCap does)");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Reset Cross-Tribe Counter")]
    void ResetCrossTribeCounter()
    {
        DungeonMonster.ResetCrossTribeEngagements();
        Debug.Log("[Commands] Cross-tribe acquisition counter reset to 0.");
    }

    [ContextMenu("Den Tunnel Breach Check")]
    void DenTunnelBreachCheck()
    {
        if (denTunnelProfile == null) { Debug.Log("[Commands] Assign Den Tunnel Profile first."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Commands] Den tunnel breach check");
        sb.AppendLine("floor  links  breached  touchOnly  apart  verdict");
        bool allGood = true;

        foreach (var entry in denTunnelProfile.Floors)
        {
            if (entry == null) continue;
            int radius = FloorRadiusFor(entry.floorIndex);
            var centre = new Vector3Int(0, 0, 0);
            int links = 0, breached = 0, touchOnly = 0, apart = 0;

            for (int s = 0; s < 500; s++)
            {
                var rng = new System.Random(s * 7919 + entry.floorIndex);

                // Chambers as discs at the authored CA scale (box 8-14 -> r 4-7).
                var centres = new System.Collections.Generic.List<Vector3Int>();
                var ids = new System.Collections.Generic.List<int>();
                var blobs = new System.Collections.Generic.List<System.Collections.Generic.HashSet<Vector3Int>>();
                int n = rng.Next(3, 7);
                for (int i = 0; i < n; i++)
                {
                    int dx = rng.Next(-radius + 10, radius - 10);
                    int dy = rng.Next(-radius + 10, radius - 10);
                    if (dx * dx + dy * dy > (radius - 10) * (radius - 10)) continue;
                    var cc = new Vector3Int(dx, dy, 0);
                    centres.Add(cc); ids.Add(centres.Count - 1);
                    blobs.Add(DiscCells(cc, rng.Next(4, 8)));
                }
                if (centres.Count == 0) continue;

                var plan = DenTunnelBuilder.Plan(rng, centre, radius, entry, 4,
                                                 centres, ids, centre, 6);
                if (!plan.valid) continue;

                var tunnels = DenTunnelBuilder.Rasterise(rng, plan, radius - 10, 16, 3f);
                foreach (var t in tunnels)
                {
                    if (t.chamberId < 0) continue;
                    links++;
                    var blob = blobs[t.chamberId];

                    // Exactly what RebuildDenTunnelCells does: the chamber owns
                    // its cells, so the run hands them back.
                    var tunnelCells = new System.Collections.Generic.HashSet<Vector3Int>();
                    foreach (var cc in DenTunnelBuilder.Cells(t))
                        if (!blob.Contains(cc)) tunnelCells.Add(cc);

                    bool touches = false;
                    foreach (var cc in tunnelCells)
                    {
                        for (int d = 0; d < Orth4Dirs.Length && !touches; d++)
                            if (blob.Contains(cc + Orth4Dirs[d])) touches = true;
                        if (touches) break;
                    }

                    var union = new System.Collections.Generic.HashSet<Vector3Int>(tunnelCells);
                    foreach (var cc in blob) union.Add(cc);
                    var seen = new System.Collections.Generic.HashSet<Vector3Int>();
                    var q = new System.Collections.Generic.Queue<Vector3Int>();
                    var start = t.polyline[0].ToVector3Int();
                    if (!union.Contains(start))
                        foreach (var cc in tunnelCells) { start = cc; break; }
                    q.Enqueue(start); seen.Add(start);
                    bool reached = false;
                    while (q.Count > 0)
                    {
                        var cc = q.Dequeue();
                        if (blob.Contains(cc)) { reached = true; break; }
                        for (int d = 0; d < Orth4Dirs.Length; d++)
                        {
                            var p = cc + Orth4Dirs[d];
                            if (union.Contains(p) && seen.Add(p)) q.Enqueue(p);
                        }
                    }

                    if (reached) breached++;
                    else if (touches) touchOnly++;
                    else apart++;
                }
            }

            bool ok = links > 0 && touchOnly == 0 && apart == 0;
            allGood = allGood && ok;
            sb.AppendLine($"{entry.floorIndex,-6} {links,-6} {breached,-9} {touchOnly,-10} {apart,-6} "
                        + (ok ? "BREACHES" : "REGRESSION"));
        }

        sb.AppendLine(allGood
            ? "All links are 4-connected into their chamber."
            : "A run now only TOUCHES its chamber. Nothing can walk in. See canon 42.");
        Debug.Log(sb.ToString());
    }

    private static readonly Vector3Int[] Orth4Dirs =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
    };

    private static System.Collections.Generic.HashSet<Vector3Int> DiscCells(Vector3Int c, int r)
    {
        var s = new System.Collections.Generic.HashSet<Vector3Int>();
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                if (x * x + y * y <= r * r) s.Add(new Vector3Int(c.x + x, c.y + y, 0));
        return s;
    }

    [ContextMenu("Den Tunnel Report (headless)")]
    void DenTunnelReport()
    {
        if (denTunnelProfile == null)
        {
            Debug.Log("[Commands] Assign Den Tunnel Profile first.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Commands] Den tunnel plan report -- {denReportSeeds} seeds per floor");
        sb.AppendLine("floor  radius  links   deadends  nolink%  short%  cells");

        foreach (var entry in denTunnelProfile.Floors)
        {
            if (entry == null) continue;
            int radius = FloorRadiusFor(entry.floorIndex);
            var centre = new Vector3Int(0, 0, 0);

            double links = 0, dead = 0, cells = 0;
            int nolink = 0, shortNet = 0, ok = 0;

            for (int s = 0; s < denReportSeeds; s++)
            {
                var rng = new System.Random(s * 7919 + entry.floorIndex);
                var chambers = SampleChamberCentres(rng, radius, centre);
                var ids = new System.Collections.Generic.List<int>();
                for (int i = 0; i < chambers.Count; i++) ids.Add(i);

                // The landing is wherever the player put the stair, which
                // generation cannot know; sampling it in the inner band is the
                // pessimistic model, because that is where a descending player is.
                int inner = Mathf.RoundToInt(radius * entry.bandInner);
                var landing = centre;
                for (int t = 0; t < 64; t++)
                {
                    int lx = rng.Next(-inner, inner + 1);
                    int ly = rng.Next(-inner, inner + 1);
                    if (lx * lx + ly * ly <= inner * inner)
                    { landing = new Vector3Int(lx, ly, 0); break; }
                }

                var plan = DenTunnelBuilder.Plan(rng, centre, radius, entry,
                                                 4, chambers, ids, landing, 6);
                if (!plan.valid) continue;
                ok++;
                links += plan.ChamberLinks;
                dead += plan.DeadEnds;
                if (plan.ChamberLinks == 0) nolink++;
                if (plan.runs.Count < entry.runCount) shortNet++;
                foreach (var r in plan.runs)
                {
                    float dx = r.b.x - r.a.x, dy = r.b.y - r.a.y;
                    cells += Mathf.Sqrt(dx * dx + dy * dy) * (r.width + r.tipWidth) / 2f;
                }
            }

            if (ok == 0) { sb.AppendLine($"{entry.floorIndex,-6} {radius,-7} NO VALID PLANS"); continue; }
            sb.AppendLine($"{entry.floorIndex,-6} {radius,-7} {links / ok,-7:F2} {dead / ok,-9:F2} "
                        + $"{100.0 * nolink / ok,-8:F1} {100.0 * shortNet / ok,-7:F1} {cells / ok,-7:F0}");
        }

        sb.AppendLine("short% is networks with fewer runs than the profile authored; "
                    + "it must read 0.0, and read 23 before the dead-end retry was added.");
        Debug.Log(sb.ToString());
    }

    /// <summary>GenerateChambers' placement, mirrored so the report can run with
    /// no floor in the scene. Kept beside the report rather than shared: this is
    /// a MODEL of the generator for measurement, and the day the generator moves
    /// on, a shared helper would quietly make the report agree with the wrong
    /// thing.</summary>
    private static System.Collections.Generic.List<Vector3Int> SampleChamberCentres(
        System.Random rng, int radius, Vector3Int centre)
    {
        const int MinChambers = 3, MaxChambers = 6, RefRadius = 150, Ceiling = 30;
        const int Spacing = 10, RimMargin = 10, Exclusion = 4;

        float scale = Mathf.Max(1f, radius / (float)RefRadius);
        int rolled = rng.Next(MinChambers, MaxChambers + 1);
        int desired = Mathf.Clamp(Mathf.RoundToInt(rolled * scale), 1, Ceiling);
        int disc = Mathf.Max(Exclusion + 1, radius - RimMargin);

        var centres = new System.Collections.Generic.List<Vector3Int>();
        int attempts = 0;
        while (centres.Count < desired && attempts < desired * 6)
        {
            attempts++;
            int dx = rng.Next(-disc, disc + 1);
            int dy = rng.Next(-disc, disc + 1);
            int d2 = dx * dx + dy * dy;
            if (d2 > disc * disc || d2 < Exclusion * Exclusion) continue;
            bool clash = false;
            foreach (var c in centres)
            {
                int ex = c.x - centre.x - dx, ey = c.y - centre.y - dy;
                if (ex * ex + ey * ey < Spacing * Spacing) { clash = true; break; }
            }
            if (clash) continue;
            centres.Add(new Vector3Int(centre.x + dx, centre.y + dy, 0));
        }
        return centres;
    }

    /// <summary>Radius per floor, from the progression table's shipped figures.</summary>
    /// <summary>
    /// The cavity's standing regression test, and the companion to Den Tunnel
    /// Breach Check. Four things, each of which has a way of going quietly wrong:
    ///
    ///   SIZE      does the carve land in its authored band, and how many cells
    ///             did the clamp have to correct to get it there? Correction is
    ///             the quality signal -- the clamp always hits the band by
    ///             construction, so the final count on its own proves nothing.
    ///   SPAN      entry 19's rule, which is the one the sizes actually rest on
    ///             after its cell-count comparator was measured and corrected: a
    ///             cave chamber is a median of 49 cells and never more than 133,
    ///             not the 100-200 that entry asserted, so the budget to check
    ///             against is a span near twice the chamber box size -- 16 to 28.
    ///   SEATING   is every run 4-connected to the hole after the cavity takes
    ///             its cells back? A den whose runs are severed from it is a den
    ///             with no way out, and nothing on screen would say so.
    ///   RESERVE   for an excavator, is there room left to grow, and does the
    ///             tier-1 sub-blob still contain the anchor every run starts at?
    ///
    /// Headless: it plans and carves against the shipped profile rather than
    /// reading a live floor, so it runs without generating anything and answers
    /// in seconds. Tools/sim_den_cavity.py is the same measurement at 2000 seeds;
    /// this is the in-editor spot check that catches a profile edit.
    /// </summary>
    [ContextMenu("Den Cavity Report")]
    void DenCavityReport()
    {
        if (denTunnelProfile == null) { Debug.Log("[Commands] Assign Den Tunnel Profile first."); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Commands] Den cavity report  (span budget 16-28, entry 19)");
        sb.AppendLine("floor  kind        open/reserve   band      span  corrected  severed  verdict");
        bool allGood = true;

        foreach (var entry in denTunnelProfile.Floors)
        {
            if (entry == null) continue;
            int radius = FloorRadiusFor(entry.floorIndex);
            var centre = new Vector3Int(0, 0, 0);

            int seeds = 0, inBand = 0, severed = 0, runs = 0, noAnchor = 0;
            int spanSum = 0, spanMax = 0, openSum = 0, reserveSum = 0, correctedSum = 0;
            int tier1MissingAnchor = 0;

            for (int s = 0; s < 200; s++)
            {
                var rng = new System.Random(s * 7919 + entry.floorIndex);

                var centres = new System.Collections.Generic.List<Vector3Int>();
                var ids = new System.Collections.Generic.List<int>();
                int n = rng.Next(3, 7);
                for (int i = 0; i < n; i++)
                {
                    int dx = rng.Next(-radius + 10, radius - 10);
                    int dy = rng.Next(-radius + 10, radius - 10);
                    if (dx * dx + dy * dy > (radius - 10) * (radius - 10)) continue;
                    centres.Add(new Vector3Int(dx, dy, 0));
                    ids.Add(centres.Count - 1);
                }
                if (centres.Count == 0) continue;

                var plan = DenTunnelBuilder.Plan(rng, centre, radius, entry, 4,
                                                 centres, ids, centre, 6);
                if (!plan.valid) { noAnchor++; continue; }

                var carve = CarveCavityForReport(rng, plan.den, entry, centre, radius);
                if (carve == null) continue;
                seeds++;

                openSum += carve.open.Count;
                reserveSum += carve.reserve.Count;
                correctedSum += carve.corrected;
                if (carve.reserve.Count >= entry.cavityMinCells
                    && carve.reserve.Count <= entry.cavityMaxCells) inBand++;
                if (!carve.open.Contains(plan.den)) tier1MissingAnchor++;

                int span = SpanOf(carve.reserve);
                spanSum += span;
                if (span > spanMax) spanMax = span;

                var tunnels = DenTunnelBuilder.Rasterise(rng, plan, radius - 10, 16, 3f);
                foreach (var t in tunnels)
                {
                    runs++;
                    var outside = new System.Collections.Generic.HashSet<Vector3Int>();
                    foreach (var cc in DenTunnelBuilder.Cells(t))
                        if (!carve.open.Contains(cc)) outside.Add(cc);
                    if (outside.Count == 0) continue;   // wholly inside the hole
                    if (!ReachesSet(outside, carve.open)) severed++;
                }
            }

            if (seeds == 0)
            {
                sb.AppendLine($"  {entry.floorIndex}    {entry.kind,-10}  NO VALID SEED ({noAnchor} anchor failures)");
                allGood = false;
                continue;
            }

            bool ok = severed == 0 && inBand == seeds && tier1MissingAnchor == 0;
            allGood = allGood && ok;
            sb.AppendLine(
                $"  {entry.floorIndex}    {entry.kind,-10}  {openSum / seeds,4}/{reserveSum / seeds,-4}     "
              + $"{100 * inBand / seeds,3}%      {spanSum / seeds,2} (max {spanMax})   "
              + $"{correctedSum / seeds,4}      {severed,3}/{runs,-4}  {(ok ? "OK" : "FAIL")}");

            if (tier1MissingAnchor > 0)
                sb.AppendLine($"         !! tier-1 carve missed the anchor on {tier1MissingAnchor} seeds -- "
                            + "the runs all start there, so those dens would be sealed.");
            // No longer an accepted exception. At the old 600 reserve about 4 per
            // cent of excavator seeds spanned over budget and canon carried that
            // as the cap doing its job; at 400 the worst of 1500 seeds is 27, so
            // anything over 28 now means something has drifted rather than that
            // a known overshoot has recurred.
            if (spanMax > 28)
                sb.AppendLine($"         !! span reaches {spanMax} against entry 19's budget of 28. "
                            + "Both bands were sized to sit inside it, so this is a regression "
                            + "rather than a known exception -- re-run Tools/sim_den_cavity.py.");
        }

        sb.AppendLine(allGood ? "VERDICT: OK" : "VERDICT: FAIL -- see rows above.");

        // THE COUPLING ASSERTION (canon 42, fork 4b), and the fault it exists
        // to catch already happened once. Growth pays the ledger on cells
        // ACTUALLY OPENED, so an excavator's lifetime income is bounded by
        // geometry: (reserve - tier 1) cells times spoilPerCell. At the numbers
        // the ledger originally shipped with that bound was 350 against a
        // tier-5 threshold of 1400, and every excavator capped at tier 3
        // without a single thing on screen saying so.
        //
        // Checked against the SMALLEST reserve, never the largest. Sizing on
        // the widest hole leaves narrow seeds permanently short of the top
        // tier, which is seed-dependent and therefore worse than a flat
        // failure -- this entry's own "a maximum is the least stable statistic"
        // rule pointing the other way for once.
        AppendCouplingAssertion(sb);
        Debug.Log(sb.ToString());
    }

    /// <summary>The DigCellsPerDay / spoilPerCell keep-in-sync check. Split out
    /// so the report above stays readable, and so the reason it exists sits
    /// next to the arithmetic rather than three screens above it.</summary>
    private void AppendCouplingAssertion(System.Text.StringBuilder sb)
    {
        var den = DenController.Instance;
        if (den == null)
        {
            sb.AppendLine("Coupling assertion SKIPPED: no DenController in the scene, "
                        + "so spoilPerCell cannot be read. Open the dungeon scene "
                        + "and re-run -- a magic-number fallback here would be "
                        + "exactly the ambiguous default this project bans.");
            return;
        }

        float threshold = DenController.ThresholdFor(DenController.MaxTier);
        foreach (var entry in denTunnelProfile.Floors)
        {
            if (entry == null || entry.kind != DenKind.Excavator) continue;
            int diggable = entry.cavityMinCells - entry.cavityTier1Cells;
            float ceiling = diggable * den.SpoilPerCell;
            bool ok = ceiling >= threshold;
            sb.AppendLine($"Coupling floor {entry.floorIndex}: {diggable} diggable cells "
                        + $"x {den.SpoilPerCell:F1} spoil = {ceiling:F0} lifetime hoard "
                        + $"against a tier-{DenController.MaxTier} threshold of {threshold:F0} "
                        + $"-- {(ok ? "OK" : "FAIL")}");
            if (!ok)
                sb.AppendLine("         !! this excavator can NEVER reach the top tier. "
                            + "Raise spoilPerCell or re-run Tools/sim_den_cavity_growth.py; "
                            + "do not widen the reserve, whose span is already at 27 of a "
                            + "budget of 28.");
        }
    }

    private class CavityCarve
    {
        public System.Collections.Generic.HashSet<Vector3Int> open;
        public System.Collections.Generic.List<Vector3Int> reserve;
        public int corrected;
    }

    /// <summary>Mirrors TerrainFeatureGenerator.CarveDenCavity closely enough to
    /// measure it, without a live floor. It is a MIRROR and not the thing itself,
    /// which is the honest limitation of a headless report: the generator filters
    /// against the floor radius, the core exclusion and reservedCoreCells, and
    /// none of those exist here. Numbers from this are therefore an upper bound
    /// on size and a lower bound on trouble; Tools/sim_den_cavity.py carries the
    /// same caveat and says so.</summary>
    private static CavityCarve CarveCavityForReport(
        System.Random rng, Vector3Int den, DenTunnelFloorEntry entry,
        Vector3Int floorCentre, int floorRadius)
    {
        int box = Mathf.Max(8, entry.cavityBox);
        int lo = Mathf.Max(16, entry.cavityMinCells);
        int hi = Mathf.Max(lo, entry.cavityMaxCells);

        System.Collections.Generic.List<Vector3Int> raw = null;
        for (int a = 0; a < 8 && raw == null; a++)
        {
            var r = CaBlob(rng, den, box);
            if (r.Count > 0) raw = r;
        }
        if (raw == null) return null;

        int before = raw.Count;
        var set = new System.Collections.Generic.HashSet<Vector3Int>(raw);
        int safety = 0;
        var cand = new System.Collections.Generic.List<Vector3Int>();
        while (set.Count < lo && safety++ < 4000)
        {
            cand.Clear();
            foreach (var c in set)
                for (int d = 0; d < Orth4Dirs.Length; d++)
                    if (!set.Contains(c + Orth4Dirs[d])) cand.Add(c + Orth4Dirs[d]);
            if (cand.Count == 0) break;
            set.Add(cand[rng.Next(cand.Count)]);
        }
        while (set.Count > hi)
        {
            Vector3Int far = den; int maxSq = -1;
            foreach (var c in set)
            {
                if (c == den) continue;
                int sq = (c.x - den.x) * (c.x - den.x) + (c.y - den.y) * (c.y - den.y);
                if (sq > maxSq) { maxSq = sq; far = c; }
            }
            if (far == den) break;
            set.Remove(far);
        }

        var reserve = new System.Collections.Generic.List<Vector3Int>(set);
        if (!set.Contains(den)) { reserve.Add(den); set.Add(den); }

        int tier1 = Mathf.Clamp(entry.cavityTier1Cells, 1, reserve.Count);
        var open = new System.Collections.Generic.HashSet<Vector3Int>();
        if (tier1 >= reserve.Count) open = new System.Collections.Generic.HashSet<Vector3Int>(reserve);
        else
        {
            var q = new System.Collections.Generic.Queue<Vector3Int>();
            q.Enqueue(den); open.Add(den);
            while (q.Count > 0 && open.Count < tier1)
            {
                var c = q.Dequeue();
                for (int d = 0; d < Orth4Dirs.Length && open.Count < tier1; d++)
                {
                    var p = c + Orth4Dirs[d];
                    if (set.Contains(p) && open.Add(p)) q.Enqueue(p);
                }
            }
        }

        return new CavityCarve
        {
            open = open,
            reserve = reserve,
            corrected = Mathf.Abs(reserve.Count - before),
        };
    }

    /// <summary>RunChamberCA's shape, reimplemented because the generator's copy
    /// is private and needs a live floor. Same fill, same smoothing rule, same
    /// flood from the box centre.</summary>
    private static System.Collections.Generic.List<Vector3Int> CaBlob(
        System.Random rng, Vector3Int centre, int size)
    {
        var walls = new bool[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                walls[x, y] = (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                              || rng.NextDouble() < 0.45;

        for (int it = 0; it < 4; it++)
        {
            var next = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    int n = 0;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= size || ny >= size) { n++; continue; }
                            if (walls[nx, ny]) n++;
                        }
                    next[x, y] = n >= 5;
                }
            walls = next;
        }

        var outCells = new System.Collections.Generic.List<Vector3Int>();
        int half = size / 2;
        if (walls[half, half]) return outCells;

        var seen = new bool[size, size];
        var stack = new System.Collections.Generic.Stack<Vector2Int>();
        stack.Push(new Vector2Int(half, half));
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.x < 0 || p.y < 0 || p.x >= size || p.y >= size) continue;
            if (seen[p.x, p.y] || walls[p.x, p.y]) continue;
            seen[p.x, p.y] = true;
            outCells.Add(new Vector3Int(centre.x + p.x - half, centre.y + p.y - half, 0));
            stack.Push(new Vector2Int(p.x + 1, p.y));
            stack.Push(new Vector2Int(p.x - 1, p.y));
            stack.Push(new Vector2Int(p.x, p.y + 1));
            stack.Push(new Vector2Int(p.x, p.y - 1));
        }
        return outCells;
    }

    private static int SpanOf(System.Collections.Generic.List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return 0;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in cells)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.y > maxY) maxY = c.y;
        }
        return Mathf.Max(maxX - minX, maxY - minY) + 1;
    }

    /// <summary>Is any cell of `from` 4-connected to `target`, walking only
    /// through the union of the two? The breach question, asked at the den end.</summary>
    private static bool ReachesSet(
        System.Collections.Generic.HashSet<Vector3Int> from,
        System.Collections.Generic.HashSet<Vector3Int> target)
    {
        var seen = new System.Collections.Generic.HashSet<Vector3Int>();
        var q = new System.Collections.Generic.Queue<Vector3Int>();
        foreach (var c in from) { q.Enqueue(c); seen.Add(c); break; }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (target.Contains(c)) return true;
            for (int d = 0; d < Orth4Dirs.Length; d++)
            {
                var p = c + Orth4Dirs[d];
                if ((from.Contains(p) || target.Contains(p)) && seen.Add(p)) q.Enqueue(p);
            }
        }
        return false;
    }

    private static int FloorRadiusFor(int floorIndex)
    {
        switch (floorIndex)
        {
            case 0: return 100;
            case 1: return 150;
            case 2: return 250;
            case 3: return 400;
            default: return 600;
        }
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
    /// <summary>True when this floor's result carries vault bookkeeping at
    /// all. A floor that never asked for one should say nothing rather than
    /// report a false negative every time the command runs.</summary>
    static bool entryAsksForVault(AncientSiteResult r)
        => r != null && (r.deadCorePlaced || !string.IsNullOrEmpty(r.deadCorePlanPicked));

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
                  .Append(", unwalkable ").Append(diag.rejectedUnwalkable)
                  .Append(", noDoorHeading ").Append(diag.rejectedNoDoorHeading)
                  .Append('\n');
                sb.Append("    seats: ").Append(diag.lanedSplits)
                  .Append(" threaded, ").Append(diag.spursEmitted)
                  .Append(" spurred (").Append(diag.spursReaimed)
                  .Append(" re-aimed), ").Append(diag.spursLost)
                  .Append(" SPUR LOST").Append('\n');
                sb.Append("    anchors in band: junctions ").Append(diag.inBandJunctions)
                  .Append(", roadCells ").Append(diag.inBandRoadCells)
                  .Append(", roadEnds ").Append(diag.inBandRoadEnds).Append('\n');

                // The holy pass on its own line, so a floor that filled its ruins
                // and starved its seals cannot read as a floor that went fine.
                // Silent-by-wording on a floor that asked for none.
                sb.Append("    ").Append(diag.HolySummary()).Append('\n');
                if (diag.holyWanted > 0 && diag.holyPlaced < diag.holyWanted)
                    sb.Append("    !! HOLY SHORTFALL -- ")
                      .Append(diag.holyWanted - diag.holyPlaced)
                      .Append(" seal(s) short of the roll. Check minSpacing ")
                      .Append("against the placement band.\n");
                if (diag.extraPlaced > 0)
                    sb.Append("    ").Append(diag.extraPlaced)
                      .Append(" site(s) outside the general budget (seals, vault)\n");

                // A pool of zero is the one failure that looks identical to "this
                // floor was not meant to have sites", so it is called out rather
                // than left as a number among numbers.
                if (entryAsksForVault(diag))
                    sb.Append("    vault: ")
                      .Append(diag.deadCorePlaced
                          ? "placed (" + diag.deadCorePlanPicked + ")"
                          : "!! NOT PLACED -- see the error above")
                      .Append('\n');

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

    /// <summary>What taking this heart costs, signed, for the holy report. The
    /// vault is priced apart from a seal, and the report has to say which figure
    /// it is quoting or the number is unreadable.</summary>
    static string HeartBill(SiteArchetype a)
        => (-(a == SiteArchetype.DeadCoreVault
                ? HolyGroundLedger.AlignmentForVaultHeart
                : HolyGroundLedger.AlignmentForHeart)).ToString("0.#");

    [ContextMenu("Log Rim Facade")]
    void LogRimFacade()
    {
        var fm = FloorManager.Instance;
        var floor = fm != null ? fm.GetFloor(0) : null;
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null) { Debug.LogWarning("[Commands] No floor 0 terrain."); return; }

        var ring = terrain.RimFacadeOuter;
        var classifier = new CaveWallClassifier(floor.TileInfluence, floor.FeatureGenerator, terrain);
        int solid = 0, southFacing = 0, fogged = 0;
        foreach (var c in ring)
        {
            if (!classifier.IsSolid(c)) continue;
            solid++;
            if (!classifier.IsSolid(c + Vector3Int.down)) southFacing++;
        }
        var fog = terrain.FogTilemap;
        if (fog != null)
            foreach (var c in ring)
                if (fog.GetTile(c) != null) fogged++;

        int band = terrain.RimFacadeLayers.Count;
        int nubs = terrain.RimNubCells.Count;

        // Per-layer light readout. A screenshot cannot tell "the ramp never reached
        // this row" from "the ramp reached it and something overwrote it", and those
        // point at completely different places, so print both.
        var shadow = FindAnyObjectByType<DungeonShadow>();
        if (shadow == null) Debug.LogWarning("[Commands] No DungeonShadow; ramp readout skipped.");
        else
        {
            int depth = Mathf.Max(1, terrain.RimFacadeDepth);
            var count = new int[depth];
            var lit = new int[depth];
            var voids = new int[depth];
            var lo = new float[depth];
            var hi = new float[depth];
            var sum = new float[depth];
            for (int i = 0; i < depth; i++) { lo[i] = 1f; hi[i] = 0f; }

            foreach (var kv in terrain.RimFacadeLayers)
            {
                int L = Mathf.Clamp(kv.Value, 0, depth - 1);
                count[L]++;
                if (shadow.IsVoidCell(kv.Key)) voids[L]++;
                if (!shadow.TryGetBaseLight(kv.Key, out float light)) continue;
                lit[L]++;
                sum[L] += light;
                if (light < lo[L]) lo[L] = light;
                if (light > hi[L]) hi[L] = light;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Commands] RIM FACADE ramp, per layer (0 = outermost):");
            for (int L = 0; L < depth; L++)
            {
                if (lit[L] == 0)
                {
                    sb.AppendLine($"  layer {L}: {count[L],5} cells, NONE lit -- the ramp never reached this row.");
                    continue;
                }
                sb.AppendLine($"  layer {L}: {count[L],5} cells, {lit[L],5} lit, " +
                              $"light min {lo[L]:F3} mean {sum[L] / lit[L]:F3} max {hi[L]:F3}, " +
                              $"{voids[L]} void");
            }
            sb.AppendLine("  expected at depth 6 / falloff 2 / inner 0.15: " +
                          "1.000, 0.628, 0.363, 0.203, 0.150, then VOID.");
            Debug.Log(sb.ToString());
        }
        Debug.Log($"[Commands] RIM FACADE floor 0 (radius {terrain.CurrentRadius}, " +
                  $"depth {terrain.RimFacadeDepth}): {band} band cells over {ring.Count} " +
                  $"outer, {solid} capped, {ring.Count - solid} notched (entrance channel " +
                  $"plus river mouths), {southFacing} draping a face, {fogged} fogged " +
                  $"(the river cells the rock filter skips -- a few is right, 0 means " +
                  $"the filter stopped working). {nubs} nubs demoted -- want 4 at a " +
                  $"circular rim.");
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
            // No per-cell figure any more, because there is no per-cell bill.
            // Hallowed ground is free to hold and free to chew; the charge is at
            // the heart, so it is printed per seal below, where it is incurred.
            sb.Append("  floor index ").Append(i).Append(": ")
              .Append(holyCells).Append(" hallowed cells, ")
              .Append(claimed).Append(" claimed, ").Append(mined)
              .Append(" mined (no alignment cost -- edges are free)\n");

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
                  .Append(site.heartCell == null
                              ? ", NO HEART (authoring fault)"
                              : heartGone
                                  ? ", heart BROKEN (" + HeartBill(site.archetype) + " alignment)"
                                  : ", heart intact (" + HeartBill(site.archetype) + " if taken)")
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