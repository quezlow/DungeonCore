using System;
using UnityEngine;

/// <summary>
/// The flag-driven climax. When the core reaches its ascension threshold (Diamond 3), the
/// dungeon's own history decides its final trial: whichever mid-game threat it provoked
/// most - ties broken by the most recent - returns escalated. A dungeon that provoked none
/// faces the threat its current profile best fits. Surviving the trial opens the Diamond 3
/// -> God 1 ascension (the god-core sandbox) and silences the recurring threats for good.
///
/// The four faces:
///   HolyOrder -> the Grand Crusade  (a named ordained Paladin leading Paladins + Clerics)
///   Mercenary -> the Iron Host      (a Tank-heavy sellsword army)
///   KingsArmy -> the King's Host     (Hero-heavy: several named Heroes + royal guard),
///                provoked by slaying nobles - NobleRetaliation is its tracked flag
///   WildBeast -> the Empowered Beast (a max-power invader that drives for the core; each
///                breach flings it back to the entrance - only killing it ends the climax)
///
/// SCENE SETUP: put this on the persistent manager GameObject alongside FactionSystem,
/// HolyOrderStrike, MercenaryContract, WildMonsterEvent, and NobleRetaliation. Add a
/// ScreenFlash component too (the beast's pushback flash). Tuning fields carry defaults.
/// </summary>
public class EndgameClimax : MonoBehaviour
{
    public static EndgameClimax Instance { get; private set; }

    public enum ClimaxThreat { None, HolyOrder, Mercenary, KingsArmy, WildBeast }

    [Header("Trigger")]
    [Tooltip("Flat core level that arms the climax (25 = Diamond 3).")]
    [SerializeField] private int climaxLevel = 25;

    [Header("Escalated force sizes")]
    [Tooltip("Grand Crusade guards behind the ordained Paladin (Church).")]
    [SerializeField] private int crusadeGuards = 8;
    [Tooltip("Iron Host sellswords (Mercenary).")]
    [SerializeField] private int armyMercs = 9;
    [Tooltip("King's Host named Heroes.")]
    [SerializeField] private int royalHeroes = 4;
    [Tooltip("King's Host royal guard behind the Heroes.")]
    [SerializeField] private int royalGuards = 4;

    [Header("Empowered Beast (Wild) stat scale")]
    [Min(1f)][SerializeField] private float beastHpMult = 3f;
    [Min(1f)][SerializeField] private float beastDamageMult = 2f;
    [Min(1f)][SerializeField] private float beastScaleMult = 1.5f;

    private bool armed;      // reached the level; fires next dawn
    private bool active;     // the trial is loose
    private bool ascended;   // survived -> god-core sandbox
    private ClimaxThreat threat = ClimaxThreat.None;
    private bool subscribed;

    // The recurring threats stand down once the finale gathers (armed), while it rages
    // (active), and forever after (ascended).
    public bool SuppressMidGameThreats => armed || active || ascended;
    public bool Ascended => ascended;
    public bool ClimaxActive => active;
    public ClimaxThreat ActiveThreat => threat;

    private static Vector3 EntrancePos =>
        DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

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
        if (ascended) return;

        if (active)
        {
            // The Wild trial is one relentless beast; if a load left the field empty,
            // make sure a beast is always present while the climax rages.
            if (threat == ClimaxThreat.WildBeast && WildMonsterEvent.Instance != null
                && !WildMonsterEvent.Instance.ClimaxBeastActive)
                WildMonsterEvent.Instance.SpawnClimaxBeast(beastHpMult, beastDamageMult, beastScaleMult);
            return;
        }

        if (armed) { Fire(); return; }

