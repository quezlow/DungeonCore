using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the fate of named-hero corpses (canon 16). Named corpses lie where they
/// fall until dawn; at dawn each one is gathered into a free sarcophagus inside a
/// valid Crypt (any floor) or, if no stone stands ready, fades for good. Housed
/// corpses persist indefinitely -- and across saves -- until deliberately raised.
///
/// The raise is irreversible and the servant is mortal: one life, no respawn, no
/// timer. Costs mana + the risen definition's capacity (held while it walks,
/// returned on the fall) and stirs notoriety.
///
/// Also the click surface: with no build mode active, clicking a housed corpse
/// opens the CryptRaiseUI.
///
/// SCENE SETUP: add to the managers object. Assign: Sarcophagus Definition,
/// Risen Hero Definition (Monster_RisenHero), Corpse Prefab (the same prefab
/// adventurers reference). No other wiring.
/// </summary>
public class CryptController : MonoBehaviour
{
    public static CryptController Instance { get; private set; }

    [Tooltip("The sarcophagus furniture definition; its pieces are the preservation slots.")]
    [SerializeField] private FurnitureDefinition sarcophagusDefinition;

    [Tooltip("The monster a deliberate raise produces (Monster_RisenHero).")]
    [SerializeField] private MonsterDefinition risenHeroDefinition;

    [Tooltip("Corpse prefab used when restoring saved named corpses (same prefab the adventurers carry).")]
    [SerializeField] private GameObject corpsePrefab;

    [Tooltip("Mana cost of a deliberate raise.")]
    [SerializeField, Min(0f)] private float raiseManaCost = 100f;

    [Tooltip("Notoriety stirred by a deliberate raise. Desecration travels.")]
    [SerializeField, Min(0f)] private float raiseNotoriety = 15f;

    [Tooltip("Visual lift so a housed corpse reads as lying on its sarcophagus.")]
    [SerializeField] private float housedYOffset = 0.15f;

    [Tooltip("Click radius (world units) for opening the raise panel on a housed corpse.")]
    [SerializeField, Min(0.1f)] private float clickRadius = 0.75f;

    public float RaiseManaCost => raiseManaCost;
    public int RisenCapacityCost => risenHeroDefinition != null ? risenHeroDefinition.CapacityCost : 0;

    private readonly Dictionary<Corpse, FurniturePiece> housed = new();
    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<FurniturePiece> furnBuf = new();
    private readonly List<Corpse> corpseScratch = new();
    private readonly List<Corpse> unhouseScratch = new();

