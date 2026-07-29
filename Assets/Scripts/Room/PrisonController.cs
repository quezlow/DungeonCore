using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the fate of adventurers taken alive. A beaten non-Hero is subdued rather
/// than slain whenever a free cell stands ready inside a valid Prison. The capture
/// itself earns no notoriety, because nothing was seen to die -- what the core does
/// next is the whole point:
///
///   Release     -- mercy quiets the legend (notoriety down, alignment up)
///   Execute     -- a public end (notoriety up, alignment down) and a corpse for
///                  the Crypt to gather
///   Interrogate -- a soul held in the core's own stone keeps no secrets; opens
///                  that banner's ledger. The captive survives the reading.
///
/// A captive left unprocessed starves after starveDays dawns and leaves a corpse
/// where the cell stands. Cells are both the capacity and the opt-in: build none
/// and nothing is ever taken alive.
///
/// SCENE SETUP: add to the managers object. Assign: Cell Definition, Prisoner
/// Prefab, Corpse Prefab (the same prefab the adventurers reference). No other wiring.
/// </summary>
public class PrisonController : MonoBehaviour
{
    public static PrisonController Instance { get; private set; }

    [Tooltip("The cell furniture definition; its pieces are the holding slots.")]
    [SerializeField] private FurnitureDefinition cellDefinition;

    [Tooltip("Prisoner prefab spawned when an adventurer is taken alive.")]
    [SerializeField] private GameObject prisonerPrefab;

    [Tooltip("Corpse prefab left by an executed or starved captive (the same prefab adventurers carry).")]
    [SerializeField] private GameObject corpsePrefab;

    [Tooltip("Dawns a captive endures before starving. 0 = they never starve.")]
    [SerializeField, Min(0)] private int starveDays = 5;

    [Tooltip("Notoriety drained by releasing a captive. Mercy quiets the legend.")]
    [SerializeField, Min(0f)] private float releaseNotoriety = 8f;

    [Tooltip("Notoriety stirred by executing a captive. Cruelty travels.")]
    [SerializeField, Min(0f)] private float executeNotoriety = 8f;

    [Tooltip("Alignment shifted toward the light by a release.")]
    [SerializeField, Min(0f)] private float releaseAlignment = 4f;

    [Tooltip("Alignment shifted toward the dark by an execution.")]
    [SerializeField, Min(0f)] private float executeAlignment = 6f;

    [Tooltip("Visual lift so a captive reads as held in its cell.")]
    [SerializeField] private float housedYOffset = 0.15f;

    [Tooltip("Click radius (world units) for opening the prisoner panel on a captive.")]
    [SerializeField, Min(0.1f)] private float clickRadius = 0.75f;

    public float ReleaseNotoriety => releaseNotoriety;
    public float ExecuteNotoriety => executeNotoriety;
    public int StarveDays => starveDays;

    private readonly Dictionary<Prisoner, FurniturePiece> housed = new();
    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<FurniturePiece> furnBuf = new();
    private readonly List<Prisoner> starveScratch = new();
    private readonly List<Prisoner> unhouseScratch = new();

    private bool subscribed;

