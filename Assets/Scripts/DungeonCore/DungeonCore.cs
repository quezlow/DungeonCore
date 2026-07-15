using System;
using UnityEngine;
using UnityEngine.Serialization;


[DefaultExecutionOrder(-20)]
public class DungeonCore : MonoBehaviour
{
    public static DungeonCore Instance { get; private set; }

    // ── Dungeon Identity ─────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private DungeonType dungeonType = DungeonType.None;

    // ── Progression Table (tier-based) ────────────────────────────
    [Header("Tier Progression")]
    [SerializeField] private DungeonCoreProgressionTable progression = new DungeonCoreProgressionTable();

    [Header("Mana Regen")]
    [SerializeField] private float baseRegenPerSecond = 1f;
    [SerializeField] private float regenPerTile = 0.1f;

    // ── XP ───────────────────────────────────────────────────────
    [Header("XP")]
    [SerializeField] private float baseXPToLevel = 100f;
    [SerializeField] private float xpScalingExponent = 1.5f;

    // ── Notoriety / Reputation ────────────────────────────────────
    [Header("Notoriety")]
    [SerializeField] private float notoriety = 0f;
    [SerializeField] private float notorietyDecayPerSecond = 0.1f;
    [Header("Reputation")]
    [SerializeField] private float reputation = 0f;

    // ── Breach ───────────────────────────────────────────────────
    [Header("Two-Strike Breach System")]
    [SerializeField] private float instabilityDuration = 60f;
    [SerializeField] private float xpPenaltyOnBreach = 50f;

    [Header("Notoriety Decay")]
    [SerializeField] private float notorietyDecayCooldown = 10f;

    private bool isUnstable = false;
    private float instabilityTimer = 0f;
    private int breachCount = 0;
    private float lastBreachTime = -999f;
    private float timeSinceLastKill = 0f;

    // ── Runtime State ─────────────────────────────────────────────
    private float currentMana;
    private float currentXP;
    private int dungeonLevel = 1; // flat 1..26 across all tiers
    private int claimedTileCount = 0;
    private int usedCapacity = 0;
    private int currentGold = 0;

    // Set once per over-cap episode so the clamp alert fires once, not per coin.
    private bool capClampAnnounced;
    private int researchPoints = 0;

    /// <summary>
    /// Number of stair-build credits the player has accumulated.
    /// Granted by qualifying tier-up transitions (Bronze 10 → Silver 1, etc.)
    /// Consumed by successfully placing a Down stair.
    /// </summary>
    private int stairCredits = 0;

    // ── Events ───────────────────────────────────────────────────
    public event Action<float, float> OnManaChanged;
    public event Action<float> OnManaRegenChanged;
    public event Action<float, float> OnXPChanged;
    public event Action<int> OnLevelUp;
    /// <summary>Fires whenever the level value changes — both on real level-up
    /// (ConfirmLevelUp) and when broadcasting loaded state (NotifyAll/LoadSaveData).
    /// Use this for UI that needs to refresh the displayed level. Use OnLevelUp
    /// only when responding to an actual level increment.</summary>
    public event Action<int> OnLevelChanged;
    public event Action OnLevelUpAvailable;
    public event Action<float> OnNotorietyChanged;
    public event Action<float> OnReputationChanged;
    public event Action<int, int> OnCapacityChanged;
    public event Action OnCoreDestroyed;
    public event Action<float> OnInstabilityTick;
    public event Action OnFirstBreach;
    public event Action OnCoreStabilised;
    public event Action OnGameOver;
    public event Action<int> OnGoldChanged;
    public event Action<int> OnResearchChanged;
    public event Action<int> OnStairCreditsChanged;

    // ── Public Reads ──────────────────────────────────────────────
    public DungeonType DungeonType => dungeonType;

    /// <summary>The core's signature colour for its type (matches the influence-ring palette).</summary>
    public Color CoreColor => ColorFor(dungeonType);

