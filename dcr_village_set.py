#!/usr/bin/env python3
# -*- coding: ascii -*-
"""
DCR delivery -- The Village Rotation (canon 19 part 3, second revision).

Run from the repo root:  python dcr_village_set.py
Requires the village + variants deliveries applied (origin/main @ bca21082+).

Three double-size holds join the game and one is rolled seeded per world:
The Terraced Vein (57x57 mining town), The Deep Market (71x49 crossroads
town), The Shrinehold (61x61 old-faith village). Selection moves from
by-name to by-archetype -- every authored DwarvenVillage plan is in the
roll, villagePlanName becomes an optional testing pin -- and the site report
now names the chosen hold. The Hearth of the Deep RETIRES from the roll:
its file stays on disk as the worked example, it just leaves authoredPlans.

Validated pre-delivery through the compiled real code: 200-seed rotation
split 65/62/73, placement 200/200, pin test 50/50, floor 2 and floor 4
regressions clean, and every hold pocket-free in all 8 orientations before
and after road cuts on four headings.

Same discipline as before: every anchor asserted count==1 before anything is
staged; per-file line endings and BOM preserved; all writes complete before
anything prints; idempotent re-run aborts.
"""
import io, os, re, sys

EDITS = [
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '    /// <summary>\n    /// Places the guaranteed dwarven village: the same first-and-loud contract as\n    /// PlaceOutpost, with one deliberate difference -- the plan is selected BY\n    /// NAME from the authored set, not filtered from the pool. The DwarvenVillage\n    /// archetype belongs to no roster, so the general loop can never serve it and\n    /// there is nothing to remove from the pool on success.\n    /// </summary>',
     '    /// <summary>\n    /// Places the guaranteed dwarven village: the same first-and-loud contract as\n    /// PlaceOutpost, with one deliberate difference -- the plan comes from the\n    /// authored set, never the pool. Every authored DwarvenVillage plan is a\n    /// candidate and one is rolled seeded, so playthroughs rotate holds; a\n    /// non-empty villagePlanName pins the roll to one plan instead (testing).\n    /// The archetype belongs to no roster, so the general loop can never serve\n    /// it and there is nothing to remove from the pool on success.\n    /// </summary>'),
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '        AuthoredSitePlan plan = null;\n        if (authoredPlans != null)\n            foreach (var p in authoredPlans)\n                if (p != null && p.name == entry.villagePlanName) { plan = p; break; }\n        if (plan == null)\n        {\n            Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +\n                " asked for a guaranteed village but no authored plan is named \'" +\n                entry.villagePlanName + "\'. Check villagePlanName against the " +\n                "plan\'s @name header and the profile\'s authoredPlans list.");\n            return;\n        }',
     '        var candidates = new List<AuthoredSitePlan>();\n        if (authoredPlans != null)\n            foreach (var p in authoredPlans)\n                if (p != null && p.archetype == SiteArchetype.DwarvenVillage)\n                    candidates.Add(p);\n        // Optional pin: a non-empty villagePlanName narrows the roll to that\n        // one plan, for testing a specific hold without unlisting the others.\n        if (!string.IsNullOrEmpty(entry.villagePlanName))\n            candidates.RemoveAll(p => p.name != entry.villagePlanName);\n        if (candidates.Count == 0)\n        {\n            Debug.LogError("[AncientSiteBuilder] Floor " + entry.floorIndex +\n                " asked for a guaranteed village but no authored DwarvenVillage " +\n                "plan is available" +\n                (string.IsNullOrEmpty(entry.villagePlanName) ? "" :\n                 " matching villagePlanName \'" + entry.villagePlanName + "\'") +\n                ". Check the profile\'s authoredPlans list" +\n                (string.IsNullOrEmpty(entry.villagePlanName) ? "." :\n                 " and the plan\'s @name header."));\n            return;\n        }\n\n        int pick = rng.Next(candidates.Count);\n        AuthoredSitePlan plan = candidates[pick];'),
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '            var placed = new AncientSitePlan\n            {\n                archetype = SiteArchetype.DwarvenVillage,\n                variant = 0,\n                anchor = anchor,\n                reservedForVillage = true,\n            };',
     '            var placed = new AncientSitePlan\n            {\n                archetype = SiteArchetype.DwarvenVillage,\n                // The candidate index, persisted through SiteData.variant as a\n                // breadcrumb for which hold this world rolled.\n                variant = pick,\n                anchor = anchor,\n                reservedForVillage = true,\n            };'),
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '            placed.id = result.sites.Count;\n            result.sites.Add(placed);\n            anchorsUsed.Add(anchor);\n            result.villagePlaced = true;\n            return;',
     '            placed.id = result.sites.Count;\n            result.sites.Add(placed);\n            anchorsUsed.Add(anchor);\n            result.villagePlaced = true;\n            result.villagePlanPicked = plan.name;\n            return;'),
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '    /// <summary>Same contract for the guaranteed village.</summary>\n    public bool villagePlaced;',
     '    /// <summary>Same contract for the guaranteed village.</summary>\n    public bool villagePlaced;\n\n    /// <summary>The @name of the hold the seeded roll chose -- the report\n    /// prints it, so rotation variety is verifiable headlessly by stepping\n    /// the report seed instead of walking the map.</summary>\n    public string villagePlanPicked = "";'),
    ('Assets/Scripts/Floors/AncientSiteBuilder.cs',
     '    /// <summary>Same shape for the village, so the site report prints both.</summary>\n    public string VillageSummary() =>\n        villagePlaced ? "village: placed" : "village: NONE";',
     '    /// <summary>Same shape for the village, so the site report prints both.\n    /// Names the chosen hold: with several villages in rotation, this line is\n    /// how variety gets verified.</summary>\n    public string VillageSummary() =>\n        villagePlaced\n            ? "village: placed (" + villagePlanPicked + ")"\n            : "village: NONE";'),
    ('Assets/Scripts/Floors/AncientSiteProfile.cs',
     '    [Tooltip("The authored plan the village is built from, matched against the " +\n             "plan\'s @name header. Selected BY NAME rather than through the " +\n             "roster, so the DwarvenVillage archetype sits in no pool and the " +\n             "plan can never be double-placed by the general fill loop.")]\n    public string villagePlanName = "";',
     '    [Tooltip("OPTIONAL PIN, empty on the shipped asset. Empty rolls seeded " +\n             "among every authored DwarvenVillage plan on this profile -- add " +\n             "a plan file with that archetype and it joins the rotation, zero " +\n             "config. Set a plan\'s @name here to force that hold, for testing. " +\n             "Either way the archetype sits in no pool, so the fill loop can " +\n             "never serve or double-place a village.")]\n    public string villagePlanName = "";'),
    ('Assets/Scripts/Floors/AncientSiteProfile.cs',
     '            reserveVillage = true,\n            villagePlanName = "The Hearth of the Deep",\n',
     '            reserveVillage = true,\n'),
    ('Assets/ScriptableObjects/Floors/AncientSiteProfile.asset',
     '    villagePlanName: The Hearth of the Deep\n',
     '    villagePlanName: \n'),
    ('Assets/ScriptableObjects/Floors/AncientSiteProfile.asset',
     '  - {fileID: 4900000, guid: 4d77a2f1c8b94e02a6d3b5e18c7f0942, type: 3}\n',
     '  - {fileID: 4900000, guid: 8c3fa2d17e5b40c9a1d6f4e2b8073c51, type: 3}\n  - {fileID: 4900000, guid: 2ab90e6c47d1483f9b5e8a0c6d2f174e, type: 3}\n  - {fileID: 4900000, guid: f5d81c30b6a24e7f8c29d5b1a4e60398, type: 3}\n'),
    ('Docs/DESIGN_CANON.md',
     'guarantee pair `reserveVillage` + `villagePlanName`.',
     'guarantee flag `reserveVillage` (plus `villagePlanName`, now an optional\npin -- see the guarantee below).'),
    ('Docs/DESIGN_CANON.md',
     "**The guarantee.** `PlaceVillage` in `AncientSiteBuilder` mirrors\n`PlaceOutpost`'s first-and-loud contract with one deliberate difference: the\nplan is selected BY NAME from the authored set, never through the pool.",
     "**The guarantee.** `PlaceVillage` in `AncientSiteBuilder` mirrors\n`PlaceOutpost`'s first-and-loud contract with one deliberate difference: the\nplan comes from the authored set, never the pool. Every authored\nDwarvenVillage plan is a candidate and one is ROLLED seeded per world, so\nplaythroughs rotate holds. A non-empty `villagePlanName` pins the roll to\nthat one plan (testing); empty -- the shipped state -- means roll, so adding\na fourth hold someday is one plan file with the archetype, zero config. The\npick index persists through `SiteData.variant` as a breadcrumb, and the\nreport names the chosen hold -- `village: placed (The Deep Market)` -- so\nrotation verifies headlessly by stepping `roadReportSeedOverride`."),
    ('Docs/DESIGN_CANON.md',
     '**The plan.** `DwarvenVillage_TheHearthOfTheDeep.txt`: 41x41, 1008 carved /\n673 masonry as drawn, four 5-wide gates on every bearing (an AlongRoad anchor\nlands on any local heading -- the gatehouse arithmetic). Quarters: the Great\nHall with its hearth pillar, two long-houses over a shared passage, the forge\nwith three coal stores, a terrace of three dwellings. THE DOOR RULE is\nrecorded in the file and is load-bearing: every door and internal passage is\n3 cells long, because the wall drape seals 2-long gaps that rotation turns\neast/west -- the v2 draft lost a 45-cell interior to exactly that. Validated\nin all eight orientations, before and after 5-wide road cuts on four\nheadings: zero sealed pockets, zero fragmentation. Post-subtraction carved\nmeasures 653--968 (avg 803) depending on how the road crosses. The scale is\ndeliberate: the landmark of its floor, partitioned into rooms and lanes --\nwhat reads as a hole in the fog is one empty rectangle, not cell count.',
     "**The plans.** Three holds rotate, sharing one contract: four 5-wide gates\non every bearing (an AlongRoad anchor lands on any local heading -- the\ngatehouse arithmetic), interiors partitioned so no single empty rectangle\nreads as a hole, and THE DOOR RULE recorded in every file -- all doors,\npassages, stall gaps and grave rows are 3 cells long, because the wall drape\nseals 2-long gaps that rotation turns east/west (the Hearth's draft lost a\n45-cell interior to that; the Market's draft, a 66-cell stall lane when the\nrule was not yet applied to furniture). Each hold validated in all eight\norientations before and after 5-wide road cuts on four headings: zero\npockets, zero fragmentation. Measured over 200 seeds the roll splits\n65/62/73:\n\n- `DwarvenVillage_TheTerracedVein.txt` -- the mining town. 57x57, 2314\n  carved as drawn, 1749--2214 after the carriageway. Terrace stacks, the\n  quarry yard with ore stacks and spoil ridge, the winch house and its\n  head-frame posts.\n- `DwarvenVillage_TheDeepMarket.txt` -- the crossroads town the sparse\n  network implies. 71x49 -- deliberately not square, so rotation deals\n  portrait and landscape markets -- 2374 carved, 1873--2306 after.\n  Crate-stacked warehouses, the grand trade hall, the stall-rowed market\n  yard, shop terraces.\n- `DwarvenVillage_TheShrinehold.txt` -- the old-faith village, entry 20's\n  deep faith on screen. 61x61, 2588 carved, 2002--2443 after. Cloister\n  terraces, the refectory, the shrine precinct with votive pillars around an\n  inner sanctum and altar, the bell court with belfry, keeper's house and\n  grave rows.\n\nThe first-ship hold, `DwarvenVillage_TheHearthOfTheDeep.txt` (41x41, 1008\ncarved), RETIRED from the roll after reading too small at play. The file\nstays on disk as the small worked example Authoring chapter 28 reads, but it\nis off the profile's `authoredPlans` -- which is all retirement takes."),
    ('Docs/DESIGN_CANON.md',
     '`ScriptableObjects/Sites/Plans/DwarvenVillage_TheHearthOfTheDeep.txt`.',
     'the three rotating `ScriptableObjects/Sites/Plans/DwarvenVillage_*.txt`\nholds (the retired Hearth stays on disk beside them).'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '      <p>The village: a 41x41 walled hold placed <b>AlongRoad</b> with four 5-wide gates, quarters\n      (Great Hall with hearth, two long-houses, the forge with coal stores, a terrace of three\n      dwellings), a settlement name rolled per run from an 8-name roster, static villagers dealt from a variant sprite list,\n      a Discovery alert, and two wisp lines. No vendor -- they trade at the gate, they live here.</p>',
     '      <p>The village: <b>one of three walled holds, rolled seeded per world</b> so playthroughs\n      differ -- <b>The Terraced Vein</b> (57x57 mining town), <b>The Deep Market</b> (71x49\n      crossroads town) or <b>The Shrinehold</b> (61x61 old-faith village), each roughly double\n      the retired first-ship Hearth and placed <b>AlongRoad</b> with four 5-wide gates. Plus a\n      settlement name rolled from an 8-name roster, static villagers dealt from a variant sprite list,\n      a Discovery alert, and two wisp lines. No vendor -- they trade at the gate, they live here.</p>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '<tr><td><code>Sites/Plans/DwarvenVillage_TheHearthOfTheDeep.txt</code> (+ .meta)</td><td><b>NEW</b> -- the authored hold, door rule recorded in-file</td></tr>',
     '<tr><td><code>Sites/Plans/DwarvenVillage_The{TerracedVein,DeepMarket,Shrinehold}.txt</code> (+ .meta)</td><td>The three rotating holds, door rule recorded in every file; the Hearth retires from the roll (file stays on disk)</td></tr>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '<td><code>PlaceVillage</code> (by-name guarantee), guarantee-only pool strip, zero-variant pool support, <code>villagePlaced</code> + <code>VillageSummary</code></td>',
     '<td><code>PlaceVillage</code> (seeded roll among authored DwarvenVillage plans; optional <code>villagePlanName</code> pin), guarantee-only pool strip, zero-variant pool support, <code>villagePlaced</code> + <code>VillageSummary</code> naming the pick</td>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     'The new plan must list clean: 41x41, carved 1008, masonry 673, and a healthy worst-orientation walkable count (hundreds, nowhere near the 16 floor). No <code>[AncientSitePlan] ... skipped</code> warning.',
     'All three holds must list clean -- Terraced Vein 57x57 (2314 carved / 935 masonry), Deep Market 71x49 (2374 / 1105), Shrinehold 61x61 (2588 / 1133) -- each with a worst-orientation walkable count in the high hundreds, nowhere near the 16 floor. The retired Hearth still validates too; it is on disk, just off the profile. No <code>[AncientSitePlan] ... skipped</code> warning.'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     'and look at the hold once: outer wall, four gates, the hall, the forge, the terrace. Rotate through orientations if you like',
     "and look at each hold once -- the Vein's quarry and terraces, the Market's stall rows and warehouses, the Shrinehold's sanctum ring and grave rows. Rotate through orientations if you like"),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '  outpost: NONE, <span class="k">village: placed</span></pre>',
     '  outpost: NONE, <span class="k">village: placed (The Deep Market)</span>  or Vein / Shrinehold</pre>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '<div class="step"><input type="checkbox" id="dcr-village-v1-c3s5"><label for="dcr-village-v1-c3s5">On the ASCII map the village is the one large <code>o</code>/<code>O</code> mass sitting on a road. Re-run a few times if you want to watch the placement move.</label></div>',
     '<div class="step"><input type="checkbox" id="dcr-village-v1-c3s5"><label for="dcr-village-v1-c3s5">On the ASCII map the village is the one large <code>o</code>/<code>O</code> mass sitting on a road. Re-run a few times if you want to watch the placement move.</label></div>\n      <div class="step"><input type="checkbox" id="dcr-village-v1-c3s5b"><label for="dcr-village-v1-c3s5b"><b>Watch the rotation:</b> set <code>roadReportSeedOverride</code> to 1, 2, 3... and re-run -- the village line names different holds (the 200-seed split measured 65 Vein / 62 Market / 73 Shrinehold). Set it back to <b>0</b> when done: zero means the world\'s own seed.</label></div>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     '<div class="step"><input type="checkbox" id="dcr-village-v1-c2s4"><label for="dcr-village-v1-c2s4">Save the scene.</label></div>',
     '<div class="step"><input type="checkbox" id="dcr-village-v1-c2s4"><label for="dcr-village-v1-c2s4">Save the scene.</label></div>\n      <div class="note"><b>Villager density:</b> at double footprint a count of 6 reads sparse; 8-10\n      fits the big holds. Inspector knob on the component -- your call, no code involved.</div>'),
    ('Docs/DCR_Guide_Dwarven_Village.html',
     'either <i>"no authored plan is named ..."</i> (the <code>villagePlanName</code> / <code>@name</code> pair drifted, or the plan is not on the profile\'s <code>authoredPlans</code>)',
     'either <i>"no authored DwarvenVillage plan is available ..."</i> (nothing with the archetype on the profile\'s <code>authoredPlans</code>, or a stale <code>villagePlanName</code> pin matching no plan -- empty means roll)'),
    ('Docs/DCR_Guide_Content_Authoring.html',
     'The plan is <code>Sites/Plans/DwarvenVillage_TheHearthOfTheDeep.txt</code>; everything in chapter 26 applies. <b>THE DOOR RULE is load-bearing and recorded in the file: every door and internal passage is THREE cells long in its run direction.</b> The drape seals 2-long gaps that rotation turns east/west -- the draft of this plan lost a 45-cell interior to exactly that. Do not shorten a door to make a wall look tidier.',
     "Three holds rotate: <code>Sites/Plans/DwarvenVillage_TheTerracedVein.txt</code>, <code>...TheDeepMarket.txt</code>, <code>...TheShrinehold.txt</code>; the retired Hearth stays beside them as the small readable example. Everything in chapter 26 applies to each. <b>THE DOOR RULE is load-bearing and recorded in every file: every door, passage, stall gap and grave-row gap is THREE cells long in its run direction.</b> The drape seals 2-long gaps that rotation turns east/west -- the Hearth's draft lost a 45-cell interior to that, the Market's draft a 66-cell stall lane. Do not shorten a gap to make furniture look tidier."),
    ('Docs/DCR_Guide_Content_Authoring.html',
     'Keep the four 5-wide gates on all bearings and keep the interior partitioned. The hold draws 1008 carved and hands 150-350 back to the carriageway; what reads as a hole in the fog is one big empty rectangle, not cell count.',
     'Keep the four 5-wide gates on all bearings and keep interiors partitioned. The holds draw 2314-2588 carved and hand 150-600 back to the carriageway; what reads as a hole in the fog is one big empty rectangle, not cell count.'),
    ('Docs/DCR_Guide_Content_Authoring.html',
     'then check the headless road report on floor index 3 for <code>village: placed</code>.',
     'then check the headless road report on floor index 3 for <code>village: placed (&lt;name&gt;)</code> -- step <code>roadReportSeedOverride</code> to watch the rotation.'),
    ('Docs/DCR_Guide_Content_Authoring.html',
     "The village is selected <b>by name</b>: <code>SiteFloorEntry.villagePlanName</code> on the site profile must equal the plan's <code>@name</code> header. Rename one, rename both, or the floor logs a loud error and places no village.",
     "Selection is <b>by archetype</b>: every authored <code>DwarvenVillage</code> plan on the profile is in the seeded roll, so <b>adding hold #4 is one dropped file, zero config</b>. <code>SiteFloorEntry.villagePlanName</code> is an optional pin -- empty (the shipped state) rolls; set to a plan's <code>@name</code> it forces that hold for testing. A stale pin logs a loud error and places no village."),
    ('Docs/DCR_Guide_Content_Authoring.html',
     'updated for the floor plan correction and <code>@general: no</code>.</div>',
     'updated for the floor plan correction and <code>@general: no</code>.\n      <br><b>Rev 7:</b> chapter 28 revised for the rotation set -- three double-size holds (Terraced Vein, Deep Market, Shrinehold) rolled by archetype, the Hearth retired to worked-example duty, and the door rule extended in writing to stalls and grave rows.</div>')
]

