using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generator for the research roster: every TechNodeDefinition plus the
/// TechTree, list-driven and idempotent (re-running refreshes text and
/// re-wires the tree in generator order -- hand-added nodes fall out, so
/// fold them in here). Also patches requiredTechKey onto the gated monster
/// and room definition assets, and the Barracks upgrade gate, so no manual
/// Inspector drags remain.
/// </summary>
public static class TechContentGenerator
{
    private const string Folder = "Assets/ScriptableObjects/Tech";
    private const string PatternFolder = "Assets/ScriptableObjects/Patterns";
    private const string RoomFolder = "Assets/ScriptableObjects/Rooms/Rooms";
    private const string MonsterFolder = "Assets/ScriptableObjects/Monsters/Regular";

    private static readonly List<TechNodeDefinition> generated = new();

    [MenuItem("Dungeon Core/Generate Tech Content")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Tech");
        generated.Clear();

        var n_skeleton = Define("skeleton", "Remembered Bones", ResearchPath.Bestiary, 1, 0, 1,
            "Something stirs at the edge of recall.",
            "The shape of a servant, remembered whole. Skeletons may be placed.");
        n_skeleton.bootstrapUnlocked = false;   // granted by the tutorial wisp, not on new game

        var n_spike_trap = Define("spike_trap", "Remembered Spikes", ResearchPath.Architecture, 1, 0, 1,
            "A sharpness, half-forgotten.",
            "Iron teeth in the floor, remembered whole. Spike traps may be placed.");
        n_spike_trap.bootstrapUnlocked = false;   // granted by the tutorial wisp, not on new game

        var n_status_bars = Define("status_bars", "Remembered Sight", ResearchPath.Observation, 1, 0, 1,
            "The dark was not always blind.",
            "The core perceives the vigour of things that move within it. Status bars are shown.");
        n_status_bars.bootstrapUnlocked = false;   // granted by the tutorial wisp, not on new game

        var n_wave_preview = Define("wave_preview", "Read the Coming Tide", ResearchPath.Observation, 1, 10, 1,
            "Footsteps, before they fall.",
            "The next raid announces itself. The wave preview is shown.");

        var n_minimap = Define("minimap", "Map the Deep Warren", ResearchPath.Observation, 1, 10, 1,
            "The shape of the dark, held in the mind.",
            "The core learns the lay of its own halls. The minimap is shown.");

        var n_alerts = Define("alerts", "Ledger of Alarums", ResearchPath.Observation, 1, 0, 1,
            "Not every disturbance need go unmarked.",
            "The core keeps a running account of what stirs. Alerts, their ledger, and the ticker are shown.");

                var n_known_parties = Define("known_parties", "Ledger of the Fallen", ResearchPath.Observation, 2, 15, 1,
            "Names, kept in the dark.",
            "A ledger of parties within and nemeses without. Opens with K.");

        var n_adventurer_stats = Define("adventurer_stats", "Study Adventurer Anatomy", ResearchPath.Observation, 2, 15, 2,
            "What are they, under the armour?",
            "The measure of an intruder, laid bare. The stats panel is shown.");
        n_adventurer_stats.overrideKey = "adventurer_stats";
        n_adventurer_stats.visibility = TechNodeDefinition.VisibilityCondition.KillsAny;
        n_adventurer_stats.visibilityKillCount = 1;   // the first fallen intruder reveals it

        var n_oracle_intent = Define("oracle_intent", "Whispers of Intent", ResearchPath.Observation, 3, 25, 2,
            "Why do they come?",
            "Purpose, read from a stride. Intent badges are shown above intruders.");
        n_oracle_intent.overrideKey = "oracle_chamber";

        var n_study_holy = Define("study_holy_order", "Study the Holy Order", ResearchPath.Observation, 3, 20, 2,
    "Why do they hate us so?",
    "The Church's ways, set down. Their profile and tactics show in the faction panel.");
        n_study_holy.overrideKey = "faction_intel.holy_order";

        var n_study_merc = Define("study_mercenaries", "Study the Mercenary Company", ResearchPath.Observation, 3, 20, 2,
            "What do they want?",
            "The Company's ledger, read. Their profile and tactics show in the faction panel.");
        n_study_merc.overrideKey = "faction_intel.mercenaries";

        var n_scout1 = Define("scout_1", "Sight Beyond the Threshold", ResearchPath.Observation, 1, 15, 7,
    "The wood past the mouth is a blur. It need not stay so.",
    "Cast the core's sight over the entrance clearing. Scout the near forest as a bounded view.");

        var n_scout2 = Define("scout_2", "Eyes on the Deep Wood", ResearchPath.Observation, 2, 25, 14,
            "There is more out there than the clearing shows.",
            "Push the scouting sight deeper — reach the wood's inner camps.");

        var n_scout3 = Define("scout_3", "The Far Marches", ResearchPath.Observation, 3, 40, 21,
            "The tree line is not the world's edge.",
            "Extend the sight to the forest's far edge, where it meets the old wood.");

        var n_deeper_lairs = Define("deeper_lairs", "Deeper Lairs", ResearchPath.Architecture, 2, 15, 2,
            "The beasts could rest easier, given better stone.",
            "Barracks upgrades past tier 1. Requires the pattern of Rough Stone.");
        n_deeper_lairs.affinity = DungeonType.Earth;

        var n_consecrant_masonry = Define("consecrant_masonry", "Consecrant Masonry", ResearchPath.Architecture, 2, 15, 2,
            "Their stone, turned to our purposes.",
            "Shrines may be built. Requires the pattern of Hallowed Stone.");
        n_consecrant_masonry.affinity = DungeonType.Light;

        var n_halls_of_war = Define("halls_of_war", "Halls of War", ResearchPath.Architecture, 3, 30, 3,
            "Iron in the walls, iron in the sleepers.",
            "Barracks upgrades to tier 3. Requires the pattern of Wrought Iron.");

        var n_deep_foundations = Define("deep_foundations", "Deep Foundations", ResearchPath.Architecture, 3, 30, 3,
            "Something vast will need somewhere to stand.",
            "Boss rooms may be built. Requires the pattern of Veined Granite.");
        n_deep_foundations.affinity = DungeonType.Earth;

        var n_shambling_dead = Define("shambling_dead", "Shambling Dead", ResearchPath.Bestiary, 2, 15, 2,
            "Flesh remembers slower than bone.",
            "Zombies may be placed.");
        n_shambling_dead.affinity = DungeonType.Dark;

        var n_bones_in_iron = Define("bones_in_iron", "Bones in Iron", ResearchPath.Bestiary, 2, 15, 2,
            "A rattle, heavier than before.",
            "Armoured skeletons may be placed.");

        var n_whisperer_in_marrow = Define("whisperer_in_marrow", "Whisperer in Marrow", ResearchPath.Bestiary, 3, 35, 3,
            "The dead learn from those who bless.",
            "Necromancers may be placed.");
        n_whisperer_in_marrow.affinity = DungeonType.Dark;

        var n_barrow_oaths = Define("barrow_oaths", "Barrow Oaths", ResearchPath.Bestiary, 3, 35, 3,
            "Oaths outlast the flesh that swore them.",
            "Barrow Knights may be placed.");
        n_barrow_oaths.affinity = DungeonType.Earth;

        var n_litany_of_graves = Define("litany_of_graves", "Litany of Graves", ResearchPath.Bestiary, 4, 60, 4,
            "The whisper becomes a sermon.",
            "Deathpriests may be placed.");
        n_litany_of_graves.affinity = DungeonType.Dark;

        var n_vaulted_reserves = Define("vaulted_reserves", "Vaulted Reserves", ResearchPath.Architecture, 2, 15, 2,
            "Somewhere below, room enough for more.",
            "Treasuries may be built. Requires the pattern of Silverwork.");

        var n_summoning_circle = Define("summoning_circle", "Summoning Circle", ResearchPath.Architecture, 2, 15, 2,
            "The calling-back, drawn in a ring.",
            "Spawn Chambers may be built. Requires the pattern of Packed Earth.");
        n_summoning_circle.affinity = DungeonType.Dark;

        var n_drawn_circle = Define("drawn_circle", "The Drawn Circle", ResearchPath.Architecture, 2, 15, 2,
            "Chalk and salt, and older things.",
            "Ritual Circles may be built. Requires the pattern of Quarry Sand.");
        n_drawn_circle.affinity = DungeonType.Dark;

        var n_proving_grounds = Define("proving_grounds", "Proving Grounds", ResearchPath.Architecture, 3, 30, 3,
            "Let them bruise, that they may bite.",
            "Arenas may be built. Requires the pattern of Tempered Steel.");

        var n_whispered_dread = Define("whispered_dread", "Whispered Dread", ResearchPath.Architecture, 3, 30, 3,
            "What the walls remember, intruders feel.",
            "Dread Chambers may be built. Requires the pattern of Cured Leather.");
        n_whispered_dread.affinity = DungeonType.Dark;

        var n_hall_of_trophies = Define("hall_of_trophies", "Hall of Trophies", ResearchPath.Architecture, 2, 15, 2,
            "A room for the proof of what I have done.",
            "The Trophy Hall may be built. Deeds earned in blood can be mounted there, and made to matter.");
        n_hall_of_trophies.affinity = DungeonType.None;

        var n_coals_below = Define("coals_below", "Coals Below", ResearchPath.Architecture, 3, 30, 3,
            "The fire kept, the iron taught.",
            "Forges may be built. All traps bite harder. Requires the pattern of Wrought Iron.");
        n_coals_below.affinity = DungeonType.Fire;

        var n_waiting_dark = Define("waiting_dark", "The Waiting Dark", ResearchPath.Architecture, 3, 30, 3,
            "Stone patient enough to hold a name.",
            "Crypts may be built. The named dead can be kept, and spent. Requires the pattern of Gravegold.");
        n_waiting_dark.affinity = DungeonType.Dark;

        // Hand-added nodes folded in verbatim, so a generator rerun keeps them.
        // Both had fallen out of the tree asset on main -- the exact hazard the
        // header warns about -- leaving Cold Iron and Standing Orders
        // unresearchable. The generator is authoritative again from here.
        var n_patrol = Define("patrol_orders", "Standing Orders", ResearchPath.Observation, 1, 10, 2,
            "A route, held in mind.",
            "A route held in mind. Creatures may be given a patrol path.");

        var n_prison = Define("prison", "Cold Iron", ResearchPath.Architecture, 2, 15, 2,
            "Cold iron, for the ones worth keeping.",
            "The Prison may be built. Beaten intruders are taken alive into its cells -- to be released, questioned, or put to the stone. Requires the pattern of Wrought Iron.");
        n_prison.affinity = DungeonType.Dark;

        // The trapworks: the crossbow trunk, six exclusive elemental
        // signatures (each visible only to its matching core), two neutral
        // craft pieces, and the Trapwright line.
        var n_crossbow = Define("crossbow_trap", "The Patient Arm", ResearchPath.Architecture, 2, 15, 2,
            "Something that waits, and does not tire.",
            "Crossbow sentries may be placed: a watched span of hall, and a bolt for whoever crosses it. Requires the pattern of Tempered Steel.");

        var n_trap_fire = Define("trap_fireball", "The Waking Ember", ResearchPath.Architecture, 3, 30, 3,
            "Heat, folded and waiting.",
            "Fireball runes may be laid: a burst for the many, and a burn that clings after. Requires the pattern of Wrought Iron.");
        n_trap_fire.affinity = DungeonType.Fire;

        var n_trap_ice = Define("trap_ice_spikes", "Teeth of Winter", ResearchPath.Architecture, 3, 30, 3,
            "The cold remembers how to bite.",
            "Ice-spike traps may be laid: a wound, and a cold that all but stills. Requires the pattern of Silverwork.");
        n_trap_ice.affinity = DungeonType.Water;

        var n_trap_earth = Define("trap_earth_spikes", "The Rising Stone", ResearchPath.Architecture, 3, 30, 3,
            "The floor need not lie still.",
            "Earth-spike traps may be laid: the floor itself striking upward, and hurling back. Requires the pattern of Veined Granite.");
        n_trap_earth.affinity = DungeonType.Earth;

        var n_trap_gale = Define("trap_gale_vent", "The Hollow Gale", ResearchPath.Architecture, 3, 30, 3,
            "A breath, held under stone.",
            "Gale vents may be laid: a hammer of wind that hurls intruders and breaks their lines. Requires the pattern of Quarry Sand.");
        n_trap_gale.affinity = DungeonType.Air;

        var n_trap_flash = Define("trap_blinding_flash", "The Searing Glance", ResearchPath.Architecture, 3, 30, 3,
            "Not all light means to guide.",
            "Blinding flash traps may be laid: judgment in a burst -- quarrels forgotten, trap-sense burned away. Requires the pattern of Hallowed Stone.");
        n_trap_flash.affinity = DungeonType.Light;

        var n_trap_umbral = Define("trap_umbral_snare", "The Clinging Dark", ResearchPath.Architecture, 3, 30, 3,
            "Some shadows do not part.",
            "Umbral snares may be laid: recoil, slowness, and senses dimmed to a candle's reach. Requires the pattern of Gravegold.");
        n_trap_umbral.affinity = DungeonType.Dark;

        var n_sleep_dart = Define("sleep_dart", "The Quiet Needle", ResearchPath.Architecture, 3, 20, 2,
            "Softly, and they forget you.",
            "Sleep darts may be laid: no wound, only a stolen moment and a forgotten quarrel. Requires the pattern of Cured Leather.");

        var n_siphon = Define("siphon_rune", "The Tithing Mark", ResearchPath.Architecture, 3, 20, 2,
            "All who pass owe something.",
            "Siphon runes may be laid: a small wound, and the taking returned to the core as mana. Requires the pattern of Silverwork.");

        var n_trapwright1 = Define("trapwright_1", "Trapwright's Craft", ResearchPath.Architecture, 3, 25, 3,
            "The craft improves with use.",
            "Every trap bites a quarter harder and its afflictions cling a quarter longer. Requires the pattern of Wrought Iron.");

        var n_trapwright2 = Define("trapwright_2", "Master Trapwright", ResearchPath.Architecture, 4, 45, 4,
            "The craft, mastered.",
            "Trap damage and afflictions rise to half again over base, and every trap resets a fifth faster. Requires the pattern of Tempered Steel.");

        var n_mutation1 = Define("mutation_1", "The Shaping of Flesh", ResearchPath.Bestiary, 3, 25, 3,
            "What serves can be made better.",
            "Every monster of the dungeon strikes harder and sheds a tenth of every wound. Requires the pattern of Cured Leather.");

        var n_mutation2 = Define("mutation_2", "The Perfected Strain", ResearchPath.Bestiary, 4, 45, 4,
            "The shaping, finished.",
            "The dungeon's monsters strike near a third harder, a fifth of every wound falls away, and their stride quickens a tenth. Requires the pattern of Runed Crystal.");

        // Prerequisites, patterns, visibility, gates -- wired after all nodes exist.
        AddPrereq(n_wave_preview, n_status_bars);
        AddPrereq(n_known_parties, n_wave_preview);
        AddPrereq(n_adventurer_stats, n_wave_preview);
        AddPrereq(n_oracle_intent, n_known_parties);
        AddPrereq(n_oracle_intent, n_adventurer_stats);
        AddPrereq(n_deeper_lairs, n_spike_trap);
        AddPattern(n_deeper_lairs, "RoughStone");
        AddGate(n_deeper_lairs, "Room_Barracks", 2);
        AddPrereq(n_consecrant_masonry, n_spike_trap);
        AddPattern(n_consecrant_masonry, "HallowedStone");
        AddPrereq(n_halls_of_war, n_deeper_lairs);
        AddPattern(n_halls_of_war, "WroughtIron");
        AddGate(n_halls_of_war, "Room_Barracks", 3);
        AddPrereq(n_deep_foundations, n_consecrant_masonry);
        AddPattern(n_deep_foundations, "VeinedGranite");
        AddPrereq(n_shambling_dead, n_skeleton);
        AddPrereq(n_bones_in_iron, n_skeleton);
        AddPrereq(n_whisperer_in_marrow, n_shambling_dead);
        AddPrereq(n_whisperer_in_marrow, n_bones_in_iron);
        AddPrereq(n_barrow_oaths, n_bones_in_iron);
        AddPrereq(n_litany_of_graves, n_whisperer_in_marrow);
        AddPrereq(n_study_holy, n_known_parties);
        n_study_holy.visibility = TechNodeDefinition.VisibilityCondition.KeyUnlocked;
        n_study_holy.visibilityKey = "encounter.holy_order";
        AddPrereq(n_study_merc, n_known_parties);
        n_study_merc.visibility = TechNodeDefinition.VisibilityCondition.KeyUnlocked;
        n_study_merc.visibilityKey = "encounter.mercenaries";
        n_whisperer_in_marrow.visibility = TechNodeDefinition.VisibilityCondition.KillsOfClass;
        n_whisperer_in_marrow.visibilityClassName = "Cleric";
        n_whisperer_in_marrow.visibilityKillCount = 5;

        n_scout1.visibility = TechNodeDefinition.VisibilityCondition.KeyUnlocked;
        n_scout1.visibilityKey = "event.entrance_discovered";

        AddPrereq(n_scout2, n_scout1);
        n_scout2.visibility = TechNodeDefinition.VisibilityCondition.KeyUnlocked;
        n_scout2.visibilityKey = "event.entrance_discovered";

        AddPrereq(n_scout3, n_scout2);
        n_scout3.visibility = TechNodeDefinition.VisibilityCondition.KeyUnlocked;
        n_scout3.visibilityKey = "event.entrance_discovered";

        AddPrereq(n_vaulted_reserves, n_spike_trap);
        AddPattern(n_vaulted_reserves, "Silverwork");
        AddPrereq(n_summoning_circle, n_spike_trap);
        AddPattern(n_summoning_circle, "PackedEarth");
        AddPrereq(n_drawn_circle, n_spike_trap);
        AddPattern(n_drawn_circle, "QuarrySand");
        AddPrereq(n_hall_of_trophies, n_deeper_lairs);
        AddPattern(n_hall_of_trophies, "WroughtIron");
        AddPrereq(n_proving_grounds, n_deeper_lairs);
        AddPattern(n_proving_grounds, "TemperedSteel");
        AddPrereq(n_whispered_dread, n_summoning_circle);
        AddPattern(n_whispered_dread, "CuredLeather");
        AddPrereq(n_coals_below, n_deeper_lairs);
        AddPattern(n_coals_below, "WroughtIron");
        AddPrereq(n_waiting_dark, n_summoning_circle);
        AddPattern(n_waiting_dark, "Gravegold");

        AddPrereq(n_prison, n_hall_of_trophies);
        AddPattern(n_prison, "WroughtIron");

        AddPrereq(n_crossbow, n_spike_trap);
        AddPattern(n_crossbow, "TemperedSteel");

        AddPrereq(n_trap_fire, n_crossbow);
        AddPattern(n_trap_fire, "WroughtIron");
        n_trap_fire.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;
        AddPrereq(n_trap_ice, n_crossbow);
        AddPattern(n_trap_ice, "Silverwork");
        n_trap_ice.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;
        AddPrereq(n_trap_earth, n_crossbow);
        AddPattern(n_trap_earth, "VeinedGranite");
        n_trap_earth.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;
        AddPrereq(n_trap_gale, n_crossbow);
        AddPattern(n_trap_gale, "QuarrySand");
        n_trap_gale.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;
        AddPrereq(n_trap_flash, n_crossbow);
        AddPattern(n_trap_flash, "HallowedStone");
        n_trap_flash.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;
        AddPrereq(n_trap_umbral, n_crossbow);
        AddPattern(n_trap_umbral, "Gravegold");
        n_trap_umbral.visibility = TechNodeDefinition.VisibilityCondition.CoreAffinity;

        AddPrereq(n_sleep_dart, n_crossbow);
        AddPattern(n_sleep_dart, "CuredLeather");
        AddPrereq(n_siphon, n_crossbow);
        AddPattern(n_siphon, "Silverwork");

        AddPrereq(n_trapwright1, n_crossbow);
        AddPattern(n_trapwright1, "WroughtIron");
        AddPrereq(n_trapwright2, n_trapwright1);
        AddPattern(n_trapwright2, "TemperedSteel");

        AddPrereq(n_mutation1, n_bones_in_iron);
        AddPattern(n_mutation1, "CuredLeather");
        AddPrereq(n_mutation2, n_mutation1);
        AddPattern(n_mutation2, "RunedCrystal");

        WireTree(new TechNodeDefinition[] { n_skeleton, n_spike_trap, n_status_bars, n_wave_preview, n_minimap, n_alerts, n_known_parties,
            n_adventurer_stats, n_oracle_intent, n_study_holy, n_study_merc, n_deeper_lairs, n_consecrant_masonry, n_halls_of_war, n_deep_foundations,
            n_shambling_dead, n_bones_in_iron, n_whisperer_in_marrow, n_barrow_oaths, n_litany_of_graves, n_vaulted_reserves, n_summoning_circle, n_drawn_circle,
            n_hall_of_trophies, n_proving_grounds, n_whispered_dread, n_coals_below, n_waiting_dark, n_scout1,n_scout2, n_scout3,
            n_patrol, n_prison, n_crossbow, n_trap_fire, n_trap_ice, n_trap_earth, n_trap_gale, n_trap_flash, n_trap_umbral,
            n_sleep_dart, n_siphon, n_trapwright1, n_trapwright2, n_mutation1, n_mutation2 });

        // Gate keys on the definitions that consume them.
        PatchKey(MonsterFolder + "/MonsterDef_Skeleton.asset", "tech.skeleton");
        PatchKey(MonsterFolder + "/MonsterDef_Zombie.asset", "tech.shambling_dead");
        PatchKey(MonsterFolder + "/MonsterDef_ArmoredSkeleton.asset", "tech.bones_in_iron");
        PatchKey(MonsterFolder + "/MonsterDef_Necromancer.asset", "tech.whisperer_in_marrow");
        PatchKey(MonsterFolder + "/MonsterDef_BarrowKnight.asset", "tech.barrow_oaths");
        PatchKey(MonsterFolder + "/MonsterDef_Deathpriest.asset", "tech.litany_of_graves");
        PatchKey(RoomFolder + "/Room_Shrine.asset", "tech.consecrant_masonry");
        PatchKey(RoomFolder + "/Room_BossRoom.asset", "tech.deep_foundations");
        PatchKey(RoomFolder + "/Room_Treasury.asset", "tech.vaulted_reserves");
        PatchKey(RoomFolder + "/Room_SpawnChamber.asset", "tech.summoning_circle");
        PatchKey(RoomFolder + "/Room_RitualCircle.asset", "tech.drawn_circle");
        PatchKey(RoomFolder + "/Room_Arena.asset", "tech.proving_grounds");
        PatchKey(RoomFolder + "/Room_DreadChamber.asset", "tech.whispered_dread");
        PatchKey(RoomFolder + "/Room_Forge.asset", "tech.coals_below");
        PatchKey(RoomFolder + "/Room_Crypt.asset", "tech.waiting_dark");
        PatchKey(RoomFolder + "/Room_TrophyHall.asset", "tech.hall_of_trophies");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"TechContentGenerator: {generated.Count} nodes wired; gate keys patched.");
    }

    private static TechNodeDefinition Define(string id, string displayName, ResearchPath path,
        int tier, int pointCost, int durationDays, string hiddenHint, string description)
    {
        string assetPath = $"{Folder}/{displayName.Replace(" ", "").Replace("'", "")}.asset";
        var node = AssetDatabase.LoadAssetAtPath<TechNodeDefinition>(assetPath);
        if (node == null)
        {
            node = ScriptableObject.CreateInstance<TechNodeDefinition>();
            AssetDatabase.CreateAsset(node, assetPath);
        }
        node.id = id;
        node.overrideKey = "";
        node.displayName = displayName;
        node.path = path;
        node.tier = tier;
        node.pointCost = pointCost;
        node.durationDays = durationDays;
        node.affinity = DungeonType.None;
        node.bootstrapUnlocked = false;
        node.visibility = TechNodeDefinition.VisibilityCondition.Always;
        node.prerequisites.Clear();
        node.patternRequirements.Clear();
        node.upgradeGates.Clear();
        node.hiddenHint = hiddenHint;
        node.description = description;
        EditorUtility.SetDirty(node);
        generated.Add(node);
        return node;
    }

    private static void AddPrereq(TechNodeDefinition node, TechNodeDefinition prereq)
        => node.prerequisites.Add(prereq);

    private static void AddPattern(TechNodeDefinition node, string patternAssetName)
    {
        var pat = AssetDatabase.LoadAssetAtPath<PatternDefinition>(
            $"{PatternFolder}/{patternAssetName}.asset");
        if (pat != null) node.patternRequirements.Add(pat);
        else Debug.LogWarning($"TechContentGenerator: pattern '{patternAssetName}' not found for {node.displayName}.");
    }

    private static void AddGate(TechNodeDefinition node, string roomAssetName, int minTier)
    {
        var room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(
            $"{RoomFolder}/{roomAssetName}.asset");
        if (room == null)
        {
            Debug.LogWarning($"TechContentGenerator: room '{roomAssetName}' not found for {node.displayName}.");
            return;
        }
        node.upgradeGates.Add(new TechNodeDefinition.RoomUpgradeGate { room = room, minTier = minTier });
    }

    private static void WireTree(TechNodeDefinition[] order)
    {
        string treePath = Folder + "/TechTree.asset";
        var tree = AssetDatabase.LoadAssetAtPath<TechTree>(treePath);
        if (tree == null)
        {
            tree = ScriptableObject.CreateInstance<TechTree>();
            AssetDatabase.CreateAsset(tree, treePath);
        }
        var so = new SerializedObject(tree);
        var list = so.FindProperty("nodes");
        list.arraySize = order.Length;
        for (int i = 0; i < order.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = order[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tree);
        foreach (var n in order) EditorUtility.SetDirty(n);
    }

    private static void PatchKey(string assetPath, string key)
    {
        var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
        if (obj == null)
        {
            Debug.LogWarning($"TechContentGenerator: asset not found for key patch: {assetPath}");
            return;
        }
        var so = new SerializedObject(obj);
        var prop = so.FindProperty("requiredTechKey");
        if (prop == null)
        {
            Debug.LogWarning($"TechContentGenerator: no requiredTechKey on {assetPath}");
            return;
        }
        prop.stringValue = key;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(obj);
    }
}