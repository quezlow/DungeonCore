#!/usr/bin/env python3
"""apply_world_events_framework.py -- delivery script 1 of 2 for the
Random World Events framework (canon 37).

Creates:
  Assets/Scripts/Gameplay/WorldEventDefinition.cs
  Assets/Scripts/Gameplay/WorldEventDirector.cs
  Tools/sim_world_events.py
Edits:
  Assets/Scripts/Save/DungeonSaveData.cs        (worldEvents field, additive)
  Assets/Scripts/Save/DungeonSaveController.cs  (save + restore + new-game reset)

Run from the repo root:  python3 apply_world_events_framework.py
All edits stage in memory; any failed assertion leaves the tree untouched.
Idempotent: a second run aborts cleanly. Apply script 2
(apply_world_events_content.py) after this one.
"""
import io, os, sys

ROOT = os.path.dirname(os.path.abspath(__file__))

NEW_FILES = {}

NEW_FILES["Assets/Scripts/Gameplay/WorldEventDefinition.cs"] = """using UnityEngine;

/// <summary>
/// What an event DOES when it fires. Multiplier kinds (RespawnRate,
/// CivilianWeight) hold for durationDays; GrantGold is instant; None fires
/// the alert alone (pure flavour).
///
/// Values serialise as ints into .asset files, so this enum is APPEND-ONLY:
/// never reorder or remove, exactly as the save-facing enums. A new kind is
/// a new value here plus one case in WorldEventDirector.Fire - that switch
/// is the single place effects become behaviour.
/// </summary>
public enum WorldEventEffectKind
{
    None = 0,
    RespawnRate = 1,     // dungeon monster respawn speed multiplier (timed)
    CivilianWeight = 2,  // civilian intent lane multiplier (timed)
    GrantGold = 3,       // one-shot gold grant to the core
}

/// <summary>
/// One authored world event: the gates that make it eligible, the weight the
/// dawn roll draws it by, and the effect it fires. Assets live under
/// Resources/Events/World so WorldEventDirector self-populates - authored by
/// Dungeon Core -> Generate World Events (Editor/WorldEventContentGenerator).
///
/// A new event on an existing effect kind is assets-only: one spec row in the
/// generator, regenerate, done. Predicates are FIELDS, not code, so authoring
/// never touches the director. Cadence maths lives in
/// Tools/sim_world_events.py - rerun it whenever gates, weights, or the dawn
/// ordering change; the director must mirror that file.
/// </summary>
[CreateAssetMenu(fileName = "WorldEvent", menuName = "Dungeon/World Event Definition")]
public class WorldEventDefinition : ScriptableObject
{
    /// <summary>Save-facing identity is the asset name. String ids, never enum
    /// indices, so authored events can come and go across versions - a save
    /// naming an event that no longer exists is skipped on load, not a fault.</summary>
    public string Id => name;

    [Header("Alert (wisp voice)")]
    [Tooltip("The line the alert speaks when the event fires.")]
    [TextArea] public string alertMessage;
    public AlertCategory alertCategory = AlertCategory.Discovery;
    public AlertSeverity alertSeverity = AlertSeverity.Info;

    [Tooltip("Hostile events are stripped from the dawn pool while the endgame " +
             "climax suppresses mid-game threats. None of the v1 trio is hostile; " +
             "this is the slot a future assault-shaped event rides.")]
    public bool hostile;

    [Header("Gates (0 = no gate)")]
    [Tooltip("First day the event can fire. The director misses day 1 by " +
             "subscription order (the threats' shared idiom), so 2 is the " +
             "effective floor.")]
    [Min(1)] public int minDay = 1;
    [Tooltip("Notoriety at or above which the event is eligible. 0 = ungated.")]
    [Min(0f)] public float minNotoriety;
    [Tooltip("DungeonRating at or above which the event is eligible. 0 = ungated.")]
    [Min(0f)] public float minRating;

    [Header("Cadence")]
    [Tooltip("Dawns between this event's fires. Clamped to at least " +
             "durationDays so a timed effect can never overlap itself.")]
    [Min(1)] public int cooldownDays = 6;
    [Tooltip("Relative draw weight among eligible events on a fire day.")]
    [Min(0f)] public float weight = 1f;

    [Header("Effect")]
    public WorldEventEffectKind effectKind = WorldEventEffectKind.None;
    [Tooltip("Multiplier applied while a RespawnRate / CivilianWeight effect " +
             "holds. Ignored by other kinds.")]
    [Min(0f)] public float magnitude = 1f;
    [Tooltip("Days a multiplier effect holds (the fire day counts as the " +
             "first). Multiplier kinds treat values below 1 as 1.")]
    [Min(0)] public int durationDays;
    [Tooltip("GrantGold: inclusive roll range.")]
    [Min(0)] public int goldMin;
    [Min(0)] public int goldMax;

    private void OnValidate()
    {
        // A cooldown shorter than the duration would let an effect re-fire
        // over itself; the sim asserts the same invariant (check 4).
        if (cooldownDays < durationDays) cooldownDays = durationDays;
        if (goldMax < goldMin) goldMax = goldMin;
    }
}
"""

