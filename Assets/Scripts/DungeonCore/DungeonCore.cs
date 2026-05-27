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

    // ── Monster Capacity ────────────────────────────────────────────────
    [Header("Monster Capacity")]
    [SerializeField] private int baseCapacity = 100;
    [SerializeField] private int capacityPerLevel = 50;

    // ── XP / Level ───────────────────────────────────────────────
    [Header("XP & Level")]
    [SerializeField] private float baseXPToLevel = 100f;
    [SerializeField] private float xpScalingExponent = 1.5f;

    // ── Notoriety / Reputation ────────────────────────────────────────────────
    [Header("Notoriety")]
    [SerializeField] private float notoriety = 0f;
    [SerializeField] private float notorietyDecayPerSecond = 0.1f;
    [Header("Reputation")]
    [SerializeField] private float reputation = 0f;

    // ── Breach Controls ───────────────────────────────────────────────
    [Header("Two-Strike Breach System")]
    [SerializeField] private float instabilityDuration = 60f;  // seconds to survive after first breach
    [SerializeField] private float xpPenaltyOnBreach = 50f;
    [SerializeField] private float influenceShrinkRadius = 3f;  // tiles lost around breach point

    // breach state
    private bool isUnstable = false;
    private float instabilityTimer = 0f;
    private int breachCount = 0;

    // ── Runtime State ────────────────────────────────────────────
    private float currentMana;
    private float currentXP;
    private int dungeonLevel = 1;
    private int ownedTileCount = 0;
    private int usedCapacity = 0;

    // ── Events ───────────────────────────────────────────────────
    public event Action<float, float> OnManaChanged;        // (current, max)
    public event Action<float, float> OnXPChanged;          // (current, xpToNext)
    public event Action<int> OnLevelUp;            // (newLevel)
    public event Action OnLevelUpAvailable;   // UI prompt
    public event Action<float> OnNotorietyChanged;   // (total notoriety)
    public event Action<float> OnReputationChanged;  // (total reputation)
    public event Action<int, int> OnCapacityChanged; // (used, max)
    public event Action OnCoreDestroyed;
    public event Action<float> OnInstabilityTick;   // (seconds remaining)
    public event Action OnFirstBreach;
    public event Action OnCoreStabilised;
    public event Action OnGameOver;

    // ── Public Reads ─────────────────────────────────────────────
    public DungeonType DungeonType => dungeonType;
    public int DungeonLevel => dungeonLevel;
    public float CurrentMana => currentMana;
    public float MaxMana => baseMana + (dungeonLevel - 1) * manaPerLevel;
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

    // ── Materials ─────────────────────────────────────────────

    // ── Gold ──────────────────────────────────────────────────────
    private int currentGold = 0;
    public int Gold => currentGold;
    public event Action<int> OnGoldChanged;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        currentMana = MaxMana;

        if (dungeonType == DungeonType.None)
            Debug.LogWarning("DungeonCore: DungeonType is None. Call SetDungeonType() from the tutorial sequence.");

        NotifyManaChanged();
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

    // ── Mana ─────────────────────────────────────────────────────

    private void RegenerateMana()
    {
        if (currentMana >= MaxMana) return;

        float regen = (baseRegenPerSecond + ownedTileCount * regenPerTile) * Time.deltaTime;
        currentMana = Mathf.Min(currentMana + regen, MaxMana);
        NotifyManaChanged();
    }

    /// <summary>Returns true and deducts mana if enough is available.</summary>
    public bool SpendMana(float amount)
    {
        if (currentMana < amount) return false;
        currentMana -= amount;
        NotifyManaChanged();
        return true;
    }

    /// <summary>Adds mana directly (e.g. GiftGiver adventurer reward).</summary>
    public void AddMana(float amount)
    {
        currentMana = Mathf.Min(currentMana + amount, MaxMana);
        NotifyManaChanged();
    }

    // ── XP & Level ───────────────────────────────────────────────

    /// <summary>Called by any NPC death handler within dungeon influence.</summary>
    public void AddXP(float amount)
    {
        currentXP += amount;
        NotifyXPChanged();
        CheckLevelUp();
    }

    /// <summary>Called by DroppedLoot on auto-absorption, and GiftGiver adventurers.</summary>
    public void AddGold(int amount)
    {
        currentGold += amount;
        OnGoldChanged?.Invoke(currentGold);
        Debug.Log($"[DungeonCore] Gold +{amount}. Total: {currentGold}");
    }

    /// <summary>Returns true if the capacity cost can be met and reserves it.</summary>
    public bool TrySpendCapacity(int cost)
    {
        if (usedCapacity + cost > MaxCapacity) return false;
        usedCapacity += cost;
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        return true;
    }

    /// <summary>Returns capacity when a spawner or monster is removed.</summary>
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

    /// <summary>Called by the player confirming the level-up from UI.</summary>
    public void ConfirmLevelUp()
    {
        if (!LevelUpAvailable) return;

        currentXP -= CalculateXPThreshold(dungeonLevel);
        dungeonLevel++;
        LevelUpAvailable = false;

        OnLevelUp?.Invoke(dungeonLevel);
        NotifyManaChanged(); // max mana went up
        NotifyXPChanged();
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);

        // In case banked XP already meets the next threshold
        CheckLevelUp();
    }

    private float CalculateXPThreshold(int level)
    {
        return baseXPToLevel * Mathf.Pow(level, xpScalingExponent);
    }

    // ── Notoriety / Reputation ────────────────────────────────────────────────

    /// <summary>Called by Pilgrim visits, dungeon milestones, etc.</summary>
    public void AddNotoriety(float amount)
    {
        notoriety += amount;
        timeSinceLastKill = 0f;   // reset decay cooldown
        OnNotorietyChanged?.Invoke(notoriety);
    }

    /// <summary>Called when an adventurer exits alive, Pilgrim visits, etc.</summary>
    public void AddReputation(float amount)
    {
        reputation += amount;
        OnReputationChanged?.Invoke(reputation);
    }

    [Header("Notoriety Decay")]
    [SerializeField] private float notorietyDecayCooldown = 10f; // seconds after last kill before decay starts

    private float timeSinceLastKill = 0f;

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
            // Survived the timer — stabilise
            isUnstable = false;
            breachCount = 0;
            Debug.Log("[DungeonCore] Instability resolved — core stabilised.");
            OnCoreStabilised?.Invoke();
        }
    }

    // ── Tile Influence ───────────────────────────────────────────

    /// <summary>Called by the tile system when the dungeon claims new tiles.</summary>
    public void AddOwnedTiles(int count)
    {
        ownedTileCount += count;
    }

    /// <summary>Called by the tile system when tiles are lost.</summary>
    public void RemoveOwnedTiles(int count)
    {
        ownedTileCount = Mathf.Max(0, ownedTileCount - count);
    }

    // ── Identity ─────────────────────────────────────────────────

    /// <summary>Called once during the tutorial death → dungeon type selection sequence.</summary>
    public void SetDungeonType(DungeonType type)
    {
        dungeonType = type;
    }

    // ── Core Destruction ─────────────────────────────────────────

    /// <summary>Called by a Destroyer adventurer reaching the Core Room.</summary>
    public void DestroyCore()
    {
        breachCount++;

        if (breachCount == 1)
        {
            // First breach — start instability
            isUnstable = true;
            instabilityTimer = instabilityDuration;

            // XP penalty
            currentXP = Mathf.Max(0f, currentXP - xpPenaltyOnBreach);
            NotifyXPChanged();

            // Shrink influence around core
            TileInfluenceManager.Instance?.ShrinkInfluenceAroundCore(influenceShrinkRadius);

            Debug.Log("[DungeonCore] FIRST BREACH — instability timer started.");
            OnFirstBreach?.Invoke();
            OnCoreDestroyed?.Invoke(); // keep existing event for any other listeners
        }
        else
        {
            // Second breach before timer cleared — game over
            Debug.Log("[DungeonCore] SECOND BREACH — game over.");
            isUnstable = false;
            OnGameOver?.Invoke();
        }
    }

    // ── Save / Load ──────────────────────────────────────────────

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
        levelUpAvailable = this.LevelUpAvailable,
        isUnstable = this.isUnstable,
        instabilityTimer = this.instabilityTimer,
        breachCount = this.breachCount
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

        NotifyManaChanged();
        NotifyXPChanged();
        OnNotorietyChanged?.Invoke(notoriety);
        OnReputationChanged?.Invoke(reputation);
        usedCapacity = data.usedCapacity;
        OnCapacityChanged?.Invoke(usedCapacity, MaxCapacity);
        isUnstable = data.isUnstable;
        instabilityTimer = data.instabilityTimer;
        breachCount = data.breachCount;
        if (isUnstable) OnFirstBreach?.Invoke();

        if (LevelUpAvailable)
            OnLevelUpAvailable?.Invoke();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private void NotifyManaChanged() =>
        OnManaChanged?.Invoke(currentMana, MaxMana);

    private void NotifyXPChanged() =>
        OnXPChanged?.Invoke(currentXP, XPToNextLevel);
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
}