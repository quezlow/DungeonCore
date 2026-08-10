using System;
using System.Collections.Generic;

/// <summary>
/// Top-level container serialised to DungeonSaveData.json.
///
/// DAY 31 PART 3D — MonsterSpawnerSaveData gains order state (orderMode,
///   patrolWaypoints, patrolLoop, hasAttackTarget, attackTargetCell).
///   Old saves missing these fields default to Wander (backwards compatible).
/// </summary>
[Serializable]
public class DungeonSaveData
{
    /// <summary>
    /// Schema version of this save data. Bump when making a non-additive change
    /// (renamed/removed/semantically-changed field) and register a migration in
    /// SaveMigrationRegistry. Additive changes (new fields) do not require a
    /// version bump — JsonUtility's default-tolerant deserialization handles them.
    /// </summary>
    public const int CURRENT_VERSION = 3;

    /// <summary>
    /// Version stamped at save time. Compared against CURRENT_VERSION on load
    /// to decide whether migration is needed. Saves predating this field
    /// deserialize with saveVersion == 0, which is treated as an implicit v1.
    /// </summary>
    public int saveVersion = 0;

    public string dungeonName = "Unnamed Dungeon";

    public bool hasSave;
    public int worldSeed;

    public DungeonCoreSaveData coreData;
    public DayNightSaveData dayNightData;

    public int coreFloorIndex;
    public bool hasCoreCell;                          
    public SerializableVector3Int coreCell;
    public int pendingCoreRelocationFloor = -1;
    public List<int> visitedFloors = new();

    public List<FloorSaveData> floors = new();

    public bool hasEntrance;
    public SerializableVector3Int entranceCell;

    public bool hasCameraState;
    public SerializableVector3 cameraWorldPos;  // world space, includes floor Y offset
    public int cameraFloorIndex;
    public List<CameraBookmarkSaveData> cameraBookmarks = new();

    public List<AlertEntrySaveData> alertHistory = new();
    public int alertUnreadCount = 0;
    public List<string> prologueFlags = new();            // the life lived above; read by CoreMemory (additive; empty on old saves)
    public bool descendantsDispatched;                   // the town's descendants have come down once (additive; false on old saves)
    public bool restArmed;                               // the resting place: descended, so the wisp may mention it (additive)
    public bool restAnnounced;                           // the resting place: the wisp has mentioned it (additive)
    public bool restFound;                               // the resting place: the stone is open (additive)
    public List<string> wispSpokenLines = new();          // tutorial one-shots already heard (additive; empty on old saves)
    public int wispPersonality = -1;                      // rolled once per dungeon; -1 = not yet rolled
    public bool tutorialComplete = false;                 // guided opening finished; never replays (additive; false on old saves)
    public int merchantNextVisitDay = -1;                 // wandering merchant schedule; -1 = unscheduled (additive)
    public int prisonReactionDay = -1;                    // faction reaction to a held captive; -1 = none pending (additive)
    public int dwarvenSpoilUnsold = 0;                    // gold owed by the Deep Holds, uncollected (additive; 0 on old saves)
    public int dwarvenSpoilLifetime = 0;                  // gold ever taken at their counter (additive; 0 on old saves)
    public int caravanNextDepartureDay = -1;              // dwarven caravan schedule; -1 = due first eligible day (additive)
    public int caravanState = 0;                          // caravan journey state machine; 0 = Idle (additive; append-only ints)
    public float caravanWalkedSeconds = 0f;               // day-phase walking elapsed this leg (additive)
    public float caravanPhaseSeconds = 0f;                // calendar seconds elapsed this transit/dwell (additive)
    public int caravanCargo = 0;                          // gold value the wagon carries this journey (additive)
    public bool caravanVerbUsed = false;                  // the one Rob/Tax/Let-pass is spent (additive)
    public bool caravanSighted = false;                   // first-ever caravan Discovery has fired (additive)
    public bool caravanTollVignettePlayed = false;        // the toll camera beat never replays (additive)
    public bool dwarvenClaimWarned = false;               // the one free warning is spent (additive; false on old saves)
    public bool dwarvenPressureWarned = false;            // rung 2's wisp line has been heard (additive; false on old saves)
    public bool holyTouchMurmured = false;                // the wisp has named hallowed ground (additive; false on old saves)
    public bool holyFirstBreakDone = false;               // one seal has been broken, so the rest are Critical (additive)
    public List<string> holyBrokenSeals = new();          // "floorIndex:siteId" already unsealed and paid out (additive)
    public List<string> dwarvenAlertedOwners = new();     // "floorIndex:ownerKey" already alerted about (additive; empty on old saves)
    public List<QuestProgress> wispQuestsActive = new();  // wisp urgings in progress (additive; empty on old saves)
    public List<string> wispQuestsHandedIn = new();       // wisp urgings completed (additive)
    public bool wispQuestsInitialised = false;            // director has reconciled this dungeon once (additive)

