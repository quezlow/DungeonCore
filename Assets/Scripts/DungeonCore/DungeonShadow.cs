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
///   - void               -> claimed solid rock (the cap interiors) joins the light map:
///                           light falls from the rim value to voidLightFloor over
///                           voidFalloffCells, then plateaus across the mass, with a
///                           faint core-type hue (coreHueStrength). With voidOpaqueFill
///                           the shadow tile PAINTS the void outright (voidBaseColor x
///                           light + hue) — the interior cap art is flat black, and
///                           darkening black shows nothing. Off, it falls back to the
///                           darkening overlay for textured interior art. With
///                           fogMatchesVoid, unexplored fog inherits DeepVoidColor so
///                           the claim boundary through solid rock stops rendering as
///                           a two-tone seam; the floor's depth tint folds into both.
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

    [Header("Void (claimed rock interior)")]
    [Tooltip("Darkness floor deep inside claimed rock. Rim light falls to this level.")]
    [SerializeField, Range(0f, 1f)] private float voidLightFloor = 0.22f;
    [Tooltip("Cells over which rim light falls to the floor level, then plateaus.")]
    [SerializeField, Min(1)] private int voidFalloffCells = 4;
    [Tooltip("How much of the core type's colour bleeds into deep rock. 0 disables.")]
    [SerializeField, Range(0f, 1f)] private float coreHueStrength = 0.12f;
    [Tooltip("Paint void cells opaque (base colour x light + hue) instead of alpha-darkening. " +
             "Required while the interior cap art is flat black; turn off if you ever texture it.")]
    [SerializeField] private bool voidOpaqueFill = true;
    [Tooltip("Fully-lit rock tone for the opaque void paint; the falloff scales it down toward the depths.")]
    [SerializeField] private Color voidBaseColor = new Color(0.16f, 0.14f, 0.13f, 1f);
    [Tooltip("Unexplored fog inherits the deep-void tone (per core type, per floor tint), erasing " +
             "the two-tone seam where claimed rock meets unrevealed rock. Requires a bright/white " +
             "fog tile sprite — fog renders as sprite x colour. Off restores FloorTint's fog colour.")]
    [SerializeField] private bool fogMatchesVoid = true;

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
    private InfluenceRingRenderer ring;
    private FloorTint floorTint;
    private TileBase whiteTile;
    private readonly Dictionary<Vector3Int, float> baseLight = new();
    private readonly Dictionary<Vector3Int, Color> baseTint = new();
    private readonly HashSet<Vector3Int> voidCells = new();
    private Color voidHueTerm = Color.black;
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
        ring = GetComponent<InfluenceRingRenderer>();
        floorTint = GetComponent<FloorTint>();
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
        baseLight.Clear(); baseTint.Clear(); cursorCells.Clear(); voidCells.Clear();
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
        voidCells.Clear();
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

        // 4) the void: claimed solid rock joins the light map with a rim-to-floor
        //    falloff, so cap interiors read as tinted dark instead of dead black.
        ApplyVoidLight();

        // 5) paint. Void cells paint an OPAQUE colour (the black interior art
        //    has nothing to darken); everything else keeps the darkening overlay.
        foreach (KeyValuePair<Vector3Int, float> kv in baseLight)
        {
            shadowTilemap.SetTile(kv.Key, whiteTile);
            shadowTilemap.SetColor(kv.Key, ShadeFor(kv.Key, kv.Value));
        }

        // 6) fog match: unexplored darkness inherits the deep-void tone, so the
        //    claim boundary through solid rock stops rendering as a two-tone
        //    seam. Recomputing here means core-type changes and loads track for
        //    free — any claim refreshes it.
        if (fogMatchesVoid && floor != null && floor.Terrain != null && floor.Terrain.FogTilemap != null)
            floor.Terrain.FogTilemap.color = DeepVoidColor;
    }

    // BFS from the lit rim caps through claimed solid rock: light falls from each
    // rim cell's (already moss-kissed) value to voidLightFloor over voidFalloffCells,
    // then plateaus across the rest of the mass. Mirrors CaveWallClassifier's solid
    // rule — rivers are never rock, and out-of-disc cells can't be claimed. Deep
    // cells take a faint core-type hue, so a Dark core's stone reads violet-black.
    // Claimed rock with no lit rim at all (a pushed tendril before any digging)
    // gets the plateau level directly — your influence in the rock glows faint.
    // BFS from the lit rim caps through claimed solid rock: light falls from each
    // rim cell's (already moss-kissed) value to voidLightFloor over voidFalloffCells,
    // then plateaus across the rest of the mass. Mirrors CaveWallClassifier's solid
    // rule — rivers are never rock, and out-of-disc cells can't be claimed. Deep
    // cells take a faint core-type hue, so a Dark core's stone reads violet-black.
    // Claimed rock with no lit rim at all (a pushed tendril before any digging)
    // gets the plateau level directly — your influence in the rock glows faint.
    // Void cells register in voidCells so the paint and cursor passes can render
    // them OPAQUE (voidOpaqueFill): the interior cap art is flat black, and an
    // alpha-darkening overlay over black shows nothing.
    private void ApplyVoidLight()
    {
        var features = floor != null ? floor.FeatureGenerator : null;
        Color hue = VoidHue();
        voidHueTerm = new Color(hue.r * coreHueStrength, hue.g * coreHueStrength,
                                hue.b * coreHueStrength, 1f);
        var deepTint = voidHueTerm;

        var queue = new Queue<Vector3Int>();
        var depth = new Dictionary<Vector3Int, int>();
        var rimLight = new Dictionary<Vector3Int, float>();

        bool IsVoidRock(Vector3Int c)
            => influence.IsTileClaimed(c)
            && !influence.IsTileMined(c)
            && !(features != null && features.IsRiver(c));

        // Seeds: rim cells already lit by step 3 that are claimed solid rock.
        foreach (KeyValuePair<Vector3Int, float> kv in baseLight)
        {
            if (!IsVoidRock(kv.Key)) continue;
            depth[kv.Key] = 0;
            rimLight[kv.Key] = kv.Value;
            queue.Enqueue(kv.Key);
        }

        while (queue.Count > 0)
        {
            Vector3Int cur = queue.Dequeue();
            int d = depth[cur];
            foreach (Vector3Int dir in Dirs4)
            {
                Vector3Int n = cur + dir;
                if (depth.ContainsKey(n) || baseLight.ContainsKey(n)) continue;
                if (!IsVoidRock(n)) continue;

                int nd = d + 1;
                depth[n] = nd;
                rimLight[n] = rimLight[cur];

                float t = Mathf.Clamp01((float)nd / voidFalloffCells);
                baseLight[n] = Mathf.Max(voidLightFloor, Mathf.Lerp(rimLight[n], voidLightFloor, t));
                baseTint[n] = deepTint;
                voidCells.Add(n);
                queue.Enqueue(n);
            }
        }

        // Rimless claimed rock (e.g. a channel tendril pushed through undug stone):
        // no step-3 light to fall from, so it sits at the plateau, faintly hued.
        foreach (Vector3Int c in influence.ClaimedTiles)
        {
            if (baseLight.ContainsKey(c) || !IsVoidRock(c)) continue;
            baseLight[c] = voidLightFloor;
            baseTint[c] = deepTint;
            voidCells.Add(c);
        }
    }

    /// <summary>Opaque void colour: base rock tone scaled by the cell's light,
    /// plus the core-type whisper, all multiplied by the floor's depth tint so
    /// deep-floor rock cools with its caps. Only used when voidOpaqueFill is
    /// on — it PAINTS the void rather than darkening art that is already black.</summary>
    private Color VoidColorFor(float light)
    {
        Color t = floorTint != null ? floorTint.CurrentTint : Color.white;
        return new Color(
            Mathf.Clamp01((voidBaseColor.r * light + voidHueTerm.r) * t.r),
            Mathf.Clamp01((voidBaseColor.g * light + voidHueTerm.g) * t.g),
            Mathf.Clamp01((voidBaseColor.b * light + voidHueTerm.b) * t.b),
            1f);
    }

    /// <summary>The plateau tone — what the deepest rock paints as. The fog
    /// match uses this so unexplored darkness reads as the same stone.</summary>
    public Color DeepVoidColor => VoidColorFor(voidLightFloor);


    /// <summary>Shadow colour for a cell: opaque paint for void cells (when
    /// enabled), the classic alpha-darkening overlay for everything else.</summary>
    private Color ShadeFor(Vector3Int cell, float light)
        => voidOpaqueFill && voidCells.Contains(cell)
            ? VoidColorFor(light)
            : ColorFor(light, baseTint[cell]);


    private static readonly Color FallbackGold = new Color(0.784f, 0.565f, 0.165f, 1f);

    private Color VoidHue()
        => ring != null ? ring.CurrentTypeColor : FallbackGold;

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
                shadowTilemap.SetColor(c, ShadeFor(c, bl));
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
                shadowTilemap.SetColor(cell, ShadeFor(cell, light));
                cursorCells.Add(cell);
            }
    }
}
