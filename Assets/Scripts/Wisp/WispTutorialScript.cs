using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wisp's guided-opening lines, kept apart from the ambient WispScript so
/// the tutorial script stays legible and the two never crowd one fill. Each
/// beat of the first-build sequence is one keyed line; the TutorialDirector
/// asks for them by id as the player earns each step.
///
/// Right-click the component header on the asset and choose Fill Canon Lines
/// to write the signed-off tutorial script into a fresh asset.
/// </summary>
[CreateAssetMenu(fileName = "WispTutorialScript", menuName = "Dungeon Core/Wisp Tutorial Script")]
public class WispTutorialScript : ScriptableObject
{
    [Serializable]
    public class Line
    {
        public string id;
        [TextArea] public string text;
    }

    public Line[] lines = new Line[0];

    private Dictionary<string, Line> byId;

    private void BuildIndex()
    {
        byId = new Dictionary<string, Line>();
        foreach (Line line in lines)
            if (line != null && !string.IsNullOrEmpty(line.id) && !byId.ContainsKey(line.id))
                byId[line.id] = line;
    }

    public string Text(string id)
    {
        if (byId == null) BuildIndex();
        return byId.TryGetValue(id, out Line line) ? line.text : null;
    }

    [ContextMenu("Fill Canon Lines")]
    private void FillCanonLines()
    {
        lines = new[]
        {
            // Beat 1 - claim territory.
            new Line { id = "tut_claim",
                text = "First, spread. Hold the claim key and press outward - the dark flows where you spend, and every cell of it is yours to build on." },

            // Beat 2 - dig for the entrance. The compass appears with this line.
            new Line { id = "tut_dig",
                text = "Feel that? Air, moving. There is a way to the surface, buried close. Dig toward it - the pointer marks the bearing. Nothing comes for you until it is open." },

            // Beat 3 - the breakthrough vignette (spoken as the entrance is found).
            new Line { id = "tut_breach",
                text = "Open. The world can see us now - and something already has. Watch." },

            // Beat 3b - the rat is absorbed and learned.
            new Line { id = "tut_rat_taken",
                text = "A hunter's arrow, and a small life spilled at our threshold. The dark takes what the surface discards. It is yours now - your first. Others will have seen the light go out down here; they will come to look." },

            // Beat 4 - the grace day.
            new Line { id = "tut_grace",
                text = "But not yet. Word travels slowly, and the wild things below are still waking. Use the quiet - there is much to raise before the first of them arrives." },

            // Beat 4b - carve a chamber before naming one.
            new Line { id = "tut_carve",
                text = "Wide, not long. A worm-run breeds a crush - your creatures fight poorly shoulder-to-shoulder. Carve me a chamber: mined ground three across at the least, and the urging in the ledger will know it." },

            // Beat 5 - designate a room, then house the rat.
            new Line { id = "tut_room",
                text = "Now give the chamber a name. Mark the room across the pocket you carved, choose its purpose, and set your new companion within it." },

            // Beat 6 - research the alert ledger.
            new Line { id = "tut_research",
                text = "One thing more. Open the tree of what the core remembers, and learn the Ledger of Alarums - so that nothing crosses our threshold again without your knowing." },

            // Beat 7 - handoff to free play.
            new Line { id = "tut_done",
                text = "You have the shape of it now: spread, dig, shape, and watch. The rest is yours to discover. Build well - they are coming, and I would like them to be impressed." },

            // Soft re-prompts if the player idles on a step (director may reuse).
            new Line { id = "tut_nudge_dig",
                text = "The way in is still buried. Follow the pointer - dig toward the moving air." },
            new Line { id = "tut_nudge_carve",
                text = "Still all tunnel. Three across, remember - a chamber the ledger will recognise." },
            new Line { id = "tut_nudge_room",
                text = "A room, when you are ready - marked across your carved chamber, then given a purpose and your companion to hold it." },
            new Line { id = "tut_nudge_research",
                text = "The Ledger of Alarums still waits in the tree, whenever you would have warning of what comes." },
        };
    }
}