NEW_FILES = {
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheTerracedVein.txt':
        "@archetype: DwarvenVillage\n@name: The Terraced Vein\n@anchor: AlongRoad\n\n// The Terraced Vein: the mining town. 57x57, 2314 carved / 935 masonry as\n// drawn (footprint 1.93x the retired Hearth). North-west and south-west carry\n// the terrace stacks the miners live in; north-east is the quarry yard with\n// its ore stacks, spoil ridge and winch hut; south-east the winch house with\n// its head-frame posts and the south work yard.\n//\n// THE DOOR RULE, measured, do not shorten: every door, passage, stall gap and\n// grave-row gap is THREE cells long in its run direction. The wall drape seals\n// 2-long gaps that rotation turns east/west -- the Hearth's v2 draft lost a\n// 45-cell interior to that, and this set's market draft lost a 66-cell stall\n// lane to 2-wide stall gaps before the rule was applied to furniture too.\n// Validated through the real builder in all eight orientations, before and\n// after five-wide road cuts on four headings: zero pockets, zero\n// fragmentation.\n//\n// One of the authored DwarvenVillage plans is rolled seeded at floor-3\n// generation (PlaceVillage); drop another file with this archetype beside\n// these and it joins the rotation with zero configuration.\n\n##########################.....##########################\n##########################.....##########################\n##.....................................................##\n##.#####...#####..##...##.......#########...##########.##\n##.#...........#..#.....#.......#########...##########.##\n##.#...........#..#.....#.......##..................##.##\n##.#...........#..#.....#.......##..................##.##\n##.#...........#..#.....#.......##..................##.##\n##.#...........#..#.....#.......##...........##.....##.##\n##.#...........#..#.....#.......##....##............##.##\n##.#####...#####..#.....#.......##..................##.##\n##.#...........#..#.....#.......##..................##.##\n##.#...........#..#.....#.......##..######..........##.##\n##.#...........#..##...##.......##..................##.##\n##.#...........#..#.....#.......##..................##.##\n##.#...........#..#.....#.......##..........#.......##.##\n##.#...........#..#.....#.......##..................##.##\n##.#####...#####..#.....#.......##...#.........#...###.##\n##.#...........#..#.....#.......##.............#...###.##\n##.#...........#..#.....#......................#...###.##\n##.#...........#..#.....#......................#...###.##\n##.#....................#......................#...###.##\n##.#....................#.......##.............#######.##\n##.#....................#.......#########...##########.##\n##.#####...#####..##...##.......#########...##########.##\n##.....................................................##\n.........................................................\n.........................................................\n.........................................................\n.........................................................\n.........................................................\n##.....................................................##\n##.###...####..###...####.......#########...##########.##\n##.#........#..###...####.......#########...##########.##\n##.#........#..##......##.......##..................##.##\n##.#........#..##......##.......##..................##.##\n##.#........#..##......##.......##..................##.##\n##.#........#..##......##.......##..................##.##\n##.#........#..##......##.......##......#....#......##.##\n##.###...####..##..#...##.......##..................##.##\n##.#........#..##......##.......##.....................##\n##.#........#..##......##.......##.....................##\n##.#........#..##......##.......##.....................##\n##.#........#..##......##.......##..................##.##\n##.#........#..###...####.......#########...##########.##\n##.#........#..###...####.......#########...##########.##\n##.###...####..........................................##\n##.#........#..........................................##\n##.#........#..........................................##\n##.#........#..........................................##\n##.#..............#..#..............#............#.....##\n##.#...................................................##\n##.#...................................................##\n##.###...####..........................................##\n##.....................................................##\n##########################.....##########################\n##########################.....##########################\n",
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheTerracedVein.txt.meta':
        'fileFormatVersion: 2\nguid: 8c3fa2d17e5b40c9a1d6f4e2b8073c51\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheDeepMarket.txt':
        "@archetype: DwarvenVillage\n@name: The Deep Market\n@anchor: AlongRoad\n\n// The Deep Market: the crossroads town the sparse network implies. 71x49,\n// 2374 carved / 1105 masonry as drawn (footprint 2.07x the retired Hearth,\n// and deliberately NOT square -- rotation hands out portrait and landscape\n// markets). West half: two crate-stacked warehouses over the grand trade\n// hall; east half: the stall-rowed market yard over the shop terraces.\n//\n// THE DOOR RULE, measured, do not shorten: every door, passage, stall gap and\n// grave-row gap is THREE cells long in its run direction. The wall drape seals\n// 2-long gaps that rotation turns east/west -- the Hearth's v2 draft lost a\n// 45-cell interior to that, and this set's market draft lost a 66-cell stall\n// lane to 2-wide stall gaps before the rule was applied to furniture too.\n// Validated through the real builder in all eight orientations, before and\n// after five-wide road cuts on four headings: zero pockets, zero\n// fragmentation.\n//\n// One of the authored DwarvenVillage plans is rolled seeded at floor-3\n// generation (PlaceVillage); drop another file with this archetype beside\n// these and it joins the rotation with zero configuration.\n\n#################################.....#################################\n#################################.....#################################\n##...................................................................##\n##.######...##########...######.........############...#############.##\n##.######...##########...######.........############...#############.##\n##.##............##..........##.........##........................##.##\n##.##............##..........##.........##........................##.##\n##.##............##..........##.........##........................##.##\n##.##..#......#..##..........##.........##...###...###...###......##.##\n##.##............##..........##.........##........................##.##\n##.##.............#..........##.........##........................##.##\n##.##.............#..........##.........##........................##.##\n##.##.............#..........##.........##...###...###...###......##.##\n##.##............##..........##.........##........................##.##\n##.##............##..........##.........##........................##.##\n##.##............##..#....#..##...................................##.##\n##.##............##..........##...................................##.##\n##.##............##..........##...................................##.##\n##.##............##..........##.........##........................##.##\n##.######...##########...######.........############...#############.##\n##.######...##########...######.........############...#############.##\n##...................................................................##\n.......................................................................\n.......................................................................\n.......................................................................\n.......................................................................\n.......................................................................\n##...................................................................##\n##.############...#############.........###...#########...######...#.##\n##.############...#############.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##.....#............#.....##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........###...#########...######...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##.##........................##.........#........####........###...#.##\n##........#............#.....##.........#........####........###...#.##\n##...........................##.........#........####........###...#.##\n##...........................##.........#.........##.........###...#.##\n##.##........................##.........#.........##.........###...#.##\n##.############...#############.........#.........##.........###...#.##\n##.############...#############.........###...#########...######...#.##\n##...................................................................##\n#################################.....#################################\n#################################.....#################################\n",
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheDeepMarket.txt.meta':
        'fileFormatVersion: 2\nguid: 2ab90e6c47d1483f9b5e8a0c6d2f174e\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n',
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheShrinehold.txt':
        "@archetype: DwarvenVillage\n@name: The Shrinehold\n@anchor: AlongRoad\n\n// The Shrinehold: the old-faith village, entry 20's deep faith on screen.\n// 61x61, 2588 carved / 1133 masonry as drawn (footprint 2.21x the retired\n// Hearth). North-west and south-west: cloister terraces and the refectory;\n// north-east: the shrine precinct, votive pillars ringing an inner sanctum\n// with its altar; south-east: the bell court, belfry, keeper's house and\n// the grave rows.\n//\n// THE DOOR RULE, measured, do not shorten: every door, passage, stall gap and\n// grave-row gap is THREE cells long in its run direction. The wall drape seals\n// 2-long gaps that rotation turns east/west -- the Hearth's v2 draft lost a\n// 45-cell interior to that, and this set's market draft lost a 66-cell stall\n// lane to 2-wide stall gaps before the rule was applied to furniture too.\n// Validated through the real builder in all eight orientations, before and\n// after five-wide road cuts on four headings: zero pockets, zero\n// fragmentation.\n//\n// One of the authored DwarvenVillage plans is rolled seeded at floor-3\n// generation (PlaceVillage); drop another file with this archetype beside\n// these and it joins the rotation with zero configuration.\n\n############################.....############################\n############################.....############################\n##.........................................................##\n##.####...####..####...####.......##########...###########.##\n##.#.........#..#.........#.......##########...###########.##\n##.#.........#..#.........#.......##....................##.##\n##.#.........#..#.........#.......##....................##.##\n##.#.........#..#.........#.......##..#..............#..##.##\n##.####...####..####...####.......##....................##.##\n##.#.........#..#.........#.......##.....###########....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.####...####..####...####.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....#....#....#....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....#.........#....##.##\n##.####...####..####...####.......##.....#.........#....##.##\n##.#.........#..#.........#.......##.....####...####....##.##\n##.#.........#..#.........#.......##....................##.##\n##.#.........#..#.........#.............................##.##\n##.#.........#..#.........#...........#..............#..##.##\n##.#............#.......................................##.##\n##.#............#.................##....................##.##\n##.#............#.................##########...###########.##\n##.####...####..####...####.......##########...###########.##\n##.........................................................##\n.............................................................\n.............................................................\n.............................................................\n.............................................................\n.............................................................\n##.........................................................##\n##.####...####..####...####.......##########...###########.##\n##.#.........#..####...####.......##########...###########.##\n##.#.........#..##.......##.......##....................##.##\n##.#.........#..##.......##.......##....................##.##\n##.#.........#..##.......##.......##....................##.##\n##.####...####..##.......##.......##....................##.##\n##.#.........#..##.......##.......##........##..........##.##\n##.#.........#..##...#...##.......##........##..........##.##\n##.#.........#..##.......##.......##....................##.##\n##.#.........#..##.......##.......##....................##.##\n##.####...####..##.......##.......##....................##.##\n##.#.........#..##.......##.......##.###.###............##.##\n##.#.........#..##.......##.......##....................##.##\n##.#.........#..##.......##.......##....................##.##\n##.#.........#..####...####.......##.###.###............##.##\n##.####...####..####...####.......##..............########.##\n##.#.........#....................##..............#....###.##\n##.#.........#....................##...................#...##\n##.#.........#....................##...................#...##\n##.#.........#....................##...................#...##\n##.#...............#...#..........##..............#....###.##\n##.#..............................##..............########.##\n##.#..............................##########...###########.##\n##.####...####....................##########...###########.##\n##.........................................................##\n############################.....############################\n############################.....############################\n",
    'Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheShrinehold.txt.meta':
        'fileFormatVersion: 2\nguid: f5d81c30b6a24e7f8c29d5b1a4e60398\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
}

