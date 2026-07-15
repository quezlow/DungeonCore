using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The authored mapping between prologue deeds and core affinities, plus every
/// line the wisp speaks when reading a life back. Lives as an asset so weights
/// and copy are tunable without a recompile.
///
/// Scores are normalised - each affinity's score is the fraction of its own
/// flags earned - so two-flag affinities weigh the same as three-flag ones.
/// Kneeling at the old stone boosts whichever affinity already leads
/// (devotion sharpens identity; it never changes who leads). The egg flags
/// vote for nothing: they earn a teasing acknowledgment and nothing more,
/// until the hidden types exist.
///
/// Right-click the component header and choose Fill Canon Table to write the
/// signed-off defaults into a fresh asset.
/// </summary>
[CreateAssetMenu(fileName = "AffinityMapping", menuName = "Dungeon Core/Affinity Mapping")]
public class AffinityMapping : ScriptableObject
{
    [Serializable]
    public class Row
    {
        public DungeonType type;

        [Tooltip("Flags that count toward this affinity.")]
        public string[] flags;

        [Tooltip("The wisp's deed read-back when this affinity leads.")]
        [TextArea] public string readBack;

        [Tooltip("One-line identity shown under the option on the choice screen.")]
        public string identity;
    }

    public Row[] rows = new Row[0];

    [Tooltip("Added to the leading score(s) when flag_pray_shrine is set. Emphasis only.")]
    public float prayShrineBoost = 0.25f;

    [TextArea] public string prayShrineLine;
    [TextArea] public string emptyHandedLine;
    [TextArea] public string eggFossilLine;
    [TextArea] public string eggMillLine;

    /// <summary>The result of reading a life back.</summary>
    public class Tally
    {
        public Dictionary<DungeonType, float> scores = new Dictionary<DungeonType, float>();
        public List<DungeonType> leaders = new List<DungeonType>();
        public bool prayed;
        public bool fossil;
        public bool mill;
        public bool emptyHanded;
    }

    public Tally Evaluate(IReadOnlyCollection<string> flags)
    {
        var set = new HashSet<string>(flags);
        var tally = new Tally
        {
            prayed = set.Contains(TutorialFlags.PrayShrine),
            fossil = set.Contains(TutorialFlags.FossilDelivered),
            mill = set.Contains(TutorialFlags.RepairMill),
        };

        float max = 0f;
        foreach (Row row in rows)
        {
            if (row == null || row.flags == null || row.flags.Length == 0) continue;

            int earned = 0;
            foreach (string flag in row.flags)
                if (set.Contains(flag)) earned++;

            float score = (float)earned / row.flags.Length;
            tally.scores[row.type] = score;
            if (score > max) max = score;
        }

        tally.emptyHanded = max <= 0f;

        if (!tally.emptyHanded)
        {
            foreach (var pair in tally.scores)
                if (pair.Value >= max - 0.0001f)
                    tally.leaders.Add(pair.Key);

            // Devotion sharpens whoever already leads - emphasis, never a coup.
            if (tally.prayed)
                foreach (DungeonType leader in tally.leaders)
                    tally.scores[leader] += prayShrineBoost;
        }

        return tally;
    }

    public Row RowFor(DungeonType type)
    {
        foreach (Row row in rows)
            if (row != null && row.type == type) return row;
        return null;
    }

    [ContextMenu("Fill Canon Table")]
    private void FillCanonTable()
    {
        rows = new[]
        {
            new Row
            {
                type = DungeonType.Fire,
                flags = new[] { TutorialFlags.Bellows, TutorialFlags.Quench },
                readBack = "You worked the forge and did not flinch.",
                identity = "Temper and hunger. Everything is fuel.",
            },
            new Row
            {
                type = DungeonType.Water,
                flags = new[] { TutorialFlags.DrawWell, TutorialFlags.FillJug, TutorialFlags.FreeNet },
                readBack = "You went toward the water when others would not.",
                identity = "Patience that wears down mountains.",
            },
            new Row
            {
                type = DungeonType.Air,
                flags = new[] { TutorialFlags.MillClimb, TutorialFlags.FreePigeon },
                readBack = "You climbed for the view, and freed what was caught.",
                identity = "Nothing holds what will not be held.",
            },
            new Row
            {
                type = DungeonType.Earth,
                flags = new[] { TutorialFlags.DigGrave, TutorialFlags.DigRow, TutorialFlags.HaulStones },
                readBack = "You turned the ground with your own hands.",
                identity = "Deep, and slow, and certain.",
            },
            new Row
            {
                type = DungeonType.Light,
                flags = new[] { TutorialFlags.HelpHealer, TutorialFlags.LightCandle, TutorialFlags.GiveAlms },
                readBack = "You mended more than you broke.",
                identity = "A flame kept burning for others.",
            },
            new Row
            {
                type = DungeonType.Dark,
                flags = new[] { TutorialFlags.SmashCrates, TutorialFlags.TakeOffering },
                readBack = "You took what was watched, and broke what was stacked.",
                identity = "What is taken in the dark, stays.",
            },
        };

        prayShrineBoost = 0.25f;
        prayShrineLine = "And you knelt at the old stone. The deep remembers its own - it is part of why you are here at all.";
        emptyHandedLine = "You came down empty-handed. That is its own kind of freedom - choose as you will.";
        eggFossilLine = "And something older stirs at the edge of you - bone that predates the parish. Not yet. Not tonight.";
        eggMillLine = "Somewhere above, a machine remembers your hands. Keep that. It will matter.";
    }
}