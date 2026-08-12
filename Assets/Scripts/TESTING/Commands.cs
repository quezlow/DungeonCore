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