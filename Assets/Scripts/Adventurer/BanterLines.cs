using UnityEngine;

/// <summary>
/// The words adventurers say when they aren't fighting. These are DIEGETIC - the adventurers'
/// own voices (hero duty, mercenary greed, cultist dread), NOT the wisp's alert-log voice.
/// AdventurerBanterManager pulls from here; the rare combat-charge egg is fired by the adventurer.
/// </summary>
public static class BanterLines
{
    // Presentation + odds
    public static readonly Color Banter = new Color(0.93f, 0.92f, 0.82f);  // soft parchment
    public static readonly Color Egg = new Color(1f, 0.85f, 0.25f);     // bright gold, stands out
    public const float RareEggChance = 0.06f;   // chance a solo line is a rare non-combat egg
    public const float ChargeEggChance = 0.015f;  // chance a combat charge yells an egg (Leroy)

    // Monsters + reactions
    public static readonly Color MonsterBark = new Color(0.95f, 0.55f, 0.5f);   // menacing red-tint
    public static readonly Color Reaction = new Color(1f, 0.7f, 0.3f);       // urgent amber
    public const float MonsterTauntChance = 0.35f;  // chance a monster taunts on locking a target
    public const float TrapReactionChance = 0.5f;   // chance a trapped adventurer yelps
    public const float CoreSightRange = 7f;     // tiles: first-sight-of-core reaction radius

    public static readonly string[] Generic =
    {
        "Keep your eyes open.",
        "I don't like the look of this place.",
        "Something's watching us. I can feel it.",
        "How much deeper does this pit go?",
        "Did you hear that?",
        "Watch your footing down here.",
        "Quiet... too quiet.",
        "My torch is nearly spent.",
        "I signed on for glory, not damp and dark.",
        "Stay close, now.",
        "There's a draught. Something opened.",
        "We're not alone in here.",
    };

    public static readonly string[] Guild =
    {
        "For the Guild.",
        "Steady. We've cleared worse than this.",
        "By the book. No heroics.",
        "The Guild pays for results, not corpses.",
        "Mark the map. We'll want the way out.",
        "Hold formation.",
        "Another day, another den of horrors.",
    };

    public static readonly string[] Mercenary =
    {
        "This had better pay.",
        "I'm counting every coin down here.",
        "Danger rate. That was the deal.",
        "Loot first. Heroics never paid a tab.",
        "You break it, I still get paid.",
        "Half now, half when we're out alive.",
        "For the right price, I'd fight the dark itself.",
    };

    public static readonly string[] Cultist =
    {
        "The deep ones stir. I feel Them.",
        "We come not to plunder, but to kneel.",
        "Do you hear it? The core... it sings to me.",
        "Blessed is the dark below.",
        "Soon we look upon the divine.",
        "Down. Always down. The faithful go down.",
        "Hush. This is holy ground.",
    };

    public static readonly string[] RareEggs =
    {
        "I've got a bad feeling about this.",
        "It's dangerous to go alone...",
        "War. War never changes.",
        "Should've stayed a farmer.",
    };

    public static readonly string[] ChargeEggs =
    {
        "LEEEEROY JENKINS!!!",
    };

    public static readonly string[][] Pairs =
    {
        new[] { "Think we'll be rich after this?", "If we live to spend it." },
        new[] { "You ever feel like we're being led somewhere?", "Every single step." },
        new[] { "Remind me why we took this job.", "Coin. Same as always." },
        new[] { "What was that noise?", "...Let's not find out." },
        new[] { "Last one to the treasure buys the ale!", "You're on." },
        new[] { "I hate caves.", "You say that every time." },
        new[] { "Stay behind me.", "Gladly." },
    };

    private static string[] FactionPool(FactionId f) => f switch
    {
        FactionId.AdventurersGuild => Guild,
        FactionId.HolyOrder => Guild,      // pious/righteous, close enough for v1
        FactionId.MercenaryCompany => Mercenary,
        FactionId.Cultists => Cultist,
        _ => Generic,
    };

    public static string Pick(string[] pool) => (pool == null || pool.Length == 0) ? "" : pool[Random.Range(0, pool.Length)];

    /// <summary>A solo line for a faction: half flavour, half generic, so flavour doesn't drown.</summary>
    public static string RandomSolo(FactionId faction)
    {
        var fac = FactionPool(faction);
        var pool = (fac.Length > 0 && Random.value < 0.5f) ? fac : Generic;
        return Pick(pool);
    }

    public static string RandomChargeEgg() => Pick(ChargeEggs);

    public static string[] RandomPair() => Pairs.Length == 0 ? null : Pairs[Random.Range(0, Pairs.Length)];

    // -- Monsters --
    public static readonly string[] MonsterGrowls =
    {
        "...",
        "Grrrrr...",
        "*hungry rattle*",
        "*a low, wet growl*",
        "Ssssoft little things...",
        "*bones shift in the dark*",
        "Warm... blood... close...",
        "*sniffs the air*",
    };

    public static readonly string[] MonsterTaunts =
    {
        "Fresh meat!",
        "Stay a while...",
        "You should not have come.",
        "Mine now.",
        "*shrieks*",
        "Come closer, little morsel.",
    };

    public static string RandomMonsterGrowl() => Pick(MonsterGrowls);
    public static string RandomMonsterTaunt() => Pick(MonsterTaunts);

    // -- Adventurer reactions to moments --
    public static readonly string[] TrapReactions =
    {
        "Trap!",
        "Argh!",
        "It's a trap!",
        "Look out!",
        "Gah - my leg!",
    };

    public static readonly string[] DeathReactionsNamed =
    {
        "No - {0}!",
        "{0}! Get up!",
        "They got {0}!",
        "{0} is down!",
    };

    public static readonly string[] DeathReactionsGeneric =
    {
        "Man down!",
        "We've lost one!",
        "No...",
        "Fall back!",
    };

    public static readonly string[] CoreSightReactions =
    {
        "There it is...",
        "By the gods... the core.",
        "That's what we came for.",
        "So it's real.",
    };

    /// <summary>A trapped adventurer yelps (chance-gated - traps fire often).</summary>
    public static void ReactTrap(DungeonAdventurer adv)
    {
        if (adv == null || Random.value > TrapReactionChance) return;
        adv.Say(Pick(TrapReactions), Reaction);
    }

    /// <summary>A surviving party-mate reacts to a fallen member, by name when known.</summary>
    public static void ReactPartyDeath(AdventurerParty party, PartyMember fallen)
    {
        if (party == null) return;
        var live = party.LiveMembers;
        if (live == null || live.Count == 0) return;
        var speaker = live[Random.Range(0, live.Count)];
        if (speaker == null) return;
        string name = (fallen != null && !string.IsNullOrEmpty(fallen.name)) ? fallen.name : null;
        string line = name != null
            ? string.Format(Pick(DeathReactionsNamed), name)
            : Pick(DeathReactionsGeneric);
        speaker.Say(line, Reaction);
    }

    /// <summary>An adventurer's hushed first sight of the core.</summary>
    public static void ReactCoreSight(DungeonAdventurer adv)
    {
        if (adv == null) return;
        adv.Say(Pick(CoreSightReactions), Banter);
    }
}