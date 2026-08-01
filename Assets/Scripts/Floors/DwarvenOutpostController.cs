using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The dwarven outpost: the inhabited Buried Age site (canon 19).
///
/// The site itself is terrain, placed and persisted by the site builder with
/// SiteData.reservedForOutpost set. This controller is the part that is alive:
/// it watches for that site to be revealed, marks the Deep Holds encountered so
/// the faction panel stops hiding them, and stands a gatekeeper at the gate.
///
/// SCENE SETUP: one of these on a persistent manager GameObject alongside the
/// other singletons. No per-floor wiring -- it finds its own floor through
/// FloorManager, which is deliberate: FloorRoot's prefab already carries eleven
/// serialized references and a twelfth that only ever matters on one floor is a
/// wiring mistake waiting to happen. gatekeeperSprite may be left unassigned;
/// the outpost then establishes with no puppet rather than failing.
///
/// WHY THIS POLLS RATHER THAN LISTENING. Fresh discovery runs through
/// TerrainFeatureGenerator.RevealSite, but the LOAD path does not: it calls
/// UnfogSite directly for every saved id and never touches RevealSite. An event
/// on RevealSite would therefore fire for a player who discovers the outpost
/// this session and stay silent for one who saved and reloaded afterwards --
/// the gatekeeper would vanish on Continue. A one-second poll that stops dead
/// the moment the outpost is established cannot get that wrong, and costs one
/// dictionary walk per second until it does.
/// </summary>
public class DwarvenOutpostController : MonoBehaviour
{
    public static DwarvenOutpostController Instance { get; private set; }

    [Header("Gatekeeper")]
    [Tooltip("Optional. The dwarf who stands at the gate. Leave unassigned and " +
             "the outpost still establishes -- there is simply nobody drawn yet.")]
    [SerializeField] private Sprite gatekeeperSprite;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Discovery Poll")]
    [Tooltip("Seconds between checks for a revealed outpost. The poll stops for " +
             "good once the outpost is established.")]
    [SerializeField, Min(0.25f)] private float pollSeconds = 1f;

    private float nextPoll;
    private SpriteRenderer keeper;

    /// <summary>True once the outpost has been found and the Deep Holds are on
    /// the board. Part 2 reads this before opening the shop.</summary>
    public bool Established { get; private set; }

    /// <summary>Floor index the outpost stands on, or -1. Part 2 needs it to
    /// decide whether the player is standing in front of the counter.</summary>
    public int OutpostFloorIndex { get; private set; } = -1;

    /// <summary>World position of the gatekeeper, valid once established.</summary>
    public Vector3 GatekeeperPosition =>
        keeper != null ? keeper.transform.position : Vector3.zero;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!Established)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + pollSeconds;
            TryEstablish();
            return;
        }
        HandleClick();
    }

    // -- Discovery -----------------------------------------------------------

    private void TryEstablish()
    {
        var floors = FloorManager.Instance;
        if (floors == null) return;

        foreach (var floor in floors.AllFloors)
        {
            if (floor == null) continue;
            var features = floor.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;

            var site = features.GetOutpostSite();
            if (site == null) continue;
            if (!features.IsSiteRevealed(site.id)) continue;

            Establish(floor, site);
            return;
        }
    }

    private void Establish(FloorRoot floor, SiteData site)
    {
        Established = true;
        OutpostFloorIndex = floor.FloorIndex;

        Vector3 at = WorldCentreOf(floor, site);

        // The faction goes on the board here and nowhere else. Until this fires
        // the Deep Holds are not listed at all -- see FactionPanel.BuildEntries.
        FactionIntel.NotifyEncounter(FactionId.Dwarves);

        if (gatekeeperSprite != null)
        {
            var go = new GameObject("DwarvenGatekeeper");
            go.transform.position = at;
            keeper = go.AddComponent<SpriteRenderer>();
            keeper.sprite = gatekeeperSprite;
            keeper.sortingLayerName = sortingLayerName;
            keeper.sortingOrder = sortingOrder;
        }

        AlertsLog.Instance?.AddAlert(
            "The road runs through a hold that is still lit.",
            at, floor.FloorIndex, AlertCategory.Discovery);

        var wisp = WispCompanion.Instance;
        if (wisp != null)
        {
            wisp.Speak("outpost_first");
            wisp.Excite(0.7f);
        }
    }

    /// <summary>Centroid of the site's carved cells, in this floor's world space.
    /// The stored anchor is the plan's centre BEFORE the carriageway was
    /// subtracted, so on an outpost the road runs straight through it -- standing
    /// a gatekeeper there would put him in the middle of the road.</summary>
    private static Vector3 WorldCentreOf(FloorRoot floor, SiteData site)
    {
        // TileInfluence is the floor's cell-to-world service (DungeonTerrain has
        // no such method); its result already carries the floor's Y offset, which
        // is what every other cross-floor spawn in the project relies on.
        var influence = floor.TileInfluence;
        if (site.cells == null || site.cells.Count == 0 || influence == null)
            return new Vector3(0f, floor.WorldOriginY, 0f);

        long sx = 0, sy = 0;
        foreach (var sv in site.cells)
        {
            var c = sv.ToVector3Int();
            sx += c.x;
            sy += c.y;
        }
        var mid = new Vector3Int((int)(sx / site.cells.Count),
                                 (int)(sy / site.cells.Count), 0);

        // Snap to the nearest carved cell: the centroid of a ring-shaped ward can
        // land on masonry, and a gatekeeper inside a wall is invisible.
        Vector3Int best = site.cells[0].ToVector3Int();
        long bestSq = long.MaxValue;
        foreach (var sv in site.cells)
        {
            var c = sv.ToVector3Int();
            long dx = c.x - mid.x, dy = c.y - mid.y;
            long d = dx * dx + dy * dy;
            if (d < bestSq) { bestSq = d; best = c; }
        }
        return influence.CellToWorld(best);
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (keeper == null) return;
        if (PauseController.IsGamePaused) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        world.z = keeper.transform.position.z;
        if (Vector3.Distance(world, keeper.transform.position) > clickRadius) return;

        OnGatekeeperClicked();
    }

    /// <summary>PART 1 speaks; PART 2 replaces this body with the shop. Kept as
    /// its own method so the seam is one line rather than a hunt through Update.</summary>
    private void OnGatekeeperClicked()
    {
        WispCompanion.Instance?.Speak("outpost_greeting");
    }
}