    /// <summary>Authored affinity colours. Static so the selection ceremony
    /// recolours with the exact values the dungeon uses.</summary>
    public static Color ColorFor(DungeonType type) => type switch
    {
        DungeonType.Fire => new Color(0.910f, 0.353f, 0.165f),
        DungeonType.Water => new Color(0.165f, 0.659f, 0.784f),
        DungeonType.Air => new Color(0.722f, 0.769f, 0.816f),
        DungeonType.Earth => new Color(0.690f, 0.478f, 0.212f),
        DungeonType.Dark => new Color(0.541f, 0.310f, 0.784f),
        DungeonType.Light => new Color(0.949f, 0.886f, 0.690f),
        _ => new Color(0.784f, 0.565f, 0.165f),
    };

    public int DungeonLevel => dungeonLevel;
    public LevelTier CurrentTier => LevelTierUtil.FromFlatLevel(dungeonLevel).tier;
    public int CurrentRank => LevelTierUtil.FromFlatLevel(dungeonLevel).rank;
    public string LevelDisplayName => LevelTierUtil.DisplayName(dungeonLevel);
    public float CurrentMana => currentMana;
    public float MaxMana => progression.ManaAt(dungeonLevel);
    public float CurrentManaRegen => baseRegenPerSecond + claimedTileCount * regenPerTile
                                     + RoomEffectCensus.ManaRegenPerSecond;
    public float CurrentXP => currentXP;
    public float XPToNextLevel => CalculateXPThreshold(dungeonLevel);
    public float Notoriety => notoriety;
    public float Reputation => reputation;
    public int ClaimedTileCount => claimedTileCount;

    [Obsolete("Phase 3 — Use ClaimedTileCount. Old name retained as safety net.")]
    public int OwnedTileCount => claimedTileCount;

    public int MaxCapacity => progression.CapacityAt(dungeonLevel);
    public int UsedCapacity => usedCapacity;
    public int FreeCapacity => MaxCapacity - usedCapacity;
    public bool LevelUpAvailable { get; private set; }
    public bool IsUnstable => isUnstable;
    public float InstabilityTimer => instabilityTimer;
    public float InstabilityDuration => instabilityDuration;
    public int Gold => currentGold;
    public int Research => researchPoints;
    public int StairCredits => stairCredits;
    public DungeonCoreProgressionTable Progression => progression;

    public bool IsInTransit => GetComponent<DungeonCoreTransit>() != null
                            && GetComponent<DungeonCoreTransit>().IsActive;

