# DUNGEON CORE: REBIRTH -- DESIGN CANON

The as-built design record. This file describes systems as they actually exist
and as they were actually decided, superseding all planning documents.

## Precedence

When sources conflict, resolve in this order:

1. **This file** -- decided design, as-built behaviour, canon values.
2. **Live code on `main`** -- for anything this file does not cover.
3. **Production Roadmap / Dev Plan / Passive Backlog** -- original intent and
   unshipped plans ONLY. The roadmap's claim to be "the canonical design
   reference" is hereby retired for anything that has shipped.

In-code header comments can themselves be stale (several are noted below);
where a comment and this file disagree, this file wins until the comment is
corrected.

## How to use this file

- **Before designing or extending any system**: read its entry here first,
  then the key files it points to. Do not design from the roadmap's version
  of a shipped system.
- **Part I** is shipped and verified against source. **Part II** is decided
  design that has NOT been built -- do not assume its classes or APIs exist.
  **Part III** is lore canon.
- Entries carry a `Verified:` date -- the date the entry was checked against
  repo HEAD.

## Update ritual

Every feature guide ends with an **Update the Canon** chapter: a find/replace
or append edit to this file, validated in the same pipeline as code edits.
A feature is not "done" until its canon entry lands. When a decision here is
deliberately changed, edit the entry (do not append a contradiction) and note
the supersession in one line.

---

## Contents

**Part I -- Shipped systems**
1. Room System
2. Adventurer Types, Intents and Goals
3. Behaviour Traits and Combat Classes
4. Named Party Tracking (Nemesis Parties)
5. Guild Assessment (Inspector + Grade System)
6. Alignment Axis
7. Factions and Standing
8. Recurring Threat Events
9. Endgame Climax (Diamond 3 Trial)
10. Assault Staging
11. Tribute and GiftGivers
12. Ambient Necromancy and Corpses
12A. Influence Field, Push and Breach Recede

**Part II -- Designed, not yet built**
13. Research Tree (Phase 4.5)
14. Material Pattern System
15. Room Effects v2 and Attractor Rooms
16. Crypt and Deliberate Nemesis Raise
17. Discovery Content (Buried Skeletons, Loot Books, Wisp Guide)
18. Phase 5 Designs
19. Buried Age Sites and Tier-Up Audiences

**Part III -- Lore canon**
20. Why Holy Sites Are Underground
21. The Buried Age

---

# PART I -- SHIPPED SYSTEMS

## 1. Room System

Status: SHIPPED. Supersedes the roadmap's room/recipe section and all
flood-fill-era descriptions. Verified: 2026-07-09.

**As built -- creation flow:**
Rooms are player-designated, not auto-detected. In `PlaceRoomAnchor` build
mode the player presses on a mined tile and drags a rectangle (live gold
preview overlay). On release, `CommitRoomFootprint` takes every mined tile in
the rectangle that is not already claimed by another room -- overlap between
rooms is blocked -- and that cell list becomes the room's **footprint**, the
source of truth for its extent. An anchor object is instantiated at the
drag-start cell and `RoomTypePickerUI` opens immediately for type selection.
Placing an anchor is free; cost enters only through upgrades. Esc cancels a
drag in progress.

**Anchor interaction:** left-click an anchor reopens the type picker;
right-click begins a redesignate (re-drag the footprint, keeping the assigned
type). Anchors are `IFloorEntity`s registered with their floor.

**Validation** (`RoomValidator.Validate(footprint, def)`, re-run on type
change and after any furniture change): intersect the footprint with
currently-mined tiles on the ACTIVE floor, then check in order: max tile cap
and `requiresCore` (Throne Room constraints), `minTileCount`, required
furniture counts, boss-spawner presence (`requiresBossSpawner`). There is NO
upper size cap in footprint mode. Validation failures surface via
`LastFailReason`; state changes fire `OnRoomValidationChanged` (tint + toast
systems listen).

**Room types are data:** `RoomDefinition` ScriptableObjects (Create ->
Dungeon -> Room Definition). New room types need no code. Reference minimums
from the definition comment: Library 12, Barracks 9, Shrine 9, Oracle Chamber
12, Boss Room 16 (+ boss spawner). Throne Room uses `requiresCore` (must
enclose the core cell) plus `maxTileCount` (size cap) -- it is the room built
AROUND the core, not a separate prestige hall as the backlog framed it.

