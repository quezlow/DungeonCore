using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validates every hand-authored site plan on an AncientSiteProfile WITHOUT
/// entering play mode or generating a floor.
///
/// This exists because the constraint that governs site geometry is invisible
/// while you are drawing: a wall's rendered face is TWO cells tall and drapes
/// over the open floor south of it, and those cells are solid to the
/// pathfinder. A room can therefore look perfectly enterable in the text file
/// and behave as a wall in game. Worse, the drape is always in world +Y, so a
/// plan can be fine on one quarter turn and impassable on another.
///
/// The validator applies the real rule to all eight orientations and reports
/// the WORST case, which is the only number that matters -- the generator will
/// eventually roll that orientation.
/// </summary>
public static class SitePlanValidator
{
    /// <summary>Mirrors AncientSiteBuilder.MinWalkableCells. If that constant
    /// moves, move this one.</summary>
    private const int MinWalkableCells = 16;

    [MenuItem("Dungeon Core/Validate Site Plans")]
    public static void Validate()
    {
        var profiles = new List<AncientSiteProfile>();
        foreach (var guid in AssetDatabase.FindAssets("t:AncientSiteProfile"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var p = AssetDatabase.LoadAssetAtPath<AncientSiteProfile>(path);
            if (p != null) profiles.Add(p);
        }

        if (profiles.Count == 0)
        {
            Debug.LogWarning("[SitePlanValidator] No AncientSiteProfile asset found.");
            return;
        }

        int totalPlans = 0, failures = 0;
        var sb = new StringBuilder();

        foreach (var profile in profiles)
        {
            var plans = profile.GetAuthoredPlans();

            // Decor entries: each must name a real authored plan, and that plan
            // must be @rotate: no -- a decor prefab does not rotate with the
            // site, so a rotatable decorated plan WILL misalign on three of four
            // quarter turns. Checked before the walkability loop so a decor
            // failure is visible even when every plan passes.
            if (profile.SiteDecor != null)
                foreach (var entry in profile.SiteDecor)
                {
                    if (entry == null || entry.prefab == null) continue;
                    AuthoredSitePlan match = null;
                    foreach (var p in plans)
                        if (p != null && p.name == entry.planName) { match = p; break; }
                    if (match == null)
                    {
                        failures++;
                        sb.Append("  DECOR FAIL: entry '").Append(entry.planName)
                          .Append("' names no authored plan on this profile.\n");
                        continue;
                    }
                    if (match.allowRotation)
                    {
                        failures++;
                        sb.Append("  DECOR FAIL: '").Append(entry.planName)
                          .Append("' allows rotation; a decorated plan must be @rotate: no.\n");
                    }
                }
            sb.Append("\n=== ").Append(profile.name).Append(" -- ")
              .Append(plans.Count).Append(" authored plan(s) ===\n");

            if (plans.Count == 0)
            {
                sb.Append("  (none assigned; all sites will use the procedural recipes)\n");
                continue;
            }

            foreach (var plan in plans)
            {
                totalPlans++;

                Debug.Log($"PLAN LOADED: '{plan.sourceName}' floor={plan.floor.Count} wall={plan.wall.Count}");


                // THE HEART and THE DOOR RULE. Both were learned by drawing the
                // eight Church plans and failing seven of them: an alcove two
                // cells deep leaves a two-cell run pinched between wall above
                // and wall below, and that seals under the drape on some
                // quarter turns. The walkability count above does not catch it,
                // because the room stays walkable while the alcove quietly
                // stops being reachable.
                if (plan.heart.Count > 1)
                {
                    failures++;
                    sb.Append("  HEART FAIL: '").Append(plan.sourceName)
                      .Append("' has ").Append(plan.heart.Count)
                      .Append(" heart cells ('X'); at most one is allowed.\n");
                }
                if (plan.heart.Count == 1 && !HeartHasClearance(plan))
                {
                    failures++;
                    sb.Append("  HEART FAIL: '").Append(plan.sourceName)
                      .Append("' has a heart with fewer than three clear cells on one side.")
                      .Append("\n         -> a free-standing heart needs a chamber of at")
                      .Append("\n            least seven by seven around it.\n");
                }

                var pinch = FindPinch(plan);
                if (pinch.HasValue)
                {
                    failures++;
                    sb.Append("  DOOR RULE FAIL: '").Append(plan.sourceName)
                      .Append("' has an open run shorter than three cells pinched")
                      .Append(" between solid at ").Append(pinch.Value)
                      .Append(".\n         -> widen it to three, or make the recess three deep.\n");
                }

                int worst = int.MaxValue, best = 0;
                int worstRot = 0;
                bool worstMirror = false;

                int rotations = plan.allowRotation ? 4 : 1;
                for (int rot = 0; rot < rotations; rot++)
                {
                    int mirrors = plan.allowRotation ? 2 : 1;
                    for (int m = 0; m < mirrors; m++)
                    {
                        int w = CountWalkable(plan.floor, rot, m == 1);
                        if (w > best) best = w;
                        if (w < worst)
                        {
                            worst = w;
                            worstRot = rot;
                            worstMirror = m == 1;
                        }
                    }
                }

                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
                foreach (var c in plan.floor) Extend(c, ref minX, ref maxX, ref minY, ref maxY);
                foreach (var c in plan.wall) Extend(c, ref minX, ref maxX, ref minY, ref maxY);

                bool ok = worst >= MinWalkableCells;
                if (!ok) failures++;

                sb.Append(ok ? "  OK   " : "  FAIL ")
                  .Append(plan.sourceName.PadRight(34))
                  .Append(plan.archetype.ToString().PadRight(17))
                  .Append((maxX - minX + 1).ToString()).Append('x').Append((maxY - minY + 1).ToString())
                  .Append("  carved ").Append(plan.floor.Count)
                  .Append("  masonry ").Append(plan.wall.Count)
                  .Append("  walkable ").Append(worst).Append("..").Append(best);

                if (!plan.allowRotation) sb.Append("  [rotation off]");
                if (plan.hasAnchorOverride) sb.Append("  [anchor ").Append(plan.anchorOverride).Append(']');

                if (!ok)
                {
                    sb.Append("\n         worst orientation: rot ").Append(worstRot * 90)
                      .Append(worstMirror ? " mirrored" : "")
                      .Append(" gives ").Append(worst).Append(" walkable cells, need ")
                      .Append(MinWalkableCells)
                      .Append("\n         -> widen the interiors. A room needs THREE rows of open floor")
                      .Append("\n            before one cell is walkable, and about five to be usable.");
                }
                sb.Append('\n');
            }
        }

        sb.Append("\n").Append(totalPlans).Append(" plan(s) checked, ")
          .Append(failures).Append(" failure(s).\n");

        if (failures > 0)
            Debug.LogError("[SitePlanValidator]" + sb);
        else
            Debug.Log("[SitePlanValidator]" + sb);
    }


    /// <summary>Three clear cells on all four sides of the heart. Anything less
    /// and the run beside it is a two-cell pinch, which seals under the drape.</summary>
    private static bool HeartHasClearance(AuthoredSitePlan plan)
    {
        var open = new HashSet<Vector2Int>(plan.floor);
        var h = plan.heart[0];
        var dirs = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };
        foreach (var d in dirs)
            for (int i = 1; i <= 3; i++)
                if (!open.Contains(new Vector2Int(h.x + d.x * i, h.y + d.y * i))) return false;
        return true;
    }

