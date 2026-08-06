using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector support for CaveWallSheetLayout. Each slot renders as one row:
/// (col, row) fields, an optional sprite override, and a live thumbnail of the
/// referenced cell cut from the assigned sheet (or of the override sprite).
/// Out-of-bounds coordinates show a red thumbnail. Cap and face slots get
/// role labels, and a Validate Layout button prints a full report.
/// Lives in an Editor folder; zero runtime footprint.
/// </summary>
[CustomPropertyDrawer(typeof(CaveWallSheetLayout.SheetSlot))]
public class CaveWallSheetSlotDrawer : PropertyDrawer
{
    private const float Thumb = 40f;
    private const float Pad = 4f;

    // Which sheet a slot's thumbnail samples. The property path is the only
    // identity a drawer gets; family slots live under
    // "families.Array.data[N]....", so the family index is parsed out of the
    // path and that family's sheet wins. The previous mechanism -- a "ruins"
    // NAME prefix on every family field -- could not survive the move into a
    // list, where every entry shares the same field names; parsing structure
    // instead of names is why this one can.
    private static readonly Regex FamilyPath = new Regex(@"^families\.Array\.data\[(\d+)\]");

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => Thumb + Pad;

    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        var layout = property.serializedObject.targetObject as CaveWallSheetLayout;
        SerializedProperty cellProp = property.FindPropertyRelative("cell");
        SerializedProperty overrideProp = property.FindPropertyRelative("overrideSprite");

        rect.y += Pad * 0.5f;
        rect.height = Thumb;
        float lineY = rect.y + (Thumb - EditorGUIUtility.singleLineHeight) * 0.5f;

        // Label column.
        var labelRect = new Rect(rect.x, lineY, EditorGUIUtility.labelWidth - 4f, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(labelRect, label);
        float x = rect.x + EditorGUIUtility.labelWidth;

        // (col, row) cell field.
        float avail = rect.xMax - x - Thumb - 8f;
        float cellW = Mathf.Clamp(avail * 0.45f, 90f, 150f);
        var cellRect = new Rect(x, lineY, cellW, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(cellRect, cellProp, GUIContent.none);
        x += cellW + 4f;

        // Override sprite field takes the rest.
        var sprRect = new Rect(x, lineY, rect.xMax - x - Thumb - 8f, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(sprRect, overrideProp, GUIContent.none);

        var thumbRect = new Rect(rect.xMax - Thumb, rect.y, Thumb, Thumb);
        Texture2D sheetTex = ResolveSheet(property, layout);
        DrawThumb(thumbRect, layout, cellProp.vector2IntValue, overrideProp.objectReferenceValue as Sprite, sheetTex);
    }

    private static Texture2D ResolveSheet(SerializedProperty property, CaveWallSheetLayout layout)
    {
        if (layout == null) return null;
        var m = FamilyPath.Match(property.propertyPath);
        if (m.Success)
        {
            int i = int.Parse(m.Groups[1].Value);
            if (layout.families != null && i >= 0 && i < layout.families.Count && layout.families[i] != null)
                return layout.families[i].sheet;
            return null;
        }
        return layout.sheet;
    }

    private static void DrawThumb(Rect r, CaveWallSheetLayout layout, Vector2Int cell, Sprite over, Texture2D tex)
    {
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.30f));

        if (over != null && over.texture != null)
        {
            Rect tr = over.textureRect;
            var uv = new Rect(tr.x / over.texture.width, tr.y / over.texture.height,
                              tr.width / over.texture.width, tr.height / over.texture.height);
            GUI.DrawTextureWithTexCoords(r, over.texture, uv, true);
            return;
        }

        if (layout == null || tex == null || cell.x < 0 || cell.y < 0) return;

        int cs = Mathf.Max(1, layout.cellSize);
        float w = tex.width;
        float h = tex.height;
        var uvCell = new Rect(cell.x * cs / w, (h - (cell.y + 1) * cs) / h, cs / w, cs / h);

        // Out of bounds: red block instead of a garbage sample.
        if (uvCell.x < 0f || uvCell.y < 0f || uvCell.xMax > 1.0001f || uvCell.yMax > 1.0001f)
        {
            EditorGUI.DrawRect(r, new Color(0.65f, 0.12f, 0.12f, 0.75f));
            return;
        }
        GUI.DrawTextureWithTexCoords(r, tex, uvCell, true);
    }
}

