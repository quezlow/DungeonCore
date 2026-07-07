using System;
using UnityEngine;

/// <summary>
/// The wild-monster reprisal. When the dungeon grows strong enough (DungeonRating),
/// a great predator emerges at the entrance, hunts the dungeon's creatures until its
/// hunger is sated - or it starves for want of prey - then turns and leaves the way it
/// came. Wound it below its threshold, or kill it, before it escapes and the threat is
/// broken; let it slip out unhurt and it returns hungrier and stronger each time.
///
/// This targets the powerful dungeon - the third face of the mid-game alongside the
/// Holy Order (dark and infamous) and the Mercenary Company (rich and generous). The
/// beast never breaches the core; it eats creatures, not rock. First appearance sets
/// the hidden climax flag the endgame reads.
///
/// SCENE SETUP: put this on the persistent manager GameObject alongside DungeonRating,
/// FactionSystem, HolyOrderStrike, and MercenaryContract. Assign the Predator Def (a
/// BossVariantDefinition whose prefab is the beast). All tuning fields carry defaults.
/// </summary>
public class WildMonsterEvent : MonoBehaviour
{
    public static WildMonsterEvent Instance { get; private set; }

    [Header("The Predator")]
    [Tooltip("Boss variant spawned as the predator - its prefab is the beast; boss stats + label come from the asset.")]
    [SerializeField] private BossVariantDefinition predatorDef;

    [Header("Trigger")]
    [Tooltip("DungeonRating at or above which the predator can emerge at dawn.")]
    [SerializeField] private float ratingThreshold = 120f;
    [Tooltip("Dawns of cooldown after the predator resolves (leaves or is slain) before another can emerge.")]
    [SerializeField] private int rearmDays = 6;

    [Header("Hunger + Retreat")]
    [Tooltip("Kills that sate the predator (dungeon monsters AND adventurers count). Then it leaves.")]
    [SerializeField] private int hungerTarget = 3;
    [Tooltip("If HP ever drops below this fraction before it escapes, it leaves wounded and does not return.")]
    [Range(0f, 1f)][SerializeField] private float woundedFraction = 0.4f;
    [Tooltip("Seconds of finding no prey in range before it gives up and leaves unsated.")]
    [SerializeField] private float giveUpSeconds = 20f;

    [Header("Escalation (per un-wounded escape, capped at Max Level)")]
    [Min(1f)][SerializeField] private float hpPerLevel = 1.4f;
    [Min(1f)][SerializeField] private float damagePerLevel = 1.25f;
    [Min(1f)][SerializeField] private float scalePerLevel = 1.1f;
    [SerializeField] private int maxLevel = 4;

    private int escalationLevel;
    private int cooldown;
    private bool climaxRaised;
    private DungeonMonster livePredator;
    private int timesEmerged;
    private int lastManifestDay;
    private DungeonMonster climaxBeast;
    private bool climaxBeastActive;
    private float climaxHpMult = 1f;
    private float climaxDmgMult = 1f;
    private float climaxScaleMult = 1f;
    private bool subscribed;

    // ── Public reads ──────────────────────────────────────────────
    public bool ClimaxRaised => climaxRaised;
    public bool PredatorActive => livePredator != null;
    public int TimesManifested => timesEmerged;
    public int LastManifestDay => lastManifestDay;
    public bool ClimaxBeastActive => climaxBeastActive;
    public float ProfileMatchScore =>
        DungeonRating.Instance != null && ratingThreshold > 0f
            ? Mathf.Clamp01(DungeonRating.Instance.CurrentRating / ratingThreshold) : 0f;

    private static Vector3 EntrancePos =>
        DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

