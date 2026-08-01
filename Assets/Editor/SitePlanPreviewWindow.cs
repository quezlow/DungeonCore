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

        List<Vector2Int> floorCells, wallCells;
        if (showAuthored && authored.Count > 0)
        {
            var p = authored[Mathf.Clamp(authoredIndex, 0, authored.Count - 1)];
            floorCells = new List<Vector2Int>(p.floor);
            wallCells = new List<Vector2Int>(p.wall);
        }
        else
        {
            AncientSiteBuilder.PreviewPlan(archetype, variant, span, seed, out floorCells, out wallCells);
        }

        Transform(floorCells, rotation, mirror);
        Transform(wallCells, rotation, mirror);

        var floorSet = new HashSet<Vector2Int>(floorCells);
        var wallSet = new HashSet<Vector2Int>(wallCells);

        int walkable = 0, blocked = 0, drawn = 0, dead = 0;
        foreach (var c in floorCells)
        {
            if (floorSet.Contains(new Vector2Int(c.x, c.y + 1))
                && floorSet.Contains(new Vector2Int(c.x, c.y + 2))) walkable++;
            else blocked++;
        }
        foreach (var c in wallCells)
        {
            if (TouchesFloor(c, floorSet)) drawn++;
            else dead++;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Carved {floorCells.Count}   walkable {walkable}   " +
                                   $"blocked by wall drape {blocked}");
        EditorGUILayout.LabelField($"Masonry {wallCells.Count}   drawn {drawn}   " +
                                   $"NEVER DRAWN {dead}");

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

        EditorGUILayout.Space();
        DrawLegend();
        EditorGUILayout.Space();
        DrawGrid(floorSet, wallSet);

        EditorGUILayout.EndScrollView();
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
    }

    private static void Swatch(Color c, string label)
    {
        var r = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
        EditorGUI.DrawRect(r, c);
        EditorGUILayout.LabelField(label, GUILayout.Width(130));
    }

    private void DrawGrid(HashSet<Vector2Int> floorSet, HashSet<Vector2Int> wallSet)
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
                if (wallSet.Contains(c))
                    col = TouchesFloor(c, floorSet) ? ColWallOk : ColWallDead;
                else if (floorSet.Contains(c))
                    col = (floorSet.Contains(new Vector2Int(x, y + 1))
                        && floorSet.Contains(new Vector2Int(x, y + 2)))
                        ? ColFloorOk : ColFloorBlocked;
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
