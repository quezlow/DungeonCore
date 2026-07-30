using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon adventurer. Pathfinds to the Core Room (across floors via stairs),
/// detours toward chests, fights monsters, collects CarriableLoot, and retreats.
///
/// MULTI-FLOOR (Day 27)
///   Each adventurer tracks its own FloorRoot (currentFloor), set from its
///   parent hierarchy in Start(). All pathfinding and trap queries use the
///   adventurer's own floor — never the player-viewed floor.
///
/// DAY 31 PART 1 — RIVER FORDING
///   terrainSpeedMultiplier drops to features.FordingSpeedMultiplier on
///   river cells, folded into every MoveTowards call.
///
/// DAY 31 PART 2 — IMonsterTarget
///   Adventurers now implement IMonsterTarget so they can be the polymorphic
///   target of a DungeonMonster's scan/combat (alongside hostile monsters
///   like wild cave dwellers). The existing public bool TakeDamage(float)
///   API is unchanged — other callers keep using it. The interface impl is
///   explicit and forwards to that method, discarding the bool return.
///
/// INITIALISE must be called by AdventurerSpawner before Start() runs.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour, IMonsterTarget
{
    public enum AdventurerState
    {
        MovingToCore,
        MovingToChest,
        Combat,
        Retreating,
        UsingStairs,
        Disarming,      // Rogue channelling to neutralise a flagged trap
        Worshipping,
        MovingToRoom,   // observers heading to a room
        Hunting,        // delver seeking the next monster to fight
        Observing,      // dwelling in a room
        Organizing,     // Forming up at the entrance before advancing
        Pinned,         // snared in a capture-trap, awaiting rescue or the cell
        MovingToRescue, // converging on a pinned ally to cut them loose
    }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP = 50f;
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("Move-speed fraction lost per gold of carried loot (encumbrance). 0 = carrying never slows.")]
    [SerializeField] private float encumbrancePerGold = 0.003f;
    [Tooltip("Floor on the encumbrance multiplier, so even a huge haul still moves (0.5 = half speed).")]
    [SerializeField] private float encumbranceFloor = 0.5f;
    [SerializeField] private float attackDamage = 8f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float detectionRange = 2.5f;

    [Header("Delving")]
    [Tooltip("How far a Delver looks for prey - large enough to cover a whole floor.")]
    [SerializeField] private float huntRange = 1000f;
    [Tooltip("Rooms a Delver searches with no monster in sight before it leaves.")]
    [SerializeField] private int delveSearchRooms = 3;
    [Tooltip("Generous backstop: a Delver leaves after this many kills even if unhurt. 0 = no cap.")]
    [SerializeField] private int delveKillCap = 12;

    [SerializeField] private float knockbackForce = 0f;        // shove distance on a heavy hit; 0 = none
    [SerializeField] private float knockbackMinDamage = 0f;    // min hit damage to trigger knockback
    [SerializeField] private float knockbackSpeed = 8f;        // shove travel speed (units/sec)
    private Vector2 knockbackDir;
    private float knockbackRemaining;
    private float telegraphSeconds = 0f;                       // windup before a hit (from combat class); 0 = instant
    private AttackTelegraph telegraph;

    [Header("Behaviour")]
    [SerializeField] private float retreatThreshold = 0.3f;

    [Tooltip("Commoners are civilians: a monster sighted within this radius sends them fleeing.")]
    [SerializeField] private float commonerPanicRange = 5f;

    [Header("Intent")]
    [Tooltip("Pilgrims move at this fraction of normal speed.")]
    [SerializeField] private float pilgrimSpeedMultiplier = 0.7f;
    [Tooltip("Seconds a Pilgrim dwells at the core before leaving peacefully.")]
    [SerializeField] private float worshipDuration = 4f;
    [Tooltip("Notoriety removed once per party on a completed pilgrimage exit.")]
    [SerializeField] private float pilgrimNotorietyReduction = 10f;
    [Tooltip("Core XP granted once per party when a worship-completed Pilgrim " +
             "party exits peacefully. Faith feeds the reborn god.")]
    [SerializeField] private float pilgrimXpReward = 10f;

    [Header("Type Behaviour")]
    [Tooltip("Seconds an observer (Scholar / Inspector / Noble) dwells in each room.")]
    [SerializeField] private float observeDwellDuration = 3f;
    [Tooltip("How many rooms an observer visits before leaving.")]
    [SerializeField] private int maxRoomsToObserve = 3;
    [Tooltip("Suicidal death grants xpOnDeath * this multiplier.")]
    [SerializeField] private float suicidalXpMultiplier = 2f;
    [Tooltip("Reputation granted when a Suicidal achieves a glorious death.")]
    [SerializeField] private float suicidalReputationGain = 3f;

    [Header("Separation")]
    [SerializeField] private float separationRadius = 0.6f;
    [SerializeField] private float separationStrength = 1.5f;
    [SerializeField] private float chestDetectionRange = 3f;

    [Header("Loot Pickup")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("XP & Notoriety")]
    [SerializeField] private float xpOnDeath = 15f;

    [Header("Leveling (named / tracked heroes)")]
    [Tooltip("XP a tracked hero gains per monster kill.")]
    [SerializeField] private float xpPerKill = 25f;
    [Tooltip("Total XP per level step: level = 1 + totalXP / this, capped at the tier ladder max (God 1).")]
    [SerializeField] private float xpPerLevel = 100f;
    [Tooltip("Max HP added per level above 1 (0.10 = +10% per level).")]
    [SerializeField] private float hpPerLevel = 0.10f;
    [Tooltip("Attack damage added per level above 1 (0.08 = +8% per level).")]
    [SerializeField] private float damagePerLevel = 0.08f;
    [Tooltip("Notoriety removed per gold of loot an adventurer escapes with (satisfied departure).")]
    [SerializeField] private float lootSatisfactionFactor = 0.1f;
    [Tooltip("Maximum notoriety a single satisfied escape can remove.")]
    [SerializeField] private float lootSatisfactionCap = 25f;
    private string className = "Adventurer";

    [Header("Dropped Loot Prefab")]
    [SerializeField] private DroppedLoot droppedLootPrefab;
    [Tooltip("Corpse left at this adventurer's death spot; a necromancer can raise it. None = no corpse.")]
    [SerializeField] private GameObject corpsePrefab;

    [Header("Loot")]
    [Tooltip("Gold-value multiplier on this unit's class loot when it's a guard escorting a VIP (Noble / Scholar / Inspector).")]
    [SerializeField] private float escortGuardLootMultiplier = 1.75f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    [Header("Animation")]
    [Tooltip("Seconds to hold the body after death so the death clip can play before despawn. 0 = despawn immediately.")]
    [SerializeField] private float deathAnimSeconds = 0f;
    private EntityAnimationDriver animDriver;

    [Header("Trap Detection")]
    [SerializeField] private bool canDetectTraps = false;
    [SerializeField] private float trapDetectionRadius = 2.5f;
    [SerializeField] private float trapDetectionChancePerSecond = 0.3f;
    [Tooltip("Seconds the party pauses when this Rogue spots a new trap.")]
    [SerializeField] private float trapHaltDuration = 1f;
    [Tooltip("Minimum seconds between trap-warning halts (anti-stutter).")]
    [SerializeField] private float trapHaltCooldown = 4f;

    [Tooltip("Seconds a Rogue spends channelling to disarm one flagged trap.")]
    [SerializeField] private float disarmChannelSeconds = 1.5f;
    [Tooltip("Chance a disarm attempt backfires, firing the trap on the Rogue once before it retries.")]
    [SerializeField, Range(0f, 1f)] private float disarmBackfireChance = 0.2f;
    [Tooltip("How close a Rogue must get to a flagged trap before it stops to disarm it.")]
    [SerializeField] private float disarmReach = 1.1f;

    [Header("Stair Traversal")]
    [SerializeField] private float stairTraversalDuration = 1.5f;

    [Header("Resource Regen")]
    [Tooltip("Stamina regen per second: slow in combat, fast out of combat.")]
    [SerializeField] private float staminaRegenInCombat = 3f;
    [SerializeField] private float staminaRegenOutOfCombat = 15f;
    [Tooltip("Mana regen per second: near-zero in combat (attrition), fast out of combat.")]
    [SerializeField] private float manaRegenInCombat = 0.5f;
    [SerializeField] private float manaRegenOutOfCombat = 12f;

    [Header("Capture Pin (capture-trap)")]
    [Tooltip("Chance a Cowardly member writes off a pinned ally instead of going back.")]
    [Range(0f, 1f)][SerializeField] private float cowardAbandonChance = 0.5f;
    [Tooltip("HP fraction a rescued ally is left at -- freed, but wounded by the ordeal.")]
    [Range(0.05f, 1f)][SerializeField] private float rescuedHpFraction = 0.4f;

    [Header("Formation March (tactical layer)")]
    [Tooltip("A follower within this distance of the lead is back in the body and resumes holding its slot.")]
    [SerializeField, Min(0.5f)] private float formationRejoinRange = 2.5f;
    [Tooltip("A follower beyond this distance from the lead has fallen out and paths in individually to rejoin.")]
    [SerializeField, Min(1f)] private float formationLostRange = 5f;
    [Tooltip("How far a holding follower may lag its (walkable) slot before the lead pauses for it -- the pace-to-slowest slack.")]
    [SerializeField, Min(0.25f)] private float formationStragglerSlack = 1.25f;

    [Header("Shield Wall (formation effect)")]
    [Tooltip("Damage reduction for a rear member (not front rank, not fleeing) while a living front rank stands.")]
    [Range(0f, 0.9f)][SerializeField] private float shieldWallMitigation = 0.4f;

    [Header("Tank Taunt (ability)")]
    [Tooltip("Stamina a Tank spends to taunt. Regen paces how often it can be used.")]
    [SerializeField, Min(0f)] private float tauntStaminaCost = 30f;
    [Tooltip("Seconds a taunt holds monster focus on the tank.")]
    [SerializeField, Min(0.5f)] private float tauntDuration = 5f;
    [Tooltip("Extra seconds after a taunt ends before the tank may taunt again.")]
    [SerializeField, Min(0f)] private float tauntRecovery = 1.5f;
    [Tooltip("A single hit from another party member of at least this fraction of a monster's max HP peels it off the taunt.")]
    [Range(0.05f, 1f)][SerializeField] private float peelDamageFraction = 0.2f;

    // ── Slow effect ───────────────────────────────────────────────
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // Room slow (Dread Chamber): reapplied each effect tick while standing in
    // the room, reset to 1 by RoomEffectController when the intruder leaves.
    private float roomSlowMultiplier = 1f;

    // ── Terrain speed (DAY 31) ───────────────────────────────────
    private float terrainSpeedMultiplier = 1f;

    // ── Runtime state ─────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;
    private BehaviourTrait trait = BehaviourTrait.Balanced;

    // Capture-trap pin: pinTimer counts down while Pinned; rescueTargetAdv is the
    // pinned ally a rescuer is converging on while MovingToRescue.
    private float pinTimer;
    private DungeonAdventurer rescueTargetAdv;

    // Prison raid (2b): a rescue party or ransom-bearer makes for a held captive's
    // cell (raidTargetPos on raidTargetFloor). A ransom-bearer pays ransomValue on
    // reaching it; a rescue party frees whoever is there for nothing.
    private Vector3 raidTargetPos;
    private int raidTargetFloor;
    private bool hasRaidTarget;
    private bool isRansomBearer;
    private int ransomValue;

    // Formation march (tactical layer): FacingDir is the lead's heading, read by followers to
    // place their class-ranked slots; holdingFormation toggles between holding the slot and
    // pathing in to rejoin; formationStraggling flags a follower lagging a walkable slot so the
    // lead knows to wait. Transient -- none of it is saved.
    public Vector2 FacingDir { get; private set; } = Vector2.zero;
    private bool holdingFormation = true;
    private bool formationStraggling;
    public bool IsStraggling => formationStraggling;

    // Intent — assigned in Initialise, shared via the party object.
    private AdventurerParty party;
    private PartyIntent intent = PartyIntent.Destroyer;
    private bool worshipCompleted = false;
    private float worshipTimer = 0f;

    // Type / goal + observer state
    private AdventurerType type = AdventurerType.Mercenary;
    private AdventurerGoal goal = AdventurerGoal.BreachCore;

    // Inspector escalation — adventurer deaths witnessed during this unit's visit.
    public static int AdventurerDeaths { get; private set; }

    /// <summary>Raised the first time a party spawns this dungeon session. The wisp listens.</summary>
    public static event System.Action OnFirstPartySpawned;

    /// <summary>Raised whenever any adventurer is slain. The wisp listens for first blood.</summary>
    public static event System.Action OnAnyAdventurerSlain;

    private static bool firstPartyAnnounced;

    /// <summary>Called by the spawner when an organic wave enters the dungeon.</summary>
    public static void NotifyPartySpawned()
    {
        if (firstPartyAnnounced) return;
        firstPartyAnnounced = true;
        OnFirstPartySpawned?.Invoke();
    }

    /// <summary>Session reset so a new dungeon re-arms the one-shot announce.</summary>
    public static void ResetSessionSignals() => firstPartyAnnounced = false;
    private int deathsAtArrival;
    private RoomAnchor roomTarget;
    private readonly HashSet<RoomAnchor> visitedRooms = new();
    private int roomsObserved = 0;
    private float observeTimer = 0f;
    private int searchRoomsRemaining = 0;
    private int delveKills = 0;
    private bool leftSatisfied = false;   // observer finished its tour and departs of its own accord (vs forced flight)

    // Combat class (Day 39) — overlay applied in Initialise
    private CombatClass combatClass = CombatClass.Fighter;
    private CombatClassDefinition classDef;   // kept for class loot on death
    private DungeonType affinity = DungeonType.None;
    public DungeonType Affinity => affinity;
    private string flavorClassName;
    public string FlavorClassName => string.IsNullOrEmpty(flavorClassName) ? combatClass.ToString() : flavorClassName;
    private MonsterDefinition unlocksOnDeath;
    private bool unlockRequiresDarkCore;

    // Named-adventurer tracking — this unit's roster record + identity.
    private PartyMember partyMember;
    [Tooltip("Damage per second the core inflicts on a death-seeker kneeling before it.")]
    [SerializeField] private float coreRoomBurnPerSecond = 15f;
    private string displayName;
    private bool named;

    // Grudge — cumulative damage taken per monster type. The worst offender is the
    // grudge carried home on survival; returnGrudge is one brought in from last visit.
    private readonly Dictionary<string, float> damageByType = new();
    private string grudgeMonster;      // running arg-max of damageByType this raid
    private float grudgeDamage;        // damage total behind the current grudge
    private string returnGrudge;       // grudge from a prior visit — biases target choice
    // Inspector parties observe; they never START a monster fight, but a hit
    // provokes them (handled in TakeDamage).
    private bool passiveUnlessProvoked;
    public void SetPassiveUnlessProvoked(bool v) => passiveUnlessProvoked = v;

    private bool healsAllies = false;
    private float healAmount = 6f;
    private float healInterval = 3f;
    private float healRadius = 4f;
    private float healTimer = 0f;
    private bool taunts = false;
    private int scoutRoomsRemaining = 0;
    private static readonly List<DungeonAdventurer> _healBuf = new();

    // Resources — pools/costs from the class; current values run down in combat.
    private float maxStamina = 0f;
    private float maxMana = 0f;
    private float attackCost = 0f;
    private bool attackUsesMana = false;
    private float healManaCost = 0f;
    private float currentStamina = 0f;
    private float currentMana = 0f;

    // Tank taunt (active ability): tauntTimer counts down while the taunt holds; IsTaunting is
    // driven by it now (no longer an always-on class flag). tauntCooldownEnd paces re-taunts.
    private float tauntTimer;
    private float tauntCooldownEnd;

    /// <summary>Front-rank classes stand in the shield wall and take blows for the rear.</summary>
    public bool IsFrontRank => combatClass == CombatClass.Tank || combatClass == CombatClass.Fighter;

    // Formation — assigned slot to hold during Organizing.
    private Vector3? formationSlot;

    // Loose muster (formation-less parties): drift target near the mouth.
    private Vector3 musterTarget;
    private float nextMusterStepTime;

    private List<Vector3> currentPath = new();
    private int pathIndex = 0;

    private List<Vector3> combatPath = new();
    private int combatPathIndex = 0;
    private float combatPathRefreshTimer = 0f;
    private const float CombatPathRefreshInterval = 0.4f;

    private float lastAttackTime;
    private IMonsterTarget combatTarget;
    private DungeonChest chestTarget;
    private EntityStatusBars statusBars;
    private LootTable lootTable;

    private readonly List<CarriableLoot> carriedLoot = new();
    private int restoredCarriedGold;                   // carried gold from a mid-raid save (no loot objects)
    private LiveMemberSaveData pendingRestore;         // applied at the end of Start once status bars exist

    // ── Tribute bearing (Pilgrim / Cultist gift delivery) ────────
    private int tributeValue;
    private TributeChest tributePrefab;
    private float tributeAbsorbDelay = 1.5f;
    private float tributeScatter = 1.2f;
    private GameObject tributeVisual;
    public bool CarryingTribute => tributePrefab != null && tributeValue > 0;
    private readonly HashSet<DungeonChest> visitedChests = new();

    // Multi-floor state
    private FloorRoot currentFloor;
    private DungeonStairs stairTarget;
    private float stairTimer;
    private AdventurerState stateBeforeStairs;

    private TrapBase disarmTarget;
    private float disarmTimer;
    private AdventurerState stateBeforeDisarm;

    // ── Initialise ────────────────────────────────────────────────

    public void Initialise(AdventurerDefinition def, BehaviourTrait assignedTrait, AdventurerParty assignedParty, CombatClassDefinition classDef = null, string presetName = null, int returningXp = 0, string returningGrudge = null, DungeonType memberAffinity = DungeonType.None)
    {
        if (def != null)
        {
            maxHP = def.maxHP * CombatBalance.AdventurerHp;
            moveSpeed = def.moveSpeed;
            attackDamage = def.attackDamage * CombatBalance.AdventurerDamage;
            attackRange = def.attackRange;
            attackCooldown = def.attackCooldown;
            knockbackForce = def.knockbackForce;
            knockbackMinDamage = def.knockbackMinDamage;
            detectionRange = def.detectionRange;
            chestDetectionRange = def.chestDetectionRange;
            xpOnDeath = def.xpOnDeath;
            className = def.className;
            canDetectTraps = def.canDetectTraps;
            trapDetectionRadius = def.trapDetectionRadius;
            trapDetectionChancePerSecond = def.trapDetectionChancePerSecond;
            unlocksOnDeath = def.unlocksOnDeath;
            unlockRequiresDarkCore = def.unlockRequiresDarkCore;
        }

        type = def != null ? def.type : AdventurerType.Mercenary;
        intent = AdventurerTypeInfo.IntentOf(type);
        goal = AdventurerTypeInfo.GoalOf(type);

        trait = (def != null && def.overrideTrait) ? def.forcedTrait : assignedTrait;
        retreatThreshold = trait switch
        {
            BehaviourTrait.Cautious => 0.5f,
            BehaviourTrait.Balanced => 0.3f,
            BehaviourTrait.Aggressive => 0.1f,
            BehaviourTrait.Cowardly => 1.0f,
            _ => 0.3f,
        };
        // Suicidal seeks a glorious death — never retreats on HP.
        if (goal == AdventurerGoal.SeekDeath) retreatThreshold = -1f;

        party = assignedParty;
        if (intent == PartyIntent.Pilgrim)
            moveSpeed *= pilgrimSpeedMultiplier;

        // A member with a preset name (a returning nemesis champion) is named
        // even when its base definition is not, so its banner reads the name.
        named = (def != null && def.named) || !string.IsNullOrEmpty(presetName);
        displayName = presetName;

        ApplyCombatClass(classDef);
        affinity = memberAffinity;

        partyMember = party?.RegisterMember(type, displayName, named);
        if (partyMember != null)
        {
            partyMember.combatClass = combatClass;
            partyMember.affinity = affinity;
            partyMember.xp = returningXp;
            partyMember.trait = trait;
            ApplyLevelBoost(LevelFromXp(returningXp));
        }
        returnGrudge = returningGrudge;
        party?.RegisterLive(this);
    }

    /// <summary>Compute this adventurer's flavour name and apply its affinity sprite
    /// tint. Called by the spawner after Initialise, once affinity and class are set.</summary>
    public void ApplyAffinityVisuals(AffinityProfiles profiles)
    {
        if (profiles == null) return;
        flavorClassName = profiles.FlavorName(combatClass, affinity);
        if (affinity == DungeonType.None) return;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            // Tint the hue only. An affinity colour saved with alpha < 255 would otherwise
            // bleed its transparency into the sprite and leave the adventurer see-through.
            float alpha = sr.color.a;
            Color tinted = Color.Lerp(sr.color, profiles.ColorFor(affinity), profiles.TintStrength);
            tinted.a = alpha;
            sr.color = tinted;
        }
        GetComponent<DamageFlash>()?.RefreshBaseColors();
    }

    /// <summary>Day 39 — overlay the combat-class multipliers + behaviour on top of
    /// the type's base stats. Null (non-combatants) leaves the member a plain Fighter.</summary>
    private void ApplyCombatClass(CombatClassDefinition c)
    {
        if (c == null) return;
        combatClass = c.combatClass;
        classDef = c;

        maxHP *= c.hpMultiplier;
        moveSpeed *= c.moveSpeedMultiplier;
        attackDamage *= c.attackDamageMultiplier;
        attackRange *= c.attackRangeMultiplier;
        attackCooldown *= c.attackCooldownMultiplier;
        telegraphSeconds = c.telegraphSeconds;
        detectionRange *= c.detectionRangeMultiplier;

        // A ranged class must sense at least as far as it can shoot.
        if (c.rangedAttacker)
            detectionRange = Mathf.Max(detectionRange, attackRange + 0.5f);

        if (c.detectsTraps) canDetectTraps = true;

        healsAllies = c.healsAllies;
        healAmount = c.healAmount;
        healInterval = c.healInterval;
        healRadius = c.healRadius;
        healTimer = c.healInterval;   // first heal one interval in

        taunts = c.taunts;
        scoutRoomsRemaining = c.scoutRooms;

        // Resource pools/costs from the class; start full.
        maxStamina = c.maxStamina;
        maxMana = c.maxMana;
        attackCost = c.attackCost;
        attackUsesMana = c.attackUsesMana;
        healManaCost = c.healManaCost;
        currentStamina = maxStamina;
        currentMana = maxMana;
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        currentHP = maxHP;

        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        lootTable = GetComponent<LootTable>();
        animDriver = GetComponent<EntityAnimationDriver>();
        telegraph = GetComponent<AttackTelegraph>();
        deathsAtArrival = AdventurerDeaths;

        currentFloor = GetComponentInParent<FloorRoot>();
        if (currentFloor == null)
            Debug.LogWarning("[Adventurer] No FloorRoot in parent — multi-floor traversal will fail.");
        else
            currentFloor.Entities?.Register(this);
        EvaluateThresholdAtArrival();

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);
            statusBars.ConfigureResourceBars(maxStamina > 0f, maxMana > 0f);
            if (maxStamina > 0f) statusBars.SetStamina(currentStamina, maxStamina);
            if (maxMana > 0f) statusBars.SetMana(currentMana, maxMana);
            if (named && !string.IsNullOrEmpty(displayName)) statusBars.SetBossLabel(displayName);
        }

        UnlockState.OnChanged += HandleUnlockChanged;
        RefreshIntentBadge();

        // Every fresh party takes the shared muster window at the mouth:
        // formations hold their slots as before, formation-less parties mill
        // loosely, and the whole group advances together.
        if (pendingRestore != null)
            ApplyPendingRestore();
        else if (party != null && Time.time < party.OrganizeEndTime)
            BeginOrganizing();
        else
            BeginAdvance();
    }

    // Claim a formation slot and hold at the threshold until the party forms up.
    private void BeginOrganizing()
    {
        state = AdventurerState.Organizing;
        // Formations claim a slot; formation-less parties muster loosely
        // instead (slotless drift handled in HandleOrganizing).
        formationSlot = party != null && party.Formation != FormationType.None
            ? ComputeFormationSlot()
            : (Vector3?)null;
        musterTarget = transform.position;
        nextMusterStepTime = 0f;
    }

    // Set the post-organize advance state (observer tour / Explorer scout / core).
    private void BeginAdvance()
    {
        if (goal == AdventurerGoal.Delve)
        {
            searchRoomsRemaining = delveSearchRooms;
            state = AdventurerState.Hunting;
        }
        else if (goal == AdventurerGoal.ObserveRooms)
        {
            if (PickNextRoom()) state = AdventurerState.MovingToRoom;
            else { leftSatisfied = true; state = AdventurerState.Retreating; }
        }
        else if (scoutRoomsRemaining > 0 && PickRandomRoom())
            state = AdventurerState.MovingToRoom;
        else
            state = AdventurerState.MovingToCore;

        RefreshPath();
    }

    // -- Threshold latch ------------------------------------------------
    // An adventurer freezes a floor's spawner respawns and placement only
    // once it has stepped out of the entrance cave into the dungeon proper.
    // Floors below arrive by stairs and latch on arrival; legacy saves with
    // a player-placed entrance have no tunnel and latch at spawn.

    private bool crossedThreshold;
    private float thresholdCheckTimer;
    private const float THRESHOLD_CHECK_INTERVAL = 0.25f;

    /// <summary>True once this adventurer counts against the floor gate
    /// (FloorIntrusion). Latched; clears only by leaving the registry.</summary>
    public bool CountsAsIntruder => crossedThreshold;

    private void EvaluateThresholdAtArrival()
    {
        if (currentFloor == null) { crossedThreshold = true; return; }
        if (currentFloor.FloorIndex != 0) { crossedThreshold = true; return; }
        var features = currentFloor.FeatureGenerator;
        if (features == null || features.EntranceCave == null) { crossedThreshold = true; return; }
        var influence = currentFloor.TileInfluence;
        if (influence == null) { crossedThreshold = true; return; }
        crossedThreshold = !features.IsEntranceCave(influence.WorldToCell(transform.position));
    }

    private void TickThresholdLatch()
    {
        if (crossedThreshold) return;
        thresholdCheckTimer -= Time.deltaTime;
        if (thresholdCheckTimer > 0f) return;
        thresholdCheckTimer = THRESHOLD_CHECK_INTERVAL;

        var features = currentFloor != null ? currentFloor.FeatureGenerator : null;
        var influence = currentFloor != null ? currentFloor.TileInfluence : null;
        if (features == null || influence == null) { crossedThreshold = true; return; }
        if (!features.IsEntranceCave(influence.WorldToCell(transform.position)))
            crossedThreshold = true;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        TickThresholdLatch();
        UpdateTerrainSpeedMultiplier();
        TrySightCore();

        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f) slowMultiplier = 1f;
        }

        if (knockbackRemaining > 0f) { KnockbackStep(); return; }

        if (state == AdventurerState.UsingStairs)
        {
            HandleStairTraversal();
            return;
        }

        if (canDetectTraps) ScanForTraps();

        if (state != AdventurerState.Retreating && currentHP / maxHP < retreatThreshold)
            StartRetreat();

        // Commoners have no business down here. The first monster they lay eyes on turns them round --
        // unless they came to worship, in which case they hold their nerve and only harm turns them.
        if (type == AdventurerType.Commoner
            && goal != AdventurerGoal.WorshipCore
            && goal != AdventurerGoal.FreePrisoner
            && state != AdventurerState.Retreating
            && currentFloor?.Entities != null
            && currentFloor.Entities.AnyWithinRadius<DungeonMonster>(transform.position, commonerPanicRange))
        {
            StartRetreat();
        }

        ApplySeparation();

        if (healsAllies) TickHeal();
        if (taunts) TickTaunt();
        TickResources();
        TryBeginRescue();

        switch (state)
        {
            case AdventurerState.MovingToCore:
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
                {
                    // Only looters detour for chests/loot; everyone else beelines.
                    if (goal == AdventurerGoal.LootAndLeave)
                    {
                        ScanForChests();
                        ScanForLoot();
                    }
                    if (!MovementHalted) AdvanceOrHold();
                }
                break;

            case AdventurerState.MovingToChest:
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
                {
                    ScanForLoot();
                    if (!MovementHalted) MoveToChest();
                }
                break;

            case AdventurerState.Combat:
                HandleCombat();
                break;

            case AdventurerState.Retreating:
                ScanForLoot();
                FollowPath();
                break;

            case AdventurerState.Disarming:
                ScanForMonsters();
                if (state == AdventurerState.Disarming) HandleDisarming();
                break;

            case AdventurerState.Worshipping:
                HandleWorship();
                break;

            case AdventurerState.MovingToRoom:
                ScanForMonsters();   // non-combat goal; only a Cowardly observer flees
                if (goal == AdventurerGoal.Delve) { ScanForChests(); ScanForLoot(); }
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating
                    && !MovementHalted)
                    FollowPath();
                break;

            case AdventurerState.Hunting:
                HandleHunting();
                break;

            case AdventurerState.Observing:
                HandleObserving();
                break;

            case AdventurerState.Organizing:
                HandleOrganizing();
                break;

            case AdventurerState.Pinned:
                HandlePinned();
                break;

            case AdventurerState.MovingToRescue:
                if (rescueTargetAdv == null || !rescueTargetAdv.IsPinned) { ResumeFromPin(); break; }
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating
                    && !MovementHalted)
                    FollowPath();
                break;
        }
    }

    // ── Terrain Speed (DAY 31) ────────────────────────────────────

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;
        if (currentFloor == null) return;

        var features = currentFloor.FeatureGenerator;
        var influence = currentFloor.TileInfluence;
        if (features == null || influence == null) return;

        Vector3Int cell = influence.WorldToCell(transform.position);
        if (features.IsRiver(cell))
            terrainSpeedMultiplier = features.FordingSpeedMultiplier;
    }

    // ── Pathfinding ───────────────────────────────────────────────

    private void RefreshPath()
    {
        pathIndex = 0;
        stairTarget = null;

        if (currentFloor == null) { currentPath = new List<Vector3>(); return; }

        int myFloor = currentFloor.FloorIndex;
        int coreFloor = FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0;

        Vector3 goalPos;

        if (state == AdventurerState.Retreating)
        {
            if (myFloor == 0)
            {
                goalPos = DungeonEntrance.Instance != null
                    ? DungeonEntrance.Instance.SpawnPosition
                    : transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goalPos = upStair.transform.position;
            }
        }
        else if (state == AdventurerState.MovingToChest && chestTarget != null)
        {
            goalPos = chestTarget.transform.position;
        }
        else if (state == AdventurerState.MovingToRoom && roomTarget != null)
        {
            goalPos = roomTarget.transform.position;
        }
        else if (state == AdventurerState.MovingToRescue && rescueTargetAdv != null)
        {
            goalPos = rescueTargetAdv.transform.position;
        }
        else if (goal == AdventurerGoal.FreePrisoner && hasRaidTarget)
        {
            // Raiders make for the gaol, not the core -- across floors if need be.
            if (myFloor == raidTargetFloor) goalPos = raidTargetPos;
            else if (raidTargetFloor > myFloor)
            {
                var downStair = FindNearestStair(DungeonStairs.Direction.Down);
                if (downStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = downStair;
                goalPos = downStair.transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goalPos = upStair.transform.position;
            }
        }
        else
        {
            if (myFloor == coreFloor)
                goalPos = DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : transform.position;
            else if (coreFloor > myFloor)
            {
                var downStair = FindNearestStair(DungeonStairs.Direction.Down);
                if (downStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = downStair;
                goalPos = downStair.transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goalPos = upStair.transform.position;
            }
        }

        currentPath = DungeonPathfinder.FindPath(currentFloor, transform.position, goalPos);
    }

    public void ForceRefreshPath()
    {
        if (goal == AdventurerGoal.Delve)
        {
            if (state != AdventurerState.Retreating && state != AdventurerState.UsingStairs)
            {
                combatTarget = null;
                roomTarget = null;
                searchRoomsRemaining = delveSearchRooms;
                state = AdventurerState.Hunting;
            }
            RefreshPath();
            return;
        }
        if (state != AdventurerState.Retreating && state != AdventurerState.UsingStairs
            && state != AdventurerState.Worshipping)
            state = AdventurerState.MovingToCore;
        RefreshPath();
    }

    // -- Capture-trap pin & rescue --------------------------------

    /// <summary>Types the dungeon will never take alive (mirrors PrisonController's rule):
    /// a Hero by decree, the Inspector because the assessment must run its course, and the
    /// Suicidal because a death in the dark is the whole point of their coming.</summary>
    public bool CanBeSubdued =>
        type != AdventurerType.Hero
        && type != AdventurerType.Inspector
        && type != AdventurerType.Suicidal;

    public bool IsPinned => state == AdventurerState.Pinned;

    /// <summary>A capture-trap has closed on this one. The uncapturable take the trap's slow
    /// and walk on; the rest are pinned in place, and their party is told, so survivors can
    /// come back for them.</summary>
    public void BeginPinned(float holdSeconds, float missSlow, float missSlowDuration)
    {
        if (state == AdventurerState.Pinned || state == AdventurerState.Retreating) return;
        if (!CanBeSubdued)
        {
            ApplySlow(missSlow, missSlowDuration);
            return;
        }
        state = AdventurerState.Pinned;
        pinTimer = Mathf.Max(1f, holdSeconds);
        combatTarget = null;
        currentPath = new List<Vector3>();
        pathIndex = 0;
        party?.RegisterPinned(this);
        AlertsLog.Instance?.AddAlert(
            displayName + " is snared, thrashing in the trap.",
            transform.position, currentFloor != null ? currentFloor.FloorIndex : -1,
            AlertCategory.Trap);
    }

    private void HandlePinned()
    {
        pinTimer -= Time.deltaTime;
        bool aloneNow = party == null || party.LiveCount() <= 1;
        if (pinTimer > 0f && !aloneNow) return;
        SecureOrStruggle();
    }

    /// <summary>The pin has run its course with no rescue. The dungeon claims them into a
    /// free cell; if every cell is full they wrench loose and press on.</summary>
    private void SecureOrStruggle()
    {
        party?.ClearPinned(this);
        if (PrisonController.Instance != null
            && PrisonController.Instance.TryImprison(displayName, type, combatClass, className, named))
        {
            // Secured -- resolved like any capture, with no death reported.
            DropCarriedTribute("dropped the offering when they were taken");
            party?.OnMemberResolved(partyMember, false, false, CarriedLootValue);
            UnregisterFromFloor(currentFloor);
            DropCarriedLoot();
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
            return;
        }
        AlertsLog.Instance?.AddAlert(
            displayName + " tore free of the trap -- no cell left to hold them.",
            transform.position, currentFloor != null ? currentFloor.FloorIndex : -1,
            AlertCategory.Trap);
        ResumeFromPin();
    }

    private void ResumeFromPin()
    {
        state = goal == AdventurerGoal.Delve ? AdventurerState.Hunting : AdventurerState.MovingToCore;
        RefreshPath();
    }

    /// <summary>A rescuer reached them: wrenched loose, wounded by the ordeal, and now free.</summary>
    public void EndPinRescued(float woundedFraction)
    {
        if (state != AdventurerState.Pinned) return;
        party?.ClearPinned(this);
        currentHP = Mathf.Min(currentHP, Mathf.Max(1f, maxHP * woundedFraction));
        statusBars?.SetHP(currentHP, maxHP);
        ResumeFromPin();
    }

    /// <summary>Each free member weighs going back for a pinned ally. Those mid-fight, fleeing
    /// or on the stairs stay their course; a coward may write the ally off. The rest converge --
    /// and since a rescuer must reach the trap alive, the monsters guarding it are the answer.</summary>
    private void TryBeginRescue()
    {
        if (party == null || !IsRescueEligibleState()) return;
        var pinnedAlly = party.NearestPinned(this, transform.position);
        if (pinnedAlly == null) return;
        if (trait == BehaviourTrait.Cowardly && UnityEngine.Random.value < cowardAbandonChance) return;
        rescueTargetAdv = pinnedAlly;
        state = AdventurerState.MovingToRescue;
        RefreshPath();
    }

    private bool IsRescueEligibleState() =>
        state == AdventurerState.MovingToCore
        || state == AdventurerState.MovingToChest
        || state == AdventurerState.MovingToRoom
        || state == AdventurerState.Hunting
        || state == AdventurerState.Observing;

    private void ArriveAtRescue()
    {
        var freed = rescueTargetAdv;
        rescueTargetAdv = null;
        if (freed != null && freed.IsPinned)
        {
            freed.EndPinRescued(rescuedHpFraction);
            party?.MarkGrudge();
            AlertsLog.Instance?.AddAlert(
                "They cut their comrade loose. This dungeon has made an enemy.",
                transform.position, currentFloor != null ? currentFloor.FloorIndex : -1,
                AlertCategory.Combat);
        }
        ResumeFromPin();
    }

    // -- Prison raid (rescue party / ransom-bearer) ---------------

    /// <summary>Mark this adventurer as a raider making for a held captive's cell. A
    /// ransom-bearer pays the core on arrival; a rescue party frees for nothing. The
    /// muster-then-advance flow and RefreshPath do the rest.</summary>
    public void SetRaidTarget(Vector3 cellPos, int cellFloor, bool ransom, int ransomAmount)
    {
        goal = AdventurerGoal.FreePrisoner;
        raidTargetPos = cellPos;
        raidTargetFloor = cellFloor;
        hasRaidTarget = true;
        isRansomBearer = ransom;
        ransomValue = ransomAmount;
    }

    /// <summary>Reached the gaol. Free the nearest held captive; a ransom-bearer pays the
    /// core its gold for the privilege. Either way, turn for the exit and fight out. If the
    /// captive is already gone (freed by another, or spent by the player), just leave.</summary>
    private void AttemptBreach()
    {
        bool freed = PrisonController.Instance != null
            && PrisonController.Instance.BreachNearest(transform.position, 1.75f);
        if (freed && isRansomBearer) DungeonCore.Instance?.AddGold(ransomValue);
        hasRaidTarget = false;
        if (state != AdventurerState.Retreating) StartRetreat();
    }

    // -- Formation march (tactical layer): hold the body together on the advance ----

    /// <summary>A formationed party advances as a body: the lead paths to the core and sets the
    /// pace (pausing when a follower lags), while the others hold their class-ranked slots
    /// relative to it. Formation-less parties, and members whose party has no formation, just
    /// beeline as before.</summary>
    private void AdvanceOrHold()
    {
        if (party == null || party.Formation == FormationType.None) { FollowPath(); return; }
        if (party.FormationBroken) { holdingFormation = false; FollowPath(); return; }
        var lead = party.CurrentLead();
        if (lead == this)
        {
            // The anchor sets the pace: hold position while the body is stretched.
            if (party.HasStraggler()) return;
            FollowPath();
            return;
        }
        FollowFormation(lead);
    }

    /// <summary>Steer toward this member's slot relative to the lead. In open ground the slot
    /// holds the formation shape; where the slot falls in rock (a tight corridor) the follower
    /// collapses toward the lead and files in behind it. A follower that falls right out of the
    /// body paths back in individually, then resumes holding once close again.</summary>
    private void FollowFormation(DungeonAdventurer lead)
    {
        formationStraggling = false;
        if (lead == null) { FollowPath(); return; }

        Vector2 fwd = lead.FacingDir;
        if (fwd.sqrMagnitude < 0.01f) fwd = party.AdvanceDir;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector2.right;
        Vector2 side = new Vector2(-fwd.y, fwd.x);
        ComputeSlotOffset(out float forward, out float lateral);
        Vector3 slot = lead.transform.position + (Vector3)(fwd * forward + side * lateral);
        float distToLead = Vector2.Distance(transform.position, lead.transform.position);

        if (holdingFormation)
        {
            if (distToLead > formationLostRange)
            {
                holdingFormation = false;
                RefreshPath();
                FollowPath();
                return;
            }
            bool walkable = SlotWalkable(slot);
            Vector3 target = walkable ? slot : lead.transform.position;
            if (walkable && Vector2.Distance(transform.position, slot) > formationStragglerSlack)
                formationStraggling = true;
            transform.position = Vector2.MoveTowards(transform.position, target, EffectiveMoveSpeed * Time.deltaTime);
            CheckTrapAtCurrentCell();
        }
        else
        {
            if (distToLead <= formationRejoinRange) { holdingFormation = true; return; }
            FollowPath();
        }
    }

    /// <summary>Is a formation slot on ground a follower can stand on? Mined tile or entrance
    /// cave counts; raw rock does not (so the follower files in behind the lead instead).</summary>
    private bool SlotWalkable(Vector3 world)
    {
        if (currentFloor == null || currentFloor.TileInfluence == null) return true;
        var cell = currentFloor.TileInfluence.WorldToCell(world);
        if (currentFloor.TileInfluence.IsTileMined(cell)) return true;
        return currentFloor.FeatureGenerator != null && currentFloor.FeatureGenerator.IsEntranceCave(cell);
    }

    // -- Shield wall + tank taunt (formation effect) --------------

    /// <summary>Is this a rear member currently shielded by the front rank? Front-rankers take
    /// their own blows; a rear member is spared only in a formationed party, while not fleeing,
    /// and while a front-ranker still stands. (3c will drop this when the body is scattered.)</summary>
    private bool WallProtected()
    {
        return state != AdventurerState.Retreating
            && party != null
            && party.Formation != FormationType.None
            && !party.FormationBroken
            && !IsFrontRank
            && party.HasLivingFrontRank();
    }

    /// <summary>A scatter trap or breaker monster has struck: disrupt this member's whole party
    /// formation. Members disperse and the shield wall drops until it re-forms.</summary>
    public void BreakFormation(float seconds) => party?.BreakFormation(seconds);

    /// <summary>The tank's taunt: in a fight, spend stamina to shout the monsters onto itself for
    /// a spell. Regen and a short recovery pace it; a heavy hit from an ally peels a monster back
    /// off (see DealAttackDamage).</summary>
    private void TickTaunt()
    {
        if (tauntTimer > 0f) { tauntTimer -= Time.deltaTime; return; }
        if (state != AdventurerState.Combat) return;
        if (Time.time < tauntCooldownEnd) return;
        if (currentStamina < tauntStaminaCost) return;

        currentStamina -= tauntStaminaCost;
        statusBars?.SetStamina(currentStamina, maxStamina);
        tauntTimer = tauntDuration;
        tauntCooldownEnd = Time.time + tauntDuration + tauntRecovery;
        BarkSpawner.Spawn(transform.position, TauntLines[UnityEngine.Random.Range(0, TauntLines.Length)], BanterLines.Reaction);
    }

    private static readonly string[] TauntLines =
    {
        "Face me!",
        "Eyes on me, filth!",
        "Come and try, then!",
        "I am your wall!",
    };

    private void FollowPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            if (stairTarget != null)
            {
                if (Vector2.Distance(transform.position, stairTarget.transform.position) < 0.6f)
                    BeginStairTraversal();
                return;
            }
            OnReachedDestination();
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];

        if (canDetectTraps && state != AdventurerState.Retreating && TryBeginDisarm(waypoint))
            return;

        Vector2 heading = (Vector2)(waypoint - transform.position);
        if (heading.sqrMagnitude > 0.0001f) FacingDir = heading.normalized;

        transform.position = Vector2.MoveTowards(
            transform.position, waypoint, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, waypoint) < 0.08f)
        {
            pathIndex++;
            CheckTrapAtCurrentCell();
        }
    }

    private void OnReachedDestination()
    {
        if (state == AdventurerState.MovingToRescue) { ArriveAtRescue(); return; }

        if (goal == AdventurerGoal.FreePrisoner && state != AdventurerState.Retreating) { AttemptBreach(); return; }

        if (state == AdventurerState.MovingToRoom)
        {
            if (goal == AdventurerGoal.Delve)
            {
                // A searching delver checks the room off and resumes the hunt.
                if (roomTarget != null) visitedRooms.Add(roomTarget);
                roomTarget = null;
                state = AdventurerState.Hunting;
                return;
            }
            BeginObserving();
            return;
        }

        if (state == AdventurerState.Retreating)
        {
            int carried = CarriedLootValue;   // capture before the haul is destroyed

            foreach (var loot in carriedLoot)
                if (loot != null) Destroy(loot.gameObject);
            carriedLoot.Clear();

            // A Pilgrim that finished worshipping leaves peacefully and calms the
            // dungeon's Notoriety — once per party, regardless of pilgrim count.
            if (intent == PartyIntent.Pilgrim && worshipCompleted)
            {
                if (party != null && !party.exitBonusApplied)
                {
                    party.exitBonusApplied = true;
                    DungeonCore.Instance?.AddNotoriety(-pilgrimNotorietyReduction);
                    DungeonCore.Instance?.AddXP(pilgrimXpReward);
                    party.notorietyDelta -= pilgrimNotorietyReduction;
                    FactionSystem.Instance?.RegisterPilgrimage();
                    AlignmentSystem.Instance?.OnPilgrimage();
                    Debug.Log($"[Adventurer] Pilgrimage complete — Notoriety -{pilgrimNotorietyReduction:0}, +{pilgrimXpReward:0} XP.");
                }
            }
            else if (intent == PartyIntent.GiftGiver)
            {
                // Tribute bearers raise the dungeon's desirability on a peaceful exit.
                DungeonCore.Instance?.AddReputation(2f);
                FactionSystem.Instance?.RegisterTribute();
                AlignmentSystem.Instance?.OnTribute();
            }
            else if (goal == AdventurerGoal.LootAndLeave && carried > 0)
            {
                // Word of rich pickings spreads — a thief escaping WITH a haul is
                // the best advertisement a dungeon can buy. Empty-handed, nobody
                // talks.
                DungeonCore.Instance?.AddReputation(2f);
            }

            // A looter that escapes with a haul leaves satisfied, calming the dungeon
            // in proportion to what it carried out (capped).
            if (carried > 0)
            {
                float drop = Mathf.Min(lootSatisfactionCap, carried * lootSatisfactionFactor);
                DungeonCore.Instance?.AddNotoriety(-drop);
                if (party != null) party.notorietyDelta -= drop;
            }

            party?.OnMemberResolved(partyMember, true, false, carried);
            AlignmentSystem.Instance?.OnAdventurerLeftAlive(carried);
            MercenaryContract.Instance?.RegisterLootExit(carried);
            // A noble driven out in flight (not one leaving of its own accord) triggers the family reprisal.
            if (type == AdventurerType.Noble && !leftSatisfied)
                NobleRetaliation.Instance?.RegisterNobleFall(displayName);

            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
        else
        {
            if (DungeonCore.Instance != null &&
                Vector2.Distance(transform.position, DungeonCore.Instance.transform.position) > 1.5f)
            {
                Debug.LogWarning("[Adventurer] OnReachedDestination called far from core — refreshing path.");
                RefreshPath();
                return;
            }

            // Worshippers (Pilgrim, Cultist) pray at the core, then depart.
            if (goal == AdventurerGoal.WorshipCore)
            {
                BeginWorship();
                return;
            }

            // A looter that reached the core empty-handed leaves — it never breaches.
            if (goal == AdventurerGoal.LootAndLeave)
            {
                StartRetreat();
                return;
            }

            // Only those who CAME to break the stone break it. Worshippers pray
            // (handled above) and looters withdraw (handled above); observers and
            // delvers have seen what they came for and leave. The death-seeking
            // never retreat -- they stop here and fight whatever finds them.
            if (goal != AdventurerGoal.BreachCore)
            {
                // The death-seeking came to end here, not to break the stone.
                // They kneel and the core burns them down; everyone else has seen
                // what they came for and leaves.
                if (goal == AdventurerGoal.SeekDeath) BeginWorship();
                else StartRetreat();
                return;
            }

            Debug.Log("[Adventurer] Reached Core Room — core breach!");
            DungeonCore.Instance?.DestroyCore();
            party?.OnMemberResolved(partyMember, false, true, CarriedLootValue);
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
    }

    // ── Stair Traversal ───────────────────────────────────────────

    private void BeginStairTraversal()
    {
        stateBeforeStairs = state;
        state = AdventurerState.UsingStairs;
        stairTimer = stairTraversalDuration;
        transform.position = stairTarget.transform.position;
        Debug.Log($"[Adventurer] Using stairs: floor {currentFloor.FloorIndex} → {stairTarget.LinkedFloorIndex}");
    }

    private void HandleStairTraversal()
    {
        if (stairTarget == null) { state = stateBeforeStairs; RefreshPath(); return; }

        stairTimer -= Time.deltaTime;
        if (stairTimer > 0f) return;

        int destIdx = stairTarget.LinkedFloorIndex;
        var destFloor = FloorManager.Instance?.GetFloor(destIdx);

        if (destFloor == null)
        {
            Debug.LogWarning($"[Adventurer] Destination floor {destIdx} doesn't exist.");
            state = stateBeforeStairs; stairTarget = null; RefreshPath();
            return;
        }

        var matchingStair = FindStairOnFloor(destIdx, stairTarget.OccupiedCell);
        if (matchingStair == null)
        {
            Debug.LogWarning($"[Adventurer] No matching stair on floor {destIdx} at {stairTarget.OccupiedCell}.");
            state = stateBeforeStairs; stairTarget = null; RefreshPath();
            return;
        }
        UnregisterFromFloor(currentFloor);
        transform.SetParent(destFloor.transform, true);
        transform.position = matchingStair.transform.position;
        currentFloor = destFloor;
        currentFloor.Entities?.Register(this);
        crossedThreshold = true;   // arrived by stairs -- already inside

        Debug.Log($"[Adventurer] Arrived on floor {destIdx}.");
        state = stateBeforeStairs; stairTarget = null; RefreshPath();
    }

    // Leaves a floor's registry and, if that floor now holds no adventurers,
    // re-arms its traps. The scene.isLoaded guard skips teardown, where the
    // registry and traps may already be gone.
    private void UnregisterFromFloor(FloorRoot floor)
    {
        if (floor == null) return;
        floor.Entities?.Unregister(this);
        if (!gameObject.scene.isLoaded) return;
        if (floor.Entities != null && floor.Entities.Count<DungeonAdventurer>() == 0)
            floor.TrapRegistry?.ResetAllTraps();
    }

    private DungeonStairs FindNearestStair(DungeonStairs.Direction dir)
    {
        if (currentFloor?.Entities == null) return null;
        return currentFloor.Entities.Nearest<DungeonStairs>(
            transform.position, float.MaxValue,
            s => s.Dir == dir);
    }

    private DungeonStairs FindStairOnFloor(int floorIndex, Vector3Int cell)
    {
        var floor = FloorManager.Instance?.GetFloor(floorIndex);
        return floor?.Entities?.GetAtCell<DungeonStairs>(cell);
    }

    // ── Separation ────────────────────────────────────────────────

    // Reused buffer — avoids per-frame allocations for the separation scan.
    private static readonly List<DungeonAdventurer> _separationBuf = new();

    private void ApplySeparation()
    {
        if (currentFloor?.Entities == null) return;
        currentFloor.Entities.FillAll(_separationBuf);

        Vector2 push = Vector2.zero;
        for (int i = 0; i < _separationBuf.Count; i++)
        {
            var other = _separationBuf[i];
            if (other == this) continue;

            Vector2 delta = (Vector2)transform.position - (Vector2)other.transform.position;
            float dist = delta.magnitude;
            if (dist < separationRadius && dist > 0.001f)
                push += delta.normalized * (separationRadius - dist);
        }

        if (push != Vector2.zero)
            transform.position += (Vector3)(push * separationStrength * Time.deltaTime);
    }

    // ── Monster Detection ─────────────────────────────────────────

    /// <summary>The nearest hostile-faction adventurer this one will brawl with inside the
    /// dungeon (Hero vs Cultist only, for now), or null.</summary>
    private DungeonAdventurer FindHostileAdventurer()
    {
        if (currentFloor?.Entities == null) return null;
        return currentFloor.Entities.Nearest<DungeonAdventurer>(
            transform.position, detectionRange,
            a => a != this && ((IMonsterTarget)a).IsAlive
                 && FactionRelations.EngagesInsideDungeon(type, a.Type));
    }

    private void ScanForMonsters()
    {
        // Faction brawl: a Hero and a Cultist are hostile and turn on each other on sight,
        // whatever else they were doing (a Cultist mid-tribute still defends itself). This is
        // the only in-dungeon faction fight for now; the rest waits for the procgen forest.
        if (currentFloor?.Entities != null && FactionRelations.IsDungeonBrawler(type))
        {
            var foe = FindHostileAdventurer();
            if (foe != null)
            {
                combatTarget = foe;
                chestTarget = null;
                state = AdventurerState.Combat;
                TryChargeBark();
                return;
            }
        }
        // Non-combat goals never initiate combat — but a Cowardly observer still
        // flees the moment it sees a monster (e.g. the Noble bolting for the exit).
        if (goal == AdventurerGoal.WorshipCore || goal == AdventurerGoal.ObserveRooms)
        {
            // Worship-bound pilgrims hold their nerve at the sight of monsters;
            // only harm turns them back. A Cowardly observer still bolts.
            if (goal == AdventurerGoal.ObserveRooms
                && trait == BehaviourTrait.Cowardly && currentFloor?.Entities != null
                && currentFloor.Entities.AnyWithinRadius<DungeonMonster>(transform.position, detectionRange))
                StartRetreat();
            return;
        }

        // Passive parties (an Inspector and its escort) do not initiate; only a
        // blow turns them, handled in TakeDamage.
        if (passiveUnlessProvoked) return;

        if (currentFloor?.Entities == null) return;
        DungeonMonster nearest = null;
        if (!string.IsNullOrEmpty(returnGrudge))
            nearest = currentFloor.Entities.Nearest<DungeonMonster>(
                transform.position, detectionRange, m => m.TypeName == returnGrudge);
        if (nearest == null)
            nearest = currentFloor.Entities.Nearest<DungeonMonster>(transform.position, detectionRange);

        if (nearest == null) return;

        // Suicidal welcomes death and never flees; anyone else Cowardly retreats.
        if (trait == BehaviourTrait.Cowardly && goal != AdventurerGoal.SeekDeath) { StartRetreat(); return; }

        combatTarget = nearest;
        chestTarget = null;
        state = AdventurerState.Combat;
        TryChargeBark();
    }

    // ── Chest Detection ───────────────────────────────────────────

    private void ScanForChests()
    {
        if (currentFloor?.Entities == null) return;
        var nearest = currentFloor.Entities.Nearest<DungeonChest>(
            transform.position, chestDetectionRange,
            c => !c.IsOpened && !visitedChests.Contains(c));

        if (nearest != null && nearest != chestTarget)
        {
            chestTarget = nearest;
            state = AdventurerState.MovingToChest;
            RefreshPath();
        }
    }

    private void MoveToChest()
    {
        if (chestTarget == null || chestTarget.IsOpened)
        {
            if (chestTarget != null) visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
            return;
        }

        if (pathIndex < currentPath.Count)
        {
            Vector3 waypoint = currentPath[pathIndex];
            transform.position = Vector2.MoveTowards(
                transform.position, waypoint, EffectiveMoveSpeed * Time.deltaTime);
            if (Vector2.Distance(transform.position, waypoint) < 0.08f)
                pathIndex++;
            return;
        }

        float dist = Vector2.Distance(transform.position, chestTarget.transform.position);
        if (dist <= chestTarget.InteractRadius)
        {
            chestTarget.Interact(this);
            visitedChests.Add(chestTarget);
            chestTarget = null;
            // Treasure Hunters leave with their prize rather than pressing on to the core.
            if (goal == AdventurerGoal.LootAndLeave) { StartRetreat(); return; }
            state = AdventurerState.MovingToCore;
            RefreshPath();
        }
        else
        {
            Debug.LogWarning("[Adventurer] Could not reach chest — resuming.");
            visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
        }
    }

    // ── Loot ─────────────────────────────────────────────────────

    private void ScanForLoot()
    {
        var all = FindObjectsByType<CarriableLoot>(FindObjectsInactive.Exclude);
        foreach (var loot in all)
        {
            if (carriedLoot.Contains(loot)) continue;
            if (Vector2.Distance(transform.position, loot.transform.position) < pickupRadius)
                PickUpLoot(loot);
        }
    }

    private void PickUpLoot(CarriableLoot loot)
    {
        carriedLoot.Add(loot);
        loot.transform.SetParent(transform);
        loot.PickUp();

        // Treasure Hunters grab and go — leave the moment they're carrying loot.
        if (goal == AdventurerGoal.LootAndLeave && state != AdventurerState.Retreating)
            StartRetreat();
    }

    // ── Delving ───────────────────────────────

    private void HandleHunting()
    {
        // Delvers loot what they pass. They do not cross the dungeon for a chest
        // the way a Treasure Hunter does -- chestDetectionRange keeps the detour
        // short -- but walking by an open strongbox and ignoring it read as broken.
        ScanForChests();
        if (state != AdventurerState.Hunting) return;
        ScanForLoot();
        if (state != AdventurerState.Hunting) return;

        // Generous backstop so a delver in a harmless dungeon still eventually leaves.
        if (delveKillCap > 0 && delveKills >= delveKillCap) { StartRetreat(); return; }

        // Hunt the nearest monster anywhere on the floor; Combat does the chase + kill.
        DungeonMonster prey = currentFloor?.Entities != null
            ? currentFloor.Entities.Nearest<DungeonMonster>(transform.position, huntRange)
            : null;

        if (prey != null)
        {
            if (trait == BehaviourTrait.Cowardly) { StartRetreat(); return; }
            searchRoomsRemaining = delveSearchRooms;   // the floor isn't empty - refill the search budget
            combatTarget = prey;
            chestTarget = null;
            state = AdventurerState.Combat;
            TryChargeBark();
            return;
        }

        // Nothing to hunt - wander the dungeon a while, then leave satisfied.
        if (searchRoomsRemaining <= 0 || !PickRandomRoom()) { StartRetreat(); return; }
        searchRoomsRemaining--;
        state = AdventurerState.MovingToRoom;
        RefreshPath();
    }

    // Delvers resume the hunt after each kill; everyone else presses on to the core.
    private AdventurerState PostCombatState()
        => goal == AdventurerGoal.Delve ? AdventurerState.Hunting : AdventurerState.MovingToCore;

    // ── Combat ───────────────────────────────────

    private void HandleCombat()
    {
        if (combatTarget == null || !combatTarget.IsAlive)
        {
            telegraph?.Cancel();
            if (goal == AdventurerGoal.Delve && combatTarget is DungeonMonster) delveKills++;
            combatTarget = null;
            state = PostCombatState();
            RefreshPath();
            return;
        }

        float dist = Vector2.Distance(transform.position, combatTarget.Transform.position);
        // A ranged class also needs a clear line to the target: blocked within range,
        // it falls through to the approach path below and walks until the shot opens.
        if (dist > attackRange
            || (RangedClass && !DungeonProjectile.HasLineOfSight(
                    currentFloor, transform.position, combatTarget.Transform.position)))
        {
            telegraph?.Cancel();   // target stepped out of range mid-windup — abort the tell

            // Pathfind to the target instead of beelining, so the approach routes
            // around walls and overhangs. Refresh on a timer since the target moves.
            Vector3 targetPos = combatTarget.Transform.position;
            combatPathRefreshTimer -= Time.deltaTime;
            bool needsRefresh = combatPath.Count == 0
                             || combatPathIndex >= combatPath.Count
                             || combatPathRefreshTimer <= 0f;
            if (needsRefresh)
            {
                combatPath = DungeonPathfinder.FindPath(currentFloor, transform.position, targetPos);
                combatPathIndex = 0;
                combatPathRefreshTimer = CombatPathRefreshInterval;
            }

            // Unreachable — drop combat and resume the invasion (or the hunt).
            if (combatPath.Count == 0)
            {
                combatTarget = null;
                state = PostCombatState();
                RefreshPath();
                return;
            }

            Vector3 stepTarget = combatPath[combatPathIndex];
            transform.position = Vector2.MoveTowards(
                transform.position, stepTarget,
                EffectiveMoveSpeed * Time.deltaTime);
            if (Vector2.Distance(transform.position, stepTarget) < 0.08f)
                combatPathIndex++;
            return;
        }
        combatPath.Clear();

        if (telegraph != null && telegraph.IsWinding) return;   // charging — the strike fires on completion

        if (Time.time - lastAttackTime < attackCooldown / roomSlowMultiplier) return;

        // Pay the attack's resource cost; if the pool is dry, skip this
        // cycle (cooldown stays elapsed, so it fires the instant regen catches up).
        if (!SpendAttackCost()) return;
        lastAttackTime = Time.time;

        // Ranged classes loose a projectile at the end of the windup; melee lands as before.
        System.Action strike = RangedClass ? FireProjectile : (System.Action)DealAttackDamage;
        if (telegraph != null && telegraphSeconds > 0f)
            telegraph.Begin(telegraphSeconds, TelegraphColors.ForClass(combatClass), strike);
        else
            strike();
    }

    /// <summary>True for a class that fires projectiles (the Mage overlay flag).</summary>
    private bool RangedClass => classDef != null && classDef.rangedAttacker;

    private const float ClassProjectileSpeed = 8f;

    /// <summary>A ranged class's strike: loose a bolt that lands the payload on
    /// impact. Peel and party XP ride the projectile so they resolve when the hit
    /// actually connects, and a shooter that falls mid-flight still lands its shot.</summary>
    private void FireProjectile()
    {
        if (combatTarget == null || !combatTarget.IsAlive) return;
        animDriver?.OnAttack();
        bool tauntingAtFire = IsTaunting;

        var payload = new DungeonProjectile.Payload
        {
            damage = attackDamage,
            numberType = FloatingDamageNumber.DamageType.MonsterHit,
            knockbackForce = knockbackForce,
            knockbackMinDamage = knockbackMinDamage,
            onHit = hit =>
            {
                // A heavy hit from anyone but the taunting tank peels that monster
                // off the taunt -- judged by the shooter's stance at fire time.
                if (!tauntingAtFire && hit is DungeonMonster struck
                    && attackDamage >= struck.MaxHP * peelDamageFraction)
                    struck.PeelFromTaunt();
            },
            onKill = fallen =>
            {
                if (party != null && party.tracked && partyMember != null)
                    partyMember.xp += Mathf.RoundToInt(xpPerKill);
            },
        };
        DungeonProjectile.Fire(currentFloor, transform.position, combatTarget,
            ClassProjectileSpeed, TelegraphColors.ForClass(combatClass), null, payload);
    }

    private void DealAttackDamage()
    {
        if (combatTarget == null || !combatTarget.IsAlive) return;

        DamageNumberSpawner.Spawn(attackDamage, combatTarget.Transform.position,
            FloatingDamageNumber.DamageType.MonsterHit);
        animDriver?.OnAttack();
        combatTarget.TakeDamage(attackDamage);
        // A heavy hit from anyone but the taunting tank peels that monster off the taunt.
        if (!IsTaunting && combatTarget is DungeonMonster hitMonster
            && attackDamage >= hitMonster.MaxHP * peelDamageFraction)
            hitMonster.PeelFromTaunt();
        if (party != null && party.tracked && partyMember != null && !combatTarget.IsAlive)
            partyMember.xp += Mathf.RoundToInt(xpPerKill);
        if (knockbackForce > 0f && attackDamage >= knockbackMinDamage)
            combatTarget.ApplyKnockback(transform.position, knockbackForce);
    }

    /// <summary>Flat level (1..God 1) from a hero's cumulative kill XP. Excess XP beyond the
    /// cap is kept in the total but does not raise the level further.</summary>
    private int LevelFromXp(int xp)
        => Mathf.Clamp(1 + Mathf.FloorToInt(xp / Mathf.Max(1f, xpPerLevel)), 1, LevelTierUtil.MaxFlatLevel);

    /// <summary>Apply a returning hero's level as flat stat multipliers. Only ever called at
    /// spawn (never mid-delve), so a hero's stats never change while it is in the dungeon.</summary>
    private void ApplyLevelBoost(int level)
    {
        if (level <= 1) return;
        int steps = level - 1;
        maxHP *= 1f + hpPerLevel * steps;
        attackDamage *= 1f + damagePerLevel * steps;
        currentHP = maxHP;
    }

    private bool gradeLeveled;
    /// <summary>Scale a fresh matched-team member to the dungeon's assessed grade. Applied
    /// once, post-spawn, on top of the (no-op) base level.</summary>
    public void ApplyGradeLevel(int level)
    {
        if (gradeLeveled || level <= 1) return;
        gradeLeveled = true;
        ApplyLevelBoost(level);
    }

    /// <summary>Party morale broke — this member turns and flees, unless already
    /// retreating. Heroes and the Suicidal are filtered out by the caller.</summary>
    public void ForceRetreat()
    {
        if (state == AdventurerState.Retreating) return;
        StartRetreat();
    }

    private void StartRetreat()
    {
        // A snared adventurer cannot flee -- they are held until freed or taken.
        if (state == AdventurerState.Pinned) return;
        // A fleeing bearer panic-drops the offering where they turn tail —
        // the god is paid either way.
        DropCarriedTribute("panic-dropped");

        state = AdventurerState.Retreating;
        combatTarget = null;
        chestTarget = null;
        RefreshPath();
    }

    // ── Worship (Day 35, Pilgrim intent) ──────────────────────────

    private void BeginWorship()
    {
        state = AdventurerState.Worshipping;
        worshipTimer = worshipDuration;
        combatTarget = null;
        chestTarget = null;

        // The offering is laid at the god's feet before the prayer begins.
        DropCarriedTribute("laid at the core");

        Debug.Log("[Adventurer] Pilgrim worshipping at the core.");
    }

    /// <summary>Marks this adventurer as the party's tribute bearer (called by the
    /// spawner). The chest rides visibly on the bearer until it is dropped — at
    /// the core on arrival, where they fall, or where they turn tail.</summary>
    public void AssignTribute(int value, TributeChest prefab, float absorbDelay, float scatter, Sprite chestSprite)
    {
        tributeValue = value;
        tributePrefab = prefab;
        tributeAbsorbDelay = absorbDelay;
        tributeScatter = scatter;

        if (chestSprite != null && tributeVisual == null)
        {
            // Grab the bearer's own renderer BEFORE creating the child, so the
            // lookup can't find the chest sprite it is about to create.
            var own = GetComponentInChildren<SpriteRenderer>();

            tributeVisual = new GameObject("TributeCarry");
            tributeVisual.transform.SetParent(transform, false);
            tributeVisual.transform.localPosition = new Vector3(0.3f, 0.35f, 0f);
            tributeVisual.transform.localScale = Vector3.one * 0.6f;
            var sr = tributeVisual.AddComponent<SpriteRenderer>();
            sr.sprite = chestSprite;
            if (own != null)
            {
                sr.sortingLayerID = own.sortingLayerID;
                sr.sortingOrder = own.sortingOrder + 1;
            }
        }
    }

    /// <summary>Drops the carried tribute chest where the bearer stands — the core
    /// on delivery, the death cell if they fall, or the spot they turned tail.
    /// The chest absorbs into the core's gold as always.</summary>
    private void DropCarriedTribute(string reason)
    {
        if (!CarryingTribute) return;

        Vector2 scatter = Random.insideUnitCircle * tributeScatter;
        Vector3 pos = transform.position + new Vector3(scatter.x, scatter.y, 0f);
        var chest = Instantiate(tributePrefab, pos, Quaternion.identity);
        chest.transform.SetParent(transform.parent, true);
        chest.Initialise(tributeValue, tributeAbsorbDelay);

        Debug.Log($"[Adventurer] Tribute ({tributeValue}g) {reason}.");

        tributeValue = 0;
        tributePrefab = null;
        if (tributeVisual != null) { Destroy(tributeVisual); tributeVisual = null; }
    }

    private void HandleWorship()
    {
        // A death-seeker kneeling at the core is unmade by its presence -- it
        // never rises or retreats. Pilgrims and cultists pray unharmed.
        if (goal == AdventurerGoal.SeekDeath)
        {
            if (TakeDamage(coreRoomBurnPerSecond * Time.deltaTime)) return;
            return;
        }

        worshipTimer -= Time.deltaTime;
        if (worshipTimer > 0f) return;

        worshipCompleted = true;
        StartRetreat();
    }

    // ── Observe (Day 37 — Scholar / Inspector / Noble) ────────────

    private bool PickNextRoom()
    {
        roomTarget = null;
        if (currentFloor?.Entities == null) return false;
        roomTarget = currentFloor.Entities.Nearest<RoomAnchor>(
            transform.position, float.MaxValue,
            r => r != null && r.GetRoomTiles() != null && !visitedRooms.Contains(r));
        return roomTarget != null;
    }

    // Day 39 — Explorer scouts a RANDOM validated room (vs the observer's nearest).
    private bool PickRandomRoom()
    {
        roomTarget = null;
        if (currentFloor?.Entities == null) return false;
        var rooms = currentFloor.Entities.GetAll<RoomAnchor>();
        RoomAnchor pick = null;
        int seen = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r == null || r.GetRoomTiles() == null || visitedRooms.Contains(r)) continue;
            seen++;
            if (Random.Range(0, seen) == 0) pick = r;   // one-pass reservoir sample
        }
        roomTarget = pick;
        return roomTarget != null;
    }

    // ── Cleric (Day 39) ───────────────────────────────────────────

    private void TickHeal()
    {
        healTimer -= Time.deltaTime;
        if (healTimer > 0f) return;
        healTimer = healInterval;
        if (currentFloor?.Entities == null) return;

        // Heal the most-wounded ally in range (excluding self).
        currentFloor.Entities.WithinRadius(transform.position, healRadius, _healBuf);
        DungeonAdventurer best = null;
        float bestRatio = 1f;
        for (int i = 0; i < _healBuf.Count; i++)
        {
            var a = _healBuf[i];
            if (a == this || a == null) continue;
            float ratio = a.MaxHP > 0f ? a.CurrentHP / a.MaxHP : 1f;
            if (ratio < bestRatio) { bestRatio = ratio; best = a; }
        }
        if (best == null || bestRatio >= 1f) return;

        // A heal costs mana; a dry Cleric can't heal until it regens.
        if (healManaCost > 0f)
        {
            if (currentMana < healManaCost) return;
            currentMana -= healManaCost;
            statusBars?.SetMana(currentMana, maxMana);
        }
        best.Heal(healAmount);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHP >= maxHP) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        statusBars?.SetHP(currentHP, maxHP);
        DamageNumberSpawner.Spawn(amount, transform.position, FloatingDamageNumber.DamageType.Heal);
    }

    // ── Resources: stamina / mana ───────────────────────

    private void TickResources()
    {
        bool inCombat = state == AdventurerState.Combat;

        if (maxStamina > 0f && currentStamina < maxStamina)
        {
            float r = inCombat ? staminaRegenInCombat : staminaRegenOutOfCombat;
            currentStamina = Mathf.Min(maxStamina, currentStamina + r * Time.deltaTime);
            statusBars?.SetStamina(currentStamina, maxStamina);
        }
        if (maxMana > 0f && currentMana < maxMana)
        {
            float r = inCombat ? manaRegenInCombat : manaRegenOutOfCombat;
            currentMana = Mathf.Min(maxMana, currentMana + r * Time.deltaTime);
            statusBars?.SetMana(currentMana, maxMana);
        }
    }

    private bool SpendAttackCost()
    {
        if (attackCost <= 0f) return true;
        if (attackUsesMana)
        {
            if (currentMana < attackCost) return false;
            currentMana -= attackCost;
            statusBars?.SetMana(currentMana, maxMana);
            return true;
        }
        if (currentStamina < attackCost) return false;
        currentStamina -= attackCost;
        statusBars?.SetStamina(currentStamina, maxStamina);
        return true;
    }

    private void BeginObserving()
    {
        state = AdventurerState.Observing;
        observeTimer = observeDwellDuration;
        combatTarget = null;
        chestTarget = null;
        if (roomTarget != null) visitedRooms.Add(roomTarget);
        roomsObserved++;
        Debug.Log($"[Adventurer] {className} observing a room ({roomsObserved}/{maxRoomsToObserve}).");
    }

    private void HandleObserving()
    {
        observeTimer -= Time.deltaTime;
        if (observeTimer > 0f) return;

        // Day 39 — Explorer (a combat class on a non-observer goal) scouts, then
        // resumes its real goal instead of leaving like an observer type.
        if (goal != AdventurerGoal.ObserveRooms)
        {
            scoutRoomsRemaining--;
            if (scoutRoomsRemaining > 0 && PickRandomRoom())
            {
                state = AdventurerState.MovingToRoom;
                RefreshPath();
                return;
            }
            state = AdventurerState.MovingToCore;   // pursue the type's actual goal
            RefreshPath();
            return;
        }

        if (roomsObserved < maxRoomsToObserve && PickNextRoom())
        {
            state = AdventurerState.MovingToRoom;
            RefreshPath();
            return;
        }

        // Inspectors file their findings with the Guild on the way out, and assess the dungeon.
        if (type == AdventurerType.Inspector)
        {
            InspectorEscalation.Instance?.ReportFindings(
                AdventurerDeaths - deathsAtArrival,
                DungeonCore.Instance != null ? DungeonCore.Instance.Reputation : 0f);
            GradeSystem.Instance?.Assess();
            WispCompanion.Instance?.SpeakLine(
                "The Inspector files its report. The Guild has your measure now -- " +
                "they will send parties fit to the rank they have given you. " +
                "Set your teeth back in the stone before the next ones come.");
        }
        leftSatisfied = true;   // observed its fill and leaves satisfied
        StartRetreat();
    }

    // ── Health ────────────────────────────────────────────────────

    public bool TakeDamage(float amount, DamageKind kind = DamageKind.Melee)
    {
        // The shield wall is a wall of shields against incoming fire: only Ranged
        // hits are mitigated. Melee that reaches the rear rank earns full damage,
        // and environmental sources (traps, chests, room effects, sparring, core
        // burn) ride the Melee default and pass through untouched.
        if (kind == DamageKind.Ranged && WallProtected()) amount *= (1f - shieldWallMitigation);
        currentHP -= amount;
        statusBars?.SetHP(currentHP, maxHP);
        GetComponent<DamageFlash>()?.Flash();
        // A struck passive party stops observing and defends itself.
        passiveUnlessProvoked = false;
        if (currentHP <= 0f) { Die(); return true; }
        animDriver?.OnHurt();
        return false;
    }

    /// <summary>Accumulates damage taken from a monster type and keeps the running
    /// worst offender — the grudge this unit carries home if it survives.</summary>
    public void RecordDamagedBy(string monsterType, float amount)
    {
        if (string.IsNullOrEmpty(monsterType) || amount <= 0f) return;
        damageByType.TryGetValue(monsterType, out float total);
        total += amount;
        damageByType[monsterType] = total;
        if (total > grudgeDamage)
        {
            grudgeDamage = total;
            grudgeMonster = monsterType;
            if (partyMember != null) partyMember.grudgeMonster = grudgeMonster;
        }
    }

    /// <summary>True for a named Hero — the only kill that earns a monster a title.</summary>
    public bool IsNamedHero => named && type == AdventurerType.Hero;

    /// <summary>True while actively fighting - used to hush ambient banter.</summary>
    public bool IsInCombat => state == AdventurerState.Combat;

    /// <summary>Take this one alive instead, if the dungeon can hold them. Heroes and
    /// Inspectors are never taken (the first by rule, the second because the assessment
    /// must run its course), and the Suicidal least of all -- a death in the dark is
    /// exactly what they walked in for. Cells are both the capacity and the opt-in:
    /// with none built, nothing here ever fires. Returns true when the captive is housed
    /// and this body is spent, in which case no death is reported at all: no kill
    /// notoriety, no faction kill, no alignment shift, no corpse. The verb chosen in the
    /// cell decides all of that later.</summary>
    private bool TryCapture()
    {
        var prison = PrisonController.Instance;
        if (prison == null) return false;
        if (type == AdventurerType.Hero
            || type == AdventurerType.Inspector
            || type == AdventurerType.Suicidal) return false;
        if (!prison.TryImprison(displayName, type, combatClass, className, named)) return false;

        // The same resolution bookkeeping a death performs, minus everything that
        // reports a kill. XP still lands: the core overcame them either way.
        DungeonCore.Instance?.AddXP(xpOnDeath);
        DropCarriedTribute("dropped the offering when they were taken");
        party?.OnMemberResolved(partyMember, false, false, CarriedLootValue);
        UnregisterFromFloor(currentFloor);
        DropCarriedLoot();
        if (statusBars != null) Destroy(statusBars.gameObject);
        Destroy(gameObject);
        return true;
    }

    private void Die()
    {
        OnAnyAdventurerSlain?.Invoke();
        DropCarriedTribute("fell with the offering");
        party?.OnMemberResolved(partyMember, false, false, CarriedLootValue);
        UnregisterFromFloor(currentFloor);

        if (type == AdventurerType.Suicidal)
        {
            // A glorious death — word spreads. XP boost + Reputation, no Notoriety penalty.
            DungeonCore.Instance?.AddXP(xpOnDeath * suicidalXpMultiplier);
            DungeonCore.Instance?.AddReputation(suicidalReputationGain);
        }
        else
        {
            DungeonCore.Instance?.AddXP(xpOnDeath);
            float notoGain = DungeonCore.Instance != null ? DungeonCore.Instance.AccrueKillNotoriety() : 0f;
            if (party != null) party.notorietyDelta += notoGain;
            FactionSystem.Instance?.RegisterKill(type, party != null ? party.Formation : FormationType.None);
            AlignmentSystem.Instance?.OnAdventurerKilled(type, combatClass, state == AdventurerState.Retreating);
            if (type == AdventurerType.Inspector) InspectorAssessor.Instance?.OnInspectorSlain();
            if (unlocksOnDeath != null
                && (!unlockRequiresDarkCore || (DungeonCore.Instance != null && DungeonCore.Instance.DungeonType == DungeonType.Dark)))
                BestiaryState.Instance?.Discover(unlocksOnDeath.monsterName);
        }

        // A slain Noble's house takes vengeance.
        if (type == AdventurerType.Noble)
            NobleRetaliation.Instance?.RegisterNobleFall(displayName);

        RunStats.Instance?.RecordAdventurerSlain(className);
        lootTable?.Roll(transform.position);
        DropCarriedLoot();
        DropClassLoot();
        if (statusBars != null) Destroy(statusBars.gameObject);

        TimeScaleController.Instance?.DoKillHitstop();

        if (corpsePrefab != null)
        {
            var corpse = Instantiate(corpsePrefab, transform.position, Quaternion.identity);
            if (currentFloor != null) corpse.transform.SetParent(currentFloor.transform, true);
            if (IsNamedHero)
            {
                // Named dead do not rot on the timer, and their fall teaches Gravegold.
                corpse.GetComponent<Corpse>()?.MarkNamed(displayName);
                PatternDiscovery.NotifyNamedHeroFelled(displayName, transform.position);
            }
        }

        animDriver?.OnDeath();
        enabled = false;                 // freeze behaviour; the Animator plays the death clip
        Destroy(gameObject, deathAnimSeconds);
    }

    private void OnDestroy()
    {
        // Safety net for retreat/exit paths and scene unloads.
        UnregisterFromFloor(currentFloor);
        UnlockState.OnChanged -= HandleUnlockChanged;
        party?.DeregisterLive(this);
    }

    private void DropCarriedLoot()
    {
        for (int i = 0; i < carriedLoot.Count; i++)
        {
            var loot = carriedLoot[i];
            if (loot == null) continue;
            Vector2 scatter = Random.insideUnitCircle * 0.3f;
            loot.DropAndAbsorb(transform.position + new Vector3(scatter.x, scatter.y), droppedLootPrefab);
        }
        if (restoredCarriedGold > 0 && droppedLootPrefab != null)
        {
            Vector2 lumpScatter = Random.insideUnitCircle * 0.3f;
            var d = Instantiate(droppedLootPrefab,
                transform.position + new Vector3(lumpScatter.x, lumpScatter.y, 0f), Quaternion.identity);
            d.Initialise(restoredCarriedGold);
            restoredCarriedGold = 0;
        }
        carriedLoot.Clear();
    }

    // Drops the unit's combat-class loot (gold) on death into the core-absorbed pool.
    // VIP-escort guards drop higher-value gear via the escort multiplier.
    private void DropClassLoot()
    {
        if (classDef == null || droppedLootPrefab == null) return;

        var entry = LootTable.PickWeighted(classDef.classLoot);
        if (entry == null) return;

        bool escortGuard = party != null
            && party.Formation == FormationType.Escort
            && type == AdventurerType.Mercenary;

        int value = Mathf.Max(1, Mathf.RoundToInt(
            entry.goldValue * (escortGuard ? escortGuardLootMultiplier : 1f)
            * LootRarity.MultiplierFor(entry.rarity)));

        Vector2 scatter = Random.insideUnitCircle * 0.3f;
        var d = Instantiate(droppedLootPrefab,
            transform.position + new Vector3(scatter.x, scatter.y, 0f), Quaternion.identity);
        d.Initialise(value, entry.rarity,
            entry.lootType == LootTable.LootType.Book ? entry.grantsNode : null);
    }

    // ── Trap Helpers ──────────────────────────────────────────────

    private void CheckTrapAtCurrentCell()
    {
        if (currentFloor == null) return;
        var influence = currentFloor.TileInfluence;
        var trapReg = currentFloor.TrapRegistry;
        if (influence == null || trapReg == null) return;

        Vector3Int cell = influence.WorldToCell(transform.position);
        var trap = trapReg.GetTrapAt(cell);
        if (trap != null) trap.OnAdventurerEntered(this);
    }

    // Only damage traps are worth a Rogue's time — Warning markers and pressure
    // plates carry no payload to defuse.
    private static bool IsDisarmableTrap(TrapBase trap)
    {
        if (trap == null || trap.Definition == null) return false;
        var b = trap.Definition.behaviour;
        return b == TrapDefinition.TrapBehaviour.SpikeTrap
            || b == TrapDefinition.TrapBehaviour.Pitfall;
    }

    // True (and enters the Disarming state) when the next waypoint holds a flagged,
    // still-armed damage trap and the Rogue is close enough to work on it.
    private bool TryBeginDisarm(Vector3 waypoint)
    {
        if (currentFloor == null) return false;
        var influence = currentFloor.TileInfluence;
        var trapReg = currentFloor.TrapRegistry;
        if (influence == null || trapReg == null) return false;

        Vector3Int cell = influence.WorldToCell(waypoint);
        var trap = trapReg.GetTrapAt(cell);
        if (trap == null || !trap.IsFlagged || trap.IsDisarmed) return false;
        if (!IsDisarmableTrap(trap)) return false;
        if (Vector2.Distance(transform.position, waypoint) > disarmReach) return false;

        BeginDisarm(trap);
        return true;
    }

    private void BeginDisarm(TrapBase trap)
    {
        disarmTarget = trap;
        disarmTimer = disarmChannelSeconds;
        stateBeforeDisarm = state;
        state = AdventurerState.Disarming;
    }

    private void HandleDisarming()
    {
        if (disarmTarget == null || disarmTarget.IsDisarmed)
        {
            state = stateBeforeDisarm;
            disarmTarget = null;
            RefreshPath();
            return;
        }

        disarmTimer -= Time.deltaTime;
        if (disarmTimer > 0f) return;

        if (Random.value < disarmBackfireChance)
        {
            // The snare bites back once; the rogue steadies and tries again.
            disarmTarget.TriggerExternally(this);
            disarmTimer = disarmChannelSeconds;
            return;
        }

        Vector3 where = disarmTarget.transform.position;
        disarmTarget.Disarm();
        AlertsLog.Instance?.AddAlert(
            "Deft hands find my snare. One of my fangs falls still.",
            where, currentFloor != null ? currentFloor.FloorIndex : -1,
            AlertCategory.Trap);

        state = stateBeforeDisarm;
        disarmTarget = null;
        RefreshFloorPaths();
    }

    // A cleared lane changes the cheapest route, so every adventurer on this floor
    // re-paths through it. Nothing listens to flagged-changes for path recompute,
    // so we push the refresh explicitly.
    private void RefreshFloorPaths()
    {
        if (currentFloor?.Entities == null) return;
        var advs = currentFloor.Entities.GetAll<DungeonAdventurer>();
        for (int i = 0; i < advs.Count; i++)
            if (advs[i] != null) advs[i].ForceRefreshPath();
    }

    private void ScanForTraps()
    {
        if (currentFloor == null) return;
        var trapReg = currentFloor.TrapRegistry;
        var influence = currentFloor.TileInfluence;
        if (trapReg == null || influence == null) return;

        float roll = Random.value;
        if (roll >= trapDetectionChancePerSecond * Time.deltaTime) return;

        foreach (var trap in trapReg.GetTrapsWithinRadius(
                     transform.position, trapDetectionRadius, influence))
        {
            if (trap.IsFlagged) continue;
            trap.Flag();
            DamageNumberSpawner.Spawn(0f, influence.CellToWorld(trap.OccupiedCell),
                FloatingDamageNumber.DamageType.Alert);
            Debug.Log($"[Adventurer] Detected trap at {trap.OccupiedCell}.");
            ReactToTrapDetection();
            RefreshPath();
            break;
        }
    }

    // ── Formation / organize + Rogue halt ───────────────

    // Movement freezes briefly when a Rogue in the party warns of a trap;
    // combat and retreat ignore the halt.
    private bool MovementHalted =>
        party != null && Time.time < party.HaltUntil
        && state != AdventurerState.Combat && state != AdventurerState.Retreating;

    private void HandleOrganizing()
    {
        // Combat overrides forming up — a monster on us breaks formation.
        ScanForMonsters();
        if (state != AdventurerState.Organizing) return;

        if (formationSlot.HasValue)
        {
            transform.position = Vector2.MoveTowards(
                transform.position, formationSlot.Value,
                EffectiveMoveSpeed * Time.deltaTime);
        }
        else
        {
            // Loose muster: drift between small points near the mouth until
            // the shared window closes. Candidate steps are validated with
            // the spawner's own ground check, so nobody strays onto unmined
            // apron where pathing cannot resolve.
            if (Time.time >= nextMusterStepTime)
            {
                nextMusterStepTime = Time.time + Random.Range(0.8f, 1.8f);
                Vector3 anchor = DungeonEntrance.Instance != null
                    ? DungeonEntrance.Instance.SpawnPosition : transform.position;
                Vector2 drift = Random.insideUnitCircle * 1.6f;
                Vector3 candidate = anchor + new Vector3(drift.x, drift.y, 0f);
                var floor0 = FloorManager.Instance?.GetFloor(0);
                if (floor0 != null && floor0.TileInfluence != null)
                {
                    var cell = floor0.TileInfluence.WorldToCell(candidate);
                    bool ok = floor0.TileInfluence.IsTileMined(cell)
                        || (floor0.FeatureGenerator != null
                            && floor0.FeatureGenerator.IsEntranceCave(cell));
                    if (!ok) candidate = anchor;
                }
                musterTarget = candidate;
            }
            transform.position = Vector2.MoveTowards(
                transform.position, musterTarget,
                EffectiveMoveSpeed * 0.6f * Time.deltaTime);
        }

        // Hold until the whole party has formed up, then advance together.
        if (party == null || Time.time >= party.OrganizeEndTime)
            BeginAdvance();
    }

    private Vector3 ComputeFormationSlot()
    {
        Vector3 anchor = DungeonEntrance.Instance != null
            ? DungeonEntrance.Instance.SpawnPosition
            : transform.position;
        Vector2 fwd = party != null ? party.AdvanceDir : Vector2.right;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector2.right;
        Vector2 side = new Vector2(-fwd.y, fwd.x);

        ComputeSlotOffset(out float forward, out float lateral);
        return anchor + (Vector3)(fwd * forward + side * lateral);
    }

    private void ComputeSlotOffset(out float forward, out float lateral)
    {
        const float rankGap = 1.1f;
        const float sideGap = 0.9f;

        if (party != null && party.Formation == FormationType.Escort)
        {
            bool isVip = type != AdventurerType.Mercenary;   // guards are Mercenary-typed
            if (isVip)
            {
                forward = 0f;
                lateral = sideGap * Spread(party.ClaimSlot(0));
            }
            else
            {
                int tier = (combatClass == CombatClass.Tank || combatClass == CombatClass.Fighter) ? 0 : 1;
                forward = tier == 0 ? 2f : 1.2f;
                lateral = sideGap * Spread(party.ClaimSlot(10 + tier));
            }
            return;
        }

        // Assault — ranks front (Tank/Fighter) to rear (Cleric).
        int rank = AssaultRank(combatClass);
        forward = 2f - rank * rankGap;
        lateral = sideGap * Spread(party != null ? party.ClaimSlot(rank) : 0);
    }

    private static int AssaultRank(CombatClass c) => c switch
    {
        CombatClass.Tank => 0,
        CombatClass.Fighter => 0,
        CombatClass.Rogue => 1,
        CombatClass.Explorer => 1,
        CombatClass.Mage => 2,
        CombatClass.Cleric => 3,
        _ => 1,
    };

    // 0 -> centre, then +1, -1, +2, -2 ... symmetric without needing the lane total.
    private static float Spread(int i)
    {
        int step = (i + 1) / 2;
        return (i % 2 == 1) ? step : -step;
    }

    // A Rogue spotting a NEW trap throws a yellow "!" and briefly halts the party.
    private void ReactToTrapDetection()
    {
        DamageNumberSpawner.Spawn(0f, transform.position, FloatingDamageNumber.DamageType.Alert);
        if (party == null || Time.time < party.HaltCooldownEnd) return;
        party.HaltUntil = Time.time + trapHaltDuration;
        party.HaltCooldownEnd = Time.time + trapHaltCooldown;
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (multiplier < slowMultiplier) slowMultiplier = multiplier;
        if (duration > slowTimer) slowTimer = duration;
    }

    /// <summary>
    /// Dread Chamber slow: multiplies move speed and divides attack rate.
    /// Clamped so a stray value can never freeze an intruder outright.
    /// </summary>
    public void SetRoomSlowMultiplier(float multiplier)
    {
        roomSlowMultiplier = Mathf.Clamp(multiplier, 0.25f, 1f);
    }

    // ── IMonsterTarget ────────────────────────────

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
        else knockbackRemaining = 0f;   // hit a wall — stop short
        knockbackRemaining -= step;
    }

    // ── Intent Badge ─────────────────────────────────────

    private void HandleUnlockChanged(string key)
    {
        if (key == UnlockState.OracleChamber) RefreshIntentBadge();
    }

    private void RefreshIntentBadge()
    {
        if (statusBars == null) return;
        if (!UnlockState.IsUnlocked(UnlockState.OracleChamber))
        {
            statusBars.SetIntentLabel(null, Color.white);
            return;
        }
        statusBars.SetIntentLabel(IntentDisplayName(), IntentColour());
    }

    private string IntentDisplayName()
    {
        if (type == AdventurerType.Inspector) return "Inspector";
        return intent switch
        {
            PartyIntent.Pilgrim => "Pilgrim",
        PartyIntent.GiftGiver => "Gift-Giver",
            PartyIntent.Destroyer => "Destroyer",
            _ => "",
        };
    }

    private Color IntentColour() => intent switch
    {
        PartyIntent.Pilgrim => new Color(0.55f, 0.80f, 1.00f),  // calm blue
        PartyIntent.GiftGiver => new Color(0.50f, 0.90f, 0.50f),  // gift green
        PartyIntent.Destroyer => new Color(1.00f, 0.45f, 0.40f),  // threat red
        _ => Color.white,
    };

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    // -- Banter -------------------------------------------------------------------
    private float lastBarkTime = -999f;
    [SerializeField] private float barkCooldown = 12f;   // min seconds between this one's lines

    /// <summary>True when this one may pipe up with idle chit-chat (idle, alive, off cooldown).</summary>
    public bool CanBanter =>
        gameObject.activeInHierarchy
        && state != AdventurerState.Combat
        && state != AdventurerState.Retreating
        && Time.time - lastBarkTime >= barkCooldown;

    /// <summary>Speak a line above this adventurer's head.</summary>
    public void Say(string line, Color colour)
    {
        if (string.IsNullOrEmpty(line)) return;
        BarkSpawner.Spawn(transform.position, line, colour);
        lastBarkTime = Time.time;
    }

    private void TryChargeBark()
    {
        // Rare "charge!" shout when breaking into combat (e.g. Leroy Jenkins).
        if (Random.value > BanterLines.ChargeEggChance) return;
        if (Time.time - lastBarkTime < barkCooldown) return;
        Say(BanterLines.RandomChargeEgg(), BanterLines.Egg);
    }

    private bool sightedCore;

    private void TrySightCore()
    {
        if (sightedCore) return;
        var core = DungeonCore.Instance;
        if (core == null) return;
        float r = BanterLines.CoreSightRange;
        if ((transform.position - core.transform.position).sqrMagnitude > r * r) return;
        sightedCore = true;
        BanterLines.ReactCoreSight(this);
    }

    public AdventurerState State => state;
    public PartyIntent Intent => intent;
    public AdventurerParty Party => party;
    public BehaviourTrait Trait => trait;
    public int CarriedLootCount => carriedLoot.Count;
    public FloorRoot CurrentFloor => currentFloor;

    // Type / goal reads
    public AdventurerType Type => type;
    public PartyMember Member => partyMember;
    public AdventurerGoal Goal => goal;
    /// <summary>True only for goals that destroy the core on arrival (Mercenary / Hero / Suicidal).
    /// Worshippers, looters and observers are NOT a danger to the core.</summary>
    public bool ThreatensCore => goal == AdventurerGoal.BreachCore || goal == AdventurerGoal.SeekDeath;

    // Combat class reads
    public CombatClass Class => combatClass;

    /// <summary>Name if this is a named individual (from named-party tracking); else null/empty.</summary>
    public string DisplayName => displayName;

    /// <summary>Total gold value of all loot this adventurer is currently carrying.</summary>
    public int CarriedLootValue
    {
        get
        {
            int v = restoredCarriedGold;
            foreach (var l in carriedLoot) if (l != null) v += l.GoldValue;
            return v;
        }
    }

    // ── Save / restore (live persistence) ───────────────
    /// <summary>Writes this unit's dynamic state into its roster save record.</summary>
    public void CaptureLiveState(LiveMemberSaveData rec)
    {
        rec.floorIndex = currentFloor != null ? currentFloor.FloorIndex : 0;
        rec.position = SerializableVector3.From(transform.position);
        rec.currentHP = currentHP;
        rec.state = (int)state;
        rec.worshipCompleted = worshipCompleted;
        rec.worshipTimer = worshipTimer;
        rec.roomsObserved = roomsObserved;
        rec.leftSatisfied = leftSatisfied;
        rec.carriedGold = CarriedLootValue;
        rec.tributeValue = tributeValue;
        rec.returnGrudge = returnGrudge;
        rec.grudgeMonster = grudgeMonster;
        rec.grudgeDamage = grudgeDamage;
    }

    /// <summary>Queues restored state; applied at the end of Start so it overrides the
    /// fresh-spawn defaults (full HP, initial state) once status bars exist.</summary>
    public void ApplyLiveState(LiveMemberSaveData rec) => pendingRestore = rec;

    private void ApplyPendingRestore()
    {
        var rec = pendingRestore;
        pendingRestore = null;

        currentHP = Mathf.Clamp(rec.currentHP, 1f, maxHP);
        statusBars?.SetHP(currentHP, maxHP);
        restoredCarriedGold = rec.carriedGold;
        returnGrudge = rec.returnGrudge;
        grudgeMonster = rec.grudgeMonster;
        grudgeDamage = rec.grudgeDamage;
        if (partyMember != null) partyMember.grudgeMonster = grudgeMonster;
        worshipCompleted = rec.worshipCompleted;
        worshipTimer = rec.worshipTimer;
        roomsObserved = rec.roomsObserved;
        leftSatisfied = rec.leftSatisfied;

        // Settled states resume; mid-action states fall back to advancing.
        state = (AdventurerState)rec.state switch
        {
            AdventurerState.UsingStairs => AdventurerState.MovingToCore,
            AdventurerState.Disarming => AdventurerState.MovingToCore,
            AdventurerState.Organizing => AdventurerState.MovingToCore,
            AdventurerState.Pinned => AdventurerState.MovingToCore,
            AdventurerState.MovingToRescue => AdventurerState.MovingToCore,
            var s => s,
        };
        RefreshPath();
    }

    /// <summary>Carried loot slows the adventurer: 1 when empty, down to encumbranceFloor for a heavy haul.</summary>
    private float EncumbranceMultiplier()
    {
        if (carriedLoot.Count == 0) return 1f;
        return Mathf.Max(encumbranceFloor, 1f - CarriedLootValue * encumbrancePerGold);
    }

    /// <summary>Move speed after every modifier -- trap slow, room dread, terrain (fording) and loot encumbrance.</summary>
    private float EffectiveMoveSpeed => moveSpeed * EncumbranceMultiplier() * slowMultiplier
                                        * terrainSpeedMultiplier * roomSlowMultiplier;
    /// <summary>Tank taunt — monsters prefer a taunting adventurer as their target.</summary>
    public bool IsTaunting => tauntTimer > 0f;
}