NEW_FILES["Assets/Scripts/Gameplay/WorldEventDirector.cs"] = """using System;
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
"""

NEW_FILES["Tools/sim_world_events.py"] = '''#!/usr/bin/env python3
"""Headless simulation of the WorldEventDirector dawn logic.

Models the exact dawn sequence the C# will implement:
  1. tick active timed effects (decrement, expire at zero, recompute mults)
  2. decrement the global cooldown
  3. gather eligible events (minDay, gates, per-event cooldown, suppression)
  4. roll the global daily fire chance; on success pick one by weight
  5. fire: instant effects apply at once, timed effects join the active list;
     the fired event's cooldown and the global cooldown re-arm

Run: python3 Tools/sim_world_events.py
Exit 0 with all checks green, 1 otherwise. Rerun whenever the tuning
defaults or the dawn ordering change; the C# must mirror this file.
"""

import random
import sys

# -- Tuning defaults (must match WorldEventDirector + the three assets) --

DAILY_FIRE_CHANCE = 0.25
GLOBAL_COOLDOWN_DAYS = 3

EVENTS = {
    # id: (minDay, minNotoriety, minRating, cooldownDays, weight,
    #      durationDays, kind, magnitude, hostile)
    "we_murrain":       (15, 0, 0, 10, 1.0, 3, "RespawnRate",   0.5, False),
    "we_pilgrim_surge": (10, 0, 0,  8, 1.0, 2, "CivilianWeight", 1.5, False),
    "we_tremor":        ( 6, 0, 0,  6, 1.5, 0, "GrantGold",      0.0, False),
}

MINDAY, MINNOTO, MINRATING, CD, WEIGHT, DURATION, KIND, MAG, HOSTILE = range(9)


class Director:
    """Mirror of the C# dawn state machine."""

    def __init__(self, rng, notoriety=0.0, rating=0.0, suppress=False):
        self.rng = rng
        self.notoriety = notoriety
        self.rating = rating
        self.suppress = suppress
        self.global_cd = 0
        self.last_fired = {}     # id -> day
        self.times_fired = {}    # id -> count
        self.active = {}         # id -> days remaining
        self.log = []            # (day, id)

    # -- cached multiplier recompute, exactly as the C# will do it --
    def respawn_mult(self):
        m = 1.0
        for eid in self.active:
            if EVENTS[eid][KIND] == "RespawnRate":
                m *= EVENTS[eid][MAG]
        return m

    def civilian_mult(self):
        m = 1.0
        for eid in self.active:
            if EVENTS[eid][KIND] == "CivilianWeight":
                m *= EVENTS[eid][MAG]
        return m

    def eligible(self, eid, day):
        e = EVENTS[eid]
        if day < e[MINDAY]:
            return False
        if e[MINNOTO] > 0 and self.notoriety < e[MINNOTO]:
            return False
        if e[MINRATING] > 0 and self.rating < e[MINRATING]:
            return False
        if eid in self.active:
            return False
        last = self.last_fired.get(eid)
        if last is not None and day - last < e[CD]:
            return False
        return True

    def dawn(self, day):
        # 1. tick active effects
        for eid in list(self.active):
            self.active[eid] -= 1
            if self.active[eid] <= 0:
                del self.active[eid]
        # 2. global cooldown
        if self.global_cd > 0:
            self.global_cd -= 1
            return
        # 3. eligibility (climax suppression strips hostile events only)
        pool = [eid for eid in EVENTS
                if self.eligible(eid, day)
                and not (self.suppress and EVENTS[eid][HOSTILE])]
        if not pool:
            return
        # 4. daily chance + weighted pick
        if self.rng.random() >= DAILY_FIRE_CHANCE:
            return
        weights = [EVENTS[eid][WEIGHT] for eid in pool]
        eid = self.rng.choices(pool, weights=weights, k=1)[0]
        # 5. fire
        self.fire(eid, day)

    def fire(self, eid, day):
        e = EVENTS[eid]
        if e[DURATION] > 0:
            self.active[eid] = e[DURATION]
        self.last_fired[eid] = day
        self.times_fired[eid] = self.times_fired.get(eid, 0) + 1
        self.global_cd = GLOBAL_COOLDOWN_DAYS
        self.log.append((day, eid))

    # -- save/load round trip: state only, no dawn refire --
    def save(self):
        return (self.global_cd, dict(self.last_fired),
                dict(self.times_fired), dict(self.active))

    @classmethod
    def load(cls, rng, blob, notoriety=0.0, rating=0.0):
        d = cls(rng, notoriety, rating)
        d.global_cd, d.last_fired, d.times_fired, d.active = (
            blob[0], dict(blob[1]), dict(blob[2]), dict(blob[3]))
        return d


# -- Checks ----------------------------------------------------------

FAILURES = []


def check(name, ok, detail=""):
    tag = "ok  " if ok else "FAIL"
    print(f"[{tag}] {name}" + (f" -- {detail}" if detail else ""))
    if not ok:
        FAILURES.append(name)


def run(seed, days=200, notoriety=0.0, rating=0.0, suppress=False):
    d = Director(random.Random(seed), notoriety, rating, suppress)
    mult_trace = []
    for day in range(1, days + 1):
        d.dawn(day)
        mult_trace.append((day, d.respawn_mult(), d.civilian_mult()))
    return d, mult_trace


def main():
    # 1. no event before its minDay
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        for day, eid in d.log:
            if day < EVENTS[eid][MINDAY]:
                bad.append((seed, day, eid))
    check("no fire before minDay", not bad, str(bad[:3]))

    # 2. per-event cooldown never violated
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        last = {}
        for day, eid in d.log:
            if eid in last and day - last[eid] < EVENTS[eid][CD]:
                bad.append((seed, eid, last[eid], day))
            last[eid] = day
    check("per-event cooldown respected", not bad, str(bad[:3]))

    # 3. global cooldown: no two fires closer than GLOBAL_COOLDOWN_DAYS + 1
    #    (fire day re-arms cd=3, which burns down over the next 3 dawns)
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        for (d1, _), (d2, _) in zip(d.log, d.log[1:]):
            if d2 - d1 <= GLOBAL_COOLDOWN_DAYS:
                bad.append((seed, d1, d2))
    check("global cooldown respected", not bad, str(bad[:3]))

    # 4. timed effects never self-overlap (guaranteed if cd >= duration)
    ok = all(e[CD] >= e[DURATION] for e in EVENTS.values())
    check("per-event cooldown >= duration (no self-overlap)", ok)

    # 5. selection proportions track weights (all eligible, high chance)
    counts = {eid: 0 for eid in EVENTS}
    rng = random.Random(7)
    d = Director(rng)
    pool = list(EVENTS)
    for _ in range(60000):
        weights = [EVENTS[eid][WEIGHT] for eid in pool]
        counts[rng.choices(pool, weights=weights, k=1)[0]] += 1
    total_w = sum(EVENTS[eid][WEIGHT] for eid in pool)
    ok = True
    for eid in pool:
        expect = EVENTS[eid][WEIGHT] / total_w
        got = counts[eid] / 60000
        if abs(got - expect) > 0.02:
            ok = False
    check("weighted selection proportions", ok, str(counts))

    # 6. cadence band: mean events per 30 days (after all minDays) in [3, 6]
    rates = []
    for seed in range(200):
        d, _ = run(seed, days=15 + 30)
        rates.append(sum(1 for day, _ in d.log if day > 15))
    mean = sum(rates) / len(rates)
    check("cadence 3-6 events per 30 eligible days", 3.0 <= mean <= 6.0,
          f"mean {mean:.2f}")

    # 7. determinism with seed
    a, _ = run(42)
    b, _ = run(42)
    check("deterministic per seed", a.log == b.log)

    # 8. zero eligible -> no fire (gates unmet)
    EVENTS_BACKUP = dict(EVENTS)
    for eid in EVENTS:
        e = list(EVENTS[eid])
        e[MINNOTO] = 999
        EVENTS[eid] = tuple(e)
    d, _ = run(1, days=100)
    check("no eligible events -> silent", not d.log)
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 9. minNotoriety gate opens correctly
    e = list(EVENTS["we_tremor"])
    e[MINNOTO] = 50
    EVENTS["we_tremor"] = tuple(e)
    d_lo, _ = run(3, days=100, notoriety=10)
    d_hi, _ = run(3, days=100, notoriety=80)
    lo_fired = any(eid == "we_tremor" for _, eid in d_lo.log)
    hi_fired = any(eid == "we_tremor" for _, eid in d_hi.log)
    check("minNotoriety gate", (not lo_fired) and hi_fired,
          f"low fired={lo_fired} high fired={hi_fired}")
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 10. save/load mid-effect: remaining days continue, no refire on load
    rng = random.Random(9)
    d = Director(rng)
    d.fire("we_murrain", 20)          # duration 3
    d.dawn(21)                        # ticks to 2
    blob = d.save()
    d2 = Director.load(random.Random(9), blob)
    ok = (d2.active.get("we_murrain") == 2
          and abs(d2.respawn_mult() - 0.5) < 1e-9
          and len(d2.log) == 0)
    check("save/load mid-effect resumes without refire", ok,
          f"active={d2.active} mult={d2.respawn_mult()}")

    # 11. expiry restores multiplier to 1
    d2.dawn(22)   # 2 -> 1
    d2.dawn(23)   # 1 -> 0, expires
    check("effect expiry restores multiplier",
          abs(d2.respawn_mult() - 1.0) < 1e-9,
          f"mult={d2.respawn_mult()}")

    # 12. multiplier active exactly duration dawns after the fire dawn
    rng = random.Random(11)
    d = Director(rng)
    d.fire("we_pilgrim_surge", 30)    # duration 2
    m0 = d.civilian_mult()            # active on fire day
    d.dawn(31)
    m1 = d.civilian_mult()            # still active (1 day left)
    d.dawn(32)
    m2 = d.civilian_mult()            # expired
    check("timed effect spans fire day + duration-1 following days",
          abs(m0 - 1.5) < 1e-9 and abs(m1 - 1.5) < 1e-9
          and abs(m2 - 1.0) < 1e-9,
          f"{m0} {m1} {m2}")

    # 13. suppression strips hostile events only; benign ones still fire
    e = list(EVENTS["we_murrain"])
    e[HOSTILE] = True
    EVENTS["we_murrain"] = tuple(e)
    d, _ = run(5, days=200, suppress=True)
    hostile_fired = any(eid == "we_murrain" for _, eid in d.log)
    benign_fired = any(eid != "we_murrain" for _, eid in d.log)
    check("suppression strips hostile only",
          (not hostile_fired) and benign_fired,
          f"hostile={hostile_fired} benign={benign_fired}")
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 14. active effect blocks its own re-fire even if cd were shorter
    rng = random.Random(13)
    d = Director(rng)
    d.fire("we_murrain", 40)
    check("active effect not re-eligible", not d.eligible("we_murrain", 41))

    print()
    if FAILURES:
        print(f"{len(FAILURES)} check(s) FAILED: {FAILURES}")
        return 1
    print("all checks green")
    return 0


if __name__ == "__main__":
    sys.exit(main())
'''

