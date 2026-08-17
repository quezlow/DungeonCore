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
            // The loot policy beat (canon 45). Two lines because the first
            // party can resolve by leaving OR by dying, and a complaint line
            // over a party that never got out would be a lie.
            new Line { id = "village_fallen", once = true,
                text = "The little hold below has gone quiet. Whatever came up out of the deep "
                     + "took it, and the lanes are empty. They were not yours - but they were "
                     + "something, and now there is one less something between you and what "
                     + "made that silence." },
            new Line { id = "village_resettled", once = false,
                text = "Settlers on the deep road, and hearthsmoke where there was "
                     + "silence. The Holds do not give up what they have named. "
                     + "Remember that, when you are deciding what to let live down "
                     + "there." },
            new Line { id = "village_abandoned", once = true,
                text = "Again and again the Holds reached for that hearth, and each "
                     + "time the dark closed over their hands. They have stopped "
                     + "reaching. The lanes stay empty now, and the wagons stay "
                     + "home." },
            new Line { id = "village_fortified", once = true,
                text = "The hold has bled enough to learn. Walls where there were "
                     + "doorways, watchers where there were children. Whatever rises "
                     + "out of the deep now will find it already answered." },

            new Line { id = "loot_policy_unset", once = true,
                text = "They leave with nothing, and they are not quiet about it. That is my "
                     + "doing - I never asked you how open-handed you meant to be. Decide now. "
                     + "Give too little and they stop coming; give too much and other eyes start counting." },
            new Line { id = "loot_policy_unset_nosurvivors", once = true,
                text = "None of them walked out, so none of them can complain. They would have, "
                     + "though - there was nothing down there to take. Tell me how generous you "
                     + "intend to be, before the next lot live long enough to talk." },

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

            // The dens (canon 42). One when a den wakes after its grace days --
            // they stirred because the player arrived -- and one on clearing a
            // den that was holding more than coin.
            new Line { id = "den_wakes", once = true,
                text = "Something below has noticed you. It has been down there longer than your stone has, and it has decided you are worth robbing." },
            new Line { id = "den_one_way", once = true,
                text = "Kill the last of them and the hole stays empty - nothing down here refills what has been emptied. Their hoard comes to you the moment it happens, so the only question worth asking is how much longer you let them gather it." },
            new Line { id = "den_hoard_content", once = true,
                text = "Not only coin in that hole. They took things they could not read and kept them anyway - and now those are yours, which is the closest thing to justice this place offers." },

            // The diggings stopping. Fires on the dig falling short of what it
            // asked for, which covers BOTH ways it ends -- the reserve spent,
            // and the player having mined it first and kept it. Coupled income
            // means the hoard stops with it, so this is the only warning that a
            // tier which has stopped moving has stopped for good.
            // The dig (canon 42, stage 2). Only REMAINS speaks: a den finds
            // roughly seven things by day 150 and a line for each would be
            // spam, so a chamber or a stretch of the player's own frontier
            // being broken into is silent and consequential.
            new Line { id = "den_remains_taken", once = true,
                text = "They have opened old stone down there - and whatever was resting in it is theirs now, not yours. That is what waiting costs, and it is the only debt in this place that grows while you do nothing." },
            new Line { id = "den_remains_returned", once = true,
                text = "What they dug out of your stone has come back with the rest of it. Late, and by the only road that was ever open - through them." },
            new Line { id = "den_tunnel_done", once = true,
                text = "The digging has stopped for good. They went as far as they had rock and patience for, and what they found on the way they have already taken." },
            // The road breach (canon 42, stage 2c). ONCE, though a den may reach
            // the carriageway on many dawns: the first time is the event -- two
            // things that were never meant to meet, meeting under your floor --
            // and the tenth is weather. Deliberately vague about who broke
            // through and who was standing there, because the player is not
            // watching and neither the wisp nor they will ever be told.
            new Line { id = "den_road_breach", once = true,
                text = "They have dug into the old road. Whatever the dwarves keep walking it has met whatever has been digging toward it, and neither of them came to you first. Nothing down there is on your side - it is only that some of it is now busy with the rest." },

            new Line { id = "den_diggings_done", once = true,
                text = "The digging below has stopped. Either they have run out of rock worth breaking or you took it from under them - either way that hole grows no further, and neither does what they have put in it." },

            // First arrival of the Wandering Merchant, once ever.
            new Line { id = "merchant_first", once = true,
                text = "A wagon on the road. He has come a long way to sell to something like you - do look at what he carries." },

            // The Buried Age (canon 19). The first ruin found on a floor, and the
            // Sealed Gate -- which is gated on having LIVED the prologue, because
            // canon 34 puts the player's death at an opened seal.
            new Line { id = "site_first", once = true,
                text = "Straight walls. Down here, that means hands. Somebody built this before anything above ground knew how to." },
            new Line { id = "site_sealed_gate", once = true,
                text = "A gate, and it is still shut. You stood at one of these once, at the end, and it was open - and three people you had met that morning found you there. Ask yourself who opened it." },

            // THE WARDEN LINES (canon 34). Entry 34 records the reading that the
            // wisp is plausibly a warden of the old deep-faith, doing a job it
            // has done before for cores that failed. These are where that stops
            // being a reading and starts being something the player hears.
            //
            // Implied and never confirmed, deliberately: canon keeps the wisp's
            // nature an open question, and a line that settles it spends
            // something no later entry can get back. It says it has been here
            // before. It does not say what it is.
            //
            // NOT gated on CoreMemory.Lived, unlike the Sealed Gate line above.
            // That one is gated because the memory belongs to the player's own
            // death. This memory is the WISP'S, and a skipped prologue does not
            // erase the wisp's history.
            new Line { id = "site_dead_core", once = true,
                text = "Stop. I know this shape - stone laid in a ring, and at the middle of it something that used to think. They did not raise this to keep a thing out. They raised it to sit with what was already finished." },
            new Line { id = "site_dead_core_before", once = true,
                text = "I have stood where you are standing, and not with you. They asked me the same questions in the same order, the others, and I have never found a better answer than go carefully. Some of them are still down here." },
            new Line { id = "site_church_seal", once = true,
                text = "The Church did not invent the sealing. They inherited it and forgot who from, the way a man inherits a house and never asks who dug the cellar. Older hands than theirs laid the first of these, and for a kinder reason." },

            // The dwarven outpost (canon 19). The first line is the discovery;
            // the greeting is deliberately NOT a one-shot, because in part 2 it
            // becomes the line that plays whenever the counter is closed.
            new Line { id = "outpost_first", once = true,
                text = "Lamps. Down here, and lit, and nobody up there knows. They kept the road, and they kept the gate on it - and they have not heard a word the Church says about things like you." },
            new Line { id = "outpost_greeting", once = false,
                text = "He looks at you the way a man looks at weather. Not afraid. Deciding." },

            // Settling the spoil invoice. Not a one-shot -- it is the beat of the
            // whole trade relationship and should not fall silent after once.
            new Line { id = "outpost_spoil_sold", once = false,
                text = "He weighs it, and he does not argue. They have been paying for stone since before anyone up there had a word for money." },
            new Line { id = "outpost_first_trade", once = true,
                text = "That is the first thing anyone down here has ever bought from them. They will remember it longer than you will." },

            // The village on the floor below the gatehouse. The greeting is NOT
            // a one-shot -- it is what plays whenever a villager is clicked.
            new Line { id = "village_first", once = true,
                text = "A whole hold, lamplit and living. They never went up, so they never learned to fear the shape of you. Walk soft - this is the oldest home the world still keeps." },
            new Line { id = "village_greeting", once = false,
                text = "Hammers pause. Eyes follow. Not fear - bookkeeping. They will remember what you touch." },

            // The Living Holds: the roads have people on them now. The toll
            // spiel is the load-bearing one -- it is the mechanic's tutorial.
            new Line { id = "caravan_first", once = true,
                text = "A wagon on the deep road. Goods, guards' wages, somebody's whole year. It will pass whether you watch or not - but you could do more than watch." },
            new Line { id = "caravan_toll_first", once = true,
                text = "It has crossed onto stone you hold. Old law: the road pays its keeper. Click the wagon and choose - take it all and be a robber, take a toll and be a power, or wave it through and be forgotten. One choice per wagon." },
            new Line { id = "caravan_robbed", once = true,
                text = "Efficient. Unforgivable, in their ledgers - and they keep the ledgers. The Holds will not send another soon." },
            new Line { id = "patrol_first", once = true,
                text = "Armed, unhurried, walking a road their grandfathers cut. They are not looking for you. Try to keep it that way." },
            // Canon 44. Speaks only where the DUNGEON did it -- a trap counts,
            // and a trap is precisely the case this line exists for: standing
            // spent on a death the player never chose is a bill they cannot
            // trace, which is a fault in their model of the game rather than a
            // consequence in it.
            // The road's own consequence, spoken however the column fell --
            // the kobold case is the one the cooldown exists for, and a
            // silence there would read as the caravan simply forgetting to
            // come.
            new Line { id = "caravan_wiped", once = false,
                text = "All of them, on the stone. The Holds will send nobody down that road for a long while - and they will not need to be told why." },
            new Line { id = "dwarf_slain_first", once = true,
                text = "One of theirs, dead on our stone. They will not have seen who swung - only where he fell. Their ledgers are long and they do not forget an entry." },

            // Canon 50: the funeral procession. The robbed line is the deep
            // entry, the respects line the roster's first credit -- and the
            // wiped line repeats, the caravan_wiped precedent: a silence on
            // the second wipe would read as the road forgetting its dead.
            new Line { id = "funeral_first", once = true,
                text = "They carry their dead down, not up. Down is where the fathers lie. Watch the road - grief walks slowly, and it does not look aside." },
            new Line { id = "funeral_robbed", once = true,
                text = "Grave goods, taken off a bier. There are entries their ledgers keep apart from the rest, and you have just written one." },
            new Line { id = "funeral_wiped", once = false,
                text = "The bearers and the borne, together on the stone. Nobody will carry the story home - the silence will carry it." },
            new Line { id = "funeral_respects", once = true,
                text = "You let it pass, and marked it. So did they. Small entries balance ledgers too." },

            // Canon 51: the deep pilgrimage. Canon 20 is the joke the
            // first line does not quite tell: the old faith prays to what
            // the player is. The wiped line repeats, the caravan_wiped
            // precedent -- a silence on the second wipe would read as the
            // faith forgetting its own.
            new Line { id = "pilgrim_first", once = true,
                text = "Pilgrims, little core. The old faith says divinity sleeps below - and you are what it says their prayers are for. They walk toward you and do not know it." },
            new Line { id = "pilgrim_robbed", once = true,
                text = "Offerings meant for the deep, taken before they got there. Their ledgers will call it theft. The old faith may call it something worse." },
            new Line { id = "pilgrim_wiped", once = false,
                text = "The faithful, ended on the road to their own god. Whatever walks that stone next will walk it quieter." },
            new Line { id = "pilgrim_blessed", once = true,
                text = "You marked them kindly, and they felt it - a warmth off the stone. The Holds will hear. The faith will remember longer." },

            // Canon 52: the refugee exodus. The wiped line repeats, the
            // caravan_wiped precedent. refugee_last speaks only for the
            // flight off an ABANDONED hold -- the road going quiet for
            // good, which is the price entry 46 promised.
            new Line { id = "refugee_first", once = true,
                text = "They are walking up, little core. Not to trade, not to pray - away. Whatever is behind them, you built it." },
            new Line { id = "refugee_robbed", once = true,
                text = "They had almost nothing, and now they have nothing. That was the cheapest coin you will ever take, and the dearest." },
            new Line { id = "refugee_wiped", once = false,
                text = "None of them reached the gatehouse. No word goes up, no grave goes down. Only the road knows." },
            new Line { id = "refugee_last", once = true,
                text = "That was the last of them. The hold is stone and dust now, and the wagons will not come again. Listen - the road is quiet." },

            // The warning ladder (canon 19). Rung 2 speaks while the push is
            // still only LEANING on dwarven ground -- before the first cell is
            // taken -- which is the one moment the choice is still free.
            // Holy Ground (canon 18/20). The murmur lands on the first
            // CLAIM of hallowed ground -- free, and before the stone.
            new Line { id = "holy_first_touch", once = true,
                text = "Careful. This stone is dressed, and it is cold in a way stone is not. A seal - the Church laid it over something, and holding it costs you nothing. Breaking it is the other thing." },
            new Line { id = "holy_break", once = true,
                text = "It is open. Whatever they bound is loose, and somewhere above, someone keeping a list has just crossed a line off it." },
            // The vault is a different beat from an altar and does not share its
            // string: canon 20 built it around a DEAD CORE, so the line has to
            // land on what that is rather than on what was broken.
            new Line { id = "holy_break_vault", once = true,
                text = "There. Under all their stone - a core, cold and long finished, and once as new as you. They built the vault around the fear of it waking. It never did, and everything it knew is yours." },
            // The echo. CoreMemory speaks this only for a core that lit a
            // candle in the life it lived; it is silent for everyone else.
            new Line { id = "echo_candle", once = true,
                text = "You lit one of these, once. Small hands, a shrine, a wick that would not take. You have just put out something much older, and it was easier." },

            new Line { id = "road_claim_warn", once = true,
                text = "Feel that? The stone pushes back. This is cut road, and it is theirs. Take it if you must - but take it knowing they will notice, and that they count." },
            new Line { id = "road_claim_first", once = true,
                text = "It is done. One stone of their road answers to you now, and that one is a gift - they will forgive a curious core once. Every stone after it goes in the ledger." },

            // First time the Pressed rule bites: creatures massed in a corridor.
            new Line { id = "pressed_first", once = true,
                text = "Feel them jostle? Packed in a tunnel, your creatures foul one another's strikes. Give a garrison a room, or watch it fight below its worth." },

            // Memory echoes (canon 34). Each answers one deed from the living
            // prologue at the dungeon moment that rhymes with it. Once ever,
            // hundreds of days apart, and only if that life was actually lived.
            new Line { id = "echo_grave", once = true,
                text = "You dug a grave once. Spelled the old man for a few honest feet, and the earth came up dark and willing. Look what your hands do with it now." },
            new Line { id = "echo_net", once = true,
                text = "You cut something loose from a net once, because it was thrashing and you could not stand to watch. Listen. It is thrashing. You are the net." },
            new Line { id = "echo_offering", once = true,
                text = "You lifted a coin from a bowl once, and an old woman watched you do it and said nothing. Now they carry the bowl down to you. Nobody is watching at all." },
            new Line { id = "echo_alms", once = true,
                text = "You dropped a copper in a beggar's bowl once and he nodded, just the once. That was the last coin that ever left your hands." },
            new Line { id = "echo_climb", once = true,
                text = "You climbed a mill once, only to see how far the fields ran. You are going the other way now. It is further." },
            new Line { id = "echo_quench", once = true,
                text = "Steel into the barrel, and that hiss. You liked that sound. I thought you might like this one." },
            new Line { id = "echo_stone", once = true,
                text = "You knelt at a stone in the evening and it was warmer than the air, and you never asked why. This is why. The deep keeps its own, and it kept you." },

            // The empty-handed voice: a life lived, nothing carried down. Three
            // lines, in order, and then the wisp stops reaching. Refusal is a
            // shape too, and this is what it sounds like.
            new Line { id = "echo_hollow_1", once = true,
                text = "I reach for the life you brought down here, and my hand closes on nothing. You lived the whole day and kept none of it." },
            new Line { id = "echo_hollow_2", once = true,
                text = "Nothing again. Not emptiness, quite - more the hush of a room swept before anyone arrives." },
            new Line { id = "echo_hollow_3", once = true,
                text = "I will stop looking. Whoever you were, you did not bring them. Whoever you become owes them nothing at all." },

            // The descendants (canon 34). One arrival line for whichever house
            // leads, and one for each house when its descendant falls. Nobody in
            // the party knows any of this. Only the two of us do.
            new Line { id = "kin_ferro", once = true,
                text = "Ferro. There was a Ferro who let you work her bellows and told you fire dislikes nerves. Her great-grandchild is at your door with a sword." },
            new Line { id = "kin_cress", once = true,
                text = "Cress. You carried an old woman's jug from the well because the walk had got long for her. Her blood has come a longer way than that." },
            new Line { id = "kin_bramm", once = true,
                text = "Bramm. You turned a row of his soil and he pretended not to approve. His line still farms up there, and one of them has come down here instead." },
            new Line { id = "kin_ashcombe", once = true,
                text = "Ashcombe. You picked feverfew so a wheezing man could breathe. That house has been mending people ever since, and it has finally run out of patience." },
            new Line { id = "kin_latch", once = true,
                text = "Latch. Forty years of what the road coughed up, stacked in order of interest - and you broke some of it. They kept the ledger. They kept the grudge longer." },
            new Line { id = "kin_sedge", once = true,
                text = "Sedge. The old woman watched you lift the coin and said nothing at all. Her line does not seem to have forgotten it either." },
            new Line { id = "kin_crane", once = true,
                text = "Crane. You lit a candle in that man's church. His successors have been polishing the plaque ever since, and now they have come to see what is under it." },
            new Line { id = "kin_vane", once = true,
                text = "Vane. Say nothing. Serra Vane stood over you at the open seal and told you to close your eyes. Her blood is at your door, following maps, exactly as she did." },

            new Line { id = "kin_ferro_fall", once = true,
                text = "That is the last Ferro. The forge above has nobody left to inherit it, and the fault runs all the way back to a girl who liked the sparks." },
            new Line { id = "kin_cress_fall", once = true,
                text = "The Cress line ends on your floor. Somewhere far above, a jug sits on a sill and nobody comes for it." },
            new Line { id = "kin_bramm_fall", once = true,
                text = "No more Bramms. Turnips do not pull themselves, and now there is nobody to say so." },
            new Line { id = "kin_ashcombe_fall", once = true,
                text = "The last of the menders, mended into the floor. Four generations of hands that helped, finished by a thing they helped make." },
            new Line { id = "kin_latch_fall", once = true,
                text = "The Latch curiosity is settled. Everything they collected, and the last of them ends up in somebody else's collection." },
            new Line { id = "kin_sedge_fall", once = true,
                text = "Sedge told you the dead go down, and that down is not away. She was right about her own, in the end." },
            new Line { id = "kin_crane_fall", once = true,
                text = "The church above is short a family. They walked in light while it was offered, and then it was not." },
            new Line { id = "kin_vane_fall", once = true,
                text = "The Vane line is finished. It opened a seal, it killed you for standing near it, and it has just been paid. There is nothing in me that calls this even." },

            // The resting place (canon 34). Spoken at the mouth, over what is
            // left of the body Serra Vane did not trouble to move.
            new Line { id = "rest_murmur", once = true,
                text = "Now that you have gone down a floor, I will tell you a thing I have been sitting on. There is a pocket of stone beside your own mouth. Open it or do not." },
            new Line { id = "rest_found_1", once = true,
                text = "There. That is what she meant by the dark taking care of the rest. She did not even move you." },
            new Line { id = "rest_found_2", once = true,
                text = "You have walked your creatures past this wall since the day the seal broke. I could have said. I did not know how you would take it." },
            new Line { id = "rest_found_3", once = true,
                text = "There is nothing to take from it. No lesson, no gift. It is only where you stopped, and everything since has been the argument against it." },
            new Line { id = "rest_found_empty", once = true,
                text = "Nothing on you, and nothing in you when you came down. You arrived at this the same way you arrived at everything that last day: empty-handed, and free." },
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