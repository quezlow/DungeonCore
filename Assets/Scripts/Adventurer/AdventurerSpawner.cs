using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns adventurer parties at the dungeon entrance.
///
/// DAY 27 SECTION 2B CHANGE
///   - Spawning pauses while the core is in transit (subscribes to
///     DungeonCoreTransit.OnTransitStarted / OnTransitCompleted).
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    public static AdventurerSpawner Instance { get; private set; }

    [Header("Adventurer Types")]
    [SerializeField] private List<AdventurerDefinition> adventurerTypes = new();

    [Header("Party Size")]
    [SerializeField] private int minPartySize = 1;
    [SerializeField] private int maxPartySize = 3;
    [Header("Grade scaling (matched teams)")]
    [Tooltip("Assessed rating per +1 adventurer level.")]
    [Min(1f)][SerializeField] private float gradeRatingPerLevel = 45f;
    [Tooltip("Assessed rating per +1 team member.")]
    [Min(1f)][SerializeField] private float gradeRatingPerExtraMember = 120f;
    [Tooltip("Chance a matched team arrives under-strength (fresh recruits).")]
    [Range(0f, 1f)][SerializeField] private float gradeUnderStrengthChance = 0.25f;
    [Tooltip("Levels dropped when a team rolls under-strength.")]
    [Min(0)][SerializeField] private int gradeUnderStrengthLevelDrop = 2;
    [Tooltip("Members dropped when a team rolls under-strength.")]
    [Min(0)][SerializeField] private int gradeUnderStrengthSizeDrop = 1;
    [SerializeField] private bool scalePartySizeWithNotoriety = false;

    [Header("Spawn Interval by Notoriety")]
    [SerializeField] private float intervalLow = 30f;
    [SerializeField] private float intervalMedium = 20f;
    [SerializeField] private float intervalHigh = 10f;
    [SerializeField] private float notorietyMediumThreshold = 25f;
    [SerializeField] private float notorietyHighThreshold = 75f;

    [Header("Behaviour Trait Weights")]
    [SerializeField] private float weightCautious = 2f;
    [SerializeField] private float weightBalanced = 4f;
    [SerializeField] private float weightAggressive = 2f;
    [SerializeField] private float weightCowardly = 1f;

    [Header("Intent Weights")]
    [Tooltip("Flat baseline weights before Notoriety/Reputation scaling. Delvers are " +
             "the common case; Destroyers stay rare until Notoriety climbs.")]
    [SerializeField] private float baseDelver = 6f;
    [SerializeField] private float baseDestroyer = 0.5f;
    [SerializeField] private float basePilgrim = 1.5f;
    [SerializeField] private float baseGiftGiver = 1f;
    [Tooltip("Per-point Notoriety added to the Destroyer weight.")]
    [SerializeField] private float notorietyToDestroyer = 0.03f;
    [Tooltip("Per-point Reputation added to the Pilgrim / Gift-Giver weights.")]
    [SerializeField] private float reputationToPilgrim = 0.04f;
    [SerializeField] private float reputationToGiftGiver = 0.02f;

    [Header("Type Weights")]
    [Tooltip("Flat weights WITHIN each intent category. The category is rolled first " +
             "(Notoriety/Reputation scaled, above), then a type is picked here.")]
    [SerializeField] private float weightDelver = 5f;          // Delver - the common adventurer
    [SerializeField] private float weightMercenary = 3f;       // Destroyer
    [SerializeField] private float weightHero = 1f;            // Destroyer (gated)
    [SerializeField] private float weightTreasureHunter = 3f;  // Delver — loot-focused, doesn't chase the core
    [SerializeField] private float weightCultist = 1f;         // Gift-Giver
    [SerializeField] private float weightPilgrim = 2f;         // Pilgrim
    [SerializeField] private float weightScholar = 1.5f;       // Pilgrim
    [SerializeField] private float weightSuicidal = 0.4f;      // Pilgrim (rare)
    [SerializeField] private float weightNoble = 1f;           // Pilgrim
    [SerializeField] private float weightInspector = 0.8f;     // Pilgrim (conditional)
    [Tooltip("Heroes only appear once Notoriety reaches this threshold.")]
    [SerializeField] private float heroNotorietyThreshold = 60f;
    [Tooltip("Master switch for Inspector spawns (later gated by the escalation system).")]
    [SerializeField] private bool inspectorEnabled = true;

    [Header("Party Composition")]
    [Tooltip("Mercenary guards escorting a Noble.")]
    [SerializeField] private int nobleGuardMin = 2;
    [SerializeField] private int nobleGuardMax = 3;
    [Tooltip("Scholars per Scholar party + their mercenary guards.")]
    [SerializeField] private int scholarMin = 1;
    [SerializeField] private int scholarMax = 2;
    [SerializeField] private int scholarGuardMin = 1;
    [SerializeField] private int scholarGuardMax = 2;
    [Tooltip("Mercenary guards escorting an Inspector.")]
    [SerializeField] private int inspectorGuardMin = 1;
    [SerializeField] private int inspectorGuardMax = 2;
    [Tooltip("Optional dedicated (e.g. high-level) Mercenary-type definition for escort " +
             "guards. Falls back to the Mercenary type asset if unset. Keep it Mercenary-typed.")]
    [SerializeField] private AdventurerDefinition guardDef;

    [Header("Noble names + retaliation")]
    [Tooltip("Name pool for nobles and the vengeance parties their deaths summon.")]
    [SerializeField] private NobleNames nobleNames;
    [SerializeField] private int nobleRetaliationBaseGuards = 3;
    [SerializeField] private int nobleRetaliationGuardsPerLevel = 1;
    [SerializeField] private int nobleRetaliationMaxGuards = 6;
    [Header("Commoners")]
    [Tooltip("Smallest / largest loose group of curious commoners per spawn during the commoner stage.")]
    [Min(1)][SerializeField] private int commonerGroupMin = 1;
    [Min(1)][SerializeField] private int commonerGroupMax = 3;

    [Header("Tribute Bearers")]
    [Tooltip("TributeChest prefab. One member of each Pilgrim or Cultist party " +
             "carries it to the core and drops it there — or where they fall or flee.")]
    [SerializeField] private TributeChest tributeChestPrefab;
    [SerializeField] private int tributeGoldValue = 20;
    [Tooltip("Cultists bring the richest tribute of any type.")]
    [SerializeField] private int cultistTributeGoldValue = 50;
    [SerializeField] private float tributeAbsorbDelay = 1.5f;
    [Tooltip("Random scatter radius around the entrance for the tribute drop.")]
    [SerializeField] private float tributeScatter = 1.2f;

    [Header("Combat Classes")]
    [Tooltip("One CombatClassDefinition asset per class. Combatant members (Mercenary, " +
             "guards, Hero, Suicidal, Treasure Hunter) roll a class from this list; " +
             "non-combatants (worshippers / observers) stay plain Fighter.")]
    [SerializeField] private List<CombatClassDefinition> combatClasses = new();
    [Tooltip("Per-faction affinity spreads. Drives each adventurer's rolled affinity.")]
    [SerializeField] private AffinityProfiles affinityProfiles;
    [Tooltip("How strongly to favour role variety within a party. 0 = pure weighted " +
             "random (repeats common); higher = each class already present is " +
             "down-weighted, so varied parties dominate but odd comps still happen.")]
    [SerializeField] private float varietyBias = 2f;

    [Header("Organize / Formation")]
    [Tooltip("Base seconds a party pauses at the entrance to form up.")]
    [SerializeField] private float organizeBaseSeconds = 1.5f;
    [Tooltip("Extra organize seconds per member.")]
    [SerializeField] private float organizePerMember = 0.4f;
    [Tooltip("Random +/- jitter (seconds) so parties vary.")]
    [SerializeField] private float organizeJitter = 0.6f;

    private float timer = 0f;
    private bool transitPaused = false;

    // ── Concurrent-party cap ──────────────────────────────────────
    // One party at a time until the player opens a second floor; one per opened
    // floor thereafter. Event spawns (Hero dispatch) bypass the gate but still
    // occupy a slot, so natural waves hold while a Hero raids.
    private readonly List<AdventurerParty> liveParties = new();

    private void RegisterLiveParty(AdventurerParty party)
    {
        if (party != null) liveParties.Add(party);
    }

    /// <summary>Parties with at least one member still alive in the dungeon.
    /// Prunes finished parties (all dead, fled, or breached) as it counts.</summary>
    public int ActivePartyCount()
    {
        for (int i = liveParties.Count - 1; i >= 0; i--)
            if (liveParties[i] == null || liveParties[i].LiveCount() == 0)
                liveParties.RemoveAt(i);
        return liveParties.Count;
    }

    public int MaxConcurrentParties()
        => Mathf.Max(1, FloorManager.Instance != null ? FloorManager.Instance.VisitedFloorCount : 1);

    public bool PartyCapReached => ActivePartyCount() >= MaxConcurrentParties();

    // ── Read API for the wave-preview HUD (no behaviour change) ──
    public bool SpawningActive =>
        !PauseController.IsGamePaused
        && !transitPaused
        && DungeonEntrance.Instance != null
        && EntranceDiscovered
        && (DayNightCycle.Instance == null || !DayNightCycle.Instance.IsNight)
        && (WaveStageController.AllowAdventurers || WaveStageController.AllowCommoners);

    /// <summary>True when the seeded entrance has been found — or on legacy saves
    /// with a player-placed entrance, which have no seal to break.</summary>
    public bool EntranceDiscovered
    {
        get
        {
            var features = FloorManager.Instance?.GetFloor(0)?.FeatureGenerator;
            if (features == null || features.EntranceCave == null) return true;
            return features.IsEntranceDiscovered;
        }
    }

    /// <summary>The day of discovery is a grace period — word spreads overnight;
    /// the first wave arrives with the next dawn.</summary>
    public bool InGraceDay
    {
        get
        {
            var cave = FloorManager.Instance?.GetFloor(0)?.FeatureGenerator?.EntranceCave;
            if (cave == null || !cave.discovered || cave.discoveredDay < 0) return false;
            return DayNightCycle.Instance != null
                && DayNightCycle.Instance.CurrentDay <= cave.discoveredDay;
        }
    }

    public float SecondsUntilNextParty => Mathf.Max(0f, CurrentInterval() - timer);
    public int PredictedMinPartySize => minPartySize;
    public int PredictedMaxPartySize
    {
        get
        {
            if (!scalePartySizeWithNotoriety || DungeonCore.Instance == null) return maxPartySize;
            float t = Mathf.Clamp01(DungeonCore.Instance.Notoriety / notorietyHighThreshold);
            return Mathf.Max(minPartySize, Mathf.RoundToInt(Mathf.Lerp(minPartySize, maxPartySize, t)));
        }
    }

    private void OnEnable()
    {
        Instance = this;

        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted += HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
        }

        DungeonCoreTransit.OnTransitStarted += HandleTransitStarted;
        DungeonCoreTransit.OnTransitCompleted += HandleTransitCompleted;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;

        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted -= HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        }

        DungeonCoreTransit.OnTransitStarted -= HandleTransitStarted;
        DungeonCoreTransit.OnTransitCompleted -= HandleTransitCompleted;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (transitPaused) return;
        if (DungeonEntrance.Instance == null) return;
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) return;
        if (!EntranceDiscovered || InGraceDay) { timer = 0f; return; }
        if (!WaveStageController.AllowAdventurers && !WaveStageController.AllowCommoners) { timer = 0f; return; }
        // Matched teams hold until the first assessment (staged flow only); the Inspector
        // and any kill-team arrive via dedicated dispatches meanwhile.
        if (WaveStageController.Instance != null && WaveStageController.AllowAdventurers
            && GradeSystem.Instance != null && !GradeSystem.Instance.PlayerHasBeenAssessed) { timer = 0f; return; }

        timer += Time.deltaTime;
        if (timer >= CurrentInterval())
        {
            // Hold at the threshold while the dungeon is at capacity — the next
            // party steps in the moment a slot frees, no fresh interval.
            if (PartyCapReached)
            {
                timer = CurrentInterval();
                return;
            }
            timer = 0f;
            if (WaveStageController.AllowCommoners) SpawnCommonerParty();
            else SpawnParty();
        }
    }

    private void HandleNightStarted() { timer = 0f; }
    private void HandleDayStarted() { }

    private void HandleTransitStarted() { transitPaused = true; Debug.Log("[AdventurerSpawner] Paused for core transit."); }
    private void HandleTransitCompleted() { transitPaused = false; timer = 0f; Debug.Log("[AdventurerSpawner] Resumed after core transit."); }

    private float CurrentInterval()
    {
        if (DungeonCore.Instance == null) return intervalLow;
        float n = DungeonCore.Instance.Notoriety;
        if (n >= notorietyHighThreshold) return intervalHigh;
        if (n >= notorietyMediumThreshold) return intervalMedium;
        return intervalLow;
    }

    private void SpawnParty()
    {
        if (adventurerTypes == null || adventurerTypes.Count == 0)
        {
            Debug.LogError("[AdventurerSpawner] adventurerTypes is empty.");
            return;
        }

        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var returning = TrackedPartyRegistry.Instance?.TakeReturningParty();
        if (returning != null)
        {
            SpawnReturningParty(returning, spawnPos);
            return;
        }

        AdventurerType partyType = RollType();
        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(partyType));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        // Grade scaling: a matched team is levelled + sized to the assessed grade, with the
        // occasional under-strength party. One variance roll drives both level and size.
        int gradeLevel = 1, extraSize = 0;
        if (GradeSystem.Instance != null && GradeSystem.Instance.HasBeenAssessed)
        {
            float rating = GradeSystem.Instance.AssessedRating;
            gradeLevel = Mathf.Clamp(1 + Mathf.FloorToInt(rating / Mathf.Max(1f, gradeRatingPerLevel)), 1, LevelTierUtil.MaxFlatLevel);
            extraSize = Mathf.FloorToInt(rating / Mathf.Max(1f, gradeRatingPerExtraMember));
            if (Random.value < gradeUnderStrengthChance)
            {
                gradeLevel = Mathf.Max(1, gradeLevel - gradeUnderStrengthLevelDrop);
                extraSize = Mathf.Max(0, extraSize - gradeUnderStrengthSizeDrop);
            }
        }

        int spawned = SpawnComposition(partyType, spawnPos, party, extraSize);
        RunStats.Instance?.RecordPartySpawned(spawned);

        SetupOrganize(party, partyType, spawned, spawnPos);

        if (gradeLevel > 1)
            foreach (var m in party.LiveMembers) m.ApplyGradeLevel(gradeLevel);

        if (party.tracked) PartyBannerManager.Instance?.ShowBanner(party);

        // Tribute is no longer littered at the spawn point: one member of each
        // Pilgrim or Cultist party carries the chest to the core (assigned in
        // SpawnMember) and drops it on arrival — or where they fall or flee.

        Debug.Log($"[AdventurerSpawner] Spawned {spawned} adventurer(s) — type {partyType}, intent {party.Intent}.");
    }

    // ── Composition (Day 37) ─────────────────────────────────────

    private void SpawnCommonerParty()
    {
        var def = Def(AdventurerType.Commoner);
        if (def == null) return;

        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;
        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Commoner));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        int n = Random.Range(commonerGroupMin, commonerGroupMax + 1);
        var used = new Dictionary<CombatClass, int>();
        for (int i = 0; i < n; i++) SpawnMember(def, RollTrait(), spawnPos, party, used);

        RunStats.Instance?.RecordPartySpawned(n);
        SetupOrganize(party, AdventurerType.Commoner, n, spawnPos);
        // Not tracked: no war banner, no return. Just curious folk.
    }

    private AdventurerDefinition Def(AdventurerType t)
    {
        var d = adventurerTypes.Find(x => x != null && x.type == t);
        if (d == null) Debug.LogError($"[AdventurerSpawner] No AdventurerDefinition for type {t}.");
        return d;
    }

    /// <summary>Spawns a party's members for its type. Returns the member count.</summary>
    private int SpawnComposition(AdventurerType partyType, Vector3 spawnPos, AdventurerParty party, int extraSize)
    {
        // Day 39 — per-party class tally so the variety bias can favour role spread.
        var used = new Dictionary<CombatClass, int>();

        switch (partyType)
        {
            case AdventurerType.Noble:
                {
                    // A cowardly Noble (trait forced on the asset) escorted by hired muscle.
                    int count = 0;
                    var noble = Def(AdventurerType.Noble);
                    if (noble != null) { SpawnMember(noble, RollTrait(), spawnPos, party, used); count++; }
                    count += SpawnGuards(Random.Range(nobleGuardMin, nobleGuardMax + 1), spawnPos, party, used);
                    return count;
                }
            case AdventurerType.Scholar:
                {
                    // Passive scholars with a small protective guard.
                    int count = SpawnUniform(AdventurerType.Scholar, Random.Range(scholarMin, scholarMax + 1), spawnPos, party, used);
                    count += SpawnGuards(Random.Range(scholarGuardMin, scholarGuardMax + 1), spawnPos, party, used);
                    return count;
                }
            case AdventurerType.Inspector:
                {
                    int count = 0;
                    var insp = Def(AdventurerType.Inspector);
                    if (insp != null) { SpawnMember(insp, RollTrait(), spawnPos, party, used); count++; }
                    count += SpawnGuards(Random.Range(inspectorGuardMin, inspectorGuardMax + 1), spawnPos, party, used);
                    return count;
                }
            default:
                return SpawnUniform(partyType, RollPartySize() + Mathf.Max(0, extraSize), spawnPos, party, used);
        }
    }

    private int SpawnUniform(AdventurerType t, int n, Vector3 spawnPos, AdventurerParty party, Dictionary<CombatClass, int> used)
    {
        var def = Def(t);
        if (def == null) return 0;
        for (int i = 0; i < n; i++) SpawnMember(def, RollTrait(), spawnPos, party, used);
        return n;
    }

    private int SpawnGuards(int n, Vector3 spawnPos, AdventurerParty party, Dictionary<CombatClass, int> used)
    {
        // Guards are Mercenary-typed muscle (Destroyer goal). A dedicated high-level
        // guardDef is used if assigned, else the standard Mercenary type asset.
        var def = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        if (def == null) return 0;
        for (int i = 0; i < n; i++) SpawnMember(def, RollTrait(), spawnPos, party, used);
        return n;
    }

    private void SpawnMember(AdventurerDefinition def, BehaviourTrait trait, Vector3 spawnPos, AdventurerParty party, Dictionary<CombatClass, int> used,
        CombatClassDefinition forcedClass = null, string presetName = null, int returningXp = 0, string returningGrudge = null, DungeonType forcedAffinity = DungeonType.None)
    {
        if (def.prefab == null) { Debug.LogError($"[AdventurerSpawner] '{def.className}' has no prefab."); return; }

        var floor = FloorManager.Instance?.GetFloor(0);

        // Scatter for a natural cluster — but never off walkable ground. A member
        // spawned on unmined apron can neither path nor resolve, freezing the
        // party and holding its cap slot forever.
        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        Vector3 pos = spawnPos + new Vector3(scatter.x, scatter.y, 0f);
        if (floor != null && floor.TileInfluence != null)
        {
            var scatterCell = floor.TileInfluence.WorldToCell(pos);
            bool okGround = floor.TileInfluence.IsTileMined(scatterCell)
                || (floor.FeatureGenerator != null
                    && floor.FeatureGenerator.IsEntranceCave(scatterCell));
            if (!okGround) pos = spawnPos;
        }

        var adventurer = Instantiate(def.prefab, pos, Quaternion.identity);

        if (floor != null)
            adventurer.transform.SetParent(floor.transform, true);

        var classDef = forcedClass != null ? forcedClass : ResolveCombatClass(def.type, used);
        string name = presetName;
        if (string.IsNullOrEmpty(name) && def.named)
        {
            if (def.type == AdventurerType.Noble && nobleNames != null)
                name = nobleNames.Generate();
            else
                name = TrackedPartyRegistry.Instance != null ? TrackedPartyRegistry.Instance.GenerateName() : "Champion";
        }

        DungeonType memberAffinity = forcedAffinity != DungeonType.None ? forcedAffinity
            : affinityProfiles != null
                ? affinityProfiles.Roll(AdventurerTypeInfo.FactionOf(def.type), def.type, classDef)
                : DungeonType.None;
        adventurer.Initialise(def, trait, party, classDef, name, returningXp, returningGrudge, memberAffinity);
        adventurer.ApplyAffinityVisuals(affinityProfiles);

        // One bearer per Pilgrim/Cultist party carries the tribute to the core.
        if (party != null && !party.tributeAssigned
            && (def.type == AdventurerType.Pilgrim || def.type == AdventurerType.Cultist)
            && tributeChestPrefab != null)
        {
            party.tributeAssigned = true;
            int value = def.type == AdventurerType.Cultist ? cultistTributeGoldValue : tributeGoldValue;
            var chestSprite = tributeChestPrefab.GetComponent<SpriteRenderer>()?.sprite;
            adventurer.AssignTribute(value, tributeChestPrefab, tributeAbsorbDelay, tributeScatter, chestSprite);
        }
    }

    // ── Combat class assignment (Day 39) ─────────────────────────
    // Combatant types roll a class (variety-biased); non-combatants stay Fighter.

    private CombatClassDefinition ResolveCombatClass(AdventurerType type, Dictionary<CombatClass, int> used)
    {
        var g = AdventurerTypeInfo.GoalOf(type);
        bool combatant = g == AdventurerGoal.BreachCore
                      || g == AdventurerGoal.SeekDeath
                      || g == AdventurerGoal.LootAndLeave;
        return combatant ? RollClassDef(used) : null;
    }

    private CombatClassDefinition RollClassDef(Dictionary<CombatClass, int> used)
    {
        if (combatClasses == null || combatClasses.Count == 0) return null;

        // Weight = spawnWeight / (1 + varietyBias * timesAlreadyPicked). Down-weighting
        // (not excluding) keeps variety likely while still allowing odd comps.
        float total = 0f;
        foreach (var c in combatClasses)
        {
            if (c == null) continue;
            used.TryGetValue(c.combatClass, out int n);
            total += Mathf.Max(0f, c.spawnWeight) / (1f + varietyBias * n);
        }
        if (total <= 0f) return combatClasses[0];

        float roll = Random.Range(0f, total);
        foreach (var c in combatClasses)
        {
            if (c == null) continue;
            used.TryGetValue(c.combatClass, out int n);
            float w = Mathf.Max(0f, c.spawnWeight) / (1f + varietyBias * n);
            if (roll < w) { used[c.combatClass] = n + 1; return c; }
            roll -= w;
        }
        return combatClasses[0];
    }

    private int RollPartySize()
    {
        if (!scalePartySizeWithNotoriety || DungeonCore.Instance == null)
            return Random.Range(minPartySize, maxPartySize + 1);

        float t = Mathf.Clamp01(DungeonCore.Instance.Notoriety / notorietyHighThreshold);
        float maxLerp = Mathf.Lerp(minPartySize, maxPartySize, t);
        return Random.Range(minPartySize, Mathf.RoundToInt(maxLerp) + 1);
    }

    private BehaviourTrait RollTrait()
    {
        float total = weightCautious + weightBalanced + weightAggressive + weightCowardly;
        float roll = Random.Range(0f, total);
        if (roll < weightCautious) return BehaviourTrait.Cautious;
        if (roll < weightCautious + weightBalanced) return BehaviourTrait.Balanced;
        if (roll < weightCautious + weightBalanced + weightAggressive) return BehaviourTrait.Aggressive;
        return BehaviourTrait.Cowardly;
    }

    // ── Intent ───────────────────────────────────────────

    private PartyIntent RollIntent()
    {
        float noto = DungeonCore.Instance != null ? DungeonCore.Instance.Notoriety : 0f;
        float rep = DungeonCore.Instance != null ? DungeonCore.Instance.Reputation : 0f;

        float wDelver = Mathf.Max(0f, baseDelver);
        float wDestroyer = Mathf.Max(0f, baseDestroyer + noto * notorietyToDestroyer);
        float wPilgrim = Mathf.Max(0f, basePilgrim + rep * reputationToPilgrim);
        float wGiftGiver = Mathf.Max(0f, baseGiftGiver + rep * reputationToGiftGiver);

        float total = wDelver + wDestroyer + wPilgrim + wGiftGiver;
        if (total <= 0f) return PartyIntent.Delver;

        float roll = Random.Range(0f, total);
        if (roll < wDelver) return PartyIntent.Delver;
        roll -= wDelver;
        if (roll < wDestroyer) return PartyIntent.Destroyer;
        if (roll < wDestroyer + wPilgrim) return PartyIntent.Pilgrim;
        return PartyIntent.GiftGiver;
    }

    // ── Type Roll (Day 37) ───────────────────────────────────────
    // The party TYPE is the roll: the category (Pilgrim/GiftGiver/Destroyer) is
    // rolled first with the Day-35 Notoriety/Reputation scaling, then a concrete
    // type is picked within that category by flat weight (with gates).

    private AdventurerType RollType()
    {
        switch (RollIntent())
        {
            case PartyIntent.Delver: return RollDelverType();
            case PartyIntent.Destroyer: return RollDestroyerType();
            case PartyIntent.GiftGiver: return RollGiftGiverType();
            default: return RollPilgrimType();
        }
    }

    private AdventurerType RollDestroyerType()
    {
        float noto = DungeonCore.Instance != null ? DungeonCore.Instance.Notoriety : 0f;
        float wHero = noto >= heroNotorietyThreshold ? Mathf.Max(0f, weightHero) : 0f;
        float wMerc = Mathf.Max(0f, weightMercenary);
        float total = wMerc + wHero;
        if (total <= 0f) return AdventurerType.Mercenary;
        float roll = Random.Range(0f, total);
        if (roll < wHero) return AdventurerType.Hero;
        return AdventurerType.Mercenary;
    }

    private AdventurerType RollDelverType()
    {
        float wDel = Mathf.Max(0f, weightDelver);
        float wTH = Mathf.Max(0f, weightTreasureHunter);
        float total = wDel + wTH;
        if (total <= 0f) return AdventurerType.Delver;
        float roll = Random.Range(0f, total);
        if (roll < wTH) return AdventurerType.TreasureHunter;
        return AdventurerType.Delver;
    }

    private AdventurerType RollGiftGiverType()
    {
        // Cultists are the only gift-bearers by category; Treasure Hunters rolled
        // here historically, but thieves take — they don't give.
        return AdventurerType.Cultist;
    }

    private AdventurerType RollPilgrimType()
    {
        float wPil = Mathf.Max(0f, weightPilgrim);
        float wSch = Mathf.Max(0f, weightScholar);
        float wSui = Mathf.Max(0f, weightSuicidal);
        float wNob = Mathf.Max(0f, weightNoble);
        float wIns = inspectorEnabled ? Mathf.Max(0f, weightInspector) : 0f;
        float total = wPil + wSch + wSui + wNob + wIns;
        if (total <= 0f) return AdventurerType.Pilgrim;

        float roll = Random.Range(0f, total);
        if (roll < wPil) return AdventurerType.Pilgrim;
        roll -= wPil; if (roll < wSch) return AdventurerType.Scholar;
        roll -= wSch; if (roll < wSui) return AdventurerType.Suicidal;
        roll -= wSui; if (roll < wNob) return AdventurerType.Noble;
        return AdventurerType.Inspector;
    }

    // ── Organize / formation ────────────────────────────

    private void SetupOrganize(AdventurerParty party, AdventurerType partyType, int count, Vector3 spawnPos)
    {
        FormationType formation = FormationFor(partyType);
        party.Formation = formation;
        if (formation == FormationType.None) return;

        // Advance direction: entrance -> core (fallback right).
        Vector2 dir = Vector2.right;
        if (DungeonCore.Instance != null)
        {
            Vector2 d = (Vector2)(DungeonCore.Instance.transform.position - spawnPos);
            if (d.sqrMagnitude > 0.01f) dir = d.normalized;
        }
        party.AdvanceDir = dir;

        // Duration: size-scaled, temperament-scaled, jittered.
        float seconds = organizeBaseSeconds + organizePerMember * count;
        seconds *= TemperamentMultiplier(RollTrait());
        seconds += Random.Range(-organizeJitter, organizeJitter);
        party.OrganizeEndTime = Time.time + Mathf.Max(0.3f, seconds);
    }

    private static FormationType FormationFor(AdventurerType t)
    {
        switch (t)
        {
            case AdventurerType.Mercenary:
            case AdventurerType.Hero:
                return FormationType.Assault;
            case AdventurerType.Noble:
            case AdventurerType.Scholar:
            case AdventurerType.Inspector:
                return FormationType.Escort;
            default:
                return FormationType.None;   // Pilgrim, Cultist, Suicidal, TreasureHunter
        }
    }

    private static float TemperamentMultiplier(BehaviourTrait t) => t switch
    {
        BehaviourTrait.Aggressive => 0.7f,   // charge in
        BehaviourTrait.Cautious => 1.3f,     // form up carefully
        _ => 1f,
    };

    [ContextMenu("Force Spawn Party Now")]
    public void ForceSpawnParty() { timer = 0f; SpawnParty(); }
    public void ForceSpawnCommonerParty() { timer = 0f; SpawnCommonerParty(); }

    /// <summary>Dispatch the Guild's Inspector (plus a small escort) as a scheduled
    /// assessment. Driven by InspectorAssessor.</summary>
    public void DispatchInspectorParty()
    {
        if (DungeonEntrance.Instance == null) return;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var insp = Def(AdventurerType.Inspector);
        if (insp == null) return;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Inspector));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        SpawnMember(insp, RollTrait(), spawnPos, party, used);
        int guards = SpawnGuards(Random.Range(inspectorGuardMin, inspectorGuardMax + 1), spawnPos, party, used);

        SetupOrganize(party, AdventurerType.Inspector, 1 + guards, spawnPos);
        RunStats.Instance?.RecordPartySpawned(1 + guards);
    }

    /// <summary>Dispatch a Hero kill-team to investigate a slain Inspector. Returns the
    /// party so the assessor can watch for its departure.</summary>
    public AdventurerParty DispatchInvestigationTeam(int guardCount)
    {
        if (DungeonEntrance.Instance == null) return null;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var heroDef = Def(AdventurerType.Hero);
        if (heroDef == null) return null;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        SpawnMember(heroDef, RollTrait(), spawnPos, party, used);

        var guardBase = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        if (guardBase != null)
            for (int i = 0; i < guardCount; i++)
                SpawnMember(guardBase, RollTrait(), spawnPos, party, used);

        SetupOrganize(party, AdventurerType.Hero, 1 + guardCount, spawnPos);
        RunStats.Instance?.RecordPartySpawned(1 + guardCount);
        PartyBannerManager.Instance?.ShowBanner(party);
        return party;
    }

    // ── Live-party restore ──────────────────────────────
    /// <summary>Recreates every in-dungeon party from a mid-raid save. Runs before floor
    /// objects restore, and each living member registers with its floor at once so trap
    /// re-arming correctly skips floors that still hold a party.</summary>
    public void RestoreLiveParties(List<LivePartySaveData> saves)
    {
        if (saves == null) return;
        foreach (var s in saves)
        {
            if (s == null || s.members == null || s.members.Count == 0) continue;

            var party = new AdventurerParty((PartyIntent)s.intent);
            RegisterLiveParty(party);
            TrackedPartyRegistry.Instance?.RegisterActive(party);
            party.ApplyRestoredState(s);

            foreach (var rec in s.members)
            {
                if (rec.isLive) SpawnRestoredMember(rec, party);
                else party.AddResolvedMember(rec);
            }

            if (party.tracked) PartyBannerManager.Instance?.ShowBanner(party);
        }
    }

    private void SpawnRestoredMember(LiveMemberSaveData rec, AdventurerParty party)
    {
        var def = Def((AdventurerType)rec.type);
        if (def == null || def.prefab == null) return;

        var floor = FloorManager.Instance?.GetFloor(rec.floorIndex) ?? FloorManager.Instance?.GetFloor(0);
        if (floor == null) return;

        var adventurer = Instantiate(def.prefab, rec.position.ToVector3(), Quaternion.identity);
        adventurer.transform.SetParent(floor.transform, true);

        var classDef = ClassDefFor((CombatClass)rec.combatClass);
        adventurer.Initialise(def, (BehaviourTrait)rec.trait, party, classDef, rec.name, rec.xp, rec.returnGrudge, (DungeonType)rec.affinity);
        adventurer.ApplyAffinityVisuals(affinityProfiles);

        // Register with the floor now (not in the deferred Start) so the trap-reset pass
        // sees the party and leaves this floor's traps as they were saved.
        floor.Entities?.Register(adventurer);

        adventurer.ApplyLiveState(rec);

        if (rec.tributeValue > 0 && tributeChestPrefab != null)
        {
            party.tributeAssigned = true;
            var chestSprite = tributeChestPrefab.GetComponent<SpriteRenderer>()?.sprite;
            adventurer.AssignTribute(rec.tributeValue, tributeChestPrefab, tributeAbsorbDelay, tributeScatter, chestSprite);
        }
    }

    /// <summary>Spawns a single Hero at the entrance (Inspector-escalation response).</summary>
    public void DispatchHeroParty()
    {
        if (DungeonEntrance.Instance == null) return;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var hero = Def(AdventurerType.Hero);
        if (hero == null) return;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);
        var used = new Dictionary<CombatClass, int>();
        SpawnMember(hero, RollTrait(), spawnPos, party, used);

        SetupOrganize(party, AdventurerType.Hero, 1, spawnPos);
        RunStats.Instance?.RecordPartySpawned(1);

        PartyBannerManager.Instance?.ShowBanner(party);
    }

    /// <summary>Dispatch a Holy Order crusade: an ordained Hero leading Paladins (Tank)
    /// and Clerics, all forced to Light so they read as a holy strike via the flavour
    /// names. Fired by HolyOrderStrike when the core is dark and infamous.</summary>
    public void DispatchHolyOrderStrike(int guardCount)
    {
        if (DungeonEntrance.Instance == null) return;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var heroDef = Def(AdventurerType.Hero);
        if (heroDef == null) return;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        // Ordained Hero, forced Light.
        SpawnMember(heroDef, RollTrait(), spawnPos, party, used, forcedAffinity: DungeonType.Light);

        // Guards: alternating Paladins (Tank) and Clerics, all Light.
        var guardBase = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        var tankClass = ClassDefFor(CombatClass.Tank);
        var clericClass = ClassDefFor(CombatClass.Cleric);
        if (guardBase != null)
            for (int i = 0; i < guardCount; i++)
            {
                var cls = (i % 2 == 0) ? tankClass : clericClass;
                SpawnMember(guardBase, RollTrait(), spawnPos, party, used,
                            forcedClass: cls, forcedAffinity: DungeonType.Light);
            }

        SetupOrganize(party, AdventurerType.Hero, 1 + guardCount, spawnPos);
        RunStats.Instance?.RecordPartySpawned(1 + guardCount);
        PartyBannerManager.Instance?.ShowBanner(party);
    }

    /// <summary>Dispatch a Mercenary Company reprisal: a band of sellswords fronted
    /// by a Tank, untinted (economic, not ideological - no forced affinity, no ordained
    /// hero). Fired by MercenaryContract when too much treasure has left the dungeon.</summary>
    public void DispatchMercenaryAssault(int mercCount)
    {
        if (DungeonEntrance.Instance == null) return;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var mercDef = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        if (mercDef == null) return;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Mercenary));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        var tankClass = ClassDefFor(CombatClass.Tank);

        int total = Mathf.Max(1, mercCount);
        for (int i = 0; i < total; i++)
        {
            // The lead sellsword fronts the band as a Tank; the rest roll their own class.
            var cls = (i == 0) ? tankClass : null;
            SpawnMember(mercDef, RollTrait(), spawnPos, party, used, forcedClass: cls);
        }

        SetupOrganize(party, AdventurerType.Mercenary, total, spawnPos);
        RunStats.Instance?.RecordPartySpawned(total);
        PartyBannerManager.Instance?.ShowBanner(party);
    }

    /// <summary>Dispatch a slain noble house's vengeance: a Destroyer party led by a named
    /// kinsman of the house, backed by a Tank-fronted retinue, scaled by nobles slain this
    /// run. Fired by NobleRetaliation after the grievance delay.</summary>
    public void DispatchNobleRetaliation(string house, int level)
    {
        if (DungeonEntrance.Instance == null) return;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        var heroDef = Def(AdventurerType.Hero);
        if (heroDef == null) return;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);
        party.bannerLabelOverride = $"House {house}";

        var used = new Dictionary<CombatClass, int>();

        // The champion: a named kinsman of the fallen house, leading the reprisal.
        string championName = nobleNames != null ? nobleNames.GenerateWithHouse(house) : null;
        SpawnMember(heroDef, RollTrait(), spawnPos, party, used, presetName: championName);

        // Retinue: Tank-fronted guards, more of them the deeper the grudge.
        int retinue = Mathf.Clamp(
            nobleRetaliationBaseGuards + (level - 1) * nobleRetaliationGuardsPerLevel,
            nobleRetaliationBaseGuards, nobleRetaliationMaxGuards);
        var guardBase = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        var tankClass = ClassDefFor(CombatClass.Tank);
        if (guardBase != null)
            for (int i = 0; i < retinue; i++)
            {
                var cls = (i == 0) ? tankClass : null;
                SpawnMember(guardBase, RollTrait(), spawnPos, party, used, forcedClass: cls);
            }

        // Escalated: a grade level raises every member's stats.
        int grade = Mathf.Clamp(level, 1, LevelTierUtil.MaxFlatLevel);
        if (grade > 1)
            foreach (var m in party.LiveMembers) m.ApplyGradeLevel(grade);

        SetupOrganize(party, AdventurerType.Hero, party.Members.Count, spawnPos);
        RunStats.Instance?.RecordPartySpawned(party.Members.Count);
        PartyBannerManager.Instance?.ShowBanner(party);
    }

    /// <summary>The Grand Crusade - the Church climax. A named ordained Paladin leads a
    /// large host of Paladins (Tank) and Clerics, all Light. Fired by EndgameClimax.</summary>
    public AdventurerParty DispatchClimaxCrusade(int guardCount)
    {
        if (DungeonEntrance.Instance == null) return null;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;
        var heroDef = Def(AdventurerType.Hero);
        if (heroDef == null) return null;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        party.isClimax = true;
        party.bannerLabelOverride = "The Grand Crusade";
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        SpawnMember(heroDef, RollTrait(), spawnPos, party, used, forcedAffinity: DungeonType.Light);

        var guardBase = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        var tankClass = ClassDefFor(CombatClass.Tank);
        var clericClass = ClassDefFor(CombatClass.Cleric);
        if (guardBase != null)
            for (int i = 0; i < guardCount; i++)
            {
                var cls = (i % 2 == 0) ? tankClass : clericClass;
                SpawnMember(guardBase, RollTrait(), spawnPos, party, used,
                            forcedClass: cls, forcedAffinity: DungeonType.Light);
            }

        SetupOrganize(party, AdventurerType.Hero, 1 + guardCount, spawnPos);
        RunStats.Instance?.RecordPartySpawned(1 + guardCount);
        PartyBannerManager.Instance?.ShowBanner(party);
        return party;
    }

    /// <summary>The Iron Host - the Mercenary climax. A large sellsword army with a heavy
    /// Tank front (every third a Tank). Fired by EndgameClimax.</summary>
    public AdventurerParty DispatchClimaxArmy(int mercCount)
    {
        if (DungeonEntrance.Instance == null) return null;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;
        var mercDef = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        if (mercDef == null) return null;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Mercenary));
        party.isClimax = true;
        party.bannerLabelOverride = "The Iron Host";
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        var tankClass = ClassDefFor(CombatClass.Tank);
        int total = Mathf.Max(1, mercCount);
        for (int i = 0; i < total; i++)
        {
            var cls = (i % 3 == 0) ? tankClass : null;
            SpawnMember(mercDef, RollTrait(), spawnPos, party, used, forcedClass: cls);
        }

        SetupOrganize(party, AdventurerType.Mercenary, total, spawnPos);
        RunStats.Instance?.RecordPartySpawned(total);
        PartyBannerManager.Instance?.ShowBanner(party);
        return party;
    }

    /// <summary>The King's Host - the crown's answer for slain nobles. Hero-heavy: several
    /// named Heroes fronted by a Tank-led royal guard - the largest raw Hero count of the
    /// climaxes. Fired by EndgameClimax.</summary>
    public AdventurerParty DispatchClimaxRoyalHost(int heroCount, int guardCount)
    {
        if (DungeonEntrance.Instance == null) return null;
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;
        var heroDef = Def(AdventurerType.Hero);
        if (heroDef == null) return null;

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(AdventurerType.Hero));
        party.isClimax = true;
        party.bannerLabelOverride = "The King's Host";
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);

        var used = new Dictionary<CombatClass, int>();
        int heroes = Mathf.Max(1, heroCount);
        for (int i = 0; i < heroes; i++)
            SpawnMember(heroDef, RollTrait(), spawnPos, party, used);

        var guardBase = guardDef != null ? guardDef : Def(AdventurerType.Mercenary);
        var tankClass = ClassDefFor(CombatClass.Tank);
        if (guardBase != null)
            for (int i = 0; i < guardCount; i++)
            {
                var cls = (i == 0) ? tankClass : null;
                SpawnMember(guardBase, RollTrait(), spawnPos, party, used, forcedClass: cls);
            }

        SetupOrganize(party, AdventurerType.Hero, heroes + guardCount, spawnPos);
        RunStats.Instance?.RecordPartySpawned(heroes + guardCount);
        PartyBannerManager.Instance?.ShowBanner(party);
        return party;
    }

    private CombatClassDefinition ClassDefFor(CombatClass c)
    {
        if (combatClasses == null) return null;
        foreach (var cd in combatClasses)
            if (cd != null && cd.combatClass == c) return cd;
        return null;
    }

    /// <summary>Re-deploys a tracked party: survivors return as their exact selves;
    /// fallen members are replaced by a fresh roll of the same type.</summary>
    private void SpawnReturningParty(TrackedParty record, Vector3 spawnPos)
    {
        if (record == null || record.members.Count == 0) return;

        AdventurerType primary = (AdventurerType)record.members[0].type;
        string leadName = null;
        foreach (var m in record.members)
            if (m.named) { primary = (AdventurerType)m.type; leadName = m.name; break; }

        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(primary));
        RegisterLiveParty(party);
        TrackedPartyRegistry.Instance?.RegisterActive(party);
        var used = new Dictionary<CombatClass, int>();

        foreach (var m in record.members)
        {
            var type = (AdventurerType)m.type;
            var def = Def(type);
            if (def == null) continue;

            if (m.survived)
                SpawnMember(def, RollTrait(), spawnPos, party, used,
                            ClassDefFor((CombatClass)m.combatClass), m.name, m.xp, m.grudgeMonster);
            else
                SpawnMember(def, RollTrait(), spawnPos, party, used);
        }

        SetupOrganize(party, primary, party.Members.Count, spawnPos);
        RunStats.Instance?.RecordPartySpawned(party.Members.Count);

        party.bannerColorIndex = record.bannerColorIndex;
        PartyBannerManager.Instance?.ShowBanner(party);

        string grudge = null;
        foreach (var m in record.members)
            if (m.survived && !string.IsNullOrEmpty(m.grudgeMonster)) { grudge = m.grudgeMonster; break; }

        string who = !string.IsNullOrEmpty(leadName) ? leadName : "A familiar party";
        string line = string.IsNullOrEmpty(grudge)
            ? $"{who} returns to the dungeon."
            : $"{who} returns — and remembers the {grudge} that drew their blood.";
        AlertsLog.Instance?.AddAlert(line, spawnPos, -1, AlertCategory.Threat);
    }
}