    private FloorRoot HuntFloor => FloorManager.Instance != null ? FloorManager.Instance.GetFloor(0) : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        if (Instance == this) Instance = null;
    }

    private void OnDawn()
    {
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats) return;
        if (livePredator != null) return;         // one beast at a time
        if (cooldown > 0) { cooldown--; return; }
        if (DungeonRating.Instance == null) return;
        if (DungeonRating.Instance.CurrentRating < ratingThreshold) return;
        SpawnPredator();
    }

    private void SpawnPredator()
    {
        var floor = HuntFloor;
        if (floor == null || predatorDef == null || predatorDef.prefab == null || DungeonEntrance.Instance == null)
            return;

        var beast = SpawnPredatorAt(EntrancePos, floor);
        if (beast == null) return;

        climaxRaised = true;                       // the endgame remembers the beast came
        timesEmerged++;
        lastManifestDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 0;
        DungeonSaveController.Instance?.RequestAutosave();
        AlertsLog.Instance?.AddAlert(
            "Something vast has scented your halls, little core, and it hungers. It comes to feed.",
            EntrancePos, 0, AlertCategory.Threat);
    }

    /// <summary>Spawn + fully configure a predator at a world position. Used by both the
    /// fresh emergence and the save restore (which overrides HP + kills afterwards).</summary>
    private DungeonMonster SpawnPredatorAt(Vector3 worldPos, FloorRoot floor)
    {
        var beast = Instantiate(predatorDef.prefab, worldPos, Quaternion.identity);
        beast.transform.SetParent(floor.transform, true);
        beast.InitialiseInvader(floor, predatorDef);
        beast.ApplyBossModifiers(predatorDef);

        int lvl = Mathf.Clamp(escalationLevel, 0, Mathf.Max(0, maxLevel));
        if (lvl > 0)
            beast.ApplyStatScale(Pow(hpPerLevel, lvl), Pow(damagePerLevel, lvl), Pow(scalePerLevel, lvl));

        beast.ConfigureAsPredator(hungerTarget, woundedFraction, giveUpSeconds);
        floor.Entities?.Register(beast);
        beast.OnDied += HandlePredatorDied;
        livePredator = beast;
        return beast;
    }

    private static float Pow(float b, int e) { float r = 1f; for (int i = 0; i < e; i++) r *= b; return r; }

    // ── Predator callbacks (from DungeonMonster) ──────────────────

    /// <summary>The predator has sated its hunger (or starved) and turned for the exit.</summary>
    public void OnPredatorBeganLeaving(bool sated, bool wounded)
    {
        string msg = sated
            ? "The beast has eaten its fill and turns for the surface. Bloody it before it escapes, or it will return."
            : "The beast finds nothing more worth the hunt and withdraws. Bloody it before it escapes, or it will return.";
        AlertsLog.Instance?.AddAlert(msg, EntrancePos, 0, AlertCategory.Threat);
    }

    /// <summary>The predator reached the exit and left the dungeon alive.</summary>
    public void OnPredatorDeparted(bool wounded)
    {
        if (wounded)
        {
            escalationLevel = 0;                    // hurt badly enough to break the pattern
            AlertsLog.Instance?.AddAlert(
                "The beast drags itself back into the dark, bleeding. It will not come back the same.",
                EntrancePos, 0, AlertCategory.System);
        }
        else
        {
            escalationLevel = Mathf.Min(Mathf.Max(0, maxLevel), escalationLevel + 1);
            AlertsLog.Instance?.AddAlert(
                "The beast slips away sated and unhurt. It will return, and there will be more of it.",
                EntrancePos, 0, AlertCategory.System);
        }
        cooldown = Mathf.Max(1, rearmDays);
        livePredator = null;
    }

    private void HandlePredatorDied(DungeonMonster m)
    {
        escalationLevel = 0;                        // slain outright - the pattern is broken
        cooldown = Mathf.Max(1, rearmDays);
        livePredator = null;
        AlertsLog.Instance?.AddAlert(
            "The great beast falls, and the halls are still. It will not rise from this.",
            EntrancePos, 0, AlertCategory.System);
    }

    /// <summary>Spawn the endgame climax beast: the predator as a max-power invader that
    /// drives for the core and, on each breach, is flung back to charge again (never
    /// leaving, never splitting). Distinct from the mid-game predator; only its death ends
    /// the climax. Fired by EndgameClimax.</summary>
    public void SpawnClimaxBeast(float hpMult, float dmgMult, float scaleMult)
    {
        if (climaxBeast != null) return;
        var floor = HuntFloor;
        if (floor == null || predatorDef == null || predatorDef.prefab == null || DungeonEntrance.Instance == null)
            return;

        climaxHpMult = Mathf.Max(1f, hpMult);
        climaxDmgMult = Mathf.Max(1f, dmgMult);
        climaxScaleMult = Mathf.Max(1f, scaleMult);

        var beast = Instantiate(predatorDef.prefab, EntrancePos, Quaternion.identity);
        beast.transform.SetParent(floor.transform, true);
        beast.InitialiseInvader(floor, predatorDef);
        beast.ApplyBossModifiers(predatorDef);
        beast.ApplyStatScale(climaxHpMult, climaxDmgMult, climaxScaleMult);
        beast.ConfigureAsClimaxInvader();
        floor.Entities?.Register(beast);
        beast.OnDied += HandleClimaxBeastDied;
        climaxBeast = beast;
        climaxBeastActive = true;
    }

    private void HandleClimaxBeastDied(DungeonMonster m)
    {
        climaxBeast = null;
        climaxBeastActive = false;
        AlertsLog.Instance?.AddAlert(
            "The great beast falls at last, little core, and the world holds its breath.",
            EntrancePos, 0, AlertCategory.System);
        EndgameClimax.Instance?.OnClimaxBeastSlain();
    }

    // ── Save / Load ───────────────────────────────────────────────

    public WildMonsterEventSaveData GetSaveData()
    {
        var data = new WildMonsterEventSaveData
        {
            escalationLevel = escalationLevel,
            cooldown = cooldown,
            climaxRaised = climaxRaised,
            predatorActive = livePredator != null,
            timesEmerged = timesEmerged,
            lastManifestDay = lastManifestDay,
            climaxBeastActive = climaxBeastActive,
            climaxHpMult = climaxHpMult,
            climaxDmgMult = climaxDmgMult,
            climaxScaleMult = climaxScaleMult,
        };
        if (livePredator != null)
        {
            var floor = HuntFloor;
            if (floor != null && floor.TileInfluence != null)
                data.predatorCell = SerializableVector3Int.From(
                    floor.TileInfluence.WorldToCell(livePredator.transform.position));
            data.predatorHP = livePredator.CurrentHP;
            data.predatorKills = livePredator.KillCount;
            data.predatorWounded = livePredator.PredatorWounded;
            data.predatorLeaving = livePredator.PredatorLeaving;
        }
        return data;
    }

    public void RestoreFromSave(WildMonsterEventSaveData data)
    {
        if (data == null) return;
        escalationLevel = Mathf.Max(0, data.escalationLevel);
        cooldown = Mathf.Max(0, data.cooldown);
        climaxRaised = data.climaxRaised;
        timesEmerged = Mathf.Max(0, data.timesEmerged);
        lastManifestDay = Mathf.Max(0, data.lastManifestDay);
        climaxHpMult = data.climaxHpMult > 0f ? data.climaxHpMult : 1f;
        climaxDmgMult = data.climaxDmgMult > 0f ? data.climaxDmgMult : 1f;
        climaxScaleMult = data.climaxScaleMult > 0f ? data.climaxScaleMult : 1f;
        if (data.climaxBeastActive) SpawnClimaxBeast(climaxHpMult, climaxDmgMult, climaxScaleMult);

        if (!data.predatorActive) return;
        var floor = HuntFloor;
        if (floor == null || floor.TileInfluence == null || predatorDef == null || predatorDef.prefab == null)
            return;

        Vector3 worldPos = floor.TileInfluence.CellToWorld(data.predatorCell.ToVector3Int());
        var beast = SpawnPredatorAt(worldPos, floor);
        if (beast == null) return;
        beast.SetCurrentHP(data.predatorHP);
        beast.SetMonsterKills(data.predatorKills);
        beast.RestorePredatorState(data.predatorWounded, data.predatorLeaving);
    }
}

[Serializable]
public class WildMonsterEventSaveData
{
    public int escalationLevel;
    public int cooldown;
    public bool climaxRaised;
    public bool predatorActive;
    public SerializableVector3Int predatorCell;
    public float predatorHP;
    public int predatorKills;
    public bool predatorWounded;
    public bool predatorLeaving;
    public int timesEmerged;
    public int lastManifestDay;
    public bool climaxBeastActive;
    public float climaxHpMult;
    public float climaxDmgMult;
    public float climaxScaleMult;
}