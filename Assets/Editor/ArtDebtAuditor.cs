using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon Core -> Audit Art Debt: the standing account of which art slots are
/// owed, which are deliberately empty, and which are wearing borrowed clothes.
///
/// It exists because every art consumer in this project degrades GRACEFULLY by
/// design -- the spell picker renders a plain block, the tech tree renders a
/// plain block, the panel button row falls back to its text label, and a monster
/// prefab variant with no sprite override silently wears the base prefab's body.
/// Every one of those fallbacks is correct and deliberate. Their combined effect
/// is that the debt is INVISIBLE in play: a dungeon with no icons at all looks
/// exactly like a dungeon that is finished. Canon says as much of the roster --
/// "stand-in sprites borrowed from donor prefabs and no icons, the roster norm,
/// pending bespoke art" -- and a norm nobody can measure is a norm nobody pays
/// down.
///
/// Two things a naive null-check would get wrong, and the reason this file is
/// longer than one:
///
///   1. ASSIGNED IS NOT DONE. Three monster sprites are named *REPLACE and are
///      wired into live definitions; seventeen of the nineteen trap prefabs
///      share ONE donor spike sprite under an affinity tint. A null-check scores
///      all twenty as complete. So an assigned slot is only FILLED when the art
///      is neither placeholder-named nor shared with another prefab.
///
///   2. NULL IS NOT ALWAYS DEBT. CaveWallSheetLayout.overrideSprite,
///      StairsDefinition.upVariantSprite and DivineAudienceScript.backdrop are
///      all documented as optional -- null is the INTENDED value and assigning
///      art would change behaviour, not complete it. Reflection cannot tell
///      those from the eleven empty spell icons, so a hand-authored ruling
///      table below says which is which. A field absent from that table reports
///      as UNCLASSIFIED rather than being quietly counted or quietly ignored,
///      because "not in the list" and "nothing owed" must never look alike.
///
/// Definition icons and in-game prefab sprites are reported as SEPARATE
/// categories on purpose: MonsterDefinition.icon is the spawner-picker icon,
/// while the body the player actually sees lives on the prefab's SpriteRenderer.
/// They are different art, usually authored in different passes, and merging
/// them into one number would hide whichever is smaller.
///
/// Writes Docs/ART_DEBT.md as a standing work queue. Output is fully sorted so
/// a re-run with no content change produces a byte-identical file -- a report
/// that churns its own diff is a report nobody commits.
/// </summary>
public static class ArtDebtAuditor
{
    /// <summary>
    /// How a single art slot is ruled. Required/Deferred/ByDesign/Unclassified
    /// are AUTHORED in the table below and describe the FIELD. Filled is never
    /// authored -- it is resolved at scan time and describes one ASSET's slot.
    /// It sits last so the report can close with an explicit account of what is
    /// already done: a type that simply stops appearing is ambiguous between
    /// "clean" and "the scan missed it", and this project treats that class of
    /// ambiguity as a defect.
    /// </summary>
    public enum ArtRuling { Required, Deferred, ByDesign, Unclassified, Filled }

    private struct Ruling
    {
        public ArtRuling kind;
        public string reason;
        public Ruling(ArtRuling k, string r) { kind = k; reason = r; }
    }

