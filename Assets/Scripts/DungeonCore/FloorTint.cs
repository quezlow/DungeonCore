using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Stage 5 — per-floor depth tint. Multiplies a colour onto the floor and wall tilemaps.
/// Each floor's tint is either an explicit per-floor override (for dramatic looks like a
/// red level) or, when none is set, a subtle depth gradient that cools and darkens as you
/// descend. Uses Tilemap.color (a layer-wide multiply): it composes with the per-cell fade
/// (CaveWallFade drives per-cell alpha, this sets the layer RGB) and sits beneath the
/// day/night overlay, which darkens everything on top.
///
/// Drop on the FloorRoot GameObject. It resolves the floor tilemap from FloorRoot.Terrain
/// and the wall tilemaps from the floor's CaveWallRenderer, then applies the tint once in
/// Start — after FloorManager has assigned the floor index.
/// </summary>
[DisallowMultipleComponent]
public class FloorTint : MonoBehaviour
{
    [System.Serializable]
    public struct FloorOverride
    {
        [Tooltip("Floor index this tint applies to (0 = first floor).")]
        public int floorIndex;
        public Color tint;
    }

    [Header("Per-floor overrides")]
    [Tooltip("Explicit tint for specific floors (e.g. a red level). Floors not listed " +
             "fall back to the depth gradient below. Alpha is ignored — the tint is colour only.")]
    [SerializeField] private List<FloorOverride> floorOverrides = new();

    [Header("Depth gradient (fallback for un-overridden floors)")]
    [Tooltip("Tint at the shallowest floor (index 0).")]
    [SerializeField] private Color shallowTint = new Color(1.00f, 0.98f, 0.93f, 1f);
    [Tooltip("Tint reached at maxDepth and below — cooler and darker.")]
    [SerializeField] private Color deepTint = new Color(0.56f, 0.64f, 0.80f, 1f);
    [Tooltip("Floor index at which the deepest gradient tint is reached.")]
    [SerializeField, Min(1)] private int maxDepth = 8;

    private void Start()
    {
        var floor = GetComponentInParent<FloorRoot>();
        int index = floor != null ? floor.FloorIndex : 0;
        Color tint = TintForFloor(index);

        if (floor != null && floor.Terrain != null)
        {
            ApplyTint(floor.Terrain.FloorTilemap, tint);
            ApplyTint(floor.Terrain.FogTilemap, tint);
        }

        var wallRenderer = floor != null ? floor.GetComponentInChildren<CaveWallRenderer>(true) : null;
        if (wallRenderer != null)
        {
            ApplyTint(wallRenderer.CapsTilemap, tint);
            ApplyTint(wallRenderer.FacesTilemap, tint);
            ApplyTint(wallRenderer.FacesBehindTilemap, tint);
        }
    }

    private Color TintForFloor(int index)
    {
        for (int i = 0; i < floorOverrides.Count; i++)
            if (floorOverrides[i].floorIndex == index)
                return floorOverrides[i].tint;

        float t = Mathf.Clamp01((float)index / maxDepth);
        return Color.Lerp(shallowTint, deepTint, t);
    }

    // Force alpha 1: the tint is a colour multiply, not a transparency. Per-cell
    // transparency is the fade's job, and it stops a default-black override (alpha 0,
    // as new Inspector list entries arrive) from making a floor vanish.
    private static void ApplyTint(Tilemap map, Color tint)
    {
        if (map != null) map.color = new Color(tint.r, tint.g, tint.b, 1f);
    }
}