**Effects and upgrades:** `RoomEffect` list per definition; shipped effect
types are `LairRegen`, `TrainingXp`, `MonsterDamageBuff` (multiplier),
`CoreRetaliation` (core zaps adventurers in the room). Effects apply while
valid, via `RoomEffectController`. Rooms tier up: Tier 1 base, `maxTier`
default 3, next tier costs `upgradeBaseCost x currentTier` in gold,
`EffectScale = Tier` (linear). `RoomAnchor.UpgradeGate` is a static
`Func<RoomDefinition,int,bool>` reserved for the research tree -- currently
null (no gate).

**Persistence:** footprints are saved explicitly. Flood-fill-era saves carry
no footprint and are seeded once via `MigrateFootprintFromFloodFill`.

**Legacy still in code:** the flood-fill model survives as the legacy
`Validate(anchorCell, def)` overload (capped at 200 tiles), the save
migration, and the connectivity checks `WouldBlockRoom` /
`WouldBlockDungeon`. STALE COMMENTS: the `RoomValidator` header and the
`minTileCount` tooltip still describe the flood-fill flow as primary -- they
are wrong; the footprint overload is the live path.

**Key files:** `Room/RoomAnchor.cs`, `Room/RoomValidator.cs`,
`Room/RoomDefinition.cs`, `Room/RoomTypePickerUI.cs`,
`Room/RoomEffectController.cs`, `DungeonCore/DungeonBuildController.cs`
(anchor mode, footprint commit, preview).

**Rejected:** flood-fill-from-anchor as the room boundary (rooms became
whatever open space touched the anchor; superseded by designated footprints).
Auto-detection of rooms from enclosed regions or furniture recipes alone was
never the shipped model.

**Room type picker (2026-07-09):** `RoomTypePickerUI` reads
`RoomDefinitionRegistry.All` directly; its separate `roomDefinitions` list is
removed. Registry order is button order. Adding a room is a single registry
append -- the picker and name-based save restore can no longer drift.

## 2. Adventurer Types, Intents and Goals

Status: SHIPPED. Supersedes the roadmap's nine-type intent table (p.13) and
any note stating "Treasure Hunter maps to Destroyer". Verified: 2026-07-09.

**As built:** there are **eleven** adventurer types and **four** intents.
`AdventurerTypeInfo` is the single source of truth mapping each type to an
intent (reward/consequence category), a goal (in-dungeon behaviour), and a
dispatching faction. A definition asset declares only its type. STALE
COMMENT: the enum header still says "nine adventurer TYPES".

| Type            | Intent    | Goal         | Faction          |
|-----------------|-----------|--------------|------------------|
| TreasureHunter  | Delver    | LootAndLeave | AdventurersGuild |
| Mercenary       | Destroyer | BreachCore   | MercenaryCompany |
| Scholar         | Pilgrim   | ObserveRooms | AdventurersGuild |
| Pilgrim         | Pilgrim   | WorshipCore  | HolyOrder        |
| Suicidal        | Pilgrim   | SeekDeath    | AdventurersGuild |
| Noble           | Delver    | ObserveRooms | AdventurersGuild |
| Cultist         | GiftGiver | WorshipCore  | Cultists         |
| Hero            | Destroyer | BreachCore   | AdventurersGuild |
| Inspector       | Pilgrim   | ObserveRooms | AdventurersGuild |
| Delver          | Delver    | Delve        | AdventurersGuild |
| Commoner        | Pilgrim   | ObserveRooms | AdventurersGuild |

**Intents** (party-wide, one per party): Pilgrim (walk to core, worship,
leave; reduces Notoriety on completion), GiftGiver (drop tribute chest near
entrance on arrival, then behave normally), Destroyer (beeline the core,
ignore loot), **Delver** (hunt monsters for XP and loot, leave alive).
GiftGiver is Cultist-only. Intent is hidden until the Oracle Chamber TechNode
unlock; until then it is hinted through behaviour.

**Goals** (drive the state machine independently of intent): WorshipCore,
LootAndLeave, BreachCore, ObserveRooms, SeekDeath, Delve.

**Faction attribution is by role, not type:** a Mercenary spawned as an
escort guard is the Guild's loss at the kill (`FactionSystem.FactionForKill`),
not the Company's.

**Key files:** `Adventurer/AdventurerType.cs`, `Adventurer/PartyIntent.cs`,
`Adventurer/AdventurerSpawner.cs`, `Adventurer/FactionSystem.cs`.

