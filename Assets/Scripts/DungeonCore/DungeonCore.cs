using System;
using UnityEngine;

[DefaultExecutionOrder(-20)]
public class DungeonCore : MonoBehaviour
{
    public static DungeonCore Instance { get; private set; }

    // ── Dungeon Identity ─────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private DungeonType dungeonType = DungeonType.None;

    // ── Mana ─────────────────────────────────────────────────────
    [Header("Mana")]
    [SerializeField] private float baseMana = 100f;
    [SerializeField] private float manaPerLevel = 25f;
    [SerializeField] private float baseRegenPerSecond = 1f;
    [SerializeField] private float regenPerTile = 0.1f;

    // ── Monster Capacity ──────────────────────────────────────────
    [Header("Monster Capacity")]
    [SerializeField] private int baseCapacity = 100;
    [SerializeField] private int capacityPerLevel = 50;

    // ── XP / Level ───────────────────────────────────────────────
    [Header("XP & Level")]
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

    // ── Breach state ──────────────────────────────────────────────
    private bool isUnstable = false;
    private float instabilityTimer = 0f;
    private int breachCount = 0;
    private float lastBreachTime = -999f;
    private float timeSinceLastKill = 0f;

    // ── Runtime State ─────────────────────────────────────────────
    private float currentMana;
    private float currentXP;
    private int dungeonLevel = 1;
    private int ownedTileCount = 0;
    private int usedCapacity = 0;
    private int currentGold = 0;

    // ── Events ───────────────────────────────────────────────────
    public event Action<float, float> OnManaChanged;
    public event Action<float> OnManaRegenChanged;
    public event Action<float, float> OnXPChanged;
    public event Action<int> OnLevelUp;
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

    // ── Public Reads ──────────────────────────────────────────────
    public DungeonType DungeonType => dungeonType;
    public int DungeonLevel => dungeonLevel;
    public float CurrentMana => currentMana;
    public float MaxMana => baseMana + (dungeonLevel - 1) * manaPerLevel;
    public float CurrentManaRegen => baseRegenPerSecond + ownedTileCount * regenPerTile;
    public float CurrentXP => currentXP;
    public float XPToNextLevel => CalculateXPThreshold(dungeonLevel);
    public float Notoriety => notoriety;
    public float Reputation => reputation;
    public int OwnedTileCount => ownedTileCount;
    public int MaxCapacity => baseCapacity + (dungeonLevel - 1) * capacityPerLevel;
    public int UsedCapacity => usedCapacity;
    public int FreeCapacity => MaxCapacity - usedCapacity;
    public bool LevelUpAvailable { get; private set; }
    public bool IsUnstable => isUnstable;
    public float InstabilityTimer => instabilityTimer;
    public float InstabilityDuration => instabilityDuration;
    public int Gold => currentGold;

    /// <summary>True while a DungeonCoreTransit is running on this GameObject.</summary>
    public bool IsInTransit => GetComponent<DungeonCoreTransit>() != null
                            && GetComponent<DungeonCoreTransit>().IsActive;

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

