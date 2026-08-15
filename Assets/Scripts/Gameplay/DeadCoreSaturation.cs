using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floor index 4 pushing back. The dead core in the vault never stopped
/// spawning, and what it makes contests whatever the player tries to hold.
///
/// A CONDITION, NOT A DEN, and every choice here follows from that (canon 42).
/// No hoard, no tier, no clear. There is nothing to defeat: the floor is
/// EXPENSIVE TO HOLD rather than DANGEROUS TO ENTER.
///
/// WHICH IS WHY PRESSURE READS ClaimedTileCount AND NOT OwnedTileCount. This
/// is the single number the whole system turns on, so it is worth being exact
/// about: MarkNaturalFloor mines every chamber, road and site interior on
/// REVEAL, so OwnedTileCount (minedTiles) climbs merely by walking around a
/// floor. Keying on it would have made ENTERING expensive -- the precise thing
/// canon rules out. ClaimedTileCount moves only when the player deliberately
/// takes ground, so the bill lands on holding.
///
/// NOT A CHAMBER POOL. A chamber-wild body is a one-time clear by construction
/// (WildMonsterController.MarkChamberCleared), which is "dangerous to enter"
/// wearing the wrong hat. These are seeded from the vault instead and simply
/// keep coming.
///
/// NO BOSS DOWN HERE. Entry 9's climax fires at Diamond 3 and surviving it
/// silences the recurring threats, so floor index 4 is entered by a god core in
/// a sandbox. The game already had its boss.
///
/// SCENE SETUP: add to the same persistent object that holds DenController.
/// No wiring -- it finds FloorManager and DayNightCycle itself.
/// </summary>
public class DeadCoreSaturation : MonoBehaviour
{
    public static DeadCoreSaturation Instance { get; private set; }

    /// <summary>The dead network. Entry 20 puts the vault on this floor and
    /// AncientSiteProfile's floor entry 4 carries reserveDeadCore.</summary>
    public const int SaturatedFloorIndex = 4;

    [Header("Population")]
    [Tooltip("Claimed cells on floor index 4 per living occupant. Higher means "
           + "a gentler floor. This is the dial the whole condition turns on.")]
    [SerializeField, Min(1)] private int claimedCellsPerOccupant = 60;

    [Tooltip("Live occupants never exceed this, before escalation.")]
    [SerializeField, Min(0)] private int populationCap = 12;

    [Tooltip("Most bodies raised in a single dawn, so a big claim does not "
           + "materialise an army in one night.")]
    [SerializeField, Min(1)] private int maxSpawnsPerDawn = 3;

    [Tooltip("Claimed cells below which the floor stays quiet entirely. A "
           + "player who has merely walked through is not holding anything.")]
    [SerializeField, Min(0)] private int quietBelowClaimedCells = 40;

    [Header("Escalation (breaking the vault heart)")]
    [Tooltip("Population cap and pressure both multiply by this once the heart "
           + "is broken. Entry 20 grants 60 research and a full level for that "
           + "break against -25 alignment and nothing else; this is its teeth.")]
    [SerializeField, Min(1f)] private float escalationMultiplier = 2f;

    [Header("Bodies")]
    [Tooltip("What the dead core is making. AUTHORED, and an empty list means "
           + "NOT YET AUTHORED rather than 'any' -- the readout says so plainly "
           + "instead of silently spawning nothing.")]
    [SerializeField] private List<MonsterDefinition> occupantDefinitions = new();

    private bool heartBroken;
    private readonly List<DungeonMonster> live = new();

    // Diagnostics. Every one of these exists because "no occupants appeared"
    // and "occupants appeared and did nothing" look identical from outside.
    private int lastClaimed;
    private int lastTarget;
    private int lastSpawned;
    private int totalSpawned;
    private int refusedNoFloor, refusedNoVault, refusedNoDefs, refusedNoCell, refusedQuiet;

