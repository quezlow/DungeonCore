using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ethereal boundary ring — the visual identity of the claim rework.
///
/// Maintains a per-floor field texture (R = signed distance to the claimed
/// boundary, G = normalized free-growth cost from InfluenceField) and drives a
/// world-space quad running the DCR/InfluenceRing shader. Bilinear sampling
/// turns the blocky per-cell boundary into organic curves; the shader wavers
/// the isoline with two octaves of scrolling noise, glows asymmetrically (a
/// short falloff into claimed ground, a long soft tail bleeding into the fog),
/// and pulses gently. Ring color follows the core's DungeonType through the
/// serialized palette (polled — there is no type-change event and none is
/// needed).
///
/// The same quad renders the free-growth overlay: a faint fill over unclaimed
/// ground the ambient creep can reach (cost within effective reach). Because
/// the texture's G channel stores raw normalized cost and the shader compares
/// it against a per-frame uniform, reach changes — level surges, breach
/// suppression, recovery — animate with zero texture rebuilds. The overlay
/// shows while toggled (GameAction.ToggleInfluenceOverlay, default O) or
/// whenever the build mode is Push, and fades in and out smoothly.
///
/// Texture rebuilds are event-driven: claim-count changes (a single event per
/// batch recede) redo the boundary SDF; field recomputes (chamber clears)
/// redo the growth channel. Both channels rebuild together on either trigger.
///
/// Setup: lives on the FloorRoot GameObject (Floor 1 scene object and the
/// Floor Template prefab). Assign the Ring Shader slot and set the sorting
/// layer/order so the quad draws ABOVE walls, faces, and units
/// (AdjacentHighlight) — both the band and the wash cover everything the eye
/// sees; WorldUI stays clear above it.
/// </summary>
[DisallowMultipleComponent]
public class InfluenceRingRenderer : MonoBehaviour
{
    [Header("Shader")]
    [Tooltip("The DCR/InfluenceRing shader asset. Falls back to Shader.Find if unset.")]
    [SerializeField] private Shader ringShader;
    [Tooltip("Sorting layer for the ring quad — above walls, faces, and units, so both the " +
             "band and the O wash cover everything the eye sees. WorldUI stays clear above it.")]
    [SerializeField] private string sortingLayerName = "AdjacentHighlight";
    [Tooltip("Order on that layer (the retired claimable tilemap sits at 40).")]
    [SerializeField] private int sortingOrder = 45;

    [Header("Ring Shape (cells)")]
    [Tooltip("Falloff on the claimed side of the boundary.")]
    [SerializeField, Min(0.05f)] private float innerFalloff = 0.35f;
    [Tooltip("Falloff bleeding outward into the fog — the ethereal tail.")]
    [SerializeField, Min(0.05f)] private float outerFalloff = 1.6f;
    [Tooltip("Waver amplitude of the isoline.")]
    [SerializeField, Range(0f, 0.6f)] private float waverAmp = 0.18f;
    [Tooltip("Cell size of the two noise octaves.")]
    [SerializeField] private Vector2 waverScales = new Vector2(9f, 3.2f);
    [Tooltip("Scroll speed of the two noise octaves.")]
    [SerializeField] private Vector2 waverSpeeds = new Vector2(0.05f, 0.11f);

    [Header("Ring Look")]
    [SerializeField, Min(0f)] private float intensity = 1.15f;
    [SerializeField, Min(0f)] private float pulseSpeed = 1.4f;
    [SerializeField, Range(0f, 1f)] private float pulseAmp = 0.12f;

    [Header("Core Type Palette")]
    [Tooltip("Ring color per core type. None is the fallback (pre-selection gold).")]
    [SerializeField]
    private List<TypeColor> palette = new List<TypeColor>
    {
        new TypeColor { type = DungeonType.None,  color = new Color(0.784f, 0.565f, 0.165f, 1f) }, // gold
        new TypeColor { type = DungeonType.Fire,  color = new Color(0.910f, 0.353f, 0.165f, 1f) }, // ember
        new TypeColor { type = DungeonType.Water, color = new Color(0.165f, 0.659f, 0.784f, 1f) }, // deep cyan
        new TypeColor { type = DungeonType.Air,   color = new Color(0.722f, 0.769f, 0.816f, 1f) }, // storm-silver
        new TypeColor { type = DungeonType.Earth, color = new Color(0.690f, 0.478f, 0.212f, 1f) }, // amber-umber
        new TypeColor { type = DungeonType.Dark,  color = new Color(0.541f, 0.310f, 0.784f, 1f) }, // violet
        new TypeColor { type = DungeonType.Light, color = new Color(0.949f, 0.886f, 0.690f, 1f) }, // white-gold
    };