**Rejected:** roadmap's Treasure Hunter = GiftGiver mapping; the interim
Treasure Hunter = Destroyer remap (superseded when the Delver intent landed);
Noble as "Mixed" intent (shipped as Delver + ObserveRooms).

**One asset per type (2026-07-09):** `AdventurerSpawner.Def()` returns the
first list entry matching the enum value, so `adventurerTypes` must hold
exactly one definition asset per type. Variant assets (GuardDef) live only in
their dedicated slots (`guardDef`), never in the main list. A shipped bug had
GuardDef shadowing Mercenary from element 2; repaired by removing it from the
list.

## 3. Behaviour Traits and Combat Classes

Status: SHIPPED. Verified: 2026-07-09.

Each member independently rolls a `BehaviourTrait`; a party mixes traits.
Retreat thresholds applied in `DungeonAdventurer.Initialise()`: Cautious 50%
HP, Balanced 30% (default), Aggressive 10%, Cowardly retreats on monster
sight (threshold unused). Traits, combat classes, and types are three
independent axes on the same adventurer. The six combat classes (Fighter,
Mage, Rogue, Cleric, Explorer, Tank) match the roadmap.

**Key files:** `Adventurer/BehaviourTrait.cs`,
`Adventurer/DungeonAdventurer.cs`.

## 4. Named Party Tracking (Nemesis Parties)

Status: SHIPPED (roadmap listed this as optional/TBD -- it is neither).
Verified: 2026-07-09.

A party becomes tracked automatically when it contains a named member (a
Hero) or when the player pins it from the Known Parties panel. A tracked
party that resolves with at least one survivor is recorded; after
`returnDelayDays` (**canon default 2** in-game days, serialized) it regroups
and redeploys -- survivors return as their exact selves, fallen members are
replaced by a fresh roll of the same type. A total wipe ends the nemesis.
Unpinning is refused for parties with a named member: **named nemeses are
permanent**. The climax's host party does not return as a nemesis. Hero names
cycle from a serialized pool. Persisted via `DungeonSaveData`.

**Key files:** `DungeonCore/TrackedPartyRegistry.cs`, `UI/PartyBanner.cs`,
`UI/PartyBannerManager.cs`.

## 5. Guild Assessment (Inspector + Grade System)

Status: SHIPPED. Supersedes the roadmap/dev-plan Inspector escalation design
(random Inspector, severe-findings timer, bribe). Any note calling the full
Assessor "future work" is stale -- scheduled arrival, re-grading cadence, and
grade-matched teams are live. Verified: 2026-07-09.

**As built:** once the assault reaches the adventurer stage,
`InspectorAssessor` sends the first Inspector as a herald, then re-sends on a
fixed cadence (**canon default 7 days**) to re-grade. `GradeSystem` surfaces
the hidden `DungeonRating` as a named tier ("Unremarkable" ... "Legendary"),
snapshotted at each assessment so matched teams face a stable grade.
Assessment is two-stage: a BACKEND rank (sizes responses and matched teams)
and a PLAYER-VISIBLE badge. An Inspector who leaves alive sets both. A slain
Inspector sets only the backend rank -- the Guild sends a Hero kill-team
(**default 3 guards**) to investigate the disappearance, and the badge stays
"Unassessed" until that team departs, when the grade is revealed. Matched
teams are held until the first assessment, then levelled and sized to the
assessed grade, with a chance to arrive under-strength ("fresh recruits").
The scheduled Assessor is the ONLY Inspector source (the spawner's own
Inspector roll is disabled in scene setup).

**Key files:** `DungeonCore/InspectorAssessor.cs`,
`DungeonCore/GradeSystem.cs`, `Adventurer/AdventurerSpawner.cs` (grade
scaling, dispatch helpers).

**Rejected:** bribe-to-cancel as the primary counter (the shipped
counterplay is reputation/standing and the slay-the-Inspector gamble).

## 6. Alignment Axis

Status: SHIPPED (System 3). Verified: 2026-07-09.

Dark (-100) to good (+100). Core affinity sets the starting lean: **Dark
-30, Light +30**. Canon action shifts (serialized defaults): slay a peaceful
or holy soul **-8**; slay a fleeing adventurer **-5**; slay an ordinary
combatant **-2**; accept tribute **-3** (tribute darkens); completed
pilgrimage **+6**; adventurer leaves alive **+2 flat** (never diminishes).
Loot walking out alive earns **+0.02 per gold**, diminishing as alignment
climbs -- gold buys redemption toward neutral but not sainthood; the flat
spare-lives gains carry the last stretch. Persisted.

