using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a thin line from each SELECTED monster to its current combat target.
/// One pooled LineRenderer per visible link, refreshed in LateUpdate so the
/// endpoints track live positions. Selected-only keeps it readable and pairs
/// with multi-select / Attack-Here (lines converge on a shared target).
///
/// SETUP: drop this on one always-active object in the dungeon scene. It builds
/// its own LineRenderers — no prefab or wiring needed.
///
/// To show lines for ALL monsters instead of just selected, replace the
/// selection loop with a floor-entity scan (FloorManager.ActiveFloor.Entities).
/// </summary>
public class TargetingLineController : MonoBehaviour
{
    [SerializeField] private Color lineColor = new Color(0.92f, 0.30f, 0.24f, 0.75f);
    [SerializeField] private float lineWidth = 0.05f;
    [SerializeField] private string sortingLayer = "AdjacentHighlight";
    [SerializeField] private int sortingOrder = 50;

    private readonly List<LineRenderer> pool = new();
    private Material lineMaterial;

    private void Awake()
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) lineMaterial = new Material(sh);
    }

    private void LateUpdate()
    {
        var sel = SpawnerSelectionController.Instance;
        int used = 0;

        if (sel != null && sel.Count > 0)
        {
            var list = sel.Selected;
            for (int i = 0; i < list.Count; i++)
            {
                var spawner = list[i];
                var mon = spawner != null ? spawner.SpawnedMonster : null;
                if (mon == null) continue;
                var tgt = mon.CombatTarget;
                if (tgt == null) continue;

                var lr = GetLine(used++);
                lr.enabled = true;
                lr.SetPosition(0, mon.transform.position);
                lr.SetPosition(1, tgt.position);
            }
        }

        for (int i = used; i < pool.Count; i++)
            if (pool[i] != null) pool[i].enabled = false;
    }

    private LineRenderer GetLine(int index)
    {
        while (pool.Count <= index)
        {
            var go = new GameObject("TargetLine_" + pool.Count);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            if (lineMaterial != null) lr.material = lineMaterial;
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.sortingLayerName = sortingLayer;
            lr.sortingOrder = sortingOrder;
            pool.Add(lr);
        }
        return pool[index];
    }
}