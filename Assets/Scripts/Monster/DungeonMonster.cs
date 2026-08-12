using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon monster.
///
/// DAY 31 PART 3D — PATROL / IDLE / ATTACK-HERE
///   - Reads orders from spawner each frame (cheap pull-based model).
///   - State auto-resolves via DetermineDesiredState — only Attack overrides.
///   - Patrol: cycle through spawner.PatrolWaypoints with index that persists
///     through combat (pause-and-resume per W4).
///   - Idle: hold-at-final when PatrolLoop=false and final waypoint reached.
///     ScanForHostiles still runs.
///   - Attack-Here: when spawner.HasAttackTarget, monster moves to that cell
///     using Patrol state. On arrival, spawner.ClearAttackTarget() reverts to
///     underlying order mode (Patrol or Wander).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonMonster : MonoBehaviour, IMonsterTarget
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP = 30f;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float attackDamage = 5f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float knockbackSpeed = 8f;   // shove travel speed (units/sec)
    private Vector2 knockbackDir;
    private float knockbackRemaining;
    private AttackTelegraph telegraph;
    [SerializeField] private float detectionRange = 3f;

    [Header("Monster XP (Veteran System)")]
    [SerializeField] private float xpPerKill = 20f;
    [SerializeField] private float xpToVeteran = 100f;

    [Tooltip("maxHP multiplied by this when the monster ascends to veteran. " +
             "currentHP scales proportionally (no free heal).")]
    [Min(1f)][SerializeField] private float veteranHpMultiplier = 1.5f;

    [Tooltip("attackDamage multiplied by this on veteran promotion.")]
    [Min(1f)][SerializeField] private float veteranDamageMultiplier = 1.3f;

    [Tooltip("xpPerKill multiplied by this on veteran promotion — " +
             "veterans yield more XP to whoever fights them.")]
    [Min(1f)][SerializeField] private float veteranXpRewardMultiplier = 1.5f;

    [Tooltip("Sprite tint applied on veteran promotion. Gold by default.")]
    [SerializeField] private Color veteranTint = new Color(1f, 0.84f, 0.36f, 1f);

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 2.5f;
    [SerializeField] private float wanderWaitMin = 1f;
    [SerializeField] private float wanderWaitMax = 3f;

    [Header("Wild Wander (DAY 31 PART 2)")]
    [Range(0f, 1f)]
    [SerializeField] private float wildAggroOutwardChance = 0.3f;

    [Header("Patrol Tuning (DAY 31 PART 3D)")]
    [Tooltip("World-unit distance at which a waypoint is considered reached.")]
    [SerializeField] private float waypointArrivalDistance = 0.25f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private enum MonsterState { Wander, Patrol, Idle, Attack, DefendCore, Invade }
    private MonsterState state = MonsterState.Wander;
    private float tauntImmuneUntil;   // set when peeled off a taunt by a heavy ally hit
    [SerializeField, Min(0f)] private float tauntPeelDuration = 2.5f;

    [Header("Animation")]
    [Tooltip("Seconds to hold the body after death so the death clip can play before despawn. 0 = despawn immediately.")]
    [SerializeField] private float deathAnimSeconds = 0f;
    private EntityAnimationDriver animDriver;

    private float currentHP;
    private float monsterXP;
    private int killCount;
    private string killTitle;   // "Slayer of X" — earned by felling a named Hero; instance-only, not saved
    private bool isVeteran;
    public MonsterSpawner Spawner => spawner;

    // ── Selection highlight (runtime ring that follows the monster) ──
    private static Sprite selectionRingSprite;
    private SpriteRenderer selectionHighlight;

    /// <summary>Shows/hides a ring under this monster when its spawner is selected.</summary>
    public void SetSelected(bool on)
    {
        if (selectionHighlight == null)
        {
            if (!on) return;
            selectionHighlight = BuildSelectionHighlight();
        }
        if (selectionHighlight != null) selectionHighlight.enabled = on;
    }

    private SpriteRenderer BuildSelectionHighlight()
    {
        if (selectionRingSprite == null) selectionRingSprite = GenerateSelectionRing();
        var body = GetComponentInChildren<SpriteRenderer>();   // capture before adding our own
        var go = new GameObject("SelectionHighlight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = new Vector3(1.35f, 1.35f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = selectionRingSprite;
        if (body != null) { sr.sortingLayerID = body.sortingLayerID; sr.sortingOrder = body.sortingOrder - 1; }
        sr.color = new Color(0.36f, 0.94f, 0.45f, 0.85f);
        return sr;
    }

    private static Sprite GenerateSelectionRing()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f, outer = size * 0.5f, inner = outer * 0.78f;
        var clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                tex.SetPixel(x, y, (d <= outer && d >= inner) ? Color.white : clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
    private float lastAttackTime;

    // Stamina — pool/cost from MonsterDefinition; regen rates here.
    [Header("Stamina Regen (Day 41)")]
    [SerializeField] private float staminaRegenInCombat = 3f;
    [SerializeField] private float staminaRegenOutOfCombat = 15f;
    private float maxStamina = 0f;
    private float attackStaminaCost = 0f;
    private float currentStamina = 0f;

    // Class-aware target priority — cached from the definition.
    private TargetPriority targetPriority = TargetPriority.Nearest;
    private static readonly List<DungeonAdventurer> _advScanBuf = new();

    private IMonsterTarget target;

    // Necromancy — a necromancer reads its behaviour params from the definition.
    private bool isNecromancer;
    private float raiseCooldownRemaining;
    private bool isChanneling;
    private float channelRemaining;
    private Corpse channelTarget;
    private readonly List<MonsterSpawner> risenSpawners = new();
    // Transient raised minion: crumbles when this reaches 0 (0 = permanent).
    private float lifetimeRemaining = 0f;
    private MonsterSpawner spawner;
    private EntityStatusBars statusBars;
    private FloorRoot currentFloor;
    private BossVariantDefinition bossDefinition;
    private PromotionRank promotedRank = PromotionRank.None;
    private string promotedTitle;   // boss rank only; null for sub-boss and below

    // Wander
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private bool wanderWaiting;
    private float wanderWaitTimer;

    // Terrain & slow
    private float terrainSpeedMultiplier = 1f;
    private float slowMultiplier = 1f;
    private float roomDamageMultiplier = 1f;   // Throne Room damage buff (1 = none)
    private float globalDamageMultiplier = 1f; // Trophy Hall global buff (1 = none); set from census, read at the strike
    private float crowdDamageMultiplier = 1f;  // Pressed rule (1 = none); set by CrowdingController, read at the strike
    private bool isPressed;
    private Color preCrowdColour = Color.white;
    private static float sharedGlobalDamageMultiplier = 1f; // last census value; new monsters adopt it at Start
    private float slowTimer = 0f;

    // Trap step
    private Vector3Int lastTrapCheckCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

    // Wild monster state (DAY 31 PART 2)
    private int wildChamberId = -1;
    private List<Vector3Int> wildChamberCells;

    // DAY 31 — direct back-reference to the MonsterDefinition that spawned this
    // wild monster. Replaces the brittle prefab-name heuristic. Null for player monsters.
    private MonsterDefinition wildDefinition;
    public MonsterDefinition WildDefinition => wildDefinition;

    // -- Novelty registry --------------------------------------------
    // Living wonders: distinct novel species currently alive anywhere in
    // the dungeon. Species-distinct (a spawner's ten of a kind count
    // once) so a menagerie, not a herd, is what draws the crowd.
    // Consumed by AdventurerSpawner for sightseer weights and the
    // arrival interval.
    private static readonly Dictionary<string, int> novelAlive = new();
    public static int NovelSpeciesCount => novelAlive.Count;

    private string novelKey;

    private void RegisterNovelty()
    {
        // Mustered monsters resolve through their spawner; wild and
        // invader monsters carry the definition directly.
        var def = wildDefinition != null ? wildDefinition
            : spawner != null ? spawner.Definition : null;
        if (def == null || !def.novel) return;
        novelKey = def.monsterName;
        novelAlive.TryGetValue(novelKey, out int n);
        novelAlive[novelKey] = n + 1;
    }

    private void UnregisterNovelty()
    {
        if (novelKey == null) return;
        if (novelAlive.TryGetValue(novelKey, out int n))
        {
            if (n <= 1) novelAlive.Remove(novelKey);
            else novelAlive[novelKey] = n - 1;
        }
        novelKey = null;
    }

    [Header("Aggression")]
    [Tooltip("Override the GLOBAL monster aggression for this monster only. " +
             "Leave off to follow the global toggle (wild monsters always default Aggressive). " +
             "Set on a prefab to make it type-wide; set at runtime via SetAggressionOverride for a single monster.")]
    [SerializeField] private bool overrideGlobalAggression = false;
    [SerializeField] private MonsterAggression aggressionOverride = MonsterAggression.Normal;
    [Tooltip("A Defensive monster that takes damage retaliates for this many seconds.")]
    [SerializeField] private float defensiveRetaliationDuration = 6f;

    // Regen
    private float lastDamageTime = -9999f;

    /// <summary>True once this creature has taken a wound from the dungeon --
    /// a commanded monster, a trap, or a working. Gates the bestiary unlock in
    /// Die: you field what you DEFEAT, and a chamber an adventuring party
    /// cleared for you was not defeated by you. ANY dungeon damage counts
    /// rather than the killing blow, because wild monsters do not regenerate
    /// (wildRegenMultiplier defaults to 0) so a wound is permanent, and
    /// because a beast your monsters wore down should still count when an
    /// adventurer steals the last hit. Instance state, never saved: a wild
    /// monster's HP snapshot restores but its history does not, so a reload
    /// mid-fight asks the player to land one more blow.</summary>
    private bool dungeonDealtDamage;
    private float pendingHealDisplay = 0f;
    private float effectiveRegenPerSecond = 0f;
    private float effectiveRegenCooldown = 5f;
    private const float HEAL_DISPLAY_THRESHOLD = 1f;

    // Patrol (DAY 31 PART 3D)
    private int patrolIndex = 0;
    private Vector3 patrolMoveTarget;

    // DefendCore pathing
    private List<Vector3> defendCorePath = new();
    private int defendCorePathIndex = 0;
    private float defendCorePathRefreshTimer = 0f;
    private const float DefendCorePathRefreshInterval = 0.5f;

    // Invader pathing - a wild monster that seeks and breaches the core.
    private bool isInvader;
    private bool isClimaxInvader;
    [SerializeField] private float invaderBreachDistance = 1.5f;
    private List<Vector3> invadePath = new();
    private int invadePathIndex = 0;
    private float invadePathRefreshTimer = 0f;

    // Hungry-predator (wild-monster event) mode: hunts to sate hunger, then leaves via
    // the entrance instead of breaching. Set through ConfigureAsPredator.
    private bool isHungryPredator;
    private int predatorHungerTarget;
    private float predatorWoundedFraction;
    private float predatorGiveUpSeconds;
    private bool predatorWounded;
    private bool predatorLeaving;
    private float predatorNoPreyTimer;

    private List<Vector3> attackPath = new();
    private int attackPathIndex = 0;
    private float attackPathRefreshTimer = 0f;
    private const float AttackPathRefreshInterval = 0.4f;

    [Header("Chase Leash")]
    [Tooltip("How far (world units, roughly tiles) this monster pursues a target from " +
             "the spot where the chase began before giving up and returning to its " +
             "duties. 0 = no leash.")]
    [SerializeField, Min(0f)] private float chaseLeashRadius = 10f;

    private Vector3 chaseAnchor;
    private bool chaseAnchored;

    // Patrol/Wander pathfinding (DAY 31 PART 3 CLOSE-OUT)
    private List<Vector3> patrolPath = new();
    private int patrolPathIndex = 0;
    private Vector3Int patrolPathTargetCell;
    private float nextNoPathBark;
    private List<Vector3> wanderPath = new();
    private int wanderPathIndex = 0;
    private Vector3Int wanderPathTargetCell;

    public bool IsBoss => bossDefinition != null;
    public bool IsWild => wildChamberId >= 0 || isInvader;

    /// <summary>Specifically an INVADER -- a beast marching on the core -- and
    /// not merely wild. IsWild is true for wild-chamber dwellers too, and those
    /// wander their own chamber and threaten nothing, so anything that cares
    /// about the core being charged has to ask this instead.</summary>
    public bool IsInvader => isInvader;
    public bool IsVeteran => isVeteran;
    public int PatrolIndex => patrolIndex;
    public int WildChamberId => wildChamberId;
    public event System.Action<DungeonMonster> OnDied;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Global balance multipliers, before currentHP is seeded from maxHP.
        maxHP *= CombatBalance.MonsterHp;
        attackDamage *= CombatBalance.MonsterDamage;

        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void Start()
    {
        spawnPosition = transform.position;
        animDriver = GetComponent<EntityAnimationDriver>();
        telegraph = GetComponent<AttackTelegraph>();
        if (currentFloor == null) currentFloor = GetComponentInParent<FloorRoot>();
        if (currentFloor == null)
            Debug.LogWarning("[DungeonMonster] No FloorRoot in parent.");
        else
            currentFloor.Entities?.Register(this);

        globalDamageMultiplier = sharedGlobalDamageMultiplier;
        ResolveEffectiveRegen();
        RegisterNovelty();
        PickWanderTarget();

        var ndef = IsWild ? wildDefinition : spawner?.Definition;
        isNecromancer = ndef != null && ndef.isNecromancer;

        // A ranged attacker must sense at least as far as it can shoot -- the caster
        // prefabs ship with the base detectionRange, so clamp it up here rather than
        // hand-editing every prefab.
        if (ndef != null && ndef.firesProjectile)
            detectionRange = Mathf.Max(detectionRange, attackRange + 0.5f);

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);
            statusBars.ConfigureResourceBars(maxStamina > 0f, false);
            if (maxStamina > 0f) statusBars.SetStamina(currentStamina, maxStamina);
            RefreshNameplate();
        }
    }

    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    public void InitialiseWild(int chamberId, FloorRoot floor, List<Vector3Int> chamberCells,
                               MonsterDefinition def)
    {
        wildChamberId = chamberId;
        currentFloor = floor;
        wildChamberCells = chamberCells != null
            ? new List<Vector3Int>(chamberCells)
            : new List<Vector3Int>();
        wildDefinition = def;
    }

    /// <summary>Sets this monster up as an invader: a chamber-free wild creature that
    /// paths to the core, fights the dungeon's monsters on the way, and breaches on
    /// arrival. Call immediately after Instantiate, before the monster's Start runs.</summary>
    public void InitialiseInvader(FloorRoot floor, MonsterDefinition def)
    {
        currentFloor = floor;
        wildDefinition = def;
        isInvader = true;
    }

    /// <summary>Marks an invader as the endgame climax beast: on breaching the core it is
    /// flung back to the entrance to charge again (never leaving, never splitting) rather
    /// than despawning. Call after InitialiseInvader.</summary>
    public void ConfigureAsClimaxInvader() => isClimaxInvader = true;

    /// <summary>Turns this invader into a hungry predator: it hunts until sated (kills)
    /// or gives up (no prey in range), then walks back to the entrance and leaves rather
    /// than breaching. Ever dropping below woundedFraction marks it wounded.</summary>
    public void ConfigureAsPredator(int hungerTarget, float woundedFraction, float giveUpSeconds)
    {
        isHungryPredator = true;
        predatorHungerTarget = Mathf.Max(1, hungerTarget);
        predatorWoundedFraction = Mathf.Clamp01(woundedFraction);
        predatorGiveUpSeconds = Mathf.Max(1f, giveUpSeconds);
    }

    /// <summary>Multiply this monster's HP, damage, and scale (wild-event escalation).
    /// Sets currentHP to the new max - call on a fresh spawn, before any HP restore.</summary>
    public void ApplyStatScale(float hpMult, float dmgMult, float scaleMult)
    {
        maxHP *= Mathf.Max(0.01f, hpMult);
        currentHP = maxHP;
        attackDamage *= Mathf.Max(0.01f, dmgMult);
        transform.localScale *= Mathf.Max(0.01f, scaleMult);
    }

    /// <summary>Restore the predator's wounded / leaving flags after a save load.</summary>
    public void RestorePredatorState(bool wounded, bool leaving)
    {
        predatorWounded = wounded;
        predatorLeaving = leaving;
    }

    /// <summary> Restore HP after wild monster respawn from save.</summary>
    public void SetCurrentHP(float hp)
    {
        currentHP = Mathf.Clamp(hp, 0f, maxHP);
        statusBars?.SetHP(currentHP, maxHP);
    }

    public void SetPatrolIndex(int index)
    {
        patrolIndex = Mathf.Max(0, index);
    }

    public void SetMonsterXP(float xp)
    {
        monsterXP = Mathf.Max(0f, xp);
    }

    /// <summary>Restores lifetime kills after save load.</summary>
    public void SetMonsterKills(int kills)
    {
        killCount = Mathf.Max(0, kills);
    }

    /// <summary>Adds HP (clamped to maxHP) with floating heal numbers. Used by
    /// room effects (Lair). No post-damage cooldown — a Lair always mends.</summary>
    public void SetRoomDamageMultiplier(float m) => roomDamageMultiplier = Mathf.Max(0f, m);

    /// <summary>The Pressed rule (CrowdingController): too many of the core's
    /// creatures shoulder-to-shoulder in a corridor fight poorly. Applies a
    /// damage-dealt multiplier and a temporary shade; clearing restores the
    /// prior colour only if nothing else recoloured the sprite meanwhile.</summary>
    public void SetCrowdPenalty(bool pressed, float damageMultiplier, Color shade)
    {
        if (pressed == isPressed) return;
        isPressed = pressed;
        crowdDamageMultiplier = pressed ? Mathf.Clamp(damageMultiplier, 0.1f, 1f) : 1f;

        var pressSr = GetComponentInChildren<SpriteRenderer>();
        if (pressSr == null) return;
        if (pressed)
        {
            preCrowdColour = pressSr.color;
            pressSr.color = preCrowdColour * shade;
        }
        else if (ColoursClose(pressSr.color, preCrowdColour * shade))
        {
            pressSr.color = preCrowdColour;
        }
    }

    /// <summary>True while the Pressed corridor penalty applies.</summary>
    public bool IsPressed => isPressed;

    private static bool ColoursClose(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    /// <summary>Census pushes the current trophy damage multiplier to every live monster.
    /// Cached so monsters spawned later adopt it in Start.</summary>
    public static void PushGlobalDamageMultiplier(float m)
    {
        sharedGlobalDamageMultiplier = Mathf.Max(0f, m);
        var all = FindObjectsByType<DungeonMonster>();
        for (int i = 0; i < all.Length; i++) all[i].globalDamageMultiplier = sharedGlobalDamageMultiplier;
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHP >= maxHP) return;
        float newHP = Mathf.Min(maxHP, currentHP + amount);
        float actuallyHealed = newHP - currentHP;
        currentHP = newHP;
        statusBars?.SetHP(currentHP, maxHP);

        pendingHealDisplay += actuallyHealed;
        if (pendingHealDisplay >= HEAL_DISPLAY_THRESHOLD)
        {
            DamageNumberSpawner.Spawn(pendingHealDisplay, transform.position,
                FloatingDamageNumber.DamageType.Heal);
            pendingHealDisplay = 0f;
        }
    }

    /// <summary>Adds XP toward Veteran (room effects — Training). Promotion gates
    /// (boss / wild / already-veteran) are handled by TryPromoteToVeteran.</summary>
    public void AddXP(float amount)
    {
        if (amount <= 0f) return;
        monsterXP += amount;
        TryPromoteToVeteran();
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — Re-apply veteran buffs after save load.
    /// Bypasses the threshold check (the monster was already veteran when saved)
    /// and skips the proportional-HP scaling because the loaded currentHP is
    /// already in veteran-space.
    /// </summary>
    public void SetVeteran(bool veteran)
    {
        if (!veteran) return;
        if (isVeteran) return;
        if (IsBoss) return;
        if (IsWild) return;

        isVeteran = true;
        maxHP *= veteranHpMultiplier;
        attackDamage *= veteranDamageMultiplier;
        xpPerKill *= veteranXpRewardMultiplier;
        ApplyVeteranVisuals();
    }

    public void ApplyBossModifiers(BossVariantDefinition def)
    {
        if (def == null) return;
        bossDefinition = def;
        ApplyStatMultipliers(def.hpMultiplier, def.damageMultiplier,
            def.xpRewardMultiplier, def.scaleMultiplier, def.tint);
    }

    /// <summary>Sub-boss scaling — the same shared stat bump as a boss, milder, and with
    /// no boss label / alert (a sub-boss is just a tougher standard monster).</summary>
    public void ApplySubBossModifiers(SubBossVariantDefinition def)
    {
        if (def == null) return;
        ApplyStatMultipliers(def.hpMultiplier, def.damageMultiplier,
            def.xpRewardMultiplier, def.scaleMultiplier, def.tint);
    }

    /// <summary>Rank promotion applied by the spawner: fresh spawns come in with
    /// prior None; a live upgrade applies only the ratio between the two ranks so
    /// multipliers never stack twice. Promotion heals to the new maximum.</summary>
    public void ApplyPromotion(PromotionRank prior, PromotionRank target,
                               PromotionTemplate t, string title)
    {
        if (t == null || target == PromotionRank.None) return;
        promotedRank = target;
        promotedTitle = target == PromotionRank.Boss ? title : null;
        ApplyStatMultipliers(
            t.HpMult(target) / t.HpMult(prior),
            t.DamageMult(target) / t.DamageMult(prior),
            t.XpMult(target) / t.XpMult(prior),
            t.ScaleMult(target) / t.ScaleMult(prior),
            t.Tint(target));
        RefreshNameplate();
    }

    /// <summary>Shared stat scaler used by both boss and sub-boss variants.</summary>
    private void ApplyStatMultipliers(float hpMult, float dmgMult, float xpMult, float scaleMult, Color tint)
    {
        maxHP *= hpMult;
        currentHP = maxHP;
        attackDamage *= dmgMult;
        xpPerKill *= xpMult;
        transform.localScale *= scaleMult;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = tint;
    }

    private void ResolveEffectiveRegen()
    {
        // DAY 31 — Wild monsters now have a direct definition back-reference (wildDefinition);
        // player monsters use spawner.Definition. Old code returned 0 for all wild monsters
        // because spawner was null — that limitation is gone.
        MonsterDefinition def = IsWild ? wildDefinition : spawner?.Definition;
        if (def == null) { effectiveRegenPerSecond = 0f; effectiveRegenCooldown = 5f; return; }

        float baseRegen = def.passiveRegenPerSecond;
        if (IsWild) baseRegen *= def.wildRegenMultiplier;
        if (bossDefinition != null) baseRegen *= bossDefinition.hpMultiplier;

        effectiveRegenPerSecond = baseRegen;
        effectiveRegenCooldown = def.regenCooldown;

        // — stamina pool from the definition (0 = tireless).
        maxStamina = def.maxStamina;
        attackStaminaCost = def.attackStaminaCost;
        currentStamina = maxStamina;

        targetPriority = def.targetPriority;
    }

    // ── Stamina ──────────────────────────────────────────

    private void TickStaminaRegen()
    {
        float r = state == MonsterState.Attack ? staminaRegenInCombat : staminaRegenOutOfCombat;
        if (r <= 0f || currentStamina >= maxStamina) return;
        currentStamina = Mathf.Min(maxStamina, currentStamina + r * Time.deltaTime);
        statusBars?.SetStamina(currentStamina, maxStamina);
    }

    private bool SpendAttackStamina()
    {
        if (maxStamina <= 0f || attackStaminaCost <= 0f) return true;
        if (currentStamina < attackStaminaCost) return false;
        currentStamina -= attackStaminaCost;
        statusBars?.SetStamina(currentStamina, maxStamina);
        return true;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        if (knockbackRemaining > 0f) { KnockbackStep(); return; }

        UpdateTerrainSpeedMultiplier();
        TickSlow();
        CheckTrapStep();

        if (target != null && !target.IsAlive) target = null;
        // Release a victim the moment a capture-trap snares them: a pinned adventurer is
        // neutralised already, and finishing them off would waste the capture.
        if (target is DungeonAdventurer pinnedVictim && pinnedVictim.IsPinned) target = null;
        if (IsRegenState(state)) TickRegen();
        if (maxStamina > 0f) TickStaminaRegen();

        if (lifetimeRemaining > 0f)
        {
            lifetimeRemaining -= Time.deltaTime;
            if (lifetimeRemaining <= 0f) { Die(); return; }
        }

        if (isNecromancer && TickNecromancer()) return;   // channeling a raise — hold position

        // DAY 31 PART 3D — re-resolve desired state from orders each frame.
        // Attack state owns transitions out of itself (target-death path).
        if (state != MonsterState.Attack)
        {
            var desired = DetermineDesiredState();
            if (state != desired) EnterState(desired);
        }

        switch (state)
        {
            case MonsterState.Wander:
                ScanForHostiles();
                Wander();
                break;
            case MonsterState.Patrol:
                ScanForHostiles();
                TickPatrol();
                break;
            case MonsterState.Idle:
                ScanForHostiles();
                // Hold position. No movement.
                break;
            case MonsterState.Attack:
                if (target == null)
                {
                    // Resume orders after combat.
                    EnterState(DetermineDesiredState());
                }
                else
                {
                    AttackTarget();
                }
                break;
            case MonsterState.DefendCore:
                TickDefendCore();
                break;
            case MonsterState.Invade:
                TickInvade();
                break;
        }
    }

    private static bool IsRegenState(MonsterState s)
        => s == MonsterState.Wander || s == MonsterState.Patrol || s == MonsterState.Idle;

    // ── State resolution (DAY 31 PART 3D) ─────────────────────────

    private MonsterState DetermineDesiredState()
    {
        // An invader ignores everything and drives for the core.
        if (isInvader) return MonsterState.Invade;

        // Wild monsters always wander (Part 2 behavior preserved).
        if (IsWild) return MonsterState.Wander;
        if (spawner == null) return MonsterState.Wander;

        // Attack-Here takes precedence: it's the player's explicit will and outranks
        // even DefendCore.
        if (spawner.HasAttackTarget) return MonsterState.Patrol;

        if (spawner.AllowDefendCore
                    && CoreThreatMonitor.Instance != null
                    && CoreThreatMonitor.Instance.IsCoreThreatened
                    && IsOnCoreFloor())
        {
            return MonsterState.DefendCore;
        }

        if (spawner.OrderMode == SpawnerOrderMode.Patrol)
        {
            int count = spawner.PatrolWaypoints.Count;
            if (count == 0) return MonsterState.Wander;
            if (!spawner.PatrolLoop && patrolIndex >= count) return MonsterState.Idle;
            return MonsterState.Patrol;
        }
        return MonsterState.Wander;
    }

    private bool IsOnCoreFloor()
    {
        if (currentFloor == null || FloorManager.Instance == null) return false;
        return currentFloor.FloorIndex == FloorManager.Instance.CoreFloorIndex;
    }

    private void EnterState(MonsterState newState)
    {
        if (newState == MonsterState.Patrol && spawner != null
            && spawner.PatrolWaypoints.Count > 0
            && patrolIndex >= spawner.PatrolWaypoints.Count)
        {
            Debug.Log($"[DungeonMonster] EnterState(Patrol): wrapping patrolIndex {patrolIndex} → 0 (was past end).");
            patrolIndex = 0;
        }

        state = newState;
        if (newState == MonsterState.Wander) { wanderPath.Clear(); PickWanderTarget(); }
        if (newState == MonsterState.Patrol) { patrolPath.Clear(); UpdatePatrolTarget(); }
        if (newState == MonsterState.Invade) { invadePath.Clear(); invadePathIndex = 0; invadePathRefreshTimer = 0f; }
    }

    // ── Patrol ───────────────────────────────────

    private void UpdatePatrolTarget()
    {
        if (spawner == null) return;
        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        Vector3Int cell;
        if (spawner.HasAttackTarget)
        {
            cell = spawner.AttackTargetCell;
        }
        else if (spawner.OrderMode == SpawnerOrderMode.Patrol && spawner.PatrolWaypoints.Count > 0)
        {
            int idx = Mathf.Clamp(patrolIndex, 0, spawner.PatrolWaypoints.Count - 1);
            cell = spawner.PatrolWaypoints[idx];
        }
        else return;

        patrolMoveTarget = influence.CellToWorld(cell);
    }

    private void TickPatrol()
    {
        if (spawner == null) { state = MonsterState.Wander; return; }

        // DAY 31 — Defensive: if patrolIndex is out of range while Loop is on,
        // reset to 0. Catches edge cases where state arrives at Patrol without
        // going through EnterState (e.g. external state mutation, save load).
        int count = spawner.PatrolWaypoints.Count;
        if (count > 0 && patrolIndex >= count && spawner.PatrolLoop)
            patrolIndex = 0;

        UpdatePatrolTarget();

        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        // Arrival check first — if we're at the waypoint, fire arrival and clear path.
        if (Vector2.Distance(transform.position, patrolMoveTarget) < waypointArrivalDistance)
        {
            patrolPath.Clear();
            OnWaypointReached();
            return;
        }

        // Recompute path when target cell changed or path is empty.
        Vector3Int targetCell = influence.WorldToCell(patrolMoveTarget);
        if (patrolPath.Count == 0 || targetCell != patrolPathTargetCell)
        {
            patrolPath = DungeonPathfinder.FindPath(currentFloor, transform.position, patrolMoveTarget);
            patrolPathIndex = 0;
            patrolPathTargetCell = targetCell;
        }

        // Follow path. If the pathfinder found nothing, the monster sits --
        // the player's waypoint is unreachable. Say so above its head rather
        // than letting the order fail silently, throttled so a standing
        // blockage does not spam.
        if (patrolPathIndex >= patrolPath.Count)
        {
            if (patrolPath.Count == 0 && Time.time >= nextNoPathBark)
            {
                nextNoPathBark = Time.time + 5f;
                BarkSpawner.Spawn(transform.position + Vector3.up * 0.8f,
                    "no path", new Color(0.95f, 0.45f, 0.4f));
            }
            return;
        }

        Vector3 stepTarget = patrolPath[patrolPathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            patrolPathIndex++;
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — Behavior while DefendCore is active.
    /// Uses DungeonPathfinder to route around walls; recomputes the path every
    /// DefendCorePathRefreshInterval seconds (or sooner if exhausted) so the
    /// monster tracks a moving threat without per-frame pathfinding cost.
    /// </summary>
    private void TickDefendCore()
    {
        ScanForHostiles();
        if (state == MonsterState.Attack) { defendCorePath.Clear(); return; }

        Vector3 destination;
        var monitorThreat = CoreThreatMonitor.Instance?.NearestThreat;
        if (monitorThreat != null)
            destination = monitorThreat.transform.position;
        else if (DungeonCore.Instance != null)
            destination = DungeonCore.Instance.transform.position;
        else
            return;

        defendCorePathRefreshTimer -= Time.deltaTime;
        bool needsRefresh = defendCorePath.Count == 0
                         || defendCorePathIndex >= defendCorePath.Count
                         || defendCorePathRefreshTimer <= 0f;
        if (needsRefresh)
        {
            defendCorePath = DungeonPathfinder.FindPath(currentFloor, transform.position, destination);
            defendCorePathIndex = 0;
            defendCorePathRefreshTimer = DefendCorePathRefreshInterval;
        }

        if (defendCorePath.Count == 0) return;
        if (defendCorePathIndex >= defendCorePath.Count) return;

        Vector3 stepTarget = defendCorePath[defendCorePathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            defendCorePathIndex++;
    }

    // Invader behaviour: always drive for the core, breach on arrival (two-strike,
    // same as an adventurer), then depart. ScanForHostiles diverts to Attack when a
    // dungeon monster is in range; combat resumes here after the kill.
    // Invader behaviour: always drive for the core, breach on arrival (two-strike,
    // same as an adventurer), then depart. ScanForHostiles diverts to Attack when a
    // dungeon monster is in range; combat resumes here after the kill.
    // Invader behaviour: drive for the core, breach on arrival (two-strike, same as an
    // adventurer), then depart. A hungry predator instead hunts until sated or starved
    // and then leaves via the entrance without ever breaching. ScanForHostiles diverts
    // to Attack when prey is in range; the hunt resumes here after the kill.
    private void FlingToEntrance()
    {
        if (DungeonEntrance.Instance != null)
            transform.position = DungeonEntrance.Instance.SpawnPosition;
        invadePath.Clear();
        invadePathIndex = 0;
        invadePathRefreshTimer = 0f;
    }

    private void TickInvade()
    {
        if (isHungryPredator && predatorLeaving) { TickPredatorLeave(); return; }

        ScanForHostiles();
        if (state == MonsterState.Attack)
        {
            invadePath.Clear();
            predatorNoPreyTimer = 0f;   // feeding - reset the starve clock
            return;
        }

        DungeonMonster quarry = null;
        if (isHungryPredator)
        {
            if (killCount >= predatorHungerTarget) { BeginPredatorLeave(sated: true); return; }

            // The beast came to eat, not to break rock. It hunts the nearest
            // dungeon creature anywhere on the floor and only gives up when the
            // halls are genuinely empty. The old clock ran whenever it was not
            // mid-swing, so a long walk across a large dungeon starved it out
            // before it ever reached anything.
            quarry = NearestPrey();
            if (quarry != null) predatorNoPreyTimer = 0f;
            else
            {
                predatorNoPreyTimer += Time.deltaTime;
                if (predatorNoPreyTimer >= predatorGiveUpSeconds) { BeginPredatorLeave(sated: false); return; }
            }
        }

        // A hunting predator walks toward its quarry; everything else drives the core.
        if (isHungryPredator && quarry != null)
        {
            TickPredatorApproach(quarry);
            return;
        }

        if (DungeonCore.Instance == null) return;
        Vector3 corePos = DungeonCore.Instance.transform.position;

        if (Vector2.Distance(transform.position, corePos) <= invaderBreachDistance)
        {
            if (isHungryPredator) { BeginPredatorLeave(sated: false); return; }   // can't eat a rock
            DungeonCore.Instance.DestroyCore();
            if (isClimaxInvader)
            {
                // The dying core's backlash hurls the beast back to the dungeon mouth; it
                // never leaves and never splits - it simply charges again.
                FlingToEntrance();
                ScreenFlash.Instance?.Flash(new Color(0.75f, 0.05f, 0.05f, 1f), 0.45f);
                return;
            }
            DespawnSilently();
            return;
        }

        invadePathRefreshTimer -= Time.deltaTime;
        bool needsRefresh = invadePath.Count == 0
                         || invadePathIndex >= invadePath.Count
                         || invadePathRefreshTimer <= 0f;
        if (needsRefresh)
        {
            invadePath = DungeonPathfinder.FindPath(currentFloor, transform.position, corePos);
            invadePathIndex = 0;
            invadePathRefreshTimer = DefendCorePathRefreshInterval;
        }

        if (invadePath.Count == 0 || invadePathIndex >= invadePath.Count) return;

        Vector3 stepTarget = invadePath[invadePathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            invadePathIndex++;
    }

    /// <summary>Nearest of the dungeon's OWN creatures on this floor. IsWild covers
    /// both invaders and chamber wilds, so the beast never hunts itself, another
    /// invader, or the neutral cave life. Unbounded range: it hunts the whole floor,
    /// which is the point of it.</summary>
    private DungeonMonster NearestPrey()
    {
        if (currentFloor?.Entities == null) return null;
        return currentFloor.Entities.Nearest<DungeonMonster>(
            transform.position, float.MaxValue,
            m => m != null && m != this && !m.IsWild);
    }

    /// <summary>Path toward prey. Combat is entered by ScanForHostiles once the
    /// quarry is inside detection range, exactly as before.</summary>
    private void TickPredatorApproach(DungeonMonster quarry)
    {
        Vector3 preyPos = quarry.transform.position;

        invadePathRefreshTimer -= Time.deltaTime;
        bool needsRefresh = invadePath.Count == 0
                         || invadePathIndex >= invadePath.Count
                         || invadePathRefreshTimer <= 0f;
        if (needsRefresh)
        {
            invadePath = DungeonPathfinder.FindPath(currentFloor, transform.position, preyPos);
            invadePathIndex = 0;
            invadePathRefreshTimer = DefendCorePathRefreshInterval;
        }

        if (invadePath.Count == 0 || invadePathIndex >= invadePath.Count) return;

        Vector3 stepTarget = invadePath[invadePathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            invadePathIndex++;
    }

    /// <summary>Sated or starved: abandon the hunt and turn for the exit.</summary>
    private void BeginPredatorLeave(bool sated)
    {
        predatorLeaving = true;
        target = null;
        invadePath.Clear();
        invadePathIndex = 0;
        invadePathRefreshTimer = 0f;
        WildMonsterEvent.Instance?.OnPredatorBeganLeaving(sated, predatorWounded);
    }

    /// <summary>Walk back to the entrance and vanish. The player can still chase and
    /// wound it here; dropping it below the wounded line prevents its return.</summary>
    private void TickPredatorLeave()
    {
        Vector3 exitPos = DungeonEntrance.Instance != null
            ? DungeonEntrance.Instance.SpawnPosition : transform.position;

        if (Vector2.Distance(transform.position, exitPos) <= invaderBreachDistance)
        {
            WildMonsterEvent.Instance?.OnPredatorDeparted(predatorWounded);
            DespawnSilently();
            return;
        }

        invadePathRefreshTimer -= Time.deltaTime;
        bool needsRefresh = invadePath.Count == 0
                         || invadePathIndex >= invadePath.Count
                         || invadePathRefreshTimer <= 0f;
        if (needsRefresh)
        {
            invadePath = DungeonPathfinder.FindPath(currentFloor, transform.position, exitPos);
            invadePathIndex = 0;
            invadePathRefreshTimer = DefendCorePathRefreshInterval;
        }

        if (invadePath.Count == 0 || invadePathIndex >= invadePath.Count) return;

        Vector3 stepTarget = invadePath[invadePathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            invadePathIndex++;
    }

    private void OnWaypointReached()
    {
        if (spawner == null) return;

        // Attack-Here completion clears the transient order.
        if (spawner.HasAttackTarget)
        {
            spawner.ClearAttackTarget();
            return;
        }

        if (spawner.OrderMode != SpawnerOrderMode.Patrol) return;
        int count = spawner.PatrolWaypoints.Count;
        if (count == 0) return;

        if (spawner.PatrolLoop)
            patrolIndex = (patrolIndex + 1) % count;
        else
            patrolIndex++;  // may go to count → Idle next frame
    }

    // ── Regen / Slow / Trap-step ──────────────────────────────────

    private void TickRegen()
    {
        if (effectiveRegenPerSecond <= 0f) return;
        if (currentHP >= maxHP) return;
        if (Time.time - lastDamageTime < effectiveRegenCooldown) return;

        float healThisFrame = effectiveRegenPerSecond * Time.deltaTime;
        float newHP = Mathf.Min(maxHP, currentHP + healThisFrame);
        float actuallyHealed = newHP - currentHP;
        currentHP = newHP;
        statusBars?.SetHP(currentHP, maxHP);

        pendingHealDisplay += actuallyHealed;
        if (pendingHealDisplay >= HEAL_DISPLAY_THRESHOLD)
        {
            DamageNumberSpawner.Spawn(pendingHealDisplay, transform.position,
                FloatingDamageNumber.DamageType.Heal);
            pendingHealDisplay = 0f;
        }
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (duration <= 0f) return;
        multiplier = Mathf.Clamp01(multiplier);
        slowMultiplier = Mathf.Min(slowMultiplier, multiplier);
        slowTimer = duration;
    }

    private void TickSlow()
    {
        if (slowTimer <= 0f) return;
        slowTimer -= Time.deltaTime;
        if (slowTimer <= 0f) { slowTimer = 0f; slowMultiplier = 1f; }
    }

    private void CheckTrapStep()
    {
        if (!IsWild) return;
        if (currentFloor == null) return;
        var influence = currentFloor.TileInfluence;
        var trapReg = currentFloor.TrapRegistry;
        if (influence == null || trapReg == null) return;
        Vector3Int cell = influence.WorldToCell(transform.position);
        if (cell == lastTrapCheckCell) return;
        lastTrapCheckCell = cell;
        var trap = trapReg.GetTrapAt(cell);
        if (trap != null) trap.OnMonsterEntered(this);
    }

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;

        // DAY 31 — Aquatic check now works for both player and wild monsters.
        MonsterDefinition def = IsWild ? wildDefinition : spawner?.Definition;
        if (def != null && def.isAquatic) return;

        if (currentFloor == null) return;
        var features = currentFloor.FeatureGenerator;
        var influence = currentFloor.TileInfluence;
        if (features == null || influence == null) return;
        Vector3Int cell = influence.WorldToCell(transform.position);
        if (features.IsRiver(cell))
            terrainSpeedMultiplier = features.FordingSpeedMultiplier;
    }

    private float EffectiveMoveSpeed =>
        moveSpeed * terrainSpeedMultiplier * slowMultiplier * (IsWild ? 1f : MonsterMastery.SpeedMultiplier)
        * (boons != null ? boons.SpeedMultiplier : 1f);

    // -- Core-spell boons (canon 38) ------------------------------
    // Null until a working is cast on this one, and read as 1 while null, so a
    // dungeon that never casts pays nothing for this.
    private MonsterBoons boons;

    /// <summary>The boon holder, created on first use. Called by SpellCaster only.</summary>
    public MonsterBoons EnsureBoons()
    {
        // Written out rather than with ?? -- Unity's fake-null makes the
        // null-coalescing operator unreliable on destroyed components.
        if (boons == null) boons = GetComponent<MonsterBoons>();
        if (boons == null) boons = gameObject.AddComponent<MonsterBoons>();
        return boons;
    }

    /// <summary>True while any core-spell boon is running. For diagnostics.</summary>
    public bool HasActiveBoon => boons != null && boons.AnyActive;

    // ── Wander ────────────────────────────────────────────────────

    /// <summary>Point the wander state orbits: the player-set post when one
    /// exists, else the spawn position. Wild monsters never read this.</summary>
    private Vector3 WanderAnchor
    {
        get
        {
            if (spawner != null && spawner.HasPost && currentFloor?.TileInfluence != null)
                return currentFloor.TileInfluence.CellToWorld(spawner.PostCell);
            return spawnPosition;
        }
    }

    /// <summary>Called by the spawner when its post changes so a wandering
    /// monster re-anchors immediately instead of finishing its old walk.</summary>
    public void NotifyPostChanged()
    {
        if (state != MonsterState.Wander) return;
        wanderPath.Clear();
        wanderWaiting = false;
        PickWanderTarget();
    }

    private void Wander()
    {
        if (wanderWaiting)
        {
            wanderWaitTimer -= Time.deltaTime;
            if (wanderWaitTimer <= 0f)
            {
                wanderWaiting = false;
                PickWanderTarget();
                wanderPath.Clear();  // force re-path on next tick
            }
            return;
        }

        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        // Arrival check — set the wait timer and clear the path.
        // Use waypointArrivalDistance (same threshold as the per-waypoint
        // increment below) to close the precision wedge between the two
        // checks. Previously this was a hard-coded 0.1f, which created a
        // dead-zone (position in 0.1–0.25 range from wanderTarget after the
        // final waypoint increment) where neither arrival nor movement
        // triggered — the monster froze.
        if (Vector2.Distance(transform.position, wanderTarget) < waypointArrivalDistance)
        {
            wanderPath.Clear();
            wanderWaiting = true;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }

        // Recompute path when target cell changed or path is empty.
        Vector3Int targetCell = influence.WorldToCell(wanderTarget);
        if (wanderPath.Count == 0 || targetCell != wanderPathTargetCell)
        {
            wanderPath = DungeonPathfinder.FindPath(currentFloor, transform.position, wanderTarget);
            wanderPathIndex = 0;
            wanderPathTargetCell = targetCell;
        }

        // Pathfinder returned nothing — target unreachable, pick a new one.
        if (wanderPath.Count == 0)
        {
            PickWanderTarget();
            return;
        }

        // Path exhausted (walked every waypoint) but arrival didn't fire above.
        // Treat as arrival — drives the state into wait so the next pick happens
        // on schedule rather than freezing.
        if (wanderPathIndex >= wanderPath.Count)
        {
            wanderPath.Clear();
            wanderWaiting = true;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }

        Vector3 stepTarget = wanderPath[wanderPathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            wanderPathIndex++;
    }

    private void PickWanderTarget()
    {
        if (IsWild) { PickWildWanderTarget(); return; }
        var influence = currentFloor?.TileInfluence;
        Vector3 anchor = WanderAnchor;
        if (influence == null) { wanderTarget = anchor; return; }

        // Enumerate mined cells within wanderRadius of spawn. Builds a complete
        // candidate list, then picks one. Robust against sparse mined areas where
        // random-sample-and-reject would fail (e.g. monster placed near the
        // claimed-not-mined edge of influence, or in the Phase 4 random starter
        // where 30% of nearby cells start as claimed-stone).
        Vector3Int spawnCell = influence.WorldToCell(anchor);
        int cellRadius = Mathf.CeilToInt(wanderRadius);
        float radiusSqr = wanderRadius * wanderRadius;

        // Only cells reachable FROM WHERE THE MONSTER STANDS are valid targets;
        // otherwise it picks a mined cell across a river or in a disconnected
        // pocket, the path comes back empty, and it stalls re-rolling. The flood
        // crosses rivers, so a genuinely fordable cell still qualifies.
        var reachable = DungeonPathfinder.ReachableCells(currentFloor, transform.position);

        var candidates = new List<Vector3Int>();
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        {
            for (int dy = -cellRadius; dy <= cellRadius; dy++)
            {
                Vector3Int cell = spawnCell + new Vector3Int(dx, dy, 0);
                if (!influence.IsTileMined(cell) || influence.IsUnderOverhang(cell)) continue;
                if (!reachable.Contains(cell)) continue;   // must be reachable from here, not just mined

                // Circular not square — use squared distance for cheap check.
                Vector3 cellWorld = influence.CellToWorld(cell);
                float sx = cellWorld.x - anchor.x;
                float sy = cellWorld.y - anchor.y;
                if (sx * sx + sy * sy > radiusSqr) continue;

                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            // Isolated monster — nothing reachable. Hold at the anchor.
            wanderTarget = anchor;
            return;
        }

        var pick = candidates[Random.Range(0, candidates.Count)];
        wanderTarget = influence.CellToWorld(pick);
    }

    private void PickWildWanderTarget()
    {
        var influence = currentFloor?.TileInfluence;
        if (influence == null || wildChamberCells == null || wildChamberCells.Count == 0)
        { wanderTarget = spawnPosition; return; }

        // Wild monsters branch here BEFORE the tame reachability filter, so they
        // need their own: a cell adjacent to some far lobe of the chamber (or
        // across a river) is not somewhere this rat can actually walk. Without
        // this it targets the unreachable, the path returns empty, and the horde
        // stalls at the water re-rolling forever.
        var reachable = DungeonPathfinder.ReachableCells(currentFloor, transform.position);

        bool tryOutward = Random.value < wildAggroOutwardChance;
        if (tryOutward)
        {
            var adjacentOwned = new List<Vector3Int>();
            var seen = new HashSet<Vector3Int>();
            foreach (var cell in wildChamberCells)
            {
                TryAddAdjacentOwned(cell + Vector3Int.up, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.down, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.left, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.right, influence, seen, adjacentOwned);
            }
            adjacentOwned.RemoveAll(c => !reachable.Contains(c));
            if (adjacentOwned.Count > 0)
            {
                var pick = adjacentOwned[Random.Range(0, adjacentOwned.Count)];
                wanderTarget = influence.CellToWorld(pick);
                return;
            }
        }

        // Chamber fallback, also reachability-filtered: a chamber split by a
        // river has lobes this monster cannot walk to. The flood always contains
        // the cell the monster stands on, so this list is never empty in practice.
        var inReach = wildChamberCells.FindAll(c => reachable.Contains(c));
        var pool = inReach.Count > 0 ? inReach : wildChamberCells;
        var chamberPick = pool[Random.Range(0, pool.Count)];
        wanderTarget = influence.CellToWorld(chamberPick);
    }

    private static void TryAddAdjacentOwned(Vector3Int candidate, TileInfluenceManager influence,
        HashSet<Vector3Int> seen, List<Vector3Int> list)
    {
        if (!seen.Add(candidate)) return;
        if (influence.IsTileMined(candidate)) list.Add(candidate);
    }

    // ── Combat ────────────────────────────────────────────────────

    // ── Aggression ───────────────────────────────────────

    /// <summary>Resolved stance: individual override > wild default (Aggressive) > global toggle.</summary>
    private MonsterAggression EffectiveAggression
    {
        get
        {
            // Player-set per-monster stance lives on the spawner (survives respawns).
            if (spawner != null && spawner.TryGetAggressionOverride(out var spawnerStance))
                return spawnerStance;
            if (overrideGlobalAggression) return aggressionOverride;   // prefab default / wild fallback
            if (IsWild) return MonsterAggression.Aggressive;
            return MonsterAggressionSettings.Global;
        }
    }

    /// <summary>Runtime per-monster override (for a future select-and-set-stance UI).</summary>
    public void SetAggressionOverride(MonsterAggression stance)
    {
        overrideGlobalAggression = true;
        aggressionOverride = stance;
    }

    public void ClearAggressionOverride() => overrideGlobalAggression = false;

    // ScanForHostiles runs every frame from several states. Its filter lambdas
    // used to CAPTURE locals (sparePilgrims, this), so C# allocated a fresh
    // closure per call -- three per scan, per monster, per frame. That churn was
    // the top GC contributor in the profile. Cache the delegates once and drive
    // them from these fields, which the scan sets before each query.
    private bool _scanSparePilgrims;
    private System.Predicate<DungeonAdventurer> _taunterPred;
    private System.Predicate<DungeonAdventurer> _advPred;
    private System.Predicate<DungeonMonster> _hostileMonsterPred;

    private void EnsureScanPredicates()
    {
        if (_taunterPred != null) return;
        _taunterPred = a => !a.IsPinned && a.IsTaunting && (!_scanSparePilgrims || (a.Intent != PartyIntent.Pilgrim && a.Intent != PartyIntent.GiftGiver));
        _advPred = a => !a.IsPinned && (!_scanSparePilgrims || (a.Intent != PartyIntent.Pilgrim && a.Intent != PartyIntent.GiftGiver));
        _hostileMonsterPred = candidate => candidate != this && candidate.IsWild != this.IsWild;
    }

    private void ScanForHostiles()
    {
        if (currentFloor?.Entities == null) return;

        var aggr = EffectiveAggression;
        EnsureScanPredicates();

        // Defensive monsters stay passive during their normal routine, but still
        // engage when retaliating, defending the core, or under an explicit order.
        bool forceEngage = state == MonsterState.DefendCore
                        || (spawner != null && spawner.HasAttackTarget)
                        || (Time.time - lastDamageTime) < defensiveRetaliationDuration;
        if (aggr == MonsterAggression.Defensive && !forceEngage) { target = null; return; }

        // Normal and (retaliating) Defensive spare Pilgrims; Aggressive does not.
        bool sparePilgrims = aggr != MonsterAggression.Aggressive;
        _scanSparePilgrims = sparePilgrims;   // the cached predicates read this

        // Tank taunt (minimal): a taunting adventurer in detection range is
        // preferred over the nearest target. FLAG: expand into full class-aware target
        // priority later (the "Class-aware target priority" backlog item).
        var taunter = currentFloor.Entities.Nearest<DungeonAdventurer>(
            transform.position, detectionRange, _taunterPred);
        if (taunter != null && Time.time >= tauntImmuneUntil) { target = taunter; state = MonsterState.Attack; TryTauntBark(); return; }

        IMonsterTarget nearest = null;
        float nearestDist = detectionRange;

        // Day 42 — class-aware target priority. Gather in-range adventurers (minus
        // spared Pilgrims), then pick by the monster's TargetPriority: hard preference,
        // nearest tie-break; Nearest = pure nearest (unchanged default).
        currentFloor.Entities.WithinRadius(transform.position, detectionRange, _advScanBuf, _advPred);
        var adv = SelectAdventurer(_advScanBuf, out float advDist);
        if (adv != null)
        {
            nearest = adv;
            nearestDist = advDist;
        }

        // Wild-vs-player monster targeting stays nearest-based; a closer hostile
        // monster still preempts the chosen adventurer (unchanged behaviour).
        var m = currentFloor.Entities.Nearest<DungeonMonster>(
            transform.position, nearestDist, _hostileMonsterPred);
        if (m != null) { nearest = m; }

        if (nearest != null) { target = nearest; state = MonsterState.Attack; TryTauntBark(); }
    }

    // ── Class-aware target priority ─────────────────────

    /// <summary>Pick the best adventurer from the in-range buffer by TargetPriority.
    /// Lower PriorityKey wins; ties break by nearest. Null if the buffer is empty.</summary>
    private DungeonAdventurer SelectAdventurer(List<DungeonAdventurer> buf, out float dist)
    {
        DungeonAdventurer best = null;
        float bestKey = float.MaxValue, bestDist = float.MaxValue;
        for (int i = 0; i < buf.Count; i++)
        {
            var a = buf[i];
            if (a == null) continue;
            float d = Vector2.Distance(transform.position, a.transform.position);
            float key = PriorityKey(a);
            if (key < bestKey || (key == bestKey && d < bestDist))
            {
                bestKey = key; bestDist = d; best = a;
            }
        }
        dist = bestDist;
        return best;
    }

    /// <summary>Lower = more preferred. Class modes: 0 for a match, 1 otherwise (so the
    /// nearest match wins, else nearest of anyone). Wounded: HP fraction (lowest wins).
    /// Nearest: 0 for all, so the tie-break yields the nearest.</summary>
    private float PriorityKey(DungeonAdventurer a)
    {
        switch (targetPriority)
        {
            case TargetPriority.Casters: return (a.Class == CombatClass.Mage || a.Class == CombatClass.Cleric) ? 0f : 1f;
            case TargetPriority.Healers: return a.Class == CombatClass.Cleric ? 0f : 1f;
            case TargetPriority.Wounded: return a.MaxHP > 0f ? a.CurrentHP / a.MaxHP : 0f;
            default: return 0f;
        }
    }

    private void AttackTarget()
    {
        if (target == null || !target.IsAlive)
        {
            telegraph?.Cancel();
            target = null;
            attackPath.Clear();
            chaseAnchored = false;
            EnterState(DetermineDesiredState());
            return;
        }

        // Leash: remember where this chase began; give up if it drags too far.
        if (!chaseAnchored)
        {
            chaseAnchor = transform.position;
            chaseAnchored = true;
        }
        if (chaseLeashRadius > 0f
            && Vector2.Distance(transform.position, chaseAnchor) > chaseLeashRadius)
        {
            telegraph?.Cancel();
            target = null;
            attackPath.Clear();
            chaseAnchored = false;
            EnterState(DetermineDesiredState());
            return;
        }

        Vector3 targetPos = target.Transform.position;
        float dist = Vector2.Distance(transform.position, targetPos);
        var rdef = IsWild ? wildDefinition : spawner?.Definition;
        bool ranged = rdef != null && rdef.firesProjectile;
        // A ranged attacker also needs a clear line to the target: blocked within
        // range, it falls through to the chase path below and walks until the shot
        // opens up. Acquisition stays distance-based -- it knows you are there.
        if (dist > attackRange
            || (ranged && !DungeonProjectile.HasLineOfSight(currentFloor, transform.position, targetPos)))
        {
            telegraph?.Cancel();   // target stepped out of range mid-windup — abort the tell

            // Pathfind to the target instead of beelining, so the chase routes around
            // walls and wall overhangs. Refresh on a timer since the target moves.
            attackPathRefreshTimer -= Time.deltaTime;
            bool needsRefresh = attackPath.Count == 0
                             || attackPathIndex >= attackPath.Count
                             || attackPathRefreshTimer <= 0f;
            if (needsRefresh)
            {
                attackPath = DungeonPathfinder.FindPath(currentFloor, transform.position, targetPos);
                attackPathIndex = 0;
                attackPathRefreshTimer = AttackPathRefreshInterval;
            }

            // Unreachable (walled off) — drop the chase and resume normal behaviour.
            if (attackPath.Count == 0)
            {
                target = null;
                chaseAnchored = false;
                EnterState(DetermineDesiredState());
                return;
            }

            Vector3 stepTarget = attackPath[attackPathIndex];
            transform.position = Vector2.MoveTowards(
                transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);
            if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
                attackPathIndex++;
            return;
        }
        attackPath.Clear();

        if (telegraph != null && telegraph.IsWinding) return;   // charging — the strike fires on completion

        if (Time.time - lastAttackTime < attackCooldown) return;
        if (!SpendAttackStamina()) return;
        lastAttackTime = Time.time;

        float windup = rdef != null ? rdef.telegraphSeconds : 0f;
        // Ranged defs loose a projectile at the end of the windup; melee lands as before.
        System.Action strike = ranged ? FireProjectile : (System.Action)DealAttackDamage;
        if (telegraph != null && windup > 0f)
            telegraph.Begin(windup, TelegraphColors.Monster, strike);
        else
            strike();
    }

    // -- Banter --
    private float lastBarkTime = -999f;
    [SerializeField] private float barkCooldown = 14f;

    /// <summary>This monster's voice, taken from its definition. No definition means no voice.</summary>
    public MonsterVoice Voice
    {
        get
        {
            var vdef = IsWild ? wildDefinition : spawner?.Definition;
            return vdef != null ? vdef.voice : MonsterVoice.Silent;
        }
    }

    /// <summary>True when this monster may growl idle chatter (has a voice, not attacking,
    /// alive, off cooldown).</summary>
    public bool CanBanter =>
        gameObject.activeInHierarchy
        && Voice != MonsterVoice.Silent
        && state != MonsterState.Attack
        && Time.time - lastBarkTime >= barkCooldown;

    public void Say(string line, Color colour)
    {
        if (string.IsNullOrEmpty(line)) return;
        BarkSpawner.Spawn(transform.position, line, colour);
        lastBarkTime = Time.time;
    }

    /// <summary>A heavy hit from a non-taunting adventurer peels this monster off the taunt: it
    /// ignores taunters briefly and re-targets (usually onto the one who just hit it).</summary>
    public void PeelFromTaunt()
    {
        tauntImmuneUntil = Time.time + tauntPeelDuration;
        if (target is DungeonAdventurer ta && ta.IsTaunting) target = null;
    }

    private void TryTauntBark()
    {
        // Chance to taunt on locking onto a delver ("fresh meat"), in this monster's own voice.
        if (Voice == MonsterVoice.Silent) return;
        if (Random.value > BanterLines.MonsterTauntChance) return;
        if (Time.time - lastBarkTime < barkCooldown) return;
        Say(BanterLines.RandomTaunt(Voice), BanterLines.MonsterBark);
    }

    /// <summary>Loose the ranged attack at the end of the telegraph. The projectile
    /// carries the full payload, so the shot lands its damage, knockback and
    /// formation break even if this monster falls mid-flight; transform-touching
    /// kill credit is guarded and forfeited by a dead shooter.</summary>
    private void FireProjectile()
    {
        if (target == null || !target.IsAlive) return;
        var def = IsWild ? wildDefinition : spawner?.Definition;
        if (def == null || !def.firesProjectile) { DealAttackDamage(); return; }

        // Mutation research sharpens dungeon monsters only; the wild ruling holds.
        float dmg = attackDamage * roomDamageMultiplier * globalDamageMultiplier * crowdDamageMultiplier
                  * (IsWild ? 1f : MonsterMastery.DamageMultiplier)
                  * (boons != null ? boons.DamageMultiplier : 1f);
        animDriver?.OnAttack();

        var payload = new DungeonProjectile.Payload
        {
            damage = dmg,
            numberType = FloatingDamageNumber.DamageType.AdventurerHit,
            sourceName = TypeName,
            fromOutsider = IsWild,
            knockbackForce = def.knockbackForce,
            knockbackMinDamage = def.knockbackMinDamage,
            breaksFormation = def.breaksFormation,
            breakSeconds = def.formationBreakSeconds,
            onKill = fallen =>
            {
                if (this == null) return;   // a dead shooter forfeits the credit
                killCount++;
                GainXP(xpPerKill);
                if (fallen is DungeonAdventurer hero && hero.IsNamedHero)
                    GrantKillTitle(hero.DisplayName);
                if (ReferenceEquals(target, fallen)) target = null;
            },
        };
        DungeonProjectile.Fire(currentFloor, transform.position, target,
            def.projectileSpeed, def.projectileTint, def.projectileSprite, payload);
    }

    private void DealAttackDamage()
    {
        if (target == null || !target.IsAlive) return;

        // Mutation research sharpens dungeon monsters only; the wild ruling holds.
        float dmg = attackDamage * roomDamageMultiplier * globalDamageMultiplier * crowdDamageMultiplier
                  * (IsWild ? 1f : MonsterMastery.DamageMultiplier)
                  * (boons != null ? boons.DamageMultiplier : 1f);
        Vector3 targetPos = target.Transform.position;
        DamageNumberSpawner.Spawn(dmg, targetPos, FloatingDamageNumber.DamageType.AdventurerHit);
        animDriver?.OnAttack();
        var advTarget = target as DungeonAdventurer;
        advTarget?.RecordDamagedBy(TypeName, dmg);
        // A WILD attacker is an outsider; a commanded one is the dungeon's arm.
        target.TakeDamage(dmg, IsWild);
        var kdef = IsWild ? wildDefinition : spawner?.Definition;
        if (kdef != null && kdef.knockbackForce > 0f && dmg >= kdef.knockbackMinDamage)
            target.ApplyKnockback(transform.position, kdef.knockbackForce);
        if (kdef != null && kdef.breaksFormation && advTarget != null)
            advTarget.BreakFormation(kdef.formationBreakSeconds);
        if (!target.IsAlive)
        {
            killCount++; GainXP(xpPerKill);
            if (advTarget != null && advTarget.IsNamedHero) GrantKillTitle(advTarget.DisplayName);
            target = null;
        }
    }

    private void GainXP(float amount)
    {
        monsterXP += amount;
        TryPromoteToVeteran();
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — single-flip veteran promotion.
    /// Gates:
    ///   - already veteran  → skip
    ///   - boss monster     → skip (boss stack does not stack with veteran)
    ///   - wild monster     → skip (player monsters only; see Passive Backlog)
    /// </summary>
    private void TryPromoteToVeteran()
    {
        if (isVeteran) return;
        if (IsBoss) return;
        if (IsWild) return;
        if (monsterXP < xpToVeteran) return;

        ApplyVeteranPromotion();
    }

    private void ApplyVeteranPromotion()
    {
        isVeteran = true;

        // Scale currentHP proportionally so a 20/30 monster becomes 30/45,
        // not 20/45. No free heal, no anticlimactic mid-health veteran.
        float hpRatio = maxHP > 0f ? currentHP / maxHP : 1f;
        maxHP *= veteranHpMultiplier;
        currentHP = maxHP * hpRatio;

        attackDamage *= veteranDamageMultiplier;
        xpPerKill *= veteranXpRewardMultiplier;

        ApplyVeteranVisuals();
    }

    private void ApplyVeteranVisuals()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = veteranTint;

        if (statusBars != null)
        {
            statusBars.SetHP(currentHP, maxHP);
            RefreshNameplate();
        }
    }

    public void TakeDamage(float amount) => TakeDamage(amount, false);

    public void TakeDamage(float amount, bool fromOutsider)
    {
        // Credit is recorded BEFORE the wound lands, because a killing blow
        // runs Die() from inside this method and a flag set afterwards would
        // arrive after the bestiary had already been asked.
        if (!fromOutsider) dungeonDealtDamage = true;
        // Mutation tier II toughens the dungeon's own; wilds and invaders take
        // full wounds. Applied to incoming damage rather than maxHP so the
        // node is retroactive for monsters already alive when it completes.
        if (!IsWild) amount *= MonsterMastery.DamageTakenMultiplier;
        // Root the Stone. Applied to incoming damage rather than to maxHP for the
        // same reason the mutation line is: it must be retroactive for a monster
        // already wounded when the working lands.
        if (boons != null) amount *= boons.DamageTakenMultiplier;
        lastDamageTime = Time.time;
        pendingHealDisplay = 0f;
        currentHP -= amount;
        if (isHungryPredator && !predatorWounded && maxHP > 0f && currentHP / maxHP < predatorWoundedFraction)
            predatorWounded = true;
        statusBars?.SetHP(currentHP, maxHP);
        GetComponent<DamageFlash>()?.Flash();
        if (currentHP <= 0f) Die();
        else animDriver?.OnHurt();
    }

    /// <summary>Raised whenever any dungeon monster is slain. The wisp listens for the first loss.</summary>
    public static event System.Action OnAnyMonsterSlain;

    private void Die()
    {
        OnAnyMonsterSlain?.Invoke();
        // You field what you defeat. The unlock is gated on the dungeon having
        // wounded it; the XP and the run statistic below deliberately are NOT,
        // because those record that something died here while the bestiary
        // records what this dungeon put down.
        if (IsWild && wildDefinition != null && dungeonDealtDamage)
            BestiaryState.Instance?.Discover(wildDefinition.monsterName);

        if (IsWild)
        {
            RunStats.Instance?.RecordWildMonsterSlain();
            if (wildDefinition != null && wildDefinition.wildCoreXpOnDeath > 0f)
                DungeonCore.Instance?.AddXP(wildDefinition.wildCoreXpOnDeath);
        }

        currentFloor?.Entities?.Unregister(this);
        if (statusBars != null) Destroy(statusBars.gameObject);
        var lootTable = GetComponent<LootTable>();
        if (lootTable != null)
        {
            var promoTemplate = DungeonBuildController.Instance != null
                ? DungeonBuildController.Instance.Promotion : null;
            float lootMult = promoTemplate != null ? promoTemplate.LootMult(promotedRank) : 1f;
            lootTable.Roll(transform.position, lootMult);
        }

        if (IsBoss)
        {
            TimeScaleController.Instance?.DoBossHitstop();
            ScreenShake.Instance?.ShakeBossDeath();
        }
        else
        {
            TimeScaleController.Instance?.DoKillHitstop();
        }

        spawner?.OnMonsterDied();
        OnDied?.Invoke(this);

        animDriver?.OnDeath();
        enabled = false;                 // freeze behaviour; the Animator plays the death clip
        Destroy(gameObject, deathAnimSeconds);
    }

    /// <summary>Transient raised minions get a finite lifetime; 0 leaves the monster permanent.</summary>
    public void SetLifetime(float seconds) { lifetimeRemaining = Mathf.Max(0f, seconds); }

    // Necromancy methods

    /// <summary>Necromancer behaviour tick. Returns true while channeling a raise (hold
    /// position this frame). Reads its params from the monster definition.</summary>
    private bool TickNecromancer()
    {
        var def = IsWild ? wildDefinition : spawner?.Definition;
        if (def == null) return false;

        if (raiseCooldownRemaining > 0f) raiseCooldownRemaining -= Time.deltaTime;

        for (int i = risenSpawners.Count - 1; i >= 0; i--)
            if (risenSpawners[i] == null) risenSpawners.RemoveAt(i);

        if (isChanneling)
        {
            ScanForHostiles();   // a threat cancels the channel
            if (target != null || channelTarget == null || channelTarget.Claimed
                || Vector2.Distance(transform.position, channelTarget.transform.position) > def.raiseRange)
            {
                isChanneling = false;
                channelTarget = null;
                return false;
            }
            channelRemaining -= Time.deltaTime;
            if (channelRemaining <= 0f)
            {
                RaiseCorpse(def, channelTarget);
                isChanneling = false;
                channelTarget = null;
                raiseCooldownRemaining = def.raiseCooldown;
                return false;
            }
            return true;
        }

        if (raiseCooldownRemaining > 0f) return false;
        if (target != null) return false;
        if (risenSpawners.Count >= def.maxRisen) return false;
        if (def.risenDefinitions == null || def.risenDefinitions.Count == 0) return false;

        var corpse = FindRaisableCorpse(def.raiseRange);
        if (corpse == null) return false;

        channelTarget = corpse;
        channelRemaining = def.raiseChannelSeconds;
        isChanneling = true;
        return true;
    }

    /// <summary>Nearest un-claimed corpse within range, or null.</summary>
    private Corpse FindRaisableCorpse(float range)
    {
        Corpse best = null;
        float bestSqr = range * range;
        var list = Corpse.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c == null || c.Claimed || c.IsNamed) continue;   // the named dead answer only the core
            float d = ((Vector2)(c.transform.position - transform.position)).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = c; }
        }
        return best;
    }

    /// <summary>Consume a corpse and spawn a random risen minion at it (transient, capped).</summary>
    private void RaiseCorpse(MonsterDefinition def, Corpse corpse)
    {
        if (corpse == null || corpse.Claimed) return;
        var floor = currentFloor;
        if (floor?.TileInfluence == null) { corpse.Claim(); return; }

        var risenDef = def.risenDefinitions[Random.Range(0, def.risenDefinitions.Count)];
        Vector3Int cell = floor.TileInfluence.WorldToCell(corpse.transform.position);
        corpse.Claim();
        if (risenDef == null) return;

        var spawner = DungeonBuildController.Instance?.SpawnTransientMinion(floor, risenDef, cell, def.risenLifetime);
        if (spawner != null) risenSpawners.Add(spawner);
    }

    /// <summary>
    /// Phase 3 closeout (#1) — remove with no loot, no respawn and no death event
    /// (player removed this monster's spawner). Destroys the separate status-bar
    /// object that OnDestroy alone would orphan.
    /// </summary>
    public void DespawnSilently()
    {
        if (statusBars != null) Destroy(statusBars.gameObject);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Safety net for any teardown path that skips Die() (e.g. scene unload).
        currentFloor?.Entities?.Unregister(this);
        UnregisterNovelty();
    }

    // ── IMonsterTarget ────────────────────────────────────────────
    Transform IMonsterTarget.Transform => transform;
    bool IMonsterTarget.IsAlive
    {
        get
        {
            if (this == null) return false;
            if (gameObject == null) return false;
            return gameObject.activeInHierarchy && currentHP > 0f;
        }
    }
    void IMonsterTarget.TakeDamage(float amount) => TakeDamage(amount);
    void IMonsterTarget.TakeDamage(float amount, bool fromOutsider)
        => TakeDamage(amount, fromOutsider);

    void IMonsterTarget.ApplyKnockback(Vector2 fromPos, float force)
    {
        if (force <= 0f) return;
        telegraph?.Cancel();
        Vector2 d = (Vector2)transform.position - fromPos;
        knockbackDir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.right;
        knockbackRemaining = force;
    }

    private void KnockbackStep()
    {
        float step = Mathf.Min(knockbackRemaining, knockbackSpeed * Time.deltaTime);
        Vector3 next = transform.position + (Vector3)(knockbackDir * step);
        if (DungeonPathfinder.IsWalkable(currentFloor, next)) transform.position = next;
        else knockbackRemaining = 0f;
        knockbackRemaining -= step;
    }

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;

    /// <summary>Calm, unengaged and alive -- eligible for Arena sparring.</summary>
    public bool IsSparReady =>
        currentHP > 0f && target == null
        && (state == MonsterState.Idle || state == MonsterState.Wander || state == MonsterState.Patrol);

    public float MonsterXP => monsterXP;
    public int KillCount => killCount;
    public bool PredatorWounded => predatorWounded;
    public bool PredatorLeaving => predatorLeaving;
    public float XpToVeteran => xpToVeteran;

    /// <summary>This monster's type name (its definition), for grudge matching. Null if unresolved.</summary>
    public string TypeName
    {
        get { var d = IsWild ? wildDefinition : spawner?.Definition; return d != null ? d.monsterName : null; }
    }

    /// <summary>Records this monster felling a named Hero. Instance-only; a respawn starts untitled.</summary>
    public void GrantKillTitle(string heroName)
    {
        if (string.IsNullOrEmpty(heroName)) return;
        killTitle = $"Slayer of {heroName}";
        AlertsLog.Instance?.AddAlert(
            $"My {TypeName} has felled {heroName}. It will be remembered.",
            transform.position, currentFloor != null ? currentFloor.FloorIndex : -1,
            AlertCategory.Combat);
    }

    /// <summary>Name for the info panel: boss title, or "Veteran {name}", or the base name.</summary>
    public string DisplayName
    {
        get
        {
            string custom = spawner != null ? spawner.CustomName : null;
            string bossT = promotedTitle ?? bossDefinition?.GetBossTitle();
            if (bossT != null)
                return !string.IsNullOrEmpty(custom) ? custom : bossT;
            var def = IsWild ? wildDefinition : spawner?.Definition;
            string n = def != null ? def.monsterName : "Monster";
            string full = !string.IsNullOrEmpty(custom) ? custom : (isVeteran ? $"Veteran {n}" : n);
            return string.IsNullOrEmpty(killTitle) ? full : $"{full} — {killTitle}";
        }
    }
    /// <summary>The player-set name for this monster (persisted on its spawner), or null.</summary>
    public string CustomName => spawner != null ? spawner.CustomName : null;

    /// <summary>True if this monster can be renamed (spawner-backed, so the name persists).</summary>
    public bool CanRename => spawner != null && !IsWild;

    /// <summary>What a rename field prefills with: the boss title, or the plain type name.</summary>
    public string BaseName
           => promotedTitle ?? (bossDefinition != null ? bossDefinition.GetBossTitle() : TypeName);

    /// <summary>Rename this monster (empty clears back to its type name). Persists via the spawner.</summary>
    public void Rename(string newName)
    {
        if (spawner != null) spawner.SetCustomName(newName);   // spawner refreshes the nameplate
    }

    /// <summary>Push the current boss / veteran / custom-name state onto the overhead label.</summary>
    public void RefreshNameplate()
    {
        if (statusBars == null) return;
        statusBars.SetMonsterLabel(
                  promotedTitle ?? (bossDefinition != null ? bossDefinition.GetBossTitle() : null),
                  isVeteran,
            CustomName,
            TypeName);
    }

    public FloorRoot CurrentFloor => currentFloor;
    public BossVariantDefinition BossDefinition => bossDefinition;

    /// <summary>Phase 3 closeout (#1) - true while actively fighting a target.</summary>
    public bool IsInCombat => state == MonsterState.Attack;

    /// <summary>The current combat target's transform, or null if not fighting.
    /// Used by the targeting-line visuals. Update() nulls dead targets, so this is
    /// safe to read in LateUpdate.</summary>
    public Transform CombatTarget => (target != null && target.IsAlive) ? target.Transform : null;
}