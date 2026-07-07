using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click builder for the prologue content set: quest assets, item prefab
/// variants, ItemDictionary registration, and every NPCDialogue asset,
/// generated from the tables below.
///
/// Menu: Dungeon Core -> Generate Tutorial Content. Safe to re-run - existing assets
/// are updated in place, and the ItemDictionary list is append-only (order
/// defines item IDs, so entries are never reordered or removed).
///
/// The generator validates every table before writing anything: parallel
/// array lengths, choice indices, end-line placement, quest and item name
/// resolution. Any error aborts the run with a full list in the console.
/// </summary>
public static class TutorialContentGenerator
{
    // ── Asset locations ──────────────────────────────────────────────────
    private const string DialogueFolder = "Assets/NPC/Tutorial";
    private const string QuestFolder = "Assets/Quests/Tutorial";
    private const string ItemFolder = "Assets/Prefabs/Items";
    private const string BaseItemPath = "Assets/Prefabs/Item.prefab";
    private const string ItemDictionaryPath = "Assets/Prefabs/ItemDictionary.prefab";
    private const string AxeQuestPath = "Assets/Quests/CollectAxe.asset";
    private const string VoicePath = "Assets/Audio/Voices/ui_menu_popup_02.wav";
    private const string CharacterRoot = "Assets/Art/characters/npc";

    // Items that already exist in the dictionary and may be used as rewards.
    private static readonly string[] KnownExistingItems = { "Health Potion", "Coin" };

    // ── Spec types ───────────────────────────────────────────────────────
    private class ItemSpec
    {
        public string name;
    }

    private class RewardSpec
    {
        public string item;
        public int amount;
    }

    private class QuestSpec
    {
        public string id;
        public string title;
        public string desc;
        public string itemName;
        public int amount = 1;
        public string handInFlag = "";
        public RewardSpec[] rewards = new RewardSpec[0];
    }

    private class ChoiceSpec
    {
        public int at;
        public string[] options;
        public int[] next;
        public string gives; // '1' per option that accepts the quest
    }

    private class DialogueSpec
    {
        public string asset;
        public string npcName;
        public string sprite = ""; // folder under Assets/Art/characters/npc, empty for none
        public float pitch = 1f;
        public string[] lines;
        public string auto; // '1' per line that auto-progresses
        public string end;  // '1' per line that ends the conversation
        public ChoiceSpec[] choices = new ChoiceSpec[0];
        public int inProgress;
        public int completed;
        public string questId = ""; // "", "AXE", or a QuestSpec id
    }

    // ── Items (dictionary order = registration order = item ID) ─────────
    private static readonly ItemSpec[] Items =
    {
        new ItemSpec { name = "Willow Basket" },
        new ItemSpec { name = "Brass Button" },
        new ItemSpec { name = "Mill Gear" },
        new ItemSpec { name = "Turnip" },
        new ItemSpec { name = "Coil of Rope" },
        new ItemSpec { name = "Strange Bones" },
        new ItemSpec { name = "Bittermoss" },
    };