    /// <summary>
    /// The ruling table. Keyed "TypeName.fieldPath", where fieldPath has its
    /// list indices stripped (families.Array.data[3].overrideSprite becomes
    /// families.overrideSprite) so one row covers every element of a list.
    ///
    /// Lookup walks up the base types before giving up, which is why
    /// TrophyDefinition -- which inherits icon from FurnitureDefinition -- still
    /// gets its own row: the two carry different art briefs even though they
    /// share a field.
    /// </summary>
    private static readonly Dictionary<string, Ruling> RULINGS = new Dictionary<string, Ruling>
    {
        // -- Owed. The consumer degrades gracefully, which is exactly why these
        //    go unnoticed; graceful is not the same as finished.
        { "MonsterDefinition.icon",        new Ruling(ArtRuling.Required, "Spawner selection UI icon.") },
        { "TrapDefinition.icon",           new Ruling(ArtRuling.Required, "Trap selection UI icon.") },
        { "FurnitureDefinition.icon",      new Ruling(ArtRuling.Required, "Build submenu button and selection panel.") },
        { "TrophyDefinition.icon",         new Ruling(ArtRuling.Required, "Trophy Hall display icon (canon 22/23).") },
        { "ChestDefinition.icon",          new Ruling(ArtRuling.Required, "Chest tier icon.") },
        { "StairsDefinition.icon",         new Ruling(ArtRuling.Required, "Build submenu icon.") },
        { "NPCDialogue.npcPortrait",       new Ruling(ArtRuling.Required, "Dialogue portrait.") },
        { "SpellDefinition.icon",          new Ruling(ArtRuling.Required, "CAST row icon. Null-safe (plain block) -- still owed.") },
        { "TechNodeDefinition.icon",       new Ruling(ArtRuling.Required, "Research tree node. Null-safe (plain block) -- still owed.") },
        { "PatternDefinition.icon",        new Ruling(ArtRuling.Required, "Pattern Codex entry. Null-safe -- still owed.") },

        // -- Deferred by an explicit shipped decision, with a written road back.
        { "MonsterDefinition.projectileSprite", new Ruling(ArtRuling.Deferred,
            "Ranged v1 ships a runtime soft-glow bolt tinted per definition. " +
            "When bespoke art lands, projectileTint MUST go back to white -- the tint multiplies the sprite.") },

        // -- Null is the intended value. Assigning art here CHANGES BEHAVIOUR
        //    rather than completing anything, so these must never be counted as
        //    debt or a future pass will "fix" them into bugs.
        { "CaveWallSheetLayout.overrideSprite", new Ruling(ArtRuling.ByDesign,
            "Optional override; when set the sheet cell coordinate is ignored.") },
        { "StairsDefinition.upVariantSprite",   new Ruling(ArtRuling.ByDesign,
            "Optional; null means the prefab's own sprite is used.") },
        { "DivineAudienceScript.backdrop",      new Ruling(ArtRuling.ByDesign,
            "Optional; null falls back to a slow radial pulse in the tint.") },
    };

    /// <summary>
    /// Prefab roots scanned for in-game sprites, each reported as its own group
    /// so a shared-sprite count means "shared with a sibling of the same kind".
    /// Counting sharing across the whole project would be noise -- an NPC and a
    /// trap using one sprite is not the donor pattern this is looking for.
    /// </summary>
    private static readonly string[] PREFAB_GROUPS =
    {
        "Assets/Prefabs/Monsters",
        "Assets/Prefabs/Traps",
        "Assets/Prefabs/NPCs",
        "Assets/Prefabs/Items",
        "Assets/Prefabs/Loot",
    };

    /// <summary>
    /// Placeholder markers matched case-insensitively against a sprite's asset
    /// name. TEMP is deliberately only matched DELIMITED (_TEMP / TEMP_): bare
    /// "temp" is a substring of Temple, Templar and Temptation, and this project
    /// has holy ground, so an undelimited match would report shipped art as
    /// debt and teach the reader to ignore the report.
    /// </summary>
    private static readonly string[] PLACEHOLDER_MARKERS =
    { "REPLACE", "PLACEHOLDER", "STANDIN", "STAND_IN", "TODO", "_TEMP", "TEMP_" };

    private class Slot
    {
        public string category;     // "Definition icons" or "In-game sprites"
        public string typeName;
        public string fieldPath;
        public string assetPath;
        public string assetName;
        public ArtRuling outcome;
        public string reason;
        public string note;         // placeholder / shared-donor detail, or empty
    }

    [MenuItem("Dungeon Core/Audit Art Debt")]
    public static void Audit()
    {
        var slots = new List<Slot>();
        ScanDefinitions(slots);
        ScanPrefabs(slots);

        var extra = new List<string>();
        CheckProjectileTints(extra);

        string report = BuildReport(slots, extra);
        Debug.Log(report);
        WriteWorkQueue(slots, extra);
    }

    // ---------------------------------------------------------------- scanning