    [Header("Free-Growth Overlay")]
    [Tooltip("Fill strength of the reach overlay when visible.")]
    [SerializeField, Range(0f, 1f)] private float overlayStrength = 0.10f;
    [Tooltip("Fade in/out speed of the overlay (per second).")]
    [SerializeField, Min(0.5f)] private float overlayFadeSpeed = 6f;
    [Tooltip("How much of the wash claimed ground receives. 0 = hard exclusion (territory reads " +
             "as black cutouts in the reach field); 1 = uniform wash. Softens the claim-boundary contrast.")]
    [SerializeField, Range(0f, 1f)] private float overlayClaimedLevel = 0.45f;

    [Header("Field Encoding")]
    [Tooltip("Cells of signed distance encoded either side of the boundary.")]
    [SerializeField, Min(2f)] private float sdfRangeCells = 4f;
    [Tooltip("Texels of padding beyond the floor radius.")]
    [SerializeField, Min(0)] private int texturePadding = 2;

    [Serializable]
    public class TypeColor
    {
        public DungeonType type;
        public Color color;
    }

    // ── State ─────────────────────────────────────────────────────

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private InfluenceField field;

    private GameObject quadGO;
    private Material material;
    private Texture2D fieldTex;
    private Color32[] pixels;
    private float[] chamferIn;
    private float[] chamferOut;
    private bool[] claimedMask;
    private bool staticUniformsDirty;
    private Vector3Int texMin;
    private int texSize;

    private bool claimDirty = true;
    private bool fieldDirty = true;
    private bool subscribedInfluence;
    private bool subscribedField;
    private bool built;

    private DungeonType lastAppliedType = (DungeonType)(-1);
    private float overlayCurrent;
    private float reachNorm = 1f;

    // Overlay toggle is global across floors; the frame guard stops the same
    // press from flipping it once per floor instance.
    private static bool overlayToggled;
    private static int lastToggleFrame = -1;

    /// <summary>Ring color for the core's current type — DungeonShadow reads this
    /// for the void's core-hue whisper.</summary>
    public Color CurrentTypeColor
    {
        get
        {
            var core = DungeonCore.Instance;
            return ColorFor(core != null ? core.DungeonType : DungeonType.None);
        }
    }

    public Color ColorFor(DungeonType type)
    {
        for (int i = 0; i < palette.Count; i++)
            if (palette[i].type == type) return palette[i].color;
        for (int i = 0; i < palette.Count; i++)
            if (palette[i].type == DungeonType.None) return palette[i].color;
        return new Color(0.784f, 0.565f, 0.165f, 1f);
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[InfluenceRingRenderer] No FloorRoot in parents — disabling.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        claimDirty = true;
        fieldDirty = true;
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (quadGO != null) quadGO.SetActive(false);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (fieldTex != null) Destroy(fieldTex);
        if (quadGO != null) Destroy(quadGO);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        staticUniformsDirty = true;
    }
#endif

    private void LateUpdate()
    {
        ResolveAndSubscribe();
        if (floor == null || influence == null || field == null || floor.Terrain == null) return;

        if (!built) BuildQuadAndTexture();
        if (!built) return;

        if (quadGO != null && !quadGO.activeSelf) quadGO.SetActive(true);

        if (staticUniformsDirty && material != null)
        {
            ApplyStaticUniforms();
            lastAppliedType = (DungeonType)(-1);   // re-push the ring colour too
            staticUniformsDirty = false;
        }

        if (claimDirty || fieldDirty)
        {
            RebuildTexture();
            claimDirty = false;
            fieldDirty = false;
        }

        PollOverlayInput();
        UpdatePerFrameUniforms();
    }

    private void ResolveAndSubscribe()
    {
        if (floor == null) return;
        if (influence == null) influence = floor.TileInfluence;
        if (field == null) field = floor.InfluenceField;

        if (!subscribedInfluence && influence != null)
        {
            influence.OnClaimedTileCountChanged += MarkClaimDirty;
            subscribedInfluence = true;
        }
        if (!subscribedField && field != null)
        {
            field.OnFieldRecomputed += MarkFieldDirty;
            subscribedField = true;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedInfluence && influence != null)
            influence.OnClaimedTileCountChanged -= MarkClaimDirty;
        subscribedInfluence = false;

        if (subscribedField && field != null)
            field.OnFieldRecomputed -= MarkFieldDirty;
        subscribedField = false;
    }

