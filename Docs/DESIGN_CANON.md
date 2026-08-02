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

**Appendix** (at the end of the file)
A. Content Registries and Authoring Keys
B. Sorting Layer Contract

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

**Faction-vs-faction.** `FactionRelations.Between` already yields Neutral for
the Deep Holds against all four -- the Cultist clause needs a lawful faction
on the other side and they are not one -- so only an explicit martial strength
was added. The deep-faith reading would put them beside the Cultists and
against the Church; that is deliberately NOT written into the matrix, because
no shipped system runs a dwarf encounter and a relationship nothing exercises
is a claim that cannot yet be shown wrong. Revisit in The Living Holds arc
(entry 19's build order, step 7), which greenlights caravans and patrols --
the first dwarves a shipped system can meet in the field.

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
in the PauseMenuController chain, opens while paused -- pause-availability
audit backlogged). Paths are horizontal lanes (empty paths hidden, so
Sorcery stays absent), tiers are columns, prerequisite edges are elbow
connectors drawn only when both endpoints are visible; every node reserves
its layout slot so reveals never reflow. Node visibility is data-driven on
`TechNodeDefinition` (Always / PatternKnown / KeyUnlocked / KillsOfClass via
`RunStats.KillsByClass` / KillsAny via `RunStats.TotalKills`); among visible
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
The node roster shipped: 15 nodes (Sorcery still empty). Two faction-intel nodes join the Observation path (Study the Holy Order,
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
buried-skeleton Bestiary discovery. Alert severity tiers
(info/warning/critical). Faction payoffs as the mid-game gold sink and the
deliberate way to lower escalation tiers (see entry 7).

**Core spells / active abilities: GREENLIT, unscheduled.** The call is made;
the build is not started. Two things unblock on it: the Sorcery research
path (which hung on this decision and may now be planned), and the two
trader books already reserved by name in the merchant catalog -- Primer of
the First Spark and The Drawn Breath (see entry 28A).

**Standing ledger (recorded so it is not re-litigated).**

- *Carry weight / encumbrance:* BINNED. Not to be revisited without a new
  case.
- *Chest tiers:* OPEN, partially built. `ChestDefinition.ChestTier`
  (Bronze/Silver/Gold) exists and is a placement-picker LABEL only -- richer
  tiers are simply authored with higher-rarity `LootTable` entries, and no
  code outside `ChestDefinition.cs` reads the field. The open half is the
  behavioural hook: tier influencing adventurer decisions (which chest a
  Treasure Hunter beelines for, whether a richer chest holds a party longer
  or deepens a Destroyer's resolve). Do not record this as shipped.
- *Random world events framework:* DEFERRED, to be revisited. What exists is
  three bespoke recurring threats, each its own component --
  `HolyOrderStrike`, `MercenaryContract`, `WildMonsterEvent` (entry 8).
  There is no scheduler, event registry or data-driven authoring surface,
  and the Wandering Merchant runs its own arrival controller rather than
  riding a shared one.

## 19. Buried Age Sites and the Deep Roads

Status: PART SHIPPED. The road substrate, the sites, and the dwarves --
faction, outpost, vendor, spoil economy, and now the village (part 3) -- are
built and described as-built below. The granite overlay and road claiming
remain DESIGN and do not exist in code; caravans and patrols are GREENLIT into
The Living Holds arc (see the build order at the end of this entry). Do not
assume the classes or APIs of anything still marked DESIGN here. The tier-up
divine audiences that used to share this entry are now 19A and have no
dependency on any of this.

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
problem because it is exploration, not economy.

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
carved: those cells stay solid rock and are merely retyped to
`TerrainType.Ruins`. They therefore render as cave wall, cost Ruins resistance
to claim, and pay out the `ancient_masonry` pattern when mined -- all of which
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

**The ruins wall family.** `CaveWallSheetLayout` carries a parallel slot set
(`ruinsSheet` + caps, inner corners, faces, straight variety, paving) sliced
from `castle_interriors.png`, and `CaveWallRenderer` renders it for any wall
cell typed `TerrainType.Ruins`. Ruins cells never roll moss and render with a
WHITE tint -- the castle art is already thematic, and the lavender tint that
sells retinted cave rock as masonry would muddy real masonry. Every ruins
slot is optional: empty caps fall back to the ruins base cap (mask 11), empty
faces to the ruins Straight face, and only a wholly unassigned family falls
back to stone, keeping the pre-visual-pass look. Filling more masks and face
variants is Inspector work on `Data/CaveWallSheetLayout.asset`; the layout's
Validate Layout context menu checks bounds and flags surprising states. The
shipped fill: base cap (2,4), Straight faces (1,6)/(2,8), two pilastered wall
variants (7,9)-(7,10) and (10,9)-(10,10), no corner art yet.

**Site paving.** The carved interior is painted with paving variants -- the
shipped four are (15,37) and (16,37)-(16,39) -- one per cell by a spatial
hash (no RNG, stable across reloads). The paint rides
`ApplyRuinsOverrides`, which both the fresh-generation path and the load
path call after the disc paint; if the lazy floor-paint backlog item ever
lands, the paving pass must move with it. The carriageway is paved too:
the road cells a site yields at placement are recorded
(`SiteData.pavedRoadCells`, appended field, empty in old saves) and painted
with site paving on the ROAD tilemap -- both in the paving pass and inside
`PaintRoadSegment`, because road segments paint lazily on reveal and a later
reveal must not repaint road over the room floor. The room reads built
around the road; a river through the band still washes it out. Straight-wall
variety mixes the plain wall into the pool at `ruinsPlainWeight` (default 4,
so roughly two in three walls are plain against the two shipped pilaster
variants); weight 0 restores the all-pilaster look on purpose.

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
cell would hand the player the floor's layout for free. Segment ids advance even
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
board when the player finds it. The VENDOR, the spoil economy, the dwarven
traps, the granite overlay, road claiming and caravans are NOT built. Design
for the vendor and the economy is recorded below and in part 2's guide; the
overlay, claiming and caravans remain DESIGN in the sub-sections further down.

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
bought-only traps, and the spoil invoice. The granite boundary, road claiming
and caravans remain DESIGN, below.

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
reads as a hole, and THE DOOR RULE recorded in every file -- all doors,
passages, stall gaps and grave rows are 3 cells long, because the wall drape
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
a run can genuinely reach the village first -- stands its STATIC villagers
on interior cells picked by the builder's own walkable rule (which also keeps
them off the carriageway), raises a Discovery alert naming the hold, and
speaks `village_first` once. Clicking a villager repeats `village_greeting`.
No vendor: they trade at the gate, they live here. Villager art is a
variant list (`villagerSprites`), dealt by a seeded round-robin over a
shuffled copy so counts stay as even as the list allows; null entries are
skipped and an empty list draws nobody. `villagerCount` ships defaulting to 4
for The Living Holds arc to raise. The discovery alert
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

**The Living Holds (GREENLIT, the next arc).** The dwarves who WALK:
villagers with routines, patrols on the sparse network, and caravans crossing
between gatehouse and village that the player may rob, let pass, or tax.
Converting a held road stretch into a toll the player collects belongs here
too. The caravan is a multi-day presence rather than a dawn-to-dusk visit --
at 180s days and a 2.6 walk speed a traveller covers roughly 470 tiles per
day, so crossing the village floor takes days. Travel MUST be authored in
days with speed derived from it, never as tiles per second, or a longer day
silently halves the crossing. This arc is also the trigger entry 7 defers the
faction-vs-faction matrix to: patrols and caravans are the first dwarves a
shipped system can meet in the field.

**Reuse note.** `WanderingMerchantController` is a singleton with a static
next-visit day, bound to the surface arrival model (forest road, camp
commerce anchor, camp-tier gate, leaves at dusk). Almost none of that
transfers to a stationary in-dungeon vendor. `TraderStockCatalog` and the
purchase path (including `PatternDiscovery.NotifyTraderPurchase`) do.
`MerchantShopUI` should be decoupled from the merchant singleton onto a stock
provider BEFORE a second vendor exists, not after.

### Claiming the road (DECIDED)

The road is the first terrain in the game with an OPINION about being
claimed. Pushing influence across it is a diplomatic act, not a mining
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

This reuses the Holy Ground pattern wholesale (entry 18): special terrain,
procgen via `TerrainTypeMap`, visually distinct, with a faction consequence
on desecration. No new pattern is needed.

### Granite holdings overlay (DECIDED)

Dwarven holdings render with their own boundary, shown when the player claims
toward them, in COOL GREY GRANITE.

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
against that constraint.

**Implementation note.** Holdings are STATIC -- authored at generation,
changing only when the player takes a stretch. No Dijkstra, no reach
animation, no cost channel. A tilemap overlay redrawn on segment change is
likely sufficient; the ring's shader path is not required. If it ever is, the
field texture's A channel is free (R = boundary SDF, G = normalised growth
cost, B = exposed fringe, A = unused).