        NotifyManaChanged();
        OnManaRegenChanged?.Invoke(CurrentManaRegen);
        NotifyXPChanged();
        OnReputationChanged?.Invoke(reputation);
        OnGoldChanged?.Invoke(currentGold);
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        RegenerateMana();
        DecayNotoriety();
        TickInstability();
    }

    // ── Relocation ────────────────────────────────────────────────

    /// <summary>
    /// Begins a 30-second relocation transit to the given floor and cell.
    /// Adds a DungeonCoreTransit component that drives the sequence and
    /// removes itself on completion. Refuses if a transit is already running.
    /// </summary>
    public void Relocate(FloorRoot destination, Vector3Int destCell)
    {
        if (destination == null)
        {
            Debug.LogError("[DungeonCore] Relocate: destination floor is null.");
            return;
        }

        if (IsInTransit)
        {
            Debug.LogWarning("[DungeonCore] Relocate: already in transit.");
            return;
        }

        var transit = gameObject.AddComponent<DungeonCoreTransit>();
        transit.Begin(destination, destCell);
    }

    // ── Mana ─────────────────────────────────────────────────────

    private void RegenerateMana()
    {
        if (currentMana >= MaxMana) return;
        float regen = (baseRegenPerSecond + ownedTileCount * regenPerTile) * Time.deltaTime;
        currentMana = Mathf.Min(currentMana + regen, MaxMana);
        NotifyManaChanged();
    }

    public bool SpendMana(float amount)
    {
        if (currentMana < amount) return false;
        currentMana -= amount;
        NotifyManaChanged();
        return true;
    }

    public void AddMana(float amount)
    {
        currentMana = Mathf.Min(currentMana + amount, MaxMana);
        NotifyManaChanged();
    }

    // ── XP & Level ───────────────────────────────────────────────

    public void AddXP(float amount)
    {
        currentXP += amount;
        NotifyXPChanged();
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
        if (!LevelUpAvailable && currentXP >= CalculateXPThreshold(dungeonLevel))
        {
            LevelUpAvailable = true;
            OnLevelUpAvailable?.Invoke();
        }
    }

    public void ConfirmLevelUp()
    {
        if (!LevelUpAvailable) return;
        currentXP -= CalculateXPThreshold(dungeonLevel);
        dungeonLevel++;
        LevelUpAvailable = false;
        OnLevelUp?.Invoke(dungeonLevel);
        NotifyManaChanged();
        NotifyXPChanged();
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        CheckLevelUp();
    }

    private float CalculateXPThreshold(int level)
        => baseXPToLevel * Mathf.Pow(level, xpScalingExponent);

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
            Debug.Log("[DungeonCore] Instability resolved.");
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

    // ── Identity ──────────────────────────────────────────────────

    public void SetDungeonType(DungeonType type) => dungeonType = type;

    // ── Core Destruction ──────────────────────────────────────────

    public void DestroyCore()
    {
        // During transit, any breach is instant game over.
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
            NotifyXPChanged();

            int coreFloor = FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0;
            var floor = FloorManager.Instance?.GetFloor(coreFloor);
            if (floor?.TileInfluence != null)
            {
                Vector3Int coreCell = floor.TileInfluence.WorldToCell(transform.position);
                floor.TileInfluence.ShrinkInfluenceAroundCore(coreCell, influenceShrinkRadius);
            }

            Debug.Log("[DungeonCore] FIRST BREACH.");
            OnFirstBreach?.Invoke();
            OnCoreDestroyed?.Invoke();
        }
        else
        {
            Debug.Log("[DungeonCore] SECOND BREACH — game over.");
            isUnstable = false;
            OnGameOver?.Invoke();
        }
    }

    // ── Save / Load ───────────────────────────────────────────────

    public DungeonCoreSaveData GetSaveData() => new DungeonCoreSaveData
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
        corePosX = transform.position.x,
        corePosY = transform.position.y,
    };

    public void LoadSaveData(DungeonCoreSaveData data)
    {
        dungeonType = data.dungeonType;
        dungeonLevel = data.dungeonLevel;
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
        transform.position = new Vector3(data.corePosX, data.corePosY, transform.position.z);

        OnManaRegenChanged?.Invoke(CurrentManaRegen);
        NotifyManaChanged();
        NotifyXPChanged();
        OnNotorietyChanged?.Invoke(notoriety);
        OnReputationChanged?.Invoke(reputation);
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        OnGoldChanged?.Invoke(currentGold);
        if (isUnstable) OnFirstBreach?.Invoke();
        if (LevelUpAvailable) OnLevelUpAvailable?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private void NotifyManaChanged() => OnManaChanged?.Invoke(currentMana, MaxMana);
    private void NotifyXPChanged() => OnXPChanged?.Invoke(currentXP, XPToNextLevel);
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
    public float corePosX;
    public float corePosY;
}