EDITS = [
    ("Assets/Scripts/Save/DungeonSaveData.cs",
     "    public NobleRetaliationSaveData nobleRetaliation;\n",
     "    public NobleRetaliationSaveData nobleRetaliation;\n\n"
     "    public WorldEventsSaveData worldEvents;   // random world events (additive; null on old saves)\n"),
    ("Assets/Scripts/Save/DungeonSaveController.cs",
     "        if (NobleRetaliation.Instance != null)\n"
     "            currentSave.nobleRetaliation = NobleRetaliation.Instance.GetSaveData();\n",
     "        if (NobleRetaliation.Instance != null)\n"
     "            currentSave.nobleRetaliation = NobleRetaliation.Instance.GetSaveData();\n\n"
     "        if (WorldEventDirector.Instance != null)\n"
     "            currentSave.worldEvents = WorldEventDirector.Instance.GetSaveData();\n"),
    ("Assets/Scripts/Save/DungeonSaveController.cs",
     "            NobleRetaliation.Instance?.RestoreFromSave(currentSave.nobleRetaliation);\n",
     "            NobleRetaliation.Instance?.RestoreFromSave(currentSave.nobleRetaliation);\n"
     "            WorldEventDirector.Instance?.RestoreFromSave(currentSave.worldEvents);\n"),
    ("Assets/Scripts/Save/DungeonSaveController.cs",
     "        HolyGroundLedger.ResetForNewGame();              // fresh dungeon, every seal intact again\n",
     "        HolyGroundLedger.ResetForNewGame();              // fresh dungeon, every seal intact again\n"
     "        WorldEventDirector.ResetForNewGame();            // fresh dungeon, fresh weather\n"),
]


