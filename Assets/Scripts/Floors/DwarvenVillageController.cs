using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The dwarven village: the hold the Deep Holds are named for, one floor below
/// the gatehouse (canon 19, part 3).
///
/// The site itself is terrain, placed and persisted by the site builder with
/// SiteData.reservedForVillage set. This controller is the part that is alive:
/// it watches for that site to be revealed, rolls the settlement's name from a
/// roster, marks the Deep Holds encountered (idempotent -- a player can reach
/// this before ever unfogging the gatehouse), and stands a handful of STATIC
/// villagers in the lanes. No vendor: they trade at the gate, they live here.
///
/// STATIC ON PURPOSE. Villagers are bare SpriteRenderers, the gatekeeper
/// pattern -- no pathfinding, no combat entity, no adventurer or monster
/// interaction. The Living Holds arc replaces them with walkers; villagerCount
/// below is the knob that grows when it does.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenOutpostController. No per-floor wiring -- it finds its floor through
/// FloorManager. villagerSprite may stay unassigned; the village then
/// establishes with nobody drawn yet, exactly as the gatekeeper does.
///
/// WHY THIS POLLS RATHER THAN LISTENING: the same reason the outpost
/// controller does. Fresh discovery runs through RevealSite, but the LOAD path
/// calls UnfogSite directly and never touches RevealSite, so an event would
/// fire for a player who discovers the village this session and stay silent
/// for one who reloaded afterwards. A one-second poll that stops dead the
/// moment the village is established cannot get that wrong. The discovery
/// alert therefore re-fires once per session after a reload, exactly as the
/// outpost's does -- recorded, accepted behaviour.
/// </summary>
public class DwarvenVillageController : MonoBehaviour
{
    public static DwarvenVillageController Instance { get; private set; }

    [Header("Villagers")]
    [Tooltip("Optional. The dwarves who stand in the lanes. Leave unassigned " +
             "and the village still establishes -- nobody is drawn yet.")]
    [SerializeField] private Sprite villagerSprite;
    [Tooltip("How many static villagers to stand up. Four for now; the Living " +
             "Holds arc raises this when they learn to walk.")]
    [SerializeField, Min(0)] private int villagerCount = 4;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Names")]
    [Tooltip("The settlement's name is rolled from this roster, seeded from the " +
             "floor seed and the site id -- deterministic, so it re-derives " +
             "identically on every load and needs no save field.")]
    [SerializeField] private List<string> villageNames = new List<string>
    {
        "The Hearth of the Deep",
        "The Last Hearth",
        "Hearthdeep",
        "The Undervault",
        "Emberhold",
        "Cinderhold",
        "Delvehold",
        "Gravenhold",
    };

    [Header("Discovery Poll")]
    [Tooltip("Seconds between checks for a revealed village. The poll stops " +
             "for good once the village is established.")]
    [SerializeField, Min(0.25f)] private float pollSeconds = 1f;

    private float nextPoll;
    private readonly List<SpriteRenderer> villagers = new List<SpriteRenderer>();

    /// <summary>True once the village has been found.</summary>
    public bool Established { get; private set; }

    /// <summary>Floor index the village stands on, or -1.</summary>
    public int VillageFloorIndex { get; private set; } = -1;

    /// <summary>The rolled settlement name, valid once established.</summary>
    public string VillageName { get; private set; } = "";

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

    private void TryEstablish()
    {
        var floors = FloorManager.Instance;
        if (floors == null) return;

        foreach (var floor in floors.AllFloors)
        {
            if (floor == null) continue;
            var features = floor.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;

            var site = features.GetVillageSite();
            if (site == null) continue;
            if (!features.IsSiteRevealed(site.id)) continue;

            Establish(floor, site);
            return;
        }
    }

    private void Establish(FloorRoot floor, SiteData site)
    {
        Established = true;
        VillageFloorIndex = floor.FloorIndex;

        // Deterministic per run: floor seed times a small prime plus the site
        // id, the same recipe WildMonsterController uses per chamber. The name
        // and the villagers' places re-derive identically on every load, so
        // nothing here needs a save field.
        int worldSeed = DungeonSaveController.Instance != null
            ? DungeonSaveController.Instance.WorldSeed : 0;
        var rng = new System.Random(unchecked(
            FloorManager.DeriveFloorSeed(worldSeed, floor.FloorIndex) * 31 + site.id));

        VillageName = villageNames != null && villageNames.Count > 0
            ? villageNames[rng.Next(villageNames.Count)]
            : "The Hearth of the Deep";

        // Idempotent, and deliberately fired here as well as at the gatehouse:
        // stairs are player-placed, so a run can genuinely reach this floor and
        // walk into the village before the gatehouse site was ever unfogged.
        FactionIntel.NotifyEncounter(FactionId.Dwarves);

        PlaceVillagers(floor, site, rng);

        Vector3 at = villagers.Count > 0
            ? villagers[0].transform.position
            : new Vector3(0f, floor.WorldOriginY, 0f);

        AlertsLog.Instance?.AddAlert(
            "Hearthsmoke in the deep - " + VillageName + " still stands.",
            at, floor.FloorIndex, AlertCategory.Discovery);

        var wisp = WispCompanion.Instance;
        if (wisp != null)
        {
            wisp.Speak("village_first");
            wisp.Excite(0.7f);
        }
    }

    /// <summary>
    /// Stands the villagers on interior lane cells. A candidate cell must have
    /// its two north neighbours carved too -- the builder's own walkable rule
    /// -- which keeps everyone clear of the wall drape AND off the carriageway,
    /// because road cells were subtracted from the site and a cell just south
    /// of the road therefore fails the rule.
    /// </summary>
    private void PlaceVillagers(FloorRoot floor, SiteData site, System.Random rng)
    {
        var influence = floor.TileInfluence;
        if (villagerSprite == null || influence == null || villagerCount <= 0) return;
        if (site.cells == null || site.cells.Count == 0) return;

        var cells = new HashSet<Vector3Int>();
        foreach (var sv in site.cells) cells.Add(sv.ToVector3Int());

        var candidates = new List<Vector3Int>();
        foreach (var c in cells)
            if (cells.Contains(new Vector3Int(c.x, c.y + 1, 0))
                && cells.Contains(new Vector3Int(c.x, c.y + 2, 0)))
                candidates.Add(c);
        if (candidates.Count == 0) return;

        var taken = new List<Vector3Int>();
        const int MinSeparationSq = 16;   // four cells apart reads as a lane, not a queue
        for (int i = 0; i < villagerCount; i++)
        {
            Vector3Int pick = candidates[rng.Next(candidates.Count)];
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var c = candidates[rng.Next(candidates.Count)];
                bool clear = true;
                foreach (var t in taken)
                {
                    long dx = c.x - t.x, dy = c.y - t.y;
                    if (dx * dx + dy * dy < MinSeparationSq) { clear = false; break; }
                }
                if (clear) { pick = c; break; }
            }
            taken.Add(pick);

            var go = new GameObject("DwarvenVillager" + (i + 1));
            go.transform.position = influence.CellToWorld(pick);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = villagerSprite;
            sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = sortingOrder;
            villagers.Add(sr);
        }
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (villagers.Count == 0) return;
        if (PauseController.IsGamePaused) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        foreach (var v in villagers)
        {
            if (v == null) continue;
            world.z = v.transform.position.z;
            if (Vector3.Distance(world, v.transform.position) > clickRadius) continue;
            // Speak() honours the line's own once flag; the greeting repeats.
            WispCompanion.Instance?.Speak("village_greeting");
            return;
        }
    }
}
