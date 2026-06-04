using System;
using UnityEngine;

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
    [SerializeField] private float influenceShrinkRadius = 3f;

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
    private int ownedTileCount = 0;
    private int usedCapacity = 0;
    private int currentGold = 0;

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
    public event Action<int> OnStairCreditsChanged;

    // ── Public Reads ──────────────────────────────────────────────
    public DungeonType DungeonType => dungeonType;
    public int DungeonLevel => dungeonLevel;
    public LevelTier CurrentTier => LevelTierUtil.FromFlatLevel(dungeonLevel).tier;
    public int CurrentRank => LevelTierUtil.FromFlatLevel(dungeonLevel).rank;
    public string LevelDisplayName => LevelTierUtil.DisplayName(dungeonLevel);
    public float CurrentMana => currentMana;
    public float MaxMana => progression.ManaAt(dungeonLevel);
    public float CurrentManaRegen => baseRegenPerSecond + ownedTileCount * regenPerTile;
    public float CurrentXP => currentXP;
    public float XPToNextLevel => CalculateXPThreshold(dungeonLevel);
    public float Notoriety => notoriety;
    public float Reputation => reputation;
    public int OwnedTileCount => ownedTileCount;
    public int MaxCapacity => progression.CapacityAt(dungeonLevel);
    public int UsedCapacity => usedCapacity;
    public int FreeCapacity => MaxCapacity - usedCapacity;
    public bool LevelUpAvailable { get; private set; }
    public bool IsUnstable => isUnstable;
    public float InstabilityTimer => instabilityTimer;
    public float InstabilityDuration => instabilityDuration;
    public int Gold => currentGold;
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
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        OnStairCreditsChanged?.Invoke(stairCredits);
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        RegenerateMana();
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
        float regen = (baseRegenPerSecond + ownedTileCount * regenPerTile) * Time.deltaTime;
        currentMana = Mathf.Min(currentMana + regen, MaxMana);
        OnManaChanged?.Invoke(currentMana, MaxMana);
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
        currentGold += amount;
        OnGoldChanged?.Invoke(currentGold);
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

    private void CheckLevelUp()
    {
        if (LevelUpAvailable) return;

        // Diamond 3 → God 1 transition is gated by a TBD special requirement.
        if (LevelTierUtil.IsDiamondCap(dungeonLevel)) return;

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
        if (LevelTierUtil.IsDiamondCap(dungeonLevel))
        {
            Debug.Log("[DungeonCore] God 1 requires special unlock (TBD).");
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
        notoriety += amount;
        timeSinceLastKill = 0f;
        OnNotorietyChanged?.Invoke(notoriety);
    }

    public void AddReputation(float amount)
    {
        reputation += amount;
        OnReputationChanged?.Invoke(reputation);
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

    public void AddOwnedTiles(int count)
    {
        ownedTileCount += count;
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
    }

    public void RemoveOwnedTiles(int count)
    {
        ownedTileCount = Mathf.Max(0, ownedTileCount - count);
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
    }

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

            int coreFloor = FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0;
            var floor = FloorManager.Instance?.GetFloor(coreFloor);
            if (floor?.TileInfluence != null)
            {
                Vector3Int coreCell = floor.TileInfluence.WorldToCell(transform.position);
                floor.TileInfluence.ShrinkInfluenceAroundCore(coreCell, influenceShrinkRadius);
            }

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
            ownedTileCount = this.ownedTileCount,
            usedCapacity = this.usedCapacity,
            gold = this.currentGold,
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
        ownedTileCount = data.ownedTileCount;
        LevelUpAvailable = data.levelUpAvailable;
        usedCapacity = data.usedCapacity;
        currentGold = data.gold;
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
    public int ownedTileCount;
    public bool levelUpAvailable;
    public int usedCapacity;
    public bool isUnstable;
    public float instabilityTimer;
    public int breachCount;
    public int gold;
    public int stairCredits;
}