    /// <summary>Floor index this tier unlocks as a Down-stair destination.</summary>
    public static int FloorUnlockedByTier(LevelTier tier)
    {
        switch (tier)
        {
            case LevelTier.Silver: return 1;
            case LevelTier.Gold: return 2;
            case LevelTier.Diamond: return 3;
            case LevelTier.God: return 4;
            default: return 0; // Bronze unlocks no new floor
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // DAY 34 — If a new-game flow set a pending type on SaveSlotManager,
        // apply it here BEFORE Start() so initial mana progression uses the
        // chosen type. Do NOT consume — DungeonSaveController still needs to
        // read the name from PendingNewGame in InitializeNewGame.
        var pending = SaveSlotManager.Instance?.PendingNewGame;
        if (pending != null)
        {
            dungeonType = pending.dungeonType;
            Debug.Log($"[DungeonCore] Applied pending new-game type: {dungeonType}");
        }
    }

    private void Start()
    {
        currentMana = MaxMana;

        if (dungeonType == DungeonType.None)
            Debug.LogWarning("DungeonCore: DungeonType is None.");

        NotifyAll();
    }

    private void NotifyAll()
    {
        OnManaChanged?.Invoke(currentMana, MaxMana);
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
        OnXPChanged?.Invoke(currentXP, XPToNextLevel);
        OnLevelChanged?.Invoke(dungeonLevel);
        OnNotorietyChanged?.Invoke(notoriety);
        OnReputationChanged?.Invoke(reputation);
        OnGoldChanged?.Invoke(currentGold);
        OnResearchChanged?.Invoke(researchPoints);
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        OnStairCreditsChanged?.Invoke(stairCredits);
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        RegenerateMana();
        AccrueTrophyNotoriety();
        DecayNotoriety();
        TickInstability();
    }

    // ── Relocation ────────────────────────────────────────────────

    public void Relocate(FloorRoot destination, Vector3Int destCell)
    {
        if (destination == null) { Debug.LogError("[DungeonCore] Relocate: null destination."); return; }
        if (IsInTransit) { Debug.LogWarning("[DungeonCore] Already in transit."); return; }
        var transit = gameObject.AddComponent<DungeonCoreTransit>();
        transit.Begin(destination, destCell);
    }

    // ── Mana ─────────────────────────────────────────────────────

    private void RegenerateMana()
    {
        if (currentMana >= MaxMana) return;
        // CurrentManaRegen folds in the census mana sum (rooms + trophies); use it
        // so those effects actually regenerate mana, not just show on the readout.
        float regen = CurrentManaRegen * Time.deltaTime;
        currentMana = Mathf.Min(currentMana + regen, MaxMana);
        OnManaChanged?.Invoke(currentMana, MaxMana);
    }

    /// <summary>Pokes the regen readout after a census change (the rate is a live property).</summary>
    public void NotifyManaRegenDisplay()
    {
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
    }

    public bool SpendMana(float amount)
    {
        if (currentMana < amount) return false;
        currentMana -= amount;
        OnManaChanged?.Invoke(currentMana, MaxMana);
        return true;
    }

    public void AddMana(float amount)
    {
        currentMana = Mathf.Min(currentMana + amount, MaxMana);
        OnManaChanged?.Invoke(currentMana, MaxMana);
    }

    // ── XP & Level ───────────────────────────────────────────────

    public void AddXP(float amount)
    {
        currentXP += amount;
        OnXPChanged?.Invoke(currentXP, XPToNextLevel);
        CheckLevelUp();
    }

    public void AddGold(int amount)
    {
        // Treasury cap: incoming gold clamps at the census cap; gold already held
        // is never confiscated when the cap shrinks (the effective cap floors at
        // the current holding).
        int cap = Mathf.Max(RoomEffectCensus.GoldCap, currentGold);
        int before = currentGold;
        currentGold = Mathf.Min(currentGold + amount, cap);
        if (before + amount > cap) AnnounceCapClamp();
        else if (currentGold < RoomEffectCensus.GoldCap) capClampAnnounced = false;
        OnGoldChanged?.Invoke(currentGold);
    }

    private void AnnounceCapClamp()
    {
        if (capClampAnnounced) return;
        capClampAnnounced = true;
        AlertsLog.Instance?.AddAlert(
            "The hoard presses against its bounds. Coin beyond this is turned away; raise deeper vaults.",
            transform.position);
    }

    /// <summary>Spends gold if affordable. Returns false (no change) if too poor.</summary>
    public bool TrySpendGold(int cost)
    {
        if (cost <= 0) return true;
        if (currentGold < cost) return false;
        currentGold -= cost;
        OnGoldChanged?.Invoke(currentGold);
        return true;
    }

    public void AddResearch(int amount)
    {
        researchPoints += amount;
        OnResearchChanged?.Invoke(researchPoints);
    }

    /// <summary>Spends research points if affordable. Returns false (no change) otherwise.</summary>
    public bool TrySpendResearch(int cost)
    {
        if (cost <= 0) return true;
        if (researchPoints < cost) return false;
        researchPoints -= cost;
        OnResearchChanged?.Invoke(researchPoints);
        return true;
    }

    public bool TrySpendCapacity(int cost)
    {
        if (usedCapacity + cost > MaxCapacity) return false;
        usedCapacity += cost;
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        return true;
    }

    public void ReturnCapacity(int cost)
    {
        usedCapacity = Mathf.Max(0, usedCapacity - cost);
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
    }

    /// <summary>Re-evaluate whether a level-up is available - called after the endgame
    /// climax is survived so the Diamond 3 -> God 1 ascension can light up.</summary>
    public void RefreshLevelUpAvailability() => CheckLevelUp();

    private void CheckLevelUp()
    {
        if (LevelUpAvailable) return;

        // Diamond 3 → God 1 transition is gated by a TBD special requirement.
        if (LevelTierUtil.IsDiamondCap(dungeonLevel) && !(EndgameClimax.Instance != null && EndgameClimax.Instance.Ascended)) return;

        if (currentXP >= CalculateXPThreshold(dungeonLevel))
        {
            LevelUpAvailable = true;
            OnLevelUpAvailable?.Invoke();
        }
    }

    public void ConfirmLevelUp()
    {
        if (!LevelUpAvailable) return;

        // Block the Diamond 3 → God 1 transition until special requirement is defined.
        if (LevelTierUtil.IsDiamondCap(dungeonLevel) && !(EndgameClimax.Instance != null && EndgameClimax.Instance.Ascended))
        {
            Debug.Log("[DungeonCore] God 1 (ascension) requires surviving the endgame climax.");
            return;
        }

        // Apply the level-up.
        bool isTierBoundary = LevelTierUtil.IsTierBoundary(dungeonLevel);

        currentXP -= CalculateXPThreshold(dungeonLevel);
        dungeonLevel = Mathf.Min(dungeonLevel + 1, LevelTierUtil.MaxFlatLevel);
        LevelUpAvailable = false;

        // Tier-up grants a stair credit (Bronze 10 → Silver 1, etc.).
        if (isTierBoundary)
        {
            stairCredits++;
            OnStairCreditsChanged?.Invoke(stairCredits);
            Debug.Log($"[DungeonCore] Tier up to {LevelDisplayName} — stair credit granted (now {stairCredits}).");
        }

        OnLevelUp?.Invoke(dungeonLevel);
        OnLevelChanged?.Invoke(dungeonLevel);
        OnManaChanged?.Invoke(currentMana, MaxMana);
        OnXPChanged?.Invoke(currentXP, XPToNextLevel);
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);

        CheckLevelUp();
    }