    public bool HeartBroken => heartBroken;
    public int LiveCount { get { Prune(); return live.Count; } }
    public int LastClaimed => lastClaimed;
    public int LastTarget => lastTarget;
    public int TotalSpawned => totalSpawned;
    public int RefusedNoFloor => refusedNoFloor;
    public int RefusedNoVault => refusedNoVault;
    public int RefusedNoDefinitions => refusedNoDefs;
    public int RefusedNoCell => refusedNoCell;
    public int RefusedQuiet => refusedQuiet;
    public bool HasDefinitions => occupantDefinitions != null && occupantDefinitions.Count > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
    }

    private void OnDisable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (Instance == this) Instance = null;
    }

    /// <summary>Breaking the vault heart escalates the floor, permanently.
    /// Called from HolyGroundLedger's existing isVault branch.
    ///
    /// The ledger already refuses to pay the same heart twice (brokenSeals),
    /// so this cannot be re-won by reloading -- but it is saved anyway, because
    /// a flag that only exists because ANOTHER system remembered is a flag that
    /// breaks the day that system is refactored.</summary>
    public void NotifyVaultHeartBroken()
    {
        heartBroken = true;
    }

    // -- the tick ------------------------------------------------------

    private void HandleDayStarted()
    {
        Prune();
        lastSpawned = 0;

        var floor = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloor(SaturatedFloorIndex) : null;
        if (floor == null) { refusedNoFloor++; lastClaimed = 0; lastTarget = 0; return; }

        var influence = floor.TileInfluence;
        var features = floor.FeatureGenerator;
        if (influence == null || features == null) { refusedNoFloor++; return; }

        // The vault is the SOURCE. No vault, no saturation -- if the floor
        // somehow generated without one, the condition has no cause and
        // pretending otherwise would be inventing a threat.
        var vault = features.GetVaultSite();
        if (vault == null) { refusedNoVault++; return; }

        lastClaimed = influence.ClaimedTileCount;
        if (lastClaimed < quietBelowClaimedCells)
        {
            refusedQuiet++;
            lastTarget = 0;
            return;
        }

        float mult = heartBroken ? Mathf.Max(1f, escalationMultiplier) : 1f;
        int cap = Mathf.RoundToInt(populationCap * mult);
        int byClaim = Mathf.RoundToInt(lastClaimed * mult / Mathf.Max(1, claimedCellsPerOccupant));
        lastTarget = Mathf.Min(cap, byClaim);

        int want = Mathf.Min(maxSpawnsPerDawn, lastTarget - live.Count);
        if (want <= 0) return;

        if (!HasDefinitions) { refusedNoDefs++; return; }

        for (int i = 0; i < want; i++)
            if (SpawnOne(floor, influence, vault)) { lastSpawned++; totalSpawned++; }
    }

    private bool SpawnOne(FloorRoot floor, TileInfluenceManager influence, SiteData vault)
    {
        var def = occupantDefinitions[Random.Range(0, occupantDefinitions.Count)];
        if (def == null || def.prefab == null) { refusedNoDefs++; return false; }

        Vector3Int cell;
        if (!TryPickSpawnCell(floor, influence, vault, out cell)) { refusedNoCell++; return false; }

        var body = Instantiate(def.prefab, influence.CellToWorld(cell), Quaternion.identity);
        body.transform.SetParent(floor.transform, true);
        body.InitialiseAsDeepOccupant(floor, def);
        live.Add(body);
        return true;
    }

    /// <summary>A revealed, walkable, UNCLAIMED cell near the vault.
    ///
    /// UNCLAIMED IS THE POINT. They come out of the parts of the network the
    /// player has not taken, which is what makes advancing the claim feel like
    /// pushing against something rather than filling in a form. Walkability is
    /// tested through DungeonPathfinder rather than re-derived: a body standing
    /// where nothing can path is a body that will never reach anything, and
    /// that failure looks exactly like a body that spawned and lost interest.
    /// </summary>
    private bool TryPickSpawnCell(FloorRoot floor, TileInfluenceManager influence,
                                  SiteData vault, out Vector3Int cell)
    {
        cell = default;
        var anchor = vault.anchorCell != null
            ? vault.anchorCell.ToVector3Int()
            : Vector3Int.zero;

        // Rings outward from the vault. Bounded rather than a full-floor scan:
        // floor index 4 runs to radius 600 and a per-dawn sweep of that disc
        // would cost more than the feature is worth.
        const int maxRing = 40;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            int ring = Random.Range(4, maxRing);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var c = new Vector3Int(
                anchor.x + Mathf.RoundToInt(Mathf.Cos(ang) * ring),
                anchor.y + Mathf.RoundToInt(Mathf.Sin(ang) * ring),
                0);

            if (!floor.IsRevealed(c)) continue;
            if (influence.IsTileClaimed(c)) continue;
            if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(c))) continue;

            cell = c;
            return true;
        }
        return false;
    }

    private void Prune()
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] == null) live.RemoveAt(i);
    }

    // -- Save / Load ---------------------------------------------------

    public DeadCoreSaturationSaveData GetSaveData()
        => new DeadCoreSaturationSaveData { heartBroken = heartBroken, totalSpawned = totalSpawned };

    public void RestoreFromSave(DeadCoreSaturationSaveData data)
    {
        heartBroken = data != null && data.heartBroken;
        totalSpawned = data != null ? data.totalSpawned : 0;
        live.Clear();
    }

    /// <summary>Bodies are NOT persisted, deliberately. They are a condition of
    /// the floor rather than characters with histories, and the dawn after a
    /// load re-raises whatever the claim warrants. Persisting them would have
    /// meant a save format for something the tick recreates for free.</summary>
    public void ResetForNewGame()
    {
        heartBroken = false;
        totalSpawned = 0;
        lastClaimed = lastTarget = lastSpawned = 0;
        refusedNoFloor = refusedNoVault = refusedNoDefs = refusedNoCell = refusedQuiet = 0;
        live.Clear();
    }
}

[System.Serializable]
public class DeadCoreSaturationSaveData
{
    public bool heartBroken;
    public int totalSpawned;
}
