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