    // ── Quests ───────────────────────────────────────────────────────────
    private static readonly QuestSpec[] Quests =
    {
        new QuestSpec
        {
            id = "tut_basket", title = "Morning Delivery",
            desc = "The note says the basket by the door goes to Maren Ashcombe.",
            itemName = "Willow Basket",
            rewards = new[] { new RewardSpec { item = "Coin", amount = 2 } },
        },
        new QuestSpec
        {
            id = "tut_goose", title = "Sir Feathers' Prize",
            desc = "Dorrit's brass button, currently in the custody of a goose.",
            itemName = "Brass Button",
            rewards = new[] { new RewardSpec { item = "Coin", amount = 1 } },
        },
        new QuestSpec
        {
            id = "tut_gear", title = "A Tooth for the Mill",
            desc = "Fetch Wick's spare gear from the crate at the smithy.",
            itemName = "Mill Gear",
            rewards = new[] { new RewardSpec { item = "Coin", amount = 3 } },
        },
        new QuestSpec
        {
            id = "tut_pantry", title = "Gruel's Empty Pot",
            desc = "Three good turnips for the tavern kitchen.",
            itemName = "Turnip", amount = 3,
            rewards = new[]
            {
                new RewardSpec { item = "Coin", amount = 2 },
                new RewardSpec { item = "Health Potion", amount = 1 },
            },
        },
        new QuestSpec
        {
            id = "tut_rope", title = "Rope for the Corner Table",
            desc = "Pell wants the coil Tally is holding. Honest work, he says.",
            itemName = "Coil of Rope",
            rewards = new[] { new RewardSpec { item = "Coin", amount = 2 } },
        },
        new QuestSpec
        {
            id = "tut_bones", title = "Strange Bones",
            desc = "Corvin pays for oddities the ground gives up.",
            itemName = "Strange Bones",
            handInFlag = TutorialFlags.FossilDelivered,
            rewards = new[] { new RewardSpec { item = "Coin", amount = 3 } },
        },
        new QuestSpec
        {
            id = "tut_moss", title = "The Last Errand",
            desc = "Bittermoss for the draught. It grows just inside the cave past the old stone.",
            itemName = "Bittermoss",
        },
    };

