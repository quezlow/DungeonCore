using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world's weather: small random events - a murrain, a pilgrim surge, a
/// tremor - rolled at dawn from data, so a new event is an asset entry rather
/// than a component. The four bespoke recurring threats (HolyOrderStrike,
/// MercenaryContract, NobleRetaliation, WildMonsterEvent) stay exactly as
/// shipped: each is a tuned state machine of its own, and folding them into a
/// generic registry would rewrite tuned behaviour for no player-visible gain.
/// The Wandering Merchant likewise keeps its own arrival controller.
///
/// Dawn sequence (Tools/sim_world_events.py mirrors this exactly - rerun it
/// whenever this ordering or the tuning defaults change):
///   1. tick active timed effects (decrement, expire, recompute multipliers)
///   2. decrement the global cooldown; while it holds, nothing rolls
///   3. gather eligible events (gates, per-event cooldown, not already
///      active; climax suppression strips HOSTILE events only)
///   4. roll the daily fire chance; on success draw one event by weight
///   5. fire: instant effects apply at once, timed effects join the active
///      list; the event's own cooldown and the global cooldown re-arm
///
/// Consumers read the two cached statics: RespawnTicker multiplies
/// RespawnRateMultiplier into its per-spawner tick, and AdventurerSpawner
/// multiplies CivilianWeightMultiplier beside the appeal ledger's civilian
/// multiplier at both intent-weight sites (roll + foresight, kept in sync so
/// WavePreviewHUD stays honest). Statics default to 1 with no instance, so
/// the hooks are inert until events exist.
///
/// No autosave on fire: these are weather, not assaults. The threat
/// components autosave because a raid is a run-defining moment; a two-day
/// pilgrim surge is not.
///
/// SCENE SETUP: put this on the persistent manager GameObject alongside the
/// threat managers. Event assets live under Resources/Events/World (authored
/// by Dungeon Core -> Generate World Events); the director self-populates.
/// </summary>
public class WorldEventDirector : MonoBehaviour
{
    public static WorldEventDirector Instance { get; private set; }

    // -- Consumer statics (default 1 = no instance, no active effects) --
    public static float RespawnRateMultiplier { get; private set; } = 1f;
    public static float CivilianWeightMultiplier { get; private set; } = 1f;

    [Header("Cadence")]
    [Tooltip("Chance each eligible dawn that some event fires. Tuned with " +
             "Tools/sim_world_events.py: 0.25 with a 3-day global cooldown " +
             "lands 4-5 events per 30 eligible days.")]
    [Range(0f, 1f)][SerializeField] private float dailyFireChance = 0.25f;
    [Tooltip("Dawns of quiet after ANY event before another can fire.")]
    [Min(0)][SerializeField] private int globalCooldownDays = 3;

