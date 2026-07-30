using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the wisp's urging assets (Quest ScriptableObjects) under
/// Resources/Quests/Wisp so the QuestRegistry self-populates. List-driven and
/// idempotent: re-running overwrites the authored fields on existing assets.
/// Menu: Dungeon Core -> Generate Wisp Quests.
/// </summary>
public static class WispQuestContentGenerator
{
    private struct Spec
    {
        public string id, name, desc, objId, objDesc;
        public int req, xp, gold;
    }

    private static readonly Spec[] specs =
    {
        new Spec { id = "wq_carve", name = "Shape the Dark",
            desc = "Carve a chamber, not a worm-run: a pocket of mined ground at least three across, clear of your standing rooms.",
            objId = "obj.carve_chamber", objDesc = "Mine a 3x3 pocket outside existing rooms",
            req = 1, xp = 15, gold = 0 },
        new Spec { id = "wq_journal", name = "Keeper of the Ledger",
            desc = "Everything the core is asked, and everything it has done, settles into the ledger. Open it, and look upon the Deeds page.",
            objId = "obj.journal_open", objDesc = "Open the ledger",
            req = 1, xp = 10, gold = 0 },
        new Spec { id = "wq_research", name = "Roots of Knowledge",
            desc = "The tree remembers more than alarums. Learn any further branch of it.",
            objId = "obj.first_research", objDesc = "Complete a research node beyond the opening grants",
            req = 1, xp = 20, gold = 0 },
        new Spec { id = "wq_pattern", name = "The Remembered Grain",
            desc = "Materials carry memory. Take a pattern into the codex - dig strange stone, or read for it in a Library.",
            objId = "obj.first_pattern", objDesc = "Learn your first material pattern",
            req = 1, xp = 15, gold = 25 },
        new Spec { id = "wq_muster", name = "A Standing Army",
            desc = "One companion is a start, not a garrison. Muster more: spawners belong inside rooms fit for their kind.",
            objId = "obj.armed_spawners", objDesc = "Keep two spawners armed",
            req = 2, xp = 20, gold = 0 },
        new Spec { id = "wq_traps", name = "Teeth in the Dark",
            desc = "Let the halls themselves bite. Set traps where uninvited feet must fall.",
            objId = "obj.placed_traps", objDesc = "Place two traps",
            req = 2, xp = 15, gold = 0 },
        new Spec { id = "wq_tier2", name = "Provisioned Halls",
            desc = "A room grows with gold. Raise any room to its second tier.",
            objId = "obj.room_tier2", objDesc = "Upgrade a room to tier 2",
            req = 1, xp = 20, gold = 50 },
        new Spec { id = "wq_capture", name = "Iron Hospitality",
            desc = "Not every intruder need die at the threshold. Take one alive, and hold them.",
            objId = "obj.hold_captive", objDesc = "Hold a living captive",
            req = 1, xp = 25, gold = 0 },
        new Spec { id = "wq_notoriety", name = "Word Below",
            desc = "Let them speak your name in the taverns above. Notoriety brings bolder blood - and richer.",
            objId = "obj.notoriety", objDesc = "Reach 25 notoriety",
            req = 1, xp = 15, gold = 0 },
        new Spec { id = "wq_floor1", name = "The Deep Road",
            desc = "The dark goes down. Cut stairs, and open a second floor beneath the first.",
            objId = "obj.first_descent", objDesc = "Establish floor 1",
            req = 1, xp = 25, gold = 75 },
    };

    [MenuItem("Dungeon Core/Generate Wisp Quests")]
    public static void Generate()
    {
        const string dir = "Assets/Resources/Quests/Wisp";
        Directory.CreateDirectory(dir);

        foreach (var s in specs)
        {
            string assetPath = dir + "/" + s.id + ".asset";
            var q = AssetDatabase.LoadAssetAtPath<Quest>(assetPath);
            if (q == null)
            {
                q = ScriptableObject.CreateInstance<Quest>();
                AssetDatabase.CreateAsset(q, assetPath);
            }

            q.questID = s.id;
            q.questName = s.name;
            q.Description = s.desc;
            q.objectives = new System.Collections.Generic.List<QuestObjective>
            {
                new QuestObjective { objectiveID = s.objId, description = s.objDesc,
                    type = ObjectiveType.Custom, requiredAmount = s.req }
            };
            if (s.id == "wq_journal")
                q.objectives.Add(new QuestObjective { objectiveID = "obj.deeds_view",
                    description = "Turn to the Deeds page",
                    type = ObjectiveType.Custom, requiredAmount = 1 });

            q.questRewards = new System.Collections.Generic.List<QuestReward>
            {
                new QuestReward { type = RewardType.Experience, amount = s.xp }
            };
            if (s.gold > 0)
                q.questRewards.Add(new QuestReward { type = RewardType.Gold, amount = s.gold });

            EditorUtility.SetDirty(q);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WispQuestContentGenerator] Authored " + specs.Length
            + " urgings under " + dir + ".");
    }
}