    private float CalculateXPThreshold(int level)
        => baseXPToLevel * Mathf.Pow(level, xpScalingExponent);

    /// <summary>Consumes one stair credit. Returns true if a credit was available.</summary>
    public bool TryConsumeStairCredit()
    {
        if (stairCredits <= 0) return false;
        stairCredits--;
        OnStairCreditsChanged?.Invoke(stairCredits);
        return true;
    }

    /// <summary>Returns a credit (e.g. if stair placement failed downstream).</summary>
    public void RefundStairCredit()
    {
        stairCredits++;
        OnStairCreditsChanged?.Invoke(stairCredits);
    }

    // ── Notoriety / Reputation ────────────────────────────────────

    public void AddNotoriety(float amount)
    {
        notoriety = Mathf.Max(0f, notoriety + amount);
        timeSinceLastKill = 0f;
        OnNotorietyChanged?.Invoke(notoriety);
    }

    public void AddReputation(float amount)
    {
        reputation += amount;
        OnReputationChanged?.Invoke(reputation);
    }

    /// <summary>An Inspector left with findings — flag for the future escalation path.</summary>
    public bool InspectorFindingsPending { get; private set; }
    public void FlagInspectorFindings()
    {
        InspectorFindingsPending = true;
        Debug.Log("[DungeonCore] Inspector departed with findings (escalation path, later).");
    }

    /// <summary>Displayed trophies can trickle notoriety upward (canon 23). Runs before
    /// decay each frame; the decay cooldown still governs the downward pull separately.</summary>
    private void AccrueTrophyNotoriety()
    {
        float rate = RoomEffectCensus.NotorietyPerSecond;
        if (rate <= 0f) return;
        notoriety += rate * Time.deltaTime;
        OnNotorietyChanged?.Invoke(notoriety);
    }

    private void DecayNotoriety()
    {
        if (notoriety <= 0f) return;
        timeSinceLastKill += Time.deltaTime;
        if (timeSinceLastKill < notorietyDecayCooldown) return;
        notoriety = Mathf.Max(0f, notoriety - notorietyDecayPerSecond * Time.deltaTime);
        OnNotorietyChanged?.Invoke(notoriety);
    }

    private void TickInstability()
    {
        if (!isUnstable) return;
        instabilityTimer -= Time.deltaTime;
        OnInstabilityTick?.Invoke(instabilityTimer);
        if (instabilityTimer <= 0f)
        {
            isUnstable = false;
            breachCount = 0;
            OnCoreStabilised?.Invoke();
        }
    }

    // ── Tile Influence ────────────────────────────────────────────

    public void AddClaimedTiles(int count)
    {
        claimedTileCount += count;
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
    }

    /// <summary>PHASE 3 — Decrements the count of claimed tiles. Reduces mana regen.</summary>
    public void RemoveClaimedTiles(int count)
    {
        claimedTileCount = Mathf.Max(0, claimedTileCount - count);
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
    }

