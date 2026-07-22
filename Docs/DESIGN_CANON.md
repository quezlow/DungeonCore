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
8A. Notoriety Model (Gain Shaping, Tier Gates, Decay)
9. Endgame Climax (Diamond 3 Trial)
10. Assault Staging
11. Tribute and GiftGivers
12. Ambient Necromancy and Corpses
12A. Influence Field, Push and Breach Recede

**Part II -- Designed, not yet built**
13. Research Tree (Phase 4.5)
14. Material Pattern System
15. Room Effects v2 and Attractor Rooms
15A. Monster Muster (Spawn Rooms, Posts, Floor Gates)
16. Crypt and Deliberate Nemesis Raise
17. Discovery Content (Buried Skeletons, Loot Books, Wisp Guide)
18. Phase 5 Designs
19. Buried Age Sites and Tier-Up Audiences

**Part III -- Lore canon**
20. Why Holy Sites Are Underground
21. The Buried Age
22. Deeds
23. Trophy Hall
24. Surface World: Radial Forest Bands
25. Camp Growth, Identity & Effects

**Appendix**
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

## 11. Tribute and GiftGivers

Status: SHIPPED. Verified: 2026-07-09.

A GiftGiver party (Cultists only) has a bearer drop a `TributeChest` near the
entrance on arrival. Tribute chests are never openable by adventurers or the
player: after a short dwell they are absorbed straight into the core's gold
pool, reusing the DroppedLoot coin-flourish. Accepting tribute shifts
alignment **-3** (dark) and raises Cultist standing.

**Key files:** `Adventurer/TributeChest.cs`.

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
+30% -- income and speed are separate levers). The active project also
STALLS at any dawn when no valid Library stands (`pauseWithoutLibrary`,
serialized default on) and resumes when one does, each transition announced
once; the stall latch is transient. This is the as-built form of the
roadmap's "research pauses if the Laboratory is destroyed" -- the Library is
that room. Disabling the toggle restores the decoupled model (projects run to
completion on banked points regardless of Libraries). Affinity halves point cost
only. Bootstrap nodes (Remembered Bones, Remembered Spikes) unlock on new
game via `bootstrapUnlocked`; they re-lock behind the tutorial wisp when the
prologue lands. **Realised:** the trio's `bootstrapUnlocked` flags are now
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

## Guided Opening (TutorialDirector)

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
(5) designate a room, then arm a spawner -- `MonsterSpawner.OnSpawnerArmed` (new)
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
valve; tribute coin flourishes roll as Common). Trader and avatar
channels remain reserved catalog entries; the EVENT channel is live for
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
AABB and exposes `MarkDirty()` for the generator.

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
