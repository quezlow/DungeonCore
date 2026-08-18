using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A static point light: one additive sprite through DCR/AdditiveSprite, sitting
/// between the shadow tilemap and the fog on the Shadow sorting layer.
///
/// WHY A SPRITE AND NOT AN ENTRY IN THE LIGHT MAP. DungeonShadow's light is one
/// flat colour per cell, so anything strong enough to see draws as concentric
/// 32px blocks -- which is exactly why the CURSOR light already carries its own
/// additive sprite instead of relying on its per-cell pass. Nothing reads
/// DungeonShadow.baseLight for gameplay (only the rim-facade readout in
/// Commands), so entering the map buys no mechanics and would add a disc walk to
/// a rebuild that fires on every claim. If light ever needs to matter
/// mechanically, this component is the registry that hook attaches to.
///
/// THE BLEND DOES THE BALANCING. DCR/AdditiveSprite is a SCREEN blend --
/// contribution = src * (1 - dst) -- so the same lamp blazes over the void and
/// all but vanishes over claimed floor that is already at 0.90 light. Light in
/// DCR is darkness removed. That is why intensity is tuned once here rather than
/// per-context, and why the ladder is about PEAK, not about how bright any given
/// cell ends up. Read the live ladder with Commands / Log Point Lights; it
/// prints DungeonShadow's own fields beside these, so no number here can go
/// stale against the thing it is being balanced against.
///
/// KEEP THE RADIUS SMALL. The disc has no occlusion: it brightens the wall cap
/// beside it and the opaque void behind that, so a radius wider than the rock
/// mass is thick will glow through to the far side. Rock beside a torch being
/// lit is correct; rock on the OTHER side of a wall is not. At radius 4 that
/// needs a wall under four cells thick. Per-light baked shadow masks are the
/// upgrade if this ever reads wrong; do not reach for them before it does.
/// </summary>
[DisallowMultipleComponent]
public class DungeonPointLight : MonoBehaviour
{
    /// <summary>What put this light here. Diagnostics only -- nothing branches
    /// on it, and it is serialised into no save (lights are rebuilt by whatever
    /// spawns them), so this may be reordered freely, unlike a save-bound
    /// enum.</summary>
    public enum LightSource
    {
        Placed = 0,
        SiteTorch = 1,
        RoadLamp = 2,
        DwarvenHold = 3,
    }

    [Header("Light")]
    [Tooltip("Radius in cells. One cell is one world unit. Keep it under the " +
             "thickness of the rock beside it -- the disc has no occlusion.")]
    [SerializeField, Min(0f)] private float radiusCells = 4f;

    [Tooltip("Peak brightness at the centre. The shader is a screen blend, so " +
             "this is how much darkness the light can remove, not a final value.")]
    [SerializeField, Range(0f, 1f)] private float intensity = 0.20f;

    [Tooltip("Light colour. Warm amber for flame, cold blue-white for holy.")]
    [SerializeField] private Color colour = new Color(1f, 0.78f, 0.45f, 1f);

    [Tooltip("What placed this light. Diagnostics only.")]
    [SerializeField] private LightSource source = LightSource.Placed;

    [Tooltip("Unlit lights keep their component and their registration but draw " +
             "nothing -- the dormant-torch case, which lights on a claim.")]
    [SerializeField] private bool lit = true;

    [Header("Flicker")]
    [Tooltip("Peak-to-peak wobble as a fraction of intensity. 0 is a steady lamp.")]
    [SerializeField, Range(0f, 0.5f)] private float flickerAmount = 0.08f;

    [Tooltip("Base flicker rate. A second wobble runs at an incommensurate " +
             "multiple of this so a lamp does not read as a metronome.")]
    [SerializeField, Min(0f)] private float flickerHz = 1.7f;

    [Header("Shader")]
    [Tooltip("DCR/AdditiveSprite. Falls back to Shader.Find if unset, but ASSIGN " +
             "IT on the prefab: Shader.Find alone can leave the shader stripped " +
             "from a build, which is why the cursor light carries the same slot.")]
    [SerializeField] private Shader lightShader;

    // -- Registry -----------------------------------------------------
    // A plain static list, for the diagnostic and for nothing else. Entries come
    // and go with OnEnable/OnDisable rather than Awake/OnDestroy, so a
    // deactivated floor's lights leave the count as well as the screen.
    private static readonly List<DungeonPointLight> live = new List<DungeonPointLight>();
    public static IReadOnlyList<DungeonPointLight> All => live;

    // One texture, one sprite and one material for every light in the game. The
    // colour rides the SpriteRenderer's vertex colour, so a shared material
    // costs nothing in flexibility and lets the lights batch.
    private static Sprite sharedSprite;
    private static Material sharedMaterial;

    private SpriteRenderer render;
    private float phase;

    public float RadiusCells => radiusCells;
    public float Intensity => intensity;
    public Color Colour => colour;
    public LightSource Source => source;
    public bool IsLit => lit;

