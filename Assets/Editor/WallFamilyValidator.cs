using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon Core -> Validate Wall Families: cross-checks every wall family's
/// terrain against the systems that consume it. The layout's own Validate
/// Layout covers slot correctness; THIS menu covers the wiring a slot check
/// cannot see:
///
///   - TerrainResistanceTable coverage. GetResistance silently returns 1.0x
///     for a terrain with no entry -- dwarven walls at dirt cost, and no
///     error anywhere. That silent fallback is the failure this menu exists
///     to catch, in seconds instead of a mining test.
///   - Pattern mapping. A family terrain with no pattern id teaches nothing
///     on first claim; legal, but worth a note.
///   - Spoil ledger. Reported informationally -- 0 is a real stance, not an
///     error.
///
/// Runs over every CaveWallSheetLayout in the project against every
/// TerrainResistanceTable, so a second layout asset someday is covered free.
/// </summary>
public static class WallFamilyValidator
{
    [MenuItem("Dungeon Core/Validate Wall Families")]
    public static void Validate()
    {
        var sb = new StringBuilder();
        int issues = 0;

        string[] layoutGuids = AssetDatabase.FindAssets("t:CaveWallSheetLayout");
        string[] tableGuids = AssetDatabase.FindAssets("t:TerrainResistanceTable");

        if (layoutGuids.Length == 0)
        { sb.AppendLine("- No CaveWallSheetLayout asset in the project."); issues++; }
        if (tableGuids.Length == 0)
        { sb.AppendLine("- No TerrainResistanceTable asset in the project."); issues++; }

        foreach (string lg in layoutGuids)
        {
            var layout = AssetDatabase.LoadAssetAtPath<CaveWallSheetLayout>(AssetDatabase.GUIDToAssetPath(lg));
            if (layout == null) continue;

            if (layout.families == null || layout.families.Count == 0)
            {
                sb.AppendLine($"- '{layout.name}': no wall families (every terrain renders stone). Legal; noting it.");
                continue;
            }

            for (int f = 0; f < layout.families.Count; f++)
            {
                var fam = layout.families[f];
                if (fam == null) { sb.AppendLine($"- '{layout.name}' family[{f}]: null entry."); issues++; continue; }
                string famName = $"'{layout.name}' family[{f}] {fam.terrain}";

                foreach (string tg in tableGuids)
                {
                    var table = AssetDatabase.LoadAssetAtPath<TerrainResistanceTable>(AssetDatabase.GUIDToAssetPath(tg));
                    if (table == null) continue;
                    if (!table.HasEntry(fam.terrain))
                    {
                        sb.AppendLine($"- {famName}: NO entry in TerrainResistanceTable '{table.name}'. " +
                                      "GetResistance will silently return 1.0x -- these walls mine at dirt cost.");
                        issues++;
                    }
                    else
                    {
                        sb.AppendLine($"- {famName}: resistance {table.GetResistance(fam.terrain)}x " +
                                      $"('{table.GetDisplayName(fam.terrain)}', table '{table.name}').");
                    }
                }

                if (!PatternDiscovery.HasTerrainPattern(fam.terrain))
                    sb.AppendLine($"- Note: {famName}: no material pattern mapped; first claim teaches nothing.");

                sb.AppendLine($"- {famName}: Deep Holds spoil value {DwarvenSpoil.ValueOf(fam.terrain)} gold per mined cell.");
            }
        }

        if (issues == 0)
            Debug.Log($"[WallFamilyValidator] No issues.\n{sb}");
        else
            Debug.LogWarning($"[WallFamilyValidator] {issues} issue(s).\n{sb}");
    }
}
