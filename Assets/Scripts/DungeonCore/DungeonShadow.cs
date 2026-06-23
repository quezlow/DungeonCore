using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// Stage 5 — per-cell darkness / glow over one floor's open areas. Paints a white
/// UnlockedTile on every mined cell (and the wall caps that front them) and tints it
/// per cell to (tintRGB, alpha = 1 - light): more light = more transparent = brighter.
///
/// Light per cell:
///   - claimed + mined   -> claimedLight   (default 0.90)
///   - unclaimed + mined  -> unclaimedLight  (default 0.50)
///   - breach fade        -> an unclaimed cell within breachFadeTiles of claimed open
///                           floor lerps up toward claimedLight, so light bleeds from a
///                           claimed edge into a newly opened feature
///   - moss glow          -> moss walls add mossBoost light + a subtle green/gold tint to
///                           mined cells within mossRadius (green cols 0-3, gold 4-7, read
///                           from CaveWallRenderer's split sets)
///   - cursor             -> within cursorRadius of the cursor the light lerps to 1.0 with
///                           a smooth falloff, like a carried light (active floor only)
///
/// The base (everything except the cursor) is static per cell, recomputed only when the
/// claimed / mined sets or the moss layout change; the cursor is a cheap per-frame delta
/// on top. The shadow tilemap sits on a sorting layer above the caps and entities, below
/// the gameplay highlights and world-space UI, and beneath the day/night overlay (which
/// darkens on top of it). Drop on the FloorRoot GameObject and assign the shadow tilemap.
/// </summary>
[DisallowMultipleComponent]
public class DungeonShadow : MonoBehaviour
{
    [Header("Layer")]
    [Tooltip("Tilemap on a 'Shadow' sorting layer placed just AFTER WalkBehind (above caps + " +
             "entities), before the highlight / WorldUI layers. Order in Layer 0, default Tile Anchor.")]
    [SerializeField] private Tilemap shadowTilemap;

    [Header("Light levels")]
    [Tooltip("Light on claimed, mined cells.")]
    [SerializeField, Range(0f, 1f)] private float claimedLight = 0.90f;
    [Tooltip("Light on unclaimed, mined cells (caverns, core tunnels, rivers).")]
    [SerializeField, Range(0f, 1f)] private float unclaimedLight = 0.50f;
    [Tooltip("An unclaimed cell within this many open-floor tiles of claimed floor fades up " +
             "toward the claimed level.")]
    [SerializeField, Min(1)] private int breachFadeTiles = 7;

    [Header("Cursor")]
    [Tooltip("Cells within this radius of the cursor brighten toward full light (active floor only). 0 disables.")]
    [SerializeField, Min(0)] private int cursorRadius = 4;

    [Header("Moss glow")]
    [Tooltip("Moss walls light mined cells within this radius.")]
    [SerializeField, Min(0)] private int mossRadius = 3;
    [Tooltip("Extra light a moss wall adds at its edge, falling off over mossRadius.")]
    [SerializeField, Range(0f, 1f)] private float mossBoost = 0.05f;
    [Tooltip("Colour cast of green moss (cols 0-3). Only the RGB is used.")]
    [SerializeField] private Color greenGlow = new Color(0.40f, 0.85f, 0.45f, 1f);
    [Tooltip("Colour cast of gold moss (cols 4-7). Only the RGB is used.")]
    [SerializeField] private Color goldGlow = new Color(0.95f, 0.82f, 0.45f, 1f);
    [Tooltip("How strong the moss colour cast is. Keep low for a subtle tint.")]
    [SerializeField, Range(0f, 1f)] private float mossTintStrength = 0.15f;