    private void OnEnable() { Instance = this; }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.OnDayStarted -= HandleDawn;
        subscribed = false;
    }

    private bool subscribed;

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

    // -- Dawn sweep -----------------------------------------------

    /// <summary>House-or-fade every unhoused named corpse; evict corpses whose
    /// crypt no longer stands, then let them compete for the remaining stone.</summary>
    private void HandleDawn()
    {
        PruneHoused();

        // Pass 1: corpses housed in a sarcophagus that is no longer inside a
        // valid Crypt fall back to unhoused (they lie where the stone is).
        unhouseScratch.Clear();
        foreach (var kvp in housed)
            if (!PieceInValidCrypt(kvp.Value)) unhouseScratch.Add(kvp.Key);
        for (int i = 0; i < unhouseScratch.Count; i++) housed.Remove(unhouseScratch[i]);

        // Pass 2: gather every unhoused named corpse into free stone, or fade it.
        corpseScratch.Clear();
        var active = Corpse.Active;
        for (int i = 0; i < active.Count; i++)
        {
            var c = active[i];
            if (c == null || c.Claimed || !c.IsNamed) continue;
            if (housed.ContainsKey(c)) continue;
            corpseScratch.Add(c);
        }
        if (corpseScratch.Count == 0) return;

        var slots = BuildFreeSlots();
        for (int i = 0; i < corpseScratch.Count; i++)
        {
            var c = corpseScratch[i];
            if (slots.Count > 0)
            {
                var piece = slots[slots.Count - 1];
                slots.RemoveAt(slots.Count - 1);
                House(c, piece);
            }
            else
            {
                AlertsLog.Instance?.AddAlert(
                    "No stone stood ready; " + c.HeroName + " is lost to the worms.",
                    c.transform.position, FloorIndexOf(c.gameObject), AlertCategory.System);
                Destroy(c.gameObject);
            }
        }
    }

    private void House(Corpse corpse, FurniturePiece piece)
    {
        var floor = piece.GetComponentInParent<FloorRoot>();
        if (floor != null) corpse.transform.SetParent(floor.transform, true);
        corpse.transform.position = piece.transform.position + Vector3.up * housedYOffset;
        housed[corpse] = piece;
        AlertsLog.Instance?.AddAlert(
            "The named dead are gathered: " + corpse.HeroName + " is laid in stone.",
            piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.System);
    }

    // -- The raise ------------------------------------------------

    /// <summary>Deliberate raise from a housed sarcophagus. Irreversible; one life.</summary>
    public bool RaiseFromSarcophagus(FurniturePiece piece)
    {
        PruneHoused();
        var corpse = GetHoused(piece);
        var core = DungeonCore.Instance;
        if (corpse == null || core == null || risenHeroDefinition == null) return false;

        if (!PieceInValidCrypt(piece))
        {
            // Raise refusals must land even before the alerts research: the
            // wisp speaks ungated; the ledger keeps the record once learned.
            const string line = "The crypt is broken; the dead will not answer.";
            WispCompanion.Instance?.SpeakLine(line);
            AlertsLog.Instance?.AddAlert(line,
                piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.System);
            return false;
        }

        int capCost = risenHeroDefinition.CapacityCost;
        if (!core.TrySpendCapacity(capCost))
        {
            const string line = "No room in my strength for another servant. The dead can wait.";
            WispCompanion.Instance?.SpeakLine(line);
            AlertsLog.Instance?.AddAlert(line,
                piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.System);
            return false;
        }
        if (!core.SpendMana(raiseManaCost))
        {
            core.ReturnCapacity(capCost);
            const string line = "Not enough mana to wake what sleeps here.";
            WispCompanion.Instance?.SpeakLine(line);
            AlertsLog.Instance?.AddAlert(line,
                piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.System);
            return false;
        }

        var floor = piece.GetComponentInParent<FloorRoot>();
        var spawner = DungeonBuildController.Instance?.SpawnRaisedMinion(
            floor, risenHeroDefinition, piece.OccupiedCell, "Risen " + corpse.HeroName);
        if (spawner == null)
        {
            core.ReturnCapacity(capCost);
            return false;
        }

        string name = corpse.HeroName;
        housed.Remove(corpse);
        corpse.Claim();
        core.AddNotoriety(raiseNotoriety);
        AlertsLog.Instance?.AddAlert(
            "Risen " + name + " walks again. One life; I intend to spend it.",
            piece.transform.position, FloorIndexOf(piece.gameObject), AlertCategory.Combat);
        DeedsController.Instance?.NotifyMoment("first_raise");
        return true;
    }

    // -- Queries --------------------------------------------------

    public bool IsHoused(Corpse corpse) => corpse != null && housed.ContainsKey(corpse);

    public Corpse GetHoused(FurniturePiece piece)
    {
        if (piece == null) return null;
        foreach (var kvp in housed)
            if (kvp.Value == piece) return kvp.Key;
        return null;
    }

    // -- Save / restore surface -----------------------------------

    /// <summary>Rebuild one saved named corpse on its floor. Housed corpses rebind to
    /// the sarcophagus at their cell; if the stone is gone, the next dawn decides.</summary>
    public void RestoreNamedCorpse(FloorRoot floor, string heroName, Vector3Int cell, bool wasHoused)
    {
        if (corpsePrefab == null || floor?.TileInfluence == null) return;
        var go = Instantiate(corpsePrefab, floor.TileInfluence.CellToWorld(cell), Quaternion.identity);
        go.transform.SetParent(floor.transform, true);
        var corpse = go.GetComponent<Corpse>();
        if (corpse == null) { Destroy(go); return; }
        corpse.MarkNamed(heroName);

        if (!wasHoused) return;
        floor.Entities.FillAll(furnBuf);
        for (int i = 0; i < furnBuf.Count; i++)
        {
            var p = furnBuf[i];
            if (p == null || p.Definition != sarcophagusDefinition) continue;
            if (p.OccupiedCell != cell) continue;
            housed[corpse] = p;
            corpse.transform.position = p.transform.position + Vector3.up * housedYOffset;
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

        Corpse best = null; FurniturePiece bestPiece = null;
        float bestSqr = clickRadius * clickRadius;
        foreach (var kvp in housed)
        {
            var c = kvp.Key;
            if (c == null || kvp.Value == null) continue;
            if (c.GetComponentInParent<FloorRoot>() != activeFloor) continue;
            float d = ((Vector2)(c.transform.position - world)).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = c; bestPiece = kvp.Value; }
        }
        if (best != null) CryptRaiseUI.Instance?.Open(bestPiece, best);
    }

    // -- Internals ------------------------------------------------

    private void PruneHoused()
    {
        unhouseScratch.Clear();
        foreach (var kvp in housed)
            if (kvp.Key == null || kvp.Key.Claimed || kvp.Value == null) unhouseScratch.Add(kvp.Key);
        for (int i = 0; i < unhouseScratch.Count; i++)
            if (unhouseScratch[i] != null) housed.Remove(unhouseScratch[i]);
        housed.Remove(null);
    }

    /// <summary>Free sarcophagi standing inside valid Crypts, all floors.</summary>
    private List<FurniturePiece> BuildFreeSlots()
    {
        var slots = new List<FurniturePiece>();
        var fm = FloorManager.Instance;
        if (fm == null || sarcophagusDefinition == null) return slots;

        foreach (var floor in fm.AllFloors)
        {
            if (floor?.Entities == null) continue;
            var cryptTiles = CryptTilesOn(floor);
            if (cryptTiles.Count == 0) continue;

            floor.Entities.FillAll(furnBuf);
            for (int i = 0; i < furnBuf.Count; i++)
            {
                var p = furnBuf[i];
                if (p == null || p.Definition != sarcophagusDefinition) continue;
                bool inside = false;
                for (int t = 0; t < cryptTiles.Count; t++)
                    if (cryptTiles[t].Contains(p.OccupiedCell)) { inside = true; break; }
                if (!inside) continue;
                if (GetHoused(p) != null) continue;
                slots.Add(p);
            }
        }
        return slots;
    }

    /// <summary>Tile sets of every valid Crypt on one floor (CryptPreservation marker).</summary>
    private List<HashSet<Vector3Int>> CryptTilesOn(FloorRoot floor)
    {
        var sets = new List<HashSet<Vector3Int>>();
        floor.Entities.FillAll(roomBuf);
        for (int r = 0; r < roomBuf.Count; r++)
        {
            var anchor = roomBuf[r];
            if (anchor == null || !anchor.IsValid || anchor.AssignedRoom?.effects == null) continue;
            bool crypt = false;
            var fx = anchor.AssignedRoom.effects;
            for (int e = 0; e < fx.Count; e++)
                if (fx[e] != null && fx[e].type == RoomEffectType.CryptPreservation) { crypt = true; break; }
            if (!crypt) continue;
            var tiles = anchor.GetRoomTiles();
            if (tiles != null && tiles.Count > 0) sets.Add(tiles);
        }
        return sets;
    }

    private bool PieceInValidCrypt(FurniturePiece piece)
    {
        if (piece == null) return false;
        var floor = piece.GetComponentInParent<FloorRoot>();
        if (floor == null) return false;
        var sets = CryptTilesOn(floor);
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