    public RunStatsSaveData runStats;

    public List<EarnedDeedSaveData> earnedDeeds = new();   // chronicle (additive; empty on old saves)

    public List<TrackedParty> trackedParties = new();

    public List<CampGrowthSaveData> campGrowth = new();

    public List<LivePartySaveData> liveParties = new();

    public InspectorEscalationSaveData inspectorEscalation;

    public FactionSystemSaveData factionSystem;

    public AlignmentSaveData alignment;

    public HolyOrderStrikeSaveData holyOrderStrike;

    public MercenaryContractSaveData mercenaryContract;

    public AppealLedgerSaveData appealLedger;

    public WildMonsterEventSaveData wildMonsterEvent;

    public NobleRetaliationSaveData nobleRetaliation;

    public WorldEventsSaveData worldEvents;   // random world events (additive; null on old saves)

    public EndgameClimaxSaveData endgameClimax;

    public GradeSystemSaveData grade;

    public InspectorAssessorSaveData inspectorAssessor;

    public BestiarySaveData bestiary;

    public List<TodoItemSaveData> playerTodos = new();   // player-authored to-do list (quest journal)

    // Material pattern system (additive; null/empty on legacy saves)
    public List<string> unlockedKeys = new();             // UnlockState flags, incl. "pattern." keys
    public List<PatternNoteSaveData> patternNotes = new();

    // Research spine (additive; empty/zero on legacy saves)
    public string activeResearchKey = "";
    public float activeResearchDaysRemaining;
    public string queuedResearchKey = "";
}

[Serializable]
public class CameraBookmarkSaveData
{
    public bool set;
    public SerializableVector3 pos;
    public int floor;
    public float zoom;
}

[Serializable]
public class TodoItemSaveData
{
    public string text;
    public bool done;
}

[Serializable]
public class PatternNoteSaveData
{
    public string key;      // full UnlockState key, e.g. "pattern.rough_stone"
    public string source;   // learned-from line shown in the codex
}

[Serializable]
public class RunStatsSaveData
{
    // Cumulative (whole run)
    public List<ClassKillSaveData> killsByClass = new();
    public int monstersLost;
    public int biggestParty;
    public int goldEarned;
    public int maxDayReached = 1;

    // Current day (preserves a mid-day save/load's partial tally)
    public int currentDay = 1;
    public int partiesToday;
    public int slainToday;
    public int monstersLostToday;
    public int goldEarnedToday;
    public float notorietyAtDayStart;
    public List<RaidRecord> raidsToday = new();
}

[Serializable]
public class ClassKillSaveData
{
    public string className;
    public int count;
}

[Serializable]
public class EarnedDeedSaveData
{
    public string key;
    public int dayEarned;
}

[Serializable]
public class FloorSaveData
{
    public int floorIndex;
    public SerializableVector3Int centerCell;
    public int floorSeed;
    public FloorFeatureSaveData featureData;
    public TileInfluenceSaveData tileData;
    public List<MonsterSpawnerSaveData> spawners = new();
    public List<DungeonChestSaveData> chests = new();
    public List<FurnitureSaveData> furniture = new();
    public List<NamedCorpseSaveData> namedCorpses = new();   // named-hero corpses (additive; null on old saves)
    public List<PrisonerSaveData> prisoners = new();         // captives held in cells (additive; null on old saves)
    public List<RoomAnchorSaveData> roomAnchors = new();
    public List<TrapSaveData> traps = new();
    public List<StairsSaveData> stairs = new();
    public string floorName;   // player-set floor name (additive; null on old saves)
}

