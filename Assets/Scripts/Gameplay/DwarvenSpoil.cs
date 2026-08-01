using UnityEngine;

/// <summary>
/// The spoil ledger: what the Deep Holds will pay for the stone you dig out.
///
/// THE FICTION. They are miners who never came up. They do not want the core
/// dead and they do not care what it is -- but they will buy good rock, and the
/// rock under floor index 3 is the rock their own road was cut through. It is
/// the same verb as claiming their road, with the sign flipped: dig your own
/// stone and they pay you; take theirs and they do not.
///
/// WHY THIS IS NOT A STOCKPILE. Canon 14 closed that question: patterns are
/// boolean discovery flags, no stockpile, no crafting sim. This is not an
/// inventory of materials, it is an unpaid INVOICE -- a single int of gold owed,
/// accrued as you mine and settled at the counter. Nothing is carried, nothing
/// is crafted, and no new resource appears anywhere in the UI.
///
/// SCALE. Mining costs 5 mana times terrain resistance, and Granite and Ruins
/// resist 4.0 and 6.0 -- so 20 and 30 mana a cell. Paying 3g and 5g against that
/// puts the return near one gold per six or seven mana at a fixed ratio to the
/// cost, which is deliberate: it must never become cheaper to mine for gold than
/// to mine for room.
/// </summary>
public static class DwarvenSpoil
{
    /// <summary>Gold owed and not yet collected.</summary>
    public static int Unsold { get; private set; }

    /// <summary>Lifetime gold taken at the counter. Not spent on anything yet;
    /// it exists so the guide's test steps have something to read.</summary>
    public static int LifetimeSold { get; private set; }

    /// <summary>Lowest floor index whose stone the Deep Holds will buy. Floors
    /// above this are nowhere near their road and they have no interest.
    ///
    /// Tracks the gatehouse floor, which is index 2 -- the outpost moved down from
    /// index 3 when the floor plan was corrected. If the gatehouse ever moves
    /// again, this moves with it: the rule is "their road's floor and below", not
    /// a number that happens to be right today.</summary>
    public const int MinFloorIndex = 2;

    public static int ValueOf(TerrainType type) => type switch
    {
        TerrainType.Granite => 3,
        TerrainType.Ruins => 5,
        _ => 0,
    };

    /// <summary>Credits one mined cell. Silent and free for terrain they do not
    /// want, which is most of it.</summary>
    public static void CreditMined(int floorIndex, TerrainType type)
    {
        if (floorIndex < MinFloorIndex) return;
        int v = ValueOf(type);
        if (v <= 0) return;
        Unsold += v;
    }

    /// <summary>Settles the invoice. Returns the gold paid.
    ///
    /// AddGold CLAMPS at the treasury cap rather than refusing, so an overlarge
    /// settlement can lose the overflow. That is by design and not a bug to work
    /// around here: Vaulted Reserves is on the dwarves' own shelf, and raising
    /// the cap before selling a deep excavation is the player's call to make.</summary>
    public static int Settle()
    {
        int owed = Unsold;
        if (owed <= 0) return 0;
        Unsold = 0;
        LifetimeSold += owed;
        DungeonCore.Instance?.AddGold(owed);
        return owed;
    }

    public static void RestoreFromSave(int unsold, int lifetime)
    {
        Unsold = Mathf.Max(0, unsold);
        LifetimeSold = Mathf.Max(0, lifetime);
    }

    public static void ResetForNewDungeon()
    {
        Unsold = 0;
        LifetimeSold = 0;
    }
}