**Key files:** `DungeonCore/AlignmentSystem.cs`.

## 7. Factions and Standing

Status: SHIPPED (System 4 foundation). Verified: 2026-07-09.

Four factions (Adventurers Guild, Holy Order, Mercenary Company, Cultists),
each with a continuous standing (**-100..+100**, neutral 0) and a sticky
escalation tier (**0..3**) that ratchets UP when standing crosses a negative
band and never decays on its own -- later systems lower it through deliberate
appeasement (Phase 5 faction payoffs). Standing moves on in-dungeon outcomes
(member slain lowers; pilgrimage or tribute raises the relevant faction).
The player sees a DISPLAYED snapshot refreshed at nightfall
(`OnNightStarted`) in the Faction Panel; live values keep moving underneath.

**Key files:** `Adventurer/FactionSystem.cs`.

## 8. Recurring Threat Events

Status: SHIPPED. The roadmap's "mid-game big bads" exist, with materially
different triggers than planned. Verified: 2026-07-09.

**Holy Order strike** (`HolyOrderStrike`): each dawn, if Notoriety is high
AND alignment is low, dispatch a crusade -- an ordained Hero leading Paladins
(Tank) and Clerics, all forced to Light affinity for holy flavour -- fire a
raid-start autosave, ratchet the Order's tier, then cool down and re-arm.
Staying dark and infamous draws larger crusades. Hunts the dark dungeon.

**Mercenary reprisal** (`MercenaryContract`): reads loot EXITING the dungeon
in satisfied adventurers' hands (not loot the core absorbs) over a rolling
window. Crossing the threshold issues an ultimatum with a countdown: choke
the outflow or pay them off before it lapses and they stand down for a while;
otherwise the assault marches (Tank-fronted, untinted -- economic, not
ideological). Each reprisal weathered grows the band and shortens their
patience. Hunts the generous dungeon -- the Holy Order's mirror.

**Noble retaliation** (`NobleRetaliation`): a Noble slain, or driven out in
flight rather than leaving freely, marks its house; the Guild tier ratchets;
after a delay an escalated Destroyer party arrives under the house banner,
led by a named kinsman. Pending grievances coalesce into one reprisal (the
freshest house leads). Strength scales with total nobles slain this run,
capped. A standing Guild grudge that recurs -- NOT a climax path.

**Wild monster event** (`WildMonsterEvent`): the wild-beast pressure event;
its escalated form is the climax's Empowered Beast face.

**Key files:** `DungeonCore/HolyOrderStrike.cs`,
`DungeonCore/MercenaryContract.cs`, `DungeonCore/NobleRetaliation.cs`,
`DungeonCore/WildMonsterEvent.cs`, dispatch helpers in
`Adventurer/AdventurerSpawner.cs`.

## 9. Endgame Climax (Diamond 3 Trial)

Status: SHIPPED. Verified: 2026-07-09.

When the core reaches the ascension threshold (**Diamond 3**), the dungeon's
own history picks its final trial: whichever mid-game threat it provoked most
(ties broken by most recent) returns escalated; a dungeon that provoked none
faces the threat its current profile best fits. Four faces: Holy Order ->
the Grand Crusade (named ordained Paladin leading Paladins + Clerics);
Mercenary -> the Iron Host (Tank-heavy sellsword army); King's Army -> the
King's Host (several named Heroes + royal guard; provoked by slaying nobles,
tracked via NobleRetaliation); Wild Beast -> the Empowered Beast (max-power
invader driving for the core; each breach flings it back to the entrance
with a screen flash -- only killing it ends the climax). Surviving the trial
opens the Diamond 3 -> God 1 ascension (god-core sandbox) and silences the
recurring threats for good. The climax host party is excluded from nemesis
returns.

**Key files:** `DungeonCore/EndgameClimax.cs` plus the four threat systems.

## 10. Assault Staging

Status: SHIPPED (the "Commoner Stage", System 5). Verified: 2026-07-09.

`WaveStageController` sequences the assault on a newly-breached dungeon:
wildlife first (the animal stage), then commoners, then proper adventurers --
the stage is derived fresh from the entrance's breach day and the calendar,
spawners query `AllowCommoners` / `AllowAdventurers`, and each handoff is
announced once in the wisp's voice. Opt-in: with no instance present the
allow-checks return true (legacy behaviour). Separately, the day the seeded
entrance is discovered is a grace day -- word spreads overnight and the first
wave arrives with the next dawn. Matched teams additionally hold until the
first Guild assessment (see entry 5).

