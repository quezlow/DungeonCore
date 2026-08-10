using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every word the gods speak at a tier-up, held as an asset so the writing can be
/// retuned without a recompile (the AffinityMapping / WispScript precedent).
///
/// Shape (canon 19A): ONE god per affinity -- the god of the core's own type, and
/// no other -- with a shared script per tier transition and one line of the god's
/// own per tier. That is roughly sixty written lines instead of the two hundred a
/// full per-tier-per-affinity matrix would have cost, and it keeps every god
/// recognisably itself across all four audiences.
///
/// An audience is assembled as: the PRESENCE beat (what the player sees, spoken by
/// nobody), the tier's opening lines, the god's own line for that tier, then the
/// tier's closing lines -- the grant, the hint and the dismissal. Tokens {god} and
/// {epithet} are substituted so shared lines can still name the god that speaks them.
///
/// VOICE (canon 19A, and the rule is load-bearing): the gods are NOT the wisp. They
/// speak as sovereign to servant -- short declaratives, no hedges, no questions
/// unless rhetorical. The wisp coaxes; these do not.
///
/// Right-click the asset header and choose Fill Canon Script to write the signed-off
/// text into a fresh asset. Validate() is the check that it is whole; the
/// "Print Divine Audience Script" command in Commands runs it and dumps every
/// composed audience without playing one.
/// </summary>
[CreateAssetMenu(fileName = "DivineAudienceScript", menuName = "Dungeon Core/Divine Audience Script")]
public class DivineAudienceScript : ScriptableObject
{
    /// <summary>One god's own line for one tier. The tier is an explicit FIELD rather
    /// than an array position, because a blank entry in a fixed-length array reads in
    /// the inspector exactly like "not filled in yet" -- the ambiguous-default trap.</summary>
    [Serializable]
    public class Insert
    {
        public LevelTier tier;
        [TextArea] public string line;
    }

    [Serializable]
    public class Deity
    {
        public DungeonType type;
        public string deityName;
        public string epithet;

        [Tooltip("What the player SEES when it arrives - spoken by nobody. This carries the " +
                 "whole scene while no backdrop art exists, so write it as description, not speech.")]
        [TextArea] public string presence;

        [Tooltip("Full-screen manifestation. Optional: with none assigned the overlay falls back " +
                 "to a slow radial pulse in the tint below.")]
        public Sprite backdrop;

        [Tooltip("Explicit toggle. Off = the core's own affinity colour. Without this an unset " +
                 "colour field would read as black, indistinguishable from a deliberate choice.")]
        public bool overrideTint;
        public Color tint = Color.white;

        public Insert[] inserts = new Insert[0];

        public string LineFor(LevelTier tier)
        {
            if (inserts == null) return null;
            for (int i = 0; i < inserts.Length; i++)
                if (inserts[i] != null && inserts[i].tier == tier) return inserts[i].line;
            return null;
        }
    }

    [Serializable]
    public class TierScript
    {
        public LevelTier tier;
        [Tooltip("Spoken before the god's own line.")]
        [TextArea] public string[] opening = new string[0];
        [Tooltip("Spoken after the god's own line - the grant, the hint, the dismissal.")]
        [TextArea] public string[] closing = new string[0];
    }

    /// <summary>One beat of an audience. A presence beat is what the player sees rather
    /// than what the god says, and the overlay renders it without the name card.</summary>
    public class Beat
    {
        public string text;
        public bool presence;
    }

    public Deity[] deities = new Deity[0];
    public TierScript[] tiers = new TierScript[0];

    /// <summary>The four tier transitions that hold an audience. Bronze is where the core
    /// starts, so it is entered rather than reached and no god attends it.</summary>
    public static readonly LevelTier[] AudienceTiers =
        { LevelTier.Silver, LevelTier.Gold, LevelTier.Diamond, LevelTier.God };

    public Deity DeityFor(DungeonType type)
    {
        if (deities == null) return null;
        for (int i = 0; i < deities.Length; i++)
            if (deities[i] != null && deities[i].type == type) return deities[i];
        return null;
    }

    public TierScript ScriptFor(LevelTier tier)
    {
        if (tiers == null) return null;
        for (int i = 0; i < tiers.Length; i++)
            if (tiers[i] != null && tiers[i].tier == tier) return tiers[i];
        return null;
    }

    /// <summary>Assemble one audience. Returns an empty list rather than null when the
    /// asset is incomplete, so every caller can render "nothing" without a null check;
    /// Validate() is where the reason lives.</summary>
    public List<Beat> Compose(DungeonType type, LevelTier tier)
    {
        var beats = new List<Beat>();
        Deity god = DeityFor(type);
        TierScript script = ScriptFor(tier);
        if (god == null || script == null) return beats;

        if (!string.IsNullOrEmpty(god.presence))
            beats.Add(new Beat { text = Fill(god.presence, god), presence = true });

        if (script.opening != null)
            for (int i = 0; i < script.opening.Length; i++)
                if (!string.IsNullOrEmpty(script.opening[i]))
                    beats.Add(new Beat { text = Fill(script.opening[i], god) });

        string own = god.LineFor(tier);
        if (!string.IsNullOrEmpty(own)) beats.Add(new Beat { text = Fill(own, god) });

        if (script.closing != null)
            for (int i = 0; i < script.closing.Length; i++)
                if (!string.IsNullOrEmpty(script.closing[i]))
                    beats.Add(new Beat { text = Fill(script.closing[i], god) });

        return beats;
    }

