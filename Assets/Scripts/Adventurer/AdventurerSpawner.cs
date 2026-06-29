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
    [Tooltip("Flat baseline weights before Notoriety/Reputation scaling. " +
             "Keep Destroyer dominant so most raids stay hostile.")]
    [SerializeField] private float baseDestroyer = 2f;
    [SerializeField] private float basePilgrim = 1f;
    [SerializeField] private float baseGiftGiver = 1f;
    [Tooltip("Per-point Notoriety added to the Destroyer weight.")]
    [SerializeField] private float notorietyToDestroyer = 0.03f;
    [Tooltip("Per-point Reputation added to the Pilgrim / Gift-Giver weights.")]
    [SerializeField] private float reputationToPilgrim = 0.04f;
    [SerializeField] private float reputationToGiftGiver = 0.02f;

    [Header("Type Weights")]
    [Tooltip("Flat weights WITHIN each intent category. The category is rolled first " +
             "(Notoriety/Reputation scaled, above), then a type is picked here.")]
    [SerializeField] private float weightMercenary = 3f;       // Destroyer
    [SerializeField] private float weightHero = 1f;            // Destroyer (gated)
    [SerializeField] private float weightTreasureHunter = 3f;  // Gift-Giver
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

    [Header("Gift-Giver Tribute")]
    [Tooltip("TributeChest prefab dropped near the entrance by a Gift-Giver party.")]
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
    [Tooltip("How strongly to favour role variety within a party. 0 = pure weighted " +
             "random (repeats common); higher = each class already present is " +
             "down-weighted, so varied parties dominate but odd comps still happen.")]
    [SerializeField] private float varietyBias = 2f;

    private float timer = 0f;
    private bool transitPaused = false;

    // ── Read API for the wave-preview HUD (no behaviour change) ──
    public bool SpawningActive =>
        !PauseController.IsGamePaused
        && !transitPaused
        && DungeonEntrance.Instance != null
        && (DayNightCycle.Instance == null || !DayNightCycle.Instance.IsNight);

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

        timer += Time.deltaTime;
        if (timer >= CurrentInterval())
        {
            timer = 0f;
            SpawnParty();
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

        AdventurerType partyType = RollType();
        var party = new AdventurerParty(AdventurerTypeInfo.IntentOf(partyType));

        int spawned = SpawnComposition(partyType, spawnPos, party);
        RunStats.Instance?.RecordPartySpawned(spawned);

        if (party.Intent == PartyIntent.GiftGiver)
            DropTribute(spawnPos, partyType);

        Debug.Log($"[AdventurerSpawner] Spawned {spawned} adventurer(s) — type {partyType}, intent {party.Intent}.");
    }

    // ── Composition (Day 37) ─────────────────────────────────────

    private AdventurerDefinition Def(AdventurerType t)
    {
        var d = adventurerTypes.Find(x => x != null && x.type == t);
        if (d == null) Debug.LogError($"[AdventurerSpawner] No AdventurerDefinition for type {t}.");
        return d;
    }

    /// <summary>Spawns a party's members for its type. Returns the member count.</summary>
    private int SpawnComposition(AdventurerType partyType, Vector3 spawnPos, AdventurerParty party)
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
                return SpawnUniform(partyType, RollPartySize(), spawnPos, party, used);
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

    private void SpawnMember(AdventurerDefinition def, BehaviourTrait trait, Vector3 spawnPos, AdventurerParty party, Dictionary<CombatClass, int> used)
    {
        if (def.prefab == null) { Debug.LogError($"[AdventurerSpawner] '{def.className}' has no prefab."); return; }

        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        Vector3 pos = spawnPos + new Vector3(scatter.x, scatter.y, 0f);

        var adventurer = Instantiate(def.prefab, pos, Quaternion.identity);

        var floor = FloorManager.Instance?.GetFloor(0);
        if (floor != null)
            adventurer.transform.SetParent(floor.transform, true);

        adventurer.Initialise(def, trait, party, ResolveCombatClass(def.type, used));
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

        float wDestroyer = Mathf.Max(0f, baseDestroyer + noto * notorietyToDestroyer);
        float wPilgrim = Mathf.Max(0f, basePilgrim + rep * reputationToPilgrim);
        float wGiftGiver = Mathf.Max(0f, baseGiftGiver + rep * reputationToGiftGiver);

        float total = wDestroyer + wPilgrim + wGiftGiver;
        if (total <= 0f) return PartyIntent.Destroyer;

        float roll = Random.Range(0f, total);
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
        if (Random.Range(0f, total) < wHero) return AdventurerType.Hero;
        return AdventurerType.Mercenary;
    }

    private AdventurerType RollGiftGiverType()
    {
        float wTH = Mathf.Max(0f, weightTreasureHunter);
        float wCult = Mathf.Max(0f, weightCultist);
        float total = wTH + wCult;
        if (total <= 0f) return AdventurerType.TreasureHunter;
        if (Random.Range(0f, total) < wCult) return AdventurerType.Cultist;
        return AdventurerType.TreasureHunter;
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

    private void DropTribute(Vector3 entrancePos, AdventurerType partyType)
    {
        if (tributeChestPrefab == null)
        {
            Debug.LogWarning("[AdventurerSpawner] Gift-Giver party but no tributeChestPrefab assigned.");
            return;
        }

        Vector2 scatter = Random.insideUnitCircle * tributeScatter;
        Vector3 pos = entrancePos + new Vector3(scatter.x, scatter.y, 0f);

        var tribute = Instantiate(tributeChestPrefab, pos, Quaternion.identity);

        var floor = FloorManager.Instance?.GetFloor(0);
        if (floor != null)
            tribute.transform.SetParent(floor.transform, true);

        int value = partyType == AdventurerType.Cultist ? cultistTributeGoldValue : tributeGoldValue;
        tribute.Initialise(value, tributeAbsorbDelay);
    }

    [ContextMenu("Force Spawn Party Now")]
    public void ForceSpawnParty() { timer = 0f; SpawnParty(); }
}