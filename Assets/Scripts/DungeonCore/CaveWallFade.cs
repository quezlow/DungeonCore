using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Stage 4 — column-fade walk-behind transparency.
///
/// The cap and upper-face slices occlude entities tucked under a wall's overhang (the
/// cap rides the WalkBehind sorting layer; the upper slice Y-sorts behind an entity
/// standing on the upper cell). This restores readability: each LateUpdate it finds the
/// cells an entity is behind and fades that whole wall column toward a translucent
/// alpha, then fades it back to opaque once the entity leaves. The lower (base) slice
/// never hides anyone, but it fades in step with the rest of its column so the entire
/// wall segment goes translucent together.
///
/// Lives on (or under) the floor's CaveWallRenderer object. Assign the SAME three
/// tilemaps the renderer paints. Works because the wall tiles are UnlockedTile, which
/// permits per-cell SetColor.
/// </summary>
[DisallowMultipleComponent]
public class CaveWallFade : MonoBehaviour
{
    [Tooltip("Caps tilemap — the same one CaveWallRenderer paints.")]
    [SerializeField] private Tilemap capsTilemap;
    [Tooltip("Upper-face tilemap — the same one CaveWallRenderer paints.")]
    [SerializeField] private Tilemap facesTilemap;
    [Tooltip("Bottom/behind-face tilemap — the same one CaveWallRenderer paints.")]
    [SerializeField] private Tilemap facesBehindTilemap;

    [Tooltip("Alpha a wall fades to while an entity is behind it. 0 = invisible, 1 = opaque.")]
    [SerializeField, Range(0f, 1f)] private float fadeAlpha = 0.35f;
    [Tooltip("Alpha units per second for fade in/out. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float fadeSpeed = 6f;

    private static readonly Vector3Int N = new Vector3Int(0, 1, 0);

    private FloorRoot floor;
    private FloorEntityRegistry registry;

    // Current alpha of each cell that is currently faded or fading. Cells not present
    // are fully opaque (alpha 1).
    private readonly Dictionary<Vector3Int, float> capState = new();
    private readonly Dictionary<Vector3Int, float> faceState = new();
    private readonly Dictionary<Vector3Int, float> behindState = new();

    // Cells that SHOULD be faded this frame (rebuilt every LateUpdate).
    private readonly HashSet<Vector3Int> wantCaps = new();
    private readonly HashSet<Vector3Int> wantFaces = new();
    private readonly HashSet<Vector3Int> wantBehind = new();

    // Reused buffers — no per-frame allocation.
    private readonly List<DungeonMonster> monsters = new();
    private readonly List<DungeonAdventurer> adventurers = new();
    private readonly List<Vector3Int> restoreScratch = new();

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor != null) registry = floor.Entities;
        if (floor == null || registry == null)
            Debug.LogWarning("[CaveWallFade] No FloorRoot/registry in parents — disabling.");
    }

    private void LateUpdate()
    {
        if (capsTilemap == null || facesTilemap == null || registry == null) return;

        wantCaps.Clear();
        wantFaces.Clear();
        wantBehind.Clear();

        registry.FillAll(monsters);
        for (int i = 0; i < monsters.Count; i++)
            MarkBehind(monsters[i].transform.position);

        registry.FillAll(adventurers);
        for (int i = 0; i < adventurers.Count; i++)
            MarkBehind(adventurers[i].transform.position);

        StepFade(capsTilemap, capState, wantCaps);
        StepFade(facesTilemap, faceState, wantFaces);
        if (facesBehindTilemap != null)
            StepFade(facesBehindTilemap, behindState, wantBehind);
    }

    /// <summary>
    /// An entity at cell C is hidden by the upper-face on C (the cell it stands on) and
    /// the cap one (or two, for tall sprites) cells north. The bottom slice one cell
    /// SOUTH belongs to the same column and fades along for visual cohesion. Only mark
    /// cells that actually carry a tile.
    /// </summary>
    private void MarkBehind(Vector3 worldPos)
    {
        Vector3Int c = capsTilemap.WorldToCell(worldPos);
        Vector3Int up = c + N;
        Vector3Int up2 = up + N;
        Vector3Int down = c - N;

        if (facesTilemap.HasTile(c)) wantFaces.Add(c);
        if (facesTilemap.HasTile(up)) wantFaces.Add(up);
        if (capsTilemap.HasTile(c)) wantCaps.Add(c);
        if (capsTilemap.HasTile(up)) wantCaps.Add(up);
        // Tall entities: the head pokes above the cap into the rock one cell further up.
        // Extend ONLY the cap reach there — the cell two north of a monster at a wall's
        // foot is the open upper-cell (no cap), so standing in front won't fade the slice.
        if (capsTilemap.HasTile(up2)) wantCaps.Add(up2);
        // Bottom slice of the same column (one cell south of the upper cell). Present only
        // when C is an upper cell, so a monster at a wall's foot never triggers it.
        if (facesBehindTilemap != null && facesBehindTilemap.HasTile(down)) wantBehind.Add(down);
    }

    private void StepFade(Tilemap map, Dictionary<Vector3Int, float> state, HashSet<Vector3Int> want)
    {
        float step = fadeSpeed * Time.deltaTime;

        // Fade wanted cells down toward fadeAlpha.
        foreach (Vector3Int cell in want)
        {
            float cur = state.TryGetValue(cell, out float a) ? a : 1f;
            cur = Mathf.MoveTowards(cur, fadeAlpha, step);
            state[cell] = cur;
            map.SetColor(cell, new Color(1f, 1f, 1f, cur));
        }

        // Fade the rest back toward opaque; drop a cell once it is fully restored.
        restoreScratch.Clear();
        foreach (var kv in state)
            if (!want.Contains(kv.Key)) restoreScratch.Add(kv.Key);

        for (int i = 0; i < restoreScratch.Count; i++)
        {
            Vector3Int cell = restoreScratch[i];
            float cur = Mathf.MoveTowards(state[cell], 1f, step);
            if (cur >= 0.999f)
            {
                map.SetColor(cell, Color.white);
                state.Remove(cell);
            }
            else
            {
                state[cell] = cur;
                map.SetColor(cell, new Color(1f, 1f, 1f, cur));
            }
        }
    }
}