    private void MarkClaimDirty(int _) => claimDirty = true;
    private void MarkFieldDirty() => fieldDirty = true;

    // ── Quad + texture construction ───────────────────────────────

    private void BuildQuadAndTexture()
    {
        var terrain = floor.Terrain;
        int radius = terrain.CurrentRadius;
        if (radius <= 0) return;

        int half = radius + texturePadding;
        texSize = half * 2 + 1;
        texMin = terrain.CoreCell - new Vector3Int(half, half, 0);

        fieldTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        pixels = new Color32[texSize * texSize];
        chamferIn = new float[texSize * texSize];
        chamferOut = new float[texSize * texSize];
        claimedMask = new bool[texSize * texSize];

        Shader shader = ringShader != null ? ringShader : Shader.Find("DCR/InfluenceRing");
        if (shader == null)
        {
            Debug.LogError("[InfluenceRingRenderer] DCR/InfluenceRing shader not found — assign the Ring Shader slot.");
            return;
        }
        material = new Material(shader);
        material.SetTexture("_FieldTex", fieldTex);

        // World rect from cell corners so any grid cell size is handled.
        Vector3 minCenter = influence.CellToWorld(texMin);
        Vector3 maxCenter = influence.CellToWorld(texMin + new Vector3Int(texSize - 1, texSize - 1, 0));
        Vector3 cellStep = (maxCenter - minCenter) / (texSize - 1);
        Vector3 lo = minCenter - cellStep * 0.5f;
        Vector3 hi = maxCenter + cellStep * 0.5f;
        Vector3 center = (lo + hi) * 0.5f;
        Vector3 size = hi - lo;

        quadGO = new GameObject("InfluenceRingQuad");
        quadGO.transform.SetParent(transform, false);
        quadGO.transform.position = new Vector3(center.x, center.y, 0f);

        var mf = quadGO.AddComponent<MeshFilter>();
        var mesh = new Mesh { name = "InfluenceRingQuad" };
        float hx = size.x * 0.5f, hy = size.y * 0.5f;
        mesh.vertices = new[]
        {
            new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f),
            new Vector3(-hx,  hy, 0f), new Vector3(hx,  hy, 0f),
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mf.sharedMesh = mesh;

        var mr = quadGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        reachNorm = field.ReachAtLevel(LevelTierUtil.MaxFlatLevel) * 1.1f;
        ApplyStaticUniforms();
        built = true;
    }

    private void ApplyStaticUniforms()
    {
        // Convert cell-space knobs to encoded SDF units (0.5 = boundary,
        // one cell = 1 / (2 * sdfRangeCells)) and to texture-uv noise space.
        float cellToEncoded = 1f / (2f * sdfRangeCells);
        material.SetFloat("_InnerFalloff", innerFalloff * cellToEncoded);
        material.SetFloat("_OuterFalloff", outerFalloff * cellToEncoded);
        material.SetFloat("_WaverAmp", waverAmp * cellToEncoded);
        material.SetFloat("_Noise1Scale", texSize / Mathf.Max(0.5f, waverScales.x));
        material.SetFloat("_Noise2Scale", texSize / Mathf.Max(0.5f, waverScales.y));
        material.SetFloat("_Noise1Speed", waverSpeeds.x);
        material.SetFloat("_Noise2Speed", waverSpeeds.y);
        material.SetFloat("_Intensity", intensity);
        material.SetFloat("_PulseSpeed", pulseSpeed);
        material.SetFloat("_PulseAmp", pulseAmp);
        material.SetFloat("_ReachEdge", 0.75f / reachNorm);
        material.SetFloat("_OverlayClaimedLevel", overlayClaimedLevel);
    }



    // ── Texture rebuild ───────────────────────────────────────────

