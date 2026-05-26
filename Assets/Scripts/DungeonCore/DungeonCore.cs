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

    // ── XP / Level ───────────────────────────────────────────────
    [Header("XP & Level")]
    [SerializeField] private float baseXPToLevel = 100f;
    [SerializeField] private float xpScalingExponent = 1.5f;

    // ── Notoriety ────────────────────────────────────────────────
    [Header("Notoriety")]
    [SerializeField] private float notoriety = 0f;

    // ── Runtime State ────────────────────────────────────────────
    private float currentMana;
    private float currentXP;
    private int dungeonLevel = 1;
    private int ownedTileCount = 0;

    // ── Events ───────────────────────────────────────────────────
    public event Action<float, float> OnManaChanged;        // (current, max)
    public event Action<float, float> OnXPChanged;          // (current, xpToNext)
    public event Action<int> OnLevelUp;            // (newLevel)
    public event Action OnLevelUpAvailable;   // UI prompt
    public event Action<float> OnNotorietyChanged;   // (total notoriety)
    public event Action OnCoreDestroyed;

    // ── Public Reads ─────────────────────────────────────────────
    public DungeonType DungeonType => dungeonType;
    public int DungeonLevel => dungeonLevel;
    public float CurrentMana => currentMana;
    public float MaxMana => baseMana + (dungeonLevel - 1) * manaPerLevel;
    public float CurrentXP => currentXP;
    public float XPToNextLevel => CalculateXPThreshold(dungeonLevel);
    public float Notoriety => notoriety;
    public int OwnedTileCount => ownedTileCount;
    public bool LevelUpAvailable { get; private set; }

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
        OnGoldChanged?.Invoke(currentGold);
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        RegenerateMana();
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

        // In case banked XP already meets the next threshold
        CheckLevelUp();
    }

    private float CalculateXPThreshold(int level)
    {
        return baseXPToLevel * Mathf.Pow(level, xpScalingExponent);
    }

    // ── Notoriety ────────────────────────────────────────────────

    /// <summary>Called by Pilgrim visits, dungeon milestones, etc.</summary>
    public void AddNotoriety(float amount)
    {
        notoriety += amount;
        OnNotorietyChanged?.Invoke(notoriety);
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
        OnCoreDestroyed?.Invoke();
    }

    // ── Save / Load ──────────────────────────────────────────────

    public DungeonCoreSaveData GetSaveData() => new DungeonCoreSaveData
    {
        dungeonType = this.dungeonType,
        dungeonLevel = this.dungeonLevel,
        currentXP = this.currentXP,
        notoriety = this.notoriety,
        currentMana = this.currentMana,
        ownedTileCount = this.ownedTileCount,
        levelUpAvailable = this.LevelUpAvailable
    };

    public void LoadSaveData(DungeonCoreSaveData data)
    {
        dungeonType = data.dungeonType;
        dungeonLevel = data.dungeonLevel;
        currentXP = data.currentXP;
        notoriety = data.notoriety;
        currentMana = Mathf.Min(data.currentMana, MaxMana);
        ownedTileCount = data.ownedTileCount;
        LevelUpAvailable = data.levelUpAvailable;

        NotifyManaChanged();
        NotifyXPChanged();
        OnNotorietyChanged?.Invoke(notoriety);

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
    public float currentMana;
    public int ownedTileCount;
    public bool levelUpAvailable;
}