[Serializable]
public class MonsterSpawnerSaveData
{
    public string monsterName;
    public string customName;   // player-set monster name (additive; null on old saves)
    public bool raisedOneLife;   // crypt-raised, one life (additive; false on old saves)
    public SerializableVector3Int cell;

    // DAY 31 PART 3D — Orders.
    public int orderMode = 0; // SpawnerOrderMode enum int
    public List<SerializableVector3Int> patrolWaypoints = new();
    public bool patrolLoop = true;
    public bool hasAttackTarget = false;
    public SerializableVector3Int attackTargetCell;
    public bool allowDefendCore = true;
    public bool hasPost = false;                    // additive; false on old saves
    public SerializableVector3Int postCell;
    public bool musterGated = false;                // additive; pre-muster spawners stay exempt
    public int promotionRank = 0;                   // additive; PromotionRank enum int, 0 on old saves
    public string bossEpithet;                      // additive; rolled boss epithet, null below boss rank

    // DAY 31 — Alive monster state. Captured when this spawner has a live monster
    // at save time; consumed by the spawner's first SpawnMonster() on load.
    // PART 3 CLOSE-OUT — XP + isVeteran added so veteran progress survives reload.
    public bool hasAliveMonster;
    public float aliveMonsterHP;
    public SerializableVector3Int aliveMonsterCell;
    public int alivePatrolIndex;
    public float aliveMonsterXP;
    public bool aliveMonsterIsVeteran;
    public int aliveMonsterKills;
}

[Serializable]
public class DungeonChestSaveData
{
    public SerializableVector3Int cell;
    public bool isOpened;
    public string chestName;
}

[Serializable]
public class FurnitureSaveData
{
    public string furnitureName;
    public SerializableVector3Int cell;
}

[Serializable]
public class NamedCorpseSaveData
{
    public string heroName;
    public SerializableVector3Int cell;   // sarcophagus cell when housed, else where it lies
    public bool housed;
}

[Serializable]
public class PrisonerSaveData
{
    public string captiveName;
    public int type;                      // AdventurerType ordinal
    public int combatClass;               // CombatClass ordinal
    public string className;
    public bool named;
    public int daysHeld;
    public SerializableVector3Int cell;   // the cell furniture holding them
}

[Serializable]
public class RoomAnchorSaveData
{
    public SerializableVector3Int cell;
    public string assignedRoomName;
    public int tier = 1;
    public List<SerializableVector3Int> footprint = new();
}

[Serializable]
public class TrapSaveData
{
    public string trapName;
    public SerializableVector3Int cell;
    public bool isFlagged;
    public bool isDisarmed;
    public string warningLabel;
    public bool hasLink;
    public SerializableVector3Int linkedCell;
}

[Serializable]
public class StairsSaveData
{
    public SerializableVector3Int cell;
    public int direction;
}

[Serializable]
public class LivePartySaveData
{
    public int intent;
    public bool tracked;
    public int bannerColorIndex = -1;
    public string bannerLabelOverride;
    public bool isClimax;
    public bool exitBonusApplied;
    public bool tributeAssigned;
    public bool fractured;
    public float notorietyDelta;
    public List<LiveMemberSaveData> members = new();
}

[Serializable]
public class LiveMemberSaveData
{
    // Roster identity — every member, alive or already resolved.
    public int type;
    public int combatClass;
    public int affinity;
    public int trait;
    public string name;
    public bool named;
    public bool resolved;
    public bool escaped;
    public bool breached;
    public int lootValue;
    public int xp;
    public string grudgeMonster;

    // Live dynamic state — only when isLive.
    public bool isLive;
    public int floorIndex;
    public SerializableVector3 position;
    public float currentHP;
    public int state;
    public bool worshipCompleted;
    public float worshipTimer;
    public int roomsObserved;
    public bool leftSatisfied;
    public int carriedGold;
    public int tributeValue;
    public string returnGrudge;
    public float grudgeDamage;
}