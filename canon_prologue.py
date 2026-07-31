#!/usr/bin/env python3
"""
Canon back-fill: the living prologue (TutorialTown -> TutorialForest -> Ceremony).

The prologue shipped without canon coverage - three incidental mentions, none
of them documenting it. This writes the missing section and repairs one stale
forward reference that called the prologue future tense.

Idempotent and safe to re-run: every edit checks whether it is already applied
before touching anything, and every anchor must match exactly once.

Usage:
    python3 canon_prologue.py            # dry run - reports, writes nothing
    python3 canon_prologue.py --apply    # writes Docs/DESIGN_CANON.md
    python3 canon_prologue.py --apply --path /path/to/DESIGN_CANON.md
"""

import argparse
import sys

DEFAULT_PATH = "Docs/DESIGN_CANON.md"


# -- Edit 1: the stale forward reference ---------------------------------------
# The bootstrap note was written while the prologue was still ahead of us; the
# scenes had in fact already shipped. Keep the sentence, fix the tense.

STALE_OLD = """game via `bootstrapUnlocked`; they re-lock behind the tutorial wisp when the
prologue lands. **Realised:** the trio's `bootstrapUnlocked` flags are now"""

STALE_NEW = """game via `bootstrapUnlocked`; they re-lock behind the tutorial wisp (the
prologue is built - see The Living Prologue). **Realised:** the trio's
`bootstrapUnlocked` flags are now"""


# -- Edit 2: the missing section -----------------------------------------------

SECTION_MARKER = "## The Living Prologue"

SECTION = """

## The Living Prologue (Town, Forest, Ceremony)

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
"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write changes (default: dry run)")
    ap.add_argument("--path", default=DEFAULT_PATH, help=f"canon path (default: {DEFAULT_PATH})")
    args = ap.parse_args()

    try:
        with open(args.path, encoding="utf-8") as handle:
            text = handle.read()
    except FileNotFoundError:
        print(f"FAIL  canon not found at {args.path}")
        print("      run from the repo root, or pass --path")
        return 1

    original = text
    planned = []
    skipped = []

    # -- Edit 1 ---------------------------------------------------------------
    if STALE_NEW in text:
        skipped.append("stale forward reference: already repaired")
    else:
        count = text.count(STALE_OLD)
        if count != 1:
            print(f"FAIL  stale-reference anchor matched {count} times, expected 1")
            print("      canon has drifted; re-read before writing")
            return 1
        text = text.replace(STALE_OLD, STALE_NEW)
        planned.append("repair the stale 'when the prologue lands' forward reference")

    # -- Edit 2 ---------------------------------------------------------------
    if SECTION_MARKER in text:
        skipped.append("prologue section: already present")
    else:
        text = text.rstrip("\n") + "\n" + SECTION
        planned.append("append the Living Prologue section")

    # -- Report ---------------------------------------------------------------
    for item in skipped:
        print(f"SKIP  {item}")
    for item in planned:
        print(f"PLAN  {item}")

    if not planned:
        print("\nNothing to do - canon is already current.")
        return 0

    if not args.apply:
        print(f"\nDry run. {len(planned)} edit(s) ready; re-run with --apply to write.")
        return 0

    with open(args.path, "w", encoding="utf-8") as handle:
        handle.write(text)

    delta = len(text) - len(original)
    print(f"\nWrote {args.path} ({delta:+d} bytes, {len(planned)} edit(s)).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
