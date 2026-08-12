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
- **Part I** and **Part IV** are shipped and verified against source; Part IV
  holds the entries that landed after the file was first split, and is no
  less as-built than Part I. **Part II** is design decided but not necessarily
  built -- check each entry's status line before assuming its classes or APIs
  exist. **Part III** is lore canon. The **Appendix** is last.
- Numbering runs in file order throughout: 1-19, then 20-21, then 22-33, then
  A-B. A letter suffix (8A, 13A, 28A) means the entry sits directly after the
  number it extends.
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
8A. Notoriety Model (Gain Shaping, Tier Gates, Decay)
8B. Prison and Captives (Capture, Verbs, Starvation)
8C. Capture Traps and Trap-Pin Rescue
8D. Faction Reaction to Held Captives (Rescue Party, Ransom-Bearer)
9. Endgame Climax (Diamond 3 Trial)
10. Assault Staging
10A. Squad Formations (March-Holding, Effects, Breaking)
11. Tribute and GiftGivers
12. Ambient Necromancy and Corpses
12A. Influence Field, Push and Breach Recede

**Part II -- Design entries (most since shipped in place)**
13. Research Tree (Phase 4.5)
13A. Guided Opening (TutorialDirector)
14. Material Pattern System
15. Room Effects v2 and Attractor Rooms
15A. Monster Muster (Spawn Rooms, Posts, Floor Gates)
16. Crypt and Deliberate Nemesis Raise
17. Discovery Content (Buried Skeletons, Loot Books, Wisp Guide)
18. Phase 5 Designs
19. Buried Age Sites and the Deep Roads
19A. Tier-Up Divine Audiences

**Part III -- Lore canon**
20. Why Holy Sites Are Underground
21. The Buried Age

**Part IV -- Later shipped systems**
22. Deeds
23. Trophy Hall
24. Surface World: Radial Forest Bands
25. Camp Growth, Identity & Effects
26. The Surface War
27. Bestiary Expansion
28. Boss Promotion (Rank-on-Spawner, Waiting Halls)
28A. The Wandering Merchant (Trader Channel)
29. Wisp Quests (Urgings) and the Pressed Rule
30. Ranged Combat (Projectiles, Damage Kinds, LOS)
31. The Trapworks (Roster, Type Exclusivity, Trapwright)
32. The Living Prologue (Town, Forest, Ceremony)
33. Monster Target Priority (Class-Aware Targeting)
34. The Core's Own Past (Persisted Life, Memory Echoes)
35. Monster Mutations (Bestiary upgrade line)
36. Built Walls and the Sealed Way
37. Random World Events (The World's Weather)
38. Core Spells (Active Abilities)
39. The Pause Rule (Availability Audit)
40. The Panel Button Row (and the Completed Availability Sweep)
41. Spell Charges
42. Dens and the Deep Occupants (Decision Record)

**Appendix** (at the end of the file)
A. Content Registries and Authoring Keys
B. Sorting Layer Contract
C. Camera Bounds Contract
D. Execution Order Contract

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
furniture counts, boss-spawner presence (`requiresBossSpawner`). There is no
GLOBAL upper size cap in footprint mode; per-room `maxTileCount` (0 =
uncapped) is enforced when set -- the Core Chamber caps at 49. Validation failures surface via
`LastFailReason`; state changes fire `OnRoomValidationChanged` (tint + toast
systems listen).

**Room types are data:** `RoomDefinition` ScriptableObjects (Create ->
Dungeon -> Room Definition). New room types need no code. Reference minimums
from the definition comment: Library 12, Barracks 9, Shrine 9, Oracle Chamber
12, Boss Room 20 (+ boss spawner). Throne Room uses `requiresCore` (must
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

**TechNode unlock on validation (as built):** a room may GRANT an unlock,
not just be gated by one. `RoomDefinition.techNodeUnlockKey` (distinct from
`requiredTechKey`, which gates the room INTO the picker) names the key the
room grants. On the room's first valid state, `RoomAnchor.Revalidate` calls
`GrantTechNodeIfAny`: if the key resolves to a `TechNodeDefinition` (via
`ResearchController.Tree.GetByKey`) it routes through `GrantNodeFully` (wisp
announce + point refund if the node was mid-research), otherwise it flips
`UnlockState.Unlock` directly. The grant is PERMANENT -- never re-locked when
the room breaks or is torn down -- and persists via the existing
`unlockedKeys` save field (no new schema). Grants are suppressed while
`DungeonSaveController.IsLoading`, and the `IsUnlocked` guard makes repeated
validations free. This is the room build-grant the research entry reserved as
an "alternate route"; the framework ships here, content assignment (Oracle
Chamber -> `oracle_chamber`) lands with the Oracle work.

**Oracle Chamber (as built):** the foresight room -- min 12 tiles, maxTier 3,
one `RaidForesight` marker effect, `techNodeUnlockKey = oracle_chamber` (grants
intent-reveal on first validation via the build-grant above), `requiredTechKey`
empty (buildable from the start, like the Library). It is the information room:
while valid it reads the coming raid in the wave-preview HUD (see entry 15).
Registered in `RoomDefinitionRegistry`. Supersedes the roadmap/backlog framing
of the Oracle Chamber as a pure intent-tooltip room -- the tooltip is the
`oracle_chamber` unlock (entry 2); the room's standing effect is foresight.

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

**Anchor lifetime (2026-07 smoke-test fix).** A room anchor never persists in an
unusable state. `RoomTypePickerUI.Close` is the single choke point for every
dismissal path; an anchor dismissed with no type assigned is destroyed there. An
anchor given a type that fails validation where it was drawn (a Core Room by the
entrance, a room under its minimum size) is destroyed in `OnEntryClicked` right
after the failing `SetRoomType` -- the toast already reported the failure, so no
stranded footprint is left occupying ground it can never use.

**Boss room spawner circle (2026-07).** The boss spawner can only be placed
inside a VALID boss room, but a boss room requires the spawner to validate --
a deadlock. Resolved by waiving the spawner requirement at the anchor:
`RoomAnchor.Revalidate` passes `ignoreBossSpawner: AssignedRoom.requiresBossSpawner`,
so a correctly-sized boss room validates on size alone and survives the
delete-on-invalid check with no spawner yet placed. Driven off the
`requiresBossSpawner` flag, not the room name, so a second spawner-gated room
inherits it. The boss room's only effect -- faster boss respawn -- needs no
separate gating: `RoomEffectCensus.GetRespawnMultiplier` returns the bonus only
for a spawner standing in the room's tiles, so a spawner-less boss room hastens
nothing.

**Demolition (2026-07).** One mode, `BuildMode.Demolish`, removes furniture,
chests, traps and room anchors, priced at half the placement mana (chests refund
flat regardless of opened state, since they re-arm between raids; room anchors
cost nothing to place and refund nothing). Priority on a shared cell is
furniture, then chest, then trap, then the anchor owning the cell. Monster
spawners are deliberately excluded -- they keep selection-driven removal through
MonsterCommandUI because removal also returns creature capacity. `TrapPanel` is
read-only as a result; its former per-entry remove button is retired.

**Mine gestures (2026-07).** Three: Single (click, Shift to queue), Drag (paint
along the path), Box (rectangle, previewed live, queued on release). Chosen from
a Mine sub-menu mirroring the Build sub-menu, persisted in PlayerPrefs under
DCR.MineGesture, defaulting to Single. Box reuses the room-drag SpriteRenderer
quad pool so both rectangle gestures render identically.

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
GiftGiver is Cultist-only. Intent is hidden until the Oracle Chamber unlock
(`UnlockState.OracleChamber` / node key `oracle_chamber`); until then it is
hinted through behaviour. As built, the unlock is granted by EITHER route --
researching Whispers of Intent, or building and validating an Oracle Chamber
(its `RoomDefinition.techNodeUnlockKey` = `oracle_chamber`, granted on first
validation via the room build-grant, see entry 1). Once understood, intent is
revealed in two places, both gated on the same key: a cursor tooltip on
adventurer hover (`UI/AdventurerIntentHover.cs`, reusing the shared
TooltipController) and the Intent line of the click inspector
(`AdventurerStatsPanel`, which reads `???` until unlocked). Labels come from
`AdventurerIntentHover.IntentLabel`; all four intents are covered
(Delver included).

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
fixed cadence (**canon default 7 days**) to re-grade. On each arrival the
view glides to the Inspector on unscaled time while the notice holds the
clock, releasing on dismissal (Day-34 camera lift). The Inspector and its
Delver escort are passive: they observe without starting a monster fight,
though any blow provokes the whole party; on a peaceful departure the wisp
announces the Guild's ranking. `GradeSystem` surfaces
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

**Escort typing (2026-07 smoke-test fix).** The Inspector's escort is
Delver-typed via `AdventurerSpawner.escortGuardDef` (the `Escort` asset,
type Delver), as are Noble and Scholar escorts: a party that came to assess,
study or gawk does not bring guards carrying the `BreachCore` goal. Destroyer
dispatches keep Mercenary muscle through the original `guardDef` -- the Hero
kill-team is unchanged, and the Holy Order strike keeps its own roster. Kill
attribution is unaffected: `FactionForKill` already routes escort guards to the
Guild, and a Delver maps there anyway. `escortGuardDef` falls back to `guardDef`
when unset, so an empty slot degrades to the old Mercenary behaviour rather than
spawning nothing.

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

Status: SHIPPED (System 4 foundation). Verified: 2026-07-09. FIFTH FACTION
(Dwarves) added with the outpost -- see the sub-section at the end of this
entry and entry 19.

Four factions (Adventurers Guild, Holy Order, Mercenary Company, Cultists),
each with a continuous standing (**-100..+100**, neutral 0) and a sticky
escalation tier (**0..3**) that ratchets UP when standing crosses a negative
band and never decays on its own -- later systems lower it through deliberate
appeasement (Phase 5 faction payoffs). Standing moves on in-dungeon outcomes
(member slain lowers; pilgrimage or tribute raises the relevant faction).
The player sees a DISPLAYED snapshot refreshed at nightfall
(`OnNightStarted`) in the Faction Panel; live values keep moving underneath.

**Key files:** `Adventurer/FactionSystem.cs`.

**Faction intel (as built):** each faction is first-seen when a member enters
(`AdventurerParty.RegisterMember` calls `FactionIntel.NotifyEncounter`, setting
`encounter.<slug>`). Two Observation-path research nodes -- Study the Holy
Order (`faction_intel.holy_order`) and Study the Mercenary Company
(`faction_intel.mercenaries`) -- are KeyUnlocked-visible on those encounter
flags and prereq'd behind Ledger of the Fallen. Completing one reveals that
faction's profile, tactics and dispatch roster (`FactionSystem.PoolFor`) as an
intel block in the existing Faction panel (an `IntelLabel` row child); the
roadmap's separate "intel panel / profile panel" is served in-panel.
`FactionIntel` (`Gameplay/FactionIntel.cs`) holds the slugs, keys and text.
Rejected: stand-alone per-faction windows (the panel already reserves the
intel slot).

### The Deep Holds -- the fifth faction (SHIPPED)

`FactionId.Dwarves` is APPENDED at index 4. The enum serialises into
`FactionRelationSave`, so it may never be reordered. Display name: **Dwarven
Holds**.

They are unlike the other four in three ways that are all deliberate:

- **They start positive.** `dwarvesStartingStanding` is **+15** -- neutral-
  CURIOUS, not friendly. Seeded in `Awake`, and seeded AGAIN in
  `RestoreFromSave` when a loaded save carries no Dwarves record, because an
  older save would otherwise restore them to a flat zero.
- **They dispatch nobody.** `PoolFor` returns empty and that is correct; the
  panel's intel block drops the "Dispatches:" line for them rather than
  printing an empty roster.
- **They carry REGARD, not just a tier.** The escalation tier ratchets one way
  and only measures how badly a faction wants the core dead, which says nothing
  above zero. Regard is the positive half: steps at **+25 / +50 / +80** named
  *Curious / Tolerated / Trusted / Kin*, reversible, no ratchet. The panel row
  shows regard while the tier is 0 and reverts to the ordinary escalation text
  the moment the tier ratchets -- so the same slot always shows whichever half
  currently means something.

**Panel visibility.** The row is HIDDEN until `FactionIntel.Encountered
(FactionId.Dwarves)`, set by `DwarvenOutpostController` when the outpost is
first revealed. Listing them from day one would advertise a floor-index-2
set-piece hundreds of days before a Diamond core can reach it.

**Faction-vs-faction.** The Living Holds (entry 19, step 7) wrote the Deep
Holds' first matrix edge: Dwarves <-> Holy Order is Hostile -- the deep-faith
reading (entry 20: divinity below, the Church's burning judgment above) made
mechanical the day shipped systems could exercise it. Two now do: patrols
withdraw toward home when a Holy Order adventurer comes near, and caravans
hurry past them at 1.5x -- so the relationship is a claim play can show
wrong. Deliberately NOT Allied to the Cultists: sharing an enemy of the
Church is not sharing a cause with people who worship what the Holds merely
live beside. Everything else stays Neutral; strengths stay 8 across the
board.

**Key files:** `Adventurer/FactionId.cs`, `Adventurer/FactionSystem.cs`,
`Adventurer/FactionRelations.cs`, `Gameplay/FactionIntel.cs`,
`UI/FactionPanel.cs`.

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

## 8A. Notoriety Model (Gain Shaping, Tier Gates, Decay)

Status: SHIPPED. Notoriety was a near-monotonic kill counter (+5 flat per
non-suicidal kill, drained only 0.1/s after a 10s idle) that saturated the
hardest ambient content within ~15 kills, long before the dungeon's tier
warranted it. This pass re-couples the difficulty ceiling to progression.
Verified: 2026-07-20.

**Gain shaping (soft cap).** A kill's notoriety is earned through
`DungeonCore.AccrueKillNotoriety()` (called from `DungeonAdventurer.Die`),
which scales the base `killNotoriety` (5) down as notoriety approaches
`notorietySoftCap` (100), never below `notorietyMinGainFraction` (0.15) of
the base. Kills near the cap add ~0.75 instead of 5, so the legend plateaus.
Drains (pilgrim calm, satisfied looters), scripted raises (Crypt +15) and the
trophy trickle bypass the shaping and call `AddNotoriety` directly. Per-party
notoriety tallies read the shaped amount `AccrueKillNotoriety` returns.

**Tier gates on the ceiling.** Ambient Heroes now require BOTH the notoriety
threshold (`heroNotorietyThreshold` 60) AND a dungeon-tier floor
(`heroTierFloor`/`heroTierRankFloor`, default Silver 4 = mid Silver) via
`AdventurerSpawner.HeroesUnlocked()`; a sub-mid-Silver dungeon never rolls a
Hero on kill-count alone. The fastest spawn band (`intervalHigh`) likewise
requires `fastSpawnTierFloor`/`fastSpawnTierRankFloor` (default Silver 4).
These gates affect the RANDOM party roll ONLY -- every scripted Hero dispatch
(Inspector kill-team, Holy Order crusade, Noble retaliation, the Diamond 3
climax hosts) builds its party directly and ignores the gate, so a slain
Inspector still summons its kill-team at any tier.

**Decay tuning.** The heat gauge now recedes meaningfully when the dungeon
lies low: recommended scene values `notorietyDecayPerSecond` 0.4 (was 0.1)
and `notorietyDecayCooldown` 6s (was 10s), still dampened by Cultist camps
(canon 25). Structure unchanged; only the magnitudes moved.

**Design forks settled (2026-07-20):** notoriety reads as a receding heat
gauge (not an accumulating legend); the ceiling is tier-gated; diminishing
gain plus faster decay via tuning, with no new player-facing sink this pass;
the trophy trickle stays an uncapped upward pull by design (the player's
opted-in trade-off). An active drain sink (prisoner/capture release) is the
noted candidate to revisit if agency over notoriety is wanted later.

**Key files:** `DungeonCore/DungeonCore.cs` (`AccrueKillNotoriety`, gain-
shaping fields, decay), `Adventurer/AdventurerSpawner.cs` (`HeroesUnlocked`,
tier-floor fields, interval gate), `Adventurer/DungeonAdventurer.cs` (`Die`
earns through `AccrueKillNotoriety`).

**Rejected this pass:** flat +5 per kill (superseded by soft-cap shaping);
pure-notoriety hero/spawn gating (superseded by the tier floors); a buildable
notoriety sink this session (deferred -- tuning solved the reported problem).

## 8B. Prison and Captives (Capture, Verbs, Starvation)

Status: SHIPPED. Capture is the player's active hand on notoriety, the
agency deliberately deferred in the 8A tuning pass. The reactive layer that
was pending here has since shipped in full -- see 8C (capture traps,
trap-pin rescue) and 8D (faction rescue party, ransom-bearer).

**Capture.** `DungeonAdventurer.TryCapture()` pre-empts `Die()`. A beaten
adventurer is taken alive when `PrisonController.TryImprison` finds a free
Cell inside a valid Prison. Exempt always: Hero (by rule), Inspector (the
assessment must run its course), Suicidal (death is what they came for).
The capture path deliberately skips every kill report -- no
`AccrueKillNotoriety`, no `FactionSystem.RegisterKill`, no
`AlignmentSystem.OnAdventurerKilled`, no corpse, no `AdventurerDeaths++`,
no `OnAnyAdventurerSlain`. XP still lands: the core overcame them either way.

**Capacity is cells, and cells are the opt-in.** There is no prisoner budget
and no toggle. Capture fires only when a free Cell stands in a valid Prison,
so a player who builds none sees the pre-capture behaviour unchanged. This
supersedes any notion of a separate holding budget or a monster-budget share.

**The verbs** (`PrisonController`, all via `PrisonerPanelUI`):
Release drains notoriety (default 8) and shifts alignment toward the light
(4). Execute raises notoriety (8), darkens alignment (6) and leaves a corpse
-- named captives keep their name, so the Crypt can gather and raise them,
making capture a controlled input to the nemesis loop. Interrogate unlocks
`FactionIntel.IntelKey` for that captive's banner, once per faction, and the
captive survives the reading. A captive left unprocessed starves after
`starveDays` (5) dawns and leaves a corpse. A Prison that falls out of
validity loses its captives at the next dawn -- a broken gaol keeps nobody.

**Room and marker.** New `RoomEffectType.PrisonHousing`, appended at the enum
tail so existing ordinals do not shift; a marker only, skipped by
`RoomEffectController`'s per-second tick exactly like `CryptPreservation`.
Prison = Room Definition (min 9 tiles, 1 Cell required, `requiredTechKey`
`tech.prison`); Cell = Furniture Definition whose `furnitureName` string
"Cell" is load-bearing for saves.

**Save.** `PrisonerSaveData` per floor (name, type/class ordinals, class
label, named flag, days held, cell). Captives record their CELL rather than
their world position, since the held sprite carries a visual lift that would
round to the wrong tile. Restore runs after the furniture pass.

**Design forks settled:** capture via subdue-on-defeat and (guide 2)
capture-traps, never monster escort; no Convert/Recruit; capturable = every
type but Hero/Inspector/Suicidal; passive starvation rather than active
guarded escape; capacity = cell availability only.

**Pending (guide 2, the reactive layer):** capture-traps that pin an
adventurer in place with a rescue window for their surviving party; faction
rescue parties that target the Prison for high-value captives; the
ransom-bearer. Lore rule established: the core never negotiates outward --
the world learns of a held captive only because an escapee carried word, and
responds by sending either a bearer or a raid.

**Key files:** `Room/Prisoner.cs`, `Room/PrisonController.cs`,
`UI/PrisonerPanelUI.cs`, `Adventurer/DungeonAdventurer.cs` (`TryCapture`),
`Room/RoomDefinition.cs` (marker), `Room/RoomEffectController.cs` (skip),
`Save/DungeonSaveData.cs`, `Save/DungeonSaveController.cs`,
`UI/PauseMenuController.cs` (Esc ladder).

**Un-binned:** prisoner/capture was a Day-34 rejection and is now shipped.
Squad formations was also un-binned and has since shipped in full -- the
light muster formation plus the complete tactical layer (march-holding,
formation effects, formation-breaking). See entry 10A.

## 8C. Capture Traps and Trap-Pin Rescue

Status: SHIPPED. The second capture route beside subdue-on-defeat, plus the
rescue tension that makes dungeon layout matter. The faction reactions that
were pending here have since shipped -- see 8D.

**The Capture Trap.** A `TrapBehaviour.CaptureTrap` (class `CaptureTrap`, a
`PitfallTrap` sibling). On trigger it calls `DungeonAdventurer.BeginPinned`
rather than dealing damage: the victim enters the new `Pinned` state, halted
in place for `captureHoldSeconds` (default 10). The uncapturable
(Hero/Inspector/Suicidal, via `CanBeSubdued`) are never pinned -- they take
the trap's slow and walk on. Wild monsters are never snared. The trap needs
NO research gate: with no free Prison cell to secure into, a snared
adventurer merely struggles free after the window, so the Prison (canon 8B)
is the real gate on the trap's value.

**Trap-pin rescue.** While a member is `Pinned`, the party's other living
members -- those in a free state, not mid-fight, fleeing or on the stairs --
converge via the new `MovingToRescue` state (built on the `MovingToRoom`
targeted-path pattern). A Cowardly member may abandon the attempt
(`cowardAbandonChance`, default 0.5). A rescuer must REACH the pin cell
alive to free the ally; your monsters block by fighting the rescuers en
route, so a trap sunk in a defended kill-box is the counter. Monsters ignore
the pinned captive themselves (target predicates skip `IsPinned`, and a
target that becomes pinned mid-attack is dropped) -- otherwise a guarded trap
would kill captures instead of securing them.

**Resolution.** Rescued: the ally is freed wounded (`rescuedHpFraction`,
default 0.4) and the whole party gains a grudge -- `MarkGrudge` sets
`tracked` (it returns as a nemesis) and raises its morale-fracture threshold
so it breaks less easily. Not rescued (timer elapses, or no living ally
remains): `PrisonController.TryImprison` secures them into a free cell with
no death reported -- the same bookkeeping as a subdue capture. Every cell
full: they wrench loose and resume.

**Persistence.** A pin is transient (~10s), so no save-schema change was made:
`Pinned` and `MovingToRescue` fall back to `MovingToCore` on restore, exactly
as `UsingStairs`/`Disarming`/`Organizing` already do.

**Design forks settled:** capture via subdue-on-defeat (8B) and this trap,
never monster escort; who diverts = free members converge, cowards may bail;
grudge = nemesis return + fracture-resistance; rescue succeeds by reaching
the trap alive (layout is the counter), not a radius test.

**Pending (guide 2b):** the faction rescue party that targets the Prison for
high-value captives, and the ransom-bearer. Lore rule (from 8B): the core
never negotiates outward; the world learns of a held captive only via an
escapee's report and answers with a bearer or a raid.

**Key files:** `Traps/CaptureTrap.cs`, `Traps/TrapDefinition.cs` (behaviour
+ `captureHoldSeconds`), `Traps/TrapBase.cs` (factory),
`Adventurer/DungeonAdventurer.cs` (`Pinned`/`MovingToRescue`, pin & rescue),
`Adventurer/AdventurerParty.cs` (pin tracking, `grudge`),
`Monster/DungeonMonster.cs` (skip the pinned).

---

## 8D. Faction Reaction to Held Captives (Rescue Party, Ransom-Bearer)

Status: SHIPPED (guide 2b -- completes the prison arc). The outside world's
answer to a held high-value captive. Closes the lore rule established in 8B:
the core never negotiates outward; the world learns of a captive only through
an escapee's report, and answers with a raid or a bearer.

**Trigger.** `PrisonController` subscribes to `AdventurerParty.MemberEscaped`.
When an adventurer escapes and a high-value captive is held (Noble, Hero, or
named -- `IsHighValue`) with no answer already pending, a reaction is scheduled
for `reactionMinDays`..`reactionMaxDays` (default 3-6) dawns hence. At the due
dawn (`TryDispatchFactionReaction`, run from `HandleDawn`), if a high-value
captive still waits, the faction answers; otherwise it fizzles. The schedule is
single-shot -- it re-arms only on a fresh escape.

**Which answer (locked fork).** By faction: `FactionId.MercenaryCompany` sends
a ransom-bearer (they deal in coin); every other faction sends a rescue party.

**Rescue party** (`AdventurerSpawner.DispatchPrisonRescue`). Mustered at the
entrance like a Noble retaliation (a Hero champion + a Tank-fronted retinue),
then every member is given `SetRaidTarget` -- the new `AdventurerGoal.FreePrisoner`
plus the captive's cell position and floor. `RefreshPath` sends `FreePrisoner`
raiders to the cell rather than the core, across floors via the same stair logic
the core-beeline uses; your monsters block them en route (they are ordinary
hostiles). On arrival, `AttemptBreach` frees the nearest held captive
(`PrisonController.BreachNearest`) and the raider turns to retreat. A captive
freed by force is NEUTRAL -- removed, word spreads, no notoriety either way. The
counterplay is layout: a Prison sunk behind guards keeps its prisoners.

**Ransom-bearer** (`DispatchRansomBearer`). A lone, peaceable Commoner-emissary
with `GiftGiver` intent and `SetRaidTarget(ransom = true, ransomGold)`. It walks
to the cell; `AttemptBreach` on arrival pays the core `ransomGold` (default 300)
via `AddGold` and takes the captive. Two behaviour changes make this work: a
raid-goal Commoner does not panic-flee near monsters, and non-aggressive monsters
now spare all `GiftGiver`-intent bearers (not only Pilgrims), so the bearer
reaches the gaol and the deal closes by default. Raising the aggression stance to
Aggressive lets monsters cut the bearer down -- the way to refuse and keep the
prisoner. (Side effect, by design: worshippers' tribute-bearers now also arrive
unharmed under a non-aggressive stance.)

**Freed captive (locked fork -- simple representation).** Breaching removes the
`Prisoner` token and raises an alert; no live fleeing adventurer is reconstructed.

**Persistence.** One additive int, `DungeonSaveData.prisonReactionDay` (-1 = none),
mirroring the wandering-merchant schedule; the dispatched party persists via
`liveParties` as any live party does.

**Key files:** `Adventurer/AdventurerType.cs` (`FreePrisoner`),
`Adventurer/DungeonAdventurer.cs` (raid targeting, `SetRaidTarget`,
`AttemptBreach`), `Adventurer/AdventurerSpawner.cs` (`DispatchPrisonRescue`,
`DispatchRansomBearer`), `Room/PrisonController.cs` (schedule, dispatch,
`BreachNearest`), `Monster/DungeonMonster.cs` (spare gift-bearers),
`Save/DungeonSaveData.cs` + `Save/DungeonSaveController.cs` (`prisonReactionDay`).

**Prison arc complete:** 8B (capture, verbs, starvation), 8C (capture-trap +
trap-pin rescue), 8D (faction reaction). The squad-formation tactical layer
named here as the next major work has since shipped in full -- see 10A.

---

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

Every party now takes a shared muster window at the mouth before advancing
together: formations hold their slots as before; formation-less parties mill
loosely on carved ground (a ~2-4 s entry delay, accepted as telegraphing;
combat still breaks the muster; restored mid-raid members skip it). The
cosmetic surface-life layer -- road-approach walkers and camp millers /
night watch -- is entry 24's `Overworld/SurfaceLifeController.cs`.

**Key files:** `DungeonCore/WaveStageController.cs` (or adjacent manager
file), `Adventurer/AdventurerSpawner.cs` (muster window + `PartyRegistered`),
`Adventurer/DungeonAdventurer.cs` (loose muster drift).

## 10A. Squad Formations -- Tactical Layer

Status: SHIPPED (guide 3a -- march-holding; formation effects and
formation-breaking are guides 3b/3c, pending). Formations now persist past the
muster: a formationed party (Assault or Escort) advances as a body instead of
dissolving into individual beelines at BeginAdvance.

**Lead-anchored soft-hold.** One member is the anchor -- `AdventurerParty.CurrentLead`
(a Hero, else a VIP, else the first live member). The lead paths to the core exactly as
before and exposes its heading (`FacingDir`). Every other member, while in MovingToCore,
runs `FollowFormation`: it places its existing class-ranked slot (`ComputeSlotOffset` --
Tank/Fighter front to Cleric rear; Escort = VIP centre, guards ahead) relative to the
lead's position and heading, and steers to it. This reuses all existing pathfinding --
only the lead pathfinds.

**Corridors and stragglers (the soft part).** Where a slot falls in raw rock (a tight
corridor), the follower collapses toward the lead and files in behind it (`SlotWalkable`
gates this), so the body compresses to single file rather than clipping walls, and spreads
back into shape in open ground. A follower that falls right out of the body (beyond
`formationLostRange`) drops to individual pathing to rejoin, and resumes holding once back
within `formationRejoinRange` (hysteresis prevents flicker). The lead sets the pace: while
any follower is holding but lagging a walkable slot (`IsStraggling`, beyond
`formationStragglerSlack`), the lead pauses (`HasStraggler`) so the body keeps together --
pace-to-slowest without a hard speed cap.

**Scope.** Only Assault and Escort parties hold; None-formation parties keep the loose
muster and beeline. Combat still pulls a member out (the Combat state owns movement) and
the member re-holds afterward. No save change: the hold is transient runtime state
(`holdingFormation` defaults to holding), so a mid-march reload simply re-forms.

**Design fork settled:** lead-anchored soft-hold (reuses pathfinding, degrades gracefully
in corridors), chosen over rigid group-pathfind (clips walls) and pure flocking (reads
less like a formation).

**Shield wall + tank taunt (guide 3b -- SHIPPED).** The formation confers two effects.
Passive: a rear member (not Tank/Fighter, in a formationed party, not fleeing) takes
`shieldWallMitigation` less damage (default 40%) while a living front-ranker stands
(`HasLivingFrontRank`) -- the front rank soaks for the body. Since section 30 (ranged combat), the wall
mitigates Ranged damage only -- melee that reaches the rear rank earns full damage, and
the front rank soaks the bolts for the body. Active: the Tank taunt is now a timed ability, not an always-on class flag --
`IsTaunting` is driven by `tauntTimer`. In combat a Tank spends stamina (`tauntStaminaCost`,
default 30; Tanks have stamina, not mana) to hold monster focus for `tauntDuration` (5s),
barking via `BarkSpawner`; regen plus `tauntRecovery` pace re-taunts. The existing taunter
lock in `ScanForHostiles` reads the timed `IsTaunting`. Counterplay: a single hit from a
non-taunting ally of at least `peelDamageFraction` of a monster's max HP (default 20%) calls
`DungeonMonster.PeelFromTaunt`, which makes that one monster ignore taunters for
`tauntPeelDuration` and re-target (usually onto the ally who hit it) -- so a DPS burst peels a
monster off the tank. Between taunts, monsters fall back to `TargetPriority` and will go for
rear casters; that exposure is intended. Passive and taunt are independent.

**Formation-breaking (guide 3c -- SHIPPED; the tactical layer is complete).** A break scatters
a party's formation for a spell: `AdventurerParty.BreakFormation(seconds)` sets
`formationBrokenUntil`, and while `FormationBroken` the members disperse (`AdvanceOrHold` drops
them to individual pathing) and the shield wall is suppressed (the extra
`!party.FormationBroken` in `WallProtected`). They re-form when it expires -- a break is a
window, not permanent (default 5s). Two sources, both routing through
`DungeonAdventurer.BreakFormation`:

- **Scatter trap** (`TrapBehaviour.ScatterTrap`, class `ScatterTrap`, a `CaptureTrap` sibling):
pure disruption, no damage -- any adventurer stepping on it scatters their whole party. The
player's answer to a shield-wall party. No research gate (without formationed parties to break
it does nothing), authored via a `TrapDefinition` with `scatterSeconds`.
- **Breaker monster** (`MonsterDefinition.breaksFormation`, `formationBreakSeconds`): a brute or
charger that shatters the formation when it lands a hit (hooked in the monster's
`DealAttackDamage`). Off by default, opt-in per monster type.

The capture-trap pin (8C) remains a partial breaker -- it removes one member from formation
without scattering the whole body.

**Squad-formation tactical layer complete:** 3a march-holding, 3b shield wall + tank taunt, 3c
breaking. Note: section 30 shipped ranged combat; the shield wall is now ranged-specific, its
original intent fulfilled.

**Key files:** `Adventurer/DungeonAdventurer.cs` (`FacingDir`, `AdvanceOrHold`,
`FollowFormation`, `SlotWalkable`), `Adventurer/AdventurerParty.cs` (`HasStraggler`).

---

## 11. Tribute and GiftGivers

Status: SHIPPED. Verified: 2026-07-09.

A GiftGiver party (Cultists only) has a bearer drop a `TributeChest` near the
entrance on arrival. Tribute chests are never openable by adventurers or the
player: after a short dwell they are absorbed straight into the core's gold
pool, reusing the DroppedLoot coin-flourish. Accepting tribute shifts
alignment **-3** (dark) and raises Cultist standing.

**Key files:** `Adventurer/TributeChest.cs`.

**Tribute announce (2026-07 smoke-test fix).** Tribute absorption speaks a wisp
line and files an `AlertCategory.System` entry at the chest's world position, so
a delivery landing off-screen is visible. Previously the gold was credited
silently with only a `Debug.Log`.

## 12. Ambient Necromancy and Corpses

Status: SHIPPED (Phase 4; corpse APIs extended by the Crypt build -- see
entry 16). Verified: 2026-07-12.

Slain adventurers (and, later, humanoid monsters) leave a `Corpse`:
source-agnostic, registered in a static `Active` list, lingering **20s**
(serialized default) before fading unless raised; raising claims and consumes
it. Necromancy is per-`MonsterDefinition`: `isNecromancer` monsters scan for
corpses in `raiseRange` (**3** units), channel **1.5s** holding still,
cooldown **5s**, sustain at most `maxRisen` (**3**) minions at once; a raise
produces a random pick from `risenDefinitions` (e.g. Skeleton, Zombie) living
`risenLifetime` (**45s**) before crumbling for good.

**Corpse APIs (as built with entry 16):** `SetLifetime(seconds)` (0 =
persist until told), `MarkNamed(name)` (no timed fade; exempt from ambient
scans -- `FindRaisableCorpse` skips named), `IsNamed` / `HeroName`. Transient
spawners are excluded from the save gather (bug fix: a mid-fight save
previously immortalised 45s minions as permanent spawners on load).

**Key files:** `Monster/Corpse.cs`, `Monster/MonsterDefinition.cs`
(Necromancy header), `Monster/DungeonMonster.cs`.

---

## 12A. Influence Field, Push and Breach Recede

Free growth (the time-extended cap model):
  - Reach R = baseReach + (dungeonLevel - 1) * reachPerLevel + (day - 1) * reachPerDay.
    The day term is what makes the cap NON-TERMINAL: growth no longer idles
    forever at a level ceiling, and given time influence covers a whole floor.
    reachPerDay 1.6 keeps R tracking just ahead of the creep frontier (~168 vs a
    ~172 frontier cost at day 100) so the overlay fringe stays a band, not a halo.
    The day term is NOT privileged: a breach suppresses it with the rest of R.
  - Ambient creep claims the CHEAPEST claimable-ring cell with D within R.
    Its rate ramps linearly in CLAIMS PER SECOND (not interval) from 0.1/sec on
    day 1 to 2.14/sec by day 100, averaging 1.12/sec -- which fills floor 0's
    ~26,900 claimable cells in about 100 days. Interval-lerping was rejected: it
    sits near the slow end too long and would need ~7,700 claims/sec to total the
    same. The rate is FIXED across floors, so larger deep floors fill
    proportionally slower and floor 4 (radius 600, ~1M cells) never fully does.
  - On a confirmed level-up the creep sprints for surgeDuration.
  - Creep never claims river cells; bedrock and uncleared chambers are infinite
    step cost, so the fill is "everything reachable inside the rim, except rivers
    and sealed chambers".
  - SUPERSEDED: the Dijkstra bound was ReachAtLevel(MaxFlatLevel) + margin, which
    hard-stopped growth at 93 cost units regardless of rate -- with a rim at ~172
    the floor could never fill at any speed. The bound is now
    EffectiveReach + fieldDepthMargin, re-dirtying as reach grows. Bedrock's
    infinite cost terminates the search at the rim anyway.
  - A fully claimed floor is a fully UNFOGGED floor: ClaimTile reveals, so late
    omniscience is the accepted reward for surviving. everClaimed likewise grows
    to the floor, lighting all void rock -- working as designed.

Breach recede: pushedFringeLost is 0.08 (was 0.2). Twenty percent of radial
extent was tuned against a small domain and would strip a ~19-cell rind from a
filled floor; 8% gives ~7-8 cells, still a real bite.

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

**Breach never darkens (2026-07 smoke-test fix).** A recede pulls the influence
boundary in and changes NOTHING about visibility: what is unfogged stays
unfogged for the run. DungeonShadow keys its void lighting off
`TileInfluenceManager.WasEverClaimed` -- an additive set that a recede never
removes from, persisted as `everClaimedTiles` and falling back to `claimedTiles`
for saves written before it existed -- NOT off `IsTileClaimed`. Keying it on
current ownership made a receded cell drop out of `baseLight`, which made the
diff-paint remove its shadow tile, which exposed the flat-black interior cap
art beneath -- read by players as the cell re-fogging, though no fog tile ever
moved. SUPERSEDED AND DELETED: `DungeonTerrain.RefogTile`, its
`permanentlyRevealed` allowlist, and `MarkPermanentlyRevealed`. `RefogTile` had
no callers at any point in its life; all three existed only to make the fog
system look like the cause, and drew several wrong fixes before the real
mechanism was found. Do not reintroduce them, and do not re-key `IsVoidRock` on
`IsTileClaimed`.

**No hole in the rim (2026-07).** MarkNaturalFloor filters bedrock. A river's dry
banks are registered as natural floor for footing, and bank cells falling inside
the rim previously became pre-mined walkable ground -- a second, unintended
entrance straight through the sealed border. The claim-side IsBedrock guard could
never catch it because pre-mined ground never needs claiming. Entrance-cave cells
stay exempt inside IsBedrock, so the real tunnel through the rim still registers.

---

# PART II -- DESIGN ENTRIES (CHECK EACH STATUS LINE)

Most entries in this part have since shipped and are recorded as-built in
place with their own Status lines (13 spine / UI / roster, 14, 15, 15A, 16,
and 17's shipped subparts); those Status lines are authoritative. Entries 18
and 19 remain design-only. Entries record APPROVED design; build guides must
still verify live source before writing edits.

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
location hint. Cross-path prerequisites at higher tiers force breadth.
SUPERSEDED: the Diamond 3 -> God 1 ascension was to require a top-tier node
from every path. As built it is gated on surviving the endgame climax
(entry 9) and reads no nodes at all, and the God audience is its ceremony
(entry 19A). Core type affinity gives a 50% discount on matching-affinity
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
+30% -- income and speed are separate levers). The active project also
STALLS at any dawn when no valid Library stands (`pauseWithoutLibrary`,
serialized default on) and resumes when one does, each transition announced
once; the stall latch is transient. This is the as-built form of the
roadmap's "research pauses if the Laboratory is destroyed" -- the Library is
that room. Disabling the toggle restores the decoupled model (projects run to
completion on banked points regardless of Libraries). Affinity halves point cost
only. Bootstrap nodes (Remembered Bones, Remembered Spikes) unlock on new
game via `bootstrapUnlocked`; they re-lock behind the tutorial wisp (the
prologue is built - see The Living Prologue). **Realised:** the trio's
`bootstrapUnlocked` flags are now
off, and the guided opening (TutorialDirector) grants each in place --
status_bars when the player first claims, skeleton + spike_trap when the
first spawner is armed. A completed run persists in
`DungeonSaveData.tutorialComplete` and never replays; a `skipTutorial`
toggle grants the trio up front for testing. Loot books route through `GrantNodeFully` (bypasses points,
prerequisites AND duration; refunds if underway). Node keys are
`tech.<id>` with an `overrideKey` field reserved for the legacy bare keys.
The spine registers `RoomAnchor.UpgradeGate` from per-node `upgradeGates`
entries. Research points live on `DungeonCore` beside gold and persist in
`DungeonCoreSaveData`; project state persists additively on
`DungeonSaveData`. Key files: `Gameplay/TechNodeDefinition.cs`,
Gameplay/TechTree.cs`, `Gameplay/ResearchController.cs`,
`Editor/TechContentGenerator.cs`.

Tree UI (shipped): RimWorld-style single scrollable canvas
(`Gameplay/ResearchTreeUI.cs` + `Gameplay/ResearchNodeView.cs`, default key R, Esc-close
in the PauseMenuController chain, opens while paused, and research may be
COMMITTED while paused -- see canon 39). Paths are horizontal lanes (empty paths hidden, so
Sorcery stays absent), tiers are columns, prerequisite edges are elbow
connectors drawn only when both endpoints are visible; every node reserves
its layout slot so reveals never reflow. Node visibility is data-driven on
`TechNodeDefinition` (Always / PatternKnown / KeyUnlocked / KillsOfClass via
`RunStats.KillsByClass` / KillsAny via `RunStats.TotalKills`
/ CoreAffinity, which shipped with the trapworks and also drops the node
from tree layout via CanAppearThisRun); among visible
nodes the DK2 name rule holds (revealed at one purchase away). Study
Adventurer Anatomy is kill-revealed: KillsAny >= 1, so the first fallen
intruder surfaces it (its Read the Coming Tide prereq still gates the
research start). This is the as-built form of the roadmap's "kill an
adventurer to unlock the task." Master-detail pane shows the affinity price
struck against base, duration, and a requirement checklist where an unmet
pattern shows only its source hint. Header strip carries the active project
(the fill bar interpolates through the day via
`DayNightCycle.CycleProgress01` -- completion itself still lands at dawn --
plus ceil days), the queued project, and full-refund cancel buttons.
`ResearchController.OnStateChanged` is static, so the panel updates the
moment a project starts, queues, cancels or completes; requirement-block
reasons no longer name undiscovered patterns.
The node roster shipped: 49 nodes across all four paths. Sorcery is no
longer empty -- it holds the three neutral core-spell nodes (entry 38),
and holds only those by design, because a core's own affinity working is
given by its god rather than researched.
Two faction-intel nodes join the Observation path (Study the Holy Order,
Study the Mercenary Company), each KeyUnlocked-visible on an `encounter.<slug>`
flag set from the adventurer spawn path -- the generic event-driven task hook:
an event sets an UnlockState key, a KeyUnlocked node reveals off it, and the
node's completion key gates the UI. With KillsAny (entry, Day-62 work) the
reveal conditions are Always / PatternKnown / KeyUnlocked / KillsOfClass /
KillsAny.

**Patrol gating (2026-07).** Patrol orders require tech.patrol_orders, an
Observation tier-1 node (10 points, 2 days, no prerequisites). Gated at both the
command-UI button and OnPatrolClicked. Patrol is a control/information affordance,
so it sits on Observation, preserving one gating identity per path.

## 13A. Guided Opening (TutorialDirector)

**As built:** a soft, event-watched sequence teaches the first-build loop by
leading the player to dig out the seeded entrance, then house the monster the
breakthrough vignette hands them. Nothing is locked -- the wisp suggests and
waits; each beat completes when the player acts, in any order. Beats: (1)
claim territory -> grants `tech.status_bars`; (2) dig for the entrance -- the
`EntranceCompass` stays hidden until this beat sets `TutorialDirector.DigPromptGiven`;
(3) breakthrough -- the `event.entrance_discovered` unlock (fired by
`MarkEntranceDiscovered`) triggers the First Blood vignette, whose mechanical
payload is `BestiaryState.Discover("Cave Rat")` (staged by FirstBloodVignette;
see the First Blood entry); (4) a grace day; 
(4b) carve - the wisp asks for a
room-shaped pocket (a 3x3 of mined ground clear of existing rooms),
satisfied through the wq_carve urging (see entry 29); (5) designate a room, then arm a spawner -- `MonsterSpawner.OnSpawnerArmed` (new)
plus a prior valid `RoomAnchor.OnRoomValidationChanged` completes it, granting
`tech.skeleton` + `tech.spike_trap`; (6) research the Ledger of Alarums --
completes when `tech.alerts` unlocks (soft; the wisp re-prompts); (7) handoff.
Tutorial lines live in a dedicated `WispTutorialScript` and speak through a new
`WispCompanion.SpeakLine(text)` raw-text path (the ambient one-shot queue was
refactored to carry either an id or raw text). The player-built Entrance build
option is retired from the action bar -- Floor 0's entrance is the seeded
tunnel, dug to, not placed.

**Grace day:** `WaveStageController` gained a `graceDays` offset (default 1) --
the wild-animal stage now begins `graceDays` after the breach, not on it, so a
new player gets a quiet day after the vignette before the first wave.

The Bronze-1 bootstrap trio is restored as nodes -- Remembered Bones, Remembered Spikes
and Remembered Sight (status bars; supersedes the free-forever note) -- all
`bootstrapUnlocked`, re-locking behind the tutorial wisp later. Option C
final mapping: damage numbers stay ungated; minimap and alerts are now each
gated by a tier-1 Observation node (Map the Deep Warren -> `tech.minimap`
hides the Minimap body; Ledger of Alarums -> `tech.alerts` gates all three
alert surfaces -- HUD button, history panel/L hotkey, and the AlertsLog
ticker at AddAlert, so nothing is recorded until researched). Both are cheap
early unlocks; no grandfathering, per the ladder rule. The ladder
runs Read the Coming Tide (WavePreviewHUD) -> Ledger of the Fallen
(KnownPartiesPanel) -> Study Adventurer Anatomy (`adventurer_stats`
override) -> Whispers of Intent (`oracle_chamber` override; the Oracle room's
build-grant is the alternate route -- the grant FRAMEWORK now ships as
room-granted unlocks, see entry 1, with content assignment landing in the
Oracle work). No grandfathering: legacy
saves lose ladder features until researched. Build gating is a
`requiredTechKey` string on RoomDefinition and MonsterDefinition, filtered
in RoomTypePickerUI, MonsterSelectionUI and the build controller (spike
traps gate in SetMode); it is PARALLEL to `requiresDiscovery` --
research is the regulars' channel, wild discovery (BestiaryState) its own,
so Thrall stays discovery-gated with no node. Whisperer in Marrow is hidden
until 5 Cleric kills. Architecture gates: Shrine and Boss Room builds,
Barracks tiers 2 and 3. The generator is list-driven and also patches the
gate keys and the Barracks gate onto the consuming assets. Loot books
shipped (see 17).

**Diegetic one-shot exception:** the alerts gate silences the ledger, not
the wisp. Feedback a player cannot recover later from a panel -- the
buried-remains murmur (its sensed flag persists), the buried grant lines,
the crypt raise refusals, and the research stall/resume announcements --
speaks through `WispCompanion.SpeakLine`
(never gated) and only ALSO logs to `AlertsLog`. Repeatable, mechanically
visible events (housed corpses, raise successes, deed and research
completions) stay ledger-only.

**Boss room tier + Crypt/Necromancer (2026-07 smoke-test fix).** Boss rooms
(Deep Foundations) sit at Architecture tier 1 with no prerequisites, 10 points
over 2 days; the Veined Granite pattern requirement is retained and is now the
sole gate, preserving the one-gating-identity rule for the path. Building a valid
Crypt grants `tech.whisperer_in_marrow` through `RoomAnchor.GrantTechNodeIfAny`,
unlocking the Necromancer; Whisperer in Marrow remains researchable on the
Bestiary path as a second route to the same flag, and the grant no-ops when
already unlocked, so the routes cannot collide.

**Wild predator hunt (2026-07 smoke-test fix, cross-ref section 15A/wild).** The
midgame wild predator hunts: it paths to the nearest of the dungeon's own
creatures (`IsWild` excludes itself, other invaders and chamber wildlife) rather
than to the core, and its starve clock advances only while no prey exists on the
floor. Previously it pathed at the core and the clock ran whenever it was not
mid-swing, so it starved while walking. `giveUpSeconds` default 45.

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
exhausted bands fizzle silently -- the Wandering Merchant is the shipped
catch-up valve (see the Wandering Merchant section); tribute coin
flourishes roll as Common). The avatar channel remains a reserved catalog
entry; the trader channel is live via the merchant; the EVENT channel is live for
Gravegold -- the fall of a named hero teaches it
(`PatternDiscovery.NotifyNamedHeroFelled`, called from the adventurer death
path). Learned-from notes persist per
pattern. Persistence: additive `unlockedKeys` + `patternNotes` on
`DungeonSaveData`, restored in `DungeonSaveController` with a silent terrain
catch-up that also heals legacy saves; the existing tech keys ride the same
field, fulfilling the "persistence lands with Laboratory" promise. The
Materials HUD panel became the Pattern Codex (silhouetted unknowns show
their source hint), then moved off the HUD entirely into the journal as a
fifth QuestLogUI tab (Active / Completed / Notes / Deeds / Patterns); the
collapsed HUD chip is retired. PatternCodexUI is unchanged -- it rebuilds on
its page OnEnable and on UnlockState.OnChanged as before. Discovery feedback
no longer relies on the (now gated) alert: PatternDiscovery.Learn speaks a
wisp bark (`pattern_learned`, not a one-shot) as the player-facing feel, and
still records the alert for the ledger once alerts are unlocked. Gold display
moved to the Level Panel. Class loot assets were re-authored from single
tint-era entries into weighted rarity ladders (see
`ScriptableObjects/Adventurers/Classes`). Pattern gating still concentrates
on the Architecture path only; loot books still grant nodes fully,
INCLUDING pattern requirements (unchanged, for the tree build).

Key files: `Gameplay/PatternDefinition.cs`, `Gameplay/PatternCatalog.cs`,
`Gameplay/PatternDiscovery.cs`, `UI/PatternCodexUI.cs`,
`UI/PatternCodexRow.cs`, `Editor/PatternContentGenerator.cs`.

## 15. Room Effects v2 and Attractor Rooms

Status: SHIPPED (supersedes the decided-not-built entry). Verified: 2026-07-11.

As built, room effects run in two lanes. The PER-SECOND ENTITY lane
(LairRegen, TrainingXp, MonsterDamageBuff, CoreRetaliation, AdventurerSlow,
SparringXp) acts on things standing in rooms and remains ACTIVE FLOOR ONLY:
`RoomEffectController` handles AdventurerSlow (every intruder in the room --
pilgrims included, fear is indiscriminate -- moves and strikes at
`1 - 0.1 x tier`, floored at 0.5; the same multiplier divides attack rate),
and the new `SparringController` handles SparringXp (idle monsters whose
spawners sit in the room are paired for bouts: an exchange every 1.5s plays
the attack/hurt animations, deals real chip damage of `2 x tier` -- never
striking below 30% max HP -- and grants `perSecond x tier x 1.5` XP to both;
six exchanges per bout, then a 10s pair rest; bruises persist by design;
facing flips are deferred because sprite flip interacts with veteran scale).
MonsterDamageBuff is the one tier-flat magnitude: the controller applies
`Mathf.Max(1f, perSecond)` as a live multiplier, unscaled by EffectScale.
The STATE/CENSUS lane (GoldCapBonus, Attractor, RespawnSpeed, ManaRegen,
TrapDamage) is ALL FLOORS, recounted by the new `RoomEffectCensus` on
validation and upgrade events plus a 2s heartbeat that also covers load.
GoldCapBonus: global gold cap = base 500 + `perSecond x tier` per valid
Treasury (authored 500); incoming gold clamps at the cap with a
one-per-episode wisp alert; gold already held is never confiscated when the
cap shrinks. Attractor: the new `RoomEffect.attractorTarget` field names an
AdventurerType; each valid room adds `perSecond x tier` to that type's
weight AND to its parent intent's stage, so bonuses survive the two-stage
roll. Shipped attractors per tier: Shrine -> Pilgrims 1.5, Library ->
Scholars 1.0, Treasury -> Treasure Hunters 1.5, Core Chamber -> Nobles
0.75. RespawnSpeed: spawners standing in a valid room carrying the effect
tick at `1 + perSecond x tier` (Spawn Chamber 0.25; Barracks, Crypt and
Boss Room 0.15 -- each muster room hastens its own residents; entry 15A). ManaRegen: the census sum folds
into `DungeonCore.CurrentManaRegen` (+1/s x tier per Ritual Circle).
TrapDamage: every trap hit (and its damage number) multiplies by
`1 + 0.1 x tier` per valid Forge, summed and capped at +50%.

New rooms (all Architecture-gated; registry appends, names save-immutable):
Treasury (GoldCapBonus 500 + Attractor 1.5 TreasureHunter, min 9 -- Treasury
and Treasure Vault are ONE room, the hoard is the bait; supersedes the
two-room mapping), Spawn Chamber (RespawnSpeed 0.25, min 9), Arena
(TrainingXp 2 + SparringXp 6, min 16 -- the Arena claims the previously
orphaned TrainingXp; Barracks stays pure LairRegen and no separate Training
Hall exists), Dread Chamber (AdventurerSlow 0.1, min 9), Ritual Circle
(ManaRegen 1, min 9), Forge (TrapDamage 0.1, min 9). 
`RaidForesight` (ordinal 14, Oracle Chamber marker) joins the STATE/CENSUS
lane: `RoomEffectCensus.ForesightTier` holds the highest tier among valid
rooms carrying it (all floors, `anchor.Tier` not EffectScale), and
`WavePreviewHUD` reads it to deepen the raid preview -- tier 1 the likely
intent, tier 2 the headline type, tier 3 the faction. The reading is a
side-effect-free argmax of the live `AdventurerSpawner` intent/type weights
(`PredictNextRaid`), never an RNG pre-roll, so it cannot desync actual
spawns. `RoomEffectType` ordinals remain append-only: ..., TrapDamage=11,
CryptPreservation=12, TrophyHousing=13, RaidForesight=14.
Nodes: Vaulted Reserves / Summoning Circle / The Drawn Circle (Architecture tier 2,
patterns Silverwork / Packed Earth / Quarry Sand, prereq Remembered
Spikes), Proving Grounds / Coals Below (tier 3, Tempered Steel / Wrought
Iron [reused -- pattern reuse across nodes is legal], prereq Deeper Lairs),
Whispered Dread (tier 3, Cured Leather, prereq Summoning Circle).
`RoomEffectType` ordinals are append-only: GoldCapBonus=5, Attractor=6,
RespawnSpeed=7, SparringXp=8, AdventurerSlow=9, ManaRegen=10, TrapDamage=11.

Key files: `Room/RoomEffectCensus.cs`, `Room/SparringController.cs`,
`Room/RoomEffectController.cs`, `Room/RoomDefinition.cs`,
`Adventurer/AdventurerSpawner.cs`, `Monster/RespawnTicker.cs`,
`Editor/TechContentGenerator.cs`.

Rejected: attractors as hard spawn guarantees (additive weights only); a
separate Training Hall room (the Arena carries the passive lane); node
requirements on reserved-band patterns (no live discovery channel exists).

## 15A. Monster Muster (Spawn Rooms, Posts, Floor Gates)

Status: SHIPPED. Verified: 2026-07-12.

**Muster rule:** new monster spawners may be placed only inside a VALID
room whose `RoomDefinition.spawnCategories` contains the monster's
`MonsterDefinition.category` (`MonsterCategory`, append-only: Beast=0,
Humanoid=1, Undead=2). Authored map: Core Chamber = all three (the
universal, research-free ground -- capped at 49 tiles and carrying no
RespawnSpeed, so real muster rooms outgrow it), Spawn Chamber = Beast,
Barracks = Humanoid, Crypt = Undead. Bosses (`BossVariantDefinition`)
route by room type instead: placement is legal in a Boss Room footprint
that validates once its own boss-spawner requirement is set aside
(`RoomValidator.Validate(footprint, def, ignoreBossSpawner)`) -- placing
the boss completes the room, closing the old chicken-and-egg. Sub-bosses
are not BossVariantDefinitions and follow their base category. Matching
valid rooms tint gold during PlaceSpawner mode; rejection toasts name the
muster rooms (distinguishing "none built" from "wrong cell"). Spawners
saved before this system carry no gate (`musterGated` false) and are
exempt forever; remove-and-replace is the migration path.

**Room-break rule:** a muster-gated spawner whose cell is no longer
inside a valid matching room pauses respawning (one wisp alert per
outage, blocked "!" on the indicator) and resumes when the room stands
again. Live monsters are unaffected -- adventurers smashing a muster room
attack production, not the standing army.

**Posts:** `MonsterSpawner` carries an optional post cell (persisted via
`hasPost`/`postCell` on `MonsterSpawnerSaveData`). The Post button on
the monster command panel enters `BuildMode.PlaceMonsterPost`
(Attack-Here flow: Esc/right-click cancels; the commit applies to the
whole multi-selection); the monster's Wander state anchors on the post
instead of the spawner, so respawns walk back from the muster room to
their station. Wander / Clear Orders clears the post. A programmatic
"P" glyph marks the post while the spawner is selected. A patrol leg the
pathfinder cannot reach raises a throttled "no path" bark over the
monster (BarkSpawner, ~5s cooldown) instead of failing silently.

**Floor gates:** spawner respawn ticking and spawner placement are both
blocked while any threshold-crossed adventurer is on that floor
(`FloorIntrusion.AnyOnFloor`; surfaces as the blocked "!" indicator).
The threshold latch (`DungeonAdventurer.CountsAsIntruder`): floor 0
latches when the adventurer's cell first leaves the entrance-cave cell
set; floors below latch on stair arrival; legacy player-placed-entrance
saves latch at spawn. The latch never clears while the adventurer lives
on the floor. Commoners count; wild animals do not (the 6-unit
per-spawner radius block is retained beneath the floor gate and still
covers them).

**Fixed in passing:** `RoomEffectCensus.GetRespawnMultiplier` resolved
cells via the ACTIVE floor's influence -- garbage for spawners on
Y-offset floors. Chambers now carry their FloorRoot and the spawner
resolves against its own cached floor (`MonsterSpawner.Floor`).

**Opening (built):** the First Blood vignette is `FirstBloodVignette` -- pure
puppet choreography (runtime SpriteRenderers, no live entities) staged from
the seeded `EntranceCaveData` (mouthCell -> spawnCell axis), triggered by the
TutorialDirector's breach step. Beats: rat sprints in, arrow loosed from
beyond the mouth, kill, corpse pause, absorb (sink/shrink/tint toward the
core's colour) firing `BestiaryState.Discover("Cave Rat")` at the take,
hunter arrives late, one floating bark ("Gone? It dropped right here..."),
exits. The camera glides to the tunnel via SetFollowTarget on a stage anchor
plus a new `NudgeZoom`/`TargetZoom` API; camera input is HARD-LOCKED for the
vignette's duration (`DungeonCameraController.InputLocked`) -- the beat plays
uninterrupted. Stage geometry is core-derived (outward = away from the core),
immune to seeded-cell naming. **Day-34 note: the dynamic-camera rejection is
lifted (2026-07-19). Scripted camera moments are permitted wherever a beat
needs one; this vignette, the death-sequence slab pan, and the Inspector
arrival glide were the first three. The other Day-34 rejections stand.** If the
vignette is absent, the director's fallback grants the rat directly. The
Skeleton bootstrap testing convenience is superseded by the tutorial's
per-step grants. The discovered-beast channel is otherwise fully shipped: 
wild definitions sit in the placeable registry behind `requiresDiscovery`.

**Key files:** `Monster/MonsterCategory.cs`, `Room/MusterRooms.cs`,
`Adventurer/FloorIntrusion.cs`, `Monster/MonsterSpawner.cs`,
`Monster/DungeonMonster.cs`, `DungeonCore/DungeonBuildController.cs`,
`Room/RoomValidator.cs`, `Room/RoomEffectCensus.cs`,
`Adventurer/DungeonAdventurer.cs`, `Monster/MonsterWaypointVisuals.cs`.

**Rejected:** soft muster (2x mana anywhere -- undermines the tension);
a wander-in-this-room order (the post subsumes it and room identity is
save-fragile); the Crypt as the only undead ground (deadlocks the
opening); strict four-room mapping without a universal ground (nothing
placeable before Architecture research); unlatching adventurers who flee
back into the tunnel (flappy respawn state).

## 16. Crypt and Deliberate Nemesis Raise

Status: SHIPPED. Verified: 2026-07-12.

As built: named-hero corpses (`Corpse.MarkNamed`, set at the adventurer
death site from `IsNamedHero` + `DisplayName`) never fade on the 20s timer
and are invisible to ambient necromancy. They lie where they fall until
dawn; at dawn `CryptController` gathers each into a free sarcophagus inside
a valid Crypt on any floor (corpses housed in stone whose Crypt has broken
are evicted first and compete again). No free stone -- the corpse fades for
good, with a wisp alert. Housed corpses persist indefinitely and across
saves; unhoused named corpses persist across saves too and still face the
first dawn after load. Preservation capacity = sarcophagi standing in valid
Crypts (recipe minimum 2; extra stones add slots).

The raise: clicking a housed corpse (BuildMode.None only) opens
`CryptRaiseUI` -- name, price, and the contract. Cost: `raiseManaCost`
(serialized, default 100) + the risen definition's capacity (25 on
`Monster_RisenHero`), held while it walks, plus 15 notoriety. The raise is
IRREVERSIBLE and the servant is MORTAL: one life, no respawn, no crumble
timer. `MonsterSpawner.InitialiseRaised` holds capacity like a placed
spawner and persists in saves (`raisedOneLife` on `MonsterSpawnerSaveData`);
on its monster's death the spawner destroys itself and the capacity comes
home. This refines the decided entry's "permanent once done": the ACTION is
permanent, the addition is not -- heroes cannot be farmed into a standing
army. Display name "Risen <hero>" rides the existing spawner CustomName.
Monster_RisenHero is registered (save restore resolves definitions by name)
but carries the sentinel key `tech.crypt_raised` that no node grants, so it
never appears in the build picker.

Research: node `waiting_dark` ("The Waiting Dark", Architecture tier 3,
30 pts / 3 days, prereq Summoning Circle, pattern Gravegold -- taught by a
named hero's fall, see entry 14). Room: Crypt, min 9 tiles + 2 Sarcophagus
furniture, marker effect `CryptPreservation` (ordinal 12, skip-listed in
the tick loop), maxTier 1. New furniture: Sarcophagus (25 mana, blocks
pathfinding). The Crypt is also the undead muster ground and carries a
RespawnSpeed 0.15 effect (entry 15A). This supersedes the backlog's "passive notoriety reduction"
framing entirely; the sarcophagi survived as the preservation slots.

Key files: `Room/CryptController.cs`, `UI/CryptRaiseUI.cs`,
`Monster/Corpse.cs`, `Monster/MonsterSpawner.cs`,
`Gameplay/PatternDiscovery.cs`, `Save/DungeonSaveController.cs`.

## 17. Discovery Content (Buried Skeletons, Loot Books, Wisp Guide)

Decided: buried skeletons are a Bestiary discovery -- dig-to-unlock matched
to core type (Dark core = undead skeletons, Earth core = dwarven skeletons).
Adventurer loot books (Scholars and literate types) drop tomes that unlock
tree nodes or research paths, bypassing prerequisites; tome flavour matches
the node. Wisp guide (folds in later): a
`QuestController.ProgressObjective` increment API enabling `TalkNPC` (hooked
in `NPC.StartDialogue`) and `Custom` objectives (optional field on
`FlagInteractable`).
SHIPPED (wisp urgings): the dungeon-side fold-in is entry 29.
SHIPPED: `LootType.Book` + `DropEntry.grantsNode`; `DroppedLoot.Absorb`
calls `GrantNodeFully` (bypasses points, prerequisites and duration; refunds
if underway) and the gold still pays, so duplicate tomes are never dead
drops. Three authored tomes: Mage -> Whisperer in Marrow, Cleric ->
Whispers of Intent, Explorer -> Deep Foundations.

## 18. Phase 5 Designs

Decided so far: Holy Ground desecration is designed INTO the Holy Order
trigger -- Holy Ground patches are procgen via `TerrainTypeMap`, desecrating
one is unsealing a Church seal and feeds the trigger; recommended reward is a
buried-skeleton Bestiary discovery. Faction payoffs as the mid-game gold sink
and the deliberate way to lower escalation tiers (see entry 7).

**Alert severity tiers (SHIPPED).** `AlertEntry` carries an `AlertSeverity`
-- Info / Warning / Critical -- PARALLEL to its category, persisted as an
additive `severity` int on `AlertEntrySaveData` that reads back 0 (Info) from
any older save. `AlertCategory` was not touched: its ints are append-only and
severity is a second axis, not more category values.

Severity is DERIVED from category unless a caller overrides it
(`AlertSeverityStyle.DefaultFor`): Threat warns, everything else informs. That
was chosen over sweeping seventy-nine call sites, which would have been a large
untested diff whose real failure mode is the eightieth caller written next year
defaulting to silence. Ten sites pass Critical explicitly -- core under threat,
halls severed, the crusade, bought steel, the beast, the house riding, a Hero
entering, the climax dispatch, ascension, and the first adventurer wave -- and
that short list is short precisely because Critical raises a banner.

**Zero prefab edits, and the constraint shaped the design.** Prefab work cannot
ride a delivery script. The alert row is Button + TMP_Text children and
`AlertsLog.BindButton` already spent labels[1] on the category colour, so
severity took labels[0] -- the timestamp -- as a marker (`! ` / `!! `) plus a
tint, and Info writes no tint at all so ordinary rows keep the prefab's own
colour. Critical additionally raises `FeatureAlertBanner`, reused rather than
duplicated: it is a static singleton, already scene-wired, and never calls
SetActive(false) on itself, so an external `Show()` cannot hit the
activation-ordering quirk `BossAlertBanner` suffers. It never fires from a
load -- a stack of banners for threats already answered is worse than silence.
The critical sting is a serialized `SoundEffectManager` key left EMPTY, so the
layer is usable before the clip exists.

**The loud half (SHIPPED, backlog item o).** The layer above shipped with a
banner and an empty sting key. The rest of item (o) is this.

*Critical washes the screen.* `ScreenFlash` already existed, self-builds its own
overlay canvas, is live in the scene and was already driving the climax beast's
pushback flash, so the alert flash reuses its colour and duration -- `0.75,
0.05, 0.05` over 0.45s -- and the two read as one language instead of two
unrelated effects. No new component and no prefab work.

*The flash and the sting are COALESCED; the banner is not.* Three Criticals can
land in the same breath -- a wave stage, a Holy Order strike and a core breach --
and three washes of red stacked on each other reads as a rendering fault rather
than as three pieces of bad news. Both loud channels share one three-second
window on UNSCALED time, because a Critical can land while the game is paused and
a scaled cooldown would never expire. The banner is exempt: it replaces its own
text rather than stacking, so rate-limiting it would just lose the newer message.

*`SettingsAccess.ReduceFlashing` suppresses the flash and NOTHING else.* The
sting and the banner still fire, because this is an opt-out from a
photosensitivity hazard, not from being told bad news. It lives beside
`BossAlertMode` rather than in `DcrVideoSettings`, which is resolution,
fullscreen and vsync -- things needing an Apply pass -- while this is a bool read
at the moment of use. There is no settings-menu row yet; rows are prefab
children, and a preference with no UI still beats a flash with no opt-out.

**Critical BYPASSES the `tech.alerts` gate, partially and on purpose.** What the
Ledger of Alarums sells is the ACCOUNT: the ticker, the history, the unread
count, the ability to look back. It was never supposed to be selling the alarm.
Before this, a player who had not bought the node watched the core go down with
no banner, no flash and no sound. So a Critical now raises its banner, flash and
sting unresearched, and still records nothing -- no history row, no ticker row,
no unread count, no `OnAlertAdded`. Nothing downstream can tell the difference
between that and the alert never existing, which is what keeps the node worth
buying.

**The breach finally says something.** `DungeonCore.DestroyCore` raised no alert
at all: `CoreThreatMonitor` announced a threat APPROACHING and the breach itself
was silent, so the loudest moment in the game reached the player as an influence
ring quietly receding. First breach, second breach and the breached-in-transit
instant loss now each raise Critical, before the event that ends or destabilises
the run, so the banner lands while the player is still looking at a live dungeon.

*There is no "HP critical" tier, because the core has no HP.* It has a breach
COUNT: the first breach opens an instability window of `instabilityDuration`
seconds and a second breach inside it ends the run. The first breach IS the
critical-health beat -- one life left, on a timer -- so it takes one alert for
that instant rather than two.

**`CoreThreatMonitor` watches invaders as well as Destroyers.** "Destroyer" is
the player-facing name for the Mercenary party type, whose goal is `BreachCore`,
and the monitor already fired Critical when one came inside `coreThreatRadius`.
But invaders are MONSTERS, not adventurers, and the monitor scanned only
`DungeonAdventurer` -- so a wild or climax beast marching on the core was
invisible to it, and the first the player heard was `DungeonMonster` calling
`DestroyCore` at `invaderBreachDistance`. Same radius, same severity, because it
is the same question. `DungeonMonster.IsInvader` was added for it: `IsWild` is
also true for wild-chamber dwellers, which wander their own chamber and threaten
nothing.

*The invader watch is ALERT-ONLY and sits outside `IsCoreThreatened`.* That flag
is not merely a report -- `DungeonMonster` reads it to decide whether to rally to
the core, and `NearestThreat` is typed `DungeonAdventurer` -- so folding invaders
in would change monster behaviour and a field's type in one move. Whether the
garrison should turn out for a beast as well as for a Destroyer is left open.

**The sting is guarded rather than trusted.** `SoundEffectManager.Play`
dereferences a static `SoundEffectLibrary` assigned only in the manager's own
`Awake`, so calling it with no manager in the scene throws outright. An alert
layer that can hard-error on a scene missing an audio object is worse than one
that is silent. `criticalSfxKey` still ships EMPTY: only `Chest` and `Footstep`
exist in the library, and a clip plus a library entry is editor work that cannot
ride a delivery script. The flash and the banner do not wait on it.

Not built, and each for a reason: severity filter pills in `AlertHistoryPanel`
(pills are prefab children); a mass-death detector (monsters are supposed to
die, and the proximity rule above is the beat that was actually wanted); boss
death re-tiered above Info; `BossAlertService.bossDeathSting`, an unwired
`AudioClip` predating `SoundEffectManager`; and generalising `BossAlertBanner`,
which the backlog asked for but which `AlertsLog` deliberately declines in favour
of `FeatureAlertBanner` for the activation-ordering reason above.

**Core spells / active abilities: GREENLIT, unscheduled.** The call is made;
the build is not started. Two things unblock on it: the Sorcery research
path (which hung on this decision and may now be planned), and the two
trader books already reserved by name in the merchant catalog -- Primer of
the First Spark and The Drawn Breath (see entry 28A).

**Standing ledger (recorded so it is not re-litigated).**

- *Carry weight / encumbrance:* BINNED. Not to be revisited without a new
  case.
- *Chest tiers:* SHIPPED (behavioural hook). Tier now drives chest choice
  for Treasure Hunters (goal LootAndLeave): detection range multiplies by
  `ChestDefinition.TierRangeMultiplier` (Bronze 1.0 / Silver 1.33 / Gold
  1.66 -- gold glitters farther), and among detected chests the highest
  tier wins with nearest as tie-break (`ScanForChestsTiered`, gathered at
  the widest reach then culled per tier). Delvers keep the plain nearest
  scan -- their detour stays short by design. Multipliers live in code on
  `ChestDefinition`, not on assets, so the three tier assets cannot drift
  apart. No UI, no save shape. Diagnostics ship with it: per-tier
  targeted/opened counters split by goal on `ChestRegistry`, one summary
  log line each time the raid cycle ends (`ResetAll`, silent on untouched
  cycles) plus a run-total print in `Commands` ("Print Chest Tier
  Stats"). The hook feeds the already-SHIPPED loot satisfaction loop for
  free: richer chests raise carried-out gold, which lowers Notoriety
  (`lootSatisfactionFactor`, cap 25 per exit), shifts Alignment good
  (`AlignmentSystem.OnAdventurerLeftAlive`) and fills the
  `MercenaryContract` outflow window -- no new code was needed on that
  side.
  REJECTED (final, not deferred): a richer chest holding a party longer
  (new linger machinery, near-invisible payoff); tier deepening Destroyer
  resolve (Destroyer code has no chest contact point -- it would be
  unattributable hidden difficulty); chest presence feeding spawn weights
  (the Treasury attractor owns that bait; not to be revisited).
  The open candidate that closed this bullet is now BUILT -- see the
  Dungeon appeal ledger bullet below.
- *Dungeon appeal ledger:* SHIPPED. `Gameplay/DungeonAppealLedger.cs`
  (a component beside the threat managers on AlignmentSystemController)
  keeps a MercenaryContract-shaped rolling day window of raid outcomes
  with ZERO new gameplay hooks: it ingests `RunStats.OnDaySummaryReady`
  (nightly; the event skips quiet days) and rotates on
  `DayNightCycle.OnDayStarted` -- rotation rides EVERY dawn
  deliberately, because ingest alone would let a bloodbath window never
  decay. Two cached shapers feed the spawner through static reads:
  `CivilianMultiplier` = 1 - deterrence, where deterrence =
  clamp01((deathRate - graceRate)/(1 - graceRate)) x maxDeterrence
  (defaults: grace 0.25 -- a dungeon is supposed to be dangerous -- and
  cap 0.6, so civilian lanes thin to 40% at total slaughter, never to
  zero; deathRate = slain / (slain + fled + breached) over the window);
  and `DelverAppealBonus` = min(appealCap, goldOut x appealPerGold)
  (defaults cap 3.0, 0.02 per gold). Application in `AdventurerSpawner`:
  the multiplier scales the Delver / Pilgrim / GiftGiver intent weights
  AFTER base + reputation + attractor additions (multiplicative so
  authored bases keep their proportions); the bonus adds to the Delver
  intent stage AND the Treasure Hunter type lane, mirroring the
  attractors' two-stage additive design (entry 15). Destroyer weights
  and the spawn interval are untouched: notoriety owns escalation and
  cadence, so a slaughterhouse dungeon trends hostile-but-sparser --
  the intended self-balancing -- and two systems fighting over the
  interval is the ambiguous-pressure trap. All four weight sites
  (RollIntent, LikeliestIntent, RollDelverType, LikeliestType) read the
  same statics, so WavePreviewHUD foresight stays honest. Persistence:
  `AppealLedgerSaveData` (three int lists) via DungeonSaveController
  beside the other threat blocks; shapers recompute on load and
  `EnsureWindow` heals pre-ledger saves. Validated by
  `Tools/sim_appeal_weights.py` (23 checks: direction, caps, grace
  edge, decay-in-windowDays, zero-resolved guard); rerun it when the
  maths changes. Diagnostics: a dawn log line whenever a shaper is
  non-neutral, and "Print Appeal Ledger" in `Commands`.
- *Appeal expansion:* SHIPPED (rides the appeal ledger). Four small
  draws, all composition-side except the novelty interval shave:
  (1) WORSHIP LEAN -- `AffinityProfiles.Roll` multiplies the affinity
  matching `DungeonCore.DungeonType` by `worshipCoreLean` (serialized
  3x) for WorshipCore-goal types only (Pilgrim, Cultist). Bias-only:
  the faction gate still zeroes unlisted affinities, so a Holy Order
  pilgrim never rolls dark even at a Dark core (0 x lean stays 0)
  while Cultists lean freely -- the asymmetry is intended.
  (2) NOVELTY -- `MonsterDefinition.novel` (asset flag, deliberately
  scarce: eight authored wonders -- Leviathan, Behemoth, StormRoc,
  Cinderwyrm, Terravore, RadiantGryphon, ChimeraSpawn, VoidMaw; the
  Archons and the horror-types were deliberately excluded so novelty
  stays a menagerie draw, not a tier tax). `DungeonMonster` keeps a
  static per-species refcount of live novel monsters (a spawner's ten
  of a kind count once); the spawner adds `scholarNoblePerNovelSpecies`
  (0.5) per species to the Scholar and Noble lanes and shaves
  `CurrentInterval` by `novelIntervalPerSpecies` (0.05) per species,
  floored at `novelIntervalFloor` (x0.85) -- the camps reduction set
  the precedent for a second bounded interval input, and the floor
  keeps notoriety as the gate's owner.
  (3) ALIGNMENT DRAW -- good alignment adds `alignmentToPilgrim`
  (0.02/point above 0) to the Pilgrim lane, dark alignment adds
  `alignmentToGiftGiver` (0.02/point below 0) to the GiftGiver lane,
  at both intent sites BEFORE the appeal multiplier (a bloody stretch
  suppresses even the invited). Evil-draws-Heroes was skipped:
  crusades and the Hero notoriety gate already punish dark play.
  (4) TROPHY FAME -- `TrophyEffectType.Fame` (ordinal 4, append-only
  rule holds): a displayed Fame trophy's magnitude sums into
  `RoomEffectCensus.TrophyFame` (ceiling `trophyFameMax` 2), which the
  spawner adds to Scholar and Noble alongside novelty via one
  `SightseerBonus()` helper at both type-roll sites. No Fame trophy
  asset ships -- the plumbing and the authoring recipe do (Content
  Authoring chapter 34); the asset and its sprite are an authoring
  step. Diagnostics: "Print Sightseer Draw" in `Commands` (novel
  species, fame, alignment). REJECTED (final): word-of-mouth per-type
  return weighting -- redundant with the appeal ledger above, which is
  outcome-based word-of-mouth at intent granularity; per-type would
  need a type field on `RaidRecord` for marginal gain.
- *Random world events framework:* SHIPPED -- see entry 37. The
  dispatcher, registry and data-driven authoring surface exist
  (`WorldEventDirector` + assets under `Resources/Events/World`); the
  bespoke threats stayed bespoke by design, and the Wandering Merchant
  keeps its own arrival controller.

## 19. Buried Age Sites and the Deep Roads

Status: PART SHIPPED. The road substrate, the sites, and the dwarves --
faction, outpost, vendor, spoil economy, the village (part 3), and now The
Living Holds (step 7: walking villagers, road patrols, the caravan and its
verbs, the entry-7 matrix revisit) -- are built and described as-built below.
The granite overlay, road claiming and the warning ladder have since SHIPPED
and are recorded as-built in this entry's own later sections -- the sentence
that stood here predated them.
Do not assume the classes or APIs of anything still marked DESIGN here. The
tier-up divine audiences that used to share this entry are now 19A and have
no dependency on any of this.

The deep floors get a civilisation instead of a difficulty curve. Scattered
ruins alone read as set dressing; a ROAD through them reads as somewhere that
used to work, and gives the player a reason to descend beyond floor space.

### Depth-as-time, made literal

Deeper floors are an older era. The player walks the same civilisation twice:
once still holding, once collapsed. Deeper is not merely richer -- it is
EARLIER, which means it is more intact as a plan and more ruined as a place.

**Floor allocation (DECIDED; renumbered and extended by the floor plan
correction below).**

- **Floor 3 [index 2] (radius 250, Gold-gated)** carries the LIVING road: one
  surviving trunk, rim to rim, still maintained, passing through the dwarven
  gatehouse. One road because it is the last stretch anyone still keeps.
- **Floor 4 [index 3] (radius 400, Diamond-gated)** carries the VILLAGE and
  the last living crossroads: a sparse network the dwarves still walk, its
  first broken spurs at the edges -- the network already dying where it once
  climbed toward the floor above.
- **Floor 5 [index 4] (radius 600, God-gated)** carries the DEAD network:
  expanded ruins joined by Buried Age roads. Deliberately DENSER than the
  village floor, not sparser -- deeper is older, and older is when the thing
  was whole. Junctions that go nowhere; spurs visibly broken. A civilisation
  that shrank.

The bottom floor being God-gated is why the living settlements are NOT there:
an inhabited hold and a second vendor reached only at God tier would arrive
after the player has stopped needing either. The dead network has no such
problem because it is exploration, not economy. It is priced to match:
road on a floor carrying no living holding claims at
`deadRoadClaimResistance` (4x, level with Granite) rather than the living
road's 8x, and takes no granite in the holdings overlay. Cost and colour
agree, and both say the same thing -- old paving with nobody behind it.

Radii come from `DungeonCoreProgressionTable`: floor 0 = 100, 1 = 150,
2 = 250, 3 = 400, 4 = 600.

### The sites (SHIPPED)

Eight archetypes: the canon six -- Sunken Plaza, Collapsed Archive, Ossuary,
Broken Aqueduct, Hollow Sanctum, Sealed Gate -- plus **Guard Post** and **Toll
House**. The Toll House earns its slot by foreshadowing: the dwarves hold a
toll gate, so a ruined one on the dead network is the same institution two
eras earlier. Sites ship INERT: no wild spawns, no loot, no flank behaviour.
The one exception, added with the authored-plan expansion: **every placed
Ossuary guarantees exactly one buried-remains cell** in its masonry, chosen
deterministically (a hash of the site's id and anchor, not an RNG -- there is
no seed to disagree about) from the ruins cells that border the carved
interior, so the claim-halo murmur can sense it from claimed floor. Appended
in `BuriedRemainsController.SitesFor` rather than sampled by
`GetBuriedSites`, because that sampler accepts only Stone and Granite and
site masonry is retyped to Ruins. Ossuaries sit on floor index 4's roster
only, so the find is rare by placement alone; the discovery pays exactly as
canon 17's ordinary buried remains do, and mining the cell also pays the
`ancient_masonry` pattern the way any ruins cell does -- both on purpose.

The road is what makes them legible as one place rather than set-pieces. The
Sunken Plaza is a junction. The Broken Aqueduct crosses the road. The
Collapsed Archive sits ON it, because archives sit on roads. The Sealed Gate
is where the road stops. Anchor preference is fixed PER ARCHETYPE rather than
authored, because the relation IS the archetype -- an aqueduct that crosses
nothing is a wall, and a toll house away from a road is a cottage. Every
preference DEGRADES to a free in-band pick rather than failing, which is what
puts the lone guard post on road-less floor index 2.

**Reveal is the carved floor plus its one-cell halo, and nothing else.** Two
independent things decide whether a cell reads as a wall. PAINTED:
`CaveWallRenderer` caps and faces a solid cell when it is claimed or 8-adjacent
to a MINED cell. REVEALED: its fog is cleared. A cell needs BOTH, and each
failure mode has its own symptom -- revealed but unpainted shows the bare floor
tile underneath, painted but fogged is invisible.

Sites hit both in turn. The first build revealed every masonry cell, including
the ones buried inside a thick wall that border no open floor and are therefore
never painted: 726 cells of bare floor slab across the roster. The correction
then removed the halo as well, which fogged the natural rock around each site
-- rock that IS 8-adjacent to the carved floor and IS painted: 683 wall cells
rendered and never unfogged, so sites had open floor with no wall attached.

The halo alone is exactly right, because "painted" is defined as 8-adjacency to
mined floor and the carved cells are the mined floor. Measured over the roster:
zero painted-but-fogged, zero revealed-but-unpainted. The masonry skin is a
subset of the halo, so it needs no pass of its own. **Any future feature that
reveals solid cells must satisfy the same invariant: reveal exactly the cells
the wall renderer will paint, no more and no fewer.** Fog is one-way, so
neither error can be corrected after the fact.

**Two cell sets, and the read.** A site's carved interior is natural floor,
revealed and marked exactly as a chamber is. Its MASONRY is deliberately not
carved: those cells stay solid rock and are merely retyped to their masonry
terrain -- `TerrainType.Ruins` for the dead sites, `DwarvenMasonry` for the
living dwarven ones (the village hold, the gatehouse outpost), decided in one
place by `TerrainFeatureGenerator.MasonryTypeFor`. They therefore render as
cave wall, cost that terrain's resistance to claim, and pay out the
`ancient_masonry` pattern when mined -- all of which
the terrain system already did for an enum value that had been reserved and
unplaced since it shipped. Straight walls against organic cellular-automata
chambers is the entire read; no props and no new art are involved.

**The plan is the no-repeat unit**, not the archetype. Each archetype has
three PROCEDURAL variants that change the layout -- wing count, colonnade
against solid wall, sunken against raised, courtyard against corridor -- for a
base pool of twenty-four. Floor index 4's thirteen sites therefore never repeat
a plan even where an archetype appears twice. Parametric jitter rides on top:
span, quarter-turn rotation, mirror, and breach count. Rotation is restricted
to quarter turns and mirrors because those are exact integer maps; an
arbitrary angle would alias every wall into a staircase and lose the read.

**Plans may also be HAND-AUTHORED, and the two kinds are interchangeable.** A
plan drawn as an ASCII grid in a text asset joins the same pool as an extra
variant of the archetype it declares, numbered above the procedural ones, and
inherits the entire placement layer unchanged -- band, anchors, spacing, disc
clamp, rotation, the walkability guard, save, reveal and terrain override. The
no-repeat rule therefore covers both kinds without knowing the difference. An
authored plan ignores `minSpan`/`maxSpan`, because being drawn at a chosen size
is the point of drawing it; it may override its archetype's anchor with
`@anchor:`, and may opt out of rotation with `@rotate: no`. A plan whose
archetype is absent from a floor's roster is never picked on that floor.

The format is `#` masonry, `.` carved, top of file is north, `@key: value`
headers, `//` comments; the grid self-centres on its bounding box. Text rather
than a Unity scene because scene authoring would need two tilemaps, an editor
bake reading `cellBounds`, and a re-bake on every tweak -- none of which exists
in the project -- whereas a monospace file needs no tooling, diffs in git, and
matches the ASCII the headless report already prints.

This is deliberately a HYBRID and is expected to stay one. Set-pieces carrying
lore weight are drawn by hand; the ordinary furniture of a dead city stays
procedural. The shipped authored set covers every archetype: three plans on
the Sealed Gate archetype (The Last Door, The Watched Road, and the dwarven
toll hold The Toll of the Deep), two Hollow Sanctums (The Kneeling Hall, The
Drowned Choir), two Sunken Plazas (The Counting Floor, The Gallows Court),
two Collapsed Archives (The Ash Stacks, The Burned Wing), and one each of
Ossuary (The Ten Thousand Quiet), Broken Aqueduct (The Dry Span), Guard Post
(The Cold Watch) and Toll House (The Weighing House). The Cold Watch exists
because Guard Post is the only archetype most players ever meet -- it alone
sits on floor index 2's roster -- so it is where a hand-drawn plan buys the
most. All eight new plans keep the archetype's default anchor: the defaults
ARE the road relations, and none of these plans argues with its own
archetype. **Because a site serialises its cells, adding, removing or reordering
authored plans cannot disturb an existing save** -- the stored `variant` is
informational only.

Anchor selection tests band membership AND spacing together, inside the
sampler. Testing spacing afterwards gave each attempt a single chance at it,
and since six of the eight archetypes anchor onto roads their candidates
cluster along the carriageway -- floor index 4 placed four sites of ten and
discarded 116 attempts. No tunable changed; the search did.

`AncientSiteResult` carries per-stage rejection counters and
`TerrainFeatureGenerator` logs them (`logSiteGeneration`, on by default), so an
empty floor reports which stage discarded its sites rather than needing to be
bisected. `DebugRevealAll` and `LogFeatureStats` both cover sites; omitting them was
what made a floor log five sites and show none, since the roads beside them
unfogged and the ruins did not. Sites also draw in the debug overlay via
`debugSiteTile`, and unlike chambers, rivers and roads they draw whether
revealed or not -- the overlay is
a development tool, and an unrevealed site is exactly what one wants to look
at. **The commonest cause of "no sites" is neither: they generate correctly and
are simply not revealed, because reveal is influence-touch only and the band
starts at 15 per cent of the radius. Read the log count before hunting a render
bug.**

**`Dungeon Core / Site Plan Preview`** draws any plan, procedural or authored,
as a colour-coded map at any rotation: walkable floor, floor blocked by the
wall drape, masonry that will be painted, and masonry that never will be. The
three failure modes of site geometry are all invisible in the plan itself and
all used to surface only in game, a full generate-and-look cycle at a time.

**`Dungeon Core / Validate Site Plans`** checks every authored plan against the
drape rule in all eight orientations and reports the worst case, without
entering play mode. Authoring by hand is otherwise the easiest way to draw a
room that looks enterable and behaves as a wall.

**The placement band, and why it is not the whole disc.** Influence reach is
COST-distance, not cells: `baseReach + (level - 1) * reachPerLevel +
(day - 1) * reachPerDay`, spent against terrain resistance, and reveal is
influence-touch only. Working the radial bands through, a plausible late run
reaches roughly the inner 65 per cent of a deep floor's radius; covering the
whole of floor index 4 would take some six hundred in-game days. A site placed
uniformly across the disc is therefore better than even money never to be seen
at all. Sites are confined to 15--65 per cent of radius and the outer third of
each disc is left empty -- which reads correctly anyway. That is past where
anyone went. **Any future deep-floor content is measured against this number
before it is scattered.**

A floor's roster is set by an explicit `useAllArchetypes` toggle, NOT by
leaving the pool list empty. An empty list in the inspector is
indistinguishable from one nobody has filled in yet, which reads as a silent
failure; the toggle makes "everything" a deliberate statement.

**Per-floor rosters and counts**, authored on `AncientSiteProfile`:

| Floor index | Radius | Sites | Roster |
|---|---|---|---|
| 2 | 250 | 1--2 | gatehouse + Archive, Sealed Gate, Guard Post, Sanctum, Toll House |
| 3 | 400 | 3--5 | village + Archive, Guard Post, Sanctum, Toll House |
| 4 | 600 | 9--13 | all eight procedural archetypes |

Both guaranteed set-pieces count toward their floor's site roll. SealedGate
left index 3's pool on purpose: sealed gates read as the dead eras below, and
keeping the archetype would have put the outpost's own authored plan into the
village floor's general pool as a pickable dead ruin.

Spans are 13--21, 20--34 and 22--40 by floor. They were roughly half again
larger at first ship and had to come down: a site reveals ENTIRE, cell count
grows with the square of span, and a span-62 plaza put some three thousand
open floor cells on screen in one rectangle against roughly 100--200 for a
cave chamber. It read as a hole in the fog rather than a building. Keep a span
near twice the chamber box size, not five times it.

Escalation by depth: one lonely structure with no road near it, then the
handful a maintained road still keeps, then a city. Floor index 4 lands
meaningfully denser than floor 3 by area, as the entry requires. Floor index 2
is deliberate reach -- floors 3 and 4 are Diamond- and God-gated, so without it
most players would never meet the Buried Age at all.

**One Sealed Gate on the gatehouse floor is flagged `reservedForOutpost`.** Nothing
reads it. It exists so the dwarves arc can convert a site in place rather than
forcing a save migration.

**Cells ARE serialised**, unlike roads. A road is pure geometry and rebuilds
from its polyline; a site is a composed plan, and re-deriving it would pin the
builder's recipes forever -- an edit to a variant would silently reshape every
existing save. A floor's whole site layer is a few thousand cells, which is
chamber-scale.

**Reveal is per SITE.** A road splits into stretches because a trunk runs rim
to rim and one touched cell would hand over the floor's layout. A site is a
single set-piece and a floor holds a handful, so it reveals entire and gets its
own discovery alert naming the archetype.

**The wisp has two lines.** `site_first` on the first ruin found on a floor.
`site_sealed_gate` on the first Sealed Gate, gated on `CoreMemory.Lived` --
deliberately NOT a memory echo, because entry 34 puts the player's death at an
OPENED SEAL regardless of what they did that last day, so the memory belongs to
every lived core and to no particular deed flag. This is the first content to
touch the blank entry 34 left open.

**Chamber count now scales with floor radius** (rolled count times
`floorRadius / chamberReferenceRadius`, capped by `chamberCountCeiling`). The
authored 3--6 was floor-radius-independent, which left floor index 4 with up to
six cellular-automata caves in a 1.13-million-cell disc. The scale is linear in
RADIUS, not area: an area scale takes floor 4 to roughly ninety-six chambers,
which is a warren. Chamber PLACEMENT stays uniform across the disc and is not
band-confined the way sites are -- that call is open.

**Key files:** `Floors/AncientSiteBuilder.cs` (pure static; `Build`, plus the
plan recipes), `Floors/AncientSitePlanLibrary.cs` (+ `AuthoredSitePlan`; the
ASCII parser), `Editor/SitePlanValidator.cs`,
`Floors/AncientSiteProfile.cs` (+ `SiteFloorEntry`,
`SiteArchetype`, `SiteAnchor`); touches `Floors/TerrainFeatureGenerator.cs`
(`GenerateSites`, `RebuildRoadAnchors`, `ApplyRuinsOverrides`, `UnfogSite`, the
site reveal API, chamber scaling), `Floors/FloorFeatureSaveData.cs` (`SiteData`,
`FeatureType.AncientSite`), `Floors/FeatureRevealController.cs`,
`Floors/TerrainTypeMap.cs` (`ApplyFeatureOverride`), `Floors/FloorRoot.cs`,
`DungeonCore/TerrainResistanceTable.cs` (`siteClaimResistance`, 3 by default),
`Wisp/WispScript.cs` + `Wisp/WispScript.asset`, `TESTING/Commands.cs`,
`Gameplay/BuriedRemainsController.cs` (the ossuary remains guarantee),
`DungeonCore/CaveWallRenderer.cs` + `DungeonCore/CaveWallSheetLayout.cs`
(the ruins family and site paving), `Data/CaveWallSheetLayout.asset`,
`Data/TerrainResistanceTable.asset`.

### Visual identity (SHIPPED)

Sites LOOK built as well as reading built. Three layers, all shipped together
and none of them touching behaviour -- masonry stays mineable, fog stays
one-way, the drape stays two slices deep:

**Wall families.** `CaveWallSheetLayout` carries `families`, a list of
per-terrain `WallFamily` blocks -- terrain key, sheet, flat tint, moss
policy, sixteen cap slots plus four inner corners, 8+8 face slices,
straight-wall variety with a plain weight, and site paving slots -- and
`CaveWallRenderer` resolves the family per wall cell by its terrain type.
Terrain with no entry renders the ordinary stone path; so does a family
whose base cap (mask 11) is empty, keeping the pre-visual-pass look. Within
a present family every slot stays optional: empty caps fall back to the
family base cap, empty faces to the family Straight face. Family cells
render the family's flat tint (WHITE for both shipped entries -- the castle
art is already thematic, and the lavender that sells retinted cave rock as
masonry would muddy real masonry) and roll moss only where `allowMoss` says
so, which no shipped family does. Two entries ship, both sliced from
`castle_interriors.png` by Sprite Editor overrides: RUINS (every cap mask,
all four inner corners, all seven face variants, one pilastered straight
variant, four paving tiles; the mask-15 interior cap deliberately samples
MainLev so the deep interior of a masonry mass still reads as rock) and
DWARVEN MASONRY, born an exact copy of the ruins entry so nothing changed
on screen the day it landed -- repointing its overrides at dwarven art is
pure Inspector work, and Add Family clones the last entry for the next
skin. A THIRD family, HOLY GROUND (terrain 5), is decided and awaiting
art -- canon 20, "The seal's own masonry". Validate Layout reports per
family; `Dungeon Core -> Validate Wall
Families` cross-checks every family terrain against the resistance table (a
missing entry silently mines at 1.0x -- the failure that menu exists to
catch), the pattern map and the spoil ledger. Authoring recipe: Content
Authoring chapter 31.

**Site paving.** The carved interior is painted with the paving variants of
the site's masonry FAMILY, resolved through the same
`TerrainFeatureGenerator.MasonryTypeFor` call that types the walls -- one
decision consulted twice, so paving and masonry can never disagree; the
shipped four tiles, (15,37) and (16,37)-(16,39), currently serve both
families -- one per cell by a spatial hash (no RNG, stable across reloads).
The paint rides `ApplyRuinsOverrides`, which both the fresh-generation path
and the load path call after the disc paint; if the lazy floor-paint
backlog item ever lands, the paving pass must move with it. The carriageway
is paved too: the road cells a site yields at placement -- carved AND
wall-band overlap, so doorway crossings pave with the room -- are recorded
(`SiteData.pavedRoadCells`, appended field, empty in old saves), painted on
the FLOOR tilemap so they carry the floor tint (painting the untinted road
tilemap was the pale-band bug), and cleared from the road tilemap.
`PaintRoadSegment` skips those cells outright, because road segments paint
lazily on reveal and a later reveal must not lay road tile back over the
room floor. The room reads built around the road; a river through the band
still washes it out. Straight-wall variety mixes the plain wall into each
family's pool at its `plainWeight` (C# default 4; the asset ships 8 for
both families, so with the one shipped pilaster variant one straight wall
in nine is a variant); weight 0 restores the all-pilaster look on purpose.

**The decor-prefab hook.** `AncientSiteProfile.siteDecor` maps a plan's
`@name` to a prefab of pure dressing (platforms, stairs, clutter -- no
walls, no floors, no colliders), instantiated once at the site anchor when
the site reveals, and re-spawned on load for already-revealed sites.
`SiteData.planName` (appended field; old saves load "") carries the lookup
key. Rules, validator-enforced where possible: a decorated plan MUST be
`@rotate: no` (the prefab does not rotate with the site); by authoring
convention decor sits on carved floor only and off the central band of
road-anchored plans, because the carriageway subtraction and later mining
can remove cells the prefab thought it was dressing. Content Authoring
guide chapter 29 is the authoring recipe.

Alongside the visual pass, Ruins claim resistance rose from 6x to 8x
(`TerrainResistanceTable`, between Granite at 4 and Holy Ground at 10):
older power resists, but the verb survives -- four shipped plans and the
ossuary remains guarantee depend on mining staying the entry.

The family refactor retypes the living dwarven structures -- the village
hold and the gatehouse outpost -- to `TerrainType.DwarvenMasonry` (appended
value 7) through `TerrainFeatureGenerator.MasonryTypeFor`, the ONE function
both the retype and the paving pass consult. Dwarven masonry claims at 9x:
living, maintained walls outrank dead ruins and stay under consecration.
Its on-screen tell is the influence boundary itself: the ring renderer
writes PROXIMITY to dwarven ground into the field texture's alpha -- 255 on
it, easing to 0 at `holdingsProximityCells` (16) -- and the ring shader lerps
its colour toward `dwarvenRingColor` (warm bronze) by the bilinearly sampled
weight, so the boundary warms as it closes and is fully bronze where it
touches. The flare is deliberately NOT reveal-gated: it is the warning that
lands before the player knows what is out there, and being shapeless it gives
away nothing the granite fill reserves for discovery.

In push mode the same weight also fills a POOL, reusing the exposed fringe's
wash on the unclaimed side, because the flare alone reads as a hairline you have
to know to look for. The pool is CLIPPED to the player's own frontier out to
`holdPoolReachCells` (7), against `chamferOut` on the CPU rather than against
the SDF in the shader -- R is pinned past `sdfRangeCells` (4), so a clip cut
from it could never reach further than four cells however it was set, and the
pool read as too faint to find. The chamfer's cap widens to match, which is
free: the relaxation sweeps the grid twice either way, and the encode clamps, so
measuring further cannot disturb the ring.  Clipping matters because: unclipped, A is a sixteen-cell dilation of
the holding's footprint and the pool would trace the outline of a hold never
found. Clipped, its shape is the frontier's own and it says only "near". It is
suppressed inside a confirmed holding, the pool being the guess and the fill the
answer.

**The frontier flare is GRANITE, not bronze.** This overturns the Wall Family
Refactor's DECIDED bronze flare, on that entry's own reasoning: the colour
caution rules out a bronze AREA, gold ring and gold HUD and amber Earth cores
leaving no room for one, and the pool is an area. Bronze survived while the
signal was a hairline and stops being viable the moment it is not. The two
signals separate by FORM instead, which is the stronger grammar anyway -- the
pool is soft and edgeless, the confirmed holding carries the hard surveyed edge.
`_RingColorAlt` is fed from `holdingsColor` in code rather than from a field of
its own, because two Inspector colours meant to match are two colours that will
eventually not. It is baked once per
floor, holdings being authored at generation and never moving.

The channel choice is forced. The flare reads its weight across the ring BAND,
and the band straddles the boundary, so its channel must mean one thing on both
sides. B cannot: it carries the exposed fringe on claimed ground, and half the
band would read that instead, flickering the bronze with breach state. So A is
proximity, and the granite fill moved to B's unclaimed half -- which suits it,
since the fill needs a hard edge on a binary value and an isoline cut from a
smooth ramp would land out in open ground. The
boundary glows bronze exactly where dwarven ground -- wall or courtyard
-- is what the push takes next and reverts to the core hue once through:
frontier cells only, by
design -- the read is "what am I about to take", not "what did I take".
In push mode (or with the overlay toggled), the same weight also paints
the holdings themselves in cool granite grey -- see the Granite holdings
overlay section, now shipped on this weight -- and the bronze frontier
flare here is the visible touch event that section asked to keep.
The `claimableRingTint` fields on `TerrainResistanceTable` are NOT this
mechanism; they fed the claimable tilemap retired by the influence-ring
rework (0431a991) and sit dormant, commented as such in code. The Deep
Holds buy the spoil at 5 a cell either way (the counter does not ask
provenance), mined dwarven masonry still teaches `ancient_masonry` -- the
dwarves are the Buried Age's heirs -- and the first-claim toast names the
terrain "dwarven masonry". Dead sites stay Ruins, which keeps the ossuary
guarantee's reasoning true.

### The generator (SHIPPED)

Roads are a carved feature of `TerrainFeatureGenerator`, alongside chambers and
rivers: `FeatureType.Road`, registered as natural floor, revealed by influence
touch, framed by the cave-wall renderer. `CaveWallClassifier.IsSolid` keys off
`minedTiles`, so passing the carriageway to `MarkNaturalFloor` is the whole of
what makes a road read as open ground -- no classifier change was needed.

**Geometry is NOT `BuildRiverPolyline`.** That function picks a rim point,
heads across on the opposite bearing and wanders until it leaves the disc.
Right for a river, which may end anywhere; wrong for a road, which must ARRIVE
-- a rim-to-rim trunk that wanders out early is not a trunk. `RoadNetworkBuilder`
builds each edge between two FIXED endpoints with a perpendicular meander
pinned to zero at both ends, so an edge always lands on its target. The meander
is the same idea at a much lower amplitude.

**`RoadNetworkBuilder` is a pure static.** No scene, no floor, no tilemap, no
singleton. That is what lets the dev road report in `Commands.cs` generate and
measure a whole floor's network without instantiating the floor -- which matters
on floor index 4, where the terrain pass alone paints 1.44 million cells. On any
floor without a core cavern or entrance cave, roads are the first consumer of
the floor's `System.Random`, so a report seeded with the floor seed reproduces
the in-game network exactly. That does not hold on floor index 0.

**Two modes**, chosen per floor on a `RoadNetworkProfile` asset (the floor
template prefab is shared across floors, so per-floor settings cannot live on a
component). `Trunk` lays one road rim to rim, its chord re-rolled until it
clears the core's exclusion disc. `Network` scatters junction nodes, spans them
with a minimum spanning tree, adds the shortest few non-tree edges as loops,
sends rim-bound trunks outward from the outermost junctions, and hangs broken
spurs off random ones.

**Roads stop short of the bedrock rim** by an authored `rimMargin`. They cannot
be driven through it: `MarkNaturalFloor` refuses to open bedrock, so a road in
the rim would sit in the lookup as `Road`, reveal, and stay solid rock. A
rim-bound trunk therefore ends in collapse instead -- which reads better anyway:
the road ran on, the rim swallowed it. The `IsEntranceCave` exemption inside
`TileInfluenceManager.IsBedrock` is the only sanctioned hole in the rim and
stays that way.

**A broken end** is the resting-pocket trick: the polyline is built in full and
the last `brokenGapCells` centreline cells are simply never opened. They stay
ordinary stone, so the road visibly stops rather than fading out.

**One alert per floor.** Reveal is per segment, but the DISCOVERY BANNER is not.
A floor holds tens of road segments by construction -- floor index 4 generates
around eighty-five -- so alerting per segment would fire a banner every forty
cells of influence and turn the find into noise. The first segment revealed on a
floor speaks; every later one reveals silently. Rivers and chambers keep their
per-feature alerts because a floor holds a handful of each.

**Reveal and claiming are per SEGMENT**, not per road. A trunk splits into runs
of `segmentLength` centreline cells, each with its own id; `featureId` in the
feature lookup is a segment id. Unfogging an 800-cell trunk from one touched
cell would hand the player the floor's layout for free. The SEAM between two
segments is frayed rather than square: chunks overlap by
`segmentSeamJitterCells` (3) and a stable per-cell hash quantised to 2x2 blocks
settles who keeps each contested cell. A square cut ran the boundary dead
straight across a five-wide carriageway, and a revealed stretch therefore ended
in a fog edge that read as a wall, mid-corridor, with nothing to justify one.
This moves an OWNERSHIP line only: every cell is opened, revealed and framed
exactly as before.

**The reveal edge is softened from the INSIDE.** `DungeonShadow` runs a
frontier fade: a multi-source walk over `MinedTiles` from cells abutting fog,
capped at `frontierFadeTiles` (4), easing light down to `voidLightFloor`. The
seed test probes to CHEBYSHEV 2: every reveal path carries a one-cell border, so
the wall fronting open floor is revealed and fog begins two cells out from mined
ground, never one. Seeding at distance one found nothing on any floor and the
fade did nothing at any setting -- silently, which is why
`Log Frontier Fade State` now reports the seed count. Open
ground therefore darkens as it approaches the unexplored and meets it at the
fog's own colour, since `fogMatchesVoid` already paints the fog
`DeepVoidColor`, which is that same level. It mirrors the breach fade in the
same file, seeded from the other kind of boundary. Confined to `MinedTiles` by
design, not by thrift: that set is exactly what `CaveWallRenderer` has framed,
so the fade can never touch ground with no wall drawn on it.

**The next stretch is PREPARED, so its fog can thin.** `roadPrepareCells` (6)
of road past the revealed frontier forms a band: `CaveWallRenderer` frames the
rock beside it -- the same explicit pass revealed river water already gets, road
never having had one -- and the fog over exactly that band thins by depth,
quadratic, reaching solid at the band's edge. One set feeds both, so the lit
region and the framed region cannot drift apart. The band is NOT marked mined:
`Minimap` paints every mined tile as floor, so that would draw the network on
the minimap from turn one, which is the layout leak per-segment reveal exists to
prevent. Bounded to the frontier, so the cost tracks how far the player has got
rather than the size of the network, and cells that fall out of the band as it
advances have their alpha restored.

The band also joins `DungeonShadow`'s light map, at `unclaimedLight` where it
meets the revealed stretch and ramping to `voidLightFloor` at its far edge --
the same span the fog thins over, so light and fog agree instead of fighting.
Without it the band rendered BRIGHTER than the revealed road beside it, because
the shadow paints only `MinedTiles` and the band is deliberately not mined: raw
floor tile under thin fog against ground darkened to `unclaimedLight`. Its wall
caps need no special handling; the cap pass snapshots `baseLight`'s keys.

The band walk and the fade must share a CONNECTIVITY. The walk ran on four
neighbours while the fade collected from eight, so road joined to the band only
diagonally -- a carriageway's edge, every bend -- was lit by a neighbour's depth
yet absent from the band, and the shadow never darkened it. Both are eight now,
and the fade additionally refuses to thin over road the band does not cover, so
a future disagreement fails to dark rather than to bright.

The frontier fade seeds only where fog covers ROAD or RIVER -- ground a passage
continues into. Fog behind a wall wants no softening: the wall is the edge and
it is drawn. Seeding on any fog made a seed of every cell beside every wall, so
whole floors dimmed uniformly with no gradient left to read.

**SHIPPED: junction shaping.** `RoadNetworkBuilder.Dilate` uses one straight
kernel, so two five-wide carriageways crossing dilated to a roughly nine-by-nine
square with square corners, and a junction read as a plaza rather than as a
widened meeting. `RoadNetworkBuilder.FilletJunctions` now runs a morphological
CLOSING in a box around each junction node -- dilate the carriageway by a disc of
`junctionFilletRadius` (3), erode by the same disc -- which fills every concave
notch smaller than the disc and leaves convex corners untouched. That is exactly
a kerb radius, and it also gives the frayed seam something better to fray
against.

ADDITIVE, and the choice matters. Chamfering the outer corners instead would
REMOVE cells, and road cells regenerate from the polyline on load while the mined
set is restored from the save file -- so every removed cell would come back
mined, revealed and no longer typed as road, drawing as bare floor beside the
carriageway. Adding cannot produce that state.

Junction nodes have ONE derivation, `RoadNetworkBuilder.JunctionNodes`, called by
the generator, the load path and the headless road report alike. Shaping changes
which cells are carriageway, so two derivations disagreeing by a cell would
repartition segments differently on load and move ownership under a save. The
fillet runs at the end of `RebuildRoadCells` -- before site generation, which
already subtracts road cells from its plans, so a filleted cell can never land
under masonry on the load path -- and each new cell is handed to the lowest
segment id holding an 8-neighbour. Lowest rather than nearest because it is
stable under a reload, the same reason the frayed seam uses a hash rather than a
coin flip.

One consequence, accepted rather than migrated: an existing save re-rasterises
its roads with the new shape, so a stretch the player HELD may come back a few
cells short of held until they claim the corners. The toll is a live test rather
than persisted state, so nothing is lost but the availability of the verb.

**REJECTED: feathering the fog's alpha at the reveal edge.** It looked like the
obvious answer, reusing entry 24's treeline curve, and it is a trap.
`fog.GetTile(cell) == null` IS the reveal flag -- `ReachabilityDirector` and the
consistency report both ask it -- so a part-transparent tile reads as
UNREVEALED to everything while the player sees through it. Unrevealed ground is
never prepared to be looked at: `CaveWallRenderer` frames only solid cells that
are claimed or 8-adjacent to mined floor, so the fade band held no wall sprites
at all and the disc-wide floor tile showed through as void. Any future partial
transparency needs the framing extended to the fog boundary FIRST, and that is
a change to the wall renderer, not to the fog. WALL FRAMING is the one
thing that is not per segment: `CaveWallClassifier.IsSolid` exempts road cells
exactly as it exempts river cells, discovered or not. It has to. Reveal calls
`MarkNaturalFloor` on the revealing segment alone, so without the exemption the
next stretch still read as solid rock, and the renderer framed the join with a
cap and a face straight across the carriageway. The exemption changes framing
only and unfogs nothing, so the anti-leak guarantee above still holds.

PAINT is not per segment either. `PaintAllRoads` lays the road tilemap over the
WHOLE network at generation and on load, because fog is what hides an
undiscovered road and the tilemap carries no secrets by itself. Painting per
segment left the one-cell halo `UnfogRoadSegment` clears spilling onto the next,
unpainted stretch, and revealed-but-unpainted ground shows as bare floor. It
runs after `PaintSitePaving`, which is what fills `sitePavedRoad` -- paint that
band twice and the pale band comes back.

A revealed HOLD reveals the carriageway through it. Site placement subtracts the
band from `site.cells` (the road is built around a site, not cut through it), so
`UnfogSite`'s halo never covered it and it waited on its own road segment --
leaving a fogged trench across a village the player had fully discovered. It is
marked natural floor at the same moment, or the rock flanking it has no mined
neighbour and goes unframed. Segment ids advance even
where a river ate a whole stretch, so saved reveal state stays aligned.

**Cells are never serialised.** `RoadData` stores the polyline, width, segment
length, broken gap, and the floor centre and clamp radius captured AT
GENERATION; one shared rasteriser (`Centreline` + `Dilate`) runs on both fresh
generation and load, so the two can never disagree, and a later edit to the
profile cannot change how an existing save rasterises.

**Claim resistance** is `TerrainResistanceTable.roadClaimResistance` (8 by
default), routed through `FloorRoot.GetClaimCostMultiplier` beside the river and
chamber cases. This is rung 1 of the warning ladder below, and it exists from
the substrate on: the player feels the road push back before anything in the
game says a word about it.

### Carve precedence (DECIDED, was Open)

`GenerateNew` runs, in order: **core cavern and its tunnels, the entrance cave,
ROADS, chambers, rivers.**

- Core cavern, tunnels and the entrance cave are carved first and keep their
  cells outright; roads route around `reservedCoreCells`.
- Chambers yield to roads. A cave that opens onto the carriageway reads fine,
  but a cell has one owner, and the road was there first. The connectivity pass
  re-runs after the subtraction, since a road crossing a chamber can otherwise
  strand a sealed islet.
- Rivers take their cells back from roads. A river cuts through a road, not the
  reverse; the washed-out crossing is free storytelling from the ordering alone.
  No ford, no bridge, nothing authored -- the river simply wins.

**Road spacing (SHIPPED).** Two geometric rules, per-floor on the profile.
`minJunctionAngleDegrees` (25) is the smallest angle permitted between two
roads meeting at one junction; `minRoadSeparation` (20 cells) is the smallest
distance permitted between two roads sharing no junction. Measured over 300
generated floor-4 networks, the unconstrained builder produced **4.7 pairs per
floor under 25 degrees** -- long thin slivers, worst case 0.0 degrees, two
roads laid exactly on top of each other -- plus 0.6 pairs of near-parallel
roads. Both rules together take those to zero and cost about 0.2 loop edges.

The dominant cause was `ExtraLoopEdges`, which returns the SHORTEST unused node
pairs: those are precisely the pairs already joined by a short tree path, so
each one closed a thin triangle. Spurs were second, firing at a fully random
bearing from a random junction. **Spanning-tree edges are never refused** --
connectivity beats spacing, and refusing one could orphan a junction; loops,
rim trunks and spurs each draw from a wider candidate list than they need so
the rules trim rather than starve. A graph-hop constraint was tested and
rejected: it starved loop edges (4.0 down to 2.5 per floor) while adding
nothing once the angle rule was in.

**Key files:** `Floors/RoadNetworkBuilder.cs` (pure static; `Build`,
`Centreline`, `Dilate`), `Floors/RoadNetworkProfile.cs` (+ `RoadFloorEntry`,
`RoadMode`); touches `Floors/TerrainFeatureGenerator.cs` (`GenerateRoads`,
`RebuildRoadCells`, the road reveal API), `Floors/FloorFeatureSaveData.cs`
(`RoadData`, `RoadKind`, `FeatureType.Road`), `Floors/FeatureRevealController.cs`,
`Floors/FloorRoot.cs`, `DungeonCore/TerrainResistanceTable.cs`,
`TESTING/Commands.cs` (floor generation + the headless road report).

### The dwarves -- faction and outpost (SHIPPED, part 1)

Part 1 of the dwarves arc is BUILT: the faction (see entry 7), the guaranteed
outpost, its authored plan, and the controller that puts the Deep Holds on the
board when the player finds it. The VENDOR, the spoil economy and the
dwarven traps have since SHIPPED in part 2 below; the granite overlay, road
claiming and the caravans have also SHIPPED (this entry's later sections and
The Living Holds step 7) -- the NOT-built sentence that stood here predated
them.

**FLOOR PLAN CORRECTION (applied after part 2).** The dwarven trunk and
gatehouse were originally authored on floor index 3 and MOVED DOWN to floor
index 2. The corrected plan is:

| Code index | Floor | Contents |
|---|---|---|
| 0 | 1 | starting floor |
| 1 | 2 | once the player has a handle on the dungeon |
| 2 | 3 | **dwarven highway + gatehouse** |
| 3 | 4 | dwarven village + highway + a handful of sites (UNBUILT) |
| 4 | 5 | bottom floor, most Buried Age sites |

Floor index 3 currently has **no road entry and no site entry**, which is the
correct way to hold the slot: a floor without an entry simply gets no features.

The renumber itself was one constant (`DwarvenSpoil.MinFloorIndex`) and two data
assets -- nothing else in the codebase is floor-keyed, because
`DwarvenOutpostController` searches all floors for the reserved site rather than
assuming one. The real work was that **index 2 is radius 250 and index 3 was
400**, so every proportional figure in the moved configuration was 60 per cent
too large. Scaled by 0.625 where proportional (`minSpacing` 110->70, `minSpan`
20->13, `maxSpan` 34->21, `meanderStep` 32->20); left alone where physical
(`trunkWidth` stays 5 -- a road's width does not shrink with its floor;
`segmentLength` stays 40 so reveal granularity is consistent across floors).

`bandInner` on this floor is **0.30, not the 0.15 every other floor uses**, and
the reason is spatial crowding rather than the core reservation --
`exclusionRadiusFromCenter` is only 8, so the hold clears it either way. The
real problem is that 0.15 puts the inner edge 37 cells out while the hold is 39
cells across: on a 250-radius floor that drops the landmark practically on the
player's doorstep, overlapping the arrival area the up-stairs open into. At 0.30
the gatehouse spans radius 52 to 185 against a usable disc of 238, which leaves
clear floor on both sides.

`minSites`/`maxSites` dropped from 3-5 to **1-2**, and the guaranteed outpost
COUNTS TOWARD that target (`PlaceOutpost` adds to `result.sites` before the fill
loop reads it). So the gatehouse floor is the gatehouse plus at most one ruin,
which is the intent: the Buried Age sites ramp on the floors below it.

Consequence for the vendor: the shelf now opens at **Gold** tier rather than
Diamond. That makes the tier-3-or-above book rule SAFER rather than looser -- a
Gold core has done less research, so a tier-3 node is more likely still
unlearned. Prices were deliberately not rebalanced; spoil is the matched income
stream and it starts on the same floor.

**The outpost is GUARANTEED, and placed first.** `SiteFloorEntry.reserveOutpost`
no longer latches onto whichever Sealed Gate the shuffle produced. It now runs
`AncientSiteBuilder.PlaceOutpost` ahead of the general loop, on its own
240-attempt budget, and takes the chosen plan out of the pool. The old rule
failed two ways on the gatehouse floor: the roster holds five archetypes and the
floor rolls three to five sites, so a run could finish with **no Sealed Gate
and therefore no dwarves**; and the Sealed Gate's `RoadEnd` preference
resolves, on a rim-to-rim trunk with no broken ends, to the two rim endpoints,
both outside the 0.15-0.65 band, so it degraded to a **free pick** and stranded
the outpost away from the road entirely.

**It anchors `AlongRoad`, and that is what puts the road THROUGH it.** No new
anchor kind was needed. Anchors CENTRE a plan on the sampled cell, and the road
anchor list handed to the builder is a thinned CENTRELINE sample -- so
`AlongRoad` already lands a plan astride the carriageway. `outpostArchetype`
(SealedGate) and `outpostAnchor` (AlongRoad) are explicit serialized fields on
the floor entry rather than hardcoded.

**The plan is hand-authored:** `Sites/Plans/DwarvenOutpost_TheTollOfTheDeep.txt`,
a toll hold with gate gaps five cells wide on ALL FOUR bearings. Four gates
because the anchor is a centreline cell whose local road heading is whatever
the meander made it -- a hold with one gate would be butted into by three roads
out of four. Procedural Sealed Gate variants are deliberately not used: they
were composed to read as SEALED, which is exactly wrong for the one gate that
is open.

**Scale, and the subtraction that corrects it.** The plan draws 521 carved
cells against 322-394 for the other authored plans. That is intentional:
`TerrainFeatureGenerator` subtracts the carriageway from a site's cells after
placement, and a five-wide road across a thirty-nine-cell span takes roughly
150 back, landing the built site near 370. The builder's own 12-cell floor is
checked BEFORE that subtraction, so an outpost can pass there and die after --
which now logs an ERROR instead of vanishing, because a floor shipping without
dwarves must never be silent.

**The controller polls; it does not listen.** `DwarvenOutpostController` is a
scene singleton with no per-floor wiring, checking once a second until the
outpost is established and then stopping for good. An event on
`RevealSite` was rejected: the LOAD path calls `UnfogSite` directly for every
saved id and never touches `RevealSite`, so an event would fire for a player
who discovers the outpost this session and stay silent for one who reloaded
afterwards -- the gatekeeper would disappear on Continue.

The gatekeeper stands at the centroid of the site's carved cells SNAPPED to the
nearest carved cell, not at the stored anchor: the anchor is the plan's centre
before subtraction, which on an outpost is the middle of the road.

### The dwarves -- the vendor and the spoil economy (SHIPPED, part 2)

Part 2 is BUILT: the shop decoupling, the Deep Holds' counter, three
bought-only traps, and the spoil invoice. The granite boundary, road
claiming and the caravans have since SHIPPED (this entry's later sections
and The Living Holds step 7); the DESIGN sentence that stood here predated
them.

**The shop is decoupled.** `IShopVendor` (`UI/IShopVendor.cs`) carries
`ShopTitle`, `CurrentStock`, `PriceOf` and `TryPurchase`. `MerchantShopUI` now
holds the interface rather than a `WanderingMerchantController`, and both
vendors implement it. `PriceOf` exists rather than the UI reading
`StockEntry.price` because the dwarves discount by regard; a vendor that does
not discount returns list price and nothing downstream knows the difference.
Done at the second vendor rather than the third, which is what canon asked.

**The grant channel is shared.** `TraderStockCatalog.ApplyPurchase` owns the
Pattern / Book / Unlock switch. It takes no payment and removes nothing from
stock -- the vendor owns both, because only the vendor knows its own price. The
switch belongs to the stock KIND, not to whoever stands behind the counter.

**`StockType.Unlock` is appended** (never reordered; it serialises into the
catalog asset as an int). It sets a bare `UnlockState` key with no research node
behind it. That is the whole gating mechanism for the dwarven traps: a
`TrapDefinition.requiredTechKey` is only ever tested through
`UnlockState.IsUnlocked`, in `TrapSelectionUI` and as a placement backstop in
`DungeonBuildController`, so a key no node owns gates a trap that can only be
bought.

**Two catalogues, deliberately.** `TraderStockGenerator` rebuilds the wagon from
every non-terrain `PatternDefinition` in the folder, so anything the dwarves
sold as a pattern would appear on the wagon too. The Deep Holds get
`DwarvenStockCatalog.asset` and their own menu item. **They sell no patterns at
all** -- machinery and books only, which keeps the two vendors legible: the
merchant sells knowledge, the dwarves sell machinery.

**The shelf does not rotate.** Everything they hold is out from the day the
outpost is found; sold is gone for good. Rotation is what makes the wagon feel
like a wagon, and copying it here would erase the difference between a visit and
a shop. It is rebuilt on each open so regard-gated stock appears the moment it
is earned.

**The three traps** are data variants of shipped behaviours -- no new
`TrapBase` subclass exists. All `DungeonType.None`: the six elemental locks are
a core's signature and dwarven engineering has no business inside them.

| Trap | Behaviour | Mana | Cap | Dmg | Cooldown | Signature | Price |
|---|---|---|---|---|---|---|---|
| Ballista Post | Crossbow | 26 | 4 | 22 | 5.5s | range 6.5 | 320g |
| Deadfall | Pitfall | 20 | 3 | 26 | 8s | slow 0.25 / 3s | 260g |
| Chainline | ScatterTrap | 18 | 3 | 0 | 6s | scatter 9s | 380g, Trusted |

Tuned deliberately heavier than the researched roster they echo (Crossbow
16/3/9/2.4, Pitfall 10/2/8/4, Sundering Plate 12/2/0/4): these cost gold and a
faction, and must feel like it.

**The books obey two rules, and the second was learned by getting it wrong.**

1. **Affinity None only.** An affinity-gated node is exclusive to its core type
   and a book granting one to a mismatched core would hand out something that
   core may never hold.
2. **Tier 3 or above only.** The outpost is on floor index 2, which is
   GOLD-gated -- the third tier of five. A tier-2 node costs 15 points
   behind a single Rare pattern and is researched hundreds of days before a core
   can descend that far, at which point `IsOwned` filters the book off the shelf
   and it is dead stock that never appears. The first slate had Vaulted Reserves
   and Hall of Trophies on it (both tier 2, 15 points) and both were pulled.

The defect is invisible in play -- an over-early book does not error, it simply
never shows -- so `DwarfStockGenerator.ValidateBookTiers` checks tier, affinity
and node existence at authoring time and logs an error rather than leaving it to
a test plan.

On the shelf: Trapwright's Craft (T3, 400g), Proving Grounds (T3, 440g), The Far
Marches (T3, 520g), Master Trapwright (T4, 600g). Prices run past the 500g base
treasury cap without apology: treasuries are a tier-2 research node and any core
that has reached Diamond has had them for a long time.

**Spoil is an invoice, not a stockpile.** Canon 14 closed the stockpile
question, and this does not reopen it: there is no inventory and nothing is
carried. Mining Granite or Ruins on floor index 2 or below, after the outpost is
found, accrues a single int of gold OWED (3g and 5g a cell). Clicking the
gatekeeper settles it before the shop opens -- collecting your own money should
not be a puzzle with one answer.

The rate is anchored to cost rather than chosen: mining is 5 mana times terrain
resistance, and Granite and Ruins resist 4.0 and 6.0, so 20 and 30 mana a cell.
3g and 5g holds the return at a fixed ratio to the cost, which is what stops
mining-for-gold ever beating mining-for-room.

`AddGold` CLAMPS at the treasury cap rather than refusing, so an overlarge
settlement loses the overflow. Recorded as accepted behaviour, not a bug: the
treasury exists to raise that cap and Vaulted Reserves is on the dwarves' own
shelf.

**Standing moves on trade.** Selling pays `2.5` standing per 100g, buying `1.0`
-- selling is the exchange where you carry something to them rather than the one
where you take something away. Both route through the shipped
`FactionSystem.AddStanding`.

**The mining hook is per-floor and guarded.** `DwarvenOutpostController` hooks
`OnTileMined` across `FloorManager.AllFloors` with named delegates held per
influence manager, exactly as `BuriedRemainsController` does.
`TileInfluenceManager.Instance` is **last-floor-wins** -- `Awake` sets it
unconditionally on a per-floor component -- so the singleton is useless here.
`HandleMined` returns early on `DungeonSaveController.IsLoading`, because
loading replays mined cells and would otherwise mint the whole excavation again
on every reload.

**Key files:** `UI/IShopVendor.cs`, `Gameplay/DwarvenSpoil.cs`,
`Gameplay/TraderStockCatalog.cs`, `UI/MerchantShopUI.cs`,
`Floors/DwarvenOutpostController.cs`, `Editor/DwarfStockGenerator.cs`,
`Editor/TrapContentGenerator.cs`.

### The dwarves -- the village (SHIPPED, part 3)

The floor plan's last slot: floor index 3 (radius 400, Diamond-gated) now
carries the sparse living network and the hold it serves. The gatehouse above
guards the way DOWN to something; this is the something.

**The road layer.** `RoadNetworkProfile` gained an index-3 entry: Network mode
tuned sparse -- junctionCount 4 at spacing 90, extraLoopEdges 1, rimTrunkCount
2, brokenSpurCount 2, meanderStep 32 / amplitude 5 (the values this radius
carried before the correction). The offshoot ladder across the deep floors is
deliberate: 0 (index 2's lone trunk), 4 (here: 2 rim trunks + 2 broken spurs),
7 (index 4). Measured through the real builder over 200 seeds: junctions
always 4, roads 7--8 (the MST's 3, plus the 0--1 loop edges the angle rule
permits, plus 2 rim trunks and 2 broken spurs), carriageway ~7,700 cells.

**The site layer.** `AncientSiteProfile` got the index-3 entry back --
minSites 3 / maxSites 5, band 0.15--0.65, minSpacing 110, spans 20--34, the
pre-correction numbers restored -- with two changes: SealedGate is out of the
pool (see the roster table note above), and the entry carries the new
guarantee flag `reserveVillage` (plus `villagePlanName`, now an optional
pin -- see the guarantee below).

**The guarantee.** `PlaceVillage` in `AncientSiteBuilder` mirrors
`PlaceOutpost`'s first-and-loud contract with one deliberate difference: the
plan comes from the authored set, never the pool. Every authored
DwarvenVillage plan is a candidate and one is ROLLED seeded per world, so
playthroughs rotate holds. A non-empty `villagePlanName` pins the roll to
that one plan (testing); empty -- the shipped state -- means roll, so adding
a fourth hold someday is one plan file with the archetype, zero config. The
pick index persists through `SiteData.variant` as a breadcrumb, and the
report names the chosen hold -- `village: placed (The Deep Market)` -- so
rotation verifies headlessly by stepping `roadReportSeedOverride`.
`SiteArchetype.DwarvenVillage` (appended, = 8) sits in no roster and has ZERO
procedural variants -- `VariantCountFor` is a switch now, zero means
authored-only, and `BuildPlanPool` no longer clamps variant counts to a
minimum of one -- so the fill loop cannot serve it and there is no pool
bookkeeping on success. The village counts toward the floor's site roll
exactly as the outpost does. `SiteData.reservedForVillage` is APPENDED
(JsonUtility-safe; no older save can hold a village because floor features
persist) and `TerrainFeatureGenerator.GetVillageSite()` mirrors the outpost
accessor. Measured over 200 seeds through the real pipeline: 200/200 placed,
0 duplicates, 0 post-subtraction walkability failures.

**@general: no.** The authored-plan format gained one header: a plan marked
`@general: no` may only be placed by a guarantee pass, and `Build` strips such
plans from the pool after the guarantees run. The outpost's plan now carries
it -- without it, floor index 4's all-archetypes roster placed the one OPEN
hold as an ordinary dead ruin on the bottom floor. Measured: 0 leaks in 200
index-4 seeds, sites still 9--13. `TryParseArchetype`'s cap moved to the enum
tail so DwarvenVillage parses; `BuildPlanPool`'s useAllArchetypes cap
deliberately stays at TollHouse, so an authored-only archetype is opted in per
floor and never swept in by "all".

**The plans.** Three holds rotate, sharing one contract: four 5-wide gates
on every bearing (an AlongRoad anchor lands on any local heading -- the
gatehouse arithmetic), interiors partitioned so no single empty rectangle
reads as a hole, and THE DOOR RULE recorded in every file -- DECLARED doors
and passages are 3 cells long, because the wall drape
seals 2-long gaps that rotation turns east/west (the Hearth's draft lost a
45-cell interior to that; the Market's draft, a 66-cell stall lane when the
rule was not yet applied to furniture). Each hold validated in all eight
orientations before and after 5-wide road cuts on four headings: zero
pockets, zero fragmentation. Measured over 200 seeds the roll splits
65/62/73:

- `DwarvenVillage_TheTerracedVein.txt` -- the mining town. 57x57, 2314
  carved as drawn, 1749--2214 after the carriageway. Terrace stacks, the
  quarry yard with ore stacks and spoil ridge, the winch house and its
  head-frame posts.
- `DwarvenVillage_TheDeepMarket.txt` -- the crossroads town the sparse
  network implies. 71x49 -- deliberately not square, so rotation deals
  portrait and landscape markets -- 2374 carved, 1873--2306 after.
  Crate-stacked warehouses, the grand trade hall, the stall-rowed market
  yard, shop terraces.
- `DwarvenVillage_TheShrinehold.txt` -- the old-faith village, entry 20's
  deep faith on screen. 61x61, 2588 carved, 2002--2443 after. Cloister
  terraces, the refectory, the shrine precinct with votive pillars around an
  inner sanctum and altar, the bell court with belfry, keeper's house and
  grave rows.

The first-ship hold, `DwarvenVillage_TheHearthOfTheDeep.txt` (41x41, 1008
carved), RETIRED from the roll after reading too small at play. The file
stays on disk as the small worked example Authoring chapter 28 reads, but it
is off the profile's `authoredPlans` -- which is all retirement takes.

**The controller.** `DwarvenVillageController` -- scene singleton beside
`DwarvenOutpostController`, and the same poll rationale, recorded on both:
the load path unfogs sites without events, so listening would stay silent
after a reload. On establish it rolls the settlement's name from an 8-name
roster seeded by floor seed and site id (deterministic, so no save field),
fires `FactionIntel.NotifyEncounter(Dwarves)` -- idempotent, and deliberately
fired here as well as at the gatehouse, because stairs are player-placed and
a run can genuinely reach the village first -- stands its villagers
on interior cells picked by the builder's own walkable rule (which also keeps
them off the carriageway), raises a Discovery alert naming the hold, and
speaks `village_first` once. Clicking a villager repeats `village_greeting`.
No vendor: they trade at the gate, they live here. Villager art is a
variant list (`villagerSprites`), dealt by a seeded round-robin over a
shuffled copy so counts stay as even as the list allows; null entries are
skipped and an empty list draws nobody. Since The Living Holds (step 7) the
villagers WALK: `DwarfWalkerPuppet`s hopping between lane cells found by a
bounded 4-neighbour BFS inside the site's own cells (200-expansion cap), so
nobody strays onto the carriageway or into the rock; 4-10s pauses between
hops of up to 6 cells, walk speed 1.2, frozen at night on the day clock.
`villagerCount` ships defaulting to 8. The discovery alert
re-fires once per session after a reload, exactly as the outpost's does --
accepted.

**Key files:** `Floors/DwarvenVillageController.cs`,
`Floors/AncientSiteBuilder.cs`, `Floors/AncientSiteProfile.cs`,
`Floors/AncientSitePlanLibrary.cs`, `Floors/RoadNetworkProfile.cs`,
the three rotating `ScriptableObjects/Sites/Plans/DwarvenVillage_*.txt`
holds (the retired Hearth stays on disk beside them).

### The dwarves (DECIDED in shape)

The outpost is the INHABITED Buried Age site -- the one Sealed Gate that is
not sealed. Outposts exist because of roads, so the road runs THROUGH the
outpost, not past it: they hold a toll gate, which is why they are rich, why
they trade, and why they care what the player does to their road.

**Why they would deal with a core at all.** Everything else in the world
arrives to kill it. The dwarves never went up, so they never learned the
Church's version -- and entry 20 records that the old deep-faith held that
some dead are reborn as cores. They are not friendly out of pragmatism but
out of OLDER LOYALTY. This gives the deep-faith lore an on-screen population
and ties the outpost to 19A's audiences.

They start neutral-curious rather than hostile. A faction that does not want
the core dead is novel in the roster and gives the alignment axis (entry 6)
something new to push against.

**Trade axis.** The surface merchant (28A) sells KNOWLEDGE -- patterns,
research books, catch-up slots. The dwarves should sell MATERIAL: trap
components, room upgrades, deep-floor-only furniture. Two channels that stay
meaningful independently, so losing the surface camp does not lock the player
out of everything.

**The Living Holds (SHIPPED, step 7).** The dwarves who walk: villagers,
patrols, and the caravan with its verbs. Everything below is as-built.

*The rail.* No walker pathfinds. `MarkNaturalFloor` put every road cell in
`minedTiles`, so the pathfinder would carry a dwarven wagon through the
player's own tunnels whenever they were shorter. Walkers follow the road's
own centreline instead: `DeepRoadGraph` (pure static, like the road builder,
so the Commands report can measure routes headlessly) rebuilds the network
from saved `RoadData` -- rails are `Centreline()` runs, nodes are endpoint
clusters merged at radius 6 (the exact `RebuildRoadAnchors` rule), routing is
node-BFS with Bresenham stitches across junction mouths. Rim ends -- where a
road leaves for another floor, in fiction -- are identified from save data
alone: kind Trunk with the RAW polyline end within 2 cells of `clampRadius`
(where `OnCircle` put it; the broken gap never moves the raw endpoint, so
floor 3's collapsed rim trunks qualify exactly as floor 2's unbroken one, and
spurs never do). All walkers are `DwarfWalkerPuppet`: one SpriteRenderer,
distance-along-path movement, flipX and a small sine bob, scaled time, and
NEVER registered with `FloorEntityRegistry` -- dwarves are not combat
entities; a rob is a verb, not a fight. Fog hides walkers on unrevealed road
for free (the shadow tilemap renders above the Player layer).

*Patrols* (`DwarvenPatrolController`, stateless -- ambient texture re-derives
each session). One paces a 60-cell beat either side of the outpost through
the gatehouse, day and night; two more set off from the village in opposite
directions and wander the whole network, picking a random connected road at
each junction, no immediate about-face where the junction offers anywhere
else. At a broken end the patrol walks to where the road stops, pauses 3s
facing past the collapse along the road's own bearing, and turns back --
warning-ladder rung 5 rehearsed on natural geometry, for step 8 to re-aim.
Reactions (0.5s throttle): any adventurer within 8 halts the patrol to
watch; a Hostile-faction adventurer sends it withdrawing toward home until
nothing hostile is within 14. Patrol speed is a plain 2.2: a loop has no
arrival to keep, so the authored-days constraint does not bind it. First
sighting on revealed road speaks `patrol_first` (wisp-persisted once).

*The caravan* (`DwarvenCaravanController`, scene singleton). The journey is
two legs and a gap, because the floors are different and no walker crosses
floors: outpost to a rim end (0.75 days authored), unseen transit (1 day),
bearing-matched rim trunk down to the village (1.5 days), a dwell (1 day),
then the same road home. Rim ends pair by bearing so the wagon leaves and
arrives on "the same" road. Travel is authored in DAYS with speed derived
(route length over authored days times `DayDuration`) -- the constraint this
entry set, kept. Walking is day-phase only; the wagon visibly camps at dusk.
Transit and dwell elapse in calendar time (day plus night) -- nothing is on
screen to camp. One caravan at a time; the next departs 2-4 days after
completion, +4 more after a robbery; departures need the Holds met (outpost
or village established), both floors generated, and standing Tier 0 -- at
Tier 1+ the road goes quiet while the gate still trades, which is the way
back. Cargo rolls 80-200g. The column is three walkers plus an optional
cart (carts do not bob), spaced 1.6 along the path; within 10 units of a
Hostile-faction adventurer the wagon hurries at 1.5x. With no walker sprites
assigned the system stays DORMANT and warns once -- an invisible wagon that
can be robbed blind is worse than none.

*Persistence is the walking clock.* The save carries walked seconds this leg
(accrued only while actually walking) and phase seconds; position is a pure
function of those. The fork's letter said "pure function of the clock" --
built against the wall clock, every halt (night, vignette, open panel) ends
in a teleport catch-up, so the clock persisted is the walking clock itself:
reload restores the wagon to the cell it stood on, halts simply do not
accrue, and no per-frame progress field exists to drift. Routes are NOT
saved; they re-derive deterministically and the leg re-stages on load. Eight
flat additive fields on `DungeonSaveData` (`caravanNextDepartureDay` -1 =
due, `caravanState` append-only ints, walked/phase seconds, cargo, verbUsed,
sighted, tollVignettePlayed); statics reset in `ResetForNewGame`, merchant
pattern.

*Sighting and the toll.* First time ever the wagon rolls onto a REVEALED
segment: one Discovery alert, `caravan_first`, and
`FactionIntel.NotifyEncounter` -- a caravan can genuinely be the player's
first meeting with the Holds. A stretch is HELD when every carriageway cell
of the segment is influence-claimed -- `TerrainFeatureGenerator.
IsRoadSegmentHeld`, placed on the generator because step 8's claiming
penalties key on the same test (claiming itself still carries no penalty;
that is step 8). The FIRST held crossing ever plays the toll vignette: camera
glides to the wagon under input lock (the First Blood pattern -- follow
target, zoom 7, scaled waits so pause holds the beat), the wisp gives the
spiel (`caravan_toll_first` -- the mechanic's tutorial), the wagon holds a
grace beat inviting the click; flag-persisted, never replays. Later held
crossings: one System alert per segment per journey while the verb is
unspent, nothing more. The panel NEVER opens itself -- manual click only.
That is the arc's anti-spam decision.

*One verb per caravan* (`CaravanActionPanel`, MerchantShopUI's open/close
pattern, closed FIRST in the ESC chain). Rob takes all cargo, standing -25,
`caravan_robbed`, +4 extra days, and the alert notes the quiet road when the
hit crosses into Tier 1; the survivors FLEE rather than vanish -- to the
nearest refuge along the road (the floor's own dwarven site, or the paired
rim end when that is closer, so a wagon robbed near the rim runs off-floor),
at hurry pace, day and night, with all toll detection off (`Fleeing = 8`,
appended; a save taken mid-flee collapses to Idle on load, the schedule set
at the rob standing). Tax needs a held stretch under
the wagon (button live only then): 20% of ORIGINAL cargo min 1, standing -3,
the wagon walks on lighter. Let pass spends the verb too. Closing the panel
WITHOUT choosing settles nothing -- a misclick must never burn the decision.
The wagon halts while the panel is open.

*Tooling.* `Commands / Test Caravan Route Report` proves the geometry
headlessly after Test Generate All Floors: rim ends and bearings per floor,
the chosen pairing and its delta, anchor snaps, per-leg cell counts and
derived speeds against the authored days, and held segments along the route;
a missing route is a loud FAIL.

**Key files:** `Floors/DeepRoadGraph.cs`, `Floors/DwarfWalkerPuppet.cs`,
`Floors/DwarvenCaravanController.cs`, `Floors/DwarvenPatrolController.cs`,
`UI/CaravanActionPanel.cs`; edits in `TerrainFeatureGenerator` (held test),
`FactionRelations` (the edge), `DwarvenVillageController` (walkers),
`PauseMenuController`, `WispScript.cs` + `Assets/Wisp/WispScript.asset`
(which was missing `village_first`/`village_greeting` at HEAD -- the saved
asset overrides code defaults, so the village wisp was silently mute; fixed
here), `DungeonSaveData`/`DungeonSaveController`, `TESTING/Commands.cs`.

**Reuse note.** `WanderingMerchantController` is a singleton with a static
next-visit day, bound to the surface arrival model (forest road, camp
commerce anchor, camp-tier gate, leaves at dusk). Almost none of that
transfers to a stationary in-dungeon vendor. `TraderStockCatalog` and the
purchase path (including `PatternDiscovery.NotifyTraderPurchase`) do.
`MerchantShopUI` should be decoupled from the merchant singleton onto a stock
provider BEFORE a second vendor exists, not after.

### Claiming the road (SHIPPED -- build order step 8)

The road is the first terrain in the game with an OPINION about being
claimed. The AMBIENT CREEP never takes it: `TryCreepOnce` skips any cell in
the holdings set, beside the rivers and uncleared chambers it already skipped.
Without that guard the creep claimed into a hold on a timer the moment
influence sat adjacent, so the ladder would have billed the player for a choice
they never made -- neither a diplomatic act nor a mining decision, but no
decision at all. Floor 4's dead network is not in the holdings set, so the creep
still crosses it freely.

Holdings also announce themselves `dwarvenWarnRangeCells` (4) out rather than on
contact. Reveal fires on the claimable ring, so discovery was already free, but
on this ground arriving and claiming are one gesture and a warning that lands on
contact is not a warning: the granite has to be on screen before the frontier
reaches it.

Pushing influence across it is a diplomatic act, not a mining
decision.

**Segmented, never binary.** Claiming is per-stretch, so standing loss scales
with how much is taken and a two-tile corridor grab stays viable. All-or-
nothing collapses the decision into a yes/no with an obvious answer.

**The warning ladder.** The player gets one free warning before anything
irreversible -- felt before told:

1. Terrain resistance slows the push. The player notices something is
   pushing back before being told why. Best first signal because it is
   discovered, not announced.
2. A wisp line, before the first claim completes.
3. An alert at warning severity on first claim; critical on repeat.
4. The faction panel row drops visibly.
5. A dwarf patrol stops, looks, and turns back. Diegetic and unmissable.

**It does NOT reuse a Holy Ground pattern, and this entry used to claim it did.**
There is no Holy Ground gameplay wiring anywhere: the enum value exists, a
resistance row exists, `PatternDiscovery` maps it to a name, and that is the
whole of it. `AlignmentSystem.Desecrate` is a stub with zero callers. This arc
BUILT the pattern -- special terrain that a faction owns, a registry that says
who owns which cell, a per-cell price for taking it, and a ladder of consequences
around the taking. Holy Ground can now be placement plus a `Desecrate` call, and
should be revisited on that basis.

**As built.**

*The penalty is PER CELL, and per DWARVEN cell rather than per road cell.*
`DwarvenClaimLedger` (pure static, `DwarvenSpoil`'s pattern) is called from
`TileInfluenceManager.ClaimTile`'s non-silent path, beside
`PatternDiscovery.NotifyTerrainClaimed` -- a direct call rather than a
subscription, because a claim handler that lost the subscription race would fail
silently and hand the road over free, which is the bug class Appendix D exists
for. Each cell of dwarven ground taken costs `StandingPerCell` (0.05), so a
two-hundred-cell stretch costs -10 against 35 points of headroom from +15 to
Tier 1, and a two-tile corridor grab costs about -0.6.

Billing on `IsRoadSegmentHeld` was the obvious route and is wrong. That test
wants EVERY carriageway cell of a ~200-cell segment while `InfluenceChannel` is a
swelling boundary with `corridorHalfWidth` 6, so a push across a road takes about
a dozen cells and stops: the ladder would have fired only for a deliberate
two-hundred-cell campaign, which is binary claiming in a segment's clothes. The
held test stays exactly where it was, pricing the toll.

*The courtyard price, decided deliberately.* A living hold spans three claim
costs -- masonry 9x, paved carriageway 8x, carved interior 3x -- and the road runs
THROUGH the outpost rather than past it, so the cheapest ground in the hold is
reachable without ever paying 9x. Rung 1 is therefore quietest exactly where the
granite shouts loudest, and left alone the ladder inverts: told first, then not
felt. The standing bill takes no interest in which of the three terrains a cell
is, so the courtyard costs what the wall costs even though it digs easier. The
holdings registry already maps a living site's whole footprint to the site, so
this is one probe rather than a three-way test.

*The one free warning.* The FIRST dwarven cell ever claimed, on any floor, costs
no standing and raises the Warning alert plus `road_claim_first` instead. Free
per floor and free per segment were both rejected: either makes a wide, shallow
grab free forever, which is the play the penalty exists to price.

*Alerts are per OWNER, never per cell* -- one Warning for the first ever, one
Critical for each new stretch or hold after it. Two hundred alerts per stretch
would bury the ticker under the act the ticker exists to make legible.

*Rung 2 rides PRESSURE, not the claim.* `InfluenceChannel` accrues pressure on a
frontier cell for a while before it takes it, and that is the only state in the
system that exists while the decision is still reversible. Leaning on holdings
speaks `road_claim_warn`, once ever. The probe is hoisted behind a per-push
emptiness test, so a floor with no dwarven ground pays one null check rather than
one lookup per claimable cell per frame.

*Rung 5 re-aimed.* `DwarvenPatrolController`'s stop-and-look beat now also
answers ground the player has TAKEN -- holdings-and-claimed, two dictionary probes
on the cell the patrol is about to enter. Gated on the cell UNDERFOOT being
untaken, so a patrol whose whole beat has been claimed walks it rather than
jittering on the spot.

*Rung 4 was already enough.* The faction panel's nightly snapshot shows the drop.
Nothing was built.

**The toll stopped being a trap.** Tax cost -3 standing per wagon on top of the
claim, and from +15 that silenced the road at Tier 1 after roughly eleven tolls
-- about 300g total, permanently -- against a single Rob paying 80-200g. A verb
nobody should ever take is not a verb. The toll now costs NO standing: holding
the stretch is the price, and per-cell claiming makes it a real one. The field
was DELETED rather than zeroed, because a serialized field keeps whatever the
Inspector wrote and a changed default would have moved nothing in the live scene.

**Diagnostics.** `Commands / Test Caravan Route Report` already counted held
segments per leg; it now also reports claimed-versus-total carriageway cells and
what the ladder would charge for them. A stretch reading UNHELD with nearly every
cell claimed is the frayed seam or the junction fillet handing a corner to a
neighbouring segment, and the raw counts say so instead of costing a test cycle.

**Key files:** `Gameplay/DwarvenClaimLedger.cs`; edits in `TileInfluenceManager`,
`InfluenceChannel`, `DwarvenPatrolController`, `DwarvenCaravanController`,
`WispScript.cs` (+ the asset, regenerated by hand),
`DungeonSaveData`/`DungeonSaveController`, `TESTING/Commands.cs`.

### Granite holdings overlay (SHIPPED -- wall-family arc, edits4)

Dwarven holdings render with their own boundary in COOL GREY GRANITE,
shown whenever the push overlay is up (push mode or the overlay toggle).
This supersedes the original claims-toward trigger on purpose: the
ownership map reads before the first accidental 9x push lands, discovered
or not -- the overlay draws above the fog by sorting, and the core senses
rival claim.

**It must not look like the influence ring.** `InfluenceRingRenderer` is
deliberately ethereal -- the isoline wavers on two octaves of scrolling
noise, glows asymmetrically with a long tail into the fog, and pulses. That
waver IS the core's identity: something alive, pushing outward. Dwarven
holdings are the opposite claim -- surveyed, built, cut straight, unchanged
for an age. A hard edge with no pulse. Yours breathes, theirs does not, and
the two can never be confused at a glance.

**Colour caution.** The palette is crowded: the default ring colour is gold,
the HUD accent is gold, and Earth cores are amber-umber. Stone, brass and
bronze all collide with two things at once. Cool grey granite was chosen
against that constraint, and the shipped fill and surveyed edge honour it
(`holdingsColor`, serialized on the renderer). The single bronze element is
the frontier flare, a thin accent rather than an area.

**Implementation note (as built).** The shader path after all, which this
entry reserved: the field texture's spare A channel carries the dwarven
weight -- 255 on unclaimed cells of a living site's WHOLE footprint
(masonry, carved interior, paved carriageway), fed from a holdings set the
override pass registers on `TerrainTypeMap` and recomputed on the same
claim-driven rebuilds -- and the holdings fill and surveyed edge are
both cut in the fragment shader from that bilinear ramp -- hard-thresholded
fill at `overlayDwarvenLevel`, edge band peaking at the ramp midpoint, no
time terms in either. Holdings remain static data; the claim-event rebuild
already covers "changes only when the player takes a stretch", and
conquered cells write zero weight, dissolving into the ordinary wash. No
tilemap overlay was needed.

**Reveal gate and the road (as built).** The registry on `TerrainTypeMap`
maps each holdings cell to the FEATURE that owns it -- a living site's whole
footprint to the site, open carriageway to its road segment -- and the
renderer paints only owners the player has revealed. Site-level for holds,
segment-level for road, matching the reveal granularity already shipped on
both: a trunk runs rim to rim, so lighting all of it off one touched cell
would hand over the floor's layout. The gate is not optional. The camera
roams the whole floor (Appendix C) and this quad sorts above Shadow, so fog
cannot do the hiding; without the gate, pressing the overlay key on a fresh
floor would draw the gatehouse's floor plan through unexplored dark. Open
carriageway takes granite only on floors carrying a LIVING holding, so
floor 4's dead network stays plain -- the same test that prices it at
`deadRoadClaimResistance`, which keeps cost and colour saying one thing.
The revealed subset is cached and rebuilt only when
`TerrainFeatureGenerator.RevealVersion` moves, and the per-texel probe is
hoisted behind an emptiness test, so floors with no dwarven ground skip it
entirely.

Claiming the interior currently costs nothing
extra ON PURPOSE: the mechanical consequence is the road-claiming ladder's
first rung ("terrain resistance slows the push"). The holdings registry is
NOT that query: road and site cells are already priced by
`FloorRoot.GetClaimCostMultiplier`, and the registry does not reach past a
living floor. What it IS ready-made for is keying the ladder's standing
PENALTY on dwarven ground.

**Consequence, kept and shipped:** the moment the player's ring touches the
holdings IS a visible event -- the living isoline lerps toward
`dwarvenRingColor` (warm bronze) exactly where it abuts the dead grey
survey, waver and pulse against hard stillness.

### Recommended build order

Roughly the reverse of how interesting each step is, so that the risky work
lands on proven ground:

1. The pre-carved road generator -- SHIPPED. See "The generator (SHIPPED)"
   and "Carve precedence" above.
2. Floor 4's dead network -- SHIPPED. Authored on `RoadNetworkProfile`.
3. Floor 3's surviving trunk -- SHIPPED. Same generator, `Trunk` mode.
3a. The sites -- SHIPPED. See "The sites" above. Inert: terrain, reveal and
   two wisp lines, no faction and no NPCs, which is what leaves step 4
   standing on known-good ground.
4. The dwarves, PART 1 -- SHIPPED. The faction and standing (entry 7), the
   guaranteed outpost, its authored plan, `DwarvenOutpostController`. See "The
   dwarves -- faction and outpost" above.
5. The dwarves, PART 2 -- SHIPPED. The vendor, the shop decoupling,
   dwarf-exclusive traps, and the spoil economy. See "The dwarves -- the vendor
   and the spoil economy" above. Split from part 1 deliberately: part 1's whole
   risk sat in `AncientSiteBuilder`, part 2's in UI and economy, and one
   delivery would have given a floor-3 regression six possible parents.
6. The village, PART 3 -- SHIPPED. Floor index 3's sparse living network,
   the guaranteed village, the DwarvenVillage archetype, the name roster, and
   the static villagers (variant sprite list, count on the controller). See
   "The dwarves -- the village" above.
7. THE LIVING HOLDS -- SHIPPED: walking villagers, road patrols, the
   multi-day caravan with its one Rob/Tax/Let-pass verb, the held-stretch
   toll and its first-time vignette, and the entry-7 matrix revisit
   (Dwarves <-> Holy Order written Hostile, exercised by walker reactions).
   See "The Living Holds (SHIPPED, step 7)" above.
8. THE GRANITE BOUNDARY, ROAD CLAIMING AND THE WARNING LADDER -- SHIPPED.
   The overlay landed with the wall-family arc; the ladder landed on the
   alert severity layer (entry 18), which was built immediately before it
   for exactly that reason. See "Claiming the road" above.

## 19A. Tier-Up Divine Audiences

Status: SHIPPED. Verified: 2026-08-10. (Sits in Part II for numbering only --
it was split out of entry 19, which it never depended on. The status line is
the arbiter, as with entry 19 itself.)

At each tier transition -- Bronze -> Silver -> Gold -> Diamond -> God -- the
screen goes black and the god of the CORE'S OWN TYPE arrives. Four audiences
in a run, one god per affinity, and no other god ever attends.

**The framing, revised.** The earlier decision read "grants knowledge rather
than power"; that is superseded. As built, the core has been SIPHONING the
god's power all along -- every claimed stone, every death kept -- and the god
is the source. It permits this, and what comes attached to the power is the
knowing. Each audience also names, in the god's own idiom, what the next tier
actually opens. Nothing unbuilt is ever promised: the hints cover the stair
credit, the floor the tier unlocks, the monsters that answer at that depth,
and (at Diamond) the climax that is coming. Core spells and the avatar are
deliberately absent from the writing and get their lines when they exist.

This is the deep-faith speaking to one of its own. Entry 20 records that the
old faith held divinity to reside below and that some dead are reborn as
cores; entry 21 records that its civilisation was entombed. The audiences are
the surviving other end of that -- the thing the Church suppressed, answering
at the exact moment the player has earned a reason to care. The Diamond
audience is where it is said outright: nothing above ground made the player,
and drawing on the god is not theft but eating at home.

**The voice rule, and it is load-bearing.** The gods are NOT the wisp. They
speak as sovereign to servant: short declaratives, no hedges, no questions
unless rhetorical. The wisp coaxes and hedges and trails off; these do not.
Any new god line is written to that rule or it is not a god line.

**Presentation.** A full-screen overlay in the dungeon scene, self-built at
Awake (the `ScreenFlash` precedent -- no scene wiring beyond the component and
its script asset). Not a scene load: rebuilding every live system for a
two-minute beat buys nothing over an opaque black rectangle. Not a puppet
vignette either -- the First Blood idiom needs a body, and these have none.
Because nothing is visible behind the blackout there is NO camera work at all.
The clock is stopped for the duration and every wait runs on unscaled time;
the prior speed is restored exactly via `PauseController.UnpauseGame` (not
`SetNormal`, which would quietly demote a player running at 5x), and a game
already paused when the audience begins is still paused when it ends. The
overlay canvas carries a `GraphicRaycaster` -- that is what makes the blackout
actually block world clicks, since the build and selection paths test
`EventSystem.IsPointerOverGameObject`. `DivineAudienceUI.IsPlaying` gates the
three input owners that would otherwise act straight through it: the speed
keys, the journal toggle, and Esc (which the audience owns while it plays).

**Manifestation.** Each god carries an optional full-screen backdrop sprite --
a firestorm, a whirlpool turning where the ceiling was, cloud lit from behind,
the weight of a mountain leaning, a dark with an edge, light climbing out of
the floor. Until that art exists the overlay falls back to a slow radial pulse
in the affinity colour, and the PRESENCE beat carries the scene: one line per
god describing what the player is looking at, spoken by nobody and rendered
without the name card. The presence line is written as description precisely
so the fallback is not a blank god.

**Content shape.** `DivineAudienceScript` (ScriptableObject, `Fill Canon
Script` context menu -- the `AffinityMapping` / `WispScript` precedent): six
deity rows (name, epithet, presence, four own-lines) plus four shared tier
scripts (opening lines, closing lines). An audience composes as presence ->
tier opening -> the god's own line -> tier closing, with `{god}` / `{epithet}`
substituted so shared lines still name their speaker. That is about sixty
written lines rather than the two hundred a full per-tier-per-affinity matrix
would have cost, and each god stays recognisably itself across all four.
The six: Kethra the Undying Coal (Fire), Ollu the Drowned Mouth (Water), Vaun
the Long Breath (Air), Morrun the Weight Below (Earth), Ussar the Unlit
(Dark), Ienna the Buried Sun (Light) -- whose epithet is the deep-faith's
answer to the Church having hung her name in the sky.

Two deliberate details, so they are not "fixed" later: the God audience closes
on the same words as the Silver audience ("I am under you the whole way"),
said to something that is no longer beneath the speaker; and the Silver
audience is the only one that introduces the god in speech, because the name
card carries identity at every later one.

**The God audience IS the ascension beat.** The Diamond 3 -> God 1 transition
was already gated on surviving the endgame climax (entry 9); the audience is
its ceremony, and the old "special requirement TBD" comments near
`ConfirmLevelUp` and in `LevelTier.cs` are retired accordingly.

**Skippable and re-readable.** Any beat advances on click, space or enter;
Esc withdraws. Held audiences are re-read in the journal's sixth tab, LORE,
which has two sub-pages: GODS (rendered from the same script asset the
audience spoke from -- never a second copy to drift) and WISP. Unheld tiers
read "???".

The WISP sub-page lists what the wisp has ACTUALLY said, grouped by id prefix
(`WispLoreIndex`): The First Days, Firsts, The Dead Below, The Surface, Kin,
Echoes, and a catch-all Other sayings for any prefix no map entry claims --
which the `Print Wisp Lore Page` command names, so a new content family cannot
vanish quietly into the bucket. It adds NO save state and NO scene wiring: the
shipped `wispSpokenLines` field already records one-shot ids, and the page
renders into the same `loreContent` root as GODS. Heard lines only, with a
gathered tally -- a placeholder per unheard line would advertise that there are
exactly eight kin to meet and eleven echoes a life might hold before the player
has met one. Prefix rather than a category field on `WispScript.Line`
deliberately: a field makes every new line depend on regenerating the asset via
Fill Canon Lines, and that step fails silently. The temperament is named at the
head of the page; the ambient bark pools are NOT listed and are not meant to be
-- they carry no ids, nothing tracks them, and they are flavour rather than
record. The four repeatable (`once = false`) lines can never appear, since only
one-shot ids are recorded as spoken; they are excluded from the tally's total
too, so it is not permanently unreachable.

**Persistence.** `DungeonSaveData.audiencesHeld`, additive, keyed on the TIER
NAME rather than the enum ordinal (`LevelTier` may gain values). Held is
recorded when the god ARRIVES, not when the last line lands: a player who
quits mid-speech has had the audience, and replaying it on load is worse than
losing its tail. On load the ledger reconciles in SILENCE -- every tier the
core has already passed is marked held, so a save predating the feature does
not fire four gods in a row on its next level-up. History is not an event to
announce (the Deeds precedent). The cost is that a legacy save reads those
audiences in the journal without having sat through them, which is the kinder
half of the trade.

**Tooling.** `Print Divine Audience Script` in `Commands` validates the asset
and dumps every composed audience -- all six gods, all four tiers -- to the
console without playing one, so the writing can be read and edited without
four tier-ups. `Play Divine Audience (preview tier)` forces one on screen for
the core's current affinity.

**Key files:** `DungeonCore/DivineAudienceScript.cs`,
`DungeonCore/DivineAudienceLedger.cs`, `UI/DivineAudienceUI.cs`,
`DungeonCore/DungeonCore.cs` (the trigger, last in `ConfirmLevelUp`),
`UI/QuestLogUI.cs` (the LORE tab), `UI/PauseMenuController.cs`,
`UI/TimeScaleController.cs`, `Save/DungeonSaveData.cs`,
`Save/DungeonSaveController.cs`, `TESTING/Commands.cs`.

**Rejected:** a dedicated audience scene (state rebuild for no gain over an
opaque overlay); a full per-tier-per-affinity script matrix (~145 lines for
identical structural payoff); an auto-advancing timed read (the clock is
already stopped -- let the player set the pace); ARBITRARY god-granted
mechanical rewards (the tier already grants the stair credit and the
unlocks; a bonus payout bolted on would make the beat a loot box).
NARROWED by entry 38: the affinity working IS granted at the audience, and
is not an exception to this. The god is the canonical source of the power
the core has been siphoning all along; this entry already reserved a hole
in the writing for core spells; and the audience exists precisely to name
what a tier opens. What stays rejected is a SECOND payment on top of the
tier's own unlock, which this is not.

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

### Holy Ground (SHIPPED -- placement)

`TerrainType.HolyGround` carried a resistance row (10, the highest in the
table), tints, a display name and a `PatternDiscovery` mapping to
`hallowed_stone` from the day the terrain system shipped, and was never once
placed. Three shipped assets sat dead behind that: `HallowedStone.asset` (whose
`sourceHint`, "Ground that stings to hold", was written for this arc),
`TheSearingGlance.asset` -- `trap_blinding_flash`, tier 3, **Light** affinity --
and `Trap_BlindingFlash.asset`. The pattern's only source is claiming Holy
Ground, so the Light core's own elemental trap was unreachable. That, rather
than the desecration beat, is why this arc was worth doing.

**Four Church archetypes, eight authored plans.** `ChurchSeal`, `SealedCrypt`,
`WardChapel` and `BlessedSpring`, appended to `SiteArchetype` at 9-12 -- the
enum serialises into saves as an int, so appending is the only legal move. They
are AUTHORED-ONLY (`VariantCountFor` returns 0), because a seal is a made object
and procedural jitter reads as a collapsed ruin, which is the Buried Age's job.
They are opt-in per floor: `BuildPlanPool`'s `useAllArchetypes` cap stops at
`TollHouse`, so floor index 4's all-archetypes roster cannot sweep them in.

They are NOT more Buried Age sites, and entry 21 is why. The Buried Age ruins
are the deep-faith's own -- welcoming, no desecration penalty. These are Church
seals laid over what that faith left, and hostile.

**The plan format gained one glyph.** `'X'` is the HEART: the altar, grave slab,
capped font or seal-stone. It parses into `wall` as well as its own list, so it
is solid and everything downstream treats it as ordinary masonry -- desecration
is unsealing, and an open floor cell cannot be mined. Exactly one per plan; two
is a parse error rather than a silent last-wins.

One authoring rule came out of drawing the eight, and it is enforced by
`Dungeon Core / Validate Site Plans`:
- A heart needs THREE clear cells on all four sides of its own SOLID
  COMPONENT. For a stone standing alone that is a seven-by-seven chamber; for
  a heart set in a plinth it is measured at the plinth's edge. Measuring from
  the heart CELL was the first attempt and it failed six of the eight plans it
  existed to protect, because a plinth-set heart has masonry for neighbours by
  design.

A second rule was attempted and RETIRED, and the retirement is the more useful
record. A door-rule gate -- any open run under three cells with solid at both
ends -- failed twelve shipped, working plans, because it flagged every
decorative niche as a sealed passage. The honest version, connectivity of the
drape-filtered walkable set, failed them too: `SunkenPlaza_TheCountingFloor`
fragments into seven pieces with the largest at 33 per cent and
`TollHouse_TheWeighingHouse` sits at 41. Both are fine, because a site is MINED
into rather than walked into through a door -- the player carves their own way
and internal fragmentation is not fatal. THE DOOR RULE remains an authoring
principle for doors and passages and does not reduce to a local geometric test.
The connectivity figure is now printed on every plan line as information and
gates nothing; a low percentage is what "this drawing did not come out right"
looks like in numbers, which is why `HollowSanctum_ThePilgrimsWay` reads 46.

The per-archetype span clamp discussed during design turned out to be
unnecessary: `AncientSiteBuilder` already ignores `minSpan`/`maxSpan` for
authored plans, which is the point of hand-authoring them. The eight are fixed
at 21-23 cells across, carved counts 150-246 -- one to two cave chambers, well
clear of the three-thousand-cell figure a 62-span site reaches.

**SHIPPED: the dead core vault, and the platform glyphs.** `DeadCoreVault` is
appended at 13 -- the Church's vault around a dead core, one per dungeon on floor
index 4, placed by guarantee rather than by pool so it can neither be rolled
twice nor displace a seal. Authored-only, and enormous by design: the three
plans run 75 by 75 at 2458-2884 carved cells against the largest dwarven village
at 2588.

The plan format gained two more glyphs, and the reason they are worth having is
that the raise is MECHANICALLY REAL with no elevation system at all.
`PlayerMovement` is Rigidbody2D with collider-based blocking rather than tile
pathing, so a platform edge drawn as masonry already stops the avatar, the
tile-pathed monsters and the adventurers alike -- and the stairs are simply the
only gap in that edge. Nothing had to be built for it.

  `'='`  platform floor -- parsed into `floor` too, so every existing consumer
         sees ordinary walkable ground
  `'^'`  stairs -- likewise floor, and the only opening in the platform edge

Both are marked in the PLAN rather than left to the decor prefab, because the
geometry has to be the single source of truth: a prefab can be redrawn without
anyone noticing the plan no longer agrees with it, and the validator can only
check what the plan states. Three rules ride with them, all catching faults that
are invisible in the grid and only bite once something walks the geometry:
a platform edge must be masonry except at stairs, a platform must have at least
one stair, and a stair run must be three cells wide -- the door rule in the one
place it genuinely applies, since this is a real passage everything has to path.

One vault per dungeon, guaranteed on floor index 4 by `reserveDeadCore` and
rolled among every authored plan, with `deadCorePlanName` as an optional pin
for checking one at a time. `PlaceDeadCore` mirrors `PlaceVillage` with one
difference that had to be found by reading it: the guarantee paths emit floor
and wall only, and the HEART is emitted by the general fill loop alone. A
vault placed without it would report NO HEART and could never be unsealed --
and nothing would say so until someone dug to the middle of a seventy-five
cell vault and found it inert. `PlaceDeadCore` emits the heart through the
same transform as its masonry, and REJECTS a placement whose heart fell
outside the clamp disc rather than shipping an inert vault.

### Door anchoring (SHIPPED)

An `AlongRoad` anchor lands a plan's bounding-box CENTRE on the carriageway.
For the vault that is wrong twice: the road drives through the middle of a
75-cell building, and its one door faces wherever the plan drew it. Truncation
then cuts the road out of the footprint, so it dies forty cells short of an
entrance it never pointed at.

**No plan-only fix works, and the measurement is the reason.** Over 20,000
random bearings, the tangential offset from the door to the road's surviving
end: one 3-wide door as shipped puts the road on the door **1.5%** of the time;
four 3-wide doors, 6.2%; four 15-wide, 25%; four 25-wide, 40%. Allowing rotation
and picking the facing quarter-turn scores 6.2% -- identical to four doors,
because both only guarantee a door on the wall the road exits, not at the point
it exits. Corners are the killer: a road at 45 degrees leaves a square 37 cells
from any wall's midpoint. Widening and multiplying doors optimises the wrong
variable.

`@anchor_on: door` offsets the plan so a declared door lands on the anchor:
`adjustedAnchor = anchor - RotateLocal(doorMid)`. `EmitTransformed` is untouched
and simply receives a different anchor. `RotateLocal` was extracted from it so
the door and the building it belongs to cannot end up a quarter turn apart.

**The heading filter is not optional.** Door anchoring alone leaves a road
running PARALLEL to the vault's face inside the truncation dilation along its
whole length, cut 41 cells from the entrance. Rejecting anchors whose local road
heading is off the door's outward normal fixes it. Heading is estimated by least
squares over road cells within six, because `Build` receives a flat cell list
and threading the polyline through would widen four signatures to answer one
question; too few cells to judge REJECTS the anchor, since an unverifiable
heading is not a passing one.

**And the road RUNS TO THE DOOR rather than stopping outside it.** Anchoring and
the heading filter together still left the carriageway four to six cells short,
because the truncation dilation blocks the approach as well as the building.
`RoadNetworkBuilder.RoadGate` opens a corridor through the blocked set: cells on
the OUTWARD side of a door line, within the wider of the door and the road. Only
outward, so the road stops at the threshold instead of driving through the hall
behind it; a site with no declared door has an empty gate list and truncates
exactly as before.

**The cone and the corridor are one decision, not two.** A road arriving steeply
drifts out of a narrow corridor before it reaches the door and is cut anyway.
Measured worst-case distance from the surviving road end to the door:

| cone | corridor +/-1 | +/-2 | +/-3 |
|---|---|---|---|
| 45 deg | 5.7 | 5.7 | 0.0 |
| 30 deg | 4.5 | **0.0** | 0.0 |
| 20 deg | 0.0 | 0.0 | 0.0 |

Thirty degrees with a corridor of two reaches the door on every bearing at a
corridor exactly one trunk wide. Forty-five would need three, wider than any
authored door, and would eat jamb beyond it. Twenty buys nothing further and
throws away anchors -- acceptance falls from about half of all bearings to about
a third, which is still ample against 240 attempts.

**The threshold overlap is intended and costs about fifteen cells.** A five-wide
carriageway reaching a doorway covers it; on a five-wide door that takes no jamb
at all, on the Ninefold Cist's three-wide door it wears one cell either side to
road width. Against 2884 carved it is under one per cent, and most of those
cells are the outermost ring corridor, which was already open ground. The
verifier was made GATE-AWARE for it: the plain count would have read intended
overlap as failure and logged an error on every correct floor.

**The adjusted anchor is re-validated against band and spacing.**
`TryPickAnchor` vetted the road cell, and the building ends up some 37 cells
away from it, so every test it passed describes somewhere the vault is not.

**Door runs are computed once, in the library**, with their outward normal --
the perpendicular whose neighbour lies outside the plan, because a door opens
onto rock rather than onto more building. The validator's own run-finder was
DELETED in favour of them: the geometry that decides where a vault goes and the
geometry that decides whether its door is legal must be the same geometry.

**No plan geometry changed**, and the vaults' own headers are why. Extra doors
would break *"each with a single passage and no two passages on the same side"*;
rotation would break *"@rotate: no -- the dais offsets are hand-placed in the
decor prefab"* and trip the validator's decor rule. Both routes were confirmed
dead by reading the plans rather than by trying them.

**On floor index 4, the ROAD yields to the vault** -- and nowhere else does
anything yield to a site. `GenerateSites` subtracts road cells from every site's
footprint, which is right on a living floor: the carriageway was carved first and
a ruin built around it reads correctly. On the vault it cost 2106 carved cells
against 2576 authored, an eighteen per cent hole through the largest hand-drawn
thing in the game. So on this floor the road stops at the vault wall instead,
with NO continuation, gated on the same `reserveDeadCore` entry as the vault
itself.

The inconsistency is the point and is recorded so it is not "fixed" later: seals
on living floors yield to roads because a severed leg there costs a trade route,
and floor 4 carries no caravans and no patrols, so severing one costs nothing.

`RoadNetworkBuilder.TruncateAroundBlocked` keeps each road's LONGEST unblocked
run, ties to the earlier one, and leaves orphaned spurs as fragments -- a stub of
dead network is a correct thing to find down there. A run shorter than the road
is wide is dropped instead, because that is a smear rather than a fragment. A
road with nothing left keeps its `RoadData` entry and loses its polyline:
removing the entry looks tidier and is not, because
`FilletJunctionsIntoSegments` reads `roads[0]` for the network's width, centre
and clamp radius, so dropping one can swap a five-wide trunk for a two-wide spur
and change fillet geometry across the whole floor. Every derivation already
guards on an empty polyline.

**Three things had to be read rather than assumed, and each would have shipped a
defect.**

*The window is one statement wide.* Truncation runs AFTER the vault is placed and
BEFORE the road-cell subtraction, with `RebuildRoadCells()` between -- otherwise
the vault still loses its cells to a carriageway that no longer exists. That is
inside `GenerateSites`, between `AncientSiteBuilder.Build` returning and the
placement loop. It is safe on load because `RoadData.polyline` is persisted: the
cut happens once and the load path rasterises the already-cut polyline, so there
is no second truncation to keep in agreement. `RebuildLookup` calls
`RebuildRoadCells` on both paths, so the authoritative segment partition is the
final one either way and the rebuild in the middle is transient -- nothing
between it and that final rebuild reads a segment id, which is what the reveal
alerts and `IsRoadSegmentHeld` key on.

*`Centreline` spends `brokenGapCells` on the tail of the whole line*, and floor
4's roads carry a gap of six. The clip therefore analyses `Centreline(road)`,
already gap-trimmed, and ZEROES `brokenGapCells` on any road it cuts. Analysing
the raw polyline instead would leave the measured run and the rasterised cells
disagreeing by six cells at the very end, which is the end the vault is usually
at.

*Bresenham restarted at an interior lattice point does not reproduce the tail,
and this was assumed the other way round when the approach was sketched.* The
compact polyline keeps the run's two ends plus the original waypoints strictly
inside it, so the line is RE-DRAWN rather than copied, and "Centreline reproduces
the kept cells exactly" is simply false. Counterexample in
`RoadNetworkBuilder.Line` itself: (0,0) to (6,4) passes through (2,1), (3,2),
(4,3), while (2,1) to (6,4) gives (3,2), (4,2). Nor is it an edge case --
measured over 51,081 restarts of random lines, 45,858 of them, ninety per cent,
diverge from the tail they restart on. What saves the approach is that both paths
stay within half a cell of the true line, so the divergence is bounded at ONE
cell Chebyshev, which was the worst observed across all 51,081. The clearance
radius is `width/2 + 1` and that `+1` is exactly this margin, not padding: remove
it and the carriageway reaches the wall. Cell counts are consequently MEASURED
after the rebuild, never predicted from a run length.

`Tools/sim_road_truncation.py` is the headless check, and it is the reason this
shipped without a test cycle spent on it. It ports `Line`, `Centreline`,
`Dilate`, `BuildEdgePolyline` and the clip, builds floor-4-shaped meandering
roads across a 75-cell vault, and re-derives the carriageway after clipping. Over
3000 seeds, 2180 of which actually crossed the vault: zero carriageway cells
survive inside it, no clipped polyline folds back on itself, and the polylines
stay compact at a mean of 17 waypoints and a worst of 31.

**And it is verified rather than trusted.** After the rebuild the generator counts
vault cells still under carriageway. It should be zero, but `FilletJunctions` is
ADDITIVE and reaches `junctionFilletRadius` beyond the carriageway at a node, and
a clipped end is a NEW road end that could pair with another inside
`RoadJunctionMergeRadius`. If the count is not zero the clip re-runs once at a
clearance widened by exactly that fillet radius, and the result is persisted, so
the load path reproduces the wider cut without needing to know why. Still
non-zero after that is a `LogError` naming the arithmetic to read. The report
prints which roads were truncated or dropped, how many centreline cells went, and
the vault-cells-under-carriageway figure on both sides -- the only number that
answers whether the truncation actually bought the vault its cells back.

Masonry is mineable, so a player who would rather not walk around can cut their
own ramp onto a platform. Left deliberately: cutting a ramp is a reasonable thing
for a dungeon core to do, and forbidding it would need an unmineable terrain flag
for no gain. The stairs are the intended route, not the enforced one.

**A SEPARATE registry, deliberately.** `TerrainTypeMap.holySiteOwner` maps cell
to site id, parallel to the dwarven holdings dictionary rather than folded into
it. Folding would have bought the warn-range reveal for free, since
`FeatureRevealController`'s warn probe reads holdings -- and would also have
handed every seal to `InfluenceRingRenderer`'s granite overlay, which fills
discovered holdings grey and knows nothing about factions, and to
`DwarvenClaimLedger`, which bills the Deep Holds for every holdings cell
claimed. A Church seal painted as a dwarven hold and charged to the dwarves.

**Masonry AND interior retype to `HolyGround`**, unlike a Buried Age site which
retypes only its walls. A seal's ground is hallowed, not just its wall, so the
interior resists at 10x rather than `siteClaimResistance`'s 3x. This inverts the
dwarven courtyard finding (entry 19) on purpose: there the cheapest ground in
the hold sat behind the loudest announcement, and here the announcement and the
cost agree.

**The overlay is NOT built, and the reason is a channel budget.** The field
texture is RGBA32 with every channel spent: R the signed distance field, G
normalised cost, B exposed fringe on claimed ground and discovered dwarven
holdings on unclaimed, A proximity to dwarven ground. B cannot be subdivided --
the shader reads it as `smoothstep(0.45, 0.55, fs.b)` precisely because the
texture is bilinear-sampled, so a third value at 128 lands on the threshold and
half-fills. What ships instead is the part of the dwarven warning that is not
rendering at all: HolyGround carries its own row in `TerrainResistanceTable`,
which `CaveWallRenderer` already consumes, and the early reveal is a second warn
probe. A second mask texture or a three-band rework of B are both on the
backlog.

Two claims made here about that tint were wrong and are corrected rather than
quietly dropped, because both would mislead anyone choosing the seal's art. It
is not a cold white-blue: the shipped row is WARM -- `stoneTint` (1, 0.95,
0.82), ring tint (1, 0.9, 0.7). And it is not permanent. `CaveWallRenderer`
reads `StoneTintFor` only for cells whose terrain has no wall FAMILY (`Color
tint = fam != null ? fam.source.tint : StoneTintFor(wall)`), so the moment a
HolyGround family is present the resistance-table stone tint stops reaching
seal walls at all and only the claimable-ring tint survives. That is the whole
reason the section below exists.

**Floors 0 and 1 gained site entries.** The profile asset carried indices 2, 3
and 4 only. Floor 0 takes exactly one seal from a `ChurchSeal`-only pool at
`bandInner` 0.35, far enough out that the arrival area stays clear.

### The seal's own masonry (DECIDED -- art pending)

Seal walls render TINTED CAVE ROCK today, and have since the seals shipped.
That contradicts this entry's own reason for making the eight plans
authored-only -- a seal is a made object, and procedural jitter reads as a
collapsed ruin -- so the skin is worth having for the same reason the plans
are hand-drawn.

**Nothing has to be built for it.** `TerrainFeatureGenerator.MasonryTypeFor`
already returns `TerrainType.HolyGround` for all five holy archetypes, and it
is the ONE decision both the wall retype and `PaintSitePaving` consult. So a
single `CaveWallSheetLayout` family entry keyed to terrain 5 skins seal walls
AND seal paving with zero renderer code, exactly as canon 19 promised a
masonry skin would. This was true from the day the seals landed and nobody
noticed, which is why it is recorded here rather than treated as new work.

ONE family covers all five archetypes, the dead core vault included, because
they share terrain 5. Splitting the vault onto a skin of its own would need an
appended `TerrainType` plus a resistance row, a pattern id, a display name and
a spoil value -- and would change what the vault's ground resists at, which is
a balance decision made for a cosmetic reason. Rejected on that.

**Unlike DWARVEN MASONRY, this entry is not invisible on landing day.** Dwarven
walls were already Ruins terrain rendering the ruins family, so an identical
clone changed nothing on screen; holy walls have no family at all. The clone
therefore carries HolyGround's cream resistance-table tint rather than white
until the marble art is repointed, so the seal keeps the tell it had while the
slots are filled one at a time. White returns with the art, matching both
older families.

`Add Family` clones the LAST entry, so the holy entry inherits dwarven's
mask-15 MainLev rock cap. Kept, on the ruins reasoning: the deep interior of a
masonry mass still reads as rock.

Paving is deliberately not gated on the base cap -- `PavingTilesFor` returns
the family's tiles whether or not mask 11 is filled -- so hallowed FLOORS can
land before a single wall slot is repointed. That staging is a property of the
shipped code, not a workaround.

One constraint that bounds the sheet choice: `cellSize` is shared by every
family. `MakeTileFrom` uses `layout.cellSize` for both the slice rect and the
sprite PPU, so a family filled by (col, row) coordinates must sit on the same
grid as the others -- 32 px today. A sheet on a different grid needs either an
override Sprite in every slot, where the import PPU applies instead, or a
per-family cell size, which is code and has no reason to exist yet.

Flips to SHIPPED when the slots are filled and both `Validate Layout` and
`Dungeon Core -> Validate Wall Families` report clean. Authoring recipe:
Content Authoring chapter 31; walkthrough: `Docs/DCR_Guide_Holy_Masonry.html`.

### The holy sub-quota (SHIPPED)

The seals were originally rolled from each floor's ORDINARY plan pool, which put
them in competition with the Buried Age ruins for the same slots -- and entry 21
draws exactly the line that says they should not be. `SiteFloorEntry` therefore
carries `minHolySites` / `maxHolySites` / `holyPool`, drawn by a pass that runs
BEFORE the general fill and OUTSIDE its budget.

| Floor | Holy | General | Guarantee | minSpacing | Radius |
|---|---|---|---|---|---|
| 0 | 1 | 0 | -- | 60 | 100 |
| 1 | 2-3 | 0 | -- | 60 (from 70) | 150 |
| 2 | 3-4 | 1-2 | outpost | 70 | 250 |
| 3 | 5-6 | 3-5 | village | 90 (from 110) | 400 |
| 4 | 0 | 9-13 | vault | 90 | 600 |

Floor 0's pool is `ChurchSeal` alone; floor 1 adds `SealedCrypt` and
`BlessedSpring` but NOT `WardChapel`, which anchors `AlongRoad` and would degrade
to a free pick on a floor with no roads -- the chapel a sealing was administered
from, stranded where nobody could reach it. Floors 2 and 3 take all four. Floor 4
takes none: the dead floor's Church presence is the vault.

**TWO STRUCTURAL TRAPS, both found by reading `AncientSiteBuilder.Build` rather
than by running it.**

*The early returns skipped the guarantees.* `Build` returned immediately on
`want == 0` and on an empty plan pool, and BOTH tests sat ahead of
`PlaceOutpost`, `PlaceVillage` and `PlaceDeadCore`. Harmless while every floor
rolled ruins -- and fatal the moment floor 0's `ChurchSeal` moved into
`holyPool`, because its general pool was then empty and its general count zero,
so the floor returned before the holy pass AND before its guarantees and shipped
nothing at all. A guarantee an unrelated roster can skip is not a guarantee. The
two returns are now one test on whether the floor has anything to place by ANY
route, and it names all three sources when it fires.

*Holy sites must not spend the general budget.* The fill loop read
`result.sites.Count < want`, and the guarantees add to `sites` before it runs --
so floor index 4 reported thirteen sites INCLUDING its vault, meaning the vault
had displaced a ruin on the one floor authored to be full. `AncientSiteResult`
now carries `extraPlaced`, incremented by the holy pass and by `PlaceDeadCore`,
and the condition is `sites.Count - extraPlaced < want`. The outpost and the
village still count toward `want` DELIBERATELY: the gatehouse floor is authored
as "the hold plus at most one ruin", and that reading depends on it.

**The fill body was extracted, not duplicated.** `Fill` is the placement loop,
called once per pool, with a `countsAsExtra` flag deciding only how progress is
measured -- the general pass against `sites.Count - extraPlaced`, the holy pass
against its own count, since a shared count would let the outpost finish the seal
quota. The holy pass gets `HolyAttemptsPerSite` 24 against the general pass's 12:
the gatehouse floor wants six anchors in an annulus of 75 to 162 at a spacing of
70, and a holy attempt is cheap because every seal is an authored plan and a
rejected attempt never runs `Compose`. A shortfall against `minHolySites` logs a
warning and prints on the site report, because a floor quietly shipping two seals
of five is otherwise only findable by walking it.

**The quotas were simulated before they were authored.** A headless model of the
band arithmetic, `TryPickAnchor`'s sample budgets, `TooClose` and both fill loops
meets every floor's holy minimum on 2000 seeds out of 2000. It also meets them at
the OLD spacings and at a 12x holy budget, so neither tunable is load-bearing
under that model -- they are kept as margin against the two things the model
cannot see, both of which push the same way: it samples anchors uniformly whereas
`TryPickAnchor` tries the ROAD source first for `AlongRoad` archetypes and so
clusters candidates onto the carriageway, and it omits the rotation-dependent
walkability rejection, which burns real attempts.

**The guarantees now go down largest first** -- vault (75x75, 5625 cells of
footprint), village (61x61, 3721), outpost (39x23, 897) -- so the hardest thing
to fit picks its ground before the floor is chewed up by anchors and spacing. No
floor on the shipped profile carries two guarantees, so this is rng-neutral
today; it becomes load-bearing the moment one wants a hold and a vault, which is
precisely when nobody would think to check.

**`BuildPlanPool` split into a roster step and `BuildPlanPoolFrom`**, shared by
both pools so the no-repeat rule, the authored-plan variant numbering and the
Fisher-Yates shuffle exist once. The holy pool has no `useAllArchetypes`
equivalent on purpose, and strips `@general: no` plans as the general pool does.
An archetype in `holyPool` that `TerrainFeatureGenerator.IsHolyArchetype` does
not recognise is WARNED and still placed -- silently dropping an authored entry
is the ambiguity this project refuses elsewhere.

**Adding the holy roll changed the rng draw order**, so every floor lays out
differently from this commit onward. Existing saves keep their floors; new games
do not reproduce old seeds.

**One authored plan on disk is deliberately unregistered** and should stay
that way: `DwarvenVillage_TheHearthOfTheDeep` (the original village, too
small). It is not an oversight, and re-registering it is not a fix. This
paragraph originally also named `HollowSanctum_ThePilgrimsWay`; the tag
audit deleted that file, and its own entry records the deletion.

### Holy Ground desecration (SHIPPED)

`AlignmentSystem.Desecrate` finally has a caller. `HolyGroundLedger` (pure
static, `DwarvenSpoil`'s pattern) is driven by DIRECT CALLS from
`TileInfluenceManager`'s live claim and mine paths -- not subscriptions, because
a handler that lost the subscription race would fail silently and give the seals
away free, which is what Appendix D exists for. Restores are silent on both
paths, so a reload can neither re-bill the alignment nor pay the discovery out
twice.

**CLAIMING TEACHES, MINING DESECRATES, and that split is the design rather than
an implementation detail.** `hallowed_stone` pays out on CLAIM through the
`PatternDiscovery` hook wired since the terrain system shipped, at no alignment
cost. `trap_blinding_flash` is LIGHT affinity, so the core with the most to lose
from a low alignment is the one whose exclusive elemental trap sits behind that
pattern; charging alignment for the pattern would have forced a Light core to go
dark to reach its own content. Hold the ground and you learn the stone. Break
the stone and you answer for it.

**The price, and it is HEART-ONLY.** `AlignmentForHeart` 10 when an ordinary
seal's heart goes, `AlignmentForVaultHeart` 25 for the dead core vault, and
nothing whatever for the ground around them. Against `killPeaceful` at eight and
`HolyOrderStrike` wanting alignment at or below minus forty, an altar is a shade
worse than a murdered pilgrim and four altars bring the Order.

**The per-cell bill was DELETED, and the correction is the useful record.**
`AlignmentPerCell` 0.5 shipped and was wrong by roughly four times, in a way
invisible from its call site. A site's carved interior is opened by
`MarkNaturalFloor` on REVEAL, which never touches the mine path -- so only the
MASONRY was ever billable, and a 21-cell seal carries 200 to 280 masonry cells.
That is minus 100 to minus 140 to clear ONE seal, against a trigger threshold of
minus forty, with some fourteen seals in a run. The figure this entry previously
gave, "about thirty", described a seal nobody could actually mine. The constant
was deleted rather than zeroed so it cannot be switched back on without reading
why it went, and its one other consumer -- the holy report in `Commands.cs` --
went with it. The edges are free now, which is what the weighting always
claimed: chewing a corner off a seal is nothing, and taking the altar is the
act.

**Nothing is wired into the Holy Order trigger directly.** It already reads
alignment, so desecration simply becomes the largest alignment sink in the game.
A separate notoriety bump or a `FactionId.HolyOrder` standing hit would have been
a second number saying the same thing.

**The reward is at the heart.** Entry 20 has the seals warding rebirth sites, so
breaking one hands back a buried discovery through
`BuriedRemainsController.GrantExternalDiscovery` -- an entry point whose own doc
comment named this arc long before it had a caller. Edges cost and give nothing.

**And the vault pays properly.** On top of that discovery, breaking the dead
core vault's heart grants `VaultResearchPoints` 60 research and a full level of
XP. Sixty is sized against the tree it spends into rather than picked round:
`LitanyofGraves` at 60 is the dearest node in the game and `TheSearingGlance` --
tier 3, and the Light core's own trap -- is 30, so this is one top node or two
tier-3 ones, against the buried-remains duplicate fallback's 10. The XP grant is
`XPToNextLevel`, which is the WHOLE threshold for the current level rather than
the remainder, so a player near the top of the bar banks the overflow instead of
being shortchanged for having earned it. Nothing is lost to that overflow:
`CheckLevelUp` neither loops nor levels -- it raises `LevelUpAvailable` and the
player confirms -- and `ConfirmLevelUp` subtracts exactly one threshold before
re-checking against the next.

**And the wisp knows it when it sees it.** Revealing the vault speaks two lines
from entry 34's warden reading -- the wisp has stood where the player is standing
before, and not with them. Entry 34 records why those are not gated on
`CoreMemory.Lived` when the Sealed Gate line is, and why the vault is tested
ahead of `IsHolyArchetype` in `SpeakForSite`: the terrain layer is right to count
the vault among the Church sites, but it is not a seal of theirs. It is the older
thing the Church built over.

The vault also takes its OWN alert copy and its own wisp line (`holy_break_vault`),
and is ALWAYS Critical rather than sharing the seal ladder's first-break Warning.
There is one vault in a dungeon and it is built around a dead core, which is to
say around what the player is; that is not a beat to learn about from a quiet row
in the log.

### A heart under the carriageway (BUG, now guarded)

`GenerateSites` subtracts road cells from `plan.ruinsCells`, and the heart lives
in that band. `SiteData.heartCell` was then written from `plan.heartCell`
UNCONDITIONALLY, so a heart under a road left a site claiming a heart at a cell
that had become open carriageway. It could never be mined, so the seal could
never be broken -- no alignment cost, no research, no wisp line, no Holy Order
pressure -- and nothing reported it.

Both `WardChapel` plans put their heart on the plan CENTRE and anchor
`AlongRoad`, which lands the centre on the carriageway by construction rather
than by chance. Every Ward Chapel rolled since the holy sub-quota shipped has
therefore been a dead seal. The three vaults do the same thing and are spared
only because floor-4 truncation cuts the road out of the footprint before the
subtraction runs -- protected by an accident of ordering on one floor.

The guard DROPS the heart claim and logs an error naming the plan, on the same
reasoning as the outpost guard beside it: a site with no heart is merely
undecorated, while a site with an unreachable one is broken in a way nothing
downstream can detect. The validator also fails a centred heart on an
`AlongRoad` plan outright, because that costs nothing to catch at authoring time
and a full run to notice otherwise.

**The routing work removes the cause.** Once a site keeps every cell and the
road is routed through an authored lane rather than carved through the building,
nothing subtracts a heart. The guard stays afterwards as a tripwire.

### The anchor fallback is now refusable (SHIPPED)

`TryPickAnchor` degrades to a free pick when its preference cannot be met, which
is right for a ruin -- a floor may simply have no junctions -- and wrong for a
building whose meaning IS its position. A Ward Chapel is the chapel a sealing was
administered from; stranded in open rock by a fallback, it explains nothing.
`@anchor_required: yes` refuses the degrade, and the plan is SKIPPED with a
reason rather than misplaced. Same principle as the three-state `@doors:` header:
a silent fallback that looks identical to success is the failure mode this
project keeps paying for.

### The symbol reference (SHIPPED)

`Assets/ScriptableObjects/Sites/Plans/_SYMBOLS.txt` carries every glyph and
header, with the drape arithmetic written where it bites. It is never parsed --
plans are an explicit `TextAsset` list on the profile, not a folder scan -- and
it sits with the plans because that is where you are when you need it.

**The authoring guide is `Docs/DCR_Guide_Content_Authoring.html`, in this repo.**
Recording it here because it has been looked for in the wrong place twice: it is
NOT in the project-knowledge folder, and `DCR_Guide_Site_Plans_Authored.html`
there is a different, older document. The repo copy is canonical for site-plan
format.

### The lane, and roads that thread sites (PART SHIPPED)

A road crossing a site used to punch its own hole: `GenerateSites` subtracted
road cells from both `cells` and `ruinsCells`, so the interior walls vanished
where the carriageway passed and the gap was paved to read as built-around. With
the holy sub-quota adding three to six `AlongRoad` sites per floor, that stopped
being an occasional crossing and became routine, and a 45-degree road takes a
long diagonal bite out of a hold.

The replacement is authored rather than inferred, like the doors before it.
`'~'` is the LANE: the route a road takes THROUGH a site, door to door. The site
keeps every cell and renders its own paving over the whole lane; the road is
ROUTED through it -- polyline, caravan graph, walkability -- but never drawn
there. A site with no lane has no through-route, and a road reaches its door and
stops, which is what the sealed vaults want.

**Three cells wide WHERE THE LANE CUTS MASONRY, five to match a five-wide
gate.** Width is an authoring rule of thumb for a corridor, not the rule: across
open floor a narrow lane is fine, because the open ground either side supplies
the drape clearance the corridor would have had to supply itself. Corrected
here because the earlier wording read as a flat minimum and the validator does
not measure width at all.

Measured, for the corridor case: a lane one or two wide has ZERO walkable cells
in half the orientations, because a floor cell is walkable only when y+1 and y+2
are also floor. One wide happens to work north-south and fails the moment the
plan rotates -- correct in the orientation it was drawn in, which is the worst
kind of fault.

**The validator checks it by PATHFIND, not by width.** Width is a proxy and
proxies drift from what they stand for; the question that matters is whether a
walker can get from one door to another through the lane, so that is what is
asked, in every orientation the plan is allowed to take and no more. A
`@rotate: no` vault is checked in its one orientation -- failing it for a
quarter turn it never takes is the false positive that retired the first door
gate.

A lane in more than one piece FAILS: a road is a path, not an archipelago. A
lane reaching fewer than two doors WARNS and is decorative. A door on the lane
with nothing opposite it WARNS, because a road entering north and leaving east
turns ninety degrees and orphans everything behind it. A lane narrower than the
door it meets WARNS -- legal, but the street pinches at the gate.

### Lane routing, measured before it was written (DECIDED)

The routing itself is designed and simulated but NOT shipped. `Tools/sim_lane_routing.py`
is the headless check, and it is recorded here because it settled four questions
that would each have cost a test cycle, and killed two designs that read fine on
paper.

**A road reaches a gate because the SITE moves, not the road.** `@anchor_on: door`
shifts a plan by -doorMid so its gate lands on the anchor cell, and refuses the
anchor unless the road's local heading is within `DoorFacingCos` of the gate's
normal. That is shipped machinery -- `PlaceDeadCore` has used it since the vault
-- and it is what makes routing tractable at all. Without it a village's gate
sits thirty-seven cells from its anchor, and a road at any angle has drifted
past it by then: measured, a 37-cell reach drifts 12 cells at 18 degrees and 21
at 30.

**One gate is exact and the other is not.** Anchoring fixes ONE of a laned site's
gates onto the carriageway. The far gate is at a vector the plan fixes, and the
road's own heading over that span is a different vector, so the road must bend to
reach it. Both approaches are therefore sized from their OWN miss. Sizing both
from the exit's miss put the whole correction on an ingress budgeted for nothing
and bent it 57 degrees at the gate mouth.

**Placement is undirected; routing is directed.** A road is travelled both ways,
so the heading test uses an absolute dot product and a gate facing east is served
by a road heading either way. A polyline runs from one end to the other, so the
rewrite must be laid down in that order. Conflating the two produced 180-degree
doublebacks on half of all placements: the anchored gate was used as the entry
regardless of which side of the site it sat on.

**A lane is entered SQUARE, from the middle of the gate.** Any threshold cell
satisfies the validator, which only asks whether a route exists. Routing has to
lay a polyline, and a start offset sideways along the run makes the segment from
the gate to the first waypoint DIAGONAL -- a fixed 26.6-degree corner just inside
every gate that no amount of approach budget could touch, because it was never in
the approach. Where the gate's middle is buried, the router REFUSES rather than
falling back to a sideways cell: the fallback is what put a re-drawn line through
the GallowsCourt gallows platform.

**The corner at the gate mouth IS the placement cone,** and no geometry buys it
down. A quadratic Bezier was tried, leaving the gate along the wall's normal and
arriving along the road's heading; it lost at every setting -- 41.6 degrees
against 39.4 for a plain straight segment, two extra waypoints a site, and no
sample count or approach length recovered the difference. The control point sits
where the tangents meet, which on a shallow miss is far off, so the curve swings
wide. Do not re-add it without re-running the sim.

A rasterised road presents only sixteen bearings, at 0, 11.31, 18.43 and 45
degrees off the nearest axis, four of each. The cone therefore has four real
settings and nothing lives between them:

| cone | bearings usable | worst elbow |
| ---- | --------------- | ----------- |
| 45 degrees | 16 of 16 | 45 |
| **30 degrees (kept)** | **12 of 16** | **30** |
| 15 degrees | 8 of 16 | 11 |

**Thirty is kept.** Fifteen buys the elbow down to 11 but throws out every 18-degree
road, a third of every anchor a laned site can use, and floor 3 must seat a
village, an outpost and five or six holy sites at `minSpacing` 90. A missed holy
minimum is a real bug; a 30-degree kink at a town gate is a road going into a
town. `MIN_APPROACH` is 48, two meander steps -- 24 gives a worst corner of 39.4
degrees, 48 gives 36.7, and past that what is left is the cone.

Over 8880 placements -- every laned plan, sixteen bearings, every rotation the
cone allows, the anchor jittered across the carriageway width -- the re-derived
centreline touches masonry ZERO times, no approach clips a building, no
centreline cell is drape-blocked anywhere but a gate threshold, and the worst
polyline runs to 20 waypoints. Three plans fall back at some rotations and the
sim names each: `SealedCrypt_TheCoffinRow` and `HollowSanctum_TheKneelingHall`
have gate middles buried under the drape, and `SunkenPlaza_TheGallowsCourt`
cannot route one axis of its roundabout.

**Not yet built:** `SiteData.laneCells`, footprint protection in
`RebuildRoadCells`, the Free-anchored keep-clear rule, `RouteRoadsThroughLanes`
itself, and the `GenerateSites` restructure that writes site data before
rerouting so both the generate and load paths read one source of truth.
`EmitDoorRuns` on the general path, the anchorable run LIST and `@anchor_on:
door` in `Fill` all SHIPPED -- see the entry below.

### The door anchor, shared and fed a whole line (SHIPPED)

Lane routing's placement half. No road is rerouted here and nothing about
walkers changes; what lands is the machinery a laned site needs to be standing
in the right place before a road can be threaded through it, plus the one number
that says why a floor came up short.

**The heading test was a density lottery wearing a facing test's clothes.**
`TryRoadHeading` fits a principal axis to the road cells within six of a point
and refuses on fewer than three. It was fed `roadAnchorCells`, which is a
stride-12 sample -- so three cells within six turned up only where several roads
converged, and door anchoring accepted or refused an anchor mostly on how busy
that corner of the floor was. Measured over the shipped road profiles with
`Tools/sim_door_anchor.py`: a sampled anchor resolved a heading **12.6 per cent**
of the time on floor index 2, **5.8** on 3 and **5.3** on 4. `RebuildRoadAnchors`
already computed the full centreline on its way to building that sample and then
threw it away; kept in `roadHeadingCells` and handed to the heading test alone,
resolution is **100 per cent on all three floors**. Anchor SAMPLING stays
thinned, because that is what the stride is for.

This also explains why the vault places at all today. At about one anchor in
twenty resolving, and roughly a third of those facing the gate, a single attempt
succeeds perhaps one and a half times in a hundred -- but `PlaceDeadCore` has 240
of them, so it lands almost always. The arithmetic that said door anchoring must
be failing was right about the rate and wrong about the outcome, and one
generated floor settled it. Do not assert runtime behaviour from arithmetic.

**`@anchor_on: door` was a vault-only feature by accident.** The block lived
inside `PlaceDeadCore`; `Fill`, `PlaceOutpost` and `PlaceVillage` never read the
flag, so the header on any plan those paths could serve did nothing and the plan
placed on its centre exactly as it had before the feature existed -- the same
silent no-op as `anchorRequired` being honoured inside `TryPickAnchor` and passed
by nobody. It is now one `TryDoorAnchor` called by all four, and a plan that
declares no anchorable run returns `placeAt == anchor`, so every procedural
placement is unchanged by construction rather than by inspection.

**`LocalPlan` keeps a LIST of anchorable runs.** The first-run-only rule is right
for a vault, which has one door by design, and wrong for anything with two or
four: the building could only ever be entered from whichever side was scanned
first. `FromAuthored` still learns nothing about headings -- the choice moved to
the placement, because that is where the heading is.

**The rng draw is conditional, and that is not a nicety.** `rot` and `mirror` are
INPUTS to the helper, drawn by each caller before it, because moving those draws
would change every world for a given seed. The choice among qualifying runs is
the only new draw, and it fires only when more than one qualifies. All three
authored `DeadCoreVault` plans declare exactly one outward-facing run, so the
vault path consumes not one extra draw and stays bit-identical in its rng stream.
Its accepted anchors DO change, because the heading data underneath it changed --
that is the point.

Where two runs qualify they are always an opposing pair: the test is an absolute
dot product, so a normal and its negation score identically. The roll therefore
decides which side of the carriageway the building sits on, which is variety
rather than a coin flip standing in for a rule nobody worked out.

**`rejectedNoDoorHeading`, general and holy.** One counter covering both ways the
gate can refuse -- no heading resolvable, or no gate facing one -- because with
the line undecimated the first case has all but vanished and the two collapse to
one meaning. Failures of the RE-VALIDATION after the shift are counted as
`too-close` instead, where `TryPickAnchor`'s own band-and-spacing failure already
lands: the site moved tens of cells and every test the anchor passed describes
somewhere the building is not. The guarantee paths cannot use these counters at
all -- `Build` overwrites `result.rejected*` from the general fill after they run
-- so each folds its own figure into its existing failure message.

**Nothing was stamped.** Only the three vaults and `GuardPost_TheColdWatch` carry
`@anchor_on: door`; no laned plan does. Stamping is authoring, it buys no visual
change until the routing half exists, and it would move floors 2 to 4 under their
seeds twice instead of once. The sim priced it in advance over 300 seeds: with
the two `WardChapel` plans stamped, floor 3 still fills its holy minimum in every
seed; with every road-anchored laned plan stamped it still does, but floor 2's
outpost misses 7 times in 300 and floor 3's village once. So the stamps are
affordable and are not free, and the outpost is the one to watch.

**Key files:** `AncientSiteBuilder.cs`, `TerrainFeatureGenerator.cs`,
`TESTING/Commands.cs`, `Tools/sim_door_anchor.py`.

### The preview window learns the markers (SHIPPED)

`Dungeon Core / Site Plan Preview` drew floor and masonry and nothing else, so
the three glyphs an author most needs to see while drawing them -- `'+'`, `'~'`
and `'X'` -- rendered as ordinary floor and ordinary stone. The window exists to
show what the grid cannot, and the markers were the part of the grid it was not
showing.

**Markers are drawn in HUE, never in VALUE.** Painting a marker as one flat
colour would have hidden the three faults the window exists for underneath it,
and a drape-blocked lane cell would then have looked exactly like a working one.
So every marker carries a PAIR: bright for the passing case, dark for the
failing one. The walkable/drape-blocked read and the drawn/never-drawn read
survive the annotation rather than being painted over by it. The heart is
magenta rather than a second red, because ColWallDead is already a loud red on
the cell beside it and two reds meaning two different things at three pixels a
cell is a guess, not a legend.

The LANE pair is the one that earns its keep. A lane cell buried under the drape
is a road that cannot thread the building, and it is orientation-dependent --
the drape is always in world +Y, so a lane can be clear on one quarter turn and
sealed on the next. The rotation slider therefore matters more on a laned plan
than on any other, and the window now says so in a HelpBox rather than leaving
it to be found by counting pixels.

Three counters ride along, because a number carries at three pixels a cell where
a colour does not: lane cells and how many are drape-blocked, door cells grouped
into runs, and **how many runs have no outward normal**. That last is reported
because a zero normal is the silent failure that turned door anchoring into a
no-op -- nothing throws, nothing looks wrong, and the plan simply places on its
centre like every other site. It is read from the UNTRANSFORMED plan: a normal
rotates with the plan, so a zero normal is zero in all eight orientations.

Platform `'='` and stair `'^'` are deliberately NOT coloured. They are floor
markers like the others and the same pair rule would fit them unchanged; they
are left out because nothing has cost a round on them yet and the legend is
already three rows. Adding them is two colours and two branches when it does.

**This entry reverses the "not in this pass" note below.** That note argued the
validator's per-run report was what annotation needed. It was right about the
report and wrong about the window: a report tells you a plan is wrong, and the
window tells you WHERE.

### The seat lens: the preview window learns the pipeline (SHIPPED)

The marker colours above show what a plan IS; since the chord anchoring arc the
interesting faults live in what the seat pipeline would DO with it -- which
orientation the signed rule picks, whether both gates resolve, whether the lane
routes, where a spur's standoff lands, and on refusal WHICH stage refused. All
of that existed only as counters in a floor report, so every seat fault cost a
generation to see. The Coffin Row's lane fault was a number on floor 3; it is
now a sentence in an editor window.

**The window never re-implements the selection.** `TryDoorAnchor` gains an
optional `SeatDiag` collector, null on every engine path and therefore free,
and `PreviewSeat` drives the SAME orchestration against a synthetic chord
through the anchor at a chosen bearing. A parallel copy of the ranked loop in
the editor layer is exactly the drift family that shipped the Abs mis-port and
the one-gate port; the collector keeps one orchestration as the only truth.
`TryGateCell` likewise gains an optional buried-cell list rather than the
editor growing a second run-finder -- the even-length construction trap its own
comment records is precisely the sort of thing a copy re-ships.

The synthetic chord's ends are FREE, nodeA and nodeB at -1, so the GateMinStub
end clamp is exempt by construction. That is correct rather than a shortcut: a
stub only means anything against a real network, and the preview has none.

What the lens shows, per authored plan:

- The GATE overlay: the cell TryGateCell chooses per run at the displayed
  orientation, and every run cell it judged buried -- judged WITH the approach
  carved, which is what the engine sees at a gate. The window's base colours
  still judge in a vacuum, which is render truth for cells no road serves, and
  the two truths are drawn as base colour and inset so neither paints over the
  other. The vacuum read over-reported burial on 7 of 16 laned plans; both
  reads are wanted, for different questions.
- The seat VERDICT at a chosen bearing and chord width: threading, spur or
  doorless class, the rotation the signed rule picks with a snap button, both
  gates, the routed lane, the standoff or the sidle, and on refusal the stage
  that refused -- in the pipeline's own words, since they are its words.
- A BEARING ROSE over all 24 bearings the audit sweeps: threaded, spurred,
  sidled or refused per bearing at a glance. A refusal is an authoring fault
  only if it holds across the rose, and the rose is what says so.
- Platform `'='` and stair `'^'` colouring, reversing the deferral recorded
  above at exactly the price it quoted: two colours and two branches each.

A plan with door glyphs but no lane and no `@anchor_on: door` reads as the
DOORLESS class here, because that is what it is to the pipeline -- doorAnchors
is filled only by a lane or the header, exactly as `FromAuthored` builds it.
The audit's inert-door plans read that way in the window on purpose; the lens
showing a sidle where an author expected a docking is the audit item made
visible.

**Key files:** `SitePlanPreviewWindow.cs`, `AncientSiteBuilder.cs`
(`SeatDiag`, `PreviewSeat`, `PreviewGateCell`, `PreviewKeepClearRadius`).

### The tag audit, and the heart out of the river (SHIPPED)

`Tools/audit_plan_tags.py` walks every plan header the way the parser does --
same key set, same value spellings, same silent skips -- because the silent
skips are the point: the parser ignores an unknown `@key` and a colonless `@`
line without a word, so a typo'd tag is a plan that quietly means something
else. The audit found the roster CLEAN on that whole class, and found four
things the parser could not see:

- **The Dry Span said `@doors: none` while drawing six door cells.** The lane
  and its gates were added after the stamp; the stamp lied to the validator.
  Now `marked`. Engine behaviour unchanged either way -- doorAnchors fills
  from the lane, not the stamp.
- **Two plans sit outside `authoredPlans`, and both by decision.** Twenty-nine
  plans on disk, twenty-seven wired. The Pilgrims Way is now DELETED. The
  Hearth of the Deep stays on disk and stays UNWIRED on purpose -- held back,
  not forgotten; this entry is the record that says so, because an unwired
  plan and an accidentally-dropped one are indistinguishable in the asset.
- **The Last Door now says `@anchor_on: door`.** A road-end gate that sidles
  away from the end it caps is a guard post with delusions; docking is
  mandatory, and on a roadless floor the plan refuses rather than stand in a
  field. The Counting Floor keeps its sidle on purpose -- a plaza BESIDE the
  crossroads reads fine, and docking one door of sixty-eight would not.
- **Both uncommented `@rotate: no` vaults now say why** (decor prefabs, the
  Ninefold Cist's reason), so the next reader can tell load-bearing from
  leftover.

**The validator's centred-heart gate is retired.** It enforced the carriageway
subtraction, and stage 2 removed that mechanism entirely -- roads arrive at
doors, lanes route around masonry. The gate had inverted into a liar: it was
failing both WardChapel plans, whose centred hearts inside lane-ringed masonry
islands are exactly the RIGHT shape now, for a deletion that can no longer
happen.

**The river spares the heart.** The one remaining way to lose a seal was a
river crossing its cell -- rivers deliberately erode sites, and they run after
the heart-loss check at site build, so the loss was silent. The erosion now
exempts the heart cell alone: a capped stone standing in the watercourse reads
as intent, an unbreakable seal reads as a bug, and the wash keeps every other
cell it took before.

Undecided, recorded for the Holy Ground arc: the five laned plans whose
effective anchor is Free (The Weeping Font, The Nine Chains, The Drowned
Choir, The Kneeling Hall, The Coffin Row) -- re-anchor onto roads or keep the
lanes as paving. The Coffin Row's lane fault gets reworked in the seat lens
first either way.

**Key files:** `Tools/audit_plan_tags.py`, `SitePlanValidator.cs`,
`TerrainFeatureGenerator.cs`, six plan files, one plan deleted.

### The door rule, rebuilt on declaration (SHIPPED)

The first door-rule gate was retired for failing twelve shipped, working plans.
It is back, and the reason it cannot repeat that is MEASURED rather than argued.

The tightest structural definition of a door available without a glyph is a
floor run of two or fewer cells bounded by masonry at BOTH ends -- a passage
THROUGH a wall, which by construction exempts a niche, since a niche is bounded
at one end only. Scanned across the 29 shipped plans, that definition returns
**815 hits in 15 of them**, 213 in `TheShrinehold` alone. None are doors. They
are stall gaps, grave rows and column spacing, and a two-cell gap between two
grave slabs is structurally IDENTICAL to a two-cell doorway. No inference can
separate them, so the author declares: `'+'` is the door glyph, floor plus a
marker exactly as `'='` and `'^'` are, and the gate reads only `'+'` cells.

**This entry's own claim is therefore narrowed.** It previously recorded the
rule as covering "all doors, passages, stall gaps and grave rows". Those 815
furniture gaps are shipped, working geometry, so the rule never applied to them
in practice and now does not claim to. Declared doors and passages, and nothing
else.

**Three is demonstrated, not derived** -- the first derivation was wrong and
predicted a 3-gap would seal under rotation. Two chambers separated by a wall
with a gap of N, at wall thicknesses of one, two and three cells: a gap of 2
leaves the interior in two disconnected pieces in the orientation it was drawn
in, and a gap of 3 or more is a single piece in all eight orientations. Wall
thickness is irrelevant. The drape is the mechanism: a floor cell is walkable
only when y+1 AND y+2 are also floor, and a two-cell run never gives its bottom
cell both.

**`@doors:` is REQUIRED, with three states.** `unmarked` (not yet annotated --
passes, and the report keeps a roll-call), `none` (genuinely has none), `marked`
(doors drawn with `'+'`, rule enforced). A missing header FAILS, so "not filled
in yet" can never be mistaken for "nothing to fill in" -- the ambiguous-default
trap this project refuses everywhere else. A wrong VALUE is a parse error rather
than a silent fall back to absent, because "unknown @doors 'markd'" sends the
author to the right line and "missing @doors header" sends them hunting for one
that is already there.

All 29 shipped plans were stamped `unmarked`. Which gaps are doors is authoring
judgement, and guessing at it is precisely what killed the first gate.

Not in this pass: tunnel termination -- that a tunnel must meet a door and there
either end or continue out a separate one -- which waits until the glyph has
been annotated and there is something to check against. The preview window's
own marker colouring DID land, and has an entry of its own above.

**The ladder.** One wisp murmur on the first CLAIM of hallowed ground, which is
free and therefore lands while the real decision is still ahead of the player. A
Warning alert on the first seal broken anywhere, Critical on every one after, on
the severity layer from entry 19's arc -- and the vault outside that ladder
entirely, always Critical, though it does consume the first-break rung so a seal
taken afterwards is Critical too.

**Entry 32's rejection is reversed.** That entry rejected a Holy Ground
desecration echo because `Desecrate` was a stub with no caller. `CoreMemory`
now carries `FirstDesecration`, bound to `TutorialFlags.LightCandle` rather than
`PrayShrine` -- praying is already spent on the buried echo, and the candle is
the sharper pairing anyway: in life you lit one at a shrine, and here you break
one.

**Diagnostics.** `Commands / Log Holy Ground State` reports alignment, hallowed
cells claimed and mined per floor, what those cells have cost, and per seal
whether it is revealed and whether its heart is still in place. Alignment has
half a dozen contributors and cannot answer "did that seal register" on its own.

**Floor 0's band was corrected to 0.55 outer** once the floor's radius was
measured at 100 cells. At 0.7 a seal could anchor 70 cells out, and entry 19's
reach arithmetic puts a plausible late run at roughly 65 per cent of the radius
-- a seal nobody reaches is content that does not exist.

**Not built:** a deed definition for `first_desecration`. The call site exists
and is a no-op until one is authored; the CoreMemory echo does not depend on it.
Art for the four Church families is deferred to the sprite backlog -- they render
on the shared stone family for now.

**Key files:** `AncientSiteProfile.cs`, `AncientSitePlanLibrary.cs`,
`AncientSiteBuilder.cs`, `FloorFeatureSaveData.cs`, `TerrainTypeMap.cs`,
`TerrainFeatureGenerator.cs`, `TerrainResistanceTable.cs`, `InfluenceField.cs`,
`FeatureRevealController.cs`, `Editor/SitePlanValidator.cs`,
`ScriptableObjects/Floors/AncientSiteProfile.asset`, and eight plans under
`ScriptableObjects/Sites/Plans/`.

### Chord anchoring, re-derived from the engine (DECIDED)

Stage 2 is not written. What is settled is the geometry it must implement, and
four corrections to how that geometry was being measured. Every entry below is
an engine measurement or a demonstrated result, not a preference.

**The drape sources from unmined rock, not from a plan's own floor.**
`TileInfluenceManager.IsUnderOverhang` blocks a cell when the cell one or two
north of it is unmined rock inside the disc; `DrapesFrom` returns false for any
mined cell. A plan judged in isolation therefore treats everything outside its
footprint as a drape source and reports every north-facing gate buried, because
the road about to be carved outside that gate is not in the plan. Measured cost
of the error: seven of sixteen laned plans reported ZERO viable placements and
roughly half of all refusals across the other nine were the same artefact. This
supersedes the reading that `SealedCrypt_TheCoffinRow`, `HollowSanctum_TheKneelingHall`
and `SunkenPlaza_TheGallowsCourt` have gate middles buried at particular
rotations -- that is mostly this, not the plans.

**A road meets a gate at a chosen CELL, not at the run's middle.** In a vertical
wall the drape runs along the door, so a three-cell door has only its
southernmost cell walkable: y+2 is the wall above the run. Measured across every
plan and rotation: 216 door runs, 34 with a buried middle, and ZERO with no
walkable cell at all. Choosing the walkable cell nearest the middle costs no
re-authoring and refuses nothing. Among the 34 is `DeadCoreVault_TheNinefoldCist`,
which is `@rotate: no` with a single east-facing three-cell door, and
`AncientSiteBuilder.TryDoorAnchor` seats a site by `chosen.mid` -- so that vault
is currently aligned to a cell nothing can stand on.

**An approach may not be emitted as an ordinary chord.** `Rasterise` runs
`BuildEdgePolyline` on every chord, and 86 per cent of floor 4's viable approach
stubs are long enough to meander at an amplitude of six cells. Handing the same
placements to the raster instead of keeping their waypoints: 372 masonry contacts
and 415 doublebacks, because without the square entry the road arrives at the
gate facing the wrong way and reverses into the lane. `RoadChord` therefore
carries an explicit waypoint list that the raster honours and does not meander.

**Seating clamps on the FOOTPRINT projected on the chord, not on the gate-to-gate
span.** A village's gates sit at its face middles, so on a chord at 38 degrees to
the plan axes the footprint reaches about forty cells further than the gates do
-- far enough that the chord's own endpoint lands inside the building, and every
approach drawn to it must cross masonry to arrive. Measured: 289 masonry contacts
on the gate span, 0 on the footprint.

**The gate mouth budget is 90 degrees, not 30.** Thirty came from `DoorFacingCos`,
which tested which door BEARINGS a rasterised road could serve -- not how sharp a
corner a walker may turn. Nothing at runtime consumes corner sharpness:
`DwarfWalkerPuppet` sets `flipX` from the sign of dx and bobs, with no heading
interpolation, and the authored village lanes already contain 90-degree street
corners that have shipped for months. The real failures are priced separately and
both must be zero: a centreline cell on masonry is a walker in a wall, and a turn
past 90 degrees is a road reversing on itself.

**Geometry, as measured green.** Square out of the gate three cells along the
door's own normal, one halving waypoint at eight on the bisector, then a straight
tail of at least eight to the chord end; waypoints are DROPPED rather than
shortened when the room is short, because an arm laid down with no tail behind it
lands beside the endpoint rather than on the line to it and measured 76 degrees on
an eleven-cell stub. `MIN_STUB` is 20: sweeping it against mouth angle and
placement rate gives 95/98/100 per cent of floor 2/3/4 chords at 12, 87/96/100 at
20 and 68/87/99 at 32. Over 10,167 placements the worst gate mouth is 60.5
degrees, doublebacks are zero and masonry contacts are zero.

**Carried, not fixed here.** `SealedCrypt_TheCoffinRow` refuses about half its
chords with "lane not connected gate to gate", which is a real authored-lane
issue and distinct from the drape artefact above. Floor 2 seats a village on only
32 to 42 per cent of its chords, which is sound -- a floor needs one -- but it is
the tightest margin in the set.

**Proof:** `Tools/sim_chord_anchor.py`, which reports GREEN only when masonry and
doublebacks are both zero and prints the meandered comparison alongside.

### Stage 2a: the road layer learns waypoints (SHIPPED)

The foundation the chord-anchor geometry needs, and nothing else. No site
behaviour changes here: no chord is given waypoints yet and no road is a Lane
yet, so every floor draws exactly as it did.

**`RoadChord.waypoints`.** Interior points a chord's polyline must pass through,
in travel order. A chord that carries them is drawn STRAIGHT through them and
takes no meander, and consumes no rng. `Rasterise` ran `BuildEdgePolyline` on
everything, and 86 per cent of floor 4's viable approach stubs are long enough to
meander at an amplitude of six cells -- so an approach emitted as an ordinary
chord loses its square entry, arrives at the gate facing the wrong way and
reverses into the lane. Over the same 10,167 placements: 0 masonry contacts and
0 doublebacks with waypoints kept, 372 and 415 with them handed to the raster.

**`RoadKind.Lane = 2`, appended.** A rail whose polyline is the authored lane
walked gate to gate. `RebuildRoadCells` paints nothing for it and skips it before
any segment id is drawn, on generation and on load alike, so both paths partition
ids identically. It exists so `DeepRoadGraph` stays connected through a site:
`Build` clusters raw polyline endpoints at merge radius 6, so without a rail the
two gates of a village 30 to 70 cells apart become separate nodes and the network
is severed at every hold. No save field and no migration -- topology is derived
from `RoadData.polyline` endpoints and nothing persists it.

**`ApproachWaypoints`, with the geometry as measured.** Square out three cells
along the door's own normal, one halving waypoint at eight on the bisector, then
a straight tail of at least eight to the chord end; waypoints are dropped rather
than shortened when the room is short. `GateMinStub` is 20. The constants are
`GateSquareEntry`, `GateSplitArm`, `GateTail` and `GateMinStub` on
`RoadNetworkBuilder`, and they are asserted equal to the sim's by the delivery
script rather than kept in step by hand.

**There is no C# compiler in the container**, so the port was checked by
re-implementing it branch for branch in Python and diffing against
`Tools/sim_chord_anchor.py` over 200,000 random gate/normal/endpoint cases: zero
mismatches. Brace balance cannot catch transcription drift; that can.

**Not yet built:** stage 2b, where `AncientSiteBuilder` seats against chords and
the four negotiating mechanisms come out.

### Stage 2b: sites seat against chords (SHIPPED)

The pipeline is now PLAN, then SITES, then DRAW. `GenerateRoads` chooses the
network and stops; `RasteriseRoads` draws it after the site pass. A building is
seated against a chord before any road is rasterised, which is the whole point --
four mechanisms used to negotiate that boundary after the fact, and every one of
them exists because the negotiation happened too late.

**Anchors come from the plan, not from drawn cells.** `AncientSiteBuilder.Build`
and the four placement paths take a `RoadPlan` in place of the four cell lists.
`Junction` samples `plan.nodes`; `AlongRoad` and `Crossing` take an exact point
along a chord, held `GateMinStub` back from either end so both approaches have
room; `RoadEnd` is a chord end with `nodeA` or `nodeB` at -1, with no cell scan.
`TryPickAnchor` returns the chord index, so the seating step knows which road it
is answering to.

**The heading is exact, so the estimator is gone.** `TryRoadHeading` fitted a
least-squares principal axis over cells within radius 6 and resolved at roughly
one anchor in twenty -- it read as a facing rule and behaved as a density rule.
A chord has a direction. `TryRoadHeading`, `RoadHeadingRadius`,
`roadHeadingCells`, `roadAnchorCells`, `roadJunctions`, `roadEndCells` and
`RebuildRoadAnchors` are all deleted.

**Rotation is chosen, and the cone is deleted with it.** `DoorFacingCos` refused
any gate facing more than 30 degrees off the road, which was right while the road
was already drawn and the site had to fit itself to it. Against a chord the site
turns to face the road, so every bearing is servable and no anchor is refused for
facing. Measured over 10,167 chord placements with rotation free: worst gate
mouth 60.5 degrees, zero doublebacks, zero centreline cells on masonry.

**Procedural paths keep their rng stream.** The callers still roll `rot` and
`mirror` before seating and those draws are untouched; a plan with no
`doorAnchors` returns before any of the new code runs. `Rasterise` moving after
the site pass does move its meander draw, so worlds change for a given seed --
the node set, chord count and chord kinds do not.

**Diagnostics** are counted off the plan: nodes in band, chords with an end in
band, and free chord ends in band, replacing counts of a thinned sample of drawn
cells that anchoring no longer reads.

**Not yet built:** stage 2c -- splitting a chord at a laned site's two gates,
the `RoadKind.Lane` rail through it, gates met at the walkable run cell rather
than the run middle, the footprint-projection clamp, and the removal of the
carriageway subtraction loop, `TruncateRoadsAroundVault` and `pavedRoadCells`.
Until then a road is still drawn across a site it was seated against and still
subtracted afterwards, exactly as before.

### Stage 2b hotfix: two compile faults (SHIPPED)

Both mine, both of a class the container cannot catch -- there is no C# compiler
here, so brace balance, ASCII, anchor uniqueness and call arity are checkable and
TYPE RESOLUTION is not.

**`DoorRun` is a struct, not a class.** `AncientSitePlanLibrary` declares it
`public struct DoorRun`, so `DoorRun bestRun = null` and `bestRun == null` do not
compile. The rotation chooser now carries a separate `haveRun` flag, which is
what a value type needs to express "nothing chosen yet". Swept the rest of the
stage 2a and 2b diffs for the same shape: every other null test is on `RoadPlan`,
`RoadChord` or a `List`, all reference types.

**`private RoadPlan lastRoadPlan;` was deleted by accident.** The delivery script
removed the four road-anchor cell lists by extracting the block around their
shared comment, and the extraction walked back one declaration too far and took
the plan field with it -- the one field the whole stage depends on. Restored in
place. The lesson is about the method, not the field: a region extracted by
walking outward from a comment must have its END as well as its start asserted,
because a deletion that removes one line too many still balances its braces and
still passes every text check.

The delivery script for this fix carries a struct-vs-null stand-in that reads the
repo's own `public struct` declarations and fails on any `= null` or `== null`
against one.

### Stage 2c: the gate is a cell, and the clamp is the footprint (SHIPPED)

Two corrections to where a seated site actually sits. Both measured, both
independent of the chord splitting still to come.

**The gate is a CELL, not the run's middle.** In a vertical wall the drape runs
along the door, so a three-cell door has only its southernmost cell walkable --
y+2 is the wall above the run. Measured across every plan and rotation: 216 door
runs, 34 with a buried middle, and ZERO with no walkable cell at all, so choosing
the walkable cell nearest the middle costs no re-authoring and refuses nothing.
Among the 34 is `DeadCoreVault_TheNinefoldCist`, `@rotate: no` with one
east-facing three-cell door -- door anchoring seated that vault on a cell nothing
can stand on, every time, because rotation is off and there was no other choice
to make.

**Walkability is judged with the approach carved.** `TileInfluenceManager.DrapesFrom`
returns false for any MINED cell, and the road about to be carved outside a gate
is mined. Judging a plan on its own instead makes every cell beyond the footprint
a drape source and marks every north-facing gate buried -- which reported seven
of sixteen laned plans as unplaceable and about half of all refusals across the
other nine. `MinedAt` treats the approach as one trunk wide, which is what
`Dilate` paints.

**The clamp is the FOOTPRINT projected on the chord, not the gate span.** A
village's gates sit at its face middles, so on a chord at 38 degrees to the plan
axes the footprint reaches about forty cells further along the chord than the
gates do -- far enough that the chord's own endpoint lands inside the building,
and every approach drawn to it must cross masonry to arrive. Measured: 289
masonry contacts clamping on the gate span, 0 clamping on the footprint. Tested
on the bounding box CORNERS: a quarter turn maps an axis-aligned box to an
axis-aligned box, so the extreme projections are always corners, and a village is
3721 cells against 240 placement attempts.

**Not yet built:** stage 2d -- splitting the chord at a laned site's two gates,
the `RoadKind.Lane` rail through it, and the removal of the carriageway
subtraction, `TruncateRoadsAroundVault` and `pavedRoadCells`. Those go together:
the subtraction is already inert, because `roadCells` is empty while the site
pass runs now, but deleting it before the split exists would let a road be DRAWN
through a building instead of merely negotiated with afterwards.

### Stage 2d: the chord splits at the gates (SHIPPED)

The arc closes. A laned site's chord is split at its own two gates: the road runs
in to the door, threads the AUTHORED lane, and leaves by the far gate. Nothing is
subtracted, nothing is truncated, nothing is rerouted, because no road is ever
drawn through a building.

**Three chords replace one.** The seated chord becomes the ingress `a -> gateIn`,
carrying the approach waypoints; an egress `gateOut -> b` is appended with the
same, reversed, and inheriting the broken gap -- that belongs to the FAR end, and
an ingress that inherited it would stop the road short of the gate it was drawn
to reach. Between them goes a `RoadKind.Lane` rail whose waypoints are every
authored lane cell. They are adjacent, so the polyline IS the route and Bresenham
between two neighbours cannot wander off it. `RebuildRoadCells` paints nothing for
a Lane: the site already drew that ground, and the rail exists so `DeepRoadGraph`
stays connected gate to gate. `Build` clusters raw polyline endpoints at merge
radius 6 and `gateIn` is byte-identical between the ingress and the rail, so they
share a node exactly rather than by proximity -- which matters, because `Route`
bridges clustered ends with a Bresenham stitch and a stitch across a gap would
cut the corner through masonry.

**The seat is recorded, not acted on.** A placement that seats successfully can
still be thrown out by the disc clamp, the twelve-cell floor or the walkability
guard, and a chord split for a site that was never placed would leave the network
cut around nothing. `SiteChordSeat` rides the placed site and
`SplitChordsForSites` runs last, once every site that is going to be placed has
been.

**One site per chord.** Splitting replaces the chord in place and appends two
more, so a second site holding an index into the original would be measuring
against a segment that no longer reaches it. A second site on the same chord
keeps its seat and is not threaded: the road passes its door rather than entering
it. Correct, and dull, which is what is wanted here.

**The lane corridor is `(lane | door)` and walkable, with BOTH approaches
carved.** A gate is drawn `'+'`, not `'~'`, so a lane-only corridor has no cell at
the threshold and every route refuses at its own front door -- sixteen plans once
failed identically on exactly that, which was a bug in the test rather than in
sixteen plans. `OnApproach` is now the single definition of where a gate's road
opens ground, shared by the threshold test and the corridor so they cannot drift.

**What came out.** The carriageway subtraction loop, `TruncateRoadsAroundVault`,
and the vault's road-yielding special case. All three were already inert after the
pipeline reorder -- `roadCells` is empty while the site pass runs -- but they
could not be deleted until the split existed, or a road would have been DRAWN
through a building instead of merely negotiated with afterwards. The heart guard
and the twelve-cell guard stay, reworded: only the core cavern can reduce a site
now, and it was the carriageway version of the second that ate floor index 2's
outpost.

**Still standing, deliberately:** `SiteData.pavedRoadCells` and the paving it
feeds. The list is now always empty, so the field is vestigial rather than wrong,
and removing a serialised field is a save-shape change that belongs in its own
pass rather than riding this one.

### Stage 2e: a lane is a declaration, and roads keep clear (SHIPPED)

Two faults found on the first live floor after 2d, both visible in one
screenshot: a road drawn straight down the middle of a dwarven village, its
masonry mined away along the way, and not one gate connected.

**A LANE IS A DECLARATION THAT THE ROAD COMES THROUGH.** `FromAuthored` filled
`doorAnchors` only `if (authored.anchorOnDoor)`, and exactly four plans in the
set carry `@anchor_on: door` -- the three `DeadCoreVault` plans and
`GuardPost_TheColdWatch`. **Not one laned plan does.** So every village, and the
outpost, reached `TryDoorAnchor` with an empty list, returned from its first
line, never seated a gate against the chord and never split anything. The vault
looked correct for the same reason inverted: it was the only archetype the
directive ever reached. A plan that drew a `~` has said where it expects a road,
which is the statement `@anchor_on: door` makes, made in the tilemap instead of
the header -- so a lane now fills `doorAnchors` too. Only the header still makes
meeting a road MANDATORY, carried on `LocalPlan.requireDoorAnchor`: a laned plan
that finds no chord is placed unthreaded, a plan that declared the directive and
finds no chord is refused.

**Roads keep clear of what they cannot enter.** Nothing subtracts a carriageway
out of a site any more, and that removed the thing that used to hide this: a
chord crossing a site does not quietly cost it cells, it MINES the masonry it
crosses and leaves the site claiming walls that are not there. `MarkNaturalFloor`
puts every road cell in `minedTiles`, and a mined cell cannot render as masonry.
`FootprintClearsChords` now refuses any placement whose footprint meets a chord,
with one exemption: the chord the site itself answered to. That one is the point
-- a laned site splits it at its own gates, and an unlaned doored one is meant to
have the road reach its door. Any other chord through the building was asked for
by nothing.

Measured on the bounding circle, conservative in the safe direction: it can
refuse a placement that would have fitted, never accept one that would not.
Sampling in-band positions against 20 planned networks per floor, the worst case
is the largest village on floor 2 at 54 per cent of positions clear, and
everything else falls between 85 and 100 -- affordable against 12 to 24 attempts
per site, and 240 for the guarantees.

**Consequence for the five Free-anchored laned plans.** `ChurchSeal_TheNineChains`,
`BlessedSpring_TheWeepingFont` and `SealedCrypt_TheCoffinRow` anchor `Free` and so
answer to no chord; they are now placed unthreaded and kept clear of every road,
which is what their decorative lanes have always effectively meant. Changing
their `@anchor:` is still an open authoring call, not a code one.

### Stage 2f: signed selection, the spur, and the free end (SHIPPED)

Closes the two faults from the first live floors, plus the audit's rulings.

**The entry gate is chosen SIGNED, and the mis-port is owned.** The sim's
`turn_to_face` picks the entry as the run most OPPOSED to travel and the exit as
the most aligned, scoring a rotation on the spread; the first C# port kept the
cone era's `Mathf.Abs` -- "a road has no forward" -- which is false the moment a
chord has a seated site on it, because the ingress arrives from `a` and the
egress leaves for `b`. Measured at 24 bearings across every plan: the undirected
pick chose a wrong-facing entry on HALF of them, and a wrong-facing entry is a
road drawn through the building to reach its far door -- the village on floor 3.
The stand-in that should have caught it had transcribed the sim's intent rather
than the shipped code's bytes; the new one transcribes the payload and agrees
with the sim on rotation and entry cell at 384 of 384 plan-bearings, with zero
wrong-facing entries. Orientations are ranked and tried in order, so a buried
gate falls through to the next-best turn instead of failing the anchor.

**The SPUR class -- stage 3, delivered.** A doored plan with no lane, or only one
usable run, is not passed through: it stands off the chord along its door's
outward normal at the smallest distance where EVERY cell of it clears the
carriageway by a cell -- exact, per cell, because the vault's cross shape would
pay about ten cells of unnecessary standoff to a bounding circle -- and a spur is
emitted from the take-off to the door, arriving square along the normal by
construction. The take-off becomes a node by splitting the host there, except
within two cells of an existing end, where the endpoint cluster does it for
free. Spur width is matched to the DOOR, held odd: a five-wide carriageway
centred on a three-cell door paints a jamb each side; a door-wide spur paints
exactly the doorway. The standoff search sign was caught by the stand-in before
anything shipped: standing off ALONG the outward normal moves the building back
ACROSS the chord, since the building extends behind the door -- vaults seated 0
of 40 bearings and a guard post needed a 20-cell spur; with the sign right the
guard post seats at every bearing on a spur of 4 to 11, and the rotation-locked
vaults on the near-perpendicular third of bearings, which 240 attempts of 64
anchor samples each covers many times over.

**A FREE chord end is exempt from the stub clamp.** A SealedGate exists to sit
where the road stops, and requiring approach room past a free end refused every
RoadEnd seat and sent the gates to the free-scatter fallback -- the floor 4 log
showed two road ends in band and every gate placed far from them.

**Audit rulings, recorded:** THE DOOR RULE is a MINIMUM of three cells in the run
direction, not an exact -- sixteen shipped plans carry wider runs, up to the
24-cell gate frontage of `SealedGate_TheWatchedRoad`, and none anywhere is
narrower than three. `BrokenAqueduct_TheDrySpan` is a doorless lane and so
keep-clear under 2e, which kills its Crossing anchor; the fix is authoring, two
glyph runs: the `~~~` in its row 10 and row 16 WALL lines become `+++`, making
the passage under the span a pair of opposed gates. `SealedCrypt_TheCoffinRow`
still routes only half its bearings gate to gate -- an authored lane fault,
unchanged -- and the Free-anchored laned trio remains an open `@anchor:` call.

### Stage 2g: the setback, the re-aim, and the counters (SHIPPED)

Three small pieces from the second live look, one of them the answer to a
silence.

**Roads stop one cell outside every gate.** `GateSetback = 1`: the ingress, the
egress and the spur all end at `gate + normal`, so the last road tile sits on the
threshold line rather than inside the doorway -- Brad's eye against the drawn
result. The Lane rail keeps its ends AT the gates; the one-cell gap to the
trimmed carriageway is inside `DeepRoadGraph`'s endpoint cluster radius, so the
graph still reads one node there and the walk stitch crosses the threshold cell,
which is the doorway.

**A contended spur RE-AIMS instead of vanishing.** The threading pass claims one
site per chord, and on the first live floor 4 a laned toll house claimed the
chord the vault had seated on -- so the vault's spur was skipped, silently, and
the guarantee site shipped with no road. The collision is legitimate; the
silence was the bug. A spur whose chord was split now aims at the nearest point
on the nearer surviving half, which is sound because the halves are colinear
pieces of the segment the standoff clearance was measured against -- distance to
a sub-segment can only grow, verified over 20,000 random splits with zero
violations -- and it arrives square through its approach waypoints, worst mouth
86.6 degrees over 5,000 re-aims against the 90 budget. Past `SpurReachLimit` 48
the connection would read as its own road, and the site is left unconnected AND
COUNTED.

**The counters.** `AncientSiteResult` carries `lanedSplits`, `spursEmitted` and
`spursLost`, printed by `Summary()`, and the seat records `spurEmitted` /
`spurReaimed` -- so the next report says what happened to every seat instead of
a screenshot having to. A spur seat with `spurEmitted` false was lost, currently
only to the reach limit after a contended re-aim.

### Stage 2h: Free means free, and doorless means exempt from nothing (SHIPPED)

The crypt with a road through it was not unlucky generation; it was two stacked
faults, both mine, both from the 2b rewrite.

**Free-anchored plans were being handed chord points.** The rewritten
`TryPickAnchor` handled `Junction` and `RoadEnd` explicitly and everything else
in `default:` -- which caught `Free` alongside `AlongRoad` and `Crossing`. Every
Free plan since 2b -- the seals, the crypts, the springs -- was quietly seated ON
a carriageway. The original code gave Free a null source and fell through to the
scatter; restored: `Free` never samples the plan.

**The doorless keep-clear exempted the chord it should have feared.** A doorless
site answers to no chord, but the early path forwarded `chordIndex` to
`FootprintClearsChords` as the exemption -- so a crypt seated on a road was
excused from clearing exactly that road, and its walls were cut where the
carriageway crossed. The exemption is now `-1`: a doorless site clears
everything.

**Doorless sites with road-flavoured anchors SIDLE.** The procedural collapsed
archives are `AlongRoad` by archetype default with no doors; with the exemption
fixed they would have thrashed against their own anchor forever. AlongRoad means
BESIDE the road, not across it: `TrySidleClear` steps the seat perpendicular to
the chord, either side, until every cell clears the carriageway -- exact, per
cell, like the spur standoff and for the same reason. Measured over every
authored doorless plan at every rotation and 24 bearings: 312 of 312 clear,
worst sidle 20 cells against the `MaxSidle` cap of 32.

**The seat counters reach the report.** `LastSitePlacement` already IS the
`AncientSiteResult`, so the Commands site report now prints the `seats:` line --
threaded, spurred, re-aimed, SPUR LOST -- per floor, next to the rejection
counters it always carried.

### Stage 2i: both gates, or neither (SHIPPED)

The seat counters earned their keep on their first outing: floor 3 placed five
laned sites and threaded three, and The Ash Stacks was one of the two that
placed unthreaded -- with its own chord still exempt from keep-clear, and an
exemption without a split is a road rasterised straight through the building.

**Both gates are tested before an orientation is accepted.** The sim's
`turn_to_face` always required a walkable cell at the entry AND the exit; the C#
ranked fallthrough tested only the entry, so a rotation with a walkable entry
and a buried exit was locked in, the lane failed AFTER the choice, and the site
placed unthreaded. The rank loop now carries the would-be exit -- the run most
aligned with travel, the same rule `TryLaneThrough` applies -- and an
orientation whose exit gate is buried falls through to the next-best turn.
Stand-in over every laned plan at 24 bearings: every plan threads at every
bearing except `SealedCrypt_TheCoffinRow` at its known 12 of 24.

**A laned site that still cannot thread refuses the seat.** The chord exemption
is earned by the split; a lane that genuinely does not route -- The Coffin Row's
authored fault -- now returns false and the caller retries at a fresh anchor,
counted under no-door-heading, the same failure family: a door arrangement the
road cannot serve. The 2d note that an unthreaded site "keeps its seat and the
road passes its door" is superseded -- it conflated passing with being exempted
through.

### Stage 2j: the truncation machinery is gone (SHIPPED)

`RoadTruncation`, `RoadGate`, `MinSurvivingRunFactor`, `TruncateAroundBlocked`,
`InGate` and `NearBlocked` are deleted from `RoadNetworkBuilder` -- one
contiguous block, eleven kilobytes, and a repo-wide search shows zero references
to any of the six outside it. They were the last of the four mechanisms that
negotiated the road/site boundary after the fact: heading estimation went in 2b,
the carriageway subtraction and the vault special case in 2d, and the gate
corridor's only consumer with them. Since 2d nothing has called any of this; it
was kept only until the chord split existed, so a road could never again be
DRAWN through a building with nothing to negotiate it back out.

The one deliberate survivor of the whole arc is `SiteData.pavedRoadCells`: now
always empty, vestigial rather than wrong, and serialised -- removing it is a
save-shape change that gets its own pass, never a rider.

### The decorative-lane five: every lane now carries a road (SHIPPED)

The tag audit's undecided item is decided. Five laned plans carried an
effective anchor of Free -- The Weeping Font, The Nine Chains and The Coffin
Row by explicit tag, The Drowned Choir and The Kneeling Hall by archetype
default -- so none ever sampled a chord and no lane among them ever carried a
road. All five now say `@anchor: AlongRoad`, and all five are SOFT: no
`@anchor_required`, no `@anchor_on: door`. On a roadless floor each degrades
to a free pick and its lane reads as paving, which is why every holy pool is
untouched -- The Nine Chains sits in the pools of floors 0 and 1 and The
Coffin Row and The Weeping Font in floor 1's, all roadless, and a hard anchor
would have refused there and thinned the pools the sub-quota entry sized.
HollowSanctum spawns only on floors 2 to 4, which all carry roads, so the two
sanctum plans never degrade at all.

**The Coffin Row's lane fault, carried from the chord-anchoring arc, is
fixed, and the mechanism was not what the refusal said.** "Lane not
connected gate to gate" read as a routing failure; it was a corridor
MEMBERSHIP failure. The rails step diagonally in toward each five-wide door,
and at north-south rotations the drape buries the step cells -- the wall two
north of them is rock. The route round them exists and is walkable: the
shoulder cells beside the door. But they were drawn '.', and the corridor is
lane and door glyphs only, so the BFS refused over ground a walker could
stand on. Four cells retyped '.' to '~', two per door row, and the funnel
connects in all four rotations.

Measured over the chord sweep (`Tools/sim_chord_anchor.py`, 610 chords,
26,790 placements): 801 placed / 1,029 refused before, 1,671 / 159 after;
the plan leaves the tightest-three list on every floor (38, 45 and 49 per
cent of floor 2, 3 and 4 chords before, above 90 after); worst gate mouth
unchanged at 60.5 degrees, doublebacks zero, centreline cells on masonry
zero, GREEN. The audit's five DECORATIVE LANE warnings are gone; the four
that remain are the deliberate inert-door sidlers (The Stilled Well, The
Bound Stone, The Ten Thousand Quiet, The Counting Floor), recorded as such
in the tag audit entry.

Two riders travelled with this script. The holy sub-quota entry's
unregistered-plans paragraph stopped naming The Pilgrims Way, which the tag
audit deleted -- the older paragraph was contradicting the newer entry. And
`_SYMBOLS.txt`'s heart paragraph stopped warning against centred hearts on
road-anchored plans: the carriageway subtraction that rule guarded against
was removed by stage 2, and the validator gate enforcing it is retired --
the WardChapel plans' centred hearts are the correct shape now, not a fault.

**Key files:** five plans under `ScriptableObjects/Sites/Plans/`,
`_SYMBOLS.txt`, this document.

### The last two glyphs: keep-clear and decor (SHIPPED)

The symbols expansion closes. Two glyphs join `# . X = ^ + ~`, both floor plus
a marker like every marker before them, and both one-way compatible by the
grid's own rule: an older parser ignores an unknown glyph the same way it
ignores space, so a plan drawing them loads on an older build with those cells
read as rock. Acceptable, and now said.

**`'-'` is KEEP-CLEAR: floor the plan wants left empty** -- the cell before a
door, a sightline down a nave, an altar approach. Parsed into
`AuthoredSitePlan.keepClear`, coloured in the preview, listed in
`_SYMBOLS.txt`, known to the tag audit -- and consumed by NOTHING yet, which
is recorded rather than hidden. The intended first consumer is paired
placement's clearance test in the site relations arc, which is why the
vocabulary ships ahead of the machinery: the alternative was coming back for
a one-glyph parser change in the middle of a placement feature.

**`'o'` is DECOR: where the plan's decor piece stands, marked in plan space so
it rotates with the plan.** This changes the decor model from one prefab at
the site anchor to INSTANCES at marked cells, and lifts the constraint the
prefab hook imposed: a prefab spawns unrotated at the anchor, so every
prefab-decorated plan had to be `@rotate: no`; glyph cells ride
`EmitTransformed` through the SAME transform as the masonry, so the positions
rotate with the building and the plan may turn freely. The piece transform
itself stays unrotated on purpose -- props are authored front-view per the
art spec, and a quarter-turned sprite reads wrong.

**The cells are SAVED, not re-derived, and the save shape is why.** `SiteData`
keeps no rotation and no mirror -- its cell lists are world cells written at
placement -- so the plan asset alone cannot say where a rotated plan's `'o'`
cells landed. `SiteData.decorCells` is an appended field written by every
placement path (the fill loop and all three guarantees), disc-clamped by
`EmitTransformed` and core-subtracted alongside the cells it decorates; old
saves load it empty and simply spawn no pieces. The handoff document asserted
the opposite ("no save-shape change should be needed") and reading the spawn
path corrected it -- which also relocated that path: decor spawns in
`TerrainFeatureGenerator.SpawnSiteDecor`, not FeatureRevealController.

**`SiteDecorEntry` carries both hooks, independently.** `prefab` spawns once
at the anchor exactly as before and keeps its rotate ban; `piecePrefab`
spawns at every `decorCells` cell and takes no ban. The validator scopes the
rotate rule to anchor-prefab entries and FAILS a `piecePrefab` naming a plan
with no `'o'` cells, because a piece that can never spawn is wiring drift
wearing a completed look. The three `@rotate: no` vault headers are
UNTOUCHED: their cited decor prefabs do not exist yet, there is nothing to
convert, and `siteDecor` remains empty -- the runtime path is wired and inert
until art lands.

Both sims that read plan grids (`sim_chord_anchor.py`, `sim_lane_routing.py`)
learned the glyphs as floor in the same script, so their geometry cannot
drift from the parser's; the tag audit's glyph set gained both characters.

**Key files:** `AncientSitePlanLibrary.cs`, `AncientSiteBuilder.cs`,
`TerrainFeatureGenerator.cs`, `FloorFeatureSaveData.cs`,
`AncientSiteProfile.cs`, `Editor/SitePlanValidator.cs`,
`Editor/SitePlanPreviewWindow.cs`, `Tools/audit_plan_tags.py`,
`Tools/sim_chord_anchor.py`, `Tools/sim_lane_routing.py`, `_SYMBOLS.txt`,
this document.

### Site relations: excludes, near, and pairs (SHIPPED)

Authored plans can now say how they stand toward other sites. Six headers, all
naming an ARCHETYPE rather than a plan -- "a ward chapel near a sealed crypt"
is archetype talk, and a plan rename must not silently break a relation:

- **`@excludes:` is hard and SYMMETRIC.** The first side to place strips the
  other's plans from BOTH pools (the `generalPool` RemoveAll precedent), and a
  sweep after the guarantees strips anything banned by an archetype they
  placed -- so the guarantee side never needs headers of its own. Stripping
  beats refusing at attempt time: a banned plan left in the pool burns
  attempts it can never win.
- **`@prefers_near:` is soft, `@requires_near:` is hard, and the hardness is
  in the NAME** -- no flag, the `@anchor_required` lesson applied at the
  vocabulary level. Both bias the FREE pick by sampling AROUND a placed
  target rather than filtering a uniform pick, because the filter form
  rejects most of the band and starves the budget. On a road-anchored plan
  the bias cannot engage, so requires_near degrades to a post-filter and the
  tag audit warns about the combination. A requires_near refusal has its own
  per-pass counter -- it is the feature working, not a spacing failure, and
  it is not dressed as one. Guarantees count as targets.
- **`@pair:` actively places a partner in the same attempt.** The partner
  must be ON OFFER in the floor's pools -- own pool first, then the other,
  crossing the holy/general boundary by design (chapel near crypt crosses
  it) -- and a relation never summons a plan from outside them. The partner
  rides `extraPlaced` whichever pool it came from: authored intent, like a
  guarantee, never displacing a rolled ruin. On success it leaves its pool so
  the cursor cannot serve it again before the wrap. Failure leaves the
  primary standing, on a named counter: at the measured seat rate the unwound
  case is one to three per two thousand seeds, which does not buy unwind
  machinery.

**The numbers are measured, not chosen** (`Tools/sim_site_relations.py`,
built ON sim_holy_quota's band/fill model by import, 2000 seeds per floor).
`@pair_gap:` defaults to 24 cells: at 16 the partners' own footprints collide
before spacing is ever tested and seating falls to 8-17 per cent; 24 seats
98-100; 32 buys nothing. `@near_radius:` defaults to 1.5x the floor's
minSpacing: nearest-neighbour distances under TooClose START at minSpacing,
so any radius below it is unsatisfiable by construction, and 1.25x already
dips under 98 per cent.

**The pair exemption is exactly one anchor wide.** A partner's seat is exempt
from TooClose against its primary ALONE, by value; every other placed anchor
still holds the floor's spacing, because a partner standing pairGap from its
primary can stand (minSpacing - pairGap) from a third site. In exchange the
seat takes two tests anchor spacing no longer covers inside a pair: footprint
disjointness against the primary, and the KEEP-CLEAR test -- the `'-'`
glyph's first consumer, exactly the consumer its entry promised. Every
placement path now emits `keepClearCells` in world space on the placed site;
the field is TRANSIENT and never reaches SiteData, because relations resolve
at placement time and no relation adds a save field.

**Existing worlds are seed-identical.** Every new rng draw sits behind a
header test, and for a plan without relation headers the TryPickAnchor call
is byte-for-byte the one that shipped. The sim proved the packing whole:
holy and general minimums at 0.00 per cent shortfall with relations active,
zero spacing leaks, zero footprint overlaps, zero excludes co-placements.
`Summary()` reports the relation counters by name.

**Deferred, on purpose:** same-chord opposite-side pairing (toll house facing
gatehouse across one road). `SplitChordsForSites` threads one site per chord
by its `done` set, and pairing across that is real machinery; v1 proximity is
radial, which on AlongRoad plans lands pairs on the same or nearby chords
often enough to read. Revisit when a pair actually wants the facing shot.

**Key files:** `AncientSitePlanLibrary.cs`, `AncientSiteBuilder.cs`,
`Tools/audit_plan_tags.py`, `Tools/sim_site_relations.py`, this document.

## 21. The Buried Age

Approved: the deep-faith's civilisation was entombed in a cataclysm. Ancient
sites are ruins of the faith that venerated cores -- welcoming, with no
desecration penalty -- whereas Holy Ground is a Church seal, hostile ground.
Two flavours of sacred underground, one axis of history: deeper is older.

---

# PART IV -- LATER SHIPPED SYSTEMS

## 22. Deeds (Diegetic Achievement Layer)

Status: SHIPPED (engine + journal tab). Verified: 2026-07-12.

The chronicle of what the core has done -- the genre-standard achievement
layer worn as in-fiction record-keeping, reversing the Day 34 rejection of a
Steam-style meta layer (Brad un-binned it 2026-07-11). Wisp-voiced, surfaced
as a DEEDS tab in the existing journal (`QuestLogUI`, fourth tab beside
Active / Completed / Notes -- no new panel, Esc already routes through
`CloseJournal`). Chronicle only: no mechanical reward at this layer. Earned
deeds gaining teeth is the Trophy Hall's job (the planned follow-on: earned
deeds unlock mountable trophy furniture with small stacking effects).

Data: `DeedDefinition` ScriptableObjects (save key `deed.` + id, immutable
after ship) in a `DeedRegistry`, two flavours. COUNTER deeds watch one run
metric (a `Metric` enum over RunStats -- kills, losses, wild slain, biggest
party, gold, days -- plus `UnlockState` prefix counts for `tech.` /
`pattern.` and distinct valid rooms across floors) against a threshold;
they sweep once a second and also nudge on `UnlockState.OnChanged` and
`RoomAnchor.OnRoomValidationChanged`. MOMENT deeds fire from
`DeedsController.NotifyMoment(id)` calls at their event site; two ship live
(`first_raise` in the crypt raise path, `first_buried` in the buried-remains
grant). Moments are never retroactive.

Toasts reuse `AlertCategory.Discovery` and fire only for deeds crossed live.
First load of an older save reconciles in silence -- `RestoreSave` marks
already-satisfied counters with no announcement, because history is not an
event to announce. Earned deeds persist as `DungeonSaveData.earnedDeeds`
(key + day, additive). Hidden deeds read "???" until earned.

Starter roster: ~16 deeds spanning every counted system (kill tiers, losses,
wild slain, days survived, gold, party size, research and pattern counts,
room variety) plus the two moments. Key files:
`Gameplay/DeedDefinition.cs`, `Gameplay/DeedRegistry.cs`,
`Gameplay/DeedsController.cs`, `UI/QuestLogUI.cs`.


## 23. Trophy Hall (Deeds With Teeth)

Status: SHIPPED. Verified: 2026-07-12.

Where earned deeds reach into play (canon 22 gave the chronicle; this gives
it teeth). A `TrophyDefinition : FurnitureDefinition` is placeable furniture
gated by an earned deed -- it appears in the build picker only once
`requiredDeedKey` is earned (`FurnitureSelectionUI` rebuilds a filtered
visible list on open, asking `DeedsController.IsEarnedByKey`). Trophies may
be placed anywhere, but contribute their effect ONLY while their cell lies
in a VALID Trophy Hall: the census gathers Trophy Hall tile sets (the
`TrophyHousing` marker, RoomEffectType ordinal 13) and, in a furniture pass,
tests each trophy cell against them -- the same containment idiom as the
respawn chambers. A trophy's contribution blinks off when its Hall breaks
and returns when it re-validates (census re-aggregates on
`OnRoomValidationChanged`); the object persists throughout.

Effects (`TrophyEffectType`, four): MonsterDamage (additive fraction to a
global monster attack multiplier), ManaRegen (mana/sec into the core's
regen), TrapDamage (additive fraction to the global trap multiplier),
Notoriety (a slow upward trickle). Same-type trophies sum, each under a
census-serialized ceiling (defaults: damage +25%, mana +2/s, notoriety
0.5/s). MonsterDamage rides a dedicated `globalDamageMultiplier` on
DungeonMonster -- separate from the Throne room's per-tick
`roomDamageMultiplier` so they never collide, combined multiplicatively at
the strike, pushed to all live monsters on census change and adopted by
new monsters in Start. Trap and mana fold into the existing census
accumulators; notoriety is a new static the core reads each frame
(`AccrueTrophyNotoriety`, before decay). No new save schema -- trophies are
furniture, already persisted.

Room: Trophy Hall, min 9 tiles, maxTier 1, `TrophyHousing` marker, gated by
research node `hall_of_trophies` ("Hall of Trophies", Architecture tier 2,
prereq Deeper Lairs, pattern Wrought Iron). Starter roster: six trophies,
each tied to a shipped milestone deed (sprites author-assigned).

BUG FIX riding this guide: `DungeonCore.RegenerateMana` computed its own
base rate and ignored `CurrentManaRegen`, so the census mana sum (room
ManaRegen from Room Effects v2, and now trophies) fed the readout but not
the actual tick. Now regenerates at the full rate.

Key files: `Room/TrophyDefinition.cs`, `Room/TrophyEffectType.cs`,
`Room/RoomEffectCensus.cs`, `Monster/DungeonMonster.cs`,
`DungeonCore/DungeonCore.cs`, `Traps/FurnitureSelectionUI.cs`.


## 24. Surface World: Radial Forest Bands

Status: SHIPPED (Phase 8 substrate, radial rework). Verified: 2026-07-16.

The dungeon's surface footprint is ONE radial world inside `Dungeon_Level_0`:
concentric forest bands ring the floor-0 bedrock rim. There is no separate
surface scene, no scene load, and no hand-built surface content -- the old
apron is simply band 0 of the forest.

**Bands and research:** band 0 (60 cells deep beyond the rim) is always on
and reproduces the old apron's look. Bands 1-3 reach total depths 100 / 180 /
260 and are gated by the authored scout chain `tech.scout_1/2/3` (Sight
Beyond the Threshold, Eyes on the Deep Wood, The Far Marches -- entrance-
discovery visibility, chained prerequisites). Research IS the cost of sight:
there is no per-second scouting mana and no scout trip.

**Sight creep:** a newly researched band paints in full the moment its key
unlocks; the camera bounds then creep outward to the new edge over roughly
`creepDays` day-night cycles (default 1), on scaled time -- pause halts the
spread, speed-up hastens it. The creep is monotonic (a chained unlock moves
the target further out) and unsaved (loading lands at full researched depth).
`DungeonBoundsUpdater` sets the floor-0 bound to the revealed disc and
exposes `MarkDirty()` for the generator (Appendix C). An edge-fog ring
(generator-painted tilemap above the props: alpha eased quadratically
across the last `fogFadeCells` (16) of painted ground, full solid landing
two cells past the edge, then holding for
`fogSolidMarginCells` (60) past it) hides the unpainted void at every
band edge and keeps the world's outermost rim misty forever.

**Determinism:** per-cell ground and scatter use a position hash of
(cell, seed), so unlocking a band never reshuffles ground that already
exists. Camps, trails, and nodes use per-purpose, per-band salted streams.
The seed derives from the entrance mouth and bearing (the apron idiom);
`RunContext` is retired. Nothing on the surface is saved.

**Road and trails:** the pilgrim road continues the seeded cave bearing
through every band (half-width 2, clearance 3, live values). Satellite camps
get wobble-walk footpath trails (`trailTile`, falling back to `roadTile`;
1 cell wide with a 1-cell scatter-free shoulder) that join the nearest point
of the existing network -- the road or an earlier trail -- so paths branch
off paths organically. Trails sweep any props beneath them, so live unlocks
and fresh loads converge on the same world.

**Camps:** `camp.main` sits on the road in band 1 (depth 38); band 2 adds
two satellites, band 3 two more (`camp.sat.N`, globally numbered; growth,
tiers, and commerce buildout are entry 25). Minimum
35-cell and 60-degree bearing separation keeps camps far apart for future
inter-camp play -- no constant combat outside the dungeon.

**Nodes:** per-band counts on a fixed radial gradient; `dist01` normalises
against the FULL authored depth, so a type's meaning never stretches --
exotics live only in the deep ring and simply do not exist until band 3 is
researched. Node types (`SurfaceNodeType`, immutable `nodeKey`) and camp ids
are the only save-facing identities; future harvesting and camp formation
bind to them.

**City gate:** the deepest authored band carries the passage to
civilisation. When that band paints, the generator raises a gate at the
road's end (`CityGate`: a trigger into `City` at spawn `FromForestRoad`,
optional `gatePrefab` visual) plus a default return `SpawnPoint` `FromCity`
a few cells back down the road, outside the trigger so arrivals cannot
bounce straight back. The City scene hand-places the counterparts: a default
`FromForestRoad` spawn and a return trigger to `Dungeon_Level_0` at
`FromCity`. Return arrivals are completed by the generator itself --
`SpawnPointManager` runs on the first frame, before the gate can exist, so
expect its one benign warning per return trip. The wandering merchant's
future arrival stages through this gate.

**Player interaction:** pre-avatar the player only observes. Assault staging
stays inside the dungeon (canon 10); the live layer is SHIPPED as theatre.
`Overworld/SurfaceLifeController.cs` (floor 0, beside the generator) runs
road-approach walkers (2-3 sprite puppets in the First Blood idiom, spawned
inside the wave lead window and cleared by the spawner's `PartyRegistered`
choke-point event; commoner waves included; dispatches bypass the timer and
get no lead-in; night never plays approaches) and camp millers (day: 5-7 at
`camp.main`, 3-5 per satellite; night: a small watch of 2 / 1 drawn from
night-watch sprites, falling back to the day pool; markers re-scanned so
live band unlocks grow the population). Real parties all take the shared
muster window now (see entry 10). Nothing in the surface-life layer is
saved, seeded, or simulated.

**Retired and deleted:** the `Forest` scene (and its Build Settings entry),
`ForestZoneGenerator`, `ForestZoneProfile` (+ asset), `DevForestTravelButton`,
`ScoutHudButton`, `ScoutController`, `ScoutReturnApplier`, `ScoutTierProfile`
(+ asset), and `RunContext`. `GameScene.Forest` stays in the enum as a
commented orphan: the enum int-serialises in scene triggers, and removing a
middle entry re-targets every hand-placed door after it.

### The rim facade (SHIPPED)

The disc's edge used to be a colour change: forest grass on one side of a
circle, void-toned fog on the other, with nothing drawn between them. Floor 0's
rim now carries a WALL, so the dungeon reads as a built edge from the treeline
before anything has been dug.

**It is the existing wall pipeline, pointed at the rim.** `DungeonTerrain`
exposes `RimRingCells` -- every in-disc cell with a cardinal neighbour outside
the radius, computed once and cached, since the radius is set at floor creation
and never grows. `GenerateAt` unfogs that ring on floor 0, which is the one
place the disc's fog is painted and therefore covers the fresh and the load path
together. `CaveWallRenderer` adds the ring to its wall set on floor 0, and caps,
faces, the drape, `CaveWallFade` and `DungeonShadow` all apply with no special
case.

The facade could NOT be drawn outside the disc, and the reason is worth keeping.
`CaveWallClassifier.IsSolid` treats every out-of-disc cell as open air, and
`TileInfluenceManager.IsUnderOverhang` deliberately mirrors it so walkability and
visuals agree at the breach. Moving the facade outward means editing that pair,
which reaches mining, pathing and the breach at once, to buy a ring of decoration.

Three things then fall out free rather than being built:
- Bedrock is never claimable and so never mined: the facade cannot be breached.
- The entrance channel's cells are open, so the ring BREAKS at the mouth. The
  gate is a hole, not a prop.
- Rivers start on the rim and `IsSolid` exempts river cells, so each river
  mouth notches the cliff from the first frame. Accepted deliberately: it gives
  away a mouth, not a course, and a river mouth in a cliff is what the geometry
  actually is.

Measured at floor 0's radius of 100: 564 ring cells, of which 201 are
south-facing and draw a two-slice face over the grass. The northern arc takes
caps only, which is correct for the projection -- you see the top of a wall from
behind it.

**Floor 0 only, and the gate is in two places.** The ring itself is added to the
wall set only on floor index 0; lower floors have nothing outside their rim, and
a lit ring in blackness reads as a fault. Separately, `WallFamily` gained
`restrictToFloors` plus a floor list, applied ONCE per family per floor inside
`BuildFamilyTiles` -- each floor owns its renderer, so the bake already knows
which floor it is for, and the gate costs nothing per cell. Without it a bedrock
skin would retexture the rim on floors 1-4 anywhere the player had dug out to
it, which they can and do. The toggle is explicit rather than an empty-list
convention, per the standing rule; Validate Layout flags the toggle-on-with-no-
floors state, which renders nowhere. One consequence recorded because it will
bite something else later: site paving resolves through the same baked table, so
restricting a family that carries paving slots also drops its paving on every
excluded floor.

**Softening the step is done on the GRASS side, never on the void side.** The
traverse is bright forest, then wall, then dark interior, and the wall is
already two to three cells of mid-tone. What was missing was anything between
grass and wall. `SurfaceZoneGenerator.PaintInnerGloom` darkens the ground toward
the rim across `rimGloomCells`, easing by `rimGloomFalloff` from
`rimGloomMaxAlpha` at the wall to nothing outward. It paints on the SURFACE fog
tilemap because of where the scene already puts it -- Player, order 100 -- so
the gloom lands over the grass and over the wall's draped face, giving a contact
shadow at its foot, but UNDER the caps on WalkBehind and under the dungeon's fog
on Shadow. Nothing was restacked. Its colour is a separate field from the edge
fog's on purpose: that one is a pale mist hiding the void past the last band,
this one is the pit's shadow.

Ramping the dungeon fog inward was rejected on two counts. `DungeonShadow`'s
`fogMatchesVoid` sets that tilemap's colour layer-wide, so a per-cell colour
could only multiply it darker; and softening it by ALPHA would show what sits
beneath the fog near the rim, where floor 0's rivers start and a site can band
close. That is a layout leak, which the river notch is not.

**The bald ring mattered more than the brightness.** `treeFreeInnerBand` (4)
skipped ALL scatter, leaving a perfect circle of empty grass hugging the wall,
and a perfect circle is what reads as machine-made. `screeInnerBand` (3) opens
that gap to rocks alone, at a flat `screeDensity` rather than the band's
inner-to-outer lerp, because a cliff foot does not thin with distance. It starts
at 3 so nothing spawns under the two-cell drape. The rubble ring uses its own
hash salt and touches only cells that were previously skipped outright, so
enabling it cannot move a tree that already stood.

`Log Rim Facade` on the Commands object reports band and outer cells, how many
are capped, how many are notched, how many drape a face, how many are still
fogged (want zero) and how many nubs were demoted (want four). Diagnostics
first: the failure modes here are all visual, and this turns them into numbers.

**The first build shipped four defects, three of them one bug.** Recorded
because the cause was geometric rather than a slip.

A one-cell ring is the wrong footprint. On a rasterised circle the widest row
sticks out a cell past the row behind it, so each of the four cardinals grew a
one-cell nub; and the cell directly behind a nub has all four cardinal
neighbours in-disc, so it never met the ring's test, never unfogged, and read as
a black square punched through the wall. The facade is now a BAND
(`rimFacadeDepth`, 3) built by breadth-first walk inward, which covers the black
squares, and the four protrusions -- in-disc cells with at most one in-disc
cardinal neighbour -- are DEMOTED out of the wall. `CaveWallClassifier.IsSolid`
exempts them, which is the load-bearing half: left solid but uncapped, the run
behind them keeps its S bit set and loses its face drape, trading a rock nub for
a one-column gap in the wall front. The walk treats a nub as outside, so that
run becomes layer 0 and takes the drape.

The band clamps per cell to the bedrock ring, because the wall family only skins
Bedrock and a band cell past it renders grey stone mid-cliff. That clamp forced
the arming out of `GenerateAt`: `IsBedrock` answers false until
`TerrainTypeMap.GenerateNew` runs, which is after terrain generation on both
paths. `SurfaceZoneGenerator.TryArm` calls `ArmRimFacade` instead -- floor 0
only by construction, already polling for generation, idempotent -- so one call
site replaces the two that would otherwise have to be kept in step. Floor 0's
`minRingThickness` went 3 -> 4 for margin on top of the clamp.

**The outer ring's GROUND belongs to the surface.** An outer corner cap does not
fill its cell, and the part it leaves uncovered is ground beyond the wall -- it
was showing dungeon floor. `ArmRimFacade` clears the floor tile under the outer
ring and the nubs, and `SurfaceZoneGenerator.PaintRimSurfaceGround` paints grass
and full-strength gloom there. `ClaimedStoneLayer` turned out to be wired to
nothing -- its Tilemap is referenced only by its own GameObject, like
`ClaimableLayer` before it -- so `FloorLayer` was the only competitor and no
sorting change was needed. Inner layers keep their floor tile: they sit under
solid interior caps, and grass under a deep cap would show green where rock
should be. River and road cells on the ring are skipped; grass over open water
would be worse than what it replaced.

**The bright-rim-to-black-void step is closed by the light map, not by paving.**
`DungeonShadow` had already solved this exact shape for the prepared road band
(step 1b): an unmined band nothing entered into `baseLight`, rendering brighter
than the revealed ground beside it. Step 1c does the same for the facade.
Paving the inner band was considered and dropped -- it needs the inner cells
excluded from the wall set or their caps draw over it, and the light map already
owns this problem.

That ramp took two goes to get right, and the reason is worth keeping because it
will catch anything else added to `baseLight`. There are two paint paths:
`voidCells` are painted OPAQUE as `voidBaseColor * light`, a fixed rock tone;
everything else is ALPHA-DARKENED, so it renders as its own art multiplied down.
A facade row and a void cell at the SAME light therefore do not match unless the
art happens to equal `voidBaseColor`. The innermost row is registered in
`voidCells` so the meeting point is the same function of the same number by
construction, and the art rows ramp to `rimFacadeInnerLight` above it.

`rimFacadeInnerLight` ships at 0.10, a little UNDER the derived match, so the
wall's inner edge reads as a shadowed lip rather than dissolving into the void.
Undershooting is the forgiving direction; overshooting bright is what produced
the cliff described below. The derived number is an equation, not a taste:
art x light must equal `voidBaseColor * voidLightFloor`. The first attempt set 0.5 on the
reasoning that cliff art is DARKER than `voidBaseColor` and so had to stop short
of the floor. That is backwards for this art -- the cliff top is grass, measured
at 98 luma against the void's 15 -- so the correct value is about 0.15, and
brighter art needs a LOWER number here rather than a higher one. Combined with a
span that divided by the void row as though it were a ramp step, so the inner
light was never reached by anything, the shipped ramp ran 98, 82, 65, then 15:
a fifty-point drop across one cell that no value on the GRASS side could have
touched, because the discontinuity was entirely on the void side.

The bedrock clamp came off at the same time. It existed so a band cell could not
spill past the ring and render ordinary stone mid-cliff, but it was redundant --
the wall family resolves by terrain, so a non-bedrock cell falls back to the
stone path unaided -- and it capped the band at the ring's 4-6 cells when the
ramp wants more rows than that.

The ramp is a CURVE, `rimFacadeFalloff` (2), not a straight lerp: linear spent
most of its rows in the bright half and left the last row before the void
reading brighter than it should. At depth 6 the art rows run, in luma against
the void's 15, 98/77/56/36/15 at falloff 1 and 98/62/36/20/15 at 2. The exponent
moves only the shape -- both ends are pinned, the outermost row on
`rimFacadeLight` and the last art row on `rimFacadeInnerLight` -- so a darker
final row than the void means lowering the inner light and accepting a seam,
not raising the falloff.

**The ramp applies to RIM TILES only.** `rimLayers` is built from geometry
alone, so it also holds the river cells where a river crosses the rim. Two
passes were needed to get this right: the first painted them opaque and punched
a black block through open water at the river mouth, the second stopped the
opaque fill but left them in the ramp, which dragged a heavy gradient across the
water instead. They are now skipped outright, so water at the rim renders as it
does anywhere else.

**The facade reveal is rock-only too.** `rimLayers` is geometry alone, so
`ArmRimFacade` was unfogging the river cells where a river crosses the rim -- and
an UNDISCOVERED river has no water tile yet, only the dungeon floor tile from
`PaintTerrain`. The result was a slab of bare dungeon floor sitting out in the
forest, which also gave away the river's course before it had been found. Rock
only: undiscovered water stays fogged and reads as void, and the ordinary river
reveal takes over on discovery. That is now the rule in three places -- reveal,
ramp and surface ground -- and the wall set gets it free from `IsSolid`.

**A postscript on diagnosis.** Most of the visible ugliness at this edge turned
out to be a wrong interior-fill slot in `CaveWallSheetLayout`, not the light
ramp -- and the ramp was genuinely broken as well, so fixing either alone
changed little and the two masked each other for several rounds. Where a
rendering fault has both a DATA and a CODE candidate, check the data slot first:
it is the one that can be confirmed in the Inspector in seconds, against a test
cycle that costs a full build.

**The forest floor carries into the mouth.** The channel became dungeon ground
the instant it crossed the disc boundary, so the entrance read as a doorway
rather than as a hole the forest runs into.
`SurfaceZoneGenerator.PaintMouthGrass` extends grass `mouthGrassCells` (4) in
from `EntranceCaveData.mouthCell`, feathered across the last
`mouthGrassFeather` (2) by the same stable hash the scatter uses, so the ragged
edge survives a reload with nothing serialised.

Only MINED cells take it, and that single test does all the shaping: the channel
is the mined part, so the grass follows it in and the rock flanking it is
untouched with no geometry to get wrong. Lighting is deliberately left alone --
those cells are in `DungeonShadow`'s light map, so the grass darkens going in,
which is what a cave mouth should do and what makes the feather and the falloff
reinforce each other rather than fight.

Two facts made this fifteen lines rather than an arc, and both are worth keeping
because they bound what else is cheap here. Floor 0 has exactly ONE ground
tilemap, `FloorLayer` on `DungeonTerrain`: `MinedFloorLayer` and
`ClaimedStoneLayer` are referenced by nothing but their own GameObjects, dead in
the same way `ClaimableLayer` is. And `floorTilemap` is painted once, by
`PaintTerrain`'s `SetTilesBlock`; mining never repaints it, so a cell cleared at
arm time stays cleared however much the player digs beside it later, and the
load path repaints the disc before this runs again.

A second defect from the same arc: `ArmRimFacade` cleared the dungeon floor tile
on every outer-ring cell, while the surface pass declined to paint grass over
river, road and mined cells. Two predicates for one decision, and they
disagreed: a road cell on the ring lost its floor and gained nothing, so the
shadow overlay rendered over an empty cell as a flat untextured block, right
where the forest road meets the entrance. The clear now sits inside
`PaintRimGround` past every skip, so one predicate governs both.

**Terrain alone cannot gate a masonry skin.** The cells flanking the carved
entrance channel are bedrock too, and they are solid and touch mined floor, so
they land in the ORDINARY wall set and wore the cliff skin -- grass-topped
chunks floating inside the dungeon. `WallFamily.rimFacadeOnly` gates the family
per CELL on facade membership, so bedrock rendering as an interior wall falls
back to the stone path and the entrance channel is lined with cave walls at no
extra cost. Generalisable: any skin that belongs to a place rather than to a
material wants this rather than a new `TerrainType`.

**Three canon corrections rode this arc**, all found by reading the shipped
assets rather than the prose: band depths are 60 / 100 / 180 / 260, not
32 / 45 / 70 / 100; `fogFadeCells` is 16, not 12; `fogSolidMarginCells` is 60,
not 24.

**Key files:** `Floors/SurfaceZoneGenerator.cs`, `Floors/SurfaceZoneProfile.cs`
(+ `SurfaceBand`, `SurfaceNodeType`), `Overworld/CampZoneMarker.cs`,
`Overworld/ResourceNodeStub.cs`; touches `DungeonCore/DungeonBoundsUpdater.cs`
(surface AABB union + `MarkDirty`), `Save/DungeonSaveController.cs`
(`RunContext` publish removed), `TESTING/Commands.cs` (scout toggles),
`Overworld/SceneTransitionTrigger.cs` and `Save/SpawnPoint.cs` (runtime
`Configure` initialisers for the gate). The rim facade adds
`DungeonCore/DungeonTerrain.cs` (`RimRingCells`, `RevealRimRing`),
`DungeonCore/CaveWallRenderer.cs` (the floor-0 ring and the per-floor family
gate), `DungeonCore/CaveWallSheetLayout.cs` (`restrictToFloors`),
`Editor/CaveWallSheetLayoutEditor.cs` and `TESTING/Commands.cs`
(`Log Rim Facade`).
Supersedes `Floors/SurfaceApronGenerator.cs` (deleted; band 0 replaces it --
same parameters, freshly hashed layout, acceptable in alpha).

**Rejected:** a separate Forest scene reached by a tunnel scene-load (one
radial world instead); hand-built surface content; serializing the layout or
the creep progress; per-second scout mana (research is the cost); staged
tile painting during the creep (the confiner hides unrevealed ground, so
paint instantly and creep only the bounds); removing `GameScene.Forest`
(the int-shift trap).


## 25. Camp Growth, Identity & Effects

Status: SHIPPED (Phase 8). Verified: 2026-07-17.

Survivors settle the surface. Every adventurer who leaves the dungeon alive
(commoner-stage escapees included) adds one growth to a receiving camp:
`camp.main` until its cap (30), then satellites in unlock order (cap 20
each). One event carries all of it -- `AdventurerParty.MemberEscaped`
(party + member), raised from `OnMemberResolved`, the single resolution
point every exit path shares. Growth is flat (+1 per escape, fled and
satisfied alike) and accrues to zone ids even before their band is
researched: the guild was already gathering, so a late-researched camp can
reveal mid-tier.

**Tiers** are an open-ended authored list on the surface profile
(`campTiers`): Waystation (0 -- a lone cart that got wind of a new
dungeon), Camp (8 -- tents and a market stall), Settlement (20 -- a shop,
more tents, palisade pieces). A future Town row (tavern, houses lining the
road) is one new row plus art -- zero code. Each tier defines a **commerce
anchor** (cart -> stall -> shop), placed facing the way home and doubling
as the wandering merchant's eventual dock, plus a prop table. Miller
counts from entry 24 scale by the tier's `millerMultiplier` (0.5 / 1 /
1.5).

**Framing:** when growth reaches `framingFraction` (0.7) of the NEXT
tier's threshold, that tier's construction-site look appears --
`framingProps[i]` renders at the exact positions `props[i]` will take
(per-prop position hashing: hash of zone/tier/row/index, so foundation and
finished building coincide by construction). The commerce framing rises
BESIDE the current anchor and the finished piece takes the anchor spot on
tier-up (documented exception to framing-in-place). Framing recedes if
decay pulls growth back under the fraction.

**Identity:** at tier 1+ a camp declares the majority faction of its
recorded settler tallies (`FactionSystem.FactionForKill` per member; ties
break to the Guild; waystations stay neutral). Sticky -- re-evaluated only
on tier-up; the banner comes down if decay drops the camp to tier 0. Wisp
announces declarations.

**Effects** -- queried by the sim, tier-scaled, summed across camps, all
numbers profile-serialized:
Guild camps shave `guildIntervalSecondsPerTier` (2 s) x tier off the wave
interval, floored at `guildIntervalFloorFraction` (0.6) of the base
(`AdventurerSpawner.CurrentInterval`). Cultist camps dampen notoriety
decay x(1 - 0.15 x tier) per camp, factors multiplying, clamped at 0.4
(`DungeonCore.DecayNotoriety`). Holy Order camps tax mana regen 4% x tier,
capped at 20% total (`DungeonCore.CurrentManaRegen`; the readout is poked
via `NotifyManaRegenDisplay` on camp changes). Mercenary camps declare but
exert no pressure yet.

**Decay:** on each dawn (`DayNightCycle.OnDayStarted` /
`CurrentDay`), a camp with no settlers for `decayGraceDays` (3) bleeds
`decayPerDay` (1); tiers drop, buildout and framing recede, floor at zero.

**Persistence:** one additive save block, `campGrowth` on
`DungeonSaveData` (`{zoneId, growth, factionTallies, declaredFaction,
lastSettleDay}`), captured and restored beside the tracked-party registry.
Field initialisers double as old-save defaults (-1 neutral / 0 unknown
day). Buildout is never saved -- it rebuilds from ledger + tier tables,
silently after a load.

**Key files:** `Overworld/CampGrowthController.cs`; touches
`Adventurer/AdventurerParty.cs` (`MemberEscaped`),
`Adventurer/AdventurerSpawner.cs` (`CurrentInterval` camp pressure),
`DungeonCore/DungeonCore.cs` (`CurrentManaRegen` tax, `DecayNotoriety`
dampening), `Floors/SurfaceZoneProfile.cs` (tier defs, framing fields,
effect/decay knobs), `Floors/SurfaceZoneGenerator.cs` (`Profile`
accessor), `Overworld/SurfaceLifeController.cs` (tier-scaled millers),
`Save/DungeonSaveData.cs` + `Save/DungeonSaveController.cs` (additive
`campGrowth` block).

**Rejected:** weighting satisfied vs fled escapees (flat +1 is readable);
saving buildout layouts (ledger + hash suffices); per-survivor pathing to
camps (escape is a resolution event, not a walk); building camp props into
the band generator (tiers change live; generation paints once); daily
faction-standing drift from camps (skipped this pass); Mercenary camp
pressure (deferred until a mechanic earns it); Holy reputation-drift into
the escalation system (effects-v2 candidate -- the mana tax ships first);
commerce framing-in-place (the current anchor occupies the spot; beside-
then-replace is the accepted exception).

---

## 26. The Surface War

Status: SHIPPED (Phase 8). Verified: 2026-07-18.

Camps interact at dawn, after decay -- event-shaped, never an always-on
skirmish (camp separation throttles frequency; range gates contact). Only
declared camps with live markers participate; distances compare in cells
(`interactionRange`, 75). Cross-faction stances are profile data
(`factionStances`: Cultists vs Holy Order HOSTILE, Guild vs Cultists COLD;
unlisted pairs neutral; same faction = kindred).

**Raids:** at most ONE hostile event per dawn world-wide; per eligible
hostile pair, `hostileDawnChance` (0.35) gated by a 2-day per-pair
cooldown (transient, not saved). Strength = tier x 2 + growth / 10 +
roll(0-3); the loser bleeds `raidLoserGrowthLoss` (3), the winner
`raidWinnerGrowthLoss` (1) -- war costs both. A camp raided to tier 0 is
DISPLACED: banner down, growth zeroed, `ruinedFromTier` recorded
(additive save field), and the camp renders its tier's RUIN layer --
`ruinProps[i]` ruins `props[i]` at the identical hashed positions, the
commerce ruin taking the anchor spot. Natural decay leaves no scar (they
packed up); ruins are raid-only. The first new settler clears the bones
(recolonisation, possibly under a new banner). Puppet raiders cross
between the camps (`SurfaceLifeController.PlayCrossing`).

**Kindred flow:** same-faction pairs in range run caravans
(`caravanDawnChance` 0.25): one growth moves larger -> smaller, counts as
life for the recipient's decay clock, puppet porters walk it. A camp
decaying to zero with kindred in range migrates a remnant (+2, capped)
instead of evaporating.

**Suppression:** hostile neighbours in range suppress each other's effect
contributions -- `suppressionPerAttackerTier` (0.2) x attacker tier,
capped at 0.6 -- applied inside entry 25's three effect queries. The cold
war in numbers; no new systems.

**Landmarks:** a declared camp at the FINAL authored tier raises its
faction's centrepiece at the camp centre (the prop ring starts at 0.3r,
so the heart is free): guild hall / church / unholy temple
(`factionLandmarkPrefabs`, FactionId order; the Mercenary slot stays
empty). While the final tier frames (the 70% rule), a shared
`factionLandmarkFramingPrefab` scaffold stands in its place. Displacement
razes the landmark with the rest; no bespoke landmark ruin (the temple
burns first). A future Town row inherits landmark duty automatically.

**Key files:** `Overworld/CampGrowthController.cs` (dawn pass, ruins,
landmarks), `Floors/SurfaceZoneProfile.cs` (stances, war knobs, ruin and
landmark fields), `Overworld/SurfaceLifeController.cs` (`PlayCrossing`),
`Floors/SurfaceZoneGenerator.cs` (edge fog, entry 24). World retune to
32 / 100 / 180 / 260 with tapered densities is asset data.

**Rejected:** an always-on surface combat sim (event-shaped by design);
trail-network-only reach (seed-fragile); saving pair cooldowns (transient
double-raid after reload is acceptable); faction-specific scaffolds (one
shared timber frame reads for all three); a landmark ruin variant.

---

## 27. Bestiary Expansion (Level-Gated Roster, Affinity Reskins, Depth-Banded Wilds)

Status: SHIPPED. Verified: 2026-07 (landing date unrecorded; placeholder
resolved by the 2026-07-29 dev-plan audit).

**Roster:** 26 new designs (36 definitions) spanning Bronze 2 through Diamond 3,
1 per rank Bronze 2-10 and 1-2 per rank Silver+, gated by
`requiredTier`/`requiredRank` (the pre-wired flat-level gate). Level-ups deliver
monsters automatically -- the core remembering as it grows -- while research keeps
the specialist branch: Barrow Knight (`tech.barrow_oaths`, Bestiary tier 3, Earth
affinity, prereq Bones in Iron) and Deathpriest (`tech.litany_of_graves`, Bestiary
tier 4, Dark affinity, prereq Whisperer in Marrow; the only new active-necromancy
source, raising Skeleton/Zombie/Ghoul). Both nodes live in TechContentGenerator
(generator-authoritative). Category spread Beast 8 / Humanoid 8 / Undead 10 keeps
all three muster rooms relevant through Diamond.

**Affinity reskins:** `MonsterDefinition.affinityType` (DungeonType, None =
universal). A typed def is HIDDEN from the picker (skip-navigation +
Show()-snap in MonsterSelectionUI) and rejected at placement
(`AffinityMatches` gate in DungeonBuildController) when the core's type
differs -- the player only ever sees their own core's skin. Two six-skin
families ship: Adept (Silver 3; Cinder/Tide/Gale/Shale/Umbral/Radiant) and
Archon (Diamond 2; Pyre/Maelstrom/Tempest/Terra/Void/Dawn). Reskins are
COSMETIC ONLY by ruling -- one stat block per family. Skin names are save keys
(restore skips the affinity gate by design; a save reloads under its own core
type).

**Elemental flavour: fork CLOSED, need met elsewhere.** Per-skin mechanical
divergence was left open as a fork when this entry was written. It is now
closed and will not be built: the stat blocks stay identical within a family,
and elemental identity is carried by three systems that shipped after the
fork was raised.

- **Visibly**, by `MonsterDefinition.projectileTint` (entry 30). Every skin
  looses its own coloured bolt -- Cinder burnt orange, Tide cold blue, Void
  violet -- so the element reads at a glance in combat without touching a
  single number. Two skins of a family differ in exactly four authored
  fields: asset name, display name, `affinityType`, `projectileTint`.
- **Mechanically**, by the six type-locked traps (entry 31). The core's
  element expresses itself as the signature trap only that core may build --
  Fireball Rune, Ice Spikes, Earth Spikes, Gale Vent, Blinding Flash, Umbral
  Snare -- which is a stronger identity lever than a per-skin damage rider,
  because the player builds with it rather than merely fielding it.
- **In the roster**, by the sixty native affinity-line monsters (27A), which
  already give each type ten creatures of its own rather than a repaint.

Consequence for authoring: a new skin in an existing family is a name, an
`affinityType` and a tint. If a future design wants a burn rider or a slow on
a specific element, it belongs on a trap or a native line, not on a reskin --
reopening this fork would put a mechanical difference behind a gate the player
can never see across (they only ever meet their own core's skin), which is the
reason it was refused in the first place.

**Mage model:** ranged monsters follow the adventurer-Mage convention -- large
attackRange (Adept 3.6, Archon 4.2) plus telegraphSeconds. SUPERSEDED by
section 30: these definitions now loose travel-time projectiles; ranges and
telegraphs are unchanged.

**Depth-banded wilds:** `MonsterDefinition.minWildFloor` (default 0);
WildMonsterController filters the shared template pool per floor before the
seeded chamber roll (restore path untouched). First user: Cave Troll
(minWildFloor 1, requiresDiscovery, wildRegenMultiplier 1) -- the discovery
channel's Silver-band entry, placeable at Silver 4 after a wild kill.

**Key files:** `Monster/MonsterDefinition.cs`, `UI/MonsterSelectionUI.cs`,
`DungeonCore/DungeonBuildController.cs`, `Monster/WildMonsterController.cs`,
`Editor/TechContentGenerator.cs` (in Assets/Editor).

**Necromancer retrofit:** brought onto the mage model for convention
consistency (prefab: 40 HP, attack range 2.8; def: telegraph 0.7s); raise pool
deliberately unchanged (Skeleton/Zombie -- Ghoul stays the Deathpriest's tier-4
draw). The remaining original five are unchanged by ruling: research-channel
identity and the cap-efficiency floor are load-bearing.

**Rejected:** shown-but-locked display for wrong-type skins (breaks the reskin
illusion); per-floor wild pool assets (one shared pool + depth field is the
authored surface); a monster projectile system -- SUPERSEDED by section 30 (ranged
combat ships travel-time projectiles; the hitscan convention is retired); mechanical flavour on reskins (no longer deferred -- the fork is CLOSED, see
the elemental-flavour note above).

### 27A. Native Affinity Lines (60 Monsters, Slot Parity)

Status: SHIPPED. Verified: 2026-07 (landing date unrecorded; placeholder
resolved by the 2026-07-29 dev-plan audit).

Each core type owns ten unique native monsters -- distinct creatures, sprites
and copy per type, nothing shared but the numbers. Ten rank slots common to all
six types (B3 Swift Skirmisher/Beast, B5 Resilient Walker/Undead, B7 Line
Fighter/Humanoid, B9 Heavy Pouncer/Beast, S2 Sentinel/Undead, S4
Champion/Humanoid, S6 Apex Predator/Beast, G1 Bulwark/Undead, G3
Warcaster/Humanoid, D1 Terror/Beast). SLOT PARITY IS A RULING: one stat block
per slot, identical across types -- core choice is balance-neutral; identity
lives in names, sprites and descriptions only. Gating is pure tier/rank (no
research, no discovery); `affinityType` + the section-27 picker filter and
placement gate make the lines native. Categories fixed per slot (Beast 4 /
Undead 3 / Humanoid 3) so muster balance is identical per core; constructs ride
Undead per the Obsidian Sentinel precedent, and Light's dead are gilded, not
rotting. The G3 warcaster uses the mage model at range 3.8 (between Adept 3.6
and Archon 4.2). Slot niches are offset from the same-rank universal monster --
native lines complement the universal roster.

Rosters (slot order B3->D1):
Fire: Magma Hound, Ash Walker, Emberblade, Greater Salamander, Ashbound Warden,
Ifrit Duelist, Lava Drake, Slagheart Golem, Pyromancer, Cinderwyrm.
Water: Riptide Skimmer, Drowned One, Brine Reaver, Reef Lurker, Coralbound
Watcher, Depthsworn Blade, Abyssal Angler, Barnacle Hulk, Tidecaller, Leviathan.
Air: Gale Harrier, Windshade, Galeblade, Sky Lynx, Zephyr Sentry, Cyclone
Duelist, Thunder Wyvern, Windcarved Monolith, Stormcaller, Storm Roc.
Earth: Crystal Skitterer, Clay Shambler, Rootbound Warrior, Granite Boar, Menhir
Watcher, Crystalguard Champion, Burrowing Horror, Basalt Warden, Geomancer,
Terravore.
Dark: Shadow Prowler, Gloom Husk, Duskblade, Nightgaunt, Wraithshell,
Shadowdancer, Duskmaw Hunter, Umbral Mass, Voidspeaker, Void Maw.
Light: Sunhound, Lantern-Bearer, Sun Zealot, Radiant Gryphon, Reliquary
Guardian, Lightsworn Duelist, Blinding Raptor, Censer Golem, Lightbinder,
Dawnmaw.

**Rejected:** per-type stat identities (bounded elemental tilts considered and
declined -- balance-neutral ruling holds); ten archetypes with six skins each
(the reskin route; uniqueness was the ask); research or discovery gating on
native lines (the automatic channel stays automatic).

---

## 28. Boss Promotion (Rank-on-Spawner, Waiting Halls)

Status: SHIPPED. Verified: 2026-07-29.

Boss and sub-boss are PROMOTION RANKS on the spawner (PromotionRank
None/SubBoss/Boss), applied to any placed monster via two command-panel
buttons -- the per-monster BossVariantDefinition/SubBossVariantDefinition
assets are retired (files deleted, registry cleaned; both CLASSES remain for
the Hungry Bear wild event, the title screen filter, and legacy code paths).
Every regular, present and future, is boss-eligible with zero per-monster
authoring.

**Rule A -- waiting halls:** boss rank exists only inside a Boss Room. The
room's spawnCategories opened to all three categories; any spawner may be
placed into an otherwise-valid empty Boss Room (MusterRooms allows placement
with ignoreBossSpawner) where it sits respawn-paused until promotion.
requiresBossSpawner is satisfied by rank (RoomValidator keys off
MonsterSpawner.IsBossSpawner); promotion revalidates anchors so the hall flips
valid and its +15% respawn hastening starts the moment the tenant rises.
Sub-boss rank rises anywhere the spawner musters.

**Limits and costs:** 1 boss and 2 sub-bosses per floor (live census).
Promotion pays the mana and capacity DIFFERENCE between ranks; the spawner's
CapacityCost folds the bonus (demolish refunds the promoted total). No demote.
Applies to the living monster immediately BY RATIO (never double-stacks) and
heals to the new maximum. Veterancy stacks. Transients cannot rise; wilds and
risen are out of scope (spawner-only).

**Numbers:** one PromotionTemplate asset (assigned on DungeonBuildController)
-- boss x5 HP / x3 dmg / x5 XP / x4 cap / x4 mana / x1.5 scale, untinted;
sub-boss x2.5 / x2 / x2.5 / x2 / x2 / x1.25, dark tint -- seeded verbatim from
the retired variants, plus the boss epithet pool.

**Titles:** bosses roll a persisted epithet ("<name>, <epithet>"); custom name
overrides; sub-bosses are untitled by design (promotedTitle null below Boss).
Boss deaths route through a new title-string NotifyBossDeath overload.

**Key files:** Monster/PromotionTemplate.cs (new; enum + template),
Monster/MonsterSpawner.cs, Monster/DungeonMonster.cs (ApplyPromotion),
Monster/MonsterCommandUI.cs, DungeonCore/DungeonBuildController.cs (promotion
API), Room/MusterRooms.cs, Room/RoomValidator.cs, UI/BossAlertService.cs,
Save/DungeonSaveData.cs + DungeonSaveController.cs (additive
promotionRank/bossEpithet).

**Rejected:** per-definition boss variant coverage (doubles the registry
forever); rank-at-placement in the picker (superseded by the command-panel
verb); demotion (refund complexity for no play value); Boss Room as optional
housing (rule B -- the room must gate, or its cost buys nothing); promotion of
wilds, risen minions, or transients.

---

*Seeded 2026-07-09 against repo HEAD. Amend via guide chapters only.*

### Reachability -- the severed-halls watch and the mine-mode wash

Connectivity is judged by the PATHFINDER'S passability rule (mined-and-not-
overhung, river, river bank, cave approach), never by "is this cell mined" --
mined-but-unwalkable cells are the failure players cannot see unaided.

ReachabilityDirector (on the dungeon GameController) floods from each floor's
core cell after every dig, debounced. If the entrance falls outside floor 0's
set while the core is on floor 0, it raises an AlertCategory.Threat alert and a
wisp line, and speaks a relief line when the route returns.

In Mine mode every core-joined cell is washed in a slow pulse; only reachable
cells are tinted, so absence is the warning. Water and banks are included, which
teaches that a ford is a real route. The overlay tilemap and its tile are built
at runtime per floor, so new floors need no wiring.

Rooms are never severed retroactively: nothing removes cells from minedTiles in
normal play. Regions are instead BORN disconnected -- MarkNaturalFloor adds
chamber, cavern and river-bank cells with no adjacency requirement, so those are
walkable islands until a tunnel joins them.


Waves gate on reachability: AdventurerSpawner holds while
ReachabilityDirector.RouteToCoreOpen is false, so raiders are never sent at a
dungeon they cannot enter. The gate is permissive when unknown -- no director,
or no check yet, both read open -- so a missing watchdog can never stall the
game. The severed-halls alert explains the silence to the player.

On a peaceful Inspector departure the wisp adds a reminder to re-arm the halls
before the next parties arrive.

### Claim-edge rendering -- three passes that must stay in step

CaveWallRenderer paints the caps, DungeonShadow shades them, InfluenceRingRenderer
draws the boundary overlay. All three rebuild in the SAME frame as the claim that
dirtied them. Do not add a time or frame gate to any of them: a deferred pass
leaves a newly claimed cell showing raw terrain art until the others catch up,
which reads as the claim edge flickering. A seconds-based gate is also inert by
construction, since it stops gating once a frame exceeds the gate.

CaveWallRenderer carries [DefaultExecutionOrder(-50)] so the caps exist before
the pass that shades them, and exposes RebuildTick; DungeonShadow follows that
tick so cap and shading always land together.

DungeonShadow.RecomputeBase is allocation-free (all scratch collections are
reused) and paints by diffing against what is already drawn rather than clearing
the tilemap. Its void flood stops at voidFalloffCells: beyond that depth the
curve has reached voidLightFloor, so every deeper cell takes the identical
plateau value that the rimless sweep assigns anyway.

PANEL HOTKEYS

A toggleable panel's hotkey is NOT a private [SerializeField] Key on the panel.
Add a GameAction to Keybinds.cs (enum, Defaults, All, Label) and read it with
Keybinds.WasPressed(GameAction.YourToggle) in Update. This gives the panel a
rebindable entry in the Controls UI and a conflict check, and keeps every
binding in one place. The text-input guard is inside WasPressed, so do not
re-check IsTextInputActive in the panel. The five journal-style panels (Loot,
Quest Log, Known Parties, Factions, Research) are the pattern to copy.

The only components that legitimately keep a private toggleKey are non-panel
debug/HUD toggles bound to F-keys (CaveWallDebugOverlay, HudToggle).

## 28A. The Wandering Merchant (Trader Channel)

**As built:** `WanderingMerchantController` (floor 0, beside the surface
systems) + `MerchantShopUI` + `TraderStockCatalog` asset authored by the
Dungeon Core -> Generate Trader Stock menu item. Per the surface-war canon he
stages through the forest-road gate (serialized gate Transform; appears at
the dock if unset) and docks at the camp's commerce anchor -- exposed by a
new `CampGrowthController.CommerceAnchors` registry filled at the commerce
Instantiate and cleared on ruin. **Visit gate:** he comes only once a camp
reaches Camp tier (TierOf >= 1) with a live anchor; a ruined stall skips
visits until rebuilt. **Cadence:** arrives on OnDayStarted when due, leaves
at dusk on OnNightStarted; the gap re-rolls 3-7 days at each departure and
persists (`DungeonSaveData.merchantNextVisitDay`; -1 = unscheduled, due the
first eligible day -- which also makes a mid-visit save re-arrive cleanly).
**Stock:** rolled per visit, 4-6 slots, at least one catch-up when eligible;
sold entries leave until the next visit. Catch-up eligibility is plumbing-
free: a loot-band pattern stocks once a HIGHER loot band is already learned.
All Reserved-band patterns sell at a flat 240g (the only source -- buying
Gravegold is what opens The Waiting Dark's gate); catch-up follows the
approved curve 60/100/180/320/550 by band; the six loot books grant nodes
outright via `GrantNodeFully` at 220g (T2) / 400g (T3) / 480g (the Whisperer
apex), anchored against the shipped bribe costs (150g Inspector / 250g
Mercenary). Purchases route patterns through a new public
`PatternDiscovery.NotifyTraderPurchase` (source "trader"; the usual
discovery bark fires). The wisp marks his first arrival once
(`merchant_first`) with an Excite. The shop closes on ESC through
PauseMenuController's central chain (first among the transient panels) or
the Close button; the game keeps running while the wagon is open.
The Sorcery book pair (Primer of the First Spark,
The Drawn Breath) stays reserved by name until core spells are greenlit.
Adding stock later (specials included) is assets-only: new typed entries in
the catalog.
## 29. Wisp Quests (Urgings) and the Pressed Rule

Status: SHIPPED with the tutorial-expansion delivery.

**Engine:** the legacy prologue quest stack (`Quest` / `QuestController` /
`QuestRegistry` / `RewardsController` / `QuestUI`), already present in the
dungeon scene, is the wisp-quest engine - the fold-in entry 17 reserved. New
`WispQuestDirector` (one component, e.g. on GameController) stands up a
`QuestRegistry` beside itself; urging assets live under
`Resources/Quests/Wisp` (authored by the Wisp Quest generator, Dungeon Core ->
Generate Wisp Quests) so the registry self-populates. `RewardsController`
gains its Gold and Experience implementations (`DungeonCore.AddGold` /
`AddXP`) - urging rewards are small Core XP, occasionally gold.

**Model:** urgings are offered, never pushed as modal steps. The opening pair
(wq_carve, wq_journal) is offered by the TutorialDirector at its new Carve
beat; the free-play batch (research, muster, traps, notoriety) offers at
tutorial handoff; the rest chain contextually in the director's once-a-second
sweep (pattern after research, tier-2 after muster, capture when a capture
trap or captive first exists, floor 1 when stairs become placeable).
Objectives are sweep-derived where possible - world state is recounted and
absolute-set through the new `QuestController.SetObjectiveProgress`, the Deeds
idiom - with one push pair: `QuestLogUI` reports journal opens and Deeds-tab
views. Completed urgings auto-hand-in (no NPC, no item removal): rewards,
one wisp line, a small Excite, and `OnWispQuestCompleted` for listeners.

**Tutorial rework:** the guided opening gains beat 4b, Carve - after the grace
day the wisp asks for a room-shaped pocket (a 3x3 of mined, claimed ground
clear of existing room footprints; `WispQuestDirector.CarvePocketExists`) and
only then for the room itself. The beat completes through the wq_carve urging;
an already-carved save skips it on offer. Resume routes through Carve.
Tutorial lines: new tut_carve / tut_nudge_carve; tut_room and its nudge
reworded to build in the carved chamber.

**The Pressed rule:** dungeon-owned monsters massed on mined ground OUTSIDE
any valid room footprint fight poorly. New `CrowdingController` (one
component) sweeps every floor each half-second, reaching owned monsters
through their spawners: four or more clustered within two cells (Chebyshev)
of one another in the corridors take a x0.75 damage-dealt multiplier
(`crowdDamageMultiplier`, combined multiplicatively at the strike beside the
Throne and Trophy multipliers) and a temporary sprite shade (promotion-safe
restore). Wild and invader monsters are exempt. Adventurers are exempt BY
DESIGN: a party debuffed in transit through a one-wide tunnel would reward
tunnel-spam, the exact habit the rule exists to break - their corridor
treatment arrives with the formation layer (formations holding in corridors).
First trigger speaks the pressed_first one-shot.

**Persistence and veterans:** `DungeonSaveData` gains wispQuestsActive /
wispQuestsHandedIn / wispQuestsInitialised (additive). `QuestProgress` now
inlines its questID - the asset reference does not survive JsonUtility
across sessions - and `LoadQuestProgress` re-links through it (fixes a
latent legacy landmine). On first meeting a
save that predates the feature, the director reconciles silently: a finished
tutorial records the opening pair as history, and any urging already
satisfied records on offer with no reward and no announcement - history is
not an event to announce (the Deeds precedent). New-game reset rides
`DungeonSaveController` beside the TutorialDirector's.

**Key files:** `Wisp/WispQuestDirector.cs`, `DungeonCore/CrowdingController.cs`,
`Editor/WispQuestContentGenerator.cs`, `DungeonCore/TutorialDirector.cs`,
`Data/QuestController.cs`, `Data/RewardsController.cs`, `UI/QuestLogUI.cs`,
`UI/QuestUI.cs`, `Monster/DungeonMonster.cs`, `Wisp/WispTutorialScript.cs`,
`Wisp/WispScript.cs`, `Save/DungeonSaveData.cs`, `Save/DungeonSaveController.cs`.

**Rejected:** symmetric corridor crowding (debuffs a marching party in
transit and so rewards one-wide tunnels - backwards); a parallel wisp-only
quest system (the journal already renders the legacy engine); pathing
refusal over a stat penalty (fights the player's own orders); temperament
variants for urging text (seven-fold writing for little gain - Excite
already colours completion by personality).
---

## 30. Ranged Combat (Projectiles, Damage Kinds, LOS)

Status: SHIPPED. Verified: 2026-07-30.

Supersedes the section-27 rejection of a monster projectile system and
fulfils the section-10A shield-wall intent. The adventurer-Mage hitscan
convention is retired.

**Damage kinds.** `DamageKind { Melee, Ranged }` (Gameplay/DamageKind.cs).
`DungeonAdventurer.TakeDamage(float, DamageKind = Melee)` -- one defaulted
parameter, zero call-site churn; `IMonsterTarget` stays single-arg and the
explicit interface implementation forwards on the default. The shield wall
mitigates Ranged ONLY: melee that reaches the rear rank earns full damage,
and environmental sources (traps, chests, room effects, sparring chip, core
burn) ride the Melee default and are never wall-mitigated. This retires the
old behaviour where the wall reduced every damage source reaching a rear
member. shieldWallMitigation stays 0.4 -- retune only if ranged-only feels
thin. Monster TakeDamage stays untyped by ruling (no mechanic reads it).

**Projectile.** `DungeonProjectile` (Monster/): a straight-line travel-time
bolt aimed at the target's position at fire moment; speed per definition
(casters 7, arrows 10, adventurer classes 8); hit radius 0.4; a dodged bolt
flies on and fizzles at the first solid cell or aim distance + 1.5. The
bolt carries the full attack payload -- damage, impact number, grudge
record, knockback, formation break, and side-specific callbacks (kill
credit / XP / titles on the monster side; taunt peel and tracked-party XP
on the adventurer side) -- so a shooter that falls mid-flight still lands
its loosed shot. Transform-touching kill credit is guarded (`this == null`)
and forfeited by a dead shooter; everything else lands regardless. Rendered
on the Player sorting layer (a world entity per Appendix B); the built-in
soft-glow sprite is generated at runtime (the selection-ring pattern) and
tinted per definition (`projectileTint`), with `projectileSprite` as the
bespoke-art override. Transient by ruling: never serialized -- a mid-flight
save drops the bolt, the same as a mid-windup telegraph. No pooling (fire
rates are one bolt per attacker per cooldown; the DamageNumberSpawner
instantiation precedent).

**Line of sight.** `DungeonProjectile.HasLineOfSight` samples
`DungeonPathfinder.IsWalkable` at 0.45-unit steps along the shooter-target
line: walls and overhangs block a shot, rivers do not, and bodies never do
-- the shield wall's mitigation IS the front-rank-blocks-for-the-rear
fiction, so bolts pass through bodies to their target (no physical
interception, by ruling). The gate is wired into the existing out-of-range
reposition branch on both sides, so a blocked shooter walks until it has
the shot; target acquisition stays distance-based (they know you are there
-- they reposition to shoot). In-flight bolts also fizzle on entering solid
rock as a safety.

**Sense clamp.** A ranged attacker must sense at least as far as it can
shoot. The caster prefabs ship with the base detectionRange (3.0) under
reaches of 2.8-4.2, so both sides clamp at initialise:
`detectionRange = max(detectionRange, attackRange + 0.5)` when
firesProjectile (monsters, in Start) or rangedAttacker (adventurer class
overlay). No prefab edits.

**Who fires.** The shipped caster roster, at shipped ranges and telegraphs
(no stat edits): the six Adepts (3.6), the six warcaster -mancers (3.8),
the six Archons (4.2), Necromancer (2.8), Deathpriest (3.0) -- stamped
firesProjectile with per-affinity bolt tints (Fire ember-orange, Water
azure, Air pale sky, Earth ochre, Dark violet, Light gilt, the necromantic
pair bone-green) via Dungeon Core -> Ranged Combat -> Stamp Ranged Casters
(idempotent; ranged-ness is authored on MonsterDefinition, never the
prefab; tints stamp on the first flip only, so hand-whitening a definition
for bespoke pre-coloured bolt art survives a rerun). The telegraph tint ramp is the cast wind-up: Begin's strike
callback swaps DealAttackDamage for FireProjectile on ranged definitions.
The adventurer Mage fires the same projectile (the rangedAttacker overlay
flag, class-colour tint, speed 8); Explorer stays melee (its ranged feel
was never real -- attackRangeMultiplier 1). Wild-side use is automatic: any
definition with firesProjectile fires, including future ranged wilds.

**Archers.** Three universal physical-ranged monsters, generator-authored
via Dungeon Core -> Ranged Combat -> Generate Archer Monsters (idempotent:
creates defs, prefab variants and registry links, and on rerun refreshes
balance stats in place -- sprite and animator hand-edits on the variants
survive; stand-in sprites borrowed from donor prefabs and no icons, the
roster norm, pending bespoke art):

- Bone Archer -- Undead, Bronze 6 (an empty rank), range 3.2, HP 24,
  damage 7, cooldown 2.0, cap 8 / 22 mana. Donor: Monster.prefab.
- Hobgoblin Sharpshooter -- Humanoid, Silver 4 (an empty rank), range 3.4,
  HP 60, damage 12, cooldown 2.2, cap 20 / 55 mana. Donor:
  Monster_HobgoblinSpearman.
- Dread Marksman -- Humanoid, Gold 2 (fills the automatic channel beside
  the research-gated Deathpriest), range 4.0, HP 100, damage 22, cooldown
  2.6, cap 60 / 125 mana. Donor: Monster_Warlord.

Arrow speed 10, fletching-grey tint; categories route them to the existing
muster rooms (Crypt / Barracks). The ranged niche trades HP for reach
against the same-rank melee entries.

**Rejected:** hitscan and homing projectile models (travel-time chosen --
formation dispersal on a break becomes real dodge value); physical
body-block interception (double-stacks with wall mitigation); LOS on target
acquisition (repositioning is the behaviour, blindness is not); typing on
monster TakeDamage (no mechanic needs it); projectile pooling; per-prefab
detectionRange hand-edits (the sense clamp is definition-driven).

**Key files:** `Gameplay/DamageKind.cs`, `Monster/DungeonProjectile.cs`,
`Monster/MonsterDefinition.cs` (Ranged block), `Monster/DungeonMonster.cs`,
`Adventurer/DungeonAdventurer.cs`, `Editor/RangedContentGenerator.cs`,
`ScriptableObjects/Monsters/Regular/MonsterDef_BoneArcher.asset` (+
Sharpshooter, Marksman, and their prefab variants, generator-created).

## 31. The Trapworks (Roster, Type Exclusivity, Trapwright)

Status: SHIPPED. Verified: Complete

The trap system reworked from a fixed six-trap set with hardcoded behaviour
switches into a data-driven roster of fifteen, with core-type exclusivity,
a research spine, and a retroactive upgrade line.

### Elemental exclusivity (the type-lock rule)

Each of the six elemental traps belongs to exactly one core type and is the
signature of that core: Fire holds the Fireball Rune, Water the Ice Spikes,
Earth the Earth Spikes, Air the Gale Vent, Light the Blinding Flash, Dark
the Umbral Snare. The other five are hidden entirely on a mismatched core:
never in the research tree (VisibilityCondition.CoreAffinity, and
CanAppearThisRun() drops them from tree layout so no reserved gap remains),
never in the trap carousel, and refused at placement as a backstop with the
wisp line "The core cannot hold that element's shape". Neutral traps are
universal. GrantBuriedDiscovery already filters by matching affinity, so
buried sites can only ever grant the core's own signature.

The canonical opposition table -- Fire against Water, Air against Earth,
Dark against Light -- is recorded here as the standing reference for future
type-interaction systems. It does not gate trap access; exclusivity does.

### The roster (fifteen traps)

Six pre-rework traps unchanged in behaviour: Spike, Pitfall, Warning,
Pressure Plate, Snare (capture), Scatter. Nine new:

- Crossbow (neutral, tech.crossbow_trap, 16 mana / 3 cap): a sentry, not a
  snare. Scans sentryRange 3.5 for the nearest adventurer with line of
  sight (wild monster as fallback target, the pressure-plate precedent) and
  looses a real DungeonProjectile every 2.4s for 9 damage. Bolts land as
  DamageKind.Ranged, so a marching shield wall mitigates them -- Scatter or
  Gale Vent is the intended opener. Never cell-triggered: walking its cell
  is safe, so detoursWhenFlagged is off and a flagged crossbow keeps firing
  until a Rogue disarms it (awareness buys avoidance, not immunity, and a
  sentry cannot be avoided by pathing).
- Fireball Rune (Fire, 22 / 3): burst radius 1.6 around the cell, 16 damage
  to everyone in it, plus a clinging burn on adventurers (4 dps for 3s,
  ticked once per second). Cooldown 6s. A wild stepper detonates it for all.
- Ice Spikes (Water, 14 / 2): 10 damage plus a near-freeze (slow x0.05 for
  2.5s). Cooldown 4s.
- Earth Spikes (Earth, 16 / 2): 20 damage -- the heaviest single wound in
  the trapworks -- plus knockback 1.5. Cooldown 5s.
- Gale Vent (Air, 12 / 2): knockback 2.5, formation broken for 4s, 4 buffet
  damage. Cooldown 5s. The strongest formation-breaker.
- Blinding Flash (Light, 14 / 2): burst radius 1.5; every adventurer in it
  loses its combat target, all but stills for 1.5s, takes 5 sear damage, and
  has trap-sense suppressed for 8s (no detecting, no disarming). Cooldown
  8s. Light-affinity theming holds: this light judges, it does not guide.
- Umbral Snare (Dark, 12 / 2): no damage; knockback 0.8, slow x0.5 for 3s,
  and monster-detection range halved for 6s so the dark's own get the first
  blow in. Cooldown 6s.
- Sleep Dart (neutral, tech.sleep_dart, 10 / 2): no damage; the blind
  primitive with no sense suppression -- target dropped, all but stilled
  for 3.5s. Cooldown 5s.
- Siphon Rune (neutral, tech.siphon_rune, 10 / 2): 6 damage and 10 mana
  returned to the core per trigger. Cooldown 3s. Adventurers only pay the
  tithe -- wilds take the wound but grant nothing, or wandering wilds would
  be a slow mana farm.

### Research spine (Architecture lane)

crossbow_trap "The Patient Arm" (t2, 15 pts / 2d, prereq spike_trap,
pattern TemperedSteel) is the trunk. All six elemental nodes sit at t3 (30
pts / 3d, prereq crossbow_trap, visibility CoreAffinity, affinity set -- so
the one core that can see its node also gets the 50% affinity discount):
trap_fireball "The Waking Ember" (WroughtIron), trap_ice_spikes "Teeth of
Winter" (Silverwork), trap_earth_spikes "The Rising Stone" (VeinedGranite),
trap_gale_vent "The Hollow Gale" (QuarrySand), trap_blinding_flash "The
Searing Glance" (HallowedStone), trap_umbral_snare "The Clinging Dark"
(Gravegold -- a second consumer for the trader-exclusive pattern).
sleep_dart "The Quiet Needle" (t3, 20 / 2d, CuredLeather) and siphon_rune
"The Tithing Mark" (t3, 20 / 2d, Silverwork) hang off the same trunk.

trapwright_1 "Trapwright's Craft" (t3, 25 / 3d, prereq crossbow_trap,
WroughtIron) and trapwright_2 "Master Trapwright" (t4, 45 / 4d, prereq
trapwright_1, TemperedSteel) are the upgrade line. TrapMastery (static,
Traps/) reads UnlockState at fire time: damage and affliction durations
x1.25 at tier I, x1.5 at tier II, cooldowns x0.8 at tier II. Retroactive by
construction -- every placed trap sharpens the moment the node completes --
and stacks multiplicatively with RoomEffectCensus.TrapDamageMultiplier
(Forges, mounted trophies). The capture hold is deliberately excluded from
duration scaling: lengthening it widens the rescue window.

### Data-driven trap flags

TrapDefinition gains requiredTechKey, affinity, disarmable and
detoursWhenFlagged; the scattered behaviour-enum switches are gone.
IsDisarmableTrap reads def.disarmable (pre-rework values preserved: Spike
and Pitfall true, the rest of the old six false; all nine new traps true).
GetFlaggedCells reads def.detoursWhenFlagged (pre-rework preserved: Warning
and Pressure Plate false, Crossbow also false, everything else true). The
carousel lists only unlocked, affinity-valid traps -- the tree is the
discovery surface, the picker is the toolbox -- and rebuilds in place on
UnlockState.OnChanged, keeping the current pick if it survives. Placement
carries both backstop gates.

### Adventurer statuses (transient by the section-30 precedent)

ApplyBurn (dps ticked once per second; strongest dps and latest end win on
refresh), ApplyBlind (telegraph cancelled, combat target dropped -- the
combat state machine self-heals off the null target -- near-stilled via the
slow machinery, trap-sense suppressed while blindUntil holds; IsBlinded
gates both ScanForTraps and TryBeginDisarm), and ApplySenseDamp
(EffectiveDetectionRange multiplies monster-detection down at the three
ScanForMonsters call sites; the faction-brawl and commoner-panic scans are
deliberately unaffected). None of these survive a save, matching the
mid-flight-bolt ruling.

### Wild monster ruling

Damage, slows and knockback apply to wilds equally; burn, blind, sense
dimming, formation breaks and the mana tithe are adventurer-only. The line:
anything needing new monster-side plumbing or modelling an adventurer
concept stays adventurer-side. A wild stepping on an adventurer-only trap
still spends its charge.

### Bug fixes and fold-ins riding this arc

- PressurePlateTrap.FireLinkedTrapForMonster used the never-assigned
  TrapRegistry.Instance, so plates never fired their linked trap for wild
  monsters. It now resolves the per-floor registry via FloorRoot; the dead
  Instance property is removed.
- The hand-added Cold Iron (prison) and Standing Orders (patrol_orders)
  nodes had fallen out of TechTree.asset on main, leaving both
  unresearchable. Both are folded into TechContentGenerator verbatim (Cold
  Iron's full description restored), so the generator is authoritative
  again and reruns keep them.

### Generator authority

TrapContentGenerator (Editor, menu Dungeon Core -> Generate Trap Content)
is authoritative for trap content: it authors the nine definitions, their
stand-in prefabs (donor spike sprite under an affinity tint, replaced by
the sprite pass later), patches the six existing definitions' new fields,
and appends missing registry links preserving hand order. Icons are never
touched on rerun. Author new traps there, not by hand.

Key files: Traps/TrapDefinition.cs, Traps/TrapBase.cs, Traps/TrapMastery.cs,
Traps/CrossbowTrap.cs (+ the eight sibling subclasses), Traps/
TrapRegistry.cs, Traps/PressurePlateTrap.cs, Traps/TrapSelectionUI.cs,
Adventurer/DungeonAdventurer.cs, DungeonCore/DungeonBuildController.cs,
Gameplay/TechNodeDefinition.cs, Gameplay/ResearchTreeUI.cs,
Editor/TechContentGenerator.cs, Editor/TrapContentGenerator.cs.


## 32. The Living Prologue (Town, Forest, Ceremony)

**As built.** A complete pre-dungeon act, shipped and previously undocumented.
Scene chain: `TitleScreen` -> `TutorialTown` -> `TutorialForest` -> `Ceremony`
-> `Dungeon_Level_0`. New Game routes through it unless
`TitleScreenController.skipPrologue` is set, which jumps straight to the direct
type-pick phase instead.

**The living part.** The prologue is not a cutscene: the player lives an
ordinary last day, and what they *do* is recorded. `FlagInteractable` writes a
flag through `Persistence.SetFlag` on interaction; the canonical flag strings
are constants in `TutorialFlags` (Inspector fields are typed by hand and must
match them exactly). Flags are session-scoped and wiped at the start of a fresh
prologue run so a previous session cannot leak forward.

**Deeds to affinity.** `AffinityMapping` (a ScriptableObject, so weights and
copy are tunable without a recompile) owns the mapping and every line the wisp
speaks reading a life back:

| Affinity | Deeds | Read-back |
|---|---|---|
| Fire | bellows, quench | worked the forge and did not flinch |
| Water | draw well, fill jug, free net | went toward the water when others would not |
| Air | mill climb, free pigeon | climbed for the view, freed what was caught |
| Earth | dig grave, dig row, haul stones | turned the ground with their own hands |
| Light | help healer, light candle, give alms | mended more than they broke |
| Dark | smash crates, take offering | took what was watched, broke what was stacked |

**Correction of record (flag wiring).** For the whole of this system's shipped
life the table above was aspirational, not true. Four authored `flagID` fields
carried a **trailing space** (`flag_draw_well `, `flag_dig_row `,
`flag_fossil_found `, `flag_smash_crates ` on five prefabs) and
`AffinityMapping` matches by exact string, so those deeds scored nothing.
`FouledNet` and `CressJug` were additionally wired to `flag_smash_crates`
rather than their own flags. Effective ceilings were Fire 2/2, Air 2/2,
Light 3/3, Earth 2/3, Dark 1/2, **Water 1/3** -- a life spent entirely on
water lost to one stray forge deed. Repaired by a `Trim()` in
`Persistence.SetFlag` (the durable fix: Inspector-typed ids are hand-entered
and a trailing space is invisible in the field) plus retyping the assets. Any
future flag audit starts here.

Scoring is **normalised** - each affinity scores the fraction of its own flags
earned - so two-flag affinities weigh exactly the same as three-flag ones.
Kneeling at the old stone (`flag_pray_shrine`) adds `prayShrineBoost` (0.25) to
whichever affinity already leads: **devotion sharpens identity, it never stages
a coup**. Earning nothing is a legitimate path - the empty-handed line frames it
as its own kind of freedom. The easter-egg flags (`fossil_delivered`,
`repair_mill`) vote for nothing; they earn a teasing acknowledgement and wait on
hidden types that do not exist yet.

**The ceremony.** `CeremonyController` directs it: the gloom lifts in stages,
the wisp arrives, and four teaching beats assemble the facsimile HUD one piece
at a time - **move** (pan), **breathe** (zoom), **reach** (sense), **pulse**
(hold to feel ambient mana). Then the read-back of the life lived, the affinity
choice, a recolour of world sprites and UI from white to the chosen affinity,
and the handoff. Two standing rules: the cage is **soft** (pan and zoom are live
from the first frame - prompts choreograph discovery, they never disable input),
and the read-back is **suggestion, not gate** (all six affinities stay
selectable; deeds add emphasis, a read-back line, and the dimming of roads not
taken).

**The commit.** The chosen type is written to
`SaveSlotManager.PendingNewGame.dungeonType`; if no pending exists (the prologue
path arrives with none, since `LaunchSlot` clears it) the controller **builds
one** rather than dropping the choice - this was the dark-became-fire bug, and
the guard must stay. Then `SceneLoader.FadeToScene("Dungeon_Level_0")`.

**Persistence.** The prologue writes a checkpoint at
`SlotPaths.ProloguePath(slotId)`; `DungeonSaveController.InitializeNewGame`
saves the real dungeon and **consumes** (deletes) that checkpoint the moment the
dungeon exists on disk, so a slot is never left with both.

**Gotcha - `SceneNames.GameScene`.** The enum int-serialises in hand-placed
scene triggers, so deleting a middle value silently re-targets every door after
it. The retired `Forest` entry is kept deliberately as a tombstone. Never remove
a middle value; append only.

**Ceremony Gloom.** The full-screen veil lives on the Shadow sorting layer (see
the sorting-layer section) so darkness covers walls and entities alike.

**What happens to the life afterwards:** entry 34. The flags recorded here are
no longer discarded at the handoff.

---

## 33. Monster Target Priority (Class-Aware Targeting)

Status: SHIPPED. Verified: 2026-07-30 (landing date unrecorded; documented by
the canon hygiene pass).

Monsters no longer swing at whoever is closest. Each monster type carries a
targeting preference, so a dungeon's roster expresses intent -- backline divers,
healer-killers, finishers -- instead of every creature behaving identically.

**The enum.** `Monster/TargetPriority.cs`, four values:

- `Nearest` -- no class bias. The default, and behaviourally unchanged from
  before the system existed.
- `Casters` -- prefer Mage or Cleric. Dive the fragile backline.
- `Healers` -- prefer Cleric. Kill the heals first.
- `Wounded` -- prefer the lowest HP fraction. The finisher.

**Resolution.** Authored per type as `MonsterDefinition.targetPriority` and
cached on `DungeonMonster`. Inside `ScanForHostiles` the monster gathers every
in-range adventurer into a reusable buffer (minus spared Pilgrims) and passes it
to `SelectAdventurer`, which scores each candidate with `PriorityKey` -- lower
wins, nearest breaks ties. The class modes return 0 for a match and 1 otherwise,
so the nearest match wins and the monster falls back to the nearest of anyone
when no match is in range; `Wounded` returns the HP fraction directly; `Nearest`
returns 0 for everyone, leaving the tie-break to pick pure nearest. The buffer
is a member field, not a local -- no per-scan allocation.

**Precedence.** A taunting Tank still overrides everything: the taunter check
runs first and returns early (see 10A -- the taunt is timed, and a peel grants
`tauntImmuneUntil`, after which target priority resumes). Wild-versus-player
monster targeting is untouched and stays nearest-based; a closer hostile monster
still preempts the chosen adventurer.

**Authoring.** Hand-set in the Inspector on the monster asset -- no generator
writes this field, so a regenerate never clobbers it and never fills it in
either. Currently authored on 31 of 111 definitions (21 `Casters`, 9 `Wounded`,
1 `Healers`); the rest sit on the `Nearest` default. New monsters default to
`Nearest` and are opted in deliberately.

**Design note.** This is the counterweight to the shield wall. A formationed
party puts Clerics and Mages in the rear rank where ranged damage is mitigated
(10A); a `Casters` or `Healers` monster is the player's answer, and the exposure
between taunts is intended, not a gap.

**Key files:** `Monster/TargetPriority.cs`, `Monster/MonsterDefinition.cs`
(`targetPriority`), `Monster/DungeonMonster.cs` (`ScanForHostiles`,
`SelectAdventurer`, `PriorityKey`).

---

## 34. The Core's Own Past (Persisted Life, Memory Echoes)

Status: SHIPPED (persisted life, eight echoes, the empty-handed voice, the
town's descendants, the resting place). The elapsed-time rule below is a
recorded lore decision, not code.

Entry 32 records a prologue that captures who the player was with real
specificity and then throws it away at the ceremony. This entry keeps it.

### The life persists

`Persistence` flags are written to `DungeonSaveData.prologueFlags` in
`SaveGame` and restored into the same static set on load (cleared first -- the
statics outlive a slot switch and one core must never wake remembering
another's life). **All twenty flags persist**, not a curated subset: the
storage is trivial and discarding one now would mean a save migration to get
it back later. The discipline belongs in the echo table, not the save.

The prologue checkpoint is still **consumed** in `InitializeNewGame` exactly as
entry 32 requires. Nothing about that invariant changed: the statics survive
the Ceremony -> Dungeon scene load and that method's `SaveGame()` runs before
the delete, so the life is captured on the way past.

**`flag_lived`** is written by `CeremonyController.Commit`. It marks a life as
having been *lived* rather than skipped -- without it an empty-handed run and a
`skipPrologue` run are indistinguishable, and the empty-handed voice would
speak to players who never had a last day at all. `TitleScreenController`'s
skip path now also clears `Persistence`, closing a leak that only became
reachable once anything downstream started reading flags.

### Memory echoes

`CoreMemory` is the dungeon's read surface (`Lived`, `Remembers`,
`EmptyHanded`, `Recall`). `Persistence` stays what its own header says it is --
the prologue's store -- and nothing below the surface touches it directly.

An **echo** is one wisp line, fired **once ever**, at a dungeon moment that
rhymes with something the player actually did on their last day alive. Echo
lines are authored `once = true` in `WispScript`, so the shipped
`wispSpokenLines` save field already remembers them: **the feature adds no save
state beyond the flag list**.

`CoreMemory.Recall(momentId)` is the entire API. Four outcomes:

| State | Result |
|---|---|
| Never lived the prologue | silence, always |
| Lived, holds the bound flag | the echo line |
| Lived, empty-handed | a hollow line, at most three ever |
| Lived, holds other flags | silence -- this memory is not theirs |

The eight shipped echoes, one per affinity plus the old faith, plus the
desecration echo added when `AlignmentSystem.Desecrate` gained a caller:

| Deed in life | Dungeon moment | Site |
|---|---|---|
| `dig_grave` | first deliberate raise | `CryptController.RaiseFromSarcophagus` |
| `free_net` | first adventurer pinned | `DungeonAdventurer.BeginPinned` |
| `take_offering` | first tribute absorbed | `TributeChest` |
| `give_alms` | first adventurer stripped | `DungeonAdventurer` death path |
| `mill_climb` | first descent below floor 0 | `FloorManager.SetActiveFloor` |
| `quench` | first trap fires on the living | `TrapBase.OnAdventurerEntered` |
| `pray_shrine` | first buried remains dug | `BuriedRemainsController.Grant` |
| `light_candle` | first Church seal broken | `HolyGroundLedger` |

`Recall` is `IsLoading`-guarded (a restore replays history in a few frames and
none of it is happening to the player) and **speaks at most one echo per
frame** -- a capture trap resolves `BeginPinned` inside `ApplyEffect` and the
trap site then recalls in the same call, so a life holding both deeds would
otherwise hear two stacked on one snare. It mirrors `DeedsController.NotifyMoment`
-- whose moment ids the table deliberately reuses where they already exist, so
the two systems read as one vocabulary. The Light echo anchors on the **kill**
rather than `DroppedLoot.Absorb`, because the tribute coin flourish runs the
same absorb and would fire the wrong memory.

The desecration echo binds `light_candle` rather than `pray_shrine`: praying is
already spent on the buried echo, and the pairing is sharper anyway -- in life
you lit a candle at a shrine, and here you break one.

**Rejected:** a trap-*kill* echo (no kill attribution exists; trap-fired is the
honest hook). The Holy Ground desecration echo was rejected here for want of a
caller and later un-rejected once `AlignmentSystem.Desecrate` had one; it is the
eighth row above. This paragraph claimed otherwise for longer than it should
have -- the code was right and the entry was stale.

### The empty-handed life has a voice

Entry 32 calls earning nothing "its own kind of freedom" and then says nothing
else about it. It now has three authored lines, spoken in order at the first
three echo moments and then never again: the wisp reaches, finds nothing,
remarks on it, and finally **stops reaching**. Refusal is a shape, and this is
what it sounds like. Deliberately not mechanical -- the empty-handed core is
unencumbered by design, and paying it a bonus would make the hollow path the
optimal one.

### Lore recorded

**How long you were gone: nobody who knew your face is alive.** A rule, not a
number -- deliberately vague in the manner of the Buried Age. Any future
content wanting a survivor of the prologue generation must argue for it. This
is load-bearing: it decides what can return, and it is why the wisp is the only
witness left to quote a life back.

**What killed you is not a blank.** The prologue exchange is explicit and
named. Deep in the cave, at an **opened seal**, three delvers the player met in
town that same day -- **Pell**, **Brother Mott** and **Serra Vane** -- find
them standing in front of it. Pell panics and strikes. Serra finishes it:
*"...Half a job."* / *"Close your eyes. The dark takes care of the rest."*
Serra had said in the tavern that there is older stone under that town than in
it, and Mott had asked whether anyone still tends the old stone.

The blank is therefore **not** the murder. It is **what seal they had opened,
and where Serra's maps came from** -- which points directly at entry 21 and the
Sealed Gates of entry 19. That question stays open on purpose, and the answer
may be shared with whatever the dwarves will not go below.

**The wisp was already there. SHIPPED.** It read the player's life back from
flags it had no business seeing, waiting at the rebirth site -- and entry 20
records that deep shrines warded rebirth sites. It is plausibly a warden of the
old deep-faith doing a job it has done many times before, for cores that failed.
This reframes every prologue line at zero cost, explains the rare `Ancient` and
`Reverent` temperaments, and gives it standing to recognise a dead core's ruin
when one is found.

That ruin is the `DeadCoreVault` of entry 20, and the recognition is now three
lines in `FeatureRevealController.SpeakForSite`. On revealing the VAULT the wisp
speaks twice -- it knows the shape, and it has stood where the player is standing
before and not with them, and some of the others are still down here. On
revealing the first CHURCH SEAL of the run it speaks once, from the other side of
the practice: the Church inherited the sealing and forgot who from.

**Implied, never confirmed, and that is a constraint rather than a preference.**
The wisp says it has been here before. It does not say what it is. Canon keeps
its nature an open question and a line that settles it spends something no later
entry can get back -- the same discipline entry 34 applies to what seal the
player had opened and where Serra's maps came from.

**NOT gated on `CoreMemory.Lived`, unlike `site_sealed_gate`.** That line is
gated because the memory is the PLAYER'S -- they died at an opened seal. These
are the WISP'S, and a core that skipped the prologue has not thereby erased the
wisp's own history. Getting this backwards would have made the deepest lore in
the game invisible to exactly the players who chose the shortest opening.

**Two lines at the vault rather than three**, because `WispCompanion.Speak`
enqueues rather than clobbers -- so a chain plays in order -- but `holdSeconds`
is 3.2 and `ShowLine` is deliberately unskippable, which puts each line at about
four seconds of standing still. Eight seconds is what a once-ever discovery of
the largest set-piece in the game earns; twelve, mid-mine, with no way out, is
not.

Not touched: `holy_break_vault`, which already carries "once as new as you" and
does not need a second pass at the same beat; and the floor-4 descent, where
`CoreMemory.FirstDescent` already speaks through `MillClimb` and a second line on
the same trigger would collide.

### What came back, and what did not

**The town comes for you (descendants).** SHIPPED. With nobody alive who knew
the player, the beat is not the healer returning; it is a **name** returning.

`PrologueHouses` binds each of seven surnames to the deed of that last day
which belongs to it: Ferro/`bellows`, Cress/`fill_jug`, Bramm/`dig_row`,
Ashcombe/`help_healer`, Latch/`smash_crates`, Sedge/`take_offering`,
Crane/`light_candle`. The mapping is not decorative -- it is read off the
persisted flags, so **the houses that come down are the ones whose lives the
player actually touched**. The beggar at the gate is deliberately absent: he
says his own name is not worth giving, so `give_alms` makes nobody eligible.

**Vane is the eighth and is never eligible** -- it is the fallback. Serra Vane
stood over the player at the opened seal regardless of what they did that day,
so her line always has standing. An empty-handed core touched nobody and
therefore gets **Vane alone**: one woman, no company, which is the loneliest
form of the encounter and is left unpadded on purpose.

**Dispatch.** Once per run, at dawn, when the Guild's player-visible assessment
puts the dungeon at grade level `descendantMinGradeLevel` (**canon default 3**,
i.e. 1 + rating / `gradeRatingPerLevel`). Two or three houses ride in: the lead
line as a **named Hero**, the rest as **named Mercenaries** (one named Hero
only -- `IsNamedHero` gates the Gravegold discovery on death and three would
spam it), plus `descendantGuards` unnamed sellswords, all grade-scaled. Banner
reads "The <House> Warrant". Gated on `flag_lived`: a skipped prologue has no
town and never sees this.

**Everything after the first arrival is entry 4, unmodified.** Named members
make the party permanently tracked, and `SpawnReturningParty` respawns each
survivor under their own `presetName` with their own XP while replacing the
fallen with unnamed rolls. So the surviving lines return forever and **the
player can extinguish individual bloodlines out of a recurring party**. That
behaviour is emergent from shipped code, not built here.

**Nobody in the party knows any of this**, and nobody alive could. The only
reaction is the wisp's: one arrival line for the leading house, and one line
per house when its descendant dies -- sixteen authored one-shots.

**Your grave -- which is not a grave.** SHIPPED. Serra left the body where it
fell and nobody recovered it.

**The pocket.** `GenerateEntranceCave` rolls one extra chamberlet off the
interior half of the tunnel centreline, exactly as it rolls the carved
offshoots -- and then **deliberately does not carve it**. Its cells never enter
`carved`, so `UnfogEntranceCave` never reveals them and `MarkNaturalFloor`
never opens them. It stays ordinary stone, indistinguishable from the rock
around it, a few cells off the tunnel the player walks down every day.
Reserved into `reservedCoreCells` so no river or chamber can grow through it,
and rejected and re-rolled (six attempts) if it would land on the tunnel, on
another reserved feature, or within `entranceRestRimMargin` (**canon default
7**) of the disc edge. That last test is the **bedrock rim** guard: bedrock can
be neither claimed nor mined, and a body sealed in unminable stone is a body
nobody ever finds. It is a margin in cells rather than a bedrock query because
the rim map is generated AFTER features -- the same reasoning, and the same
default, as `riverBankRimMargin`.

Persisted on `EntranceCaveData` as `restCells` / `restCell` / `hasRest`.
`GenerateEntranceCave` runs only from `GenerateNew`, so **dungeons saved before
this shipped simply have no resting place** -- the fields are absent, `hasRest`
is false, and nothing speaks. No migration.

**Arming (the answer to "why not on day two").** The stone is inert until the
player **first descends below floor 0**: they have stopped being a hole in the
ground and started being a dungeon, and that is when it is worth showing them
what they used to be. Mining the cell before that does nothing at all and
leaves it undiscovered. The wisp admits the pocket exists at the **next dawn**,
never on the descent itself, so it cannot stack on that descent's own memory
echo (`echo_climb`). Then the player digs it out at whatever pace they like --
the camera is already there, so this needs no vignette and no camera move.

**Payload: nothing.** A hidden Deed (`found_self`, "Where You Stopped") and
four wisp lines, one of which is swapped for an empty-handed variant. No
Bestiary rung, no research, no pattern. `HandleMined` checks the resting cell
**before** the seeded buried sites and returns early rather than falling
through, so it can never pay an entry 17 grant by accident. This is the one
discovery in the dungeon that gives the player nothing, and that is the point.

Gated on `flag_lived` throughout: a skipped prologue has no body.

**Folded into `BuriedRemainsController`** rather than given its own manager --
it already sits on the managers object and already hooks `OnTileMined` per
floor, so this added **no new scene wiring**. `remainsPrefab` is optional and
null-safe: without art the beat still plays, it simply has no sprite.

**Key files:** `Save/CoreMemory.cs`, `Save/TutorialFlags.cs`
(`Lived`, `AffinityFlags`), `Save/Persistence.cs` (the trim),
`Save/DungeonSaveData.cs` (`prologueFlags`, `descendantsDispatched`, the three
`rest*` flags), `Save/DungeonSaveController.cs`,
`Ceremony/CeremonyController.cs`, `Wisp/WispScript.cs` (thirty-one lines in
total for this entry), the seven echo call sites,
`Adventurer/PrologueHouses.cs`, `Adventurer/AdventurerSpawner.cs`
(`TryDispatchDescendants` / `DispatchDescendants`),
`Floors/FloorFeatureSaveData.cs` (`restCells`),
`Floors/TerrainFeatureGenerator.cs` (the uncarved pocket), and
`Gameplay/BuriedRemainsController.cs` (arming, dawn murmur, reveal).

---

## 35. Monster Mutations (Bestiary upgrade line)

Status: BUILT. Verified: pending smoke test

The Bestiary path gains its upgrade line -- the trapwright pattern applied
to monsters. Two stacked nodes: mutation_1 "The Shaping of Flesh" (t3, 25
pts / 3d, prereq bones_in_iron, pattern CuredLeather) and mutation_2 "The
Perfected Strain" (t4, 45 pts / 4d, prereq mutation_1, pattern RunedCrystal
-- the Epic band's first research consumer). Both Always-visible under the
DK2 name rule; no affinity, no cross-path edge, matching trapwright.

MonsterMastery (static, Monster/) is the read point. Tier I: damage x1.15,
damage taken x0.9. Tier II: damage x1.3, damage taken x0.8, move speed
x1.1. Reach was considered and DROPPED: attack range feeds the combat state
machine's spacing decisions and cannot be verified off-screen. Values are
cached and recomputed on UnlockState.OnChanged because the speed multiplier
sits on the per-frame movement path (the EntityAnimationDriver hash-guard
lesson); TrapMastery stays uncached because fire time is rare.

Consumption sites, all gated !IsWild so wild monsters AND invaders are
excluded (the trapworks wild ruling): both strike computations
(FireProjectile and DealAttackDamage -- the projectile payload carries the
mutated figure, so a dead shooter's bolt still lands it), TakeDamage (the
damage-taken multiplier, applied to incoming damage rather than maxHP so it
is retroactive for monsters already alive), and EffectiveMoveSpeed.
Mutations stack multiplicatively with roomDamageMultiplier, the Trophy Hall
global buff and the crowd penalty. The damage-taken multiplier also softens
friendly trap wounds (a Fireball Rune burst hits everyone in it); accepted
as flavour-consistent. Nothing is excluded from scaling, unlike
trapwright's capture-hold carve-out -- subdue-on-defeat is adventurer-side,
so capture is untouched.

Key files: Monster/MonsterMastery.cs, Monster/DungeonMonster.cs,
Editor/TechContentGenerator.cs (regenerate via Dungeon Core menu after
pulling).

---

## 36. Built Walls and the Sealed Way

Status: BUILT. Verified: pending smoke test

The player can raise walls on their own claimed ground, and the world answers
when those walls cut the mouth off from the heart. One build mode, one new
component, one new adventurer state, and the first time the spawn loop has
ever actually read the reachability watchdog.

**Building.** `BuildMode.BuildWall` (Build sub-menu, "Wall"; the scene's
serialised `buildEntries` list predates the entry, so `ActionBarHUD.Start`
appends it at runtime when absent -- no Inspector step). Clicks are RESOLVED
(`ResolveBuildTarget`, the `ResolveMineTarget` mirror): a click on open
ground with open air above is the wall's visual bottom -- the solid lands
two north (`click + (0,2,0)`) and the face drapes the clicked pair -- while
a click on or against existing solid (a cap, a draped face, or the open cell
just above a cap) grows that column northward, targeting the first open
cell above it (walk bounded at 8; beyond that refuses "The rock runs too
deep to crown"). The v1 geometry was click-plus-two ONLY, which made
bottom-up building impossible rather than merely awkward: the cells above
an existing cap are open floor in the data, but every candidate click's
fixed footprint contained the existing wall. Validation covers the target
plus only the OPEN cells its new face drapes (up to two south, stopping at
the first solid -- already-solid neighbours impose nothing, which is what
makes stacking a one-cell check), and the ghost draws exactly that many
face slices. Single click builds one column; drag paints a run, one column
per cell entered; there is deliberately no box gesture. Cost
`buildWallManaCost` (serialised, default 10 -- 2x the dig) is spent only
after validation passes; refusals route through the standard `RejectAt` ->
`BuildFeedback.Reject` popup with per-cause reasons (influence, core cell,
water, doorway, furniture / chest / trap / stairs / spawner / room anchor,
registered room footprints, anything living stood in the footprint, the
column-walk bound).

**The ghost** (this also ships polish item p, walls only). Three translucent
sprites -- lower face, upper face, cap -- pulled live from the floor's own
`CaveWallRenderer` via the new `TryGetGhostColumnSprites` (first plain
straight variant; falls back to flat quads until the sheet is sliced), tinted
by validity, with the mana price floating over the cap on the TMP default
font. Hover validity is the full placement check, so the ghost never shows
green where the click would refuse.

**The mechanism.** `TileInfluenceManager.UnmineTile` is the exact reverse of
`MineTile`: the cell leaves `minedTiles`, stays claimed (claimed solids cap,
so the wall paints with no further plumbing; mana-per-claimed-tile is
untouched), and `OnTileCountChanged` rebuilds the renderer. The build path
pokes `ReachabilityDirector.MarkDirty()` DIRECTLY (Appendix D): the watchdog
only subscribes to `OnTileMined`, which un-mining never fires. Terrain: the
cell is retyped `Stone` via `ApplyFeatureOverride` and recorded in
`FloorFeatureSaveData.builtWallCells` (additive, the `pavedRoadCells`
precedent -- `patchOverrides` does not serialise); `ApplyRuinsOverrides`
re-applies the retype on load, so a built wall renders stone and re-mines at
Stone resistance after a reload. Re-mining IS the removal path -- Demolish is
untouched -- and list entries are never pruned on re-mine (a Stone override
under open floor is inert).

**The seal clock.** `SealPenaltyController` (persistent managers GameObject;
no references) watches `RouteToCoreOpen`. When the route severs it records
the absolute in-game day (`CurrentDay + CycleProgress01`; 0 = not sealed, and
days start at 1, so an old save's missing field deserialises to clean); after
`graceDays` (serialised, default 1) the core's regeneration is replaced by a
drain of `sealDrainPerSecond` (serialised, default 3) to zero --
`DungeonCore.RegenerateMana` reads `ManaSealed` directly, same Appendix D
reasoning. Absolute-day storage means save/reload cannot reset the grace
window. The figure rides `DungeonCoreSaveData.sealStartDays`. One Critical
Threat alert and one wisp line fire when the drain begins; the watchdog's own
severed / reopened lines are unchanged. Mana is the ONLY penalty: XP drain
was considered and rejected because the 1..26 ladder's tier boundaries grant
irreversible things (stair credits, floor unlocks, 19A audiences) and
de-levelling across one either claws back a built-on floor or hands out
free re-earned credits.

**The witness party.** The spawner's Update never read the watchdog before --
only the wave-forecast HUD property did (`SpawningActive`), the
declared-but-never-wired gate -- so a sealed dungeon kept spawning parties
whose empty `FindPath` fell straight through `FollowPath` into
`OnReachedDestination`: blocked pilgrims "arrived" at a core they never
reached. Now: while severed, the animal and commoner stages spawn nothing;
once `WaveStage.Adventurers` is current, exactly one party a day spawns
(`TrySpawnSealLoiterParty`, normal `RollType` composition, no flag). Any
adventurer in `MovingToCore` whose path comes back empty while the route is
severed -- and who is genuinely far from the goal, because `FindPath` returns
empty on BOTH failure and start==goal -- enters the new `Loitering` state
(enum value appended): walks to the reachable cell nearest the heart, drifts
there on the loose-muster pattern, and polls the watchdog about once a
second; on reopen, `BeginAdvance` resumes the originally-rolled goal, so the
ones at the gate come in first. The same hook fires when a stair target is
unreachable, replacing the old stand-forever stall. Loiterers keep normal
combat, retreat and banter rules; `AdventurerBanterManager` swaps a
`SealLoitering` speaker onto the new `BanterLines.Blocked` /
`BlockedPairs` pools at unchanged cadence and pair odds.

**Cross-floor severance (follow-up, SHIPPED).** `Recompute` no longer floods
each floor from its own terrain centre (which made every non-core floor's
overlay meaningless and left the watchdog blind below floor 0). It seeds the
CORE floor at the heart itself -- the core object's position, authoritative
over the terrain centre because the heart can be relocated -- and spreads
through stair pairs, matched by the SAME CELL on the linked floor exactly as
`HandleStairTraversal` matches them, worklist-bounded by each floor's own
reachable set. A half pair (no matching stair on the linked floor) carries
nothing. `CheckSevered` then reads floor 0's set wherever the core lives,
with one guard: an empty surface set is a verdict only once the core floor's
own flood has ground under it, so bootstrap never reads as severance. Two
direct pokes keep the graph honest without a dig: `DungeonStairs.Initialise`
(a new stair changes the route with no `OnTileMined`) and
`FloorManager.SetCoreFloor` (relocation re-roots the whole web). The seal
clock, the witness party, the loiter state and the mine overlay all keyed
off `RouteToCoreOpen` already, so they lit up with no further changes --
and `BeginSealLoiter` now anchors at the stair the party was making for
when one is set, so a mid-descent party mills at the wall in front of the
stairs rather than at the terrain centre of an intermediate floor. Verified
headless (`Tools/sim_crossfloor_sever.py`): open route through stairs,
seal on the surface leg, seal on a deeper leg, redundant stair pairs,
three-floor chains, and disconnected same-floor pockets fed by separate
stairs.

**Known limits, inherited and chosen.** (1) Only `MovingToCore` (and the
stair branch) detects blockage; an observer in `MovingToRoom` whose room is
sealed off keeps the pre-existing empty-path fallthrough. (2) The wildlife
spawner is not gated -- animals wandering up to a wall is harmless and it is
a separate system. (3) Loitering is not persisted: a save under a seal
restores members via the normal pending-restore path and they re-derive the
loiter on their first failed path. (4) The witness party spawns on floor 0
and loiters at the first blockage it meets; it does not spelunk past open
legs of a partly-sealed route to find the actual wall on a deeper floor --
it stalls wherever its own pathing stalls, which is the honest behaviour.

---

## 37. Random World Events (The World's Weather)

Status: SHIPPED. Verified: Complete.

The deferred framework from entry 18, revisited and shipped: a data-driven
dispatcher so a new world event is an asset entry, not a component.
`Gameplay/WorldEventDirector.cs` (one component beside the threat managers)
self-populates from `Resources/Events/World` (authored by Dungeon Core ->
Generate World Events, `Editor/WorldEventContentGenerator.cs`) and rolls at
dawn: tick active timed effects, burn the global cooldown, gather eligible
events (minDay / minNotoriety / minRating gates, per-event cooldown, not
already active; climax suppression strips HOSTILE-flagged events only), roll
the daily fire chance (0.25), then one weighted draw. Global cooldown 3 days
between any two events; the tuning lands 4-5 events per 30 eligible days,
validated by `Tools/sim_world_events.py` (14 checks: gates, both cooldowns,
no self-overlap, weighted proportions, cadence band, determinism, save/load
mid-effect without refire, expiry, hostile-only suppression). The C# mirrors
that file's dawn ordering exactly; rerun it when the tuning or the ordering
changes.

**Deliberately greenfield.** The four bespoke threats (`HolyOrderStrike`,
`MercenaryContract`, `NobleRetaliation`, `WildMonsterEvent`) are untouched:
each is a tuned state machine of its own, and folding them into a generic
registry would rewrite tuned behaviour for no player-visible gain. The
Wandering Merchant keeps its own arrival controller. The `hostile` flag on a
definition exists so a future assault-shaped event honours
`SuppressMidGameThreats`; none of the v1 trio carries it.

**Effects are the honest data boundary.** `WorldEventEffectKind`
(append-only -- it serialises into .asset files) names what an event does;
the director's `Fire` switch is the single place kinds become behaviour. A
new event on an existing kind is one generator spec row plus a regenerate; a
new kind is one enum value plus one switch case. Timed kinds hold a
multiplier for durationDays (the fire day counts as the first); the
per-event cooldown is clamped to at least the duration so an effect can
never overlap itself, and same-kind effects from DIFFERENT events stack
multiplicatively by design.

**Consumers read two cached statics** that default to 1 with no instance, so
the hooks are inert until events exist: `RespawnRateMultiplier` multiplies
into `RespawnTicker`'s per-spawner tick beside the room-effect multiplier,
and `CivilianWeightMultiplier` multiplies beside
`DungeonAppealLedger.CivilianMultiplier` at BOTH intent-weight sites in
`AdventurerSpawner` (roll + foresight) so WavePreviewHUD stays honest -- the
appeal ledger's same-sites rule applied again.

**The v1 trio:** `we_murrain` (day 15+, cooldown 10, respawn x0.5 for 3
days; Threat/Warning), `we_pilgrim_surge` (day 10+, cooldown 8, civilian
x1.5 for 2 days; Discovery/Info), `we_tremor` (day 6+, cooldown 6, weight
1.5, instant 40-80 gold; Discovery/Info). No autosave on fire: these are
weather, not assaults -- the threat components autosave because a raid is a
run-defining moment.

**Persistence:** `WorldEventsSaveData` (additive on `DungeonSaveData`;
parallel lists, JsonUtility takes no dictionaries) carries the global
cooldown, per-event lastFiredDay / timesFired keyed by STRING id (the asset
name, never an enum index -- a retired event's entry is harmless on load,
and an active effect whose asset is gone is dropped), and the active effects
with days remaining. Restore recomputes the multipliers immediately, because
`DayNightCycle.LoadSaveData` deliberately never re-fires OnDayStarted --
without that a saved murrain would load cured. New-game reset rides
`DungeonSaveController.InitializeNewGame` beside the merchant's, since the
director carries scheduling state exactly as the merchant does. Diagnostics:
a log line per fire and per expiry, and "Print World Events" in `Commands`.

**Scene setup is two manual steps** and both fail silently if skipped: the
`WorldEventDirector` component goes on the persistent manager GameObject
beside the threat managers, and Dungeon Core -> Generate World Events must
run once to author the three assets. No component or no assets means no
events and no error -- the wisp-asset lesson.

**Key files:** `Gameplay/WorldEventDefinition.cs`,
`Gameplay/WorldEventDirector.cs`, `Editor/WorldEventContentGenerator.cs`,
`Monster/RespawnTicker.cs` (one-line hook),
`Adventurer/AdventurerSpawner.cs` (the two civMult sites),
`TESTING/Commands.cs`, `Save/DungeonSaveData.cs`,
`Save/DungeonSaveController.cs`, `Tools/sim_world_events.py`.

**Rejected:** migrating the shipped threats (above). An "earthquake vein
reveal" as first designed -- no mineral veins exist anywhere in the
codebase, so it was a resource system wearing an event's clothes; the
tremor's instant gold grant is its honest reshape. An abandoned free-loot
chest -- chests are player-placed bait feeding the Treasure-Hunter tier
scan and the appeal loop, and a world-spawned one needs a placement solver
while muddying a tuned economy.

---

## 38. Core Spells (Active Abilities)

Status: SHIPPED. Verified: 2026-08-10.

**What a spell is.** A working cast at a cell on the active floor, acting
over a radius, priced in mana and held back by a per-spell cooldown. The
shape is identical across the whole roster on purpose: the power budget is
then four dials -- radius, duration, magnitude, mana -- rather than an
argument about which verb is stronger, exactly as the trapworks balances on
mana / capacity / damage / cooldown. A spell whose shape has to be
special-cased in `SpellCaster` needs its reason written into
`SpellDefinition` alongside it.

**The two channels, and the fiction that splits them.** *A core cannot
research its god's own power; it is given.* The Sorcery research path holds
only the NEUTRAL craft -- what any core could work out for itself. The six
affinity workings come from the god of the core's own type, handed down at a
tier-up audience and deepened at the two after it (entry 19A, part 2 of this
arc). This is why the Sorcery lane is deliberately short: it is not a thin
path, it is half a path, and the other half is not researchable by design.

**The neutral trio (SHIPPED).** Lash (8 mana, 1.5s, radius 1.4): damage 12
plus a hurl of 1.2 outward from the cell. Knit (15 mana, 6s, radius 2.2):
heals 25 to the dungeon's own only -- a wild in the ring is very often the
thing your monsters are fighting. Call to Arms (10 mana, no cooldown, radius
9): every one of your monsters in the radius has its spawner given the cast
cell as an Attack-Here target, through the shipped `SetAttackTarget` path, so
a rallied monster reverts to its underlying order mode on arrival with no new
order state. Rally gathers by MONSTER rather than by spawner: the spawner is
a fixed point its occupant may be two rooms from, and that wandering garrison
is exactly what the spell exists to call back.

**The six affinity workings (SHIPPED).** One per core type, `CoreAffinity`
exclusive by the trapworks type-lock rule, so a run only ever holds one of
them. Every one shares the cast-at-a-cell-over-a-radius shape, so the balance
argument is a set of dials rather than a debate:

| Core | Working | Verb | Mana / cd |
|---|---|---|---|
| Fire | The Coals Wake | monsters in radius strike 35% harder | 30 / 14s |
| Water | Undertow | intruders in radius are PULLED to the cell | 22 / 9s |
| Earth | Root the Stone | monsters in radius take 30% less | 26 / 12s |
| Air | Second Wind | monsters in radius move and swing 30% faster | 24 / 11s |
| Dark | Terror | intruders in radius rout on the spot | 34 / 20s |
| Light | The Buried Sun | intruders in radius take 30% more from everything | 28 / 13s |

**No working repeats its own trap's verb.** The six elemental traps already
own the six elemental bursts (entry 31), so a spell that delivered the same
burst on demand would be a worse copy of a shipped system. Traps hurt
intruders where they walk; spells act on the fight that is happening now.

**The pairs are balanced as pairs, not one at a time.** Fire, Water and Dark
each COMPOUND with their own trap -- Fire is already the heaviest burst, Water
already the control lock, Dark already the deny-without-killing core -- so
their workings are deliberately the quiet half: Fire carries the shortest
duration of the three boons at the highest cost, and Terror is the most
expensive thing in the roster on the longest cooldown. Earth, Air and Light
counterweight their traps instead and can afford to carry more. Do not
"balance" one of these in isolation; the unit is trap plus working.

**Undertow needs no new primitive.** A pull is `ApplyKnockback` with the
origin mirrored across the target cell, and the force is clamped to the
distance so nothing is flung past the centre. It is the only pull in the game
and it exists to set up everything else you own.

**Terror pays for itself.** A routed party leaves ALIVE: no notoriety from
the kills you did not make, their loot walks out with them, and
`AlignmentSystem.OnAdventurerLeftAlive` shifts the core good. Driving off a
party you cannot beat is a real option with a real price rather than a free
win. The Suicidal are exempt -- a death in the dark is what they came for,
and their retreat threshold is already -1 by ruling.

**Boons are transient and never stack.** `MonsterBoons` is added on demand,
holds three (multiplier, expiry) pairs, and has NO Update: the getters compare
against `Time.time` when read, so an expired boon costs nothing and a dungeon
full of them costs nothing per frame. Recasting takes the stronger multiplier
and the later expiry rather than multiplying them, because stacking would make
spamming one working the correct play at high mana -- exactly what the
cooldowns exist to prevent. Read as one more factor in the chains that already
exist: `attackDamage * roomDamage * globalDamage * crowdDamage * mastery *
boon`. Both the melee and the ranged damage sites take it; they were
byte-identical and are edited as a pair.

**The god gives, and then deepens.** The affinity working is not researchable.
It arrives at the SILVER audience and deepens at Gold and Diamond; the God
audience grants nothing new, being the ascension ceremony and already
carrying its own weight. Deepening widens RADIUS and lengthens DURATION only,
never magnitude: the god's hand reaching further reads as a god, and a bigger
number reads as a stat bump. Tier 2 is radius x1.25 and duration x1.3; tier 3
is x1.5 and x1.6.

**The grant is authored beside the words that announce it.**
`DivineAudienceScript.Insert` carries `grantsUnlockKey` and `grantLine`
alongside the god's own line for that tier, so the mechanical effect and the
sentence that declares it cannot drift apart -- entry 19A's stated principle,
applied. `Validate()` now faults a deity missing a grant on Silver, Gold or
Diamond, because a missing grant is a working the player silently never
receives. The keys are bare `spell.*` strings that no node owns, the canon 28A
precedent for a thing that can only be given.

**Grants land on ARRIVAL, not on completion** -- beside `MarkHeld`, and for
the same reason the ledger gives: an audience withdrawn from early still
happened, and a player who skips must not lose the power. `UnlockState.Unlock`
is idempotent, so a forced replay re-grants harmlessly.

**Lash is not a projectile.** A travelling bolt was designed and dropped. A
projectile needs an origin; the only sensible origin is the core; the core is
routinely hundreds of cells from the fight, so
`DungeonProjectile.HasLineOfSight` would refuse nearly every cast and the
spell would read as broken. The core's will arrives where it is pointed.

**Nothing is billed for air.** `SpellCaster.Resolve` returns whether it
actually did anything, and mana and cooldown are stamped only on a true. A
cast into an empty room is refused with feedback rather than charged.

**The pause rule (stated here; APPLIED across the whole surface by canon
39, which is where the audit actually closed).** *Pause permits
selection, navigation and ORDERS; it forbids anything that spends mana or
changes world state.* Call to Arms is an order -- the right-click Attack-Here
path it rides has always run above the pause gate -- so it carries an
EXPLICIT `castableWhilePaused` toggle and casts through a held clock. Every
other spell refuses, as mining, walling and placing already refuse. Cast mode
may still be ENTERED while paused and the radius ghost still draws, so a shot
can be lined up against a frozen board and unpaused into. The toggle is a
serialized bool rather than a property derived from the effect enum, so a
future order-spell cannot become silently pause-illegal.

**Access.** `BuildMode.CastSpell`, appended AFTER `None` in the enum -- the
scene serialises `ActionBarHUD.buildEntries[].mode` as the ORDINAL, so
inserting anywhere earlier would re-point every existing sub-menu entry. A
fifth CAST tab is a scene Button assigned in the Inspector beside the other
four. It was a runtime clone of a sibling tab at first, to spare a scene
edit; that trade came off once the spell row became a designed panel, since
a tab that can be placed and styled beats one inheriting whatever Summon
happens to look like. There is deliberately NO clone-if-unassigned fallback
-- "empty means make one for me" reads in the Inspector exactly like "not
filled in yet", which is the ambiguous-default trap; an unassigned tab is a
named fault from `ValidateSpellRowWiring` instead. The tab lights on
`SpellBook.AnySpellKnown` rather than on the trunk node, so a god's grant is
reachable by a core that never researched the trunk, and its visibility is
driven from code -- leave the button ACTIVE in the scene.

**Content lives in Resources.** Spell assets sit in `Resources/Spells` and
`SpellBook` loads them itself (the `WorldEventDirector` precedent) -- no
registry asset, no Inspector drag, no empty-slot failure mode. Authored by
Dungeon Core -> Generate Spell Content; the generator is authoritative the
way `TechContentGenerator` is for nodes. `SpellDefinition.SpellEffect` is
APPEND-ONLY: it serialises into the assets as an int.

**Cooldowns are transient.** Kept in a static ledger stamped from `Time.time`,
so they pause with the clock as trap cooldowns do, and are never serialised --
the section-30 ruling that drops a mid-flight bolt and a mid-windup telegraph
covers a half-elapsed cooldown for the same reason. Cleared from
`DungeonBuildController.Awake` so a load never inherits stale timers.

**Sorcery research spine (SHIPPED).** Three nodes. The First Spark (t1, 20
pts / 3d, prereq Whispers of Intent -- the cross-path demand that makes
Sorcery cost breadth), then The Drawn Breath (t2, 30 / 3, prereqs First Spark
+ Shambling Dead) and Call to Arms (t2, 25 / 2, prereqs First Spark +
Standing Orders). The two reserved trader books are wired to the first two:
Primer of the First Spark at 220g and The Drawn Breath at 400g, through the
shipped `GrantNodeFully` book channel.

**Costs are anchored, not chosen.** Sized against the shipped economy so the
curve matches the trapworks: a Bronze 1 core holds 100 mana at about 1/s,
mining granite costs 20 a cell, a wall 10, a crossbow trap 16, a fireball
rune 22. Real at Bronze, small change by Gold -- the shape the traps already
have.

**The spell row lives in `ActionBarHUD`,** as a THIRD sub-menu beside Build
and Mine rather than a component of its own. It is the mine-gesture pattern
exactly: one serialized `spellEntryContainer`, entries instantiated from the
SHARED `submenuEntryPrefab`, and the container IS the panel -- showing it is
toggling its GameObject. Two standalone versions were written first and both
replaced: a self-built canvas (layout is a design decision and belongs in the
scene) and then a separate scene-wired `SpellSelectionUI` (a second panel and
a second entry prefab to produce a row that should look identical to the
other two). The merged form costs ONE container in the scene and inherits
the bar's look for nothing.

The row lists every castable working at once so cooldowns read across the
whole set mid-fight, and rebuilds on `SpellBook.OnRosterChanged` so a
completed node or a god's grant appears without a reopen. Entry labels carry
`(1)`-`(9)` matching the tab convention (`MINE (M)`), and those number keys
select while the row is open -- guarded on the row being open so the digits
stay free elsewhere. The detail label follows the POINTER rather than the
selection, appearing on hover and hiding on exit, so the row stays a clean
strip of names. The CAST tab toggles on ROW-OPEN state, not on which tab is
lit, because cast mode can be entered by hotkey without the row ever opening
-- the same reason the Mine tab does it that way. Default key **Q**.

A blank container opens an empty row and reports nothing on its own, which
has cost this project test cycles before, so it is refused:
`ValidateSpellRowWiring` names every unassigned slot and
Commands -> Validate Spell Picker Wiring runs it.

**Rejected:** a Reveal Area spell (fog is not a stored set -- `RevealTile`
nulls a tile and a load re-derives the revealed area from claimed + mined, so
the spell would silently re-fog on reload and would need a save field of its
own); one `BuildMode` per spell (enum bloat -- the `PlaceTrap` +
`SetSelectedTrap` pattern carries the whole roster on one value); a spell row
parallel to the tabs (new layout and new highlight ownership for no gain over
a fifth tab); cooldown-free mana-only pricing (a Diamond core holds 3840 mana
and would machine-gun the cheap spells).

**Bug fixed riding this arc.** The trader's "Whispers Set to Parchment" sold
for 400g against `nodeKey = "tech.oracle_intent"`, but that node carries
`overrideKey = "oracle_chamber"`, so its real key is `oracle_chamber`.
`GetByKey` returned null, `GrantNodeFully(null)` returned silently, and
`WanderingMerchantController.TryPurchase` had already taken the gold -- and
because `IsOwned` tested the same dead key, the book restocked forever and
could be bought again. Corrected to the real key. The lesson generalises:
a book's `nodeKey` must be the node's KEY, not its id, and `overrideKey`
makes those differ.

**Key files:** `Gameplay/SpellDefinition.cs`, `Gameplay/SpellBook.cs`,
`Gameplay/SpellCaster.cs`, `Monster/MonsterBoons.cs`,
`DungeonCore/DivineAudienceScript.cs`,
`UI/DivineAudienceUI.cs`,
`Editor/SpellContentGenerator.cs`, `DungeonCore/DungeonBuildController.cs`,
`Data/Keybinds.cs`, `UI/ActionBarHUD.cs`, `Editor/TechContentGenerator.cs`,
`Editor/TraderStockGenerator.cs`, `TESTING/Commands.cs`.

---

## 39. The Pause Rule (Availability Audit)

Status: SHIPPED. Verified: 2026-08-10.

**The rule.** *Pause permits DECIDING. It forbids ACTING.* Deciding is
selection, navigation, browsing, orders, and commitments that touch nothing
but a ledger. Acting is anything that reaches an entity standing on the board
or a cell of the tilemap: placing, removing, spawning, damaging, healing,
retyping, channelling.

This supersedes the first wording, in canon 38, which forbade "anything that
spends mana or changes world state". That sentence was stated and never
applied, and it does not survive contact: research spends and does not act,
trade spends and does not act, while a prisoner's release spends nothing and
plainly does. The test is what the action REACHES, not what it costs.

**What the audit found.** The rule existed in eleven places and disagreed with
itself in three of them. Five commits ran freely while held -- research, trade,
the crypt raise, the three prisoner verbs, the three caravan verbs. Three
openers refused while held even though opening is navigation -- the room anchor
click, the trap panel hotkey, the crypt corpse click. Two of those were
INVERTED: pausing before you clicked locked you out, while pausing after left
every button behind them live. The crypt was the sharpest -- pause mid-raid,
raise defenders, unpause.

**Gate the action, never the opener.** Every panel opens, browses and inspects
while the world is held; only the button that commits refuses. This is the
whole shape of the fix and the reason the inversions cannot recur. `PauseGate`
(`Data/PauseGate.cs`) carries it: `Held`, `CanAct(out reason)`, `RefuseAt(pos)`
for anything with a position and `RefuseAtCore()` for a bare HUD button. A
refusal toasts in the wisp's voice rather than failing silently, and where a
button can be greyed ahead of the click it is (the crypt raise, the bribe).

**Research commits while held.** It is pre-paid, refunded in full on cancel,
and its progress runs on the day clock, which stops with everything else -- so
committing while frozen confers no advantage whatever. It is a planning screen
and it behaves like one. Trade likewise: a purchase is a ledger swap, and the
goods arriving are its consequence, not its act. Both are named here rather
than left to be re-derived, because both look like spends and neither is one.

**A refusal never burns a decision.** The caravan verb refuses BEFORE the panel
closes, so the choice survives it and the wagon stays halted -- the same ruling
that already protected a misclick from spending the one verb.

**The inspector's notice restores what it interrupted.** `Announce` records
whether the player was already holding the world; `Dismiss` unpauses only when
the notice was the thing that took it, and through `PauseController.UnpauseGame`
rather than `SetNormal`. The old call demoted a player running at 5x and
force-unpaused a player who had paused deliberately -- the identical defect
canon 19A named for the divine audience, in a second place. The bribe itself is
forbidden while held: both fuses it pays off run on scaled time, so the notice
pauses in order to be READ, and the coin is offered once the world runs again.

**Ghosts draw while held.** The mine highlight and the wall ghost both hid
themselves while paused, so a frozen board could not be planned against. They
now draw, following the spell ghost's precedent from canon 38. A dig target you
cannot see is a dig you cannot plan, and planning on a held board is the entire
point of an active pause.

**The hover cost preview (polish item p) is finished.** It shipped for walls
only, as a price floating over the ghost cap. It now covers mine (at the
effective claim multiplier, not the authored base), build wall, trap, chest,
furniture, stairs, spawner, spell, and demolish -- the last showing the half-mana
refund each type's `RemoveByPlayer` hands back, and showing nothing at all on a
room anchor, which refunds nothing. Entrance and the order modes cost nothing
and so display nothing. One label, owned by `UpdateCostPreview`, parented to the
build controller rather than to the wall ghost it outlived; unaffordable reads
red, a refund reads green. In Mine mode it also carries the standing dig queue's
cell count and total mana, priced per cell on its own floor's multiplier -- the
queue is precisely what gets built on a frozen board and its price is invisible
anywhere else.

**This entry's first pass was partial, and canon 40 completed it.** The sweep
that produced this rule was drawn from the resource-spending call sites plus the
panels that happened to be read alongside them -- not from an exhaustive sweep of
`PauseController.IsGamePaused`. It fixed four over-gated openers and missed seven
more of the identical class, listed in canon 40. The lesson is recorded here
rather than quietly corrected: an audit bounded by the evidence already gathered
is not an audit, and the exhaustive grep costs minutes.

**The register lives in code.** `Commands` -> Print Pause Audit prints the live
hold state, the rule, and every reachable action with its ruling. A new action
belongs in that table the day it is written; the audit existed because eleven
files had to be swept to discover what the rule even was.

**Key files:** `Data/PauseGate.cs`, `Data/PauseController.cs`,
`UI/TimeScaleController.cs`, `DungeonCore/DungeonBuildController.cs`,
`UI/InspectorArrivalPopup.cs`, `UI/BribePromptUI.cs`, `UI/CryptRaiseUI.cs`,
`UI/PrisonerPanelUI.cs`, `UI/CaravanActionPanel.cs`, `UI/TrapPanel.cs`,
`Room/RoomAnchor.cs`, `Room/RoomTypePickerUI.cs`, `Room/CryptController.cs`,
`Floors/DwarvenCaravanController.cs`, `TESTING/Commands.cs`.

---

## 40. The Panel Button Row (and the Completed Availability Sweep)

Status: SHIPPED. Verified: 2026-08-11.

**The problem.** Eight window actions had no on-screen affordance whatever:
traps, alerts, loot, the journal, known parties, factions, research, and
recentring on the core. A player who never opened the keybind screen could not
discover the research tree or the bestiary. That is most of the game's UI
surface hidden behind unlabelled keys.

**The row.** `UI/PanelButtonRow.cs`, one button per action, code-built at Awake
from a single scene anchor. Deliberately separate from `ActionBarHUD`: that bar
selects a TOOL and this row opens a WINDOW, and mixing them would make the
action bar's selected-tab highlight meaningless. Each button shows its bound key
underneath, pulled from `Keybinds.DisplayName` and refreshed on
`Keybinds.OnRebind` -- the row teaches the hotkey rather than replacing it. A
button with no icon assigned falls back to its text label, the same degradation
`ActionBarHUD` submenu entries already use, so a temp-art row reads as eight
names rather than eight blank squares.

**Built in code, not wired in the scene.** `AlertHudButton` is the precedent for
the behaviour and is scene-wired, but replicating that eight times is eight sets
of Inspector references and eight silent failure modes. Silent failure is
actively designed against in this project, so the row builds its own children
and takes only sprites from the Inspector.

**Locked buttons are hidden, not greyed.** A greyed button for a system the
player has never heard of is both a spoiler and a dead click. Alerts gates on
`tech.alerts`; known parties and factions both gate on `tech.known_parties`;
the rest are always shown. Gating re-applies on `UnlockState.OnChanged`.

**Alerts alone carries a badge.** It is the only one of the eight with an
existing unread count. Badges for the others would mean inventing the counters
first, and an invented counter is a number nobody can trust.

**Recentring is not a window.** It is momentary -- no open state, no badge, no
gate -- and calls `DungeonCameraController.RecenterOnCore`, which was already
public and documented as callable by menus.

**The completed sweep.** Canon 39 fixed four over-gated openers and left seven
more of the same class, because its scope was drawn around the evidence already
gathered rather than an exhaustive grep. The row forced the issue: a button that
visibly does nothing while the world is held is a bug report, where a dead
hotkey is merely invisible. The seven, all now ungated:

- `UI/LootPanel.cs`, `UI/KnownPartiesPanel.cs`, `UI/FactionPanel.cs`,
  `UI/AlertHistoryPanel.cs` -- window hotkeys refused while held, although the
  journal and the research tree never carried a gate. That inconsistency was
  the tell that these were written case by case rather than to a rule.
- `DungeonCore/InfluenceRingRenderer.cs` -- the overlay is a read-only lens and
  changes nothing on the board. Held is exactly when a player wants to check
  their reach before committing to a dig.
- `UI/AdventurerInspectController.cs` -- clicking an adventurer to read its
  stats. Inspection is the paradigm case for an active pause and it was refused.
- `UI/ActionBarHUD.cs` -- the Mine, Build, Summon and Push tab hotkeys.

**Mode entry is pause-legal.** Selecting a tool places nothing, and
`DungeonBuildController`'s dispatch gate still refuses every acting handler.
Cast has been enterable while held since canon 38, so four of the five tabs
disagreed with the fifth. The consequence was worse than an inconsistency: the
hover cost preview keys off `CurrentMode`, so it was only reachable if the mode
had been entered BEFORE pausing -- half of a shipped feature, invisible. Only
the four hotkeys moved above the gate. The Esc-cancel block below it stays where
it is: it coordinates with `PauseMenuController`'s chain, and freeing it is a
separate question from window availability.

**What the sweep confirmed as already correct**, and deliberately did not touch:
the journal, research, recenter, the minimap's click-to-pan and the speed keys
carry no gate and should not; `HotbarController` gates correctly because it
calls `item.UseItem()`, which acts; every simulation tick gates correctly; and
the guards in `Data/MenuController.cs` and `Overworld/PrologueSettingsHotkey.cs`
stay, because "do not open over another pause holder" is a different concern
from availability and answering it with the availability rule would break both.

**Key files:** `UI/PanelButtonRow.cs`, `UI/AlertHudButton.cs`, `Data/Keybinds.cs`,
`Gameplay/UnlockState.cs`, `UI/AlertsLog.cs`, `UI/ActionBarHUD.cs`,
`UI/LootPanel.cs`, `UI/KnownPartiesPanel.cs`, `UI/FactionPanel.cs`,
`UI/AlertHistoryPanel.cs`, `UI/AdventurerInspectController.cs`,
`DungeonCore/InfluenceRingRenderer.cs`, `DungeonCore/DungeonCameraController.cs`.

---

## 41. Spell Charges

Status: SHIPPED. Verified: 2026-08-11.

**What a charge is.** One banked casting of a working the core does not hold.
It reuses the entire shipped cast surface -- the CAST tab, targeting, the radius
ghost, the hover cost preview, cooldowns and the pause rule -- and adds exactly
one thing: an integer per spell id that decrements on use.

**Why this shape and not an item system.** The original backlog framing was
"specials / one-shot consumables", which needs an inventory the dungeon side has
no concept of, a UI surface to hold it, and a targeting path -- three arcs, and a
drift toward direct intervention that the Hand-of-Evil rejection already ruled
against. Charges need none of them. The rule that keeps it honest: IF IT CANNOT
BE CAST AT A CELL, IT IS NOT IN THIS FEATURE. A forged writ that cancels an
Inspector dispatch is a good idea and it belongs to a different arc.

**The problem it actually solves.** Gold had five sinks and twelve-plus sources,
and every `StockType` on the manifest was a permanent grant that `IsOwned` pulled
from stock forever. Nothing in the game could be bought twice. Once the manifest
emptied, gold stopped meaning anything. A charge is the only repeatable purchase
shape available, and it is the only place an effect too strong to sit on a
cooldown can live at all.

**Keyed on spell id, never an ordinal.** Ids are declared stable and never
renamed (canon 38), while `SpellEffect` is explicitly append-only -- so keying
the ledger on an effect ordinal would silently re-key every banked charge in
every existing save the first time a working was added.

**Charges bypass the affinity type-lock, and pay for it.** `IsAvailable` now
returns true for a working the core cannot hold when charges are banked; the
permanent roster keeps its lock, so the core's own signature still means
something. What a borrowed god's power loses is reach: an off-affinity cast
scales radius and duration by `OffAffinityScale` (0.6). RADIUS AND DURATION ONLY,
the same lever deepening uses, and for the same reason canon 38 gave -- a changed
damage number reads as a stat bump and would mean retuning the whole affinity
roster twice over. `IsAligned` is only ever false on a charge cast.

**The permanent grant always wins.** `HeldPermanently` is split out from
`IsAvailable` precisely so the consumption point can ask the narrower question. A
charge is spent only when it is the sole way the core holds the working, which
stops the trap where researching a spell quietly eats the scrolls banked for it.
Surviving charges stay banked against a future core that cannot hold it.

**Nothing is billed for air.** Charges are spent after `SpellCaster.Resolve`
succeeds, alongside the mana and the cooldown -- the same refusal the arc already
makes when a cast finds nothing. A cast that passes availability with an empty
ledger warns rather than silently granting a free casting: the two disagreeing is
a bug worth seeing.

**Persistence is two parallel lists.** `spellChargeIds` and `spellChargeCounts`,
additive and empty on legacy saves, because JsonUtility cannot serialise a
dictionary and the paired-list shape already has precedent in the save data. A
length mismatch takes the shorter of the two rather than throwing: a hand-edited
ledger must not cost the player the rest of the file. The static ledger is
cleared on scene wake alongside the cooldowns, and the load path repopulates it
immediately afterwards.

**The picker is told, not polled.** `SpellCharges` calls
`SpellBook.NotifyChargesChanged` explicitly rather than subscribing an event in a
static constructor that may not have run when the first grant lands.

**Readout.** A working held only through charges shows its remaining count after
the price on its CAST row, never instead of it -- a charge still bills mana and
the row has to say both.

**Testable with no content.** `Commands` -> Grant Spell Charges banks castings of
the first working this core does not hold, so the whole substrate is provable
before a single scroll exists. Print Spell Charges shows the ledger, what is held
permanently, and the alignment penalty applied to each. Building it this way was
deliberate: a substrate proved only by its own content pass gives every
regression two possible parents.

**A charge is a STOCK KIND, not a shop.** `StockType.Charge` is appended (never
reordered; it serialises into both catalog assets as an int) and carries two
fields: the `SpellDefinition` banked and how many castings. `ApplyPurchase`
grants through the same shared switch every other kind uses, so neither vendor
learned anything new about spells. `IsOwned` returns FALSE for a charge and that
is the entire mechanism -- a charge is the only repeatable purchase in the game.

**A malformed charge entry FAILS CLOSED.** An entry with no working, or with a
working whose id is blank, reports OWNED, which keeps it off every shelf.
Failing open would give a row that takes gold in `TryPurchase` and grants nothing
in `ApplyPurchase` -- byte for byte the dead-node book defect from entry 38, which
survived a whole arc unnoticed. `ValidateChargeEntries` errors at authoring time
on top, because dead stock does not error in play: it simply never appears.

**The wagon gets a slot; the shelf does not, and the asymmetry is the point.**
`RollStock` gains a third bucket and one guaranteed charge slot beside the
catch-up slot, and charges are kept OUT of the general pool entirely. Without
that, an entry that is never owned sits in a 4-6 slot roll forever and crowds the
finite manifest out exactly as the manifest empties -- the moment the wagon most
needs to still have books on it. Exactly one a visit: a wagon rolling three
scrolls and one book would be a scroll cart.

The Deep Holds' shelf has NO slot count, so there is nothing to crowd and nothing
to ration, and a rolled slot there would have made the shelf ROTATE on every open
-- worse than a wagon that rotates every few days, and "the shelf does not
rotate" (entry 19, part 2) is the one line separating a shop from a visit. Their
charges simply sit there, and buying one takes it off the shelf until the next
open. This was raised as a locked decision to apply at both vendors and was
OVERRULED on reading `BuildShelf`: the ruling had been written against the
wagon's problem, and the shelf does not have it.

**Which vendor sells what follows the line already drawn.** Entry 19 part 2 says
the merchant sells KNOWLEDGE and the dwarves sell MACHINERY, and powder is
machinery. So: the wagon carries the demos and the relics, and the Deep Holds
carry the two setting charges. `The Keying Course` is Root the Stone moved OFF
the wagon rather than duplicated onto the shelf -- stone that holds under load is
the one affinity working dwarves can plausibly have got by CRAFT rather than by a
god's hand, and the same charge under two names at two vendors reads as a bug.
The cost is accepted and written down: a non-Earth core cannot buy that one relic
until it can descend to floor index 2, where the other five are on the road from
Camp tier. It is also the relic whose effect an Earth core holds natively.

**The manifest.** Demos are priced well under the 220g book that grants the node
outright, so a scroll is never the cheap way to OWN a working -- only the fast way
to try one. Relics are a flat 260g, just above the 240g Reserved-pattern
exclusive, because a borrowed god should be the dearest thing on the wagon and
one number is easier to hold than six; two castings rather than three, because
the affinity type-lock is the rule they break.

| Item | Vendor | Working | x | Price |
|---|---|---|---|---|
| A Borrowed Blow | wagon | Lash | 3 | 80g |
| The Muster Horn | wagon | Call to Arms | 3 | 100g |
| Suture Chalk | wagon | Knit | 3 | 120g |
| Kethra's Ember, Stoppered | wagon | The Coals Wake | 2 | 260g |
| A Jar of the Drowned Mouth | wagon | Undertow | 2 | 260g |
| Vaun's Held Breath | wagon | Second Wind | 2 | 260g |
| The Unlit Hour | wagon | Terror | 2 | 260g |
| Ienna's Splinter | wagon | The Buried Sun | 2 | 260g |
| Coalbed Ash, Sealed | wagon | Ashrise | 2 | 300g |
| The Setting Charge | Deep Holds | Shear the Face | 5 | 300g, Tolerated |
| The Keying Course | Deep Holds | Root the Stone | 2 | 320g, Trusted |

**Only two workings were authored, and eight were not.** The first content pass
proposed a new asset per god: the same six affinity effects at a larger radius,
which is EXACTLY what deepening already does at t2 and t3, and one of them
collided with a shipped `displayName`. The affinity bypass already delivers the
whole borrow-another-god's-power fantasy with zero new assets -- a Water core
buying a scroll of The Coals Wake IS the idea -- so a charge entry points at the
shipped asset and nothing is duplicated. New assets exist only where the EFFECT
is new.

**Ashrise summons TRANSIENTS, and had to.** `MonsterSpawner.TickRespawn` refuses
outright while a threshold-crossed adventurer walks the floor (`isBlocked` via
`FloorIntrusion.AnyOnFloor`), so a working that hastened respawns would do
precisely nothing in the only situation anyone would ever cast it in.
`SpawnTransientMinion` gives a spawner that holds no capacity, never respawns,
self-destructs with its monster and is skipped by `DungeonSaveController` -- so
thralls on the board at a save are gone on the reload, the same ruling section 30
makes for a bolt in flight. `magnitude` is HOW MANY and `durationSeconds` is HOW
LONG, both read through `SpellBook`, so a Fire core gets the god's full measure
and a borrowed cast gets 0.6 of the reach and the hold. They scatter one to a
cell across `DungeonPathfinder.IsWalkable` ground in the ring and fall back to
the cast cell: the pathfinder's own rule is the only honest test of where a body
may stand, and a stack on one cell reads as a bug even when it is not.

The body is `MonsterDef_Coalborn`, a DEDICATED definition rather than the
necromancer's risen list -- a summoning is not a raising, and sharing the list
would tie a god's gift to the tuning of a monster with its own reasons to change.
It is deliberately absent from `MonsterDefinitionRegistry`, which is what the
build picker lists and what the save controller resolves by name, and carries a
`requiredTechKey` nothing grants on top of that. It SHIPS ON THE ASH WALKER'S
BODY: monster stats live on the prefab's `DungeonMonster`, not on the definition,
so its own numbers mean a prefab variant made in the editor. Recorded as debt,
not pretended away.

**Shear the Face iterates, because the frontier rule made it.**
`TileInfluenceManager.MineTile` yields a cell only when it is orthogonally next
to already carved ground (or to a river, which counts as open), so a single pass
over the ring would open the rim nearest the existing dig and leave everything
behind it standing. Each pass re-runs while the pass before it moved anything.
The frontier test is MIRRORED in `SpellCaster` rather than left to `MineTile`,
for the same reason `CanMineCell` mirrors it: `MineTile` BARKS on a refusal, and
a working that just blew a hole in the wall must not also scold the player about
the cells outside the frontier it correctly declined to touch. It only ever opens
CLAIMED, unmined ground -- a faster shovel, never a land grab -- and every opened
cell runs the normal mined path, so holy ground still unseals and dwarven spoil
still accrues. Buying setting charges from the Deep Holds and then owing them
more spoil for using them is not an accident.

It is also cast at a MINED cell, because `IsCellValidForSpell` has always
required one. That was discovered rather than designed and it is the better
shape: the working starts on the frontier by construction.

**Both new workings are pause-illegal,** per entry 39. Bodies on the board and a
write to the tilemap are the two clearest cases the rule has.

**Heard of, and then greyed.** A working nothing grants and nobody can research
would otherwise be invisible until the scroll was already in hand, which makes
the CAST tab lie by omission to a player who has stood at the shelf and read the
row. So a vendor calls `TraderStockCatalog.NotifyStocked` for every charge entry
that reaches a shelf, which sets a bare `spell.heard.<id>` key that no node owns
-- the entry 19 precedent for a thing that can only be given, used a third time --
and the working then lists GREYED at the tail of the CAST row with its
`sourceLine` on hover. On the SHELF, not on the purchase: what the row tells a
player is that the thing exists and where it comes from, which they already know
the moment they have read it. `UnlockState.Unlock` is idempotent, so calling this
on every roll and every open costs nothing after the first.

The greyed rows are a SECOND list in `ActionBarHUD`, never folded into
`spellEntries`. The number keys, `selectedSpellIndex` and `PushSelectedSpell` all
key on that index, and putting uncastable entries inside it would make every one
of them need a castability test it does not have. They are non-interactable and
carry no number key, and `AnySpellKnown` still ignores them, so a rumour can
never light the CAST tab for a core that holds nothing.

**Rejected, with reasons.** A corridor-sealing working (turn floor back into
rock) was designed and dropped: `CanBuildWallAt` ends in `AnyEntityStandsIn`, so
burial is PREVENTED and never handled, and an area effect that walls a corridor
would refuse on every cell that had anybody in it -- which is every cell worth
casting it on. A floor-scale pull was dropped for the same class of reason:
`Pull` clamps force to `Mathf.Min(def.secondary, dist)` and `KnockbackStep` stops
dead at the first wall, so a floor-wide Undertow would read as a metre of
shuffling against masonry. Both stay dropped. A charge listing in the journal was
dropped too: the CAST row already shows the count after the price, and a second
surface for one integer is a tab nobody would open twice.

**Key files:** `Gameplay/SpellCharges.cs`, `Gameplay/SpellBook.cs`,
`Gameplay/SpellDefinition.cs`, `Gameplay/SpellCaster.cs`,
`Gameplay/TraderStockCatalog.cs`, `Overworld/WanderingMerchantController.cs`,
`Floors/DwarvenOutpostController.cs`, `Save/DungeonSaveData.cs`,
`Save/DungeonSaveController.cs`, `DungeonCore/DungeonBuildController.cs`,
`UI/ActionBarHUD.cs`, `Editor/SpellContentGenerator.cs`,
`Editor/TraderStockGenerator.cs`, `Editor/DwarfStockGenerator.cs`,
`TESTING/Commands.cs`.

---

## 42. Dens and the Deep Occupants (Decision Record)

Status: DECIDED, NOT BUILT. This entry is a decision record written BEFORE any
code, so that a fresh chat re-syncing and reading canon builds the agreed
version instead of re-deriving it from a stale backlog. Nothing below is
as-built. Statements about EXISTING systems were each verified against source
at `0a4a1b9c`; everything else is a ruling. When the dens ship, the built
sections replace the decided ones in place and the status line changes -- the
rulings and the reasons stay.

Supersedes `DCR_Backlog.html` section C entirely. That section was written
against canon at `dbe77598` (entries 1-33) and its floor ladder, its item
split and three of its premises no longer survive contact with shipped canon.

### What a den is, and what it is not

A den is a SOURCE. Every threat in the game today is either an arrival
(adventurers, the threat events) or a one-time clear (a wild chamber goes
permanently `cleared`). Nothing yet is a place that keeps producing until the
player goes and destroys it. That gap is the whole reason for the feature.

A den is NOT a faction: no standing, no Faction panel row, no edge in
`FactionRelations`. Five factions is the roster.

### Floor allocation

Floor index 0 gets nothing. It is radius 100, carries the entrance cave, the
core cavern and its tunnels, the guided opening (entry 13A), the resting place
(entry 34) and every wave in the game. It is full.

Floor index 1 carries the GOBLIN HOLE. Floor index 2 carries the KOBOLD DEN,
alongside the gatehouse and the living trunk road. Floor indices 3 and 4 carry
the deep occupants and are a SEPARATE PHASE, not to be started until 1 and 2
are complete and verified. Their lore is locked below so that a later session
cannot invent something incompatible; nothing else about them is.

One den per floor, GUARANTEED rather than chance-rolled. A den behind a Silver
or Gold gate that then fails a roll is a feature most players never meet, and
the guarantee pattern is already shipped three times over (`PlaceOutpost`,
`PlaceVillage`, `PlaceDeadCore`).

The den sits inside entry 19's placement band -- 15 to 65 per cent of floor
radius. That number is not decoration: reveal is influence-touch only, and
entry 19 measured a plausible late run as reaching roughly the inner 65 per
cent. Content outside the band is content nobody meets, and entry 19 requires
any future deep-floor content to be measured against it before it is
scattered.

### The tunnel substrate

Each den floor is generated with a PRE-CARVED TUNNEL NETWORK the den connects
to. The tunnels are the reason the den reads as somewhere rather than as a
spawn point, and they are also the reason the floor has a shape before the
player touches it.

- **Carved at generation, dormant from birth.** A floor does not EXIST until
  the player places a down stair: `DungeonBuildController` ->
  `FloorManager.EnsureFloorExists` -> `CreateFloor` -> `FloorRoot.Bootstrap`.
  No `FloorRoot` means no entity registry, no `RespawnTicker` iteration and
  no den. The dormancy is therefore free rather than merely cheap, and needs
  no flag.
- **Walkable, unclaimed, unrevealed.** `MarkNaturalFloor` registers cells as
  mined-but-unclaimed exactly as a chamber's interior is, and
  `DungeonPathfinder.IsWalkable` tests `IsTileMined` -- so tunnels are pathable
  with NO new pathfinding work. Reveal follows the ROAD model: progressive,
  with the prepared band past the revealed stretch, not the whole-network
  reveal a site gets.
- **The generators exist.** `TerrainFeatureGenerator.BuildTunnel` already
  walks a wobbling centreline, dilates it to a tapering width with a square
  brush and clamps to the disc. `RoadNetworkBuilder` supplies junctions,
  fillets, chords and `ApproachWaypoints`. Extend these; do not write a third
  centreline carver.
- **Tunnels never touch the landing.** On floors above 0 there is no core
  cavern -- `GenerateCoreCavernAndTunnels` runs for floor index 0 ONLY -- so
  the keep-clear zone is the starter blob `ClaimStarterArea` opens around the
  stair-landing cell, plus a margin. First contact must happen when the
  player's digging or ambient creep reaches the network. The player causes it.
- **Tunnels outlive the den.** Clear it and the network stays.

**Carve precedence gains a sixth stage, and entry 19's ordering section is
amended accordingly.** `GenerateNew` runs: core cavern and its tunnels, the
entrance cave, roads (plan), sites, roads (rasterise), chambers, DEN TUNNELS,
rivers. Tunnels come after chambers because they connect chambers and cannot
be routed before their endpoints exist, and before rivers because a river cuts
through a tunnel exactly as it cuts through a road -- the flooded run is free
storytelling from the ordering alone, on the same argument entry 19 already
made for the washed-out crossing. Chronology agrees: the roads are Buried Age
and the tunnels are recent, so the newest dug thing is carved last.

**The shortcut is intended, not incidental.** Pre-mined tunnels are open ground
the pathfinder will happily route adventurers down, so a floor arrives with a
tactical geometry the player did not choose and must answer. This is the
design, and it is what finally gives entry 36's built walls a recurring
diegetic job. `SealPenaltyController` clocks only a SEVERED CORE ROUTE, so
walling a side tunnel costs nothing -- correct, and confirmed rather than
assumed.

**The free mana is accepted and not priced.** Pre-mined cells cost nothing to
dig and ambient creep will claim them. The tunnels are the compensation for
the den living on the floor.

### The two dens, and why their verbs differ

The whole read is that the two dens do DIFFERENT THINGS to the floor. Goblins
take what you drop; kobolds take what you have not found yet.

**Goblin Hole (floor index 1) -- occupy.**

- They never dig. The hole is what was already there.
- They steal `CarriableLoot` from the ground -- the coins monsters and dead
  adventurers drop, which currently auto-absorb to the core after 30 seconds.
  That window is load-bearing: it forces goblins to arrive during or just
  after a fight, which IS the scavenger identity.
- Explicitly NOT chests. `DungeonChest` stores no value (it rolls loot on
  open, and `RemoveByPlayer` says so), and chests are tuned bait feeding the
  Treasure Hunter tier scan, the appeal ledger and the `MercenaryContract`
  outflow window. Robbing them poisons three tuned systems at once.
- Explicitly NOT mana: an invisible bleed on the build resource.
- The hoard FEEDS DEN TIER; tier feeds raid size and frequency. Without that
  the den is a pinata -- gold carries no interest or upkeep in DCR, so a hoard
  that only accumulates costs the player nothing to ignore.
- Clearing returns the full hoard.
- Size 250-400 cells, FIXED. Because they do not dig, the hole cannot grow, so
  TIER READS OFF HOW FULL IT IS -- population and visible hoard.
- No new art. `MonsterDef_GoblinCutthroat`, both Hobgoblins and
  `WildDef_GoblinScout` already ship.
- **The identity question is answered by saying it out loud.** Goblins already
  serve player cores, so a goblin den raiding one is a good beat if somebody
  says so and a muddle if nobody does. One wisp line, the first time a
  goblin-fielding core finds the hole.

**Kobold Den (floor index 2) -- extend.**

- They carve offshoots over days, with progress VISIBLE between visits. This
  is the counterpart to the goblins' fixed hole.
- They dig UNCLAIMED rock only and never claim. Claiming is the player's verb
  and a digging faction that claims would collide with the influence model and
  with `DwarvenClaimLedger`'s per-cell billing.
- Size ~150 cells at tier 1, WIDENING per tier, hard-capped at 600. Tier reads
  off how big it is. The cap is measured against entry 19's scale rule: a cave
  chamber is 100-200 cells and the largest dwarven village is 2588, and a
  span-62 plaza at roughly 3000 cells "read as a hole in the fog rather than a
  building". 600 leaves the top-tier den clearly the biggest natural cavity on
  its floor without becoming a set-piece.
- **Contested discovery lives here.** Kobolds excavate buried remains before
  the player finds them. An excavated cell leaves a VISIBLE EMPTY HOLE --
  without that the loss is invisible, because the claim-halo murmur only fires
  within `senseRadius` 2 of a claimed cell and a player who never sensed the
  remains would never know they were robbed. Clearing the den recovers the
  grant through `BuriedRemainsController.GrantExternalDiscovery`, whose doc
  comment has named the desecration arc as its only caller since it shipped.
  This is the first thing in DCR that punishes slowness rather than
  aggression. Note the ceiling: `sitesPerFloor` is 2, so this is a set-piece
  beat at most twice per floor, not ongoing pressure.
- **Their tunnels intersect the living trunk road, and skirmishes follow.**
  Floor index 2 already carries the road, the gatehouse and
  `DwarvenPatrolController`. Clearing the kobolds EARNS DWARVEN STANDING --
  the first positive lever that is not shopping. The entire positive side
  today is `standingPerHundredSold` and `standingPerHundredSpent`; everything
  else is loss (`DwarvenClaimLedger` at -0.05 per cell, robbery at -25).
- A wisp line names who dug it. `DwarvenClaimLedger` bills on `ClaimTile` only
  so the player can never actually be charged for a kobold tunnel, but a
  player who suspects they were will not read the code to find out.
- New creature art required. `DCR_Guide_Content_Authoring.html` gains a
  chapter.

### Engineering rulings

- **Den raids roll their own dawn check; they do NOT ride
  `WorldEventDirector`.** This reverses an earlier suggestion in the same
  design session, and the reversal is the useful record. The director gates on
  minDay / minNotoriety / minRating and its effects are multipliers held in
  `WorldEventEffectKind`; a den raid is conditional on DEN STATE and its
  effect is to SPAWN BODIES. Riding it needs a new gate concept and a new
  effect kind, at which point nothing is being reused but the dawn loop.
  COPY the director's dawn ordering and its sim-before-C# discipline; do not
  inherit its plumbing.
- **Growth is a dawn-ticked ledger**, on the `CampGrowthController` model
  (`OnDayStarted`, grace days, decay, buildout rebuilt from ledger and tiers
  rather than saved). Bodies instantiate only when the player is on that floor
  or a raid fires.
- **Its own controller.** `WildMonsterController` cannot be extended into
  this: it is chamber-scoped end to end (`spawnedPerChamber`,
  `HandleChamberRevealed`, `MarkChamberCleared`) and its clearing is
  permanent, which is the opposite of a source.
- **Save shape copies `ChamberData`** -- sentinel count plus a per-monster
  snapshot list with a coarse re-roll fallback. It is proven and it already
  handles the unresolvable-definition case.
- **Grace of 5 days after `OnFloorCreated`** before the den ticks, stored as
  an awakened-day int. A wisp line on waking: they stirred because the player
  arrived.
- **Cleared dens regrow unless the tunnels feeding them are sealed.** This is
  what makes clearing a choice rather than a chore, and it is the second job
  it hands entry 36.
- **Dens are hostile to adventurers too**, which falls out of `ScanForHostiles`
  for free.
- **The great predator ignores dens.** Folding them into `NearestPrey` would
  retune `WildMonsterEvent`'s hunger pacing, which is tuned.
- **Agent caps: 10 goblins, 15 kobolds, both serialized.** Provisional and
  expected to move after testing; serialized so moving them is not a
  recompile.
- **Existing saves get nothing and need no migration.** Floors already created
  carry no tunnels; only newly-created floors do.
- **`FeatureType` gains appended values only.** The enum serialises into saves
  as ints.

### Two rules lifted out of the den work

**"You field what you defeat" is a canon RULE, and ships first and
separately.** `AdventurerDefinition.unlocksOnDeath` is read at exactly one
site (`DungeonAdventurer`) and calls `BestiaryState.Discover`. Mirroring the
field onto `MonsterDefinition` and firing it from the wild death path is a
handful of lines. Stating it as a rule rather than shipping two special cases
is the point: it makes the bestiary a conquest record and tells every future
author where unlocks come from. It is not bundled into the dens and does not
wait on them.

**Allegiance is a field now, hostility is a pass later.** `IsWild` is derived
(`wildChamberId >= 0 || isInvader`) and is the hostility test in exactly two
places -- `_hostileMonsterPred` and `NearestPrey`. Add the allegiance field
with the dens so the door is built; leave cross-den hostility DISABLED. When
it is enabled it needs a readout, because the payoff otherwise happens in fog
on a floor nobody is looking at.

### The deep occupants -- lore locked, nothing built

Two canon questions have been waiting for this: entry 19's "whatever the
dwarves will not go below", and entry 34's blank, which entry 34 already says
"may be shared" with it. This is the answer, recorded now so a later session
cannot contradict it.

**They are what a dead core still makes.** Not demons and not a separate
mythology. A core that failed and was SEALED RATHER THAN DESTROYED, still
doing the only thing a core does -- spawning -- for centuries, with nobody
left to shape what comes out. Unshaped, unnamed, and further from anything
nameable the longer it runs.

This uses the shipped `DeadCoreVault` (entry 20: guaranteed one per dungeon on
floor index 4, three authored plans at 75x75) instead of standing beside it,
and it retroactively explains why the Church built a seventy-five-cell vault
rather than simply breaking the stone. YOU CANNOT KILL A CORE. YOU CAN ONLY
WALL ONE IN. The wisp already says, on revealing the vault, that some of the
others are still down here.

**The dwarves are not the cause.** The "dug too greedily" reading was
considered and declined: entry 19's dwarves are careful -- they hold a gate,
maintain one road and stopped. Dwarves who KNOW and refuse to go are a better
faction than dwarves who blundered.

**The prologue is the endgame in miniature.** Entry 34 records the death as
explicit and named: Pell, Brother Mott and Serra Vane, at an OPENED SEAL, and
Serra finishing it. The blank was never the murder -- it is what seal they had
opened and where Serra's maps came from. What got out under that town is the
same category of thing that is under floor index 4, and the player, a core
reborn at a warded rebirth site, is the same object as the thing in the vault
at a different stage.

**Never seen clearly, and that is a constraint rather than a preference** --
entry 34's own words about the wisp, applied here. The prologue never shows
what came out either: the screen goes dark and Serra narrates over it. "You do
not see what is behind a seal" is already the game's idiom. Three consequences,
all held:

- **No bestiary unlock.** This is the STATED EXCEPTION to the rule above, and
  stating the exception is what makes the rule read as a rule.
- **No name in UI.** Alerts and wisp lines refer obliquely.
- **No loot table.** They carry nothing. Everything else in the game wants
  something -- gold, the core, the road, the coins on the floor. These do not,
  and that absence is the characterisation.

**Floor index 4 is a CONDITION, not a den.** No hoard, no tier, no clear.
Saturation makes the dead network expensive to HOLD rather than dangerous to
ENTER. This matters because entry 9's climax fires at Diamond 3 and surviving
it "silences the recurring threats for good", so index 4 is entered by a god
core in a sandbox. Do not build a boss down there; the game already had its
boss.

**Floor index 3's stake is the VILLAGE, not the road** -- a different verb
from the kobold skirmish, because the same beat on consecutive floors is
repetition wearing escalation's clothes. The village can FALL: villagers gone,
lanes still. It is also RECOVERABLE, and the recovery is what makes the loss
acceptable -- dwarven patrols check the fallen hold, and once one reports it
clear, settlers arrive after a delay and the hold re-establishes. Clearing the
den after failing therefore still means something, and the patrols gain a
reason to exist beyond flavour. Defaults for the resettle, recorded so the
later phase does not re-litigate them: the SAME authored hold plan (it is the
same place rebuilt, and the plan is world-seeded), walkers returning at
reduced count and recovering, standing recovering on resettle, and the
clearing itself paying as the kobold clearing does.

**Breaking the vault heart escalates floor index 4 saturation.** Entry 20
grants 60 research and a full level of XP for that break against a price of
-25 alignment and nothing else. This gives the largest reward in the game its
first teeth.

**The cataclysm's cause stays unstated.** Entry 21 has the Buried Age
"entombed in a cataclysm", unattributed. If the occupants caused it, three
open questions collapse into one answer -- elegant, and it spends all three at
once. Floor index 4 implies and never states; the dwarves refusing to discuss
it does the work.

### The tunnel substrate -- shape E (BUILT: plan layer)

Status of this section: BUILT. The plan layer (`DenTunnelBuilder`,
`DenTunnelProfile`, the headless report) and now the rasterise, save and reveal
half, which mirrors the ROAD machinery line for line because that machinery had
already solved every problem this one has.

**Cells are derived, never persisted.** `DenTunnelData` stores the polyline and
the two widths; `Centreline` and `Cells` rebuild the rest, and the SAME pair
serves generation and load so the two can never disagree. The RoadData
contract, for the RoadData reason.

**A tunnel tapers where a road does not**, so it dilates per centreline cell at
that cell's own width rather than once at a single width. The taper is a
property of the WHOLE run: `DenTunnelCellsForRange` indexes into the run's own
centreline rather than restarting the lerp at each reveal segment, which would
have stepped the section back to full width four times down a long tunnel.

**Reveal satisfies entry 19's invariant BY CONSTRUCTION.** A cell reads as wall
only when it is both PAINTED (the renderer caps a solid 8-adjacent to a MINED
cell) and REVEALED. `UnfogDenTunnelSegment` reveals each carved cell plus its
full 8-neighbourhood and calls `MarkNaturalFloor` on the carved cells -- so the
cells the renderer will paint are exactly the cells revealed, no more and no
fewer. This is `UnfogRoadSegment`'s shape and it is not to be tidied. Fog is
one-way; neither error can be corrected afterwards.

**A run BREACHES the chamber it links -- measured, not assumed.** The
pathfinder walks 4-neighbours only and Bresenham takes diagonal steps, so
"reaches the chamber" and "something can walk between them" are different
claims. Flood-filled over ~1800 links: 831/831 on floor index 1 and 959/959 on
floor index 2 are 4-connected from the den end into the chamber interior, zero
merely touching, zero apart. Three things carry it, and any one could be
undone by a tidy-up: the centreline is driven to the chamber CENTRE rather than
its edge; consecutive dilations OVERLAP, so a 2-wide tip stays 4-connected
across a diagonal step; and handing the overlap back to the chamber severs
nothing, because the run's cells outside the blob stay 4-adjacent to cells
inside it. `Den Tunnel Breach Check` in `Commands` is the standing regression
test.

**Ownership order.** `RebuildDenTunnelCells` hands back every cell already
owned by the core cavern, entrance, carriageway, sites and chambers; rivers
take theirs afterwards, which is the flooded run. So a tunnel keeps only the
stretch outside whatever it meets, and the chamber owns the overlap.

**Walkability follows reveal, per feature.** Tunnel and chamber each become
walkable when their OWN unfog runs. A revealed run abutting an unrevealed
chamber therefore ends at rock until the chamber reveals -- which it does,
because claiming down the run puts the chamber's edge into the claimable ring
and the reveal controller chains from there. Roads already behave this way.

**The agreed fork 7 was WRONG, and the measurement is the record.** Tunnels
were to link chambers AND stay inside the 15-65 per cent band. They cannot do
both: `GenerateChambers` places chambers UNIFORMLY across the disc and says so
in its own comment. Measured over 2000 seeds in `Tools/sim_den_tunnels.py`:

| floor | chambers | in band | fewer than 2 in band |
|---|---|---|---|
| 1 | 4.48 | 2.12 | **30.8%** |
| 2 | 7.56 | 3.25 | **12.9%** |

Three shapes were measured before any C# was written:

- **A -- link the nearest chambers, band or not.** 17-18 per cent of tunnel
  length falls outside the band and the worst endpoint reaches 0.96 of the
  radius, inside the bedrock rim's approach.
- **D -- a self-contained in-band network, chambers joined opportunistically.**
  Robust and pointless: touches NO chamber on some 40 per cent of seeds.
- **E -- a FIXED number of runs, each taking a chamber if one is in range and
  ending in the rock if none is.** SHIPS. It cannot starve by construction,
  because chamber count changes the FLAVOUR of a network rather than whether
  one exists.

**A dead end is content, not failure.** It reads as an unfinished dig, and on
an Excavator floor it is exactly what the population extends.

**Tuned by sweep, not by taste.** Endpoint clamp 0.85 of radius, longest run
0.90 of radius, minimum 12 cells, section 3 tapering to 2. Runs: 3 on floor
index 1, 4 on floor index 2.

| floor | chamber links | dead ends | no link at all | carved cells |
|---|---|---|---|---|
| 1 | 2.10 | 0.91 | 5.8% | 508 (worst ~960) |
| 2 | 3.29 | 0.71 | 0.9% | 1107 (worst ~2070) |

**Raising the run count does NOT reduce the no-link rate** -- it is invariant
at 5.8 per cent across three, four and five runs on floor index 1, because
"no link" means no ELIGIBLE CHAMBER EXISTS and more runs cannot conjure one.
Extra runs buy dead ends and carved rock only (508 / 650 / 772 cells), which is
why floor index 1 stays at three. Anyone tempted to raise it to fix starvation
should read this paragraph instead.

**Nearest-first, never bearing-first.** Runs choose their chambers by distance.
Bearing-first was written and dropped: it discarded a perfectly good chamber
for sitting off its run's assigned heading, costing floor index 1 a chamber
link on a quarter of seeds and buying nothing.

**Dead-end bearings RETRY rather than surrender**, shortening the run before
giving up. The first version abandoned a blocked bearing outright, which handed
a den fewer runs than the profile authored -- invisible in the inspector, and
indistinguishable from the generator having quietly failed. The headless
report's `short%` column exists to catch exactly that regression: it reads 0.0
now and read 23 before the retry.

**The landing test is on the SEGMENT, not the endpoints.** A run whose two ends
both clear the starter blob can still drive straight through it.

**The mana gift, sized rather than waved through.** Fork 11 accepted that
pre-mined tunnel cells cost the player no digging. That is 508 cells on floor
index 1 and 1107 on floor index 2 -- under one per cent of either disc, and
well clear of the roughly 3000-cell site scale entry 19 warned about.

**Cross-checked, C# against Python.** The report reproduces the sim's figures
(mean run 71 against 70, and 111 against 112; cells 508 against 522, and 1107
against 1121). The RNGs differ, so the numbers cannot match cell for cell, but
a builder that disagrees with the sim it was written from has a bug -- which is
how the dropped-run fault above was found.

**Key files:** `Floors/DenTunnelProfile.cs` (+ `DenTunnelFloorEntry`,
`DenKind`), `Floors/DenTunnelBuilder.cs` (+ `DenTunnelPlan`, `DenTunnelRun`;
pure static), `TESTING/Commands.cs` (the headless report),
`Tools/sim_den_tunnels.py`.

### Open, and deliberately so

- **Fork 7 was AMENDED by the read pass, and the reason is recorded rather
  than the amendment alone.** The agreed wording had generated tunnels linking
  chambers to each other AND to the two buried-remains cells. That is not
  reachable: `FloorRoot.Bootstrap` runs `featureGenerator.GenerateNew` BEFORE
  `terrainTypeMap.GenerateNew`, and `TerrainTypeMap.GetBuriedSites` returns
  empty while `generated` is false -- so remains cells do not exist yet when
  tunnels are carved. The amendment: GENERATED tunnels link chambers (and, on
  floor index 2, reach the road and the sites); the RUNTIME kobolds dig toward
  remains, which is what they do anyway and by which time the type map exists.
  This is better than the original: a dig visibly heading somewhere over days
  is a stronger race than a tunnel that always pointed there.
- **Buried remains are NOT band-confined.** `GetBuriedSites` samples uniformly
  across the usable disc, so a remains cell can land in the outer third that
  entry 19 says nobody reaches. Kobolds target only remains INSIDE the 15-65
  per cent band; remains outside it are left alone, so the band measurement
  stays intact.
- No Ancient Sites are added to floor index 1. Entry 19 calls index 2's lone
  guard post "deliberate reach -- without it most players would never meet the
  Buried Age at all", and sites at index 1 spend that reveal a floor early.
- Den behaviour: the populations themselves, their growth ledger and
  their raids. The substrate carries no den yet, only its tunnels.
- Floor index 1 links no chamber at all on 5.8 per cent of seeds. Accepted:
  the only levers are the clamp, the run bounds, or the landing clearance that
  fork 8 made a hard rule, and a den of pure dead ends is legitimate content.
- Goblin tier is legible from population and hoard; exactly what renders the
  hoard is not yet decided.
- Cross-den hostility, and the readout it needs.
- `DCR_Backlog.html` is stale against canon and was NOT refreshed with this
  entry, by decision. Section E is shipped as entry 34, section D item 1 is
  largely shipped as the `DeadCoreVault`, and world events, chest tiers, core
  spells, the pause audit and the button row have all shipped since it was
  written. Anyone reading it should read canon first.

### Update the Canon

This entry IS the canon update for the decision pass. When the dens ship, the
feature guide's own Update-the-Canon chapter revises the sections above in
place and moves the status line off DECIDED -- it does not append a second
entry.

---

# APPENDIX

## A. Content Registries and Authoring Keys

Status: SHIPPED (documentation of as-built behaviour). Verified: 2026-07-09.

**Registry membership** = placeable through its picker + restorable by name on
load. `DungeonSaveController` restores placed spawners, traps, furniture,
chests and room assignments via `GetByName` against the five registries.
Anything the player can place and save must be registered; transient-only
definitions stay out (event beasts spawned from a component slot, e.g.
`WildMonsterEvent.predatorDef` / the Ravenous Bear).

**Unified wild/placeable model:** one `MonsterDefinition` serves both roles.
It sits in the floor template's `TerrainFeatureGenerator.wildMonsterPool`
(spawns wild) AND in the monster registry with `requiresDiscovery`
(picker-locked until its own `monsterName` is discovered by slaying it wild).
Adventurer-granted unlocks reuse the Bestiary channel via `unlocksOnDeath`
(Commoner -> Thrall, Dark cores only). Discovery, save restore and the picker
all key on `monsterName`.

**YOU FIELD WHAT YOU DEFEAT -- the unlock rule (SHIPPED).** Stated as a RULE
rather than left implicit, because it decides where every future unlock comes
from and an author with no rule invents a channel.

*The bestiary is a conquest record. A definition enters it when something of
that kind is brought down BY THIS DUNGEON, and by nothing else.*

A wild kill discovers its OWN definition -- there is deliberately no
cross-unlock on `MonsterDefinition`, and one was designed and rejected in the
same pass that wrote this rule: a wild kill teaching some other creature makes
the bestiary a table of authored gifts rather than a record of what the
dungeon has put down, which is the whole of the rule. The adventurer channel
(`AdventurerDefinition.unlocksOnDeath`) is not that: the Thrall is the human
frame the Dark core learns to raise FROM the commoner it killed, which is the
same creature by another route.

**Attribution, and why it is inverted.** The unlock previously fired whoever
landed the blow, and adventurers do kill wild monsters -- their monster scan
(`DungeonAdventurer`) carries no `IsWild` filter and simply takes the nearest
`DungeonMonster` -- so a chamber cleared by a raiding party unlocked creatures
the player never fought. `TakeDamage(float)` carries no attacker, is called
from twenty-six sites and sits on `IMonsterTarget`, so threading a source
through all of it would have rewritten adventurer combat.

Instead `dungeonDealtDamage` is set by DEFAULT and only OUTSIDER sources mark
themselves, through a `bool fromOutsider` overload on `IMonsterTarget` and a
`fromOutsider` field on `DungeonProjectile.Payload` (the bolt outlives the
shot, so the shooter cannot be asked at impact). FIVE sites mark: monster
melee and monster fire when the shooter `IsWild`, adventurer melee and
adventurer fire always. Roughly twenty dungeon sources -- nine traps, the
crossbow sentry, core spells, the trap chest, room-effect burn, sparring,
core-room burn -- are UNTOUCHED and correct by default. That is a quarter of
the edits, and it fails in the safe direction: a source nobody marked keeps
the shipped behaviour rather than silently refusing an unlock the player
earned.

**ANY dungeon damage counts, not the killing blow.** Wild monsters do not
regenerate (`wildRegenMultiplier` defaults to 0) so a wound is permanent and
the flag never needs to expire, and a beast the dungeon's monsters wore down
still counts when an adventurer steals the last hit. Majority-damage
accounting was considered and refused: per-source bookkeeping for a
distinction no player can perceive.

**Instance state, never saved.** A wild monster's HP snapshot restores but its
history does not, so a reload mid-fight asks for one more blow. Accepted: the
alternative is a save field per live wild monster to remove a nuisance nobody
will meet twice.

**The gate covers the unlock only.** `wildCoreXpOnDeath` and
`RunStats.RecordWildMonsterSlain` still fire whoever killed it, deliberately:
those record that something died in this dungeon, while the bestiary records
what this dungeon put down. `AdventurerDefinition.unlocksOnDeath` takes the
SAME gate, so the rule has no exception -- today only `Commoner.asset` authors
a grant and Commoners are not caught in Hero-versus-Cultist fights, so that
half is a guard against the case rather than a fix for a seen bug.

**The stated exception:** the deep occupants of entry 42 are never discovered
at all. That exception is what makes this a rule rather than a habit -- they
are the one thing the player defeats and cannot field, and the absence is
characterisation.

**Diagnostics:** "Print Bestiary Sources" in `Commands` lists every floor's
wild pool with each definition's discovered state, flagging entries whose
`minWildFloor` means that floor can never roll them. Without it "the unlock
did not fire" and "the beast never spawned" look identical from the picker.

**Key files:** `Monster/IMonsterTarget.cs`, `Monster/DungeonMonster.cs`
(`TakeDamage`, `Die`), `Adventurer/DungeonAdventurer.cs` (`TakeDamage`, the
death path), `Monster/DungeonProjectile.cs` (`Payload`, `Impact`),
`DungeonCore/BestiaryState.cs`, `TESTING/Commands.cs`.

**Immutable keys** (save-breaking if renamed after ship): `monsterName`,
`roomName`, `trapName`, `furnitureName`, `chestName`, `questID`, item `ID`
ints, and enum ordinals (`AdventurerType`, `CombatClass` persist as ints --
append new values only, never reorder). Registry list order is free: lookups
are name-based; order only drives picker presentation.

**Material patterns:** the sixth registry. `PatternCatalog.asset` lists every
`PatternDefinition`; the save key is the `UnlockState` flag `pattern.`
(id immutable after ship -- asset filenames free). Band = channel: Terrain is
deterministic first-claim and code-bound; Common..Legendary join their band's
loot roll automatically; Reserved is silhouette-only until a future system
unlocks it. Authored via `Editor/PatternContentGenerator` (single source of
truth -- regeneration rebuilds the catalog list).

**Authoring reference:** step-by-step recipes live in the Content Authoring
guide; this appendix records the rules those recipes obey.


## B. Sorting Layer Contract

Status: RECORDED after the Phase 3 cleanup regression. Verified: 2026-07-17.

The TagManager order is load-bearing; scripts and prefabs assume it.
Bottom to top: Default, Ground, Collision, OwnedTiles, Decor, WalkInFront,
Player, WalkBehind, Shadow, AdjacentHighlight, WorldUI. Identity is the
uniqueID; list order is the draw order -- reordering the list IS the
change.

Semantics: every Y-sorting entity (player, NPCs, monsters, adventurers,
cave faces, surface trees) lives on Player. WalkInFront draws UNDER
entities -- ground furniture the player walks in front of. WalkBehind
draws OVER entities -- town canopy and cave wall caps the player walks
behind (CaveWallFade and CaveWallRenderer assume this). Shadow sits just
above WalkBehind so darkness covers walls and entities alike
(DungeonShadow, FogLayer, and the Ceremony Gloom veil, which lives on
Shadow for exactly this reason). AdjacentHighlight and WorldUI cap the
world.

The Phase 3 cleanup inverted this order and silently broke cap occlusion,
fog cover, and the town walk layers for three weeks. Any future layer
change re-reads this entry first.
## C. Camera Bounds Contract

Status: RECORDED with the free-roam change. Verified: 2026-08-03.

The camera roams the whole floor. Every FloorRoot carries a "DungeonBounds"
child whose PolygonCollider2D is the Cinemachine confiner shape, and
`DungeonBoundsUpdater` sets it to a square around the core cell measuring
(floor radius + paddingCells) cells on every side.

It used to be an axis-aligned box around CLAIMED tiles, rebuilt on every
claim event, and that was wrong twice over. The deeper floors felt locked,
because the player had claimed almost nothing there and could not pan to a
patrol, a caravan, or a road stretch. Worse, it broke alerts silently:
`DungeonCameraController.PanTo` is clamped by this confiner, so clicking an
alert about anything outside the claimed box panned the camera into a wall
and showed the player nothing. Floor 0 hid the fault, because the surface
union had already widened that floor to its full disc.

Floor 0 remains the ONE exception, and it is a ceiling rather than a floor:
its bound reaches the researched surface depth and must never go further.
Forest bands paint in full the instant their research key unlocks, and past
the revealed edge no ground is painted at all, so this confiner is the only
thing standing between the player and the void. Entry 24 rejects staged tile
painting on exactly that basis, and that rejection depends on this ceiling.

Reveal is fog's job, not the camera's. DungeonShadow covers unexplored
ground on every floor, so a roaming camera sees darkness rather than
secrets. Anything drawing ABOVE Shadow in the sorting order (Appendix B)
bypasses fog and must therefore gate itself; the influence ring quad on
AdjacentHighlight is the one that does.

Consequence for callers: bounds change only when the floor radius is first
known, or when floor 0's revealed depth advances (`MarkDirty()`). A
recalculation attempted while the terrain radius is still zero re-arms
`boundsDirty` and retries next frame instead of baking in the viewport
minimum -- the old per-claim rebuild was covering that case by accident.

**Every camera jump carries a floor.** `PanTo(worldPos)` writes a position
and nothing else, so calling it with a coordinate from another floor drives
the camera into the active floor's bound and pins it at the edge. Alerts hit
this: `AlertsLog.AddAlert` defaults `floorIndex` to -1 and most callers take
the default, so a click on a floor-0 alert stranded the camera on whatever
floor the player was standing on. `BuildEntry` now resolves -1 to
`FloorManager.ActiveFloorIndex` at creation, and `BindButton` recovers the
floor of an already-persisted -1 entry from the stored Y (floors are
`floorIndex * -2000` apart and the widest disc is 600 cells, so the mapping
is unambiguous). Anything else that pans across floors uses the two-argument
overload.

**Getting home.** `GameAction.RecenterCamera` (Home by default) pans to the
ACTIVE floor's core cell via `DungeonCameraController.RecenterOnCore()`. Free
roam makes it easy to be far out on the disc with no landmark in shot, and
the F1-F4 bookmarks only help once they have been set.

**Anything that tracks the active floor tests IDENTITY, not just events.**
`FloorManager` carries no `[DefaultExecutionOrder]`, so its `Awake` races the
`OnEnable` of every default-order component that wants
`OnActiveFloorChanged`. `Minimap` lost that race and skipped its subscription
silently, then went on looking healthy on floor 0 for a whole session while
following nothing -- label frozen, floor 0's tiles painted, and the camera
view outline disappearing on every other floor because it clamps to the
painted frame. Subscribing is an optimisation; the correctness guarantee is a
per-frame comparison of the hooked floor against `FloorManager.ActiveFloor`.
Other components subscribing to a singleton in `OnEnable` under the same
`if (Instance != null)` guard are exposed to the same race; the ones reading
`DungeonCore` are safe only because it sits at execution order -20.
## D. Execution Order Contract

Status: RECORDED after the minimap subscription race. Verified: 2026-08-03.

Manager singletons whose events are subscribed to from another component's
`OnEnable` sit in a REGISTRY TIER at execution order -90:
`FloorManager`, `DungeonBuildController`, `SpawnerSelectionController`,
`DayNightCycle`. `DungeonCore` at -20 is grandfathered -- anything negative
is early enough, so the tier is a ceiling, not an exact number.

Why it is load-bearing. Thirteen components subscribe under the pattern
`if (X.Instance != null) X.Instance.OnSomething += Handler;` inside
`OnEnable`. At default order 0 that guard races the singleton's `Awake`, and
Unity does not define which wins. The loser takes the null branch, skips the
subscription, and never tries again -- no exception, no warning, nothing in
the console. `Minimap` lost that race against `FloorManager` and spent whole
sessions painting floor 0, its label frozen and its camera outline vanishing
on every other floor, while looking perfectly healthy where the player
happened to start.

Rules:
- A new manager singleton whose `Instance` is read from another component's
  `OnEnable` joins the registry tier.
- Nothing that SUBSCRIBES may sit below the tier; subscribers belong at
  default order or later.
- Registry-tier `Awake` bodies stay cheap and self-contained. The four above
  set `Instance` and, in `DungeonBuildController`'s case, build two sprite
  assets from nothing. None reads another singleton, which is why moving them
  earlier is safe.
- This project has NO `MonoManager.asset`, so there are no project-level
  execution order overrides and the attributes are the complete picture. If
  one is ever added, this entry stops being sufficient on its own.
- `Dungeon Core / Commands / Validate Execution Order Contract` checks the
  tier by reflection and fails loudly.

**Load order is a contract too, and its failures are just as quiet.**
`DungeonSaveController` restores a floor's FEATURES before its INFLUENCE.
`TileInfluenceManager.LoadSaveData` opens with `minedTiles.Clear()` and rebuilds
from the save, so every `MarkNaturalFloor` the feature load performed moments
earlier is discarded -- while the reveals it performed survive, fog being a
tilemap that call never touches. Ground therefore came back REVEALED but not
OPEN, and the renderer frames nothing next to it, so it drew as bare floor.
That call also restored mined cells without revealing them, so dug ground the
player had not also claimed came back under fog with its wall caps painted
beneath. Two symptoms, one ordering fault, and both invisible on a fresh
generation: the site pass measured this clean by hand because it never
reloaded. `TerrainFeatureGenerator.ReassertOpenGround()` runs after the
influence restore, and `Dungeon Core / Commands / Validate Reveal Consistency`
is the standing check -- run it after a LOAD, not after a generate.

Preferred belt where a subscription is genuinely optional: subscribe in
`Start()` rather than `OnEnable()`. Unity guarantees every `Awake` completes
before any `Start`, which removes the race by language rule instead of by
ordering convention. `SpawnerSelectionController` already does this. It only
works for subscriptions that never need re-making on re-enable.

Best belt of all, where the signal has observable state behind it: do not
depend on the event for correctness. `Minimap` re-hooks whenever its cached
floor differs from `FloorManager.ActiveFloor`, so the subscription is an
optimisation and a missed event costs one frame instead of a session
(Appendix C).