**Key files:** `DungeonCore/WaveStageController.cs` (or adjacent manager
file), `Adventurer/AdventurerSpawner.cs`.

## 11. Tribute and GiftGivers

Status: SHIPPED. Verified: 2026-07-09.

A GiftGiver party (Cultists only) has a bearer drop a `TributeChest` near the
entrance on arrival. Tribute chests are never openable by adventurers or the
player: after a short dwell they are absorbed straight into the core's gold
pool, reusing the DroppedLoot coin-flourish. Accepting tribute shifts
alignment **-3** (dark) and raises Cultist standing.

**Key files:** `Adventurer/TributeChest.cs`.

## 12. Ambient Necromancy and Corpses

Status: SHIPPED (Phase 4). The Crypt / deliberate nemesis raise is NOT built
-- see entry 16. Verified: 2026-07-09.

Slain adventurers (and, later, humanoid monsters) leave a `Corpse`:
source-agnostic, registered in a static `Active` list, lingering **20s**
(serialized default) before fading unless raised; raising claims and consumes
it. Necromancy is per-`MonsterDefinition`: `isNecromancer` monsters scan for
corpses in `raiseRange` (**3** units), channel **1.5s** holding still,
cooldown **5s**, sustain at most `maxRisen` (**3**) minions at once; a raise
produces a random pick from `risenDefinitions` (e.g. Skeleton, Zombie) living
`risenLifetime` (**45s**) before crumbling for good.

**Not in code yet** (belongs to the Crypt design): a corpse-lifetime API
(`SetLifetime(0)`-style persistence), any named-hero exclusion from ambient
raising, and deliberate player-initiated raises. Do not reference these as
existing APIs.

**Key files:** `Monster/Corpse.cs`, `Monster/MonsterDefinition.cs`
(Necromancy header), `Monster/DungeonMonster.cs`.

---

## 12A. Influence Field, Push and Breach Recede

**As built:** territory is claimed cells on a 4-connected frontier.
`InfluenceField` floods cost-distance from the core cell (8-directional,
terrain-weighted) and the ambient creep claims the cheapest claimable cell
within the level's reach cap (`MaxReach`). Bedrock and uncleared chambers are
impassable; rivers cost heavily but remain passable.

**Push (`InfluenceChannel`, `BuildMode.Push`):** boundary-pressure inflation,
not a path. Frontier cells inside the corridor between the core cell and the
cursor accrue pressure -- strongest on the axis, tapering to the corridor edge,
and gated at the cursor distance so the swell stops at the cursor rather than
overshooting. On contact it fills concave notches, pulling the core edge out to
smooth the shape, then settles. Mana is spent per claimed cell scaled by that
cell's terrain resistance; the push ignores the reach cap. Off-corridor
boundary never grows. Superseded: the cheapest-path spine with lateral flanks,
and its tapered-pseudopod variant.

**Breach recede:** one radial rule. A breach reclaims claimed cells beyond
`BreachSafeRadius()` -- straight-line from the core, at `1 - pushedFringeLost`
of the domain's farthest claimed extent (default: outer 20% lost) -- and
nothing else. Ground around the core is never reclaimed. Reclaimed cells still
inside the reach cap regrow via creep as `EffectiveReach` recovers; reclaimed
cells beyond it were pushed, and stay gone. Superseded: the rule that unclaimed
the band between suppressed reach and the reach cap, which carved an unclaimed
ring around the core.

**Overlay (`O`):** three tiers, driven from the same radius -- brightest over
unclaimed ground within reach (where creep will fill), mid over claimed
breach-durable ground, dim over the exposed fringe. `InfluenceRingRenderer`
bakes the exposed flag into the field texture's B channel from
`BreachSafeRadius()`, so what reads as exposed is exactly what a breach takes.

**Key files:** `DungeonCore/InfluenceField.cs`,
`DungeonCore/InfluenceChannel.cs`, `DungeonCore/TileInfluenceManager.cs`,
`DungeonCore/InfluenceRingRenderer.cs`, `Shaders/InfluenceRing.shader`.

---

# PART II -- DESIGNED, NOT YET BUILT