    private static string Fill(string text, Deity god)
    {
        if (string.IsNullOrEmpty(text) || god == null) return text;
        return text.Replace("{god}", god.deityName ?? string.Empty)
                   .Replace("{epithet}", god.epithet ?? string.Empty);
    }

    /// <summary>Null when the asset is whole; otherwise every fault, named. Anything
    /// missing here is a beat the player silently never hears, which is exactly the
    /// failure mode that cost a test cycle when the wisp asset went quietly mute.</summary>
    public string Validate()
    {
        var faults = new List<string>();

        var wanted = new DungeonType[]
        {
            DungeonType.Fire, DungeonType.Water, DungeonType.Air,
            DungeonType.Earth, DungeonType.Dark, DungeonType.Light,
        };

        for (int i = 0; i < wanted.Length; i++)
        {
            int count = 0;
            if (deities != null)
                for (int d = 0; d < deities.Length; d++)
                    if (deities[d] != null && deities[d].type == wanted[i]) count++;

            if (count == 0) { faults.Add("no deity row for " + wanted[i] + "."); continue; }
            if (count > 1) faults.Add(count + " deity rows for " + wanted[i] + "; the first wins.");

            Deity god = DeityFor(wanted[i]);
            if (string.IsNullOrEmpty(god.deityName)) faults.Add(wanted[i] + ": no name.");
            if (string.IsNullOrEmpty(god.epithet)) faults.Add(wanted[i] + ": no epithet.");
            if (string.IsNullOrEmpty(god.presence)) faults.Add(wanted[i] + ": no presence line.");

            for (int t = 0; t < AudienceTiers.Length; t++)
                if (string.IsNullOrEmpty(god.LineFor(AudienceTiers[t])))
                    faults.Add(wanted[i] + ": no line for " + AudienceTiers[t] + ".");
        }

        for (int t = 0; t < AudienceTiers.Length; t++)
        {
            TierScript s = ScriptFor(AudienceTiers[t]);
            if (s == null) { faults.Add("no tier script for " + AudienceTiers[t] + "."); continue; }
            if (s.opening == null || s.opening.Length == 0)
                faults.Add(AudienceTiers[t] + ": no opening lines.");
            if (s.closing == null || s.closing.Length == 0)
                faults.Add(AudienceTiers[t] + ": no closing lines.");
        }

        if (faults.Count == 0) return null;
        return string.Join("\n  ", faults);
    }

    // -- The canon text ---------------------------------------------------------