[CustomEditor(typeof(CaveWallSheetLayout))]
public class CaveWallSheetLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var layout = (CaveWallSheetLayout)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("sheet"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cellSize"));
        if (layout.sheet != null)
            EditorGUILayout.HelpBox(
                $"Sheet grid: {layout.SheetCols} x {layout.SheetRows} cells at {layout.cellSize}px. " +
                "Coordinates are (column, row from top). Overrides win over coordinates.",
                MessageType.None);

        DrawLabeledArray(serializedObject.FindProperty("capSlots"), CaveWallSheetLayout.CapMaskLabels,
            "Caps - one per mask (N=1, E=2, S=4, W=8; bit set = neighbour solid)", skipZero: false);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Concave corners (replace mask 15 when one diagonal is open)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSE"), new GUIContent("Inner SE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSW"), new GUIContent("Inner SW"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNE"), new GUIContent("Inner NE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNW"), new GUIContent("Inner NW"));

        DrawLabeledArray(serializedObject.FindProperty("faceUpperSlots"), CaveWallSheetLayout.FaceLabels,
            "Face upper slices (by face variant)", skipZero: true);
        DrawLabeledArray(serializedObject.FindProperty("faceLowerSlots"), CaveWallSheetLayout.FaceLabels,
            "Face lower slices (by face variant)", skipZero: true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Straight-wall variety (mask 11)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("greenMossVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("goldMossVariants"), true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Cap variety pools", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("capVariety"), true);

        // -- Wall families (per-terrain masonry, canon 19) -------------------
        // Every family field is drawn explicitly: this editor replaces the
        // default Inspector wholesale, so any field it skips is INVISIBLE --
        // the trap that hid the first ruins fields until they were wired in.
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Wall families (per-terrain masonry)", EditorStyles.boldLabel);
        SerializedProperty famsProp = serializedObject.FindProperty("families");
        int removeAt = -1;
        for (int f = 0; f < famsProp.arraySize; f++)
        {
            SerializedProperty fam = famsProp.GetArrayElementAtIndex(f);
            SerializedProperty terrainProp = fam.FindPropertyRelative("terrain");
            string famName = ((TerrainType)terrainProp.intValue).ToString();

            EditorGUILayout.BeginHorizontal();
            fam.isExpanded = EditorGUILayout.Foldout(fam.isExpanded, $"Family [{f}] -- {famName}", true);
            if (GUILayout.Button("Remove", GUILayout.Width(64f))) removeAt = f;
            EditorGUILayout.EndHorizontal();
            if (!fam.isExpanded) continue;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(terrainProp);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("sheet"));
            var famObj = (layout.families != null && f < layout.families.Count) ? layout.families[f] : null;
            if (famObj != null && famObj.sheet != null)
                EditorGUILayout.HelpBox(
                    $"Family sheet grid: {famObj.SheetCols(layout.cellSize)} x {famObj.SheetRows(layout.cellSize)} cells " +
                    $"at {layout.cellSize}px. Every family slot is optional: empty caps fall back to the family " +
                    "mask-11 cap (the family base), empty faces to the family Straight face. Only a family " +
                    "with no base cap at all falls back to stone.",
                    MessageType.None);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("tint"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("allowMoss"));

            // Drawn explicitly like everything else here: this Inspector lists
            // fields by name, which is the trap that hid the first ruins fields
            // until they were wired in.
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("restrictToFloors"));
            if (famObj != null && famObj.restrictToFloors)
                EditorGUILayout.PropertyField(fam.FindPropertyRelative("floors"), true);

            DrawLabeledArray(fam.FindPropertyRelative("capSlots"), CaveWallSheetLayout.CapMaskLabels,
                "Family caps - one per mask (mask 11 doubles as the family base cap)", skipZero: false);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Family concave corners", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerSE"), new GUIContent("Inner SE"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerSW"), new GUIContent("Inner SW"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerNE"), new GUIContent("Inner NE"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerNW"), new GUIContent("Inner NW"));

            DrawLabeledArray(fam.FindPropertyRelative("faceUpperSlots"), CaveWallSheetLayout.FaceLabels,
                "Family face upper slices (by face variant)", skipZero: true);
            DrawLabeledArray(fam.FindPropertyRelative("faceLowerSlots"), CaveWallSheetLayout.FaceLabels,
                "Family face lower slices (by face variant)", skipZero: true);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Family straight-wall variety and site paving", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("variants"), true);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("plainWeight"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("pavingSlots"), true);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
        }
        if (removeAt >= 0)
            famsProp.DeleteArrayElementAtIndex(removeAt);
        if (GUILayout.Button("Add Family", GUILayout.Height(22f)))
        {
            // Unity clones the LAST element into the new slot, which is the
            // intended workflow: add, retarget the terrain, repoint the art.
            famsProp.arraySize++;
        }

        EditorGUILayout.Space(10f);
        if (GUILayout.Button("Validate Layout", GUILayout.Height(26f)))
            layout.ValidateLayout();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawLabeledArray(SerializedProperty prop, string[] labels, string header, bool skipZero)
    {
        if (prop == null) return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        for (int i = 0; i < prop.arraySize; i++)
        {
            if (skipZero && i == 0) continue;
            string label = (labels != null && i < labels.Length) ? labels[i] : $"Slot {i}";
            EditorGUILayout.PropertyField(prop.GetArrayElementAtIndex(i), new GUIContent(label));
        }
    }
}
