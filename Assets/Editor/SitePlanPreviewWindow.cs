using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws every site plan -- procedural and hand-authored -- as a colour-coded
/// map, without entering play mode or generating a floor.
///
/// This exists because site geometry has three failure modes that are all
/// INVISIBLE in the plan itself and only appear in game:
///
///   1. Masonry that renders as nothing. CaveWallRenderer only paints a solid
///      cell if it is claimed or 8-adjacent to a MINED cell. Site masonry is
///      never mined, so a masonry cell buried inside a thick wall touches no
///      open floor and is never drawn -- while UnfogSite still strips its fog,
///      leaving bare floor tile where a wall should be.
///   2. Floor that cannot be walked on. A wall's face renders two cells tall
///      and drapes over the floor south of it, and the pathfinder treats those
///      cells as blocked.
///   3. Both of the above are orientation-dependent, because the drape is
///      always in world +Y.
///
/// THE MARKER GLYPHS ARE DRAWN IN HUE, NEVER IN VALUE, and that rule is the
/// whole of why this window can show them at all. A door, a lane and a heart
/// used to render as ordinary floor and ordinary masonry, so the one thing an
/// author most needs to see while drawing them was the one thing the window
/// did not have. Painting a marker as a flat colour would have hidden the
/// three faults above underneath it -- a drape-blocked lane cell would have
/// looked exactly like a working one. So every marker carries TWO colours, the
/// bright one for the passing case and the dark one for the failing case, and
/// the diagnostic survives the annotation.
///
/// A drape-blocked LANE cell is the specific fault worth hunting here: the lane
/// is the route a road takes through the site, so a lane cell buried under the
/// drape is a road that cannot thread the building. It is orientation
/// dependent, which is why the rotation slider matters more on a laned plan
/// than on any other.
///
/// THE SEAT LENS. The colours above show what a plan IS; since the chord
/// anchoring arc the interesting faults live in what the seat pipeline would
/// DO with it -- which orientation the signed rule picks, whether both gates
/// resolve, whether the lane routes, where a spur's standoff lands, and on
/// refusal WHICH stage refused. All of that used to exist only as counters in
/// a floor report. The lens asks the pipeline itself, through
/// AncientSiteBuilder.PreviewSeat, against a synthetic chord at a chosen
/// bearing: the window never re-implements the selection, because a parallel
/// copy of the ranked loop in the editor layer is exactly the drift family
/// that shipped the Abs mis-port. Two drape truths are drawn at once and kept
/// apart on purpose: the BASE colours still judge the plan in a vacuum, which
/// is render truth for cells no road serves, while the gate INSETS are judged
/// with the approach carved, which is what the engine sees at a gate. Painting
/// only the second would hide burials that are real everywhere a road is not.
///
/// Open it from Dungeon Core / Site Plan Preview.
/// </summary>
public class SitePlanPreviewWindow : EditorWindow
{
    private AncientSiteProfile profile;
    private SiteArchetype archetype = SiteArchetype.SunkenPlaza;
    private int variant;
    private int span = 30;
    private int seed = 7;
    private int rotation;
    private bool mirror;
    private bool showAuthored;
    private int authoredIndex;
    private Vector2 scroll;

    // ---- Seat lens state -------------------------------------------
    // The lens is recomputed only when its key changes: 24 PreviewSeat calls
    // per refresh walk the full ranked selection over plans of thousands of
    // cells, and OnGUI fires several times per interaction.
    private int bearingStep;
    private int chordWidth = 5;
    private string seatKey = "";
    private SeatDiag seatDiag;
    private SeatDiag[] roseDiags;
    private bool overlayOn;
    private readonly HashSet<Vector2Int> gateChosen = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> gateBuried = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> routeCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> chordCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> waypointCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> ringCells = new HashSet<Vector2Int>();

    private static readonly Color ColFloorOk = new Color(0.30f, 0.55f, 0.35f);
    private static readonly Color ColFloorBlocked = new Color(0.55f, 0.45f, 0.20f);
    private static readonly Color ColWallOk = new Color(0.62f, 0.58f, 0.66f);
    private static readonly Color ColWallDead = new Color(0.85f, 0.15f, 0.20f);
    private static readonly Color ColEmpty = new Color(0.08f, 0.08f, 0.10f);