    /// <summary>
    /// Every ScriptableObject the project itself declares. Deliberately NOT a
    /// hand-kept type list: a new definition type with a Sprite field must show
    /// up here the day it is written, and the UNCLASSIFIED bucket is what makes
    /// that arrival loud instead of silent. Types from Unity and packages are
    /// skipped by assembly, which keeps TMP settings and input assets out.
    /// </summary>
    private static void ScanDefinitions(List<Slot> slots)
    {
        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.StartsWith("Assets/")) continue;

            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so == null) continue;

            System.Type t = so.GetType();
            if (t.Assembly.GetName().Name != "Assembly-CSharp") continue;

            var sobj = new SerializedObject(so);
            SerializedProperty p = sobj.GetIterator();
            bool enter = true;
            while (p.Next(enter))
            {
                enter = true;
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!IsSpriteReference(p.type)) continue;

                string field = StripIndices(p.propertyPath);
                Ruling r = Resolve(t, field);

                var sprite = p.objectReferenceValue as Sprite;
                string note = "";
                bool filled = false;
                if (sprite != null)
                {
                    if (IsPlaceholderName(sprite.name))
                        note = "placeholder art assigned: " + sprite.name;
                    else
                    { filled = true; note = sprite.name; }
                }

                slots.Add(new Slot
                {
                    category = "Definition icons",
                    typeName = t.Name,
                    fieldPath = field,
                    assetPath = path,
                    assetName = so.name,
                    outcome = filled ? ArtRuling.Filled : r.kind,
                    reason = r.reason,
                    note = note,
                });
            }
        }
    }

    /// <summary>
    /// Prefab bodies. Loaded through AssetDatabase rather than read as YAML
    /// because 49 of the 50 monster prefabs are VARIANTS: a variant that never
    /// overrides m_Sprite has no sprite line of its own and inherits the base
    /// prefab's body. On disk that looks like nothing; loaded, it resolves to
    /// the donor sprite and the borrowing becomes countable. Reading the YAML
    /// would have reported two shared sprites where there are two dozen.
    /// </summary>
    private static void ScanPrefabs(List<Slot> slots)
    {
        foreach (string root in PREFAB_GROUPS)
        {
            if (!AssetDatabase.IsValidFolder(root)) continue;

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });

            // First pass: how many prefabs in this group resolve to each sprite.
            var users = new Dictionary<string, List<string>>();
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr.sprite == null) continue;
                    string key = AssetDatabase.GetAssetPath(sr.sprite) + "::" + sr.sprite.name;
                    if (!users.ContainsKey(key)) users[key] = new List<string>();
                    if (!users[key].Contains(path)) users[key].Add(path);
                }
            }

            // Second pass: rule each renderer against that count.
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                if (renderers.Length == 0) continue;

                for (int i = 0; i < renderers.Length; i++)
                {
                    var sr = renderers[i];
                    string field = renderers.Length == 1
                        ? "SpriteRenderer.sprite"
                        : "SpriteRenderer.sprite (" + sr.gameObject.name + ")";

                    ArtRuling outcome;
                    string reason;
                    string note = "";

                    if (sr.sprite == null)
                    {
                        outcome = ArtRuling.Required;
                        reason = "Prefab renders nothing until a sprite is assigned.";
                    }
                    else if (IsPlaceholderName(sr.sprite.name))
                    {
                        outcome = ArtRuling.Required;
                        reason = "Placeholder art is wired in; a null-check scores this as done.";
                        note = "placeholder: " + sr.sprite.name;
                    }
                    else
                    {
                        string key = AssetDatabase.GetAssetPath(sr.sprite) + "::" + sr.sprite.name;
                        int shared = users.ContainsKey(key) ? users[key].Count : 1;
                        if (shared > 1)
                        {
                            outcome = ArtRuling.Deferred;
                            reason = "Borrowed donor body -- canon's roster norm pending bespoke art.";
                            note = sr.sprite.name + " (shared by " + shared + " prefabs in " + root + ")";
                        }
                        else
                        {
                            outcome = ArtRuling.Filled;
                            reason = "";
                            note = sr.sprite.name;
                        }
                    }

                    slots.Add(new Slot
                    {
                        category = "In-game sprites",
                        typeName = root.Substring(root.LastIndexOf('/') + 1) + " prefab",
                        fieldPath = field,
                        assetPath = path,
                        assetName = go.name,
                        outcome = outcome,
                        reason = reason,
                        note = note,
                    });
                }
            }
        }
    }

    /// <summary>
    /// The tint trap. projectileTint MULTIPLIES the projectile sprite, so a
    /// definition that keeps a non-white tint after bespoke art arrives renders
    /// the new art through the old colour and looks like a bad sprite rather
    /// than a stale field. Only fires once a sprite is actually assigned, so it
    /// stays quiet through the whole deferred period and speaks exactly when it
    /// becomes true.
    /// </summary>
    private static void CheckProjectileTints(List<string> extra)
    {
        foreach (string g in AssetDatabase.FindAssets("t:MonsterDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
            if (def == null || def.projectileSprite == null) continue;
            if (def.projectileTint != Color.white)
                extra.Add("- " + def.name + ": projectileSprite assigned but projectileTint is "
                          + def.projectileTint + ", not white. The tint multiplies the sprite. (" + path + ")");
        }
        extra.Sort(System.StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// SerializedProperty.type for an object reference reads "PPtr&lt;$Sprite&gt;".
    /// Matched exactly on the inner name rather than by Contains, so
    /// SpriteAtlas and SpriteLibraryAsset fields are not swept in as icons.
    /// </summary>
    private static bool IsSpriteReference(string propType)
    {
        if (string.IsNullOrEmpty(propType)) return false;
        if (!propType.StartsWith("PPtr<") || !propType.EndsWith(">")) return false;
        string inner = propType.Substring(5, propType.Length - 6);
        if (inner.StartsWith("$")) inner = inner.Substring(1);
        return inner == "Sprite";
    }

    /// <summary>families.Array.data[3].overrideSprite -> families.overrideSprite</summary>
    private static string StripIndices(string propertyPath)
    {
        var sb = new StringBuilder();
        string[] parts = propertyPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Array") { i++; continue; }   // also skips data[N]
            if (sb.Length > 0) sb.Append('.');
            sb.Append(parts[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exact path first, then the LEAF field name -- both walking up the base
    /// types. The leaf fallback exists for sprites on nested serialisable
    /// classes: CaveWallSheetLayout reaches SheetSlot.overrideSprite down more
    /// than a dozen distinct paths (capSlots, innerSE, families.faceUpperSlots,
    /// families.pavingSlots and the rest, 260 slots in the shipped asset), and
    /// authoring a row per path would be a list nobody could keep true. One
    /// leaf row covers them all, while an exact-path row still wins where a
    /// nested sprite needs its own ruling.
    /// </summary>
    private static Ruling Resolve(System.Type t, string field)
    {
        Ruling r;
        for (System.Type cur = t; cur != null; cur = cur.BaseType)
            if (RULINGS.TryGetValue(cur.Name + "." + field, out r)) return r;

        int dot = field.LastIndexOf('.');
        if (dot >= 0)
        {
            string leaf = field.Substring(dot + 1);
            for (System.Type cur = t; cur != null; cur = cur.BaseType)
                if (RULINGS.TryGetValue(cur.Name + "." + leaf, out r)) return r;
        }

        return new Ruling(ArtRuling.Unclassified,
            "Not in the ruling table. Add a row saying whether this slot is owed or intentionally empty.");
    }

    private static bool IsPlaceholderName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string upper = name.ToUpperInvariant();
        foreach (string m in PLACEHOLDER_MARKERS)
            if (upper.Contains(m)) return true;
        return false;
    }

    private static int Order(ArtRuling r)
    {
        switch (r)
        {
            case ArtRuling.Unclassified: return 0;
            case ArtRuling.Required: return 1;
            case ArtRuling.Deferred: return 2;
            case ArtRuling.ByDesign: return 3;
            default: return 4;   // Filled last, always
        }
    }

    private static void SortSlots(List<Slot> slots)
    {
        slots.Sort(delegate (Slot a, Slot b)
        {
            int c = Order(a.outcome).CompareTo(Order(b.outcome));
            if (c != 0) return c;
            c = string.CompareOrdinal(a.category, b.category); if (c != 0) return c;
            c = string.CompareOrdinal(a.typeName, b.typeName); if (c != 0) return c;
            c = string.CompareOrdinal(a.fieldPath, b.fieldPath); if (c != 0) return c;
            return string.CompareOrdinal(a.assetPath, b.assetPath);
        });
    }

    // ---------------------------------------------------------------- output

    private static string BuildReport(List<Slot> slots, List<string> extra)
    {
        SortSlots(slots);
        var sb = new StringBuilder();
        int owed = 0, unclassified = 0;
        foreach (var s in slots)
        {
            if (s.outcome == ArtRuling.Required) owed++;
            if (s.outcome == ArtRuling.Unclassified) unclassified++;
        }

        sb.AppendLine("[ArtDebtAuditor] " + owed + " slot(s) owed, " + unclassified + " unclassified.");
        AppendCounts(sb, slots);
        if (extra.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Tint coupling:");
            foreach (string e in extra) sb.AppendLine(e);
        }
        sb.AppendLine();
        sb.AppendLine("Full breakdown written to Docs/ART_DEBT.md.");
        return sb.ToString();
    }

    /// <summary>
    /// One line per category/type/field with a count per outcome. Every group is
    /// printed even when its owed count is zero -- a group that vanishes when it
    /// is clean cannot be told from a group the scan dropped.
    /// </summary>
    private static void AppendCounts(StringBuilder sb, List<Slot> slots)
    {
        var keys = new List<string>();
        var tally = new Dictionary<string, int[]>();
        foreach (var s in slots)
        {
            string k = s.category + " | " + s.typeName + "." + s.fieldPath;
            if (!tally.ContainsKey(k)) { tally[k] = new int[5]; keys.Add(k); }
            tally[k][Order(s.outcome)]++;
        }
        keys.Sort(System.StringComparer.Ordinal);
        foreach (string k in keys)
        {
            int[] c = tally[k];
            sb.AppendLine("- " + k + ": "
                + c[1] + " owed, " + c[2] + " deferred, " + c[3] + " by design, "
                + c[0] + " unclassified, " + c[4] + " filled.");
        }
    }

    /// <summary>Counts per group for a state that is not enumerated.</summary>
    private static void AppendSummaryOnly(StringBuilder sb, List<Slot> slots, ArtRuling state, string gloss)
    {
        var keys = new List<string>();
        var tally = new Dictionary<string, int>();
        foreach (var s in slots)
        {
            if (s.outcome != state) continue;
            string k = s.category + " | " + s.typeName + "." + s.fieldPath;
            if (!tally.ContainsKey(k)) { tally[k] = 0; keys.Add(k); }
            tally[k]++;
        }
        sb.Append("\n## " + state.ToString().ToUpperInvariant() + " (counts only)\n\n");
        sb.Append(gloss + "\n\n");
        if (keys.Count == 0) { sb.Append("- none\n"); return; }
        keys.Sort(System.StringComparer.Ordinal);
        foreach (string k in keys) sb.Append("- " + k + ": " + tally[k] + "\n");
    }

    /// <summary>
    /// The standing work queue. Written to Docs/ (repo root, outside Assets/)
    /// so it never becomes an imported asset with a .meta of its own. Sorted
    /// throughout, so re-running with nothing changed rewrites the same bytes
    /// and the file is safe to commit and diff.
    /// </summary>
    private static void WriteWorkQueue(List<Slot> slots, List<string> extra)
    {
        SortSlots(slots);
        var sb = new StringBuilder();
        sb.Append("# Art Debt\n\n");
        sb.Append("Generated by `Dungeon Core / Audit Art Debt`. Do not hand-edit -- rerun the menu.\n");
        sb.Append("Ruled by the table in `Assets/Editor/ArtDebtAuditor.cs`; an UNCLASSIFIED row means\n");
        sb.Append("that table needs a new entry, not that the slot is owed.\n\n");
        sb.Append("Prompts for these follow the Style Contract in `DCR_Guide_Content_Authoring.html`\n");
        sb.Append("chapter 0 -- fixed head, subject slot, fixed tail.\n\n");

        AppendCounts(sb, slots);

        if (extra.Count > 0)
        {
            sb.Append("\n## Tint coupling\n\n");
            foreach (string e in extra) sb.Append(e + "\n");
        }

        // UNCLASSIFIED, REQUIRED and DEFERRED are enumerated per asset: those
        // are the states someone acts on. BY DESIGN and FILLED are summarised
        // to counts instead, because the shipped wall sheet alone carries 260
        // overrideSprite slots and listing 113 intentionally-empty ones would
        // bury the eleven spell icons this file exists to surface. The counts
        // still print, so neither state can be confused with "scan missed it".
        ArtRuling current = (ArtRuling)(-1);
        foreach (var s in slots)
        {
            if (s.outcome == ArtRuling.ByDesign || s.outcome == ArtRuling.Filled) continue;
            if (s.outcome != current)
            {
                current = s.outcome;
                sb.Append("\n## " + current.ToString().ToUpperInvariant() + "\n\n");
            }
            sb.Append("- `" + s.assetName + "` " + s.typeName + "." + s.fieldPath);
            if (!string.IsNullOrEmpty(s.note)) sb.Append(" -- " + s.note);
            sb.Append("\n  `" + s.assetPath + "`\n");
            if (!string.IsNullOrEmpty(s.reason)) sb.Append("  " + s.reason + "\n");
        }

        AppendSummaryOnly(sb, slots, ArtRuling.ByDesign,
            "Null is the intended value here. Do not \"complete\" these.");
        AppendSummaryOnly(sb, slots, ArtRuling.Filled,
            "Real art assigned, not placeholder-named, not shared with a sibling prefab.");

        string dir = System.IO.Path.Combine(Application.dataPath, "..");
        dir = System.IO.Path.Combine(dir, "Docs");
        if (!System.IO.Directory.Exists(dir))
        {
            Debug.LogWarning("[ArtDebtAuditor] No Docs folder at " + dir + "; work queue not written.");
            return;
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ART_DEBT.md"), sb.ToString());
    }
}
