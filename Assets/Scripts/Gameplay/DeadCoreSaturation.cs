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

    [Tooltip("Half-width, in cells, of the ground an occupant will roam around "
           + "where it rose. Without a roam pool the body stands still forever "
           + "-- PickWildWanderTarget bails to its spawn position when the pool "
           + "is empty.")]
    [SerializeField, Min(1)] private int roamRadiusCells = 12;

    // ---- FLOOR INDEX 3: THE INCURSION -------------------------------
    // A SECOND NAMED BLOCK rather than a list of per-floor entries. Canon
    // defines exactly two deep floors and their rules genuinely differ -- floor
    // 4 answers to claimed ground around a vault, floor 3 to a road network and
    // a village. A generic list would be generality with no second user, and
    // half its fields inert per entry, which is the shape this project has
    // already been bitten by.
    [Header("Floor index 3 -- the incursion")]
    [Tooltip("Days between top-ups of the roaming incursion.")]
    [SerializeField, Min(1)] private int incursionEveryDays = 4;

    [Tooltip("Most hostiles roaming floor index 3 at once. Sized against the "
           + "eight villagers: enough that a hold which meets them can "
           + "plausibly lose, few enough that it can plausibly win.")]
    [SerializeField, Min(0)] private int incursionMax = 6;

    [Tooltip("World units from the camera below which a spawn is considered "
           + "ON-SCREEN and refused. They must never be seen arriving.")]
    [SerializeField, Min(1f)] private float offScreenDistance = 26f;

    [Tooltip("Half-width in cells of road an occupant roams once it has risen.")]
    [SerializeField, Min(1)] private int incursionRoamRadius = 20;

    public const int IncursionFloorIndex = 3;

    [Header("Escalation (breaking the vault heart)")]
    [Tooltip("Population cap and pressure both multiply by this once the heart "
           + "is broken. Entry 20 grants 60 research and a full level for that "
           + "break against -25 alignment and nothing else; this is its teeth.")]
    [SerializeField, Min(1f)] private float escalationMultiplier = 2f;

    [Header("Bodies")]
    [Tooltip("What the dead core is making. AUTHORED, and an empty list means "
           + "NOT YET AUTHORED rather than 'any' -- the readout says so plainly "
           + "instead of silently spawning nothing.")]
    [SerializeField] private List<DeepOccupantEntry> occupantDefinitions = new();

    private bool heartBroken;
    private readonly List<DungeonMonster> live = new();

    // Diagnostics. Every one of these exists because "no occupants appeared"
    // and "occupants appeared and did nothing" look identical from outside.
    private int lastClaimed;
    private int lastTarget;
    private int lastSpawned;
    private int totalSpawned;
    private int refusedNoFloor, refusedNoVault, refusedNoDefs, refusedNoCell, refusedQuiet;
    // Counted rather than refused: a body with no roam pool still spawns and
    // still fights, it simply never moves. Silent statues are exactly the
    // failure this counter exists to make loud.
    private int refusedNoRoam;

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
    public int SpawnedWithoutRoam => refusedNoRoam;
    public bool HasDefinitions => occupantDefinitions != null && occupantDefinitions.Count > 0;

    /// <summary>Pick an entry, or null when the list is empty or the chosen
    /// slot is unusable. One place to change if the pick ever stops being
    /// uniform.</summary>
    private DeepOccupantEntry PickEntry()
    {
        if (!HasDefinitions) return null;
        var e = occupantDefinitions[Random.Range(0, occupantDefinitions.Count)];
        return (e == null || e.definition == null || e.definition.prefab == null) ? null : e;
    }

    /// <summary>Apply an entry's tint, if it asked for one. Kept beside the two
    /// spawn paths rather than inside them so they cannot drift apart.</summary>
    private static void ApplyEntryTint(DungeonMonster body, DeepOccupantEntry entry)
    {
        if (body == null || entry == null || !entry.applyTint) return;
        body.SetDeepOccupantTint(entry.tint);
    }

    private readonly List<DungeonMonster> incursion = new List<DungeonMonster>();
    private bool villageFound;
    private int incursionSpawned, incursionOnScreenFallbacks, incursionNoCell;
    private int villageReachedTimes;

    public int IncursionLive { get { PruneIncursion(); return incursion.Count; } }
    public int IncursionSpawned => incursionSpawned;
    public int IncursionOnScreenFallbacks => incursionOnScreenFallbacks;
    public int IncursionNoCell => incursionNoCell;
    public bool VillageFound => villageFound;
    /// <summary>How many times hostiles have REACHED the village lanes. Held
    /// apart from how many times the hold actually FELL, because "they got
    /// there" and "they won" are different questions and a single number
    /// answers neither.</summary>
    public int VillageReachedTimes => villageReachedTimes;

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
        // THE CLIMAX STANDS EVERYTHING DOWN, exactly as it does for the
        // mercenaries, the nobles, the Holy Order and the wild events. Canon 9:
        // surviving it silences the recurring threats for good. Without this the
        // player would be defending a hold that is not even theirs, forever,
        // which is the failure mode this gate exists to prevent.
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats)
            return;

        TickIncursion();

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
        var entry = PickEntry();
        if (entry == null) { refusedNoDefs++; return false; }
        var def = entry.definition;

        Vector3Int cell;
        if (!TryPickSpawnCell(floor, influence, vault, out cell)) { refusedNoCell++; return false; }

        var roam = CollectRoamCells(floor, influence, cell);
        if (roam.Count == 0) refusedNoRoam++;

        var body = Instantiate(def.prefab, influence.CellToWorld(cell), Quaternion.identity);
        body.transform.SetParent(floor.transform, true);
        body.InitialiseAsDeepOccupant(floor, def, roam);
        ApplyEntryTint(body, entry);
        live.Add(body);
        return true;
    }

    /// <summary>Ground this body may walk, gathered around where it rose.
    ///
    /// ROAM CELLS ARE NOT SPAWN CELLS, and the difference is the floor 4
    /// condition working rather than a detail. A SPAWN cell must be unclaimed:
    /// they come out of ground the player has not taken. A ROAM cell may be
    /// claimed, because walking INTO held ground is the whole of "expensive to
    /// hold" -- leash them to unclaimed ground only and they would politely
    /// avoid the player's territory, which is the opposite of the design.
    ///
    /// A square band rather than a disc: cheap, and the pathfinder filters what
    /// is actually reachable when the wander picks from this pool anyway.</summary>
    private List<Vector3Int> CollectRoamCells(FloorRoot floor, TileInfluenceManager influence,
                                              Vector3Int origin)
    {
        var cells = new List<Vector3Int>();
        for (int dy = -roamRadiusCells; dy <= roamRadiusCells; dy++)
        {
            for (int dx = -roamRadiusCells; dx <= roamRadiusCells; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                if (!floor.IsRevealed(c)) continue;
                if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(c))) continue;
                cells.Add(c);
            }
        }
        return cells;
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

    // ---- TEST SCAFFOLDING ---------------------------------------------
    // Called only from Commands. These bypass the CADENCE and the THRESHOLDS,
    // which is the point, but never the definition check -- an empty
    // occupantDefinitions is a real fault and the most likely one to be
    // present, so it is reported rather than hidden.

    /// <summary>Raise one occupant at a given cell on a given floor and return
    /// a human-readable account of what happened. Joins the floor's own live
    /// list so the readouts and the caps see it like any other body.</summary>
    public string ForceSpawnAt(FloorRoot floor, Vector3Int cell)
    {
        if (floor == null) return "no floor";
        var influence = floor.TileInfluence;
        if (influence == null) return "floor has no TileInfluence";
        if (!HasDefinitions)
            return "occupantDefinitions is EMPTY -- author at least one MonsterDefinition "
                 + "on DeadCoreSaturation before expecting a body";

        var entry = PickEntry();
        if (entry == null)
            return "an entry in occupantDefinitions is null, or has no definition or prefab";
        var def = entry.definition;

        var roam = CollectRoamCells(floor, influence, cell);
        bool stuck = roam.Count == 0;
        if (stuck) roam.Add(cell);

        var body = Instantiate(def.prefab, influence.CellToWorld(cell), Quaternion.identity);
        body.transform.SetParent(floor.transform, true);
        body.InitialiseAsDeepOccupant(floor, def, roam);
        ApplyEntryTint(body, entry);

        if (floor.FloorIndex == IncursionFloorIndex) incursion.Add(body);
        else live.Add(body);

        return $"raised {def.monsterName}"
             + (entry.applyTint ? $" (tinted {entry.tint})" : " (untinted)")
             + $" at {cell} on floor index {floor.FloorIndex}, "
             + $"roam pool {roam.Count} cell(s)"
             + (stuck ? "  !! POOL OF ONE -- it will not move. The surrounding ground is "
                      + "not revealed and walkable." : "");
    }

    /// <summary>Fill the floor 3 incursion to its cap on the village's own
    /// doorstep and trip the aggregation at once, so the siege can be watched
    /// rather than waited for.</summary>
    public string ForceVillageSiege()
    {
        var floor = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloor(IncursionFloorIndex) : null;
        if (floor == null) return $"floor index {IncursionFloorIndex} does not exist";
        var village = DwarvenVillageController.Instance;
        if (village == null || !village.Established)
            return "no established village to besiege";

        var lanes = new List<Vector3Int>();
        foreach (var c in village.LaneCells) lanes.Add(c);
        if (lanes.Count == 0) return "the village reports no lane cells";

        PruneIncursion();
        int want = Mathf.Max(0, incursionMax - incursion.Count);
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < want; i++)
        {
            var cell = lanes[Random.Range(0, lanes.Count)];
            report.AppendLine("    " + ForceSpawnAt(floor, cell));
        }

        // Trip the draw directly rather than waiting for the dawn probe.
        villageFound = true;
        villageReachedTimes++;
        for (int i = 0; i < incursion.Count; i++)
            incursion[i]?.SetDeepRoamCells(lanes);

        return $"raised {want} in the lanes and tripped the aggregation; "
             + $"{incursion.Count} now drawn to the hold.\n" + report.ToString().TrimEnd();
    }

    private void PruneIncursion()
    {
        for (int i = incursion.Count - 1; i >= 0; i--)
            if (incursion[i] == null) incursion.RemoveAt(i);
    }

    // ---- floor index 3 ------------------------------------------------

    /// <summary>Top the roaming incursion up, then check whether any of them
    /// has found the village.</summary>
    private void TickIncursion()
    {
        PruneIncursion();

        var floor = FloorManager.Instance != null
            ? FloorManager.Instance.GetFloor(IncursionFloorIndex) : null;
        if (floor == null) return;
        var influence = floor.TileInfluence;
        var features = floor.FeatureGenerator;
        if (influence == null || features == null) return;

        CheckVillageReached(influence);

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (day % Mathf.Max(1, incursionEveryDays) != 0) return;
        if (incursion.Count >= incursionMax) return;
        if (!HasDefinitions) { refusedNoDefs++; return; }

        var roadCells = CollectRevealedRoadCells(floor, influence, features);
        if (roadCells.Count == 0) return;   // nothing revealed yet: nothing to walk

        SpawnIncursionOne(floor, influence, roadCells);
    }

    /// <summary>Revealed road centreline cells on this floor.
    ///
    /// REVEALED, and this is a correction to the first design rather than a
    /// detail. Spawning on an UNREVEALED segment was the intent, and it cannot
    /// work: CaveWallClassifier records that UnfogRoadSegment calls
    /// MarkNaturalFloor per segment, "so the next stretch stayed un-mined and
    /// therefore SOLID". A body raised there could not stand, walk or path. The
    /// intent -- that the player never watches them arrive -- is kept by
    /// choosing an OFF-SCREEN cell instead, and on-screen fallbacks are counted
    /// so the rate is visible rather than assumed.</summary>
    private List<Vector3Int> CollectRevealedRoadCells(FloorRoot floor,
                                                      TileInfluenceManager influence,
                                                      TerrainFeatureGenerator features)
    {
        var cells = new List<Vector3Int>();
        var data = features.FeatureData;
        if (data == null || data.roads == null) return cells;

        foreach (var road in data.roads)
        {
            if (road == null || road.polyline == null) continue;
            int segLen = Mathf.Max(1, road.segmentLength);
            for (int i = 0; i < road.polyline.Count; i++)
            {
                if (!features.IsRoadSegmentRevealed(i / segLen)) continue;
                var c = road.polyline[i].ToVector3Int();
                if (!floor.IsRevealed(c)) continue;
                if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(c))) continue;
                cells.Add(c);
            }
        }
        return cells;
    }

    private void SpawnIncursionOne(FloorRoot floor, TileInfluenceManager influence,
                                   List<Vector3Int> roadCells)
    {
        var entry = PickEntry();
        if (entry == null) { refusedNoDefs++; return; }
        var def = entry.definition;

        var cam = Camera.main;
        Vector3Int chosen = default;
        bool found = false;
        float bestDist = -1f;

        // Prefer the FURTHEST off-screen cell rather than the first: on a floor
        // the player is actively working, "off-screen" can mean just past the
        // edge, and a body appearing one pan away is a body they watched arrive.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var c = roadCells[Random.Range(0, roadCells.Count)];
            float d = cam != null
                ? Vector3.Distance(cam.transform.position, influence.CellToWorld(c))
                : float.MaxValue;
            if (d < offScreenDistance) continue;
            if (d > bestDist) { bestDist = d; chosen = c; found = true; }
        }

        if (!found)
        {
            // Every sample was on-screen. Raise one anyway rather than stalling
            // the incursion forever on a player who happens to be parked on the
            // road -- but COUNT it, because a high number here means they are
            // routinely seen arriving and the distance wants raising.
            chosen = roadCells[Random.Range(0, roadCells.Count)];
            incursionOnScreenFallbacks++;
        }

        var body = Instantiate(def.prefab, influence.CellToWorld(chosen), Quaternion.identity);
        body.transform.SetParent(floor.transform, true);

        var roam = CollectRoadRoam(roadCells, chosen);
        if (roam.Count == 0) { roam.Add(chosen); incursionNoCell++; }
        body.InitialiseAsDeepOccupant(floor, def, roam);
        ApplyEntryTint(body, entry);

        incursion.Add(body);
        incursionSpawned++;
    }

    private List<Vector3Int> CollectRoadRoam(List<Vector3Int> roadCells, Vector3Int origin)
    {
        var roam = new List<Vector3Int>();
        int r = Mathf.Max(1, incursionRoamRadius);
        for (int i = 0; i < roadCells.Count; i++)
        {
            var c = roadCells[i];
            if (Mathf.Abs(c.x - origin.x) <= r && Mathf.Abs(c.y - origin.y) <= r)
                roam.Add(c);
        }
        return roam;
    }

    /// <summary>Has one of them found the hold? If so, draw the rest.
    ///
    /// ONE FINDS IT AND ALL COME, which is the design and also the only version
    /// that reads as a siege: six bodies wandering in one at a time is six
    /// skirmishes the villagers win. The draw is a RE-LEASH, so the shipped
    /// wander does the walking.</summary>
    private void CheckVillageReached(TileInfluenceManager influence)
    {
        var village = DwarvenVillageController.Instance;
        if (village == null || !village.Established || incursion.Count == 0) return;

        var lanes = new List<Vector3Int>();
        foreach (var c in village.LaneCells) lanes.Add(c);
        if (lanes.Count == 0) return;

        if (!villageFound)
        {
            var laneSet = new HashSet<Vector3Int>(lanes);
            for (int i = 0; i < incursion.Count; i++)
            {
                var b = incursion[i];
                if (b == null) continue;
                if (!laneSet.Contains(influence.WorldToCell(b.transform.position))) continue;
                villageFound = true;
                villageReachedTimes++;
                Debug.Log("[DeepIncursion] one of them has found the hold; the rest are "
                        + "drawn to it.");
                break;
            }
        }

        if (!villageFound) return;
        for (int i = 0; i < incursion.Count; i++)
            incursion[i]?.SetDeepRoamCells(lanes);
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
        refusedNoRoam = 0;
        incursion.Clear();
        villageFound = false;
        incursionSpawned = incursionOnScreenFallbacks = incursionNoCell = 0;
        villageReachedTimes = 0;
        live.Clear();
    }
}