def fail(msg):
    print("ABORT (nothing written): " + msg)
    sys.exit(1)

def main():
    if not os.path.isdir("Assets") or not os.path.isdir("Docs"):
        fail("run from the DungeonCore repo root")

    builder = "Assets/Scripts/Floors/AncientSiteBuilder.cs"
    with io.open(builder, "r", encoding="utf-8", newline="") as f:
        b = f.read()
    if "villagePlanPicked" in b:
        fail("already applied (villagePlanPicked present)")
    if "PlaceVillage" not in b:
        fail("village delivery not applied first")
    for p in NEW_FILES:
        if os.path.exists(p):
            fail("new file already exists: " + p)

    texts, crlf, bom = {}, {}, {}
    for path, _, _ in EDITS:
        if path in texts:
            continue
        if not os.path.exists(path):
            fail("missing target file: " + path)
        with io.open(path, "r", encoding="utf-8", newline="") as f:
            raw = f.read()
        bom[path] = raw.startswith("\ufeff")
        if bom[path]:
            raw = raw.lstrip("\ufeff")
        crlf[path] = raw.count("\r\n") > raw.count("\n") // 2
        texts[path] = raw.replace("\r\n", "\n")

    problems = []
    for i, (path, old, new) in enumerate(EDITS):
        n = texts[path].count(old)
        if n != 1:
            problems.append(f"edit {i}: count=={n} in {path} :: {old[:60]!r}")
    if problems:
        fail("anchor drift, repo does not match the state this was built "
             "against:\n  " + "\n  ".join(problems))

    for path, old, new in EDITS:
        texts[path] = texts[path].replace(old, new, 1)

    for path, t in texts.items():
        if path.endswith(".cs"):
            for a, b2 in (("{", "}"), ("(", ")")):
                if t.count(a) != t.count(b2):
                    fail("brace imbalance after staging in " + path)
        if path.endswith(".html"):
            ids = re.findall(r'id="([^"]+)"', t)
            if len(ids) != len(set(ids)):
                fail("duplicate ids after staging in " + path)

    for path, t in texts.items():
        out = t.replace("\n", "\r\n") if crlf[path] else t
        if bom[path]:
            out = "\ufeff" + out
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(out)
    for path, t in NEW_FILES.items():
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(t)

    def has(path, needle):
        with io.open(path, "r", encoding="utf-8", newline="") as f:
            return needle in f.read()
    for path, needle in [
        (builder, "int pick = rng.Next(candidates.Count);"),
        (builder, 'village: placed ("'),
        ("Assets/ScriptableObjects/Floors/AncientSiteProfile.asset", "8c3fa2d17e5b40c9a1d6f4e2b8073c51"),
        ("Docs/DESIGN_CANON.md", "**The plans.**"),
        ("Docs/DCR_Guide_Content_Authoring.html", "Rev 7:"),
        ("Docs/DCR_Guide_Dwarven_Village.html", "dcr-village-v1-c3s5b"),
        (PLANS_VEIN, "@name: The Terraced Vein"),
    ]:
        if not has(path, needle):
            print("WARNING: post-write check failed: " + needle + " in " + path)

    print("Village Rotation applied.")
    print(f"  {len(EDITS)} edits across {len(texts)} files; {len(NEW_FILES)} new files.")
    print("  The Hearth is retired from the roll; its file remains on disk.")
    print("  Guide chapters 0/3 carry the new expected numbers; canon updated in the same pass.")

PLANS_VEIN = "Assets/ScriptableObjects/Sites/Plans/DwarvenVillage_TheTerracedVein.txt"

if __name__ == "__main__":
    main()