Nothing in this part exists in code, with one exception: entry 14 has
shipped and is recorded as-built in place. Entries record APPROVED design;
build guides must still verify live source before writing edits.

## 13. Research Tree (Phase 4.5)

Decided: four paths -- Observation (UX/info gating), Architecture (building
unlocks), Bestiary (monster unlocks), Sorcery (active spells; ONLY if Phase 5
core spells are greenlit -- drop the path entirely otherwise). Currency:
Library rooms passively generate research points with diminishing returns on
multiple Libraries. Bootstrap: Bronze 1 starts with Skeleton, Spike Trap, and
Status Bars unlocked (the core "remembering" its powers). UI is DK2-style:
tree shape visible from the start as greyed icons, node names hidden until
one prerequisite away; buried-skeleton and book-drop discoveries stay genuine
surprises. Visible nodes show pattern requirements plus an atmospheric
location hint. Cross-path prerequisites at higher tiers force breadth. God 1
ascension requires a top-tier node from every path (or a super-node requiring
all paths). Core type affinity gives a 50% discount on matching-affinity
nodes -- points only; pattern requirements are never discounted. Room
upgrades gate through the existing `RoomAnchor.UpgradeGate` hook.

Forks resolved (spine shipped; see key files below): purchase model is
TIMED PROJECTS -- spending the points starts the project, which runs
`durationDays` and completes at dawn (the instant-spend recommendation was
considered and declined). One active project plus a queue of one; the queued
project is pre-paid and promotes free. Cancelling (active or queued) refunds
the full point cost; progress is lost. Observation retro-gating is Option C:
status bars and damage numbers are free forever (never nodes); the
researchable intel ladder starts at wave preview (wired in the roster
session). Income lands at dawn: every valid Library on every floor
contributes magnitude x tier, ranked largest-first through serialised
diminishing multipliers (1.0 / 0.5 / 0.25, then a 0.10 floor). Extra
Libraries beyond the first also speed the ACTIVE project (+10% each, capped
+30% -- income and speed are separate levers). Affinity halves point cost
only. Bootstrap nodes (Remembered Bones, Remembered Spikes) unlock on new
game via `bootstrapUnlocked`; they re-lock behind the tutorial wisp when the
prologue lands. Loot books route through `GrantNodeFully` (bypasses points,
prerequisites AND duration; refunds if underway). Node keys are
`tech.<id>` with an `overrideKey` field reserved for the legacy bare keys.
The spine registers `RoomAnchor.UpgradeGate` from per-node `upgradeGates`
entries. Research points live on `DungeonCore` beside gold and persist in
`DungeonCoreSaveData`; project state persists additively on
`DungeonSaveData`. Key files: `Gameplay/TechNodeDefinition.cs`,
`Gameplay/TechTree.cs`, `Gameplay/ResearchController.cs`,
`Editor/TechContentGenerator.cs`. Tree UI and the node roster are the next
sessions.

## 14. Material Pattern System

Status: SHIPPED (graduated from Part II; recorded as-built in place).
Verified: 2026-07-09.

As built: boolean discovery flags ("patterns") -- no stockpile, no crafting
sim; the core reconstitutes materials from mana. Flags live in `UnlockState`
under a `pattern.` key prefix; definitions are `PatternDefinition`
ScriptableObjects listed in one `PatternCatalog` asset (18 patterns: 6
terrain, 8 loot-band, 4 reserved). Live channels: terrain first-claim
(deterministic, hooked in `TileInfluenceManager.ClaimTile`'s non-silent
block; Bedrock teaches nothing) and adventurer loot (rolled in
`DroppedLoot.Absorb` against serialised per-rarity chances
10/20/35/60/100%; expected drops to finish a band = band size / chance;
exhausted bands fizzle silently -- the trader stays the designed catch-up
valve; tribute coin flourishes roll as Common). Trader, avatar and event
channels are reserved catalog entries only. Learned-from notes persist per
pattern. Persistence: additive `unlockedKeys` + `patternNotes` on
`DungeonSaveData`, restored in `DungeonSaveController` with a silent terrain
catch-up that also heals legacy saves; the existing tech keys ride the same
field, fulfilling the "persistence lands with Laboratory" promise. The
Materials HUD panel is now the Pattern Codex (silhouetted unknowns show
their source hint; the collapsed chip counts discoveries); gold display
moved to the Level Panel. Class loot assets were re-authored from single
tint-era entries into weighted rarity ladders (see
`ScriptableObjects/Adventurers/Classes`). Pattern gating still concentrates
on the Architecture path only; loot books still grant nodes fully,
INCLUDING pattern requirements (unchanged, for the tree build).