    private const float MaxLight = 1f;
    private static readonly Vector3Int[] Dirs4 =
        { new Vector3Int(0,1,0), new Vector3Int(0,-1,0), new Vector3Int(1,0,0), new Vector3Int(-1,0,0) };
    private static readonly Vector3Int[] Dirs8 =
    {
        new Vector3Int(0,1,0), new Vector3Int(0,-1,0), new Vector3Int(1,0,0), new Vector3Int(-1,0,0),
        new Vector3Int(1,1,0), new Vector3Int(-1,1,0), new Vector3Int(1,-1,0), new Vector3Int(-1,-1,0),
    };

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private CaveWallRenderer wallRenderer;
    private TileBase whiteTile;
    private readonly Dictionary<Vector3Int, float> baseLight = new();
    private readonly Dictionary<Vector3Int, Color> baseTint = new();
    private readonly HashSet<Vector3Int> cursorCells = new();
    private int lastMossCount = -1;
    private bool subscribed;
    private bool dirty;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null) { Debug.LogWarning("[DungeonShadow] No FloorRoot in parents — disabling."); enabled = false; return; }
        influence = floor.TileInfluence;
        wallRenderer = floor.GetComponentInChildren<CaveWallRenderer>(true);
        whiteTile = BuildWhiteTile();
    }

    private TileBase BuildWhiteTile()
    {
        var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply();
        var spr = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        var tile = ScriptableObject.CreateInstance<UnlockedTile>();
        tile.sprite = spr;
        return tile;
    }

    private void OnEnable()
    {
        if (influence != null && !subscribed)
        {
            influence.OnClaimedTileCountChanged += MarkDirty;
            influence.OnTileCountChanged += MarkDirty;
            subscribed = true;
        }
        dirty = true;
    }

    private void OnDisable()
    {
        if (influence != null && subscribed)
        {
            influence.OnClaimedTileCountChanged -= MarkDirty;
            influence.OnTileCountChanged -= MarkDirty;
            subscribed = false;
        }
        if (shadowTilemap != null) shadowTilemap.ClearAllTiles();
        baseLight.Clear(); baseTint.Clear(); cursorCells.Clear();
    }

    private void MarkDirty(int _) => dirty = true;

    private void LateUpdate()
    {
        if (shadowTilemap == null || influence == null) return;

        // The renderer rebuilds its moss sets in its own LateUpdate; poll the count so the
        // glow refreshes the moment the walls (re)build, without coupling to its timing.
        int mc = MossCount();
        if (mc != lastMossCount) { dirty = true; lastMossCount = mc; }

        if (dirty) { RecomputeBase(); dirty = false; }
        UpdateCursor();
    }

    private int MossCount()
        => wallRenderer == null ? 0 : wallRenderer.GreenMossWalls.Count + wallRenderer.GoldMossWalls.Count;

    private static Color ColorFor(float light, Color tint)
        => new Color(tint.r, tint.g, tint.b, 1f - Mathf.Clamp01(light));

    private void RecomputeBase()
    {
        shadowTilemap.ClearAllTiles();
        baseLight.Clear();
        baseTint.Clear();
        cursorCells.Clear();   // tilemap was cleared; the cursor re-applies this frame

        // 1) base light on every mined cell: claimed flat, unclaimed with a breach fade.
        Dictionary<Vector3Int, int> dist = BreachDistances();
        foreach (Vector3Int cell in influence.MinedTiles)
        {
            float light;
            if (influence.IsTileClaimed(cell))
                light = claimedLight;
            else
            {
                int d = dist.TryGetValue(cell, out int v) ? v : breachFadeTiles;
                float t = 1f - Mathf.Clamp01((float)d / breachFadeTiles);
                light = Mathf.Lerp(unclaimedLight, claimedLight, t);
            }
            baseLight[cell] = light;
            baseTint[cell] = Color.black;
        }

        // 2) moss glow (green cols 0-3, gold 4-7): extra light + subtle colour on nearby mined cells.
        if (wallRenderer != null)
        {
            foreach (Vector3Int c in wallRenderer.GreenMossWalls) ApplyMossGlow(c, greenGlow);
            foreach (Vector3Int c in wallRenderer.GoldMossWalls) ApplyMossGlow(c, goldGlow);
        }

        // 3) wall caps (solid cells touching open floor) inherit the brightest adjacent open
        //    cell, so a cavern rim darkens with it. Snapshot keys first — we add walls as we go.
        var minedKeys = new List<Vector3Int>(baseLight.Keys);
        var seenWalls = new HashSet<Vector3Int>();
        foreach (Vector3Int open in minedKeys)
            foreach (Vector3Int dir in Dirs8)
            {
                Vector3Int w = open + dir;
                if (influence.IsTileMined(w) || !seenWalls.Add(w)) continue;
                float best = -1f; Color bestTint = Color.black;
                foreach (Vector3Int d2 in Dirs8)
                {
                    Vector3Int o = w + d2;
                    if (influence.IsTileMined(o) && baseLight.TryGetValue(o, out float ol) && ol > best)
                    { best = ol; bestTint = baseTint[o]; }
                }
                if (best >= 0f) { baseLight[w] = best; baseTint[w] = bestTint; }
            }

        // 4) paint.
        foreach (KeyValuePair<Vector3Int, float> kv in baseLight)
        {
            shadowTilemap.SetTile(kv.Key, whiteTile);
            shadowTilemap.SetColor(kv.Key, ColorFor(kv.Value, baseTint[kv.Key]));
        }
    }

    // Multi-source BFS over open floor from claimed open cells, capped at breachFadeTiles.
    private Dictionary<Vector3Int, int> BreachDistances()
    {
        var dist = new Dictionary<Vector3Int, int>();
        var queue = new Queue<Vector3Int>();
        foreach (Vector3Int cell in influence.MinedTiles)
            if (influence.IsTileClaimed(cell)) { dist[cell] = 0; queue.Enqueue(cell); }
        while (queue.Count > 0)
        {
            Vector3Int cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= breachFadeTiles) continue;
            foreach (Vector3Int dir in Dirs4)
            {
                Vector3Int n = cur + dir;
                if (dist.ContainsKey(n) || !influence.IsTileMined(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    // A moss wall lights mined cells within mossRadius, adding mossBoost light and a subtle
    // colour cast, both falling off with distance. Caps are handled afterwards, so this only
    // touches mined cells already in baseLight.
    private void ApplyMossGlow(Vector3Int source, Color glow)
    {
        for (int dx = -mossRadius; dx <= mossRadius; dx++)
            for (int dy = -mossRadius; dy <= mossRadius; dy++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > mossRadius) continue;
                Vector3Int t = source + new Vector3Int(dx, dy, 0);
                if (!baseLight.ContainsKey(t)) continue;
                float f = 1f - d / (mossRadius + 1f);
                baseLight[t] = Mathf.Min(MaxLight, baseLight[t] + mossBoost * f);
                Color tint = baseTint[t];
                float k = mossTintStrength * f;
                baseTint[t] = new Color(
                    Mathf.Min(1f, tint.r + glow.r * k),
                    Mathf.Min(1f, tint.g + glow.g * k),
                    Mathf.Min(1f, tint.b + glow.b * k), 1f);
            }
    }

    // Per-frame: restore the previous cursor cells to base, then brighten cells near the
    // cursor on the active floor. Restoring every frame self-heals after a base recompute.
    private void UpdateCursor()
    {
        foreach (Vector3Int c in cursorCells)
            if (baseLight.TryGetValue(c, out float bl))
                shadowTilemap.SetColor(c, ColorFor(bl, baseTint[c]));
        cursorCells.Clear();

        if (cursorRadius <= 0) return;
        if (FloorManager.Instance == null || FloorManager.Instance.ActiveFloor != floor) return;
        if (Camera.main == null || Mouse.current == null) return;

        Vector3 world = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector3Int center = influence.WorldToCell(world);

        for (int dx = -cursorRadius; dx <= cursorRadius; dx++)
            for (int dy = -cursorRadius; dy <= cursorRadius; dy++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > cursorRadius) continue;
                Vector3Int cell = center + new Vector3Int(dx, dy, 0);
                if (!baseLight.TryGetValue(cell, out float bl)) continue;
                float u = Mathf.Clamp01(1f - d / cursorRadius);
                float f = u * u * (3f - 2f * u);                  // smoothstep falloff
                float light = Mathf.Lerp(bl, 1f, f);
                shadowTilemap.SetColor(cell, ColorFor(light, baseTint[cell]));
                cursorCells.Add(cell);
            }
    }
}
