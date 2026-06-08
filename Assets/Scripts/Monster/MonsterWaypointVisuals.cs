using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// DAY 31 PART 3D — Per-spawner runtime visuals for patrol waypoints and attack target.
///
/// Visible only when this spawner is selected via SpawnerSelectionController.
/// Renders:
///   - A numbered marker (TextMesh inside a runtime GameObject) at each patrol waypoint cell.
///   - A LineRenderer threading through them, closing the loop if PatrolLoop = true.
///   - A distinct "X" marker at the attack-target cell when one is active.
///
/// Place this component as a child of each MonsterSpawner prefab. Assign the
/// waypoint marker and attack target marker prefabs in the Inspector (simple
/// world-space prefabs with a sprite or text).
/// </summary>
public class MonsterWaypointVisuals : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MonsterSpawner spawner;
    [SerializeField] private LineRenderer lineRenderer;

    [Header("Marker Prefabs")]
    [Tooltip("Runtime-instantiated marker per patrol waypoint. Should contain a TextMesh / TMP_Text " +
             "child named 'Number' for the index label.")]
    [SerializeField] private GameObject waypointMarkerPrefab;
    [Tooltip("Distinct marker for the attack-target cell. No number required.")]
    [SerializeField] private GameObject attackTargetMarkerPrefab;

    [Header("Layout")]
    [SerializeField] private Vector3 markerWorldOffset = new Vector3(0f, 0.1f, 0f);

    private readonly List<GameObject> waypointMarkers = new();
    private GameObject attackTargetMarker;
    private bool isVisible;

    private void Awake()
    {
        if (spawner == null) spawner = GetComponentInParent<MonsterSpawner>();
        if (lineRenderer != null) lineRenderer.positionCount = 0;
    }

    private void Start()
    {
        if (SpawnerSelectionController.Instance != null)
            SpawnerSelectionController.Instance.OnSelectionChanged += HandleSelectionChanged;
        if (spawner != null) spawner.OnOrdersChanged += Refresh;
        HideAll();
    }

    private void OnDestroy()
    {
        if (SpawnerSelectionController.Instance != null)
            SpawnerSelectionController.Instance.OnSelectionChanged -= HandleSelectionChanged;
        if (spawner != null) spawner.OnOrdersChanged -= Refresh;
        ClearMarkers();
    }

    private void HandleSelectionChanged(MonsterSpawner selected)
    {
        isVisible = (selected == spawner);
        if (isVisible) Refresh();
        else HideAll();
    }

    private void Refresh()
    {
        if (!isVisible) { HideAll(); return; }
        if (spawner == null) { HideAll(); return; }

        var influence = spawner.GetComponentInParent<FloorRoot>()?.TileInfluence;
        if (influence == null) { HideAll(); return; }

        ClearMarkers();

        var waypoints = spawner.PatrolWaypoints;
        for (int i = 0; i < waypoints.Count; i++)
        {
            Vector3 worldPos = influence.CellToWorld(waypoints[i]) + markerWorldOffset;
            var marker = CreateWaypointMarker(i + 1, worldPos);
            if (marker != null) waypointMarkers.Add(marker);
        }

        // Line
        if (lineRenderer != null)
        {
            int count = waypoints.Count;
            bool loop = spawner.PatrolLoop && count > 1;
            int positions = count + (loop ? 1 : 0);
            lineRenderer.positionCount = positions;
            for (int i = 0; i < count; i++)
                lineRenderer.SetPosition(i, influence.CellToWorld(waypoints[i]) + markerWorldOffset);
            if (loop)
                lineRenderer.SetPosition(count, influence.CellToWorld(waypoints[0]) + markerWorldOffset);
        }

        // Attack target marker
        if (spawner.HasAttackTarget && attackTargetMarkerPrefab != null)
        {
            Vector3 worldPos = influence.CellToWorld(spawner.AttackTargetCell) + markerWorldOffset;
            attackTargetMarker = Instantiate(attackTargetMarkerPrefab, worldPos, Quaternion.identity, transform);
        }
    }

    private void HideAll()
    {
        ClearMarkers();
        if (lineRenderer != null) lineRenderer.positionCount = 0;
    }

    private void ClearMarkers()
    {
        for (int i = 0; i < waypointMarkers.Count; i++)
            if (waypointMarkers[i] != null) Destroy(waypointMarkers[i]);
        waypointMarkers.Clear();
        if (attackTargetMarker != null) { Destroy(attackTargetMarker); attackTargetMarker = null; }

        // Defensive — sweep any orphan children that escaped the list-tracked Destroy
        // (handles edge cases where Refresh fires twice in a frame, leaving the previous
        // frame's destroyed-but-end-of-frame-pending markers stranded in the hierarchy).
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (lineRenderer != null && child == lineRenderer.transform) continue;
            Destroy(child.gameObject);
        }
    }

    private GameObject CreateWaypointMarker(int index, Vector3 worldPos)
    {
        // DAY 31 — Programmatic marker creation. Bypasses prefab variability
        // (Screen Space Canvas, font asset issues, world space scale gotchas).
        // Uses TextMeshPro 3D component which renders directly as a world-space
        // mesh — no canvas required.
        var marker = new GameObject($"WaypointMarker_{index}");
        marker.transform.SetParent(transform, false);
        marker.transform.position = worldPos;

        var tmp = marker.AddComponent<TMPro.TextMeshPro>();
        tmp.text = index.ToString();
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.fontSize = 4f;
        tmp.color = new Color(0.83f, 0.65f, 0.15f, 1f);  // gold accent
        tmp.fontStyle = TMPro.FontStyles.Bold;

        var mr = marker.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 100;

        return marker;
    }
}