def load(rel):
    raw = io.open(os.path.join(ROOT, rel), "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    txt = raw.decode("utf-8")
    crlf = "\r\n" in txt
    if crlf:
        txt = txt.replace("\r\n", "\n")
    return txt, crlf, bom


def store(rel, txt, crlf, bom):
    if crlf:
        txt = txt.replace("\n", "\r\n")
    data = txt.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(os.path.join(ROOT, rel), "wb").write(data)


def main():
    # Idempotency guard
    guard, _, _ = load("Assets/Scripts/Save/DungeonSaveData.cs")
    if "WorldEventsSaveData worldEvents" in guard:
        print("Already applied -- aborting with the tree untouched.")
        return 0

    for rel in NEW_FILES:
        if os.path.exists(os.path.join(ROOT, rel)):
            print(f"REFUSED: {rel} already exists.")
            return 1

    # Stage everything in memory; assert every anchor BEFORE any write.
    staged = {}
    for rel, old, new in EDITS:
        txt, crlf, bom = staged.get(rel, (None, None, None))
        if txt is None:
            txt, crlf, bom = load(rel)
        n = txt.count(old)
        if n != 1:
            print(f"ANCHOR FAULT in {rel}: expected 1, found {n}. Nothing written.")
            return 1
        staged[rel] = (txt.replace(old, new), crlf, bom)

    # Validate embedded files: ASCII + brace balance.
    for rel, body in NEW_FILES.items():
        bad = [c for c in body if ord(c) > 127]
        if bad:
            print(f"NON-ASCII in embedded {rel}: {bad[:5]!r}. Nothing written.")
            return 1
        for a, b in (("{", "}"), ("(", ")"), ("[", "]")):
            if body.count(a) != body.count(b):
                print(f"UNBALANCED {a}{b} in embedded {rel}. Nothing written.")
                return 1

    # All checks passed: write everything, then report.
    for rel, body in NEW_FILES.items():
        path = os.path.join(ROOT, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        io.open(path, "w", encoding="utf-8", newline="\n").write(body)
    for rel, (txt, crlf, bom) in staged.items():
        store(rel, txt, crlf, bom)

    print("apply_world_events_framework: applied.")
    print("  created: " + ", ".join(sorted(NEW_FILES)))
    print("  edited:  Save/DungeonSaveData.cs, Save/DungeonSaveController.cs")
    print("Next: python3 Tools/sim_world_events.py (expect all green), then")
    print("      python3 apply_world_events_content.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