    [Obsolete("Phase 3 — Use AddClaimedTiles. Old name retained as safety net.")]
    public void AddOwnedTiles(int count) => AddClaimedTiles(count);

    [Obsolete("Phase 3 — Use RemoveClaimedTiles. Old name retained as safety net.")]
    public void RemoveOwnedTiles(int count) => RemoveClaimedTiles(count);

    public void SetDungeonType(DungeonType type) => dungeonType = type;

    // ── Core Destruction ──────────────────────────────────────────

    public void DestroyCore()
    {
        if (IsInTransit)
        {
            Debug.Log("[DungeonCore] BREACH DURING TRANSIT — instant game over.");
            OnGameOver?.Invoke();
            return;
        }

        if (Time.time - lastBreachTime < 5f) return;
        lastBreachTime = Time.time;
        breachCount++;

        if (breachCount == 1)
        {
            isUnstable = true;
            instabilityTimer = instabilityDuration;
            currentXP = Mathf.Max(0f, currentXP - xpPenaltyOnBreach);
            OnXPChanged?.Invoke(currentXP, XPToNextLevel);

            // Influence recede is handled per floor by InfluenceField, which
            // subscribes to OnFirstBreach (fired below) and unclaims territory
            // beyond the suppressed reach on every floor. Mined tunnels persist.

            OnFirstBreach?.Invoke();
            OnCoreDestroyed?.Invoke();
        }
        else
        {
            isUnstable = false;
            OnGameOver?.Invoke();
        }
    }

    // ── Save / Load ───────────────────────────────────────────────

    public DungeonCoreSaveData GetSaveData()
    {
        Debug.Log($"[DungeonCore] GetSaveData: dungeonLevel={dungeonLevel}, display={LevelDisplayName}");
        return new DungeonCoreSaveData
        {
            dungeonType = this.dungeonType,
            dungeonLevel = this.dungeonLevel,
            currentXP = this.currentXP,
            notoriety = this.notoriety,
            reputation = this.reputation,
            currentMana = this.currentMana,
            claimedTileCount = this.claimedTileCount,
            ownedTileCount = 0,
            usedCapacity = this.usedCapacity,
            gold = this.currentGold,
            researchPoints = this.researchPoints,
            levelUpAvailable = this.LevelUpAvailable,
            isUnstable = this.isUnstable,
            instabilityTimer = this.instabilityTimer,
            breachCount = this.breachCount,
            stairCredits = this.stairCredits,
        };
    }

    public void LoadSaveData(DungeonCoreSaveData data)
    {
        dungeonType = data.dungeonType;
        dungeonLevel = Mathf.Clamp(data.dungeonLevel, 1, LevelTierUtil.MaxFlatLevel);
        Debug.Log($"[DungeonCore] LoadSaveData: dungeonLevel raw={data.dungeonLevel}, clamped={dungeonLevel}, display={LevelDisplayName}");
        currentXP = data.currentXP;
        notoriety = data.notoriety;
        reputation = data.reputation;
        currentMana = Mathf.Min(data.currentMana, MaxMana);
        claimedTileCount = data.claimedTileCount;
        LevelUpAvailable = data.levelUpAvailable;
        usedCapacity = data.usedCapacity;
        currentGold = data.gold;
        researchPoints = data.researchPoints;
        isUnstable = data.isUnstable;
        instabilityTimer = data.instabilityTimer;
        breachCount = data.breachCount;
        stairCredits = data.stairCredits;

        NotifyAll();
        if (isUnstable) OnFirstBreach?.Invoke();
        if (LevelUpAvailable) OnLevelUpAvailable?.Invoke();
    }
}

[Serializable]
public class DungeonCoreSaveData
{
    public DungeonType dungeonType;
    public int dungeonLevel;
    public float currentXP;
    public float notoriety;
    public float reputation;
    public float currentMana;
    public int claimedTileCount;
    public int ownedTileCount; //obsolete
    public bool levelUpAvailable;
    public int usedCapacity;
    public bool isUnstable;
    public float instabilityTimer;
    public int breachCount;
    public int gold;
    public int researchPoints; 
    public int stairCredits;
}