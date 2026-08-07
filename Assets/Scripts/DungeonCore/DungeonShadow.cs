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

    [Tooltip("Open ground within this many tiles of unexplored fog darkens toward " +
             "voidLightFloor, so a revealed stretch fades into the dark instead of " +
             "stopping at a line. A hard edge mid-corridor reads as a wall, because " +
             "there is no architecture out there to justify one.\n\n" +
             "This is the INSIDE of the boundary. Fading the fog's alpha outward " +
             "instead cannot work: CaveWallRenderer frames only cells claimed or " +
             "8-adjacent to mined floor, so anything made visible past that shows " +
             "bare floor with no wall drawn on it.\n\n" +
             "0 disables. Tighter than breachFadeTiles on purpose -- that one bleeds " +
             "light INTO a space, this one recedes into dark.")]
    [SerializeField, Min(0)] private int frontierFadeTiles = 4;

    [Tooltip("Fade claimed ground at the fog boundary too. Off by default: the " +
             "player's own domain keeps its flat light and the influence ring " +
             "already sits on that edge. On, it softens every reveal boundary alike.")]
    [SerializeField] private bool frontierFadeClaimed = true;

    [Header("Cursor")]
    [Tooltip("Cells within this radius of the cursor brighten toward full light (active floor only). 0 disables.")]
    [SerializeField, Min(0)] private int cursorRadius = 4;

    [Header("Cursor Light")]
    [Tooltip("The DCR/AdditiveSprite shader for the cursor light sprite. Falls back to Shader.Find " +
             "if unset; assign it so builds include the shader.")]
    [SerializeField] private Shader cursorLightShader;
    [Tooltip("Radius of the additive cursor light in cells -- the smooth taper over void. 0 disables the sprite.")]
    [SerializeField, Min(0f)] private float cursorLightRadius = 4.5f;
    [Tooltip("Peak brightness of the light at its centre.")]
    [SerializeField, Range(0f, 1f)] private float cursorLightIntensity = 0.45f;
    [Tooltip("Light colour. White reads neutral; warm it slightly for a lantern feel.")]
    [SerializeField] private Color cursorLightColor = Color.white;

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
    [SerializeField, Min(1)] private int voidFalloffCells = 1;

    [Tooltip("Light on the OUTERMOST cell of floor 0's rim facade -- the face the forest " +
             "sees. It ramps inward across the facade's depth to voidLightFloor, which is " +
             "the level DeepVoidColor is built from, so the lit rim meets the dark instead " +
             "of stopping against it in one cell. 1 is full daylight.")]
    [SerializeField, Range(0f, 1f)] private float rimFacadeLight = 1f;

    [Tooltip("Light on the LAST art row of the rim facade -- the row before the " +
             "innermost, which is painted as void. It must satisfy one equation, not " +
             "taste: art x light has to equal the void's voidBaseColor x voidLightFloor, " +
             "or the last art row and the void beside it are different brightnesses and " +
             "the fade ends in a step. With grass-topped cliff art measured at 98 luma " +
             "against the void's 15, that lands near 0.15. BRIGHTER art needs a LOWER " +
             "number here, not a higher one -- getting that backwards is what left a " +
             "fifty-point cliff in the first build.")]
    [SerializeField, Range(0f, 1f)] private float rimFacadeInnerLight = 0.5f;
    [Tooltip("How much of the core type's colour bleeds into deep rock. 0 disables.")]
    [SerializeField, Range(0f, 1f)] private float coreHueStrength = 0f;
    [Tooltip("Paint void cells opaque (base colour x light + hue) instead of alpha-darkening. " +
             "Required while the interior cap art is flat black; turn off if you ever texture it.")]
    [SerializeField] private bool voidOpaqueFill = true;
    [Tooltip("Fully-lit rock tone for the opaque void paint; the falloff scales it down toward the depths.")]
    [SerializeField] private Color voidBaseColor = new Color(0.267f, 0.267f, 0.267f, 1f);
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
    private int lastWallTick = -1;
    private bool subscribed;
    private bool dirty;

    // Scratch collections, reused across rebuilds. These used to be allocated
    // fresh every rebuild and grew with the claimed area, which is where the
    // multi-megabyte per-frame garbage came from.
    private readonly Dictionary<Vector3Int, int> breachDist = new();
    private readonly Dictionary<Vector3Int, int> frontierDist = new();
    private readonly Dictionary<Vector3Int, int> voidDepth = new();
    private readonly Dictionary<Vector3Int, float> voidRim = new();
    private readonly Queue<Vector3Int> bfsQueue = new();
    private readonly List<Vector3Int> minedKeysScratch = new();
    private readonly HashSet<Vector3Int> seenWallsScratch = new();
    private readonly List<Vector3Int> removeScratch = new();

    // What is currently on the tilemap, so a rebuild can repaint only what moved
    // instead of clearing and re-setting every cell.
    private readonly HashSet<Vector3Int> paintedCells = new();
    private readonly Dictionary<Vector3Int, Color> paintedColor = new();

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null) { Debug.LogWarning("[DungeonShadow] No FloorRoot in parents — disabling."); enabled = false; return; }
        influence = floor.TileInfluence;
        wallRenderer = floor.GetComponentInChildren<CaveWallRenderer>(true);

        // These may live anywhere under the floor — resolve robustly rather than
        // assuming a shared GameObject. A silent gold fallback hid a missed ring
        // reference once (fog and void computed with the wrong core hue); the
        // warning below makes sure that class of miss can never be quiet again.
        ring = GetComponent<InfluenceRingRenderer>();
        if (ring == null) ring = GetComponentInParent<InfluenceRingRenderer>();
        if (ring == null) ring = floor.GetComponentInChildren<InfluenceRingRenderer>(true);
        if (ring == null)
            Debug.LogWarning("[DungeonShadow] No InfluenceRingRenderer found under this floor — void and fog hues fall back to gold.");

        floorTint = GetComponent<FloorTint>();
        if (floorTint == null) floorTint = GetComponentInParent<FloorTint>();
        if (floorTint == null) floorTint = floor.GetComponentInChildren<FloorTint>(true);
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
        HideCursorLight();
        baseLight.Clear(); baseTint.Clear(); cursorCells.Clear(); voidCells.Clear();
        paintedCells.Clear(); paintedColor.Clear();
    }

    private void MarkDirty(int _) => dirty = true;

    private void LateUpdate()
    {
        if (shadowTilemap == null || influence == null) return;

        // Follow the wall renderer rather than keeping our own schedule. On
        // independent gates a newly claimed cell gets its cap a frame or two
        // before its void shading, which reads as the claim edge flickering.
        // The tick plus DefaultExecutionOrder puts cap and shade in one frame.
        int wallTick = wallRenderer != null ? wallRenderer.RebuildTick : 0;
        if (wallRenderer != null && wallTick != lastWallTick) { dirty = true; lastWallTick = wallTick; }

        // No frame gate. Deferring a rebuild leaves a newly claimed cell showing
        // raw terrain art until the caps and void shading arrive, which reads as
        // the claim edge flickering. The rebuild is cheap now, so it lands in the
        // same frame as the claim, as it did before any throttling existed.
        if (dirty) { RecomputeBase(); dirty = false; }

        UpdateCursor();
    }

    private static Color ColorFor(float light, Color tint)
        => new Color(tint.r, tint.g, tint.b, 1f - Mathf.Clamp01(light));

    private void RecomputeBase()
    {
        // No ClearAllTiles: the paint pass below diffs against what is already
        // drawn, so a rebuild touches only cells that actually changed. Cursor
        // cells are left alone -- UpdateCursor restores them from baseLight.
        baseLight.Clear();
        baseTint.Clear();
        voidCells.Clear();

        // 1) base light on every mined cell: claimed flat, unclaimed with a breach
        //    fade, then everything eased down toward the void at the fog frontier.
        BreachDistances();
        FrontierDistances();
        Dictionary<Vector3Int, int> dist = breachDist;
        bool fadeFrontier = frontierFadeTiles > 0 && frontierDist.Count > 0;
        foreach (Vector3Int cell in influence.MinedTiles)
        {
            float light;
            bool claimed = influence.IsTileClaimed(cell);
            if (claimed)
                light = claimedLight;
            else
            {
                int d = dist.TryGetValue(cell, out int v) ? v : breachFadeTiles;
                float t = 1f - Mathf.Clamp01((float)d / breachFadeTiles);
                light = Mathf.Lerp(unclaimedLight, claimedLight, t);
            }

            // FRONTIER FADE. Applied last so it wins: a cell against the fog is
            // dark whatever the breach fade wanted for it. It lands on
            // voidLightFloor, which is the level DeepVoidColor is built from and
            // the colour fogMatchesVoid paints the fog, so the gradient meets the
            // dark rather than butting against it.
            if (fadeFrontier && (frontierFadeClaimed || !claimed)
                && frontierDist.TryGetValue(cell, out int fd))
            {
                float ft = Mathf.Clamp01((float)fd / frontierFadeTiles);
                light = Mathf.Lerp(voidLightFloor, light, ft);
            }

            baseLight[cell] = light;
            baseTint[cell] = Color.black;
        }

        // 1b) the PREPARED ROAD BAND. Not mined, so the loop above skipped it, and
        //     it rendered brighter than the revealed stretch beside it: raw floor
        //     tile under thin fog, against ground the shadow had darkened to
        //     unclaimedLight. Ground further into the unknown glowed.
        //
        //     Matched to the revealed road where the two meet, ramping to
        //     voidLightFloor at the band's far edge -- the same span the fog thins
        //     over, so light and fog agree rather than fighting. Wall caps come
        //     free: step 3 snapshots baseLight's keys and gives every cell here
        //     its rim.
        var bandFeats = floor != null ? floor.FeatureGenerator : null;
        if (bandFeats != null && bandFeats.RoadPrepareCells > 0)
        {
            int span = Mathf.Max(1, bandFeats.RoadPrepareCells - 1);
            foreach (var band in bandFeats.RoadFeatherBand)
            {
                if (influence.IsTileMined(band.Key)) continue;   // handled above
                float bt = 1f - Mathf.Clamp01((band.Value - 1) / (float)span);
                baseLight[band.Key] = Mathf.Lerp(voidLightFloor, unclaimedLight, bt);
                baseTint[band.Key] = Color.black;
            }
        }

        // 1c) the RIM FACADE band. Same shape of problem as 1b and the same cure:
        //     an unmined band that nothing else enters into the light map, so it
        //     rendered at full brightness and then hit black in a single cell.
        //     Ramped from rimFacadeLight on the outermost row to rimFacadeInnerLight
        //     on the last row carrying art, with the INNERMOST row registered as
        //     void so it paints at the void's own level through the void's own
        //     function. The wall fades onto the dark rather than past it.
        var rimTerr = floor != null ? floor.Terrain : null;
        if (rimTerr != null && rimTerr.RimFacadeLayers.Count > 0)
        {
            int rimLast = Mathf.Max(0, rimTerr.RimFacadeDepth - 1);
            // Span the ART rows only. Dividing by rimLast counted the void row as a
            // ramp step, so rimFacadeInnerLight was never reached by anything: at
            // depth 4 the last art row landed on 0.667 instead of 0.5 and then fell
            // straight to the void.
            int rimArtSpan = Mathf.Max(1, rimLast - 1);
            var rimFeats = floor != null ? floor.FeatureGenerator : null;

            foreach (var rimCell in rimTerr.RimFacadeLayers)
            {
                if (influence.IsTileMined(rimCell.Key)) continue;

                // Only solid ROCK may become void. rimLayers is built from geometry
                // alone, so it contains the river cells where a river crosses the
                // rim -- and registering those as void painted an opaque black block
                // straight over open water at the river mouth. Water and road keep
                // the alpha overlay instead, so they darken into the cave.
                bool rimRock = true;
                if (rimFeats != null)
                {
                    FeatureType rimF = rimFeats.GetFeatureAt(rimCell.Key);
                    if (rimF == FeatureType.River || rimF == FeatureType.Road) rimRock = false;
                }

                // The innermost ROCK row is registered as void, so step 5 paints it
                // through VoidColorFor at the void's own level. That makes the
                // meeting point exact by construction -- the same function of the
                // same number -- rather than something to tune by eye.
                if (rimCell.Value >= rimLast)
                {
                    baseLight[rimCell.Key] = voidLightFloor;
                    baseTint[rimCell.Key] = Color.black;
                    if (rimRock) voidCells.Add(rimCell.Key);
                    continue;
                }

                float rt = 1f - Mathf.Clamp01(rimCell.Value / (float)rimArtSpan);
                baseLight[rimCell.Key] = Mathf.Lerp(rimFacadeInnerLight, rimFacadeLight, rt);
                baseTint[rimCell.Key] = Color.black;
            }
        }

        // 2) moss glow (green cols 0-3, gold 4-7): extra light + subtle colour on nearby mined cells.
        if (wallRenderer != null)
        {
            foreach (Vector3Int c in wallRenderer.GreenMossWalls) ApplyMossGlow(c, greenGlow);
            foreach (Vector3Int c in wallRenderer.GoldMossWalls) ApplyMossGlow(c, goldGlow);
        }

        // 3) wall caps (solid cells touching open floor) inherit the brightest adjacent open
        //    cell, so a cavern rim darkens with it. Snapshot keys first — we add walls as we go.
        minedKeysScratch.Clear();
        foreach (Vector3Int k in baseLight.Keys) minedKeysScratch.Add(k);
        var minedKeys = minedKeysScratch;
        seenWallsScratch.Clear();
        var seenWalls = seenWallsScratch;
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
        removeScratch.Clear();
        foreach (Vector3Int cell in paintedCells)
            if (!baseLight.ContainsKey(cell)) removeScratch.Add(cell);
        for (int i = 0; i < removeScratch.Count; i++)
        {
            shadowTilemap.SetTile(removeScratch[i], null);
            paintedCells.Remove(removeScratch[i]);
            paintedColor.Remove(removeScratch[i]);
        }

        foreach (KeyValuePair<Vector3Int, float> kv in baseLight)
        {
            Color c = ShadeFor(kv.Key, kv.Value);
            if (paintedCells.Add(kv.Key)) shadowTilemap.SetTile(kv.Key, whiteTile);
            else if (paintedColor.TryGetValue(kv.Key, out Color prev) && prev == c) continue;
            shadowTilemap.SetColor(kv.Key, c);
            paintedColor[kv.Key] = c;
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

        var queue = bfsQueue; queue.Clear();
        var depth = voidDepth; depth.Clear();
        var rimLight = voidRim; rimLight.Clear();

        // Keyed on EVER-claimed, not currently-claimed. A breach recede strips
        // ownership; it must not strip light. Under the old rule a receded cell
        // dropped out of baseLight, the diff-paint below removed its shadow tile,
        // and the flat-black interior cap art underneath showed through -- which
        // read as the cell being re-fogged even though no fog tile ever moved.
        // Do not re-key this on IsTileClaimed.
        bool IsVoidRock(Vector3Int c)
            => influence.WasEverClaimed(c)
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

                // Past voidFalloffCells the curve has already reached voidLightFloor,
                // so every deeper cell would be handed the identical plateau value --
                // which the rimless sweep below assigns anyway. Stopping here makes
                // the flood cover the lit rim band instead of the entire claimed mass.
                if (nd < voidFalloffCells) queue.Enqueue(n);
            }
        }

        // Rimless claimed rock (e.g. a channel tendril pushed through undug stone):
        // no step-3 light to fall from, so it sits at the plateau, faintly hued.
        foreach (Vector3Int c in influence.EverClaimedTiles)
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

    // Multi-source BFS over open floor from cells that ABUT UNEXPLORED FOG,
    // capped at frontierFadeTiles. Same shape as BreachDistances below, seeded
    // from the other kind of boundary.
    //
    // Confined to MinedTiles, which is the point rather than an optimisation:
    // that set is exactly what CaveWallRenderer has framed, so a fade computed
    // over it can never darken -- or reveal -- ground with no wall drawn on it.
    [ContextMenu("Log Frontier Fade State")]
    private void LogFrontierFadeState()
    {
        var fog = floor != null && floor.Terrain != null ? floor.Terrain.FogTilemap : null;
        int mined = influence != null ? influence.MinedTiles.Count : -1;

        FrontierDistances();
        int seeds = 0;
        float lo = 1f, hi = 0f;
        foreach (var kv in frontierDist)
        {
            if (kv.Value == 0) seeds++;
            if (baseLight.TryGetValue(kv.Key, out float l))
            {
                if (l < lo) lo = l;
                if (l > hi) hi = l;
            }
        }

        Debug.Log("[FrontierFade] floor " + (floor != null ? floor.FloorIndex : -1)
                + "  fadeTiles=" + frontierFadeTiles
                + "  fadeClaimed=" + frontierFadeClaimed
                + "  fog=" + (fog == null ? "MISSING" : "ok")
                + "  mined=" + mined
                + "  seeds=" + seeds
                + "  fadedCells=" + frontierDist.Count
                + "  lightRange=" + lo.ToString("F2") + ".." + hi.ToString("F2")
                + "  (voidLightFloor=" + voidLightFloor.ToString("F2") + ")");
    }

    private void FrontierDistances()
    {
        var dist = frontierDist; dist.Clear();
        if (frontierFadeTiles <= 0) return;

        var fog = floor != null && floor.Terrain != null ? floor.Terrain.FogTilemap : null;
        if (fog == null) return;

        var seedFeats = floor != null ? floor.FeatureGenerator : null;

        var queue = bfsQueue; queue.Clear();
        foreach (Vector3Int cell in influence.MinedTiles)
        {
            // Interior cells cannot be near fog -- every neighbour is open floor.
            // This culls most of the dug area before any tilemap probe, which is
            // what keeps the pass affordable on a heavily mined floor.
            bool perimeter = false;
            for (int dx = -1; dx <= 1 && !perimeter; dx++)
                for (int dy = -1; dy <= 1 && !perimeter; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (!influence.IsTileMined(new Vector3Int(cell.x + dx, cell.y + dy, cell.z)))
                        perimeter = true;
                }
            if (!perimeter) continue;

            // Probe to CHEBYSHEV 2, not 1. Every reveal path carries a one-cell
            // border -- UnfogRoadSegment, UnfogSite, UnfogRiver, UnfogCoreCavern,
            // ClaimStarterArea and the mined restore on load all reveal their
            // cells plus a ring -- so the wall fronting open floor is revealed and
            // fog starts TWO cells out. Probing one cell found nothing anywhere,
            // on any floor, and the fade silently did nothing at every setting.
            // AND the fog must cover ground the passage CONTINUES into. Fog behind
            // a wall needs no softening: the wall is the edge, it is drawn, and it
            // reads correctly. Seeding on any fog at all made a seed of every cell
            // beside every wall, so whole corridors dimmed uniformly and there was
            // no gradient left to see -- floor 1 went dark end to end.
            bool nearFog = false;
            for (int dx = -2; dx <= 2 && !nearFog; dx++)
                for (int dy = -2; dy <= 2 && !nearFog; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var probe = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                    if (fog.GetTile(probe) == null) continue;
                    FeatureType pf = seedFeats != null ? seedFeats.GetFeatureAt(probe)
                                                       : FeatureType.None;
                    if (pf == FeatureType.Road || pf == FeatureType.River) nearFog = true;
                }
            if (nearFog) { dist[cell] = 0; queue.Enqueue(cell); }
        }

        while (queue.Count > 0)
        {
            Vector3Int cur = queue.Dequeue();
            int d = dist[cur];
            if (d >= frontierFadeTiles) continue;
            foreach (Vector3Int dir in Dirs4)
            {
                Vector3Int n = cur + dir;
                if (dist.ContainsKey(n) || !influence.IsTileMined(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }
    }

    // Multi-source BFS over open floor from claimed open cells, capped at breachFadeTiles.
    private void BreachDistances()
    {
        var dist = breachDist; dist.Clear();
        var queue = bfsQueue; queue.Clear();
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

    // -- Cursor light sprite -----------------------------------------
    // The per-cell cursor pass cannot taper smoothly over void: SetColor is
    // one flat colour per cell, and the opaque void fill has no art
    // underneath to carry the gradient, so the smoothstep quantises into
    // 32px blocks. This additive sprite carries the void taper instead -- a
    // single radial gradient following the mouse, above the void fill
    // (Shadow / 5) and below the fog (Shadow / 10), so unexplored ground
    // still occludes it. Built at runtime like the mine highlight; nothing
    // to wire in the scene beyond the shader slot.

    private SpriteRenderer cursorLightRenderer;
    private Texture2D cursorLightTexture;

    private void EnsureCursorLight()
    {
        if (cursorLightRenderer != null) return;

        Shader shader = cursorLightShader != null ? cursorLightShader : Shader.Find("DCR/AdditiveSprite");
        if (shader == null) return;

        const int size = 128;
        cursorLightTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Vector2 centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), centre) / maxR;
                float u = Mathf.Clamp01(1f - r);
                float a = u * u * (3f - 2f * u) * u;   // smoothstep taper, softened shoulder
                cursorLightTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        cursorLightTexture.Apply();

        var go = new GameObject("CursorLight");
        go.transform.SetParent(transform, false);
        cursorLightRenderer = go.AddComponent<SpriteRenderer>();
        cursorLightRenderer.sprite = Sprite.Create(
            cursorLightTexture, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        cursorLightRenderer.sharedMaterial = new Material(shader);
        cursorLightRenderer.sortingLayerName = "Shadow";
        cursorLightRenderer.sortingOrder = 5;   // above the void fill (0), below the fog (10)
        cursorLightRenderer.enabled = false;
    }

    private void UpdateCursorLight(Vector3 world)
    {
        if (cursorLightRadius <= 0f) { HideCursorLight(); return; }
        EnsureCursorLight();
        if (cursorLightRenderer == null) return;

        cursorLightRenderer.transform.position = new Vector3(world.x, world.y, 0f);
        cursorLightRenderer.transform.localScale = Vector3.one * (cursorLightRadius * 2f);
        Color col = cursorLightColor;
        col.a = cursorLightIntensity;            // the shader multiplies rgb by alpha
        cursorLightRenderer.color = col;
        cursorLightRenderer.enabled = true;
    }

    private void HideCursorLight()
    {
        if (cursorLightRenderer != null) cursorLightRenderer.enabled = false;
    }

    // Per-frame: restore the previous cursor cells to base, then brighten cells near the
    // cursor on the active floor. Restoring every frame self-heals after a base recompute.
    private void UpdateCursor()
    {
        foreach (Vector3Int c in cursorCells)
            if (baseLight.TryGetValue(c, out float bl))
                shadowTilemap.SetColor(c, ShadeFor(c, bl));
        cursorCells.Clear();

        if (FloorManager.Instance == null || FloorManager.Instance.ActiveFloor != floor
            || Camera.main == null || Mouse.current == null)
        {
            HideCursorLight();
            return;
        }

        Vector3 world = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector3Int center = influence.WorldToCell(world);

        UpdateCursorLight(world);

        if (cursorRadius <= 0) return;

        for (int dx = -cursorRadius; dx <= cursorRadius; dx++)
            for (int dy = -cursorRadius; dy <= cursorRadius; dy++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > cursorRadius) continue;
                Vector3Int cell = center + new Vector3Int(dx, dy, 0);
                if (!baseLight.TryGetValue(cell, out float bl)) continue;
                if (voidCells.Contains(cell)) continue;   // the additive sprite carries the void taper
                float u = Mathf.Clamp01(1f - d / cursorRadius);
                float f = u * u * (3f - 2f * u);                  // smoothstep falloff
                float light = Mathf.Lerp(bl, 1f, f);
                shadowTilemap.SetColor(cell, ShadeFor(cell, light));
                cursorCells.Add(cell);
            }
    }
}