    private void OnEnable() { Instance = this; }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.OnDayStarted -= HandleDawn;
        subscribed = false;
    }

    private void Update()
    {
        // DayNightCycle may enable after us; subscribe as soon as it exists.
        if (!subscribed && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDawn;
            subscribed = true;
        }

        TickClick();
    }

    // -- Capture --------------------------------------------------

    /// <summary>House a freshly beaten adventurer in a free cell. Returns false when
    /// no cell stands ready, which is exactly how the player opts in: build no cells
    /// and nothing is ever taken alive.</summary>
    public bool TryImprison(string captiveName, AdventurerType type, CombatClass cls,
                            string className, bool named)
    {
        if (prisonerPrefab == null) return false;
        PruneHoused();

        var slots = BuildFreeSlots();
        if (slots.Count == 0) return false;

        var piece = slots[slots.Count - 1];
        var floor = piece.GetComponentInParent<FloorRoot>();
        var go = Instantiate(prisonerPrefab,
            piece.transform.position + Vector3.up * housedYOffset, Quaternion.identity);
        if (floor != null) go.transform.SetParent(floor.transform, true);

        var prisoner = go.GetComponent<Prisoner>();
        if (prisoner == null) { Destroy(go); return false; }

        prisoner.Initialise(captiveName, type, cls, className, named);
        housed[prisoner] = piece;

        AlertsLog.Instance?.AddAlert(
            captiveName + " is taken alive and put in the dark.",
            piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.Combat);
        DeedsController.Instance?.NotifyMoment("first_capture");
        return true;
    }

    // -- Dawn sweep -----------------------------------------------

    /// <summary>Evict captives whose gaol no longer stands, then age the rest; any
    /// who reach the starve mark are left as corpses where their cell stands.</summary>
    private void HandleDawn()
    {
        PruneHoused();

        // Pass 1: a captive whose cell has fallen out of a valid Prison slips
        // away into the dark. A broken gaol keeps nobody.
        unhouseScratch.Clear();
        foreach (var kvp in housed)
            if (!PieceInValidPrison(kvp.Value)) unhouseScratch.Add(kvp.Key);
        for (int i = 0; i < unhouseScratch.Count; i++)
        {
            var p = unhouseScratch[i];
            housed.Remove(p);
            if (p == null) continue;
            AlertsLog.Instance?.AddAlert(
                p.CaptiveName + " found the gaol broken and slipped away.",
                p.transform.position, FloorIndexOf(p.gameObject), AlertCategory.Threat);
            p.Resolve();
        }

        if (starveDays <= 0) return;

        // Pass 2: age every remaining captive; the forgotten ones starve.
        starveScratch.Clear();
        foreach (var kvp in housed)
        {
            var p = kvp.Key;
            if (p == null) continue;
            if (p.AdvanceDay() >= starveDays) starveScratch.Add(p);
        }

        for (int i = 0; i < starveScratch.Count; i++)
        {
            var p = starveScratch[i];
            housed.TryGetValue(p, out var piece);
            Vector3 where = piece != null ? piece.transform.position : p.transform.position;
            int floorIdx = piece != null ? FloorIndexOf(piece.gameObject) : FloorIndexOf(p.gameObject);

            SpawnCorpseFor(p, where);
            AlertsLog.Instance?.AddAlert(
                p.CaptiveName + " starved in the dark. I forgot they were mine to spend.",
                where, floorIdx, AlertCategory.System);
            housed.Remove(p);
            p.Resolve();
        }
    }

    // -- The verbs ------------------------------------------------

    /// <summary>Let them walk. Word of mercy travels as fast as word of murder:
    /// notoriety drains and the core drifts toward the light.</summary>
    public bool Release(FurniturePiece piece)
    {
        var p = GetHoused(piece);
        if (p == null) return false;

        DungeonCore.Instance?.AddNotoriety(-releaseNotoriety);
        AlignmentSystem.Instance?.Shift(releaseAlignment);
        AlertsLog.Instance?.AddAlert(
            p.CaptiveName + " walks free. Let them tell it however they like.",
            p.transform.position, FloorIndexOf(p.gameObject), AlertCategory.System);

        housed.Remove(p);
        p.Resolve();
        return true;
    }

    /// <summary>End them where they stand. The legend grows, the light recedes, and a
    /// corpse is left for the Crypt to gather at the next dawn.</summary>
    public bool Execute(FurniturePiece piece)
    {
        var p = GetHoused(piece);
        if (p == null) return false;

        Vector3 where = p.transform.position;
        int floorIdx = FloorIndexOf(p.gameObject);

        DungeonCore.Instance?.AddNotoriety(executeNotoriety);
        AlignmentSystem.Instance?.Shift(-executeAlignment);
        SpawnCorpseFor(p, where);
        AlertsLog.Instance?.AddAlert(
            p.CaptiveName + " is put to the stone. Let that be the story they carry.",
            where, floorIdx, AlertCategory.Combat);

        housed.Remove(p);
        p.Resolve();
        return true;
    }

    /// <summary>Read them. A soul held in the core's own rock keeps no secrets, so
    /// their banner's ledger opens. Once per banner; the captive survives it.</summary>
    public bool Interrogate(FurniturePiece piece)
    {
        var p = GetHoused(piece);
        if (p == null) return false;

        var f = p.Faction;
        if (FactionIntel.IntelKnown(f)) return false;

        UnlockState.Unlock(FactionIntel.IntelKey(f));
        AlertsLog.Instance?.AddAlert(
            p.CaptiveName + " gave up their banner's habits. The ledger is open.",
            p.transform.position, FloorIndexOf(p.gameObject), AlertCategory.Discovery);
        return true;
    }

    // -- Queries --------------------------------------------------

    public bool IsHoused(Prisoner p) => p != null && housed.ContainsKey(p);

    public Prisoner GetHoused(FurniturePiece piece)
    {
        if (piece == null) return null;
        foreach (var kvp in housed)
            if (kvp.Value == piece) return kvp.Key;
        return null;
    }

    /// <summary>The cell coordinate of the furniture holding this captive. The save
    /// reads this rather than the world position, which carries a visual lift.</summary>
    public bool TryGetCell(Prisoner p, out Vector3Int cell)
    {
        cell = default;
        if (p == null) return false;
        if (!housed.TryGetValue(p, out var piece) || piece == null) return false;
        cell = piece.OccupiedCell;
        return true;
    }

    // -- Save / restore surface -----------------------------------

    /// <summary>Rebuild one saved captive on its floor, rebinding to the cell at its
    /// recorded coordinate. A captive whose cell is gone slips away at the next dawn.</summary>
    public void RestorePrisoner(FloorRoot floor, string captiveName, AdventurerType type,
                                CombatClass cls, string className, bool named,
                                int daysHeld, Vector3Int cell)
    {
        if (prisonerPrefab == null || floor?.TileInfluence == null) return;

        var go = Instantiate(prisonerPrefab,
            floor.TileInfluence.CellToWorld(cell), Quaternion.identity);
        go.transform.SetParent(floor.transform, true);

        var p = go.GetComponent<Prisoner>();
        if (p == null) { Destroy(go); return; }
        p.Initialise(captiveName, type, cls, className, named, daysHeld);

        floor.Entities.FillAll(furnBuf);
        for (int i = 0; i < furnBuf.Count; i++)
        {
            var piece = furnBuf[i];
            if (piece == null || piece.Definition != cellDefinition) continue;
            if (piece.OccupiedCell != cell) continue;
            housed[p] = piece;
            p.transform.position = piece.transform.position + Vector3.up * housedYOffset;
            return;
        }
    }

    // -- Click surface --------------------------------------------

    private void TickClick()
    {
        if (PauseController.IsGamePaused) return;
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (DungeonBuildController.Instance == null
            || DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (housed.Count == 0) return;

        var cam = Camera.main;
        var activeFloor = FloorManager.Instance?.ActiveFloor;
        if (cam == null || activeFloor == null) return;

        Vector2 mp = mouse.position.ReadValue();
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -cam.transform.position.z));

        Prisoner best = null; FurniturePiece bestPiece = null;
        float bestSqr = clickRadius * clickRadius;
        foreach (var kvp in housed)
        {
            var p = kvp.Key;
            if (p == null || kvp.Value == null) continue;
            if (p.GetComponentInParent<FloorRoot>() != activeFloor) continue;
            float d = ((Vector2)(p.transform.position - world)).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = p; bestPiece = kvp.Value; }
        }
        if (best != null) PrisonerPanelUI.Instance?.Open(bestPiece, best);
    }

    // -- Internals ------------------------------------------------

    private void SpawnCorpseFor(Prisoner p, Vector3 where)
    {
        if (corpsePrefab == null || p == null) return;
        var go = Instantiate(corpsePrefab, where, Quaternion.identity);
        var floor = p.GetComponentInParent<FloorRoot>();
        if (floor != null) go.transform.SetParent(floor.transform, true);
        // A named captive keeps their name in death, so the Crypt can gather them.
        if (p.IsNamed) go.GetComponent<Corpse>()?.MarkNamed(p.CaptiveName);
    }

    private void PruneHoused()
    {
        unhouseScratch.Clear();
        foreach (var kvp in housed)
            if (kvp.Key == null || kvp.Key.Resolved || kvp.Value == null)
                unhouseScratch.Add(kvp.Key);
        // The scratch holds DESTROYED keys: Unity's overloaded null would make a
        // guard here skip exactly the entries that need removing. These are real
        // C# references -- remove them directly, no guard.
        for (int i = 0; i < unhouseScratch.Count; i++)
            housed.Remove(unhouseScratch[i]);
    }

    /// <summary>Free cells standing inside valid Prisons, all floors.</summary>
    private List<FurniturePiece> BuildFreeSlots()
    {
        var slots = new List<FurniturePiece>();
        var fm = FloorManager.Instance;
        if (fm == null || cellDefinition == null) return slots;

        foreach (var floor in fm.AllFloors)
        {
            if (floor?.Entities == null) continue;
            var prisonTiles = PrisonTilesOn(floor);
            if (prisonTiles.Count == 0) continue;

            floor.Entities.FillAll(furnBuf);
            for (int i = 0; i < furnBuf.Count; i++)
            {
                var p = furnBuf[i];
                if (p == null || p.Definition != cellDefinition) continue;
                bool inside = false;
                for (int t = 0; t < prisonTiles.Count; t++)
                    if (prisonTiles[t].Contains(p.OccupiedCell)) { inside = true; break; }
                if (!inside) continue;
                if (GetHoused(p) != null) continue;
                slots.Add(p);
            }
        }
        return slots;
    }

    /// <summary>Tile sets of every valid Prison on one floor (PrisonHousing marker).</summary>
    private List<HashSet<Vector3Int>> PrisonTilesOn(FloorRoot floor)
    {
        var sets = new List<HashSet<Vector3Int>>();
        floor.Entities.FillAll(roomBuf);
        for (int r = 0; r < roomBuf.Count; r++)
        {
            var anchor = roomBuf[r];
            if (anchor == null || !anchor.IsValid || anchor.AssignedRoom?.effects == null) continue;
            bool prison = false;
            var fx = anchor.AssignedRoom.effects;
            for (int e = 0; e < fx.Count; e++)
                if (fx[e] != null && fx[e].type == RoomEffectType.PrisonHousing) { prison = true; break; }
            if (!prison) continue;
            var tiles = anchor.GetRoomTiles();
            if (tiles != null && tiles.Count > 0) sets.Add(tiles);
        }
        return sets;
    }

    private bool PieceInValidPrison(FurniturePiece piece)
    {
        if (piece == null) return false;
        var floor = piece.GetComponentInParent<FloorRoot>();
        if (floor == null) return false;
        var sets = PrisonTilesOn(floor);
        for (int i = 0; i < sets.Count; i++)
            if (sets[i].Contains(piece.OccupiedCell)) return true;
        return false;
    }

    private static int FloorIndexOf(GameObject go)
    {
        var floor = go != null ? go.GetComponentInParent<FloorRoot>() : null;
        return floor != null ? floor.FloorIndex : -1;
    }
}