    /// <summary>The first open run under three cells long with solid on BOTH
    /// ends, on either axis. Rotation-independent: a pinch is a pinch on every
    /// quarter turn, so this is checked once rather than eight times.</summary>
    private static Vector2Int? FindPinch(AuthoredSitePlan plan)
    {
        var open = new HashSet<Vector2Int>(plan.floor);
        var solid = new HashSet<Vector2Int>(plan.wall);

        for (int axis = 0; axis < 2; axis++)
        {
            var lines = new Dictionary<int, List<int>>();
            foreach (var c in open)
            {
                int key = axis == 0 ? c.y : c.x;
                int val = axis == 0 ? c.x : c.y;
                if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<int>();
                list.Add(val);
            }

            foreach (var kv in lines)
            {
                var vs = kv.Value;
                vs.Sort();
                int start = 0;
                for (int i = 1; i <= vs.Count; i++)
                {
                    if (i < vs.Count && vs[i] == vs[i - 1] + 1) continue;
                    int len = i - start;
                    if (len < 3)
                    {
                        var before = Cell(axis, kv.Key, vs[start] - 1);
                        var after = Cell(axis, kv.Key, vs[i - 1] + 1);
                        if (solid.Contains(before) && solid.Contains(after))
                            return Cell(axis, kv.Key, vs[start]);
                    }
                    start = i;
                }
            }
        }
        return null;
    }

    private static Vector2Int Cell(int axis, int key, int val)
        => axis == 0 ? new Vector2Int(val, key) : new Vector2Int(key, val);

    private static void Extend(Vector2Int c, ref int minX, ref int maxX, ref int minY, ref int maxY)
    {
        if (c.x < minX) minX = c.x;
        if (c.x > maxX) maxX = c.x;
        if (c.y < minY) minY = c.y;
        if (c.y > maxY) maxY = c.y;
    }

    /// <summary>The pathfinder's rule: a cell is walkable only if the two cells
    /// directly north of it are also open, because that is the wall face
    /// draping down over it.</summary>
    private static int CountWalkable(List<Vector2Int> cells, int rot, bool mirror)
    {
        var set = new HashSet<Vector2Int>();
        foreach (var c in cells) set.Add(Transform(c, rot, mirror));

        int n = 0;
        foreach (var c in set)
        {
            if (!set.Contains(new Vector2Int(c.x, c.y + 1))) continue;
            if (!set.Contains(new Vector2Int(c.x, c.y + 2))) continue;
            n++;
        }
        return n;
    }

    private static Vector2Int Transform(Vector2Int p, int rot, bool mirror)
    {
        int x = mirror ? -p.x : p.x;
        int y = p.y;
        switch (rot & 3)
        {
            case 1: return new Vector2Int(-y, x);
            case 2: return new Vector2Int(-x, -y);
            case 3: return new Vector2Int(y, -x);
            default: return new Vector2Int(x, y);
        }
    }
}