    // The marker pairs. Bright is the passing case, dark the failing one, so
    // the walkable/drape-blocked and drawn/never-drawn reads survive underneath
    // the annotation rather than being painted over by it.
    private static readonly Color ColDoorOk = new Color(0.85f, 0.66f, 0.22f);
    private static readonly Color ColDoorBlocked = new Color(0.42f, 0.32f, 0.10f);
    private static readonly Color ColLaneOk = new Color(0.34f, 0.56f, 0.76f);
    private static readonly Color ColLaneBlocked = new Color(0.16f, 0.26f, 0.36f);

    // Magenta rather than another red: the heart is masonry, and ColWallDead is
    // already a loud red on the cell beside it. Two reds meaning two different
    // things at three pixels a cell is not a legend, it is a guess.
    private static readonly Color ColHeartOk = new Color(0.80f, 0.30f, 0.70f);
    private static readonly Color ColHeartDead = new Color(0.38f, 0.12f, 0.33f);

    // Platform and stairs, the deferral reversed: the same pair rule, two
    // colours and two branches, exactly as the canon priced it. Stairs sit
    // ABOVE platform in the paint order because the stair is the one opening
    // in the platform's edge and everything has to path it -- a stair painted
    // as generic platform is the hole in the wall you cannot find.
    private static readonly Color ColPlatOk = new Color(0.30f, 0.62f, 0.62f);
    private static readonly Color ColPlatBlocked = new Color(0.14f, 0.30f, 0.30f);
    private static readonly Color ColStairOk = new Color(0.95f, 0.45f, 0.12f);
    private static readonly Color ColStairBlocked = new Color(0.45f, 0.22f, 0.06f);

    // Seat lens colours. Insets sit ON base cells, so they are chosen against
    // every base colour they can land on: the chosen gate is near-white because
    // nothing else in the palette is, and the buried inset is a deep maroon
    // that reads against both door golds.
    private static readonly Color ColGateChosen = new Color(0.95f, 0.95f, 0.92f);
    private static readonly Color ColGateBuriedIn = new Color(0.35f, 0.08f, 0.08f);
    private static readonly Color ColRoute = new Color(0.88f, 0.93f, 1.00f);
    private static readonly Color ColChord = new Color(0.46f, 0.40f, 0.30f);
    private static readonly Color ColWaypoint = new Color(0.85f, 0.85f, 0.30f);
    private static readonly Color ColRing = new Color(0.36f, 0.20f, 0.42f);

    private static readonly Color ColRoseThread = new Color(0.30f, 0.75f, 0.35f);
    private static readonly Color ColRoseSpur = new Color(0.35f, 0.60f, 0.85f);
    private static readonly Color ColRoseSidle = new Color(0.30f, 0.65f, 0.60f);
    private static readonly Color ColRoseRefuse = new Color(0.85f, 0.20f, 0.20f);

