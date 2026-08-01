using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One hand-authored site plan, parsed from an ASCII grid. Sits alongside the
/// procedural recipes in AncientSiteBuilder rather than replacing them: an
/// authored plan is simply an extra VARIANT of its archetype, so it inherits
/// the whole placement layer -- band, anchors, spacing, disc clamp, rotation,
/// the walkability guard, save, reveal and terrain override -- unchanged.
/// </summary>
public class AuthoredSitePlan
{
    public string name = "";
    public string sourceName = "";
    public SiteArchetype archetype = SiteArchetype.GuardPost;

    /// <summary>Set only when the plan declared its own @anchor. Otherwise the
    /// archetype's fixed preference applies.</summary>
    public SiteAnchor anchorOverride = SiteAnchor.Free;
    public bool hasAnchorOverride;

    /// <summary>Cleared by "@rotate: no" for a plan whose orientation carries
    /// meaning. Costs variety, so it is off by default.</summary>
    public bool allowRotation = true;

    /// <summary>Local cells, centred on the plan's bounding box.</summary>
    public readonly List<Vector2Int> floor = new List<Vector2Int>();
    public readonly List<Vector2Int> wall = new List<Vector2Int>();
}

/// <summary>
/// Parses the authored-plan text format.
///
/// THE FORMAT
///   Lines beginning '@' are metadata: "@archetype: SealedGate", "@name: ...",
///   "@anchor: RoadEnd", "@rotate: no". Lines beginning '//' are comments.
///   Everything else is the grid, read top to bottom as NORTH to SOUTH:
///     '#'  masonry  -- stays solid rock, retyped to Ruins
///     '.'  carved   -- open floor
///     anything else (including space) is not part of the site at all
///
///   Rows may be ragged; short rows are simply short. The grid is centred on
///   its own bounding box automatically, so the author never thinks about
///   origins.
///
/// WHY TEXT RATHER THAN A SCENE
///   Authoring in a scene would need two tilemaps, an editor bake step reading
///   cellBounds, and a re-bake on every tweak -- none of which exists in the
///   project. A monospace text file needs no tooling at all, diffs cleanly in
///   git, and can be read at a glance. The headless report already draws sites
///   as ASCII, so the authoring format and the debug output match.
/// </summary>
public static class AncientSitePlanLibrary
{
    /// <summary>Parses one plan. Returns null and sets `error` on a bad file --
    /// authoring mistakes are reported, never silently swallowed.</summary>
    public static AuthoredSitePlan Parse(string text, string sourceName, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(text))
        {
            error = "file is empty";
            return null;
        }

        var plan = new AuthoredSitePlan { sourceName = sourceName, name = sourceName };
        bool archetypeSet = false;

        var rows = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("//")) continue;

            if (trimmed.StartsWith("@"))
            {
                int colon = trimmed.IndexOf(':');
                if (colon < 0) continue;
                string key = trimmed.Substring(1, colon - 1).Trim().ToLowerInvariant();
                string val = trimmed.Substring(colon + 1).Trim();

                switch (key)
                {
                    case "archetype":
                        if (!TryParseArchetype(val, out var a))
                        {
                            error = "unknown @archetype '" + val + "'";
                            return null;
                        }
                        plan.archetype = a;
                        archetypeSet = true;
                        break;
                    case "name":
                        plan.name = val;
                        break;
                    case "anchor":
                        if (!TryParseAnchor(val, out var an))
                        {
                            error = "unknown @anchor '" + val + "'";
                            return null;
                        }
                        plan.anchorOverride = an;
                        plan.hasAnchorOverride = true;
                        break;
                    case "rotate":
                        plan.allowRotation = !(val.ToLowerInvariant() == "no"
                                            || val.ToLowerInvariant() == "false"
                                            || val == "0");
                        break;
                }
                continue;
            }

            // Leading blank lines are skipped so a file may breathe between its
            // header and its grid; blanks inside the grid are meaningful rows.
            if (trimmed.Length == 0 && rows.Count == 0) continue;
            rows.Add(raw);
        }

        while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
            rows.RemoveAt(rows.Count - 1);

        if (!archetypeSet)
        {
            error = "missing '@archetype:' header";
            return null;
        }

        var floor = new List<Vector2Int>();
        var wall = new List<Vector2Int>();
        for (int r = 0; r < rows.Count; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                // Top of the file is NORTH, so the row index runs down -Y.
                if (row[c] == '#') wall.Add(new Vector2Int(c, -r));
                else if (row[c] == '.') floor.Add(new Vector2Int(c, -r));
            }
        }

        if (floor.Count == 0)
        {
            error = "grid has no carved cells ('.') -- a site with no interior is a rock";
            return null;
        }

        // Centre on the bounding box of the WHOLE plan, masonry included, so a
        // plan with a heavy wall on one side does not sit off its own anchor.
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in floor)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        foreach (var p in wall)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        var offset = new Vector2Int((minX + maxX) / 2, (minY + maxY) / 2);

        var wallSet = new HashSet<Vector2Int>();
        foreach (var p in wall) wallSet.Add(p - offset);

        foreach (var p in floor)
        {
            var q = p - offset;
            // Masonry wins a collision, matching the procedural composer.
            if (!wallSet.Contains(q)) plan.floor.Add(q);
        }
        plan.wall.AddRange(wallSet);

        return plan;
    }

    /// <summary>Parses a whole set, logging and skipping anything malformed.
    /// One bad file never costs the others.</summary>
    public static List<AuthoredSitePlan> LoadAll(IEnumerable<TextAsset> assets)
    {
        var result = new List<AuthoredSitePlan>();
        if (assets == null) return result;

        foreach (var asset in assets)
        {
            if (asset == null) continue;
            var plan = Parse(asset.text, asset.name, out string error);
            if (plan == null)
            {
                Debug.LogWarning("[AncientSitePlan] '" + asset.name + "' skipped: " + error);
                continue;
            }
            result.Add(plan);
        }
        return result;
    }

    public static bool TryParseArchetype(string s, out SiteArchetype value)
    {
        value = SiteArchetype.GuardPost;
        if (string.IsNullOrEmpty(s)) return false;
        string k = s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        for (int i = 0; i <= (int)SiteArchetype.TollHouse; i++)
        {
            var candidate = (SiteArchetype)i;
            if (candidate.ToString().ToLowerInvariant() == k)
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }

    public static bool TryParseAnchor(string s, out SiteAnchor value)
    {
        value = SiteAnchor.Free;
        if (string.IsNullOrEmpty(s)) return false;
        string k = s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
        for (int i = 0; i <= (int)SiteAnchor.Crossing; i++)
        {
            var candidate = (SiteAnchor)i;
            if (candidate.ToString().ToLowerInvariant() == k)
            {
                value = candidate;
                return true;
            }
        }
        return false;
    }
}