Key files: `Gameplay/PatternDefinition.cs`, `Gameplay/PatternCatalog.cs`,
`Gameplay/PatternDiscovery.cs`, `UI/PatternCodexUI.cs`,
`UI/PatternCodexRow.cs`, `Editor/PatternContentGenerator.cs`.

## 15. Room Effects v2 and Attractor Rooms

Decided: Treasury gold cap, Library research-point generation, Spawn Chamber
respawn behaviour as new room effects. (Library research-point generation
shipped early with the research spine -- `RoomEffectType.LibraryResearch`,
granted at dawn by `ResearchController`, not the per-tick effect loop.)
Attractor rooms add weight to adventurer-type spawn rolls, 
summed across all floors: Shrine -> Pilgrims,
Library -> Scholars, Treasure Vault -> Treasure Hunters, Throne -> Nobles.

Rejected: attractors as hard spawn guarantees (they are additive weights).

## 16. Crypt and Deliberate Nemesis Raise

Decided: the Crypt room preserves NAMED corpses indefinitely; named hero
corpses linger until end of raid/day even outside a Crypt; named corpses are
EXCLUDED from ambient auto-raise; raising a named hero is a deliberate player
action (click + confirm) costing mana and Hero-tier monster capacity (~25),
and is permanent once done. Requires new corpse APIs (persistence /
`SetLifetime(0)`-style, named flags) that do not exist yet -- see entry 12.
This supersedes the backlog's "Crypt: passive notoriety reduction, requires
sarcophagi" framing entirely (sarcophagi may remain as furniture flavour).

## 17. Discovery Content (Buried Skeletons, Loot Books, Wisp Guide)

Decided: buried skeletons are a Bestiary discovery -- dig-to-unlock matched
to core type (Dark core = undead skeletons, Earth core = dwarven skeletons).
Adventurer loot books (Scholars and literate types) drop tomes that unlock
tree nodes or research paths, bypassing prerequisites; tome flavour matches
the node. Wisp guide (folds in later): a
`QuestController.ProgressObjective` increment API enabling `TalkNPC` (hooked
in `NPC.StartDialogue`) and `Custom` objectives (optional field on
`FlagInteractable`).

## 18. Phase 5 Designs

Decided so far: Holy Ground desecration is designed INTO the Holy Order
trigger -- Holy Ground patches are procgen via `TerrainTypeMap`, desecrating
one is unsealing a Church seal and feeds the trigger; recommended reward is a
buried-skeleton Bestiary discovery. Alert severity tiers
(info/warning/critical). Faction payoffs as the mid-game gold sink and the
deliberate way to lower escalation tiers (see entry 7). Core spells / active
abilities are NOT yet greenlit; the Sorcery research path hangs on that call.

## 19. Buried Age Sites and Tier-Up Audiences

Decided (unscheduled): pre-carved unclaimed tunnels ship inert first, flank
behaviour added as a separate later step. Ancient sites: Sunken Plaza,
Collapsed Archive, Ossuary, Broken Aqueduct, Hollow Sanctum, Sealed Gate.
Depth-as-time: deeper floors = older era = richer sites. Tier-up divine
audiences at Bronze -> Silver -> Gold -> Diamond -> God milestones; the god
of the core's type grants knowledge, feeding deep-faith lore.

---

# PART III -- LORE CANON

## 20. Why Holy Sites Are Underground

Confirmed: the dead go down; the old deep-faith taught that divinity resides
below and that some dead are reborn as dungeon cores -- the player among
them. Deep shrines venerated the divine below and warded rebirth sites. The
modern Church suppressed the deep-faith as heresy; Cultists are its surviving
remnant. Holy Ground patches are Church-maintained seals; desecration is
unsealing. This explains Pilgrims worshipping at cores, the Cultists, the
Holy Order's hatred of Dark cores, buried shrines, and the game's title.

## 21. The Buried Age

Approved: the deep-faith's civilisation was entombed in a cataclysm. Ancient
sites are ruins of the faith that venerated cores -- welcoming, with no
desecration penalty -- whereas Holy Ground is a Church seal, hostile ground.
Two flavours of sacred underground, one axis of history: deeper is older.

---

*Seeded 2026-07-09 against repo HEAD. Amend via guide chapters only.*