    [MenuItem("Dungeon Core/Site Plan Preview")]
    public static void Open()
    {
        GetWindow<SitePlanPreviewWindow>("Site Plans").minSize = new Vector2(520, 560);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        profile = (AncientSiteProfile)EditorGUILayout.ObjectField(
            "Profile", profile, typeof(AncientSiteProfile), false);

        var authored = profile != null ? profile.GetAuthoredPlans() : new List<AuthoredSitePlan>();

        showAuthored = EditorGUILayout.Toggle("Hand-authored plan", showAuthored);
        if (showAuthored)
        {
            if (authored.Count == 0)
            {
                EditorGUILayout.HelpBox("No authored plans on this profile.", MessageType.Info);
                showAuthored = false;
            }
            else
            {
                var names = new string[authored.Count];
                for (int i = 0; i < authored.Count; i++)
                    names[i] = authored[i].sourceName + "  (" + authored[i].archetype + ")";
                authoredIndex = EditorGUILayout.Popup("Plan", Mathf.Clamp(authoredIndex, 0, authored.Count - 1), names);
            }
        }

        if (!showAuthored)
        {
            archetype = (SiteArchetype)EditorGUILayout.EnumPopup("Archetype", archetype);
            variant = EditorGUILayout.IntSlider("Variant", variant, 0,
                Mathf.Max(0, AncientSiteProfile.VariantCountFor(archetype) - 1));
            span = EditorGUILayout.IntSlider("Span", span, 10, 70);
            seed = EditorGUILayout.IntField("Seed", seed);
        }

        EditorGUILayout.Space();
        rotation = EditorGUILayout.IntSlider("Rotation (quarter turns)", rotation, 0, 3);
        mirror = EditorGUILayout.Toggle("Mirror", mirror);

        // The markers are AUTHORED ONLY. AncientSiteBuilder.PreviewPlan hands
        // back floor and wall alone, because a procedural recipe has no doors,
        // no lane and no heart to hand back -- so these stay empty on that path
        // and every marker branch below falls through to the plain colours.
        List<Vector2Int> floorCells, wallCells;
        var doorCells = new List<Vector2Int>();
        var laneCells = new List<Vector2Int>();
        var heartCells = new List<Vector2Int>();
        var platformCells = new List<Vector2Int>();
        var stairCells = new List<Vector2Int>();
        int doorRuns = 0, doorRunsNoNormal = 0;
        bool rotatable = true;
        AuthoredSitePlan seatPlan = null;

        if (showAuthored && authored.Count > 0)
        {
            var p = authored[Mathf.Clamp(authoredIndex, 0, authored.Count - 1)];
            seatPlan = p;
            floorCells = new List<Vector2Int>(p.floor);
            wallCells = new List<Vector2Int>(p.wall);
            doorCells.AddRange(p.door);
            laneCells.AddRange(p.lane);
            heartCells.AddRange(p.heart);
            platformCells.AddRange(p.platform);
            stairCells.AddRange(p.stairs);
            rotatable = p.allowRotation;

            // Read from the UNTRANSFORMED plan on purpose. A run's outward
            // normal rotates with the plan, so a zero normal is zero in all
            // eight orientations and re-deriving it per rotation would say the
            // same thing four more times. Reported at all because a zero normal
            // is the silent failure that turned door anchoring into a no-op:
            // nothing throws, nothing looks wrong, the plan just places on its
            // centre like every other site.
            doorRuns = p.doorRuns.Count;
            foreach (var run in p.doorRuns)
                if (run.outward == Vector2Int.zero) doorRunsNoNormal++;
        }
        else
        {
            AncientSiteBuilder.PreviewPlan(archetype, variant, span, seed, out floorCells, out wallCells);
        }

        Transform(floorCells, rotation, mirror);
        Transform(wallCells, rotation, mirror);
        Transform(doorCells, rotation, mirror);
        Transform(laneCells, rotation, mirror);
        Transform(heartCells, rotation, mirror);
        Transform(platformCells, rotation, mirror);
        Transform(stairCells, rotation, mirror);

        var floorSet = new HashSet<Vector2Int>(floorCells);
        var wallSet = new HashSet<Vector2Int>(wallCells);
        var doorSet = new HashSet<Vector2Int>(doorCells);
        var laneSet = new HashSet<Vector2Int>(laneCells);
        var heartSet = new HashSet<Vector2Int>(heartCells);
        var platformSet = new HashSet<Vector2Int>(platformCells);
        var stairSet = new HashSet<Vector2Int>(stairCells);

        int walkable = 0, blocked = 0, drawn = 0, dead = 0;
        int laneBlocked = 0, doorBlocked = 0, heartDead = 0;
        foreach (var c in floorCells)
        {
            bool ok = Walkable(c, floorSet);
            if (ok) walkable++;
            else
            {
                blocked++;
                if (laneSet.Contains(c)) laneBlocked++;
                if (doorSet.Contains(c)) doorBlocked++;
            }
        }
        foreach (var c in wallCells)
        {
            if (TouchesFloor(c, floorSet)) drawn++;
            else
            {
                dead++;
                if (heartSet.Contains(c)) heartDead++;
            }
        }

        // The seat lens, authored plans only. Refreshed before the verdict is
        // drawn and before the grid asks for its overlays.
        if (seatPlan != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seat lens", EditorStyles.boldLabel);
            bearingStep = EditorGUILayout.IntSlider(
                "Chord bearing (x15 deg)", bearingStep, 0, 23);
            chordWidth = EditorGUILayout.IntSlider("Chord width", chordWidth, 3, 9);
            RefreshSeatLens(seatPlan);
        }
        else
        {
            seatDiag = null;
            roseDiags = null;
            overlayOn = false;
            seatKey = "";
            gateChosen.Clear(); gateBuried.Clear();
            routeCells.Clear(); chordCells.Clear();
            waypointCells.Clear(); ringCells.Clear();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Carved {floorCells.Count}   walkable {walkable}   " +
                                   $"blocked by wall drape {blocked}");
        EditorGUILayout.LabelField($"Masonry {wallCells.Count}   drawn {drawn}   " +
                                   $"NEVER DRAWN {dead}");

        if (doorCells.Count > 0 || laneCells.Count > 0 || heartCells.Count > 0)
            EditorGUILayout.LabelField(
                $"Doors {doorCells.Count} in {doorRuns} run(s) ({doorRunsNoNormal} with no normal)   " +
                $"lane {laneCells.Count} ({laneBlocked} drape-blocked)   " +
                $"heart {heartCells.Count}");

        if (!rotatable)
            EditorGUILayout.LabelField(
                "This plan is @rotate: no -- it only ever places at rotation 0, unmirrored.");

        if (dead > 0)
            EditorGUILayout.HelpBox(
                dead + " masonry cells touch no open floor. CaveWallRenderer will not paint " +
                "them, so if their fog is stripped they show as bare floor tile. Thin the wall, " +
                "or leave those cells fogged.", MessageType.Error);
        if (walkable < 16)
            EditorGUILayout.HelpBox(
                "Only " + walkable + " walkable cells in this orientation; the generator rejects " +
                "a site below 16. Widen the interiors -- a room needs three rows of open floor " +
                "before one cell is walkable.", MessageType.Error);
        if (laneBlocked > 0)
            EditorGUILayout.HelpBox(
                laneBlocked + " LANE cells are buried under the wall drape in this orientation. " +
                "The lane is the route a road takes through the site, so a road cannot thread " +
                "it here. Check every rotation this plan is allowed to take -- the drape is " +
                "always in world +Y, so a lane can be clear on one quarter turn and sealed on " +
                "the next.", MessageType.Error);
        if (doorBlocked > 0)
            EditorGUILayout.HelpBox(
                doorBlocked + " DOOR cells are buried under the wall drape in this orientation. " +
                "A door nothing can walk through is a wall with a marker on it. This is the " +
                "VACUUM judgement -- the gate insets below judge with the approach carved, " +
                "which is what the engine sees at a seated gate.", MessageType.Warning);
        if (doorRunsNoNormal > 0)
            EditorGUILayout.HelpBox(
                doorRunsNoNormal + " door run(s) have NO outward normal, so door anchoring will " +
                "not use them. A run gets one only when exactly one of its two flanking sides is " +
                "outside the plan entirely; a door drawn into an interior wall has building on " +
                "both sides and cannot say which way it opens.", MessageType.Warning);
        if (heartDead > 0)
            EditorGUILayout.HelpBox(
                "The HEART is buried in masonry that touches no open floor, so it is never " +
                "painted. Unsealing means mining it, and a seal-stone nobody can see is a seal " +
                "nobody breaks.", MessageType.Error);

        if (seatPlan != null)
            DrawSeatPanel(seatPlan);

        EditorGUILayout.Space();
        DrawLegend();
        EditorGUILayout.Space();
        DrawGrid(floorSet, wallSet, doorSet, laneSet, heartSet, platformSet, stairSet);

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Recomputes the lens when its key changes: the verdict at the chosen
    /// bearing, the 24-bearing rose, the per-run gate insets at the displayed
    /// orientation, and the grid overlays. Everything the pipeline decides is
    /// asked OF the pipeline -- PreviewSeat and PreviewGateCell drive the
    /// shipped selection with a diag collector; nothing here re-derives it.
    /// </summary>
    private void RefreshSeatLens(AuthoredSitePlan plan)
    {
        string key = (profile != null ? profile.GetInstanceID() : 0) + ":" + authoredIndex
            + ":" + rotation + ":" + mirror + ":" + chordWidth + ":" + bearingStep;
        if (key == seatKey) return;
        seatKey = key;

        seatDiag = AncientSiteBuilder.PreviewSeat(
            plan, bearingStep * 15, chordWidth, rotation, mirror);
        roseDiags = new SeatDiag[24];
        for (int b = 0; b < 24; b++)
            roseDiags[b] = AncientSiteBuilder.PreviewSeat(
                plan, b * 15, chordWidth, rotation, mirror);

        gateChosen.Clear();
        gateBuried.Clear();
        foreach (var run in plan.doorRuns)
        {
            var buried = new List<Vector2Int>();
            if (AncientSiteBuilder.PreviewGateCell(
                    plan, run, rotation, mirror, out var g, buried))
                gateChosen.Add(g);
            foreach (var bc in buried) gateBuried.Add(bc);
        }

        routeCells.Clear();
        chordCells.Clear();
        waypointCells.Clear();
        ringCells.Clear();

        // Overlays only when the grid shows the orientation the diag describes.
        // A doorless plan keeps the rotation it was handed; a doored plan had
        // its rotation CHOSEN, so the overlay waits for the grid to match --
        // the snap button exists for exactly this. An @rotate: no plan was
        // seated at 0 unmirrored whatever the sliders say.
        overlayOn = seatDiag != null && seatDiag.placed
            && (plan.allowRotation || (rotation == 0 && !mirror))
            && (seatDiag.doorless || seatDiag.chosenRot == rotation);

        if (overlayOn)
        {
            var pa = new Vector2Int(seatDiag.placeAt.x, seatDiag.placeAt.y);

            // The synthetic chord, in the grid's local space: PreviewSeat runs
            // it through the world origin at the bearing, +/-300, and local is
            // world minus placeAt. Built with the same rounding PreviewSeat
            // uses so the drawn line is the tested line.
            float ang = bearingStep * 15f * Mathf.Deg2Rad;
            var wa = new Vector2Int(Mathf.RoundToInt(-Mathf.Cos(ang) * 300f),
                                    Mathf.RoundToInt(-Mathf.Sin(ang) * 300f));
            var wb = new Vector2Int(Mathf.RoundToInt(Mathf.Cos(ang) * 300f),
                                    Mathf.RoundToInt(Mathf.Sin(ang) * 300f));
            PlotLine(wa - pa, wb - pa, chordCells);

            if (seatDiag.doorless)
            {
                // Nothing more to draw: the chord's offset from the footprint
                // IS the sidle, made visible.
            }
            else if (seatDiag.spurClass)
            {
                // The spur, take-off to gate, with its approach waypoints. The
                // take-off is the anchor, which PreviewSeat put at the world
                // origin, on the chord.
                var gateW = new Vector3Int(seatDiag.placeAt.x + seatDiag.entryGate.x,
                                           seatDiag.placeAt.y + seatDiag.entryGate.y, 0);
                var pts = RoadNetworkBuilder.ApproachWaypoints(
                    gateW,
                    new Vector2(seatDiag.entryNormal.x, seatDiag.entryNormal.y),
                    Vector3Int.zero);
                var chain = new List<Vector2Int> { seatDiag.entryGate };
                foreach (var p in pts)
                    chain.Add(new Vector2Int(p.x - pa.x, p.y - pa.y));
                chain.Add(new Vector2Int(-pa.x, -pa.y));
                for (int i = 0; i + 1 < chain.Count; i++)
                    PlotLine(chain[i], chain[i + 1], routeCells);
                foreach (var p in pts)
                    waypointCells.Add(new Vector2Int(p.x - pa.x, p.y - pa.y));
                waypointCells.Add(new Vector2Int(-pa.x, -pa.y));
            }
            else
            {
                // The routed lane, plus each gate's approach waypoints toward
                // the chord end it serves: the entry OPPOSES travel a-to-b, so
                // ingress arrives from a and egress leaves for b.
                foreach (var c in seatDiag.lane) routeCells.Add(c);
                var gin = new Vector3Int(seatDiag.placeAt.x + seatDiag.entryGate.x,
                                         seatDiag.placeAt.y + seatDiag.entryGate.y, 0);
                var gout = new Vector3Int(seatDiag.placeAt.x + seatDiag.exitGate.x,
                                          seatDiag.placeAt.y + seatDiag.exitGate.y, 0);
                foreach (var p in RoadNetworkBuilder.ApproachWaypoints(
                             gin,
                             new Vector2(seatDiag.entryNormal.x, seatDiag.entryNormal.y),
                             new Vector3Int(wa.x, wa.y, 0)))
                    waypointCells.Add(new Vector2Int(p.x - pa.x, p.y - pa.y));
                foreach (var p in RoadNetworkBuilder.ApproachWaypoints(
                             gout,
                             new Vector2(seatDiag.exitNormal.x, seatDiag.exitNormal.y),
                             new Vector3Int(wb.x, wb.y, 0)))
                    waypointCells.Add(new Vector2Int(p.x - pa.x, p.y - pa.y));
            }
        }

        // The keep-clear ring, for any plan the pipeline classes doorless: the
        // bounding circle FootprintClearsChords tests, plus half the chord and
        // the one-cell margin, about the displayed footprint's centre. Drawn
        // whether or not the seat succeeded -- it is a property of the plan.
        if (seatDiag != null && seatDiag.doorless)
        {
            float r = (float)AncientSiteBuilder.PreviewKeepClearRadius(plan)
                + chordWidth * 0.5f + 1f;
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in plan.floor)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            foreach (var c in plan.wall)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            if (minX <= maxX)
            {
                // The bounding-box centre, turned the way the grid is turned.
                // A quarter turn maps the box to a box, so turning the centre
                // point is turning the circle.
                float cx0 = (minX + maxX) * 0.5f;
                float cy0 = (minY + maxY) * 0.5f;
                if (mirror) cx0 = -cx0;
                float cx, cy;
                switch (rotation & 3)
                {
                    case 1: cx = -cy0; cy = cx0; break;
                    case 2: cx = -cx0; cy = -cy0; break;
                    case 3: cx = cy0; cy = -cx0; break;
                    default: cx = cx0; cy = cy0; break;
                }
                int lox = Mathf.FloorToInt(cx - r - 1f), hix = Mathf.CeilToInt(cx + r + 1f);
                int loy = Mathf.FloorToInt(cy - r - 1f), hiy = Mathf.CeilToInt(cy + r + 1f);
                for (int x = lox; x <= hix; x++)
                    for (int y = loy; y <= hiy; y++)
                    {
                        float dxr = x - cx, dyr = y - cy;
                        float d = Mathf.Sqrt(dxr * dxr + dyr * dyr);
                        if (Mathf.Abs(d - r) < 0.55f)
                            ringCells.Add(new Vector2Int(x, y));
                    }
            }
        }
    }

