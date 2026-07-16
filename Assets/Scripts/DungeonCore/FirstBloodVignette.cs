using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The First Blood vignette: the moment the player digs out the entrance, a
/// wild rat is chased into the tunnel by a hunter, an arrow takes it just
/// inside, and the core absorbs the corpse a breath before the hunter reaches
/// it. He finds nothing, says so, and leaves - and the surface has its reason
/// to come looking.
///
/// Staged entirely with puppets: plain SpriteRenderer objects the coroutine
/// moves along the entrance tunnel. No live entities, no AI - deterministic
/// choreography. Geometry comes from the seeded EntranceCaveData (mouthCell is
/// the surface end, spawnCell the interior), so the scene stages itself
/// wherever the tunnel was rolled.
///
/// The camera glides to the tunnel on the existing follow machinery and a
/// gentle zoom; manual pan breaks the hold at any moment (native behaviour),
/// and the vignette plays on regardless. The one mechanical payload is
/// BestiaryState.Discover("Cave Rat"), fired at the absorb beat.
///
/// TutorialDirector calls Play() from the breach step; if this component is
/// absent or the seeded cave is missing, the director's fallback grants the
/// rat directly.
/// </summary>
public class FirstBloodVignette : MonoBehaviour
{
    public static FirstBloodVignette Instance { get; private set; }

    [Header("Sprites (assign; placeholders fine until the art pass)")]
    [SerializeField] private Sprite ratSprite;
    [SerializeField] private Sprite hunterSprite;
    [SerializeField] private Sprite arrowSprite;
    [Tooltip("Sorting layer the puppets render on - matches live entities.")]
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;

    [Header("Choreography")]
    [SerializeField] private float ratRunSpeed = 5f;
    [SerializeField] private float arrowSpeed = 16f;
    [SerializeField] private float hunterWalkSpeed = 3.2f;
    [Tooltip("Seconds the corpse lies still before the dark takes it.")]
    [SerializeField] private float corpsePause = 0.9f;
    [SerializeField] private float absorbSeconds = 1.2f;

    [Header("Camera")]
    [Tooltip("Glide the view to the tunnel for the scene. Manual pan always breaks the hold.")]
    [SerializeField] private bool moveCamera = true;
    [Tooltip("Orthographic size while the vignette plays; prior zoom restores after.")]
    [SerializeField] private float cameraZoom = 6f;

    [Header("SFX keys (null-safe; silent until clips exist)")]
    [SerializeField] private string arrowSfx = "ArrowLoose";
    [SerializeField] private string deathSfx = "RatDeath";
    [SerializeField] private string absorbSfx = "CoreAbsorb";

    [Header("The hunter's line")]
    [TextArea]
    [SerializeField] private string hunterLine = "Gone? It dropped right here...";
    [SerializeField] private Color hunterBarkColour = new Color(0.85f, 0.80f, 0.70f);

    private bool playing;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Run the vignette. Calls onComplete when the hunter has gone.
    /// Returns false (and does not call onComplete) if staging is impossible -
    /// the caller keeps its mechanical fallback.</summary>
    public bool Play(Action onComplete)
    {
        if (playing) return false;

        var floor0 = FloorManager.Instance?.GetFloor(0);
        var cave = floor0 != null && floor0.FeatureGenerator != null
            ? floor0.FeatureGenerator.EntranceCave
            : null;
        if (floor0?.TileInfluence == null || cave == null || !cave.hasSpawnCell) return false;

        StartCoroutine(Run(floor0, cave, onComplete));
        return true;
    }