    private void RebuildTexture()
    {
        int total = texSize * texSize;
        float far = sdfRangeCells + 2f;

        // Base fill: claimed mask, both chamfer seeds, and G = normalized
        // free-growth cost (255 = unreachable) straight from the field.
        for (int y = 0; y < texSize; y++)
        {
            int row = y * texSize;
            for (int x = 0; x < texSize; x++)
            {
                var cell = new Vector3Int(texMin.x + x, texMin.y + y, 0);
                bool claimed = influence.IsTileClaimed(cell);
                int i = row + x;
                claimedMask[i] = claimed;
                chamferIn[i] = claimed ? far : 0f;    // distance to unclaimed
                chamferOut[i] = claimed ? 0f : far;   // distance to claimed
                byte g = 255;
                if (field.TryGetCost(cell, out float cost))
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.Clamp01(cost / reachNorm)), 0, 254);
                pixels[i] = new Color32(0, g, 0, 255);
            }
        }

        // Two-pass chamfer distance transforms (orthogonal 1.0, diagonal 1.4):
        // quasi-Euclidean, so the boundary isoline curves instead of the
        // staircase the old 4-connected BFS (Manhattan distance) produced.
        Chamfer(chamferIn);
        Chamfer(chamferOut);

        for (int i = 0; i < total; i++)
        {
            // Boundary calibration matches the old encode: the first claimed
            // cell sits at +0.5, the first unclaimed at -0.5, so the encoded
            // 0.5 isoline lands exactly on the shared cell edge.
            float signedCells = claimedMask[i] ? chamferIn[i] - 0.5f : -(chamferOut[i] - 0.5f);
            float enc = Mathf.Clamp01(0.5f + signedCells / (2f * sdfRangeCells));
            Color32 p = pixels[i];
            p.r = (byte)Mathf.Clamp(Mathf.RoundToInt(enc * 255f), 0, 255);
            pixels[i] = p;
        }

        fieldTex.SetPixels32(pixels);
        fieldTex.Apply(false);
    }

    /// <summary>In-place two-pass chamfer distance transform over the texture
    /// grid. Seeds are 0; everything else relaxes toward the nearest seed with
    /// orthogonal steps at 1.0 and diagonal steps at 1.4.</summary>
    private void Chamfer(float[] d)
    {
        const float Orth = 1f;
        const float Diag = 1.4f;
        int s = texSize;

        // Forward: relax from W, N, NW, NE.
        for (int y = 0; y < s; y++)
        {
            int row = y * s;
            for (int x = 0; x < s; x++)
            {
                int i = row + x;
                float v = d[i];
                if (x > 0 && d[i - 1] + Orth < v) v = d[i - 1] + Orth;
                if (y > 0)
                {
                    int up = i - s;
                    if (d[up] + Orth < v) v = d[up] + Orth;
                    if (x > 0 && d[up - 1] + Diag < v) v = d[up - 1] + Diag;
                    if (x < s - 1 && d[up + 1] + Diag < v) v = d[up + 1] + Diag;
                }
                d[i] = v;
            }
        }

        // Backward: relax from E, S, SE, SW.
        for (int y = s - 1; y >= 0; y--)
        {
            int row = y * s;
            for (int x = s - 1; x >= 0; x--)
            {
                int i = row + x;
                float v = d[i];
                if (x < s - 1 && d[i + 1] + Orth < v) v = d[i + 1] + Orth;
                if (y < s - 1)
                {
                    int dn = i + s;
                    if (d[dn] + Orth < v) v = d[dn] + Orth;
                    if (x < s - 1 && d[dn + 1] + Diag < v) v = d[dn + 1] + Diag;
                    if (x > 0 && d[dn - 1] + Diag < v) v = d[dn - 1] + Diag;
                }
                d[i] = v;
            }
        }
    }

    // ── Per-frame ─────────────────────────────────────────────────

    private void PollOverlayInput()
    {
        if (PauseController.IsGamePaused) return;
        if (Keybinds.IsTextInputActive()) return;
        if (Keybinds.WasPressed(GameAction.ToggleInfluenceOverlay) && lastToggleFrame != Time.frameCount)
        {
            overlayToggled = !overlayToggled;
            lastToggleFrame = Time.frameCount;
        }
    }

    private void UpdatePerFrameUniforms()
    {
        var core = DungeonCore.Instance;
        DungeonType type = core != null ? core.DungeonType : DungeonType.None;
        if (type != lastAppliedType)
        {
            material.SetColor("_RingColor", ColorFor(type));
            lastAppliedType = type;
        }

        material.SetFloat("_EffReach", Mathf.Clamp01(field.EffectiveReach / reachNorm));

        bool pushActive = DungeonBuildController.Instance != null
                          && DungeonBuildController.Instance.CurrentMode == BuildMode.Push;
        float target = (overlayToggled || pushActive) ? overlayStrength : 0f;
        overlayCurrent = Mathf.MoveTowards(overlayCurrent, target, overlayFadeSpeed * overlayStrength * Time.deltaTime);
        material.SetFloat("_OverlayStrength", overlayCurrent);
    }
}