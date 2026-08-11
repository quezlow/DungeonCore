using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The Wandering Merchant. Per canon he stages through the forest-road gate
/// and docks at the camp's commerce anchor (cart, stall, shop - whichever the
/// tier has raised); per the locked forks he only visits once the main camp
/// reaches Camp tier, arrives on OnDayStarted, leaves at dusk on
/// OnNightStarted, and the gap between visits is rolled fresh each departure
/// (3-7 days), persisted so Continue keeps his schedule.
///
/// He is a puppet walker (the vignette pattern): a runtime SpriteRenderer
/// that walks gate -> dock, stands clickable while docked, and walks out at
/// dusk. Clicking him opens the MerchantShopUI. Stock is rolled per visit
/// from the TraderStockCatalog: 4-6 slots, at least one catch-up when any is
/// eligible, sold means gone until his next visit.
///
/// Catch-up eligibility needs no new plumbing: a loot-band pattern stocks
/// once some higher loot band is already learned - proof the ladder rolled
/// past it. If a raid ruins the anchor, he skips visits until it is rebuilt.
/// </summary>
public class WanderingMerchantController : MonoBehaviour, IShopVendor
{
    public static WanderingMerchantController Instance { get; private set; }

    [Header("Data")]
    [SerializeField] private TraderStockCatalog catalog;

