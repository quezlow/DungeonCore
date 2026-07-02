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

        // Thumbnail.
        var thumbRect = new Rect(rect.xMax - Thumb, rect.y, Thumb, Thumb);
        DrawThumb(thumbRect, layout, cellProp.vector2IntValue, overrideProp.objectReferenceValue as Sprite);
    }

    private static void DrawThumb(Rect r, CaveWallSheetLayout layout, Vector2Int cell, Sprite over)
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

        if (layout == null || layout.sheet == null || cell.x < 0 || cell.y < 0) return;

        int cs = Mathf.Max(1, layout.cellSize);
        float w = layout.sheet.width;
        float h = layout.sheet.height;
        var uvCell = new Rect(cell.x * cs / w, (h - (cell.y + 1) * cs) / h, cs / w, cs / h);

        // Out of bounds: red block instead of a garbage sample.
        if (uvCell.x < 0f || uvCell.y < 0f || uvCell.xMax > 1.0001f || uvCell.yMax > 1.0001f)
        {
            EditorGUI.DrawRect(r, new Color(0.65f, 0.12f, 0.12f, 0.75f));
            return;
        }
        GUI.DrawTextureWithTexCoords(r, layout.sheet, uvCell, true);
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

        DrawLabeledArray("capSlots", CaveWallSheetLayout.CapMaskLabels,
            "Caps - one per mask (N=1, E=2, S=4, W=8; bit set = neighbour solid)", skipZero: false);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Concave corners (replace mask 15 when one diagonal is open)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSE"), new GUIContent("Inner SE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSW"), new GUIContent("Inner SW"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNE"), new GUIContent("Inner NE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNW"), new GUIContent("Inner NW"));

        DrawLabeledArray("faceUpperSlots", CaveWallSheetLayout.FaceLabels,
            "Face upper slices (by face variant)", skipZero: true);
        DrawLabeledArray("faceLowerSlots", CaveWallSheetLayout.FaceLabels,
            "Face lower slices (by face variant)", skipZero: true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Straight-wall variety (mask 11)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("greenMossVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("goldMossVariants"), true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Cap variety pools", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("capVariety"), true);

        EditorGUILayout.Space(10f);
        if (GUILayout.Button("Validate Layout", GUILayout.Height(26f)))
            layout.ValidateLayout();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawLabeledArray(string propName, string[] labels, string header, bool skipZero)
    {
        SerializedProperty prop = serializedObject.FindProperty(propName);
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