    // ── Dialogue ─────────────────────────────────────────────────────────
    private static readonly DialogueSpec[] Dialogues =
    {
        new DialogueSpec
        {
            asset = "MarenDialogue", npcName = "Maren Ashcombe", sprite = "npc2_2", pitch = 1.05f,
            lines = new[]
            {
                "Hold still. Not you, him. He wheezes if he talks, so he's finally quiet.",
                "If you're idle, there's a note waiting on your own table, I'd wager. And feverfew wants picking. Third row.",
                "You've found the right cottage. The basket, though, is still by your door.",
                "There it is, willow-work and all. Set it on the table. He'll eat tonight because of you.",
            },
            auto = "0000",
            end = "0111",
            inProgress = 2, completed = 3, questId = "tut_basket",
        },
        new DialogueSpec
        {
            asset = "MarenErrandDialogue", npcName = "Maren Ashcombe", sprite = "npc2_2", pitch = 1.05f,
            lines = new[]
            {
                "You've a knack for arriving when I'm short a pair of hands.",
                "The draught for his lungs is done, all but one thing. Bittermoss. It only grows where the sun's never been.",
                "Past the old stone, where the path gives out, there's a cave mouth. The moss lines the rock just inside.",
                "Stay where the light reaches. Pick what you need and come straight back.",
                "It's that or he doesn't see the week out. I'd go myself if these hands could climb.",
                "Then be quick deciding. Dusk won't wait on either of us.",
                "The cave past the old stone. Just inside. No deeper than the light.",
                "You came back. Sometimes I think the well listens when I worry.",
            },
            auto = "00101000",
            end = "00010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 2,
                    options = new[] { "I'll fetch it.", "The cave? At this hour?" },
                    next = new[] { 3, 4 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 4,
                    options = new[] { "I'll fetch it.", "Not yet." },
                    next = new[] { 3, 5 },
                    gives = "10",
                },
            },
            inProgress = 6, completed = 7, questId = "tut_moss",
        },
        new DialogueSpec
        {
            asset = "CraneDialogue", npcName = "Father Aldous Crane", sprite = "npc3_2", pitch = 0.95f,
            lines = new[]
            {
                "Mind the step. I've just polished the plaque, and the water walks where it pleases.",
                "The well? Blessed since before my predecessor's predecessor. We keep it... attended.",
                "The old stone past the gate? A weed with manners. We let it be.",
                "Light a candle if you've a moment. Small flames keep long watches.",
            },
            auto = "0000",
            end = "0001",
        },
        new DialogueSpec
        {
            asset = "SedgeDialogue", npcName = "Oldmother Sedge", sprite = "npc4_5", pitch = 0.85f,
            lines = new[]
            {
                "You walk loud. The path forgives it. The wood won't.",
                "Ask your question or don't. The stone hears either way.",
                "The dead go down. Down is not away.",
                "Go on, then. Dusk is patient. You aren't.",
            },
            auto = "0000",
            end = "0001",
        },
        new DialogueSpec
        {
            asset = "CorvinDialogue", npcName = "Corvin Latch", sprite = "npc3_3", pitch = 1.1f,
            lines = new[]
            {
                "Careful of the stack. That's forty years of what the road coughs up, in order of interest.",
                "You've the look of someone who pokes at things. Excellent. I pay for pokers.",
                "The ground here gives up oddities. Old bone, older stone. If anything strange surfaces, bring it to this desk.",
                "Splendid. Touch nothing else on the way out.",
                "Strange as in wrong for the ground it came from. Teeth where no jaw should be. You'll know it when it unsettles you.",
                "Suit yourself. The ground is patient. It's outlasted better sceptics.",
                "Nothing yet? Try where the earth's been opened. Fresh digging tells old secrets.",
                "A tooth! No, a claw. No. Put it on the desk and touch nothing. This predates the parish. Possibly the language.",
            },
            auto = "00101000",
            end = "00010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 2,
                    options = new[] { "I'll keep an eye out.", "Strange how?" },
                    next = new[] { 3, 4 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 4,
                    options = new[] { "I'll keep an eye out.", "I'd rather not." },
                    next = new[] { 3, 5 },
                    gives = "10",
                },
            },
            inProgress = 6, completed = 7, questId = "tut_bones",
        },
        new DialogueSpec
        {
            asset = "BronaDialogue", npcName = "Brona Ferro", sprite = "npc2_4", pitch = 0.9f,
            lines = new[]
            {
                "Watch the sparks or don't. They'll teach you either way.",
                "Pump the bellows if your arms are bored. Slow. Fire doesn't like nerves.",
                "The priest polishes a plaque. I polish steel. One of us is honest about it.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "WickDialogue", npcName = "Wick", sprite = "npc4_2", pitch = 1.2f,
            lines = new[]
            {
                "She's sulking. Hear that? No, you don't. That's the trouble. She should be turning.",
                "Stripped a tooth in the gearworks and won't say which. I need a spare, and Brona keeps my spares.",
                "There's a crate of mill parts at the smithy. Fetch me the gear and I'll see to the rest.",
                "Bless you. She'll turn again by supper. You'll hear her clear across the plaza.",
                "With these knees? The ladder alone would be the end of me.",
                "No. But it's everyone's bread.",
                "The smithy. Brona's spares crate. She knows the one.",
                "That's her tooth! Give it here. And if you've steady hands, the gearworks could use them once I've set this.",
            },
            auto = "00101000",
            end = "00010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 2,
                    options = new[] { "I'll get it.", "Fix it yourself." },
                    next = new[] { 3, 4 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 4,
                    options = new[] { "Fine, I'll get it.", "Not my mill." },
                    next = new[] { 3, 5 },
                    gives = "10",
                },
            },
            inProgress = 6, completed = 7, questId = "tut_gear",
        },
        new DialogueSpec
        {
            asset = "PollDialogue", npcName = "Poll", sprite = "npc1_4", pitch = 1.15f,
            lines = new[]
            {
                "Greens! Fresh as the morning, unlike my sister's junk heap.",
                "First time at market? Coin for goods, goods for coin. The shop behind me handles the fancier trades.",
                "Tell Tally her stall's a foot over the line. She counts everything but that.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "TallyDialogue", npcName = "Tally", sprite = "npc1_5", pitch = 1.12f,
            lines = new[]
            {
                "Four coppers. Three if you make me haggle. I'm counting either way.",
                "I buy odd and sell odder. That rope? Spoken for. Some sellsword's runner is meant to collect it.",
                "Poll says I'm over the line. I measured. She's under it.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "HarlDialogue", npcName = "Harl Bramm", sprite = "npc4_3", pitch = 0.92f,
            lines = new[]
            {
                "Turnips don't pull themselves. Well. They don't.",
                "Lost my axe somewhere between the stump and the second ale. Stump's easier to search.",
                "Fetch it back and there's coin in it. Fair warning: the goose has opinions about the yard.",
                "Good on you. Check along the track the children run. Things wander there.",
                "Ha! He'd sooner bite me than help. You're the better bet.",
                "Axe'll rust, turnips'll wait, and I'll grumble. Nothing new.",
                "Try the track between here and the plaza. If the goose has it, may the stone help us all.",
                "That's her! Notch and all. You've earned your coin. Mind the rows on your way out.",
            },
            auto = "00101000",
            end = "00010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 2,
                    options = new[] { "I'll find it.", "Ask the goose." },
                    next = new[] { 3, 4 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 4,
                    options = new[] { "Fine, I'll look.", "Some other time." },
                    next = new[] { 3, 5 },
                    gives = "10",
                },
            },
            inProgress = 6, completed = 7, questId = "AXE",
        },
        new DialogueSpec
        {
            asset = "DorritDialogue", npcName = "Dorrit", sprite = "npc1_1", pitch = 1.25f,
            lines = new[]
            {
                "Can't stop! He's GETTING AWAY.",
                "Sir Feathers! He bit the priest once. It was the best day. But now he's got my button.",
                "Brass one, off Ma's coat. He's CARRYING it. Get it back? I'll pay a whole copper.",
                "You have to be sneaky. He respects sneaky.",
                "It's a BRASS one. From Ma's COAT.",
                "Fine. Me and him will settle it the old way. Running.",
                "He stops to gloat at the corners. That's when you grab it!",
                "MY BUTTON! You're my second favourite person now. First is still the goose.",
            },
            auto = "00101000",
            end = "00010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 2,
                    options = new[] { "I'm on it.", "It's just a button." },
                    next = new[] { 3, 4 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 4,
                    options = new[] { "Alright, alright.", "Good luck." },
                    next = new[] { 3, 5 },
                    gives = "10",
                },
            },
            inProgress = 6, completed = 7, questId = "tut_goose",
        },
        new DialogueSpec
        {
            asset = "GruelDialogue", npcName = "Gruel", sprite = "npc2_5", pitch = 0.88f,
            lines = new[]
            {
                "Sit anywhere that holds you. Stew's off. That's a kindness, trust me.",
                "Kitchen's bare of turnips and Harl's boy never came. If you're headed westway, three good ones fills the pot.",
                "Ale's on me when you're back. Watered, but on me.",
                "Gossip? Those three in the corner asked after the old paths east. The stone, the caves. Paid in city coin. And the board outside has honest work, if you're collecting.",
                "Door's always open. Hinge is broke.",
                "Three turnips. Harl's rows, or Poll's stall if he's stingy.",
                "Now that's a pot worth stirring. First bowl's yours. Refusal is wise, but noted.",
            },
            auto = "0101000",
            end = "0010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 1,
                    options = new[] { "I'll bring turnips.", "What's the gossip?" },
                    next = new[] { 2, 3 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 3,
                    options = new[] { "I'll bring your turnips.", "Thanks." },
                    next = new[] { 2, 4 },
                    gives = "10",
                },
            },
            inProgress = 5, completed = 6, questId = "tut_pantry",
        },
        new DialogueSpec
        {
            asset = "SerraDialogue", npcName = "Serra Vane", sprite = "npc3_6", pitch = 1f,
            lines = new[]
            {
                "You're standing in the door. Habit of mine to notice.",
                "Work's south, they said. Then the maps said otherwise. There's older stone under this town than in it.",
                "Drink your drink. We're poor company by design.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "PellDialogue", npcName = "Pell", sprite = "npc3_7", pitch = 1.08f,
            lines = new[]
            {
                "Relax. If I'd taken it, you'd still be smiling.",
                "Since you're upright and idle: the junk twin outside holds a coil of rope for us. Fetch it, earn a copper honestly. Novel feeling.",
                "Quick about it. We move at dusk.",
                "And be seen hauling rope through town? I've a reputation to underachieve.",
                "Suit yourself. Doors open either way. Some just need persuading.",
                "Tally's stall. Say it's for the corner table. She'll grumble, she'll hand it over.",
                "Good rope. It'll hold a man's weight easy. ...What? It's a compliment to the rope.",
            },
            auto = "0101000",
            end = "0010111",
            choices = new[]
            {
                new ChoiceSpec
                {
                    at = 1,
                    options = new[] { "Easy money.", "Fetch it yourself." },
                    next = new[] { 2, 3 },
                    gives = "10",
                },
                new ChoiceSpec
                {
                    at = 3,
                    options = new[] { "Fine.", "No." },
                    next = new[] { 2, 4 },
                    gives = "10",
                },
            },
            inProgress = 5, completed = 6, questId = "tut_rope",
        },
        new DialogueSpec
        {
            asset = "MottDialogue", npcName = "Brother Mott", sprite = "npc3_1", pitch = 0.97f,
            lines = new[]
            {
                "Forgive them. And me, for the company I keep.",
                "The stone past the gate. Does anyone tend it? I only ask because... no. Never mind. Old habits of a curious novice.",
                "Walk in light, friend. While it's offered.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "MoseDialogue", npcName = "Mose", sprite = "npc4_6", pitch = 0.8f,
            lines = new[]
            {
                "Mind the edge. She's fresh.",
                "Graves is like bread. You want one ready before you're hungry.",
                "The heap's mine but the ground's everyone's. Turn a spade if your back itches. Odd things come up, this soil.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "CressDialogue", npcName = "Widow Cress", sprite = "npc2_7", pitch = 1f,
            lines = new[]
            {
                "The flowers drink more than I do, and I'm no stranger to thirst.",
                "The well talks in dry summers. Ask anyone old enough to lie about it.",
                "My jug's on the sill if your arms are young. The walk to the well gets longer every year.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "TamDialogue", npcName = "Fisher Tam", sprite = "npc1_7", pitch = 1.02f,
            lines = new[]
            {
                "Shh. Not for the fish. For the herons. They judge.",
                "Net's fouled on a snag off the bank. Third time this season. Creek keeps what it likes.",
                "Free it if you're wading anyway. I'll owe you a fish I haven't caught.",
            },
            auto = "000",
            end = "001",
        },
        new DialogueSpec
        {
            asset = "BeggarDialogue", npcName = "The man at the gate", sprite = "npc4_8", pitch = 0.93f,
            lines = new[]
            {
                "No name worth giving, friend. Names cost more than coppers out here.",
                "The bowl takes what the day allows. The gate takes everyone, eventually.",
                "Eastway? Then you'll pass the stone. Nod to it. Costs nothing. Might be worth something.",
            },
            auto = "000",
            end = "001",
        },
    };

    // ── Entry point ──────────────────────────────────────────────────────
    [MenuItem("Dungeon Core/Generate Tutorial Content")]
    public static void Generate()
    {
        List<string> errors = ValidateSpecs();
        if (errors.Count > 0)
        {
            foreach (string e in errors)
                Debug.LogError("TutorialContentGenerator: " + e);
            Debug.LogError($"TutorialContentGenerator: aborted, {errors.Count} validation error(s). Nothing was written.");
            return;
        }

        EnsureFolder(DialogueFolder);
        EnsureFolder(QuestFolder);
        EnsureFolder(ItemFolder);

        CreateItemVariants();
        Dictionary<string, int> itemIds = RegisterItemsAndNormaliseIds();
        if (itemIds == null) return;

        Dictionary<string, Quest> quests = CreateQuests(itemIds);
        if (quests == null) return;

        CreateDialogues(quests);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string idTable = string.Join("\n", itemIds.OrderBy(kv => kv.Value)
            .Select(kv => $"  {kv.Value,2}  {kv.Key}"));
        Debug.Log($"TutorialContentGenerator: done. {Quests.Length} quests, {Dialogues.Length} dialogues, " +
                  $"{Items.Length} item variants.\nItem IDs (dictionary order):\n{idTable}");
    }

    // ── Validation ───────────────────────────────────────────────────────
    private static List<string> ValidateSpecs()
    {
        var errors = new List<string>();

        var itemNames = new HashSet<string>();
        foreach (ItemSpec item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.name))
                errors.Add("Item with empty name.");
            else if (!itemNames.Add(item.name))
                errors.Add($"Duplicate item name '{item.name}'.");
        }

        var resolvableItems = new HashSet<string>(itemNames);
        foreach (string known in KnownExistingItems) resolvableItems.Add(known);

        var questIds = new HashSet<string>();
        foreach (QuestSpec q in Quests)
        {
            if (string.IsNullOrWhiteSpace(q.id) || !questIds.Add(q.id))
                errors.Add($"Quest id missing or duplicated: '{q.id}'.");
            if (!resolvableItems.Contains(q.itemName))
                errors.Add($"Quest '{q.id}': unknown objective item '{q.itemName}'.");
            if (q.amount < 1)
                errors.Add($"Quest '{q.id}': amount must be at least 1.");
            if (!string.IsNullOrEmpty(q.handInFlag) && !q.handInFlag.StartsWith("flag_"))
                errors.Add($"Quest '{q.id}': handInFlag '{q.handInFlag}' does not start with 'flag_'.");
            foreach (RewardSpec r in q.rewards)
                if (!resolvableItems.Contains(r.item))
                    errors.Add($"Quest '{q.id}': unknown reward item '{r.item}'.");
        }

        var assetNames = new HashSet<string>();
        foreach (DialogueSpec d in Dialogues)
        {
            string tag = $"Dialogue '{d.asset}'";
            if (string.IsNullOrWhiteSpace(d.asset) || !assetNames.Add(d.asset))
                errors.Add($"{tag}: asset name missing or duplicated.");
            int n = d.lines?.Length ?? 0;
            if (n == 0) { errors.Add($"{tag}: no lines."); continue; }
            if ((d.auto + d.end).Any(c => c != '0' && c != '1'))
                errors.Add($"{tag}: auto/end must contain only '0' and '1'.");
            if (d.auto == null || d.auto.Length != n || d.end == null || d.end.Length != n)
            {
                errors.Add($"{tag}: auto ({d.auto?.Length ?? 0}) and end ({d.end?.Length ?? 0}) must both match {n} lines.");
                continue; // index checks below need well-formed arrays
            }

            bool hasQuest = !string.IsNullOrEmpty(d.questId);
            if (hasQuest)
            {
                if (d.questId != "AXE" && !questIds.Contains(d.questId))
                    errors.Add($"{tag}: unknown questId '{d.questId}'.");
                if (d.inProgress < 0 || d.inProgress >= n || d.end[d.inProgress] != '1')
                    errors.Add($"{tag}: inProgress must point at an end line.");
                if (d.completed < 0 || d.completed >= n || d.end[d.completed] != '1')
                    errors.Add($"{tag}: completed must point at an end line.");
            }

            bool anyGives = false;
            foreach (ChoiceSpec c in d.choices)
            {
                if (c.at < 0 || c.at >= n) { errors.Add($"{tag}: choice line {c.at} out of range."); continue; }
                if (d.end[c.at] == '1')
                    errors.Add($"{tag}: choice sits on end line {c.at} (end wins, choices never show).");
                if (d.auto[c.at] != '1')
                    errors.Add($"{tag}: choice line {c.at} should auto-progress so the options appear without an extra press.");
                int opts = c.options?.Length ?? 0;
                if (opts == 0 || c.next == null || c.next.Length != opts || c.gives == null || c.gives.Length != opts)
                    errors.Add($"{tag}: choice at {c.at} has mismatched options/next/gives lengths.");
                else
                {
                    foreach (int nx in c.next)
                        if (nx < 0 || nx >= n)
                            errors.Add($"{tag}: choice at {c.at} jumps to {nx}, out of range.");
                    if (c.gives.Contains('1')) anyGives = true;
                }
            }
            if (anyGives && !hasQuest)
                errors.Add($"{tag}: a choice gives a quest but no questId is set.");
        }

        return errors;
    }

    // ── Items ────────────────────────────────────────────────────────────
    private static void CreateItemVariants()
    {
        GameObject baseItem = AssetDatabase.LoadAssetAtPath<GameObject>(BaseItemPath);
        if (baseItem == null)
        {
            Debug.LogError($"TutorialContentGenerator: base item prefab missing at {BaseItemPath}.");
            return;
        }

        foreach (ItemSpec spec in Items)
        {
            string path = $"{ItemFolder}/{spec.name.Replace(" ", "")}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseItem);
            instance.name = spec.name.Replace(" ", "");
            instance.GetComponent<Item>().Name = spec.name;
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Appends any missing tutorial items to the ItemDictionary prefab list,
    /// then writes each listed prefab's serialised ID to match its list
    /// position. Append-only: existing entries are never reordered or removed
    /// (list position defines the item ID everywhere).
    /// </summary>
    private static Dictionary<string, int> RegisterItemsAndNormaliseIds()
    {
        GameObject dictRoot = PrefabUtility.LoadPrefabContents(ItemDictionaryPath);
        var dict = dictRoot.GetComponent<ItemDictionary>();
        if (dict == null)
        {
            Debug.LogError($"TutorialContentGenerator: no ItemDictionary component on {ItemDictionaryPath}.");
            PrefabUtility.UnloadPrefabContents(dictRoot);
            return null;
        }

        foreach (ItemSpec spec in Items)
        {
            if (dict.itemPrefabs.Any(p => p != null && p.Name == spec.name)) continue;

            string path = $"{ItemFolder}/{spec.name.Replace(" ", "")}.prefab";
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (variant == null)
            {
                Debug.LogError($"TutorialContentGenerator: expected item variant missing at {path}.");
                continue;
            }
            dict.itemPrefabs.Add(variant.GetComponent<Item>());
        }

        var ids = new Dictionary<string, int>();
        var pathsInOrder = new List<string>();
        for (int i = 0; i < dict.itemPrefabs.Count; i++)
        {
            Item entry = dict.itemPrefabs[i];
            if (entry == null) continue;
            ids[entry.Name] = i + 1;
            pathsInOrder.Add(AssetDatabase.GetAssetPath(entry));
        }

        PrefabUtility.SaveAsPrefabAsset(dictRoot, ItemDictionaryPath);
        PrefabUtility.UnloadPrefabContents(dictRoot);

        // Serialise the position-derived ID into each item prefab so pickups
        // placed in scenes carry the same ID the dictionary assigns at runtime.
        for (int i = 0; i < pathsInOrder.Count; i++)
        {
            string itemPath = pathsInOrder[i];
            if (string.IsNullOrEmpty(itemPath)) continue;
            GameObject itemRoot = PrefabUtility.LoadPrefabContents(itemPath);
            var comp = itemRoot.GetComponent<Item>();
            if (comp != null && comp.ID != i + 1)
            {
                comp.ID = i + 1;
                PrefabUtility.SaveAsPrefabAsset(itemRoot, itemPath);
            }
            PrefabUtility.UnloadPrefabContents(itemRoot);
        }

        return ids;
    }

    // ── Quests ───────────────────────────────────────────────────────────
    private static Dictionary<string, Quest> CreateQuests(Dictionary<string, int> itemIds)
    {
        var created = new Dictionary<string, Quest>();

        foreach (QuestSpec spec in Quests)
        {
            if (!itemIds.TryGetValue(spec.itemName, out int objectiveId))
            {
                Debug.LogError($"TutorialContentGenerator: quest '{spec.id}' item '{spec.itemName}' not in dictionary. Aborting quest pass.");
                return null;
            }

            string path = $"{QuestFolder}/{spec.id}.asset";
            var quest = AssetDatabase.LoadAssetAtPath<Quest>(path);
            if (quest == null)
            {
                quest = ScriptableObject.CreateInstance<Quest>();
                AssetDatabase.CreateAsset(quest, path);
            }

            quest.questID = spec.id;
            quest.questName = spec.title;
            quest.Description = spec.desc;
            quest.handInFlag = spec.handInFlag;
            quest.objectives = new List<QuestObjective>
            {
                new QuestObjective
                {
                    objectiveID = objectiveId.ToString(),
                    description = $"Collect {spec.amount}x {spec.itemName}",
                    type = ObjectiveType.CollectItem,
                    requiredAmount = spec.amount,
                    currentAmount = 0,
                },
            };
            quest.questRewards = new List<QuestReward>();
            foreach (RewardSpec r in spec.rewards)
            {
                if (!itemIds.TryGetValue(r.item, out int rewardId))
                {
                    Debug.LogError($"TutorialContentGenerator: quest '{spec.id}' reward '{r.item}' not in dictionary. Aborting quest pass.");
                    return null;
                }
                quest.questRewards.Add(new QuestReward
                {
                    type = RewardType.Item,
                    rewardID = rewardId,
                    amount = r.amount,
                });
            }

            EditorUtility.SetDirty(quest);
            created[spec.id] = quest;
        }

        return created;
    }

    // ── Dialogue ─────────────────────────────────────────────────────────
    private static void CreateDialogues(Dictionary<string, Quest> quests)
    {
        var voice = AssetDatabase.LoadAssetAtPath<AudioClip>(VoicePath);
        var axeQuest = AssetDatabase.LoadAssetAtPath<Quest>(AxeQuestPath);
        if (axeQuest == null)
            Debug.LogWarning($"TutorialContentGenerator: axe quest missing at {AxeQuestPath}; Harl's dialogue will have no quest.");

        foreach (DialogueSpec spec in Dialogues)
        {
            string path = $"{DialogueFolder}/{spec.asset}.asset";
            var dialogue = AssetDatabase.LoadAssetAtPath<NPCDialogue>(path);
            if (dialogue == null)
            {
                dialogue = ScriptableObject.CreateInstance<NPCDialogue>();
                AssetDatabase.CreateAsset(dialogue, path);
            }

            dialogue.npcName = spec.npcName;
            dialogue.npcPortrait = LoadPortrait(spec.sprite);
            dialogue.dialogueLines = spec.lines;
            dialogue.autoProgressLines = Bits(spec.auto);
            dialogue.endDialogueLines = Bits(spec.end);
            dialogue.autoProgressDelay = 1.5f;
            dialogue.typingSpeed = 0.05f;
            dialogue.voiceSound = voice;
            dialogue.voicePitch = spec.pitch;
            dialogue.questInProgressIndex = spec.inProgress;
            dialogue.questCompletedIndex = spec.completed;

            dialogue.choices = spec.choices.Select(c => new DialogueChoice
            {
                dialogueIndex = c.at,
                choices = c.options,
                nextDialogueIndexes = c.next,
                givesQuest = Bits(c.gives),
            }).ToArray();

            if (spec.questId == "AXE") dialogue.quest = axeQuest;
            else if (!string.IsNullOrEmpty(spec.questId)) dialogue.quest = quests[spec.questId];
            else dialogue.quest = null;

            EditorUtility.SetDirty(dialogue);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static bool[] Bits(string s)
    {
        var result = new bool[s.Length];
        for (int i = 0; i < s.Length; i++) result[i] = s[i] == '1';
        return result;
    }

    private static Sprite LoadPortrait(string spriteSet)
    {
        if (string.IsNullOrEmpty(spriteSet)) return null;
        string path = $"{CharacterRoot}/{spriteSet}/down_stand.png";
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (sprite == null)
            Debug.LogWarning($"TutorialContentGenerator: no sprite found at {path}.");
        return sprite;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string leaf = path.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}