    /// <summary>The verdict at the chosen bearing, and the rose over all 24.</summary>
    private void DrawSeatPanel(AuthoredSitePlan plan)
    {
        if (seatDiag == null) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Seat verdict -- bearing " + (bearingStep * 15) + " deg, width " + chordWidth,
            EditorStyles.boldLabel);

        string cls = seatDiag.doorless
            ? "DOORLESS path -- no anchorable runs (only a lane or @anchor_on: door fills " +
              "them); the seat sidles clear of the chord instead"
            : seatDiag.spurClass
                ? "SPUR class -- " + seatDiag.usableRuns +
                  " usable run(s); the road does not pass through, a spur tees off to the door"
                : "THREADING class -- " + seatDiag.usableRuns +
                  " usable runs; the road passes through, gate to gate";
        EditorGUILayout.LabelField(cls, EditorStyles.wordWrappedLabel);

        if (seatDiag.haveOrientation)
        {
            string pick = "Engine picks rotation " + seatDiag.chosenRot;
            if (plan.allowRotation && seatDiag.chosenRot != rotation)
                pick += "   (grid shows " + rotation + " -- overlays wait for a match)";
            EditorGUILayout.LabelField(pick);
            if (plan.allowRotation && seatDiag.chosenRot != rotation
                && GUILayout.Button("Snap grid to the engine's pick"))
            {
                rotation = seatDiag.chosenRot;
                seatKey = "";
                Repaint();
            }
        }