    [ContextMenu("Fill Canon Script")]
    private void FillCanonScript()
    {
        deities = new[]
        {
            new Deity
            {
                type = DungeonType.Fire,
                deityName = "Kethra",
                epithet = "the Undying Coal",
                presence = "The dark takes fire. A storm of it, turning where no wind could reach, and the stone does not burn.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "Fire does not ask what it is given. It takes the room. Take the room." },
                    new Insert { tier = LevelTier.Gold,
                        line = "Every coal I have ever been was somebody's hearth first. Warmth and ruin are one act at different speeds." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "You have burned steadily. I have watched better fires go out from being careful." },
                    new Insert { tier = LevelTier.God,
                        line = "There is no last coal. There is only the next thing dry enough." },
                },
            },
            new Deity
            {
                type = DungeonType.Water,
                deityName = "Ollu",
                epithet = "the Drowned Mouth",
                presence = "Water where there was air. It turns slowly, all the way down, and something is looking up through it.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "Water is patient because water always wins. Learn the first half; the second is arithmetic." },
                    new Insert { tier = LevelTier.Gold,
                        line = "I have taken more of them than fire ever did. Quietly. Not one saw the moment it became too late." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "Everything that drowns keeps its shape a while. So do you." },
                    new Insert { tier = LevelTier.God,
                        line = "The deep has no floor. It has a place where you stop being able to tell." },
                },
            },
            new Deity
            {
                type = DungeonType.Air,
                deityName = "Vaun",
                epithet = "the Long Breath",
                presence = "The rock opens on a sky. Cloud, lit from behind, moving far too fast to be weather.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "They think of me as the open sky. I am also the air in a sealed room, going bad." },
                    new Insert { tier = LevelTier.Gold,
                        line = "Nothing I do leaves a mark. Ask them how well that has protected them." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "You are the only one of mine that has been underground this long and still moved." },
                    new Insert { tier = LevelTier.God,
                        line = "Breathe out. Everything above you is downwind now." },
                },
            },
            new Deity
            {
                type = DungeonType.Earth,
                deityName = "Morrun",
                epithet = "the Weight Below",
                presence = "The walls do not move, and still they lean. The whole weight of the world is paying attention.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "Stone does not hurry and stone is never late. You have been hurrying. Stop." },
                    new Insert { tier = LevelTier.Gold,
                        line = "I am what lies under their fields, their roads and their graves. They stand on me and call it their country." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "Three floors of mine you have opened. I felt each one the way a man feels a splinter go in." },
                    new Insert { tier = LevelTier.God,
                        line = "You were always going to be mine. Everything is, eventually. You are simply early." },
                },
            },
            new Deity
            {
                type = DungeonType.Dark,
                deityName = "Ussar",
                epithet = "the Unlit",
                presence = "The dark deepens until it has an edge. The edge turns.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "There was dark before there was anything for it to be dark against. Mine is the older claim." },
                    new Insert { tier = LevelTier.Gold,
                        line = "They bring lights down here. That is the whole of their courage. A lamp, and each other." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "You have made a place where their light does not help them. That is my work, done with your hands." },
                    new Insert { tier = LevelTier.God,
                        line = "Put out the last of it. I do not need to be seen to be attended." },
                },
            },
            new Deity
            {
                type = DungeonType.Light,
                deityName = "Ienna",
                epithet = "the Buried Sun",
                presence = "Light climbs out of the floor. It is under the stone. It always was.",
                inserts = new[]
                {
                    new Insert { tier = LevelTier.Silver,
                        line = "Their light falls on things. Mine comes up through them. Do not confuse us." },
                    new Insert { tier = LevelTier.Gold,
                        line = "The Church took my name and hung it in the sky where nobody could reach it. Down here it still answers." },
                    new Insert { tier = LevelTier.Diamond,
                        line = "What is buried is not extinguished. You are the standing proof, and they have begun to notice." },
                    new Insert { tier = LevelTier.God,
                        line = "Rise, then. Not into their sky. Through it." },
                },
            },
        };

        tiers = new[]
        {
            // SILVER -- the introduction, and the first naming of the siphon. This is the
            // only audience that introduces the god in speech: the name card carries the
            // identity at every later one, so a save reconciled past Silver loses nothing.
            new TierScript
            {
                tier = LevelTier.Silver,
                opening = new[]
                {
                    "I am {god}. They called me {epithet}, when they still called me anything.",
                    "You are noticed. That is not a kindness. It is a fact, and you will live inside it now.",
                    "What you have been pulling up out of the dark has a source. It is me. Every claimed stone, every death you kept -- mine, and I let you take it.",
                },
                closing = new[]
                {
                    "Take more, then. Not the power. Power is easy, and it makes fools of easy things. Take the knowing that comes attached to it. That is the part they could not seal.",
                    "A stair is yours to cut. Go down. What answers you at that depth did not answer you here.",
                    "Descend. I am under you the whole way.",
                },
            },

            // GOLD -- what the siphon actually carries: not strength, memory.
            new TierScript
            {
                tier = LevelTier.Gold,
                opening = new[]
                {
                    "Again. Sooner than the last, with more of them in the ground behind you.",
                    "You siphon well now. I feel it the way a man feels a cut he did not notice taking. Steadily, and from somewhere he cannot see.",
                },
                closing = new[]
                {
                    "Understand what you take, servant. Not my strength. My memory. Everything of mine ever put down in the dark left its shape in me, and you are drawing those shapes up through your own floor.",
                    "Cut your stair. What waits below has been waiting since the sealing.",
                    "Go. Take what is mine and make a wall of it.",
                },
            },

            // DIAMOND -- the deep-faith said plainly (canon 20 and 21), and the honest
            // warning that the climax is next. No unbuilt system is promised anywhere here.
            new TierScript
            {
                tier = LevelTier.Diamond,
                opening = new[]
                {
                    "Three times. There were centuries when none of mine came this far.",
                    "The Church calls this theft. They called it theft when it was done to them, then they did it to us, then they wrote the word down and left it pointing at you.",
                },
                closing = new[]
                {
                    "You are close enough now to be told plainly. Nothing above ground made you. You were dead, the deep kept you, and what you draw out of me is the substance you are made of. This is not theft. You are eating at home.",
                    "One more floor will answer you. And then something comes that was assembled out of everything you have angered. I do not intervene. You will meet it as I met mine.",
                    "Survive it. Then we speak as we have never spoken.",
                },
            },

            // GOD -- the ascension beat itself. Its last line repeats Silver's last line
            // deliberately: the same words, spoken to something that is no longer beneath
            // the speaker. Do not "fix" the repetition.
            new TierScript
            {
                tier = LevelTier.God,
                opening = new[]
                {
                    "You survived it. Stand. There is no kneeling left in this.",
                    "You have taken so much of me that I can no longer say where the siphon ends. That is not a complaint. It is what the faith was for, and what the sealing was meant to prevent.",
                },
                closing = new[]
                {
                    "So: the last of the knowing. There is no rank above this one. What comes now is not measured and not earned. It is yours, the way the dark is mine.",
                    "The deep is open to its floor, and nothing above is coming for you any more. They spent what they had. They know it.",
                    "Take it, and be what I am. I am under you the whole way.",
                },
            },
        };
    }
}