    private readonly List<WorldEventDefinition> events = new();
    private readonly Dictionary<string, int> lastFiredDay = new();
    private readonly Dictionary<string, int> timesFired = new();
    // Parallel lists rather than a dictionary so the save shape below can
    // mirror them directly (JsonUtility cannot serialise dictionaries).
    private readonly List<string> activeIds = new();
    private readonly List<int> activeDaysRemaining = new();
    private readonly List<WorldEventDefinition> pool = new();   // dawn scratch buffer
    private int globalCooldown;
    private bool subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        events.Clear();
        events.AddRange(Resources.LoadAll<WorldEventDefinition>("Events/World"));
    }

    private void Start()
    {
        if (subscribed || DayNightCycle.Instance == null) return;
        DayNightCycle.Instance.OnDayStarted += OnDawn;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= OnDawn;
        if (Instance == this)
        {
            Instance = null;
            // Statics must not outlive the instance into another scene.
            RespawnRateMultiplier = 1f;
            CivilianWeightMultiplier = 1f;
        }
    }

    // -- Dawn ------------------------------------------------------

    private void OnDawn()
    {
        TickActiveEffects();

        if (globalCooldown > 0) { globalCooldown--; return; }

        bool suppress = EndgameClimax.Instance != null
                        && EndgameClimax.Instance.SuppressMidGameThreats;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;

        // Literal mirror of the sim's dawn: gather the pool, roll the daily
        // chance, then one weighted draw. Kept structurally identical to
        // Tools/sim_world_events.py on purpose - auditable over clever.
        pool.Clear();
        float totalWeight = 0f;
        foreach (var def in events)
        {
            if (def == null || !Eligible(def, day, suppress)) continue;
            if (def.weight <= 0f) continue;
            pool.Add(def);
            totalWeight += def.weight;
        }
        if (pool.Count == 0 || totalWeight <= 0f) return;

        if (UnityEngine.Random.value >= dailyFireChance) return;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        WorldEventDefinition pick = pool[pool.Count - 1];
        foreach (var def in pool)
        {
            if (roll < def.weight) { pick = def; break; }
            roll -= def.weight;
        }
        Fire(pick, day);
    }

    private bool Eligible(WorldEventDefinition def, int day, bool suppress)
    {
        if (suppress && def.hostile) return false;
        if (day < def.minDay) return false;
        if (def.minNotoriety > 0f)
        {
            var core = DungeonCore.Instance;
            if (core == null || core.Notoriety < def.minNotoriety) return false;
        }
        if (def.minRating > 0f)
        {
            if (DungeonRating.Instance == null
                || DungeonRating.Instance.CurrentRating < def.minRating) return false;
        }
        if (activeIds.Contains(def.Id)) return false;
        if (lastFiredDay.TryGetValue(def.Id, out int last)
            && day - last < Mathf.Max(1, def.cooldownDays)) return false;
        if (def.effectKind == WorldEventEffectKind.BeginPilgrimage
            && !DwarvenPilgrimageController.CanBegin)
        {
            // A journey is not a timed effect, so the active list cannot
            // police the overlap; the controller's departure gate does
            // (canon 51). Print Road Journeys prints the refusal in words.
            return false;
        }
        return true;
    }

    private void Fire(WorldEventDefinition def, int day)
    {
        switch (def.effectKind)
        {
            case WorldEventEffectKind.RespawnRate:
            case WorldEventEffectKind.CivilianWeight:
                activeIds.Add(def.Id);
                activeDaysRemaining.Add(Mathf.Max(1, def.durationDays));
                RecomputeMultipliers();
                break;
            case WorldEventEffectKind.GrantGold:
                DungeonCore.Instance?.AddGold(
                    UnityEngine.Random.Range(def.goldMin, def.goldMax + 1));
                break;
            case WorldEventEffectKind.BeginPilgrimage:
                // Eligible consulted CanBegin at dawn; the controller
                // re-checks and counts a rare late failure as a fizzle.
                DwarvenPilgrimageController.Instance?.BeginFromEvent();
                break;
            case WorldEventEffectKind.None:
            default:
                break;
        }

        lastFiredDay[def.Id] = day;
        timesFired[def.Id] = timesFired.TryGetValue(def.Id, out int n) ? n + 1 : 1;
        globalCooldown = Mathf.Max(0, globalCooldownDays);

        if (!string.IsNullOrEmpty(def.alertMessage))
            AlertsLog.Instance?.AddAlert(def.alertMessage, Vector3.zero, 0,
                def.alertCategory, def.alertSeverity);
        Debug.Log($"[WorldEventDirector] Day {day}: '{def.Id}' fired " +
                  $"(kind {def.effectKind}, fires {timesFired[def.Id]}).");
    }

    private void TickActiveEffects()
    {
        bool changed = false;
        for (int i = activeIds.Count - 1; i >= 0; i--)
        {
            activeDaysRemaining[i]--;
            if (activeDaysRemaining[i] > 0) continue;
            Debug.Log($"[WorldEventDirector] '{activeIds[i]}' has run its course.");
            activeIds.RemoveAt(i);
            activeDaysRemaining.RemoveAt(i);
            changed = true;
        }
        if (changed) RecomputeMultipliers();
    }

    private WorldEventDefinition FindDefinition(string id)
    {
        foreach (var def in events)
            if (def != null && def.Id == id) return def;
        return null;
    }

    private void RecomputeMultipliers()
    {
        float respawn = 1f, civilian = 1f;
        foreach (var id in activeIds)
        {
            var def = FindDefinition(id);
            if (def == null) continue;
            if (def.effectKind == WorldEventEffectKind.RespawnRate)
                respawn *= Mathf.Max(0f, def.magnitude);
            else if (def.effectKind == WorldEventEffectKind.CivilianWeight)
                civilian *= Mathf.Max(0f, def.magnitude);
        }
        RespawnRateMultiplier = respawn;
        CivilianWeightMultiplier = civilian;
    }

    // -- Diagnostics -----------------------------------------------

    /// <summary>Headless state report ("Print World Events" in Commands).</summary>
    public static void PrintState()
    {
        var d = Instance;
        if (d == null) { Debug.Log("[WorldEventDirector] No instance in scene."); return; }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[WorldEventDirector] {d.events.Count} event(s) loaded, " +
                      $"global cooldown {d.globalCooldown}, " +
                      $"respawn x{RespawnRateMultiplier:0.##}, " +
                      $"civilian x{CivilianWeightMultiplier:0.##}.");
        foreach (var def in d.events)
        {
            if (def == null) continue;
            d.lastFiredDay.TryGetValue(def.Id, out int last);
            d.timesFired.TryGetValue(def.Id, out int n);
            int idx = d.activeIds.IndexOf(def.Id);
            string active = idx >= 0 ? $", ACTIVE {d.activeDaysRemaining[idx]}d left" : "";
            sb.AppendLine($"  {def.Id}: fired {n}x, last day {last}{active}");
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>Fresh dungeon, fresh weather. Wired in
    /// DungeonSaveController.InitializeNewGame beside the merchant's reset -
    /// the director carries scheduling state exactly as the merchant does.</summary>
    public static void ResetForNewGame()
    {
        RespawnRateMultiplier = 1f;
        CivilianWeightMultiplier = 1f;
        var d = Instance;
        if (d == null) return;
        d.lastFiredDay.Clear();
        d.timesFired.Clear();
        d.activeIds.Clear();
        d.activeDaysRemaining.Clear();
        d.globalCooldown = 0;
    }

    // -- Save / Load -----------------------------------------------

    public WorldEventsSaveData GetSaveData()
    {
        var data = new WorldEventsSaveData { globalCooldown = globalCooldown };
        foreach (var kv in lastFiredDay)
        {
            data.firedIds.Add(kv.Key);
            data.firedLastDay.Add(kv.Value);
            data.firedTimes.Add(timesFired.TryGetValue(kv.Key, out int n) ? n : 0);
        }
        data.activeIds.AddRange(activeIds);
        data.activeDaysRemaining.AddRange(activeDaysRemaining);
        return data;
    }

    public void RestoreFromSave(WorldEventsSaveData data)
    {
        if (data == null) return;
        lastFiredDay.Clear();
        timesFired.Clear();
        activeIds.Clear();
        activeDaysRemaining.Clear();
        globalCooldown = Mathf.Max(0, data.globalCooldown);

        int n = Mathf.Min(data.firedIds.Count,
            Mathf.Min(data.firedLastDay.Count, data.firedTimes.Count));
        for (int i = 0; i < n; i++)
        {
            // Unknown ids (an event retired between versions) are kept in the
            // fired ledger harmlessly - they gate nothing that no longer exists.
            lastFiredDay[data.firedIds[i]] = data.firedLastDay[i];
            timesFired[data.firedIds[i]] = data.firedTimes[i];
        }
        int m = Mathf.Min(data.activeIds.Count, data.activeDaysRemaining.Count);
        for (int i = 0; i < m; i++)
        {
            // An active effect whose asset no longer exists is dropped: with
            // no definition there is no magnitude to apply.
            if (FindDefinition(data.activeIds[i]) == null) continue;
            activeIds.Add(data.activeIds[i]);
            activeDaysRemaining.Add(Mathf.Max(1, data.activeDaysRemaining[i]));
        }
        // DayNightCycle.LoadSaveData deliberately does not re-fire
        // OnDayStarted, so a loaded mid-effect state must re-arm its
        // multipliers here or a saved murrain would load cured.
        RecomputeMultipliers();
    }
}

[Serializable]
public class WorldEventsSaveData
{
    public int globalCooldown;
    public List<string> firedIds = new();
    public List<int> firedLastDay = new();
    public List<int> firedTimes = new();
    public List<string> activeIds = new();
    public List<int> activeDaysRemaining = new();
}
