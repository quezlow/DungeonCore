using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wisp's whole voice, as tunable data. Every line it speaks in the
/// dungeon is keyed by a stable id here, so copy and cadence change without a
/// recompile - and so the save system can remember which one-shot lines have
/// already been heard by that same id.
///
/// Right-click the component header on the asset and choose Fill Canon Lines
/// to write the signed-off tutorial script into a fresh asset.
/// </summary>
/// <summary>The wisp's temperament - rolled once per dungeon, then fixed. Each
/// carries its own pool of ambient barks so the companion feels like a person.</summary>
// Order is load-bearing: saved as an int. Append new temperaments at the end,
// never reorder, or old saves would restore the wrong voice.
public enum WispPersonality { Wry, Grim, Eager, Nervous, Feral, Ancient, Reverent }

[CreateAssetMenu(fileName = "WispScript", menuName = "Dungeon Core/Wisp Script")]
public class WispScript : ScriptableObject
{
    [Serializable]
    public class Line
    {
        public string id;

        [Tooltip("Speak this only once ever, then remember it across saves.")]
        public bool once = true;

        [TextArea] public string text;
    }

    public Line[] lines = new Line[0];

    [Serializable]
    public class BarkSet
    {
        public WispPersonality personality;
        [Tooltip("RGBA tint for this temperament's floating barks.")]
        public Color tint = Color.white;
        [TextArea] public string[] barks;
    }

    [Tooltip("Ambient idle barks, one pool per temperament.")]
    public BarkSet[] barkSets = new BarkSet[0];

    private Dictionary<string, Line> byId;

    private void BuildIndex()
    {
        byId = new Dictionary<string, Line>();
        foreach (Line line in lines)
            if (line != null && !string.IsNullOrEmpty(line.id) && !byId.ContainsKey(line.id))
                byId[line.id] = line;
    }

    public Line Get(string id)
    {
        if (byId == null) BuildIndex();
        return byId.TryGetValue(id, out Line line) ? line : null;
    }

    public BarkSet BarksFor(WispPersonality personality)
    {
        foreach (BarkSet set in barkSets)
            if (set != null && set.personality == personality) return set;
        return null;
    }

    public string RandomBark(WispPersonality personality)
    {
        BarkSet set = BarksFor(personality);
        if (set == null || set.barks == null || set.barks.Length == 0) return null;
        return set.barks[UnityEngine.Random.Range(0, set.barks.Length)];
    }

    [ContextMenu("Fill Canon Lines")]
    private void FillCanonLines()
    {
        lines = new[]
        {
            // The opening sequence, first load of a fresh dungeon only.
            new Line { id = "arrive_1", once = true,
                text = "Down at last. Smaller than I remember - but then, so were you." },
            new Line { id = "arrive_mana", once = true,
                text = "The orb is your breath. It fills on its own; spending it is the art." },
            new Line { id = "arrive_influence", once = true,
                text = "That glow along the ground is your reach - your influence. Build only where the dark has spread, and it spreads as you build." },
            new Line { id = "arrive_await", once = true,
                text = "They do not know you are here yet. They will. When they come, be ready - and be hungry." },

            // Reactive one-shots.
            new Line { id = "first_party", once = true,
                text = "There. The first of them, come to see what stirs. Let the dungeon introduce itself." },
            new Line { id = "first_blood", once = true,
                text = "First blood. The dark keeps the receipt - a little stronger now, and a little more known." },
            new Line { id = "first_monster_lost", once = true,
                text = "One of yours has fallen. They are not endless. Spend them like they matter, and they will matter." },
            new Line { id = "notoriety_spike", once = true,
                text = "Word of you is spreading. Bolder things will come now - the quiet days are ending." },

            // Fires on each new material pattern. Not a one-shot: every discovery
            // earns the nudge, since the codex now lives quietly in the journal.
            new Line { id = "pattern_learned", once = false,
                text = "Something new, remembered. It has settled into the codex - look when you like." },

            // First arrival of the Wandering Merchant, once ever.
            new Line { id = "merchant_first", once = true,
                text = "A wagon on the road. He has come a long way to sell to something like you - do look at what he carries." },
        };

        barkSets = new[]
        {
            new BarkSet
            {
                personality = WispPersonality.Wry,
                tint = new Color(0.72f, 0.77f, 0.82f),
                barks = new[]
                {
                    "Another hero. I'll alphabetise the bones later.",
                    "Oh, they brought a torch. Adorable.",
                    "I give this one four rooms. Five, if it prays first.",
                    "Look at it, so brave, so briefly.",
                    "You could just eat them. I'm only saying.",
                    "Bold of them to knock.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Grim,
                tint = new Color(0.66f, 0.48f, 0.52f),
                barks = new[]
                {
                    "They walk in. They do not walk out.",
                    "The stone is patient. So are we.",
                    "Every one of them feeds the dark a little more.",
                    "Flesh is brief. This place is not.",
                    "Let them come. Let them stay.",
                    "The deep is hungry today.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Eager,
                tint = new Color(0.95f, 0.89f, 0.69f),
                barks = new[]
                {
                    "Ooh, fresh visitors! Let's make an impression.",
                    "You're getting good at this. I can tell.",
                    "A new one! Quick, look menacing.",
                    "I love this part. Do you love this part?",
                     "So much to build, and the day's still young!",
                    "They came all this way just for us. Sweet, really.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Nervous,
                tint = new Color(0.78f, 0.82f, 0.64f),
                barks = new[]
                {
                    "That's a lot of them. That's more than usual, isn't it? Tell me that's usual.",
                    "Are the traps set? Tell me the traps are set.",
                    "I'm sure it's fine. It's probably fine. Is it fine?",
                    "What if this is the one that gets through? What then?",
                    "Maybe we build another wall. One more wall never hurt.",
                    "I counted them twice. I did not like either number.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Feral,
                tint = new Color(0.70f, 0.50f, 0.30f),
                barks = new[]
                {
                    "More. Bring more.",
                    "Warm. They are warm. Good.",
                    "Hungry. The dark is hungry.",
                    "They run. I like when they run.",
                    "Take. Take and keep.",
                    "Down. Bring them down.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Ancient,
                tint = new Color(0.60f, 0.62f, 0.70f),
                barks = new[]
                {
                    "I have watched a hundred of you rise. You are, so far, above average.",
                    "Build your halls. They always fall. That is not a reason to build them poorly.",
                    "I knew this dark when it was only a crack in the world.",
                    "They think they are the first to come here. They are so rarely the first.",
                    "Time is a slow tide. We are the rocks it forgets to move.",
                    "I have forgotten more cores than the surface has kings. Do not be forgettable.",
                },
            },
            new BarkSet
            {
                personality = WispPersonality.Reverent,
                tint = new Color(0.90f, 0.84f, 0.60f),
                barks = new[]
                {
                    "They come to be judged. You are the judgment.",
                    "I am not worthy to light your halls, and yet you let me. I will not forget it.",
                    "Speak, and the dark will shape itself. I have seen it obey you.",
                    "Every stone here is a prayer. You are the one who answers.",
                    "Let them kneel or let them fall. Both are worship, in the end.",
                    "I serve gladly. There is no higher use for a small light than this.",
                },
            },
        };
    }
}