/// <summary>One authored deep occupant body, and whether it wants darkening.
///
/// A BOOL RATHER THAN "WHITE MEANS NO TINT". A white tint in the Inspector is
/// indistinguishable from a field nobody filled in, which is the ambiguous
/// default this project has already been bitten by. applyTint defaults OFF
/// because the AUTHORED ART IS THE INTENDED LOOK -- the derived tail in the art
/// guide already darkens these at generation time, so a runtime tint is the
/// exception for a body that came out brighter than its fellows, not the rule.
/// </summary>
[System.Serializable]
public class DeepOccupantEntry
{
    public MonsterDefinition definition;

    [Tooltip("Off means ship the sprite exactly as authored. On applies the "
           + "tint below, for a body that reads too bright beside the others.")]
    public bool applyTint;

    [Tooltip("Multiplied into the sprite. Note this is a MULTIPLY, so it can "
           + "only darken and cannot brighten, and it preserves the sprite's "
           + "internal contrast rather than flattening it -- it will not turn a "
           + "legible body into a silhouette. Around 0.75 grey takes a body "
           + "down a shade without flattening it.")]
    public Color tint = new Color(0.75f, 0.75f, 0.8f, 1f);
}

[System.Serializable]
public class DeadCoreSaturationSaveData
{
    public bool heartBroken;
    public int totalSpawned;
}