        if (seatDiag.placed)
        {
            if (seatDiag.doorless)
                EditorGUILayout.LabelField(
                    "Seated: sidled to offset (" + seatDiag.placeAt.x + ", " +
                    seatDiag.placeAt.y + "), every cell clear of the carriageway.");
            else if (seatDiag.spurClass)
                EditorGUILayout.LabelField(
                    "Seated: stands off " + seatDiag.spurStandoff +
                    " cells; a door-width spur meets the gate square along its normal.");
            else
                EditorGUILayout.LabelField(
                    "Seated: threads. Entry gate (" + seatDiag.entryGate.x + ", " +
                    seatDiag.entryGate.y + "), exit gate (" + seatDiag.exitGate.x + ", " +
                    seatDiag.exitGate.y + "), lane " + seatDiag.lane.Count +
                    " cells. The road stops one cell outside each gate.");
        }
        else
        {
            EditorGUILayout.HelpBox(
                "REFUSED -- " + seatDiag.refusal + ". In game the plan retries at a fresh " +
                "anchor rather than placing badly; a refusal is only an authoring fault if " +
                "it holds at every bearing on the rose.", MessageType.Error);
        }

        if (plan.hasAnchorOverride && plan.anchorOverride == SiteAnchor.Free)
            EditorGUILayout.HelpBox(
                "This plan is @anchor: Free -- in game it never samples a chord, so this " +
                "verdict and the rose are what-if. That is the read to use when deciding " +
                "whether the anchor should change.", MessageType.Info);