    /// <summary>Light or snuff this lamp. The dormant torches of a Buried Age
    /// site will call this when their cell is claimed, and a road lamp when the
    /// player takes the stretch. Cheap enough to call every poll.</summary>
    public void SetLit(bool value)
    {
        if (lit == value) return;
        lit = value;
        Apply();
    }

    /// <summary>Retune at runtime -- the holy blue, the dwarven gold. Callers
    /// that spawn one shared prefab and then differentiate it use this rather
    /// than carrying a prefab per colour.</summary>
    public void Configure(float radius, float peak, Color tint)
    {
        radiusCells = Mathf.Max(0f, radius);
        intensity = Mathf.Clamp01(peak);
        colour = tint;
        Apply();
    }

    private void OnEnable()
    {
        live.Add(this);
        EnsureRenderer();

        // Phase from POSITION, not from Time or Random: a row of road lamps
        // built in one frame would otherwise pulse in unison, which reads as a
        // single flashing object rather than as separate flames. Position is
        // also stable across a reload, so a lamp does not jump phase on load.
        Vector3 p = transform.position;
        int h = unchecked(Mathf.RoundToInt(p.x * 7f) * 73856093
                        ^ Mathf.RoundToInt(p.y * 7f) * 19349663);
        phase = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;

        Apply();
    }

    private void OnDisable()
    {
        live.Remove(this);
        if (render != null) render.enabled = false;
    }

    private void Update()
    {
        if (!lit || flickerAmount <= 0f || flickerHz <= 0f || render == null) return;

        // Gate on visibility. The wobble is trivial per light, but a deep floor
        // may carry hundreds once roads and sites light up, and a light off
        // screen has nothing to wobble for. isVisible is a cheap native read.
        if (!render.isVisible) return;

        float t = Time.time * flickerHz + phase;
        // Two waves at a deliberately awkward ratio: one alone is a metronome.
        float w = Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.37f + 1.1f) * 0.4f;
        SetRendererColour(intensity * (1f + flickerAmount * w));
    }

    private void Apply()
    {
        EnsureRenderer();
        if (render == null) return;

        if (!lit) { render.enabled = false; return; }

        // The RADIUS is the child's scale, never the root's. Writing the root
        // here would stomp whatever scale the prefab was authored at -- and this
        // component shares its GameObject with FurniturePiece on the brazier.
        render.transform.localScale = Vector3.one * (radiusCells * 2f);
        SetRendererColour(intensity);
        render.enabled = radiusCells > 0f && intensity > 0f;
    }

    private void SetRendererColour(float peak)
    {
        Color c = colour;
        c.a = Mathf.Clamp01(peak);   // the shader multiplies rgb by alpha
        render.color = c;
    }

    private void EnsureRenderer()
    {
        if (render != null) return;

        Shader shader = lightShader != null ? lightShader : Shader.Find("DCR/AdditiveSprite");
        if (shader == null)
        {
            Debug.LogWarning("[DungeonPointLight] DCR/AdditiveSprite not found and no "
                + "shader assigned on '" + name + "' -- this light will not draw.");
            return;
        }

        if (sharedSprite == null) sharedSprite = BuildDiscSprite();
        if (sharedMaterial == null) sharedMaterial = new Material(shader);

        var go = new GameObject("PointLightSprite");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        render = go.AddComponent<SpriteRenderer>();
        render.sprite = sharedSprite;
        render.sharedMaterial = sharedMaterial;

        // Appendix B: Shadow sits above WalkBehind so darkness covers walls and
        // entities alike. Within it, the void fill paints at order 0 and the fog
        // at order 10. Static lights take 4 and the cursor light keeps 5, so the
        // carried light composites LAST -- a screen blend is not quite
        // order-independent, and the cursor should win where they overlap.
        render.sortingLayerName = "Shadow";
        render.sortingOrder = 4;
        render.enabled = false;

        // The child inherits the root's scale, so a prefab authored at 0.5
        // silently halves every radius on it -- a lamp that looks weak for a
        // reason nothing on the component can explain. Say so once, here,
        // rather than leaving it to be found by eye.
        Vector3 s = transform.localScale;
        if (Mathf.Abs(s.x - 1f) > 0.01f || Mathf.Abs(s.y - 1f) > 0.01f)
            Debug.LogWarning("[DungeonPointLight] '" + name + "' root is scaled ("
                + s.x.ToString("0.##") + ", " + s.y.ToString("0.##")
                + "); the light radius is scaled with it. Keep light-bearing prefab "
                + "roots at scale 1 and scale the art child instead.");
    }

    /// <summary>The radial taper, built once. Deliberately the same falloff the
    /// cursor light uses -- smoothstep with a softened shoulder -- so a placed
    /// lamp and the carried light read as the same kind of thing at different
    /// strengths rather than as two different effects.</summary>
    private static Sprite BuildDiscSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        Vector2 centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), centre) / maxR;
                float u = Mathf.Clamp01(1f - r);
                float a = u * u * (3f - 2f * u) * u;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size),
                             new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
    }
}