        if (DungeonCore.Instance != null && DungeonCore.Instance.DungeonLevel >= climaxLevel)
        {
            armed = true;
            DungeonSaveController.Instance?.RequestAutosave();
            Announce("You have grown vast, little core, and the world has taken note. Its answer gathers beyond your walls.");
        }
    }

    private void Fire()
    {
        armed = false;
        active = true;
        threat = DetermineDominant();

        switch (threat)
        {
            case ClimaxThreat.HolyOrder:
                AdventurerSpawner.Instance?.DispatchClimaxCrusade(crusadeGuards);
                Announce("The Order empties its temples, little core. A grand crusade marches to cleanse you utterly.");
                break;
            case ClimaxThreat.Mercenary:
                AdventurerSpawner.Instance?.DispatchClimaxArmy(armyMercs);
                Announce("Every blade the coin-lords could buy moves as one host, little core. They mean to close the account for good.");
                break;
            case ClimaxThreat.KingsArmy:
                AdventurerSpawner.Instance?.DispatchClimaxRoyalHost(royalHeroes, royalGuards);
                Announce("You have thinned the realm's noble blood, little core, and the crown answers in kind. The King's own host descends on you.");
                break;
            default:
                threat = ClimaxThreat.WildBeast;
                WildMonsterEvent.Instance?.SpawnClimaxBeast(beastHpMult, beastDamageMult, beastScaleMult);
                Announce("The thing that once hungered returns vast and tireless, little core. It will not leave while your core still beats.");
                break;
        }

        DungeonSaveController.Instance?.RequestAutosave();
    }

    private void Announce(string msg) =>
        AlertsLog.Instance?.AddAlert(msg, EntrancePos, 0, AlertCategory.Threat);

    /// <summary>Pick the dominant provoked threat: most times manifested, ties broken by the
    /// most recent. If none was ever provoked, fall back to the best current profile fit.</summary>
    private ClimaxThreat DetermineDominant()
    {
        int ho = HolyOrderStrike.Instance != null ? HolyOrderStrike.Instance.TimesManifested : 0;
        int me = MercenaryContract.Instance != null ? MercenaryContract.Instance.TimesManifested : 0;
        int ki = NobleRetaliation.Instance != null ? NobleRetaliation.Instance.TimesManifested : 0;
        int wi = WildMonsterEvent.Instance != null ? WildMonsterEvent.Instance.TimesManifested : 0;

        int max = Mathf.Max(Mathf.Max(ho, me), Mathf.Max(ki, wi));
        if (max <= 0) return FallbackByProfile();

        int hd = HolyOrderStrike.Instance != null ? HolyOrderStrike.Instance.LastManifestDay : 0;
        int md = MercenaryContract.Instance != null ? MercenaryContract.Instance.LastManifestDay : 0;
        int kd = NobleRetaliation.Instance != null ? NobleRetaliation.Instance.LastManifestDay : 0;
        int wd = WildMonsterEvent.Instance != null ? WildMonsterEvent.Instance.LastManifestDay : 0;

        ClimaxThreat best = ClimaxThreat.WildBeast;
        int bestDay = -1;
        if (ho == max && hd > bestDay) { best = ClimaxThreat.HolyOrder; bestDay = hd; }
        if (me == max && md > bestDay) { best = ClimaxThreat.Mercenary; bestDay = md; }
        if (ki == max && kd > bestDay) { best = ClimaxThreat.KingsArmy; bestDay = kd; }
        if (wi == max && wd > bestDay) { best = ClimaxThreat.WildBeast; bestDay = wd; }
        return best;
    }

    private ClimaxThreat FallbackByProfile()
    {
        float ho = HolyOrderStrike.Instance != null ? HolyOrderStrike.Instance.ProfileMatchScore : 0f;
        float me = MercenaryContract.Instance != null ? MercenaryContract.Instance.ProfileMatchScore : 0f;
        float ki = NobleRetaliation.Instance != null ? NobleRetaliation.Instance.ProfileMatchScore : 0f;
        float wi = WildMonsterEvent.Instance != null ? WildMonsterEvent.Instance.ProfileMatchScore : 0f;

        float max = Mathf.Max(Mathf.Max(ho, me), Mathf.Max(ki, wi));
        if (max <= 0f) return ClimaxThreat.WildBeast;   // a quiet dungeon still faces the beast
        if (wi >= max) return ClimaxThreat.WildBeast;
        if (ho >= max) return ClimaxThreat.HolyOrder;
        if (me >= max) return ClimaxThreat.Mercenary;
        return ClimaxThreat.KingsArmy;
    }

    /// <summary>A climax host (Grand Crusade / Iron Host / King's Host) was wiped or driven
    /// off with the core intact - the trial is passed.</summary>
    public void OnClimaxThreatResolved()
    {
        if (!active || ascended) return;
        Ascend();
    }

    /// <summary>The empowered beast was slain - the trial is passed. Its breaches are the
    /// normal two-strike; only its death ends the climax.</summary>
    public void OnClimaxBeastSlain()
    {
        if (!active || ascended) return;
        Ascend();
    }

    private void Ascend()
    {
        active = false;
        ascended = true;
        DungeonCore.Instance?.RefreshLevelUpAvailability();
        DungeonSaveController.Instance?.RequestAutosave();
        AlertsLog.Instance?.AddAlert(
            "The world has spent its fury and you endure, little core. What waits beyond is not survival - it is apotheosis. Ascend.",
            EntrancePos, 0, AlertCategory.System);
    }

    public EndgameClimaxSaveData GetSaveData() => new()
    {
        armed = armed,
        active = active,
        ascended = ascended,
        threat = (int)threat,
    };

    public void RestoreFromSave(EndgameClimaxSaveData data)
    {
        if (data == null) return;
        armed = data.armed;
        active = data.active;
        ascended = data.ascended;
        threat = (ClimaxThreat)Mathf.Clamp(data.threat, 0, 4);
        if (ascended) DungeonCore.Instance?.RefreshLevelUpAvailability();
    }
}

[Serializable]
public class EndgameClimaxSaveData
{
    public bool armed;
    public bool active;
    public bool ascended;
    public int threat;
}