        DrawRose();
    }

    /// <summary>
    /// One dot per bearing, the audit's 24-bearing sweep drawn: green threads,
    /// blue spurs, teal sidles, red refuses. The current bearing's dot is
    /// larger. Screen Y runs down, so +Y bearings are drawn upward.
    /// </summary>
    private void DrawRose()
    {
        if (roseDiags == null) return;
        EditorGUILayout.LabelField("Bearing rose -- one dot per 15 deg");
        var area = GUILayoutUtility.GetRect(124, 124, GUILayout.Width(124));
        EditorGUI.DrawRect(area, ColEmpty);
        var mid = area.center;
        int th = 0, sp = 0, sd = 0, rf = 0;
        for (int b = 0; b < 24; b++)
        {
            var d = roseDiags[b];
            Color col;
            if (d == null || !d.placed) { col = ColRoseRefuse; rf++; }
            else if (d.doorless) { col = ColRoseSidle; sd++; }
            else if (d.spurClass) { col = ColRoseSpur; sp++; }
            else { col = ColRoseThread; th++; }
            float ang = b * 15f * Mathf.Deg2Rad;
            float x = mid.x + Mathf.Cos(ang) * 46f;
            float y = mid.y - Mathf.Sin(ang) * 46f;
            float s = b == bearingStep ? 9f : 6f;
            EditorGUI.DrawRect(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), col);
        }
        EditorGUILayout.LabelField(
            th + " thread, " + sp + " spur, " + sd + " sidle, " + rf + " refuse");
    }

    /// <summary>The drape rule, in one place. A floor cell is walkable only
    /// when y+1 AND y+2 are also floor: a wall's face renders two cells tall
    /// and the pathfinder treats what it covers as blocked.</summary>
    private static bool Walkable(Vector2Int c, HashSet<Vector2Int> floorSet)
    {
        return floorSet.Contains(new Vector2Int(c.x, c.y + 1))
            && floorSet.Contains(new Vector2Int(c.x, c.y + 2));
    }

    private static bool TouchesFloor(Vector2Int c, HashSet<Vector2Int> floorSet)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (floorSet.Contains(new Vector2Int(c.x + dx, c.y + dy))) return true;
            }
        return false;
    }

    private static void Transform(List<Vector2Int> cells, int rot, bool mirror)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            int x = mirror ? -cells[i].x : cells[i].x;
            int y = cells[i].y;
            switch (rot & 3)
            {
                case 1: cells[i] = new Vector2Int(-y, x); break;
                case 2: cells[i] = new Vector2Int(-x, -y); break;
                case 3: cells[i] = new Vector2Int(y, -x); break;
                default: cells[i] = new Vector2Int(x, y); break;
            }
        }
    }

    /// <summary>Four-connected Bresenham, display only: the overlay lines the
    /// lens draws, never geometry the engine acts on.</summary>
    private static void PlotLine(Vector2Int p0, Vector2Int p1, HashSet<Vector2Int> into)
    {
        int dx = Mathf.Abs(p1.x - p0.x), sx = p0.x < p1.x ? 1 : -1;
        int dy = -Mathf.Abs(p1.y - p0.y), sy = p0.y < p1.y ? 1 : -1;
        int err = dx + dy;
        var p = p0;
        int guard = 2 * (dx - dy) + 4;
        while (guard-- > 0)
        {
            into.Add(p);
            if (p == p1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; p.x += sx; }
            if (e2 <= dx) { err += dx; p.y += sy; }
        }
    }

    private void DrawLegend()
    {
        EditorGUILayout.BeginHorizontal();
        Swatch(ColFloorOk, "walkable");
        Swatch(ColFloorBlocked, "floor, drape-blocked");
        Swatch(ColWallOk, "masonry, drawn");
        Swatch(ColWallDead, "masonry, NEVER drawn");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        Swatch(ColDoorOk, "door '+'");
        Swatch(ColDoorBlocked, "door, drape-blocked");
        Swatch(ColLaneOk, "lane '~'");
        Swatch(ColLaneBlocked, "lane, drape-blocked");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        Swatch(ColHeartOk, "heart 'X'");
        Swatch(ColHeartDead, "heart, NEVER drawn");
        Swatch(ColPlatOk, "platform '='");
        Swatch(ColStairOk, "stairs '^'");
        EditorGUILayout.EndHorizontal();

        if (showAuthored)
        {
            EditorGUILayout.BeginHorizontal();
            Swatch(ColGateChosen, "gate cell (inset)");
            Swatch(ColGateBuriedIn, "run cell buried (inset)");
            Swatch(ColRoute, "route / spur");
            Swatch(ColWaypoint, "waypoint / take-off");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            Swatch(ColChord, "chord centreline");
            Swatch(ColRing, "keep-clear ring");
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void Swatch(Color c, string label)
    {
        var r = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
        EditorGUI.DrawRect(r, c);
        EditorGUILayout.LabelField(label, GUILayout.Width(130));
    }

    private void DrawGrid(HashSet<Vector2Int> floorSet, HashSet<Vector2Int> wallSet,
                          HashSet<Vector2Int> doorSet, HashSet<Vector2Int> laneSet,
                          HashSet<Vector2Int> heartSet, HashSet<Vector2Int> platformSet,
                          HashSet<Vector2Int> stairSet)
    {
        if (floorSet.Count == 0 && wallSet.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in floorSet) Extend(c, ref minX, ref maxX, ref minY, ref maxY);
        foreach (var c in wallSet) Extend(c, ref minX, ref maxX, ref minY, ref maxY);
        foreach (var c in ringCells) Extend(c, ref minX, ref maxX, ref minY, ref maxY);

        // Room for the lens overlays: the chord passes within a standoff or a
        // sidle of the footprint, so a fixed margin shows the road arriving.
        if (chordCells.Count > 0 || routeCells.Count > 0)
        {
            minX -= 6; maxX += 6; minY -= 6; maxY += 6;
        }

        int w = maxX - minX + 1, h = maxY - minY + 1;
        float px = Mathf.Clamp(460f / Mathf.Max(w, h), 2f, 14f);

        var area = GUILayoutUtility.GetRect(w * px, h * px);
        EditorGUI.DrawRect(area, ColEmpty);

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var c = new Vector2Int(x, y);
                // Initialised, not merely declared: the compiler cannot see
                // that `have` tracks assignment through the chain below, and a
                // waypoint on an otherwise empty cell reaches the draw.
                Color col = ColEmpty;
                bool have = true;

                // MASONRY FIRST, and the heart inside it. The parser puts 'X'
                // in the wall set as well as the heart list -- it is a marker on
                // a cell that is already masonry, not a third kind of cell --
                // so testing the heart before the wall is what keeps it from
                // being painted as ordinary stone.
                if (wallSet.Contains(c))
                {
                    bool shown = TouchesFloor(c, floorSet);
                    if (heartSet.Contains(c)) col = shown ? ColHeartOk : ColHeartDead;
                    else col = shown ? ColWallOk : ColWallDead;
                }
                else if (floorSet.Contains(c))
                {
                    bool ok = Walkable(c, floorSet);
                    if (doorSet.Contains(c)) col = ok ? ColDoorOk : ColDoorBlocked;
                    else if (laneSet.Contains(c)) col = ok ? ColLaneOk : ColLaneBlocked;
                    else if (stairSet.Contains(c)) col = ok ? ColStairOk : ColStairBlocked;
                    else if (platformSet.Contains(c)) col = ok ? ColPlatOk : ColPlatBlocked;
                    else col = ok ? ColFloorOk : ColFloorBlocked;
                }
                else if (chordCells.Contains(c)) col = ColChord;
                else if (routeCells.Contains(c)) col = ColRoute;
                else if (ringCells.Contains(c)) col = ColRing;
                else have = false;

                if (!have && !waypointCells.Contains(c)) continue;

                // Screen Y runs down, plan Y runs up.
                var r = new Rect(area.x + (x - minX) * px,
                                 area.y + (maxY - y) * px, px, px);
                EditorGUI.DrawRect(r, col);

                // The insets: the seat lens drawn ON the diagnostic colours
                // rather than over them, so the vacuum reads survive.
                float ins = Mathf.Max(1f, px * 0.44f);
                float off = (px - ins) * 0.5f;
                var ri = new Rect(r.x + off, r.y + off, ins, ins);
                if (waypointCells.Contains(c)) EditorGUI.DrawRect(ri, ColWaypoint);
                if (routeCells.Contains(c) && floorSet.Contains(c))
                    EditorGUI.DrawRect(ri, ColRoute);
                if (gateBuried.Contains(c)) EditorGUI.DrawRect(ri, ColGateBuriedIn);
                if (gateChosen.Contains(c)) EditorGUI.DrawRect(ri, ColGateChosen);
            }
    }

    private static void Extend(Vector2Int c, ref int minX, ref int maxX, ref int minY, ref int maxY)
    {
        if (c.x < minX) minX = c.x;
        if (c.x > maxX) maxX = c.x;
        if (c.y < minY) minY = c.y;
        if (c.y > maxY) maxY = c.y;
    }
}