    [Header("Staging")]
    [Tooltip("Where he walks in from - the forest-road gate. If unset, he appears at the dock.")]
    [SerializeField] private Transform gateEntry;
    [SerializeField] private Sprite merchantSprite;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField] private float walkSpeed = 2.6f;
    [Tooltip("He stands this far to the home side of the anchor, leaving the stall itself clear.")]
    [SerializeField] private float dockOffset = 0.9f;
    [SerializeField] private float clickRadius = 0.9f;

    [Header("Visits")]
    [Tooltip("Random days between visits, rolled at each departure.")]
    [SerializeField] private int minGapDays = 3;
    [SerializeField] private int maxGapDays = 7;
    [SerializeField] private int minSlots = 4;
    [SerializeField] private int maxSlots = 6;

    private SpriteRenderer walker;
    private Transform dock;
    private bool docked;
    private bool travelling;
    private readonly List<TraderStockCatalog.StockEntry> currentStock = new();

    public bool IsDocked => docked;
    public IReadOnlyList<TraderStockCatalog.StockEntry> CurrentStock => currentStock;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void OnEnable()
    {
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
            DayNightCycle.Instance.OnNightStarted += HandleNightStarted;
        }
    }

    private void OnDisable()
    {
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
            DayNightCycle.Instance.OnNightStarted -= HandleNightStarted;
        }
    }

    private void Start()
    {
        // Late-load catch: a save made mid-visit has an unrolled (due) schedule,
        // so he simply arrives again today.
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsDay)
            TryArrive();
    }

    private void Update()
    {
        if (!docked || walker == null) return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        world.z = walker.transform.position.z;
        if (Vector3.Distance(world, walker.transform.position) <= clickRadius)
            MerchantShopUI.Instance?.Open(this);
    }

    // -- Schedule ------------------------------------------------------------

    private void HandleDayStarted() => TryArrive();

    private void HandleNightStarted()
    {
        if (docked || travelling) StartCoroutine(Depart());
    }

    private void TryArrive()
    {
        if (docked || travelling) return;

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (nextVisitDay >= 0 && day < nextVisitDay) return;

        dock = FindDock();
        if (dock == null) return;   // camp not yet at tier, or the stall lies in ruin

        StartCoroutine(Arrive());
    }

    /// <summary>The first registered commerce anchor whose zone has reached
    /// Camp tier and whose prop is alive. No serialized zone id to go stale.</summary>
    private static Transform FindDock()
    {
        var camps = CampGrowthController.Instance;
        if (camps == null) return null;
        foreach (var pair in camps.CommerceAnchors)
        {
            if (pair.Value == null) continue;
            if (camps.TierOf(pair.Key) >= 1) return pair.Value;
        }
        return null;
    }

    private IEnumerator Arrive()
    {
        travelling = true;

        Vector3 dockPos = dock.position + (transform.position - dock.position).normalized * 0f;
        // Stand to the home side of the anchor: the canon places the anchor
        // facing the way home, so offset toward the dungeon reads as "at the counter".
        Vector3 homeward = (transform.position - dock.position).normalized;
        Vector3 stand = dock.position + homeward * dockOffset;

        Vector3 from = gateEntry != null ? gateEntry.position : stand;
        walker = MakeWalker(from);
        walker.flipX = (stand.x - from.x) < 0f;

        yield return MoveTo(walker.transform, stand, walkSpeed);

        RollStock();
        docked = true;
        travelling = false;

        if (WispCompanion.Instance != null && !WispCompanion.Instance.HasSpoken("merchant_first"))
        {
            WispCompanion.Instance.Speak("merchant_first");
            WispCompanion.Instance.Excite();
        }
    }

    private IEnumerator Depart()
    {
        MerchantShopUI.Instance?.CloseIfOpen();
        docked = false;
        travelling = true;

        if (walker != null)
        {
            Vector3 exit = gateEntry != null ? gateEntry.position
                                             : walker.transform.position;
            walker.flipX = (exit.x - walker.transform.position.x) < 0f;
            yield return MoveTo(walker.transform, exit, walkSpeed);
            Destroy(walker.gameObject);
            walker = null;
        }

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextVisitDay = day + Random.Range(minGapDays, maxGapDays + 1);
        travelling = false;
    }

    // -- Stock ---------------------------------------------------------------

    private void RollStock()
    {
        currentStock.Clear();
        if (catalog == null) return;

        int highestLearnedLootBand = 0;
        foreach (var e in catalog.entries)
            if (e.isCatchUp && e.pattern != null && UnlockState.IsUnlocked(e.pattern.Key))
                highestLearnedLootBand = Mathf.Max(highestLearnedLootBand, (int)e.pattern.band);

        var catchUps = new List<TraderStockCatalog.StockEntry>();
        var charges = new List<TraderStockCatalog.StockEntry>();
        var others = new List<TraderStockCatalog.StockEntry>();
        foreach (var e in catalog.entries)
        {
            if (TraderStockCatalog.IsOwned(e)) continue;
            if (e.type == TraderStockCatalog.StockType.Charge) charges.Add(e);
            else if (e.isCatchUp)
            {
                if (e.pattern != null && (int)e.pattern.band < highestLearnedLootBand)
                    catchUps.Add(e);
            }
            else others.Add(e);
        }

        int slots = Random.Range(minSlots, maxSlots + 1);

        // The guaranteed catch-up slot, when any is eligible.
        if (catchUps.Count > 0)
        {
            var pick = catchUps[Random.Range(0, catchUps.Count)];
            currentStock.Add(pick);
            catchUps.Remove(pick);
        }

        // The charge slot, mirroring the catch-up slot above and for a harder reason
        // (canon 41). A charge is never OWNED -- that is the whole point of it -- so
        // without a slot of its own every charge entry stays in the general pool
        // forever and crowds the finite manifest out exactly as the manifest empties,
        // which is the moment the wagon most needs to still have books on it.
        // EXACTLY ONE A VISIT: a wagon that rolled three scrolls and one book would
        // be a scroll cart, and the manifest is what he is for.
        if (charges.Count > 0)
            currentStock.Add(charges[Random.Range(0, charges.Count)]);

        // Charges are deliberately NOT poured into the pool below. The slot above is
        // their whole allowance.
        var pool = new List<TraderStockCatalog.StockEntry>();
        pool.AddRange(others);
        pool.AddRange(catchUps);
        while (currentStock.Count < slots && pool.Count > 0)
        {
            var pick = pool[Random.Range(0, pool.Count)];
            currentStock.Add(pick);
            pool.Remove(pick);
        }

        // Anything that reached the wagon has been HEARD OF, bought or not (canon 41).
        for (int i = 0; i < currentStock.Count; i++)
            TraderStockCatalog.NotifyStocked(currentStock[i]);
    }

    /// <summary>Spend gold and apply the purchase. Sold entries leave the
    /// wagon until his next visit rolls fresh stock.</summary>
    // -- IShopVendor -----------------------------------------------------------

    public string ShopTitle => "The Wandering Merchant";

    /// <summary>He haggles with nobody. List price, always.</summary>
    public int PriceOf(TraderStockCatalog.StockEntry entry) => entry != null ? entry.price : 0;

    public bool TryPurchase(TraderStockCatalog.StockEntry entry)
    {
        if (entry == null || !currentStock.Contains(entry)) return false;
        if (DungeonCore.Instance == null || !DungeonCore.Instance.TrySpendGold(entry.price)) return false;

        Vector3 at = walker != null ? walker.transform.position : transform.position;
        TraderStockCatalog.ApplyPurchase(entry, at,
            "The merchant's book gives up its whole art: " + entry.displayName + ".");

        currentStock.Remove(entry);
        return true;
    }

    // -- Puppet helpers ------------------------------------------------------

    private SpriteRenderer MakeWalker(Vector3 at)
    {
        var go = new GameObject("WanderingMerchant");
        go.transform.position = at;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = merchantSprite;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    private static IEnumerator MoveTo(Transform t, Vector3 goal, float speed)
    {
        while (t != null && Vector3.Distance(t.position, goal) > 0.05f)
        {
            t.position = Vector3.MoveTowards(t.position, goal, speed * Time.deltaTime);
            yield return null;
        }
        if (t != null) t.position = goal;
    }

    // -- Persistence (the TutorialDirector pattern) --------------------------

    private static int nextVisitDay = -1;   // -1: unscheduled - due the first eligible day

    public static int NextVisitDayForSave => nextVisitDay;
    public static void RestoreNextVisitDay(int value) => nextVisitDay = value;
    public static void ResetForNewGame() => nextVisitDay = -1;
}