**Consequence worth keeping:** once both boundaries render, the moment the
player's ring touches the dwarves' becomes a VISIBLE EVENT rather than a
number in a panel -- which solves most of the "convey it to the player"
problem for free.

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
7. THE LIVING HOLDS -- GREENLIT, the next arc, fresh chat: living dwarves,
   patrols, caravans, and the entry-7 matrix revisit. Static villagers become
   walkers; `villagerCount` on the controller is the knob that grows.
8. Later, and still DESIGN: the granite boundary, road claiming and the
   warning ladder (blocked on an alert severity layer that does not exist --
   `AlertEntry` carries categories only).

## 19A. Tier-Up Divine Audiences

Status: DESIGN -- decided, unscheduled. Split out of entry 19, which it never
depended on: this is a progression-milestone reward and needs no terrain work,
no roads and no dwarves. It may ship at any time.

Decided: a divine audience at each tier-up milestone -- Bronze -> Silver ->
Gold -> Diamond -> God. The god of the CORE'S OWN TYPE attends, and grants
knowledge rather than power.

This is the deep-faith speaking directly to one of its own. Entry 20 records
that the old faith held divinity to reside below and that some dead are reborn
as cores; entry 21 records that its civilisation was entombed. The audiences
are the surviving other end of that -- the thing the Church suppressed,
answering. Each is an opportunity to feed deep-faith lore at the exact moment
the player has earned a reason to care.

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

**Bands and research:** band 0 (32 cells deep beyond the rim) is always on
and reproduces the old apron's look. Bands 1-3 reach total depths 45 / 70 /
100 and are gated by the authored scout chain `tech.scout_1/2/3` (Sight
Beyond the Threshold, Eyes on the Deep Wood, The Far Marches -- entrance-
discovery visibility, chained prerequisites). Research IS the cost of sight:
there is no per-second scouting mana and no scout trip.

**Sight creep:** a newly researched band paints in full the moment its key
unlocks; the camera bounds then creep outward to the new edge over roughly
`creepDays` day-night cycles (default 1), on scaled time -- pause halts the
spread, speed-up hastens it. The creep is monotonic (a chained unlock moves
the target further out) and unsaved (loading lands at full researched depth).
`DungeonBoundsUpdater` unions the revealed disc into the floor-0 confiner
AABB and exposes `MarkDirty()` for the generator. An edge-fog ring
(generator-painted tilemap above the props: alpha eased quadratically
across the last `fogFadeCells` (12) of painted ground, full solid landing
two cells past the edge, then holding for
`fogSolidMarginCells` (24) past it) hides the unpainted void at every
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

**Key files:** `Floors/SurfaceZoneGenerator.cs`, `Floors/SurfaceZoneProfile.cs`
(+ `SurfaceBand`, `SurfaceNodeType`), `Overworld/CampZoneMarker.cs`,
`Overworld/ResourceNodeStub.cs`; touches `DungeonCore/DungeonBoundsUpdater.cs`
(surface AABB union + `MarkDirty`), `Save/DungeonSaveController.cs`
(`RunContext` publish removed), `TESTING/Commands.cs` (scout toggles),
`Overworld/SceneTransitionTrigger.cs` and `Save/SpawnPoint.cs` (runtime
`Configure` initialisers for the gate).
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

Status: SHIPPED (persisted life, seven echoes, the empty-handed voice, the
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

The seven shipped echoes, one per affinity plus the old faith:

| Deed in life | Dungeon moment | Site |
|---|---|---|
| `dig_grave` | first deliberate raise | `CryptController.RaiseFromSarcophagus` |
| `free_net` | first adventurer pinned | `DungeonAdventurer.BeginPinned` |
| `take_offering` | first tribute absorbed | `TributeChest` |
| `give_alms` | first adventurer stripped | `DungeonAdventurer` death path |
| `mill_climb` | first descent below floor 0 | `FloorManager.SetActiveFloor` |
| `quench` | first trap fires on the living | `TrapBase.OnAdventurerEntered` |
| `pray_shrine` | first buried remains dug | `BuriedRemainsController.Grant` |

`Recall` is `IsLoading`-guarded (a restore replays history in a few frames and
none of it is happening to the player) and **speaks at most one echo per
frame** -- a capture trap resolves `BeginPinned` inside `ApplyEffect` and the
trap site then recalls in the same call, so a life holding both deeds would
otherwise hear two stacked on one snare. It mirrors `DeedsController.NotifyMoment`
-- whose moment ids the table deliberately reuses where they already exist, so
the two systems read as one vocabulary. The Light echo anchors on the **kill**
rather than `DroppedLoot.Absorb`, because the tribute coin flourish runs the
same absorb and would fire the wrong memory.

**Rejected:** a Holy Ground desecration echo (`AlignmentSystem.Desecrate` is a
stub with no caller) and a trap-*kill* echo (no kill attribution exists;
trap-fired is the honest hook).

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

**The wisp was already there.** It read the player's life back from flags it
had no business seeing, waiting at the rebirth site -- and entry 20 records
that deep shrines warded rebirth sites. It is plausibly a warden of the old
deep-faith doing a job it has done many times before, for cores that failed.
This reframes every prologue line at zero cost, explains the rare `Ancient`
and `Reverent` temperaments, and gives it standing to recognise a dead core's
ruin when one is found.

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

# APPENDIX

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
