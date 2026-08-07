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
        int doorRuns = 0, doorRunsNoNormal = 0;
        bool rotatable = true;

        if (showAuthored && authored.Count > 0)
        {
            var p = authored[Mathf.Clamp(authoredIndex, 0, authored.Count - 1)];
            floorCells = new List<Vector2Int>(p.floor);
            wallCells = new List<Vector2Int>(p.wall);
            doorCells.AddRange(p.door);
            laneCells.AddRange(p.lane);
            heartCells.AddRange(p.heart);
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

        var floorSet = new HashSet<Vector2Int>(floorCells);
        var wallSet = new HashSet<Vector2Int>(wallCells);
        var doorSet = new HashSet<Vector2Int>(doorCells);
        var laneSet = new HashSet<Vector2Int>(laneCells);
        var heartSet = new HashSet<Vector2Int>(heartCells);

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
                "A door nothing can walk through is a wall with a marker on it.", MessageType.Warning);
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

        EditorGUILayout.Space();
        DrawLegend();
        EditorGUILayout.Space();
        DrawGrid(floorSet, wallSet, doorSet, laneSet, heartSet);

        EditorGUILayout.EndScrollView();
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
        EditorGUILayout.EndHorizontal();
    }

    private static void Swatch(Color c, string label)
    {
        var r = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
        EditorGUI.DrawRect(r, c);
        EditorGUILayout.LabelField(label, GUILayout.Width(130));
    }

    private void DrawGrid(HashSet<Vector2Int> floorSet, HashSet<Vector2Int> wallSet,
                          HashSet<Vector2Int> doorSet, HashSet<Vector2Int> laneSet,
                          HashSet<Vector2Int> heartSet)
    {
        if (floorSet.Count == 0 && wallSet.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in floorSet) Extend(c, ref minX, ref maxX, ref minY, ref maxY);
        foreach (var c in wallSet) Extend(c, ref minX, ref maxX, ref minY, ref maxY);

        int w = maxX - minX + 1, h = maxY - minY + 1;
        float px = Mathf.Clamp(460f / Mathf.Max(w, h), 2f, 14f);

        var area = GUILayoutUtility.GetRect(w * px, h * px);
        EditorGUI.DrawRect(area, ColEmpty);

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var c = new Vector2Int(x, y);
                Color col;

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
                    else col = ok ? ColFloorOk : ColFloorBlocked;
                }
                else continue;

                // Screen Y runs down, plan Y runs up.
                var r = new Rect(area.x + (x - minX) * px,
                                 area.y + (maxY - y) * px, px, px);
                EditorGUI.DrawRect(r, col);
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