    private IEnumerator Run(FloorRoot floor0, EntranceCaveData cave, Action onComplete)
    {
        playing = true;

        // -- Stage geometry, derived from the seeded tunnel -----------------
        Vector3 mouth = floor0.TileInfluence.CellToWorld(cave.mouthCell.ToVector3Int());
        Vector3 inner = floor0.TileInfluence.CellToWorld(cave.spawnCell.ToVector3Int());
        Vector3 outward = (mouth - inner).normalized;

        Vector3 ratStart = mouth + outward * 2.5f;      // beyond the mouth, off the carved floor
        Vector3 killPoint = inner;                      // a few steps down the tunnel
        Vector3 hunterStop = Vector3.Lerp(mouth, inner, 0.45f);
        Vector3 arrowStart = mouth + outward * 1.5f;    // loosed from outside, before he is seen
        bool facingLeft = outward.x > 0f;               // actors move opposite the outward axis

        // -- Camera: glide to the tunnel; player pan breaks the hold --------
        var cam = DungeonCameraController.Instance;
        GameObject anchor = null;
        float priorZoom = 0f;
        if (moveCamera && cam != null)
        {
            anchor = new GameObject("VignetteCameraAnchor");
            anchor.transform.position = Vector3.Lerp(mouth, inner, 0.5f);
            priorZoom = cam.TargetZoom;
            cam.SetFollowTarget(anchor.transform);
            cam.NudgeZoom(cameraZoom);
            yield return new WaitForSeconds(1.1f);      // let the glide land
        }

        // -- The rat, running for its life -----------------------------------
        SpriteRenderer rat = MakePuppet("Vignette_Rat", ratSprite, ratStart, facingLeft);
        yield return MoveTo(rat.transform, killPoint, ratRunSpeed, hop: true);

        // -- The arrow, loosed from beyond the mouth --------------------------
        SoundEffectManager.Play(arrowSfx);
        SpriteRenderer arrow = MakePuppet("Vignette_Arrow", arrowSprite, arrowStart, false);
        Vector3 flight = killPoint - arrowStart;
        arrow.transform.rotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(flight.y, flight.x) * Mathf.Rad2Deg);
        yield return MoveTo(arrow.transform, killPoint, arrowSpeed, hop: false);
        Destroy(arrow.gameObject);

        // -- The kill ----------------------------------------------------------
        SoundEffectManager.Play(deathSfx);
        rat.transform.rotation = Quaternion.Euler(0f, 0f, facingLeft ? 90f : -90f);
        yield return new WaitForSeconds(corpsePause);

        // -- The dark takes it -------------------------------------------------
        SoundEffectManager.Play(absorbSfx);
        Color tint = DungeonCore.Instance != null
            ? DungeonCore.ColorFor(DungeonCore.Instance.DungeonType)
            : Color.white;
        yield return Absorb(rat, tint);

        // The one mechanical line: the rat is learned the instant it is taken.
        BestiaryState.Instance?.Discover("Cave Rat");
        yield return new WaitForSeconds(0.6f);

        // -- The hunter, a breath too late --------------------------------------
        SpriteRenderer hunter = MakePuppet("Vignette_Hunter", hunterSprite, ratStart, facingLeft);
        yield return MoveTo(hunter.transform, hunterStop, hunterWalkSpeed, hop: false);
        yield return new WaitForSeconds(0.8f);

        BarkSpawner.Spawn(hunterStop + Vector3.up * 0.8f, hunterLine, hunterBarkColour);
        yield return new WaitForSeconds(2.2f);

        FlipPuppet(hunter, !facingLeft);
        yield return MoveTo(hunter.transform, ratStart + outward * 1.0f, hunterWalkSpeed, hop: false);
        Destroy(hunter.gameObject);

        // -- Release the camera; the view stays on the entrance ------------------
        if (anchor != null && cam != null)
        {
            cam.ClearFollowTargetIf(anchor.transform);
            cam.NudgeZoom(priorZoom);
            Destroy(anchor);
        }

        playing = false;
        onComplete?.Invoke();
    }

    // -- Puppet helpers ---------------------------------------------------------

    private SpriteRenderer MakePuppet(string name, Sprite sprite, Vector3 at, bool faceLeft)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        sr.flipX = faceLeft;
        return sr;
    }

    private static void FlipPuppet(SpriteRenderer sr, bool faceLeft) => sr.flipX = faceLeft;

    private static IEnumerator MoveTo(Transform t, Vector3 goal, float speed, bool hop)
    {
        Vector3 basePos = t.position;
        float total = Vector3.Distance(basePos, goal);
        float travelled = 0f;
        while (travelled < total)
        {
            travelled += speed * Time.deltaTime;
            float k = Mathf.Clamp01(travelled / total);
            Vector3 p = Vector3.Lerp(basePos, goal, k);
            if (hop) p.y += Mathf.Abs(Mathf.Sin(k * total * 6f)) * 0.08f;   // a small scurrying bounce
            t.position = p;
            yield return null;
        }
        t.position = goal;
    }

    private IEnumerator Absorb(SpriteRenderer sr, Color coreTint)
    {
        Vector3 startScale = sr.transform.localScale;
        Vector3 startPos = sr.transform.position;
        Color startColour = sr.color;
        Color sink = Color.Lerp(coreTint, Color.black, 0.45f);

        float t = 0f;
        while (t < absorbSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / absorbSeconds);
            sr.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, k);
            sr.transform.position = startPos + Vector3.down * (0.3f * k);
            sr.color = Color.Lerp(startColour, sink, k);
            yield return null;
        }
        Destroy(sr.gameObject);
    }
}