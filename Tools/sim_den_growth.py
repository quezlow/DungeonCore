#!/usr/bin/env python3
"""
Headless simulation of DEN GROWTH -- hoard, tier and raid pressure (canon 42).

There is no Unity here, so this proves nothing about rendering or pathing. What
it CAN answer, before a line of C# exists, is the only question that decides
whether the feature is worth building: does hoard -> tier -> raid produce a
READABLE escalation on a plausible timescale, or does it saturate in three days
and sit at max for the rest of the run?

THE STRUCTURAL PROPOSAL BEING TESTED. Canon 42 says the goblin hoard feeds tier
and tier feeds raids, but never says what raises a KOBOLD's tier -- they do not
steal. Rather than invent a second system, both dens share ONE ledger, ONE tier
curve and ONE raid table, and differ only in the INCOME FUNCTION:

    Occupier (goblins)   earn by THEFT       -- loose loot on their floor,
                                                plus a cut of the player's gold
                                                on a successful raid.
    Excavator (kobolds)  earn by EXCAVATION  -- spoil per cell dug, plus a lump
                                                for each buried remains taken.

One controller, one save shape, one tier curve, and the verbs stay distinct.
Clearing a den returns its hoard either way.

AS OF STAGE 2b THE EXCAVATOR EARNS BY BOTH, and the second stream is kept out
of tier on purpose. Kobolds now steal as well as dig, but theft is paid into a
separate additive `stolenHoard` that TierOf cannot see -- so `hoard` stays
exactly cellsDug x spoilPerCell plus the remains lumps, "tier 5 IS the completed
hole" survives a den that also robs, and Den Cavity Report's coupling assertion
stays a static bound. Clearing pays hoard + stolenHoard. The share table below
is half the occupier's; the count that hits it is DenController.ThievesByTier.

TWO FAULTS IN THIS FILE WERE FIXED IN THE SAME PASS, both of which had already
reached canon. Its excavator was modelled on the PRE-COUPLING rates and with no
geometry ceiling, so it reported a typical hoard of 2073 against the coupled
model's 1560; and it modelled expansion off gold-per-day rather than off claimed
cells, which is what DenController.ExpansionMultiplier actually reads. Both are
now taken from one place, and sim_den_cavity_growth.py asserts the two files
agree rather than trusting that they do.

EVERY INPUT BELOW WAS READ FROM SOURCE, not remembered:

  DayNightCycle          dayDuration 180 + nightDuration 60 = 240s per day.
  WaveStageController    WaveStage is a PROGRESSION (Dormant/Animals/Commoners/
                         Adventurers), not a day/night gate -- so once in the
                         Adventurers stage parties arrive continuously.
  AdventurerSpawner      intervalLow 30 / intervalMedium 20 / intervalHigh 10,
                         banded at notoriety 25 and 75; party size 1-3.
  Adventurer.prefab      LootTable gold 3 (weight 2) and 8 (weight 1), owner
                         Adventurer -> DroppedLoot. Expected 4.667 per kill.
  DungeonCore            killNotoriety 5, notorietySoftCap 100,
                         notorietyMinGainFraction 0.15 (soft-capped accrual).
  Monster prefabs        Authored expected drops, from the shipped weights:
                         Skeleton (2 w3 / 5 w1) = 2.75, Zombie (0 w1 / 2 w2) =
                         1.33, Armored Skeleton (5 w2 / 10 w1) = 6.67. Owner is
                         Monster -> CarriableLoot. The roster is being authored
                         out across 44 prefabs, so monster loot is now modelled
                         as PRESENT by default rather than optional.
  DungeonAdventurer      ScanForLoot picks up CarriableLoot within pickupRadius
                         0.6 and carries it OUT -- so monster drops are
                         CONTESTED and adventurer drops are not. This is the
                         difference between the two streams and the reason they
                         cannot be added together and left at that.
  LootAbsorbGate         loose loot lingers >= 30s, held while adventurers are
                         within 5 units, so a scavenging window exists at all.

Usage:  python3 sim_den_growth.py [days] [--verbose]
"""

import sys

# ---- read from source ---------------------------------------------------

DAY_SECONDS = 180.0 + 60.0        # DayNightCycle
INTERVAL_LOW = 30.0               # AdventurerSpawner
INTERVAL_MED = 20.0
INTERVAL_HIGH = 10.0
NOTORIETY_MED = 25.0
NOTORIETY_HIGH = 75.0
PARTY_MIN, PARTY_MAX = 1, 3
ADV_GOLD_EXPECTED = (3 * 2 + 8 * 1) / 3.0     # 4.667, DroppedLoot

# Authored monster drops, expected value per weighted table. The roster spans
# cheap early undead to richer later monsters; the mean is what a mixed defence
# loses on average.
MONSTER_GOLD_BY_EXAMPLE = {
    "skeleton": (2 * 3 + 5 * 1) / 4.0,          # 2.75
    "zombie": (0 * 1 + 2 * 2) / 3.0,            # 1.33
    "armoured skeleton": (5 * 2 + 10 * 1) / 3.0,  # 6.67
}
MONSTER_GOLD_EXPECTED = sum(MONSTER_GOLD_BY_EXAMPLE.values()) / 3.0   # 3.58

# Player monsters lost per adventurer killed. A defence that trades one monster
# for three adventurers is doing well; one for two is ordinary.
MONSTER_LOSS_RATIO = 0.4

# Share of monster drops an adventurer reaches first. CarriableLoot is what
# adventurers are FOR -- they pick it up and carry it out -- so goblins are the
# third party at that corpse, not the first.
ADVENTURER_TAKE_OF_MONSTER_LOOT = 0.45
KILL_NOTORIETY = 5.0
NOTORIETY_SOFT_CAP = 100.0
NOTORIETY_MIN_FRACTION = 0.15

# ---- the knobs this sim exists to choose --------------------------------

# Geometric-ish thresholds. A flat curve would put a den at max tier while the
# player is still on Bronze; a steeper one would never leave tier 1.
TIER_THRESHOLDS = [0, 60, 200, 550, 1400]      # tier 1..5 entry hoard

# Share of the loose loot on their floor that goblins actually reach. Not 1.0:
# the window is 30s, they have to be near, and the player's own monsters are
# competing for the same corpses.
#
# TIER 1 MUST BE NON-ZERO. The first version had 0.0 here and the den could
# never start: no income at tier 1 means no hoard, which means no tier 2, for
# ever. Any income curve gated on its own output needs a non-zero seed, and this
# is the kind of deadlock that looks like "the feature does nothing" in play.
STEAL_SHARE_BY_TIER = [0.06, 0.12, 0.20, 0.30, 0.42]

# Raids: mean days between, and what a successful one takes.
#
# A FLAT tier-scaled amount, CAPPED as a fraction of the player's gold -- not a
# pure percentage. A percentage was measured first and rewarded the wrong thing:
# a player who spent to zero paid nothing at all (0 gold lost against 8,174 for
# a hoarder), so the rule punished SAVING rather than punishing losing. Flat
# always bites; the cap stops it emptying a poor dungeon.
RAID_INTERVAL_BY_TIER = [14.0, 9.0, 6.0, 4.0, 3.0]
RAID_FLAT_BY_TIER = [12.0, 30.0, 65.0, 130.0, 240.0]
RAID_CAP_FRACTION = 0.25

# Excavator income. Dig rate rises with tier, so a kobold den accelerates.
#
# THE BASE IS NOT THE SHIPPED RATE, and this file printed the shipped rate
# WRONG for two releases because the distinction was only in the other sim.
# Fork 4b coupled the ledger to geometry and scaled these rates by DIG_SCALE;
# sim_den_cavity_growth.py SOLVES the spoil and SWEEPS the scale against this
# base, so the base has to stay here for it to sweep against. What was missing
# is the RESULT, declared below and asserted over there -- canon 42 recorded a
# theft table measured on this file while it was still modelling the
# pre-coupling excavator, which is the load-bearing-artefact failure that entry
# already records once for a destroyed sim and now records twice.
DIG_CELLS_PER_DAY_BASE = [7, 11, 17, 26, 38]

# Read off DenController.cs as shipped. sim_den_cavity_growth.py asserts that
# what it solves and sweeps equals these, so a re-tune there that is not
# reflected here FAILS LOUDLY instead of leaving two sims quietly disagreeing.
DIG_SCALE = 0.22                  # DenController.DigCellsPerDay / the base above
SPOIL_PER_CELL = 7.8              # DenController.spoilPerCell
DIG_CELLS_PER_DAY_BY_TIER = [round(c * DIG_SCALE, 1) for c in DIG_CELLS_PER_DAY_BASE]
REMAINS_LUMP = 120.0              # a buried remains taken; at most sitesPerFloor

# Days a remains is reached on, WHEN ONE IS REACHED AT ALL. Both ends are
# printed figures from Tools/sim_den_digger.py section J at the shipped cap
# (2400) and budget (x3), typical dungeon: the first find lands about day 65 and
# the digging stops about day 104, so every lump a den will ever take falls
# inside that window and the middle value is the midpoint of it.
#
# THE DEFAULT NUMBER OF LUMPS IS ZERO, AND THAT IS THE MEDIAN DUNGEON RATHER
# THAN AN OMISSION. The same section measures only 14.4 per cent of typical
# dungeons as EVER losing a remains, so most excavator ledgers carry none at
# all. The old model here credited two lumps on every run, on days 16 and 21,
# which is both too many and far too early -- 240 gold arriving before day 25
# against thresholds of 200 and 550 pulled tier 3 and tier 4 forward by five and
# nine days. The tail is stress-tested where it belongs, in
# sim_den_cavity_growth.py section R, which asks whether a lump can break the
# coupling and therefore models it as always present and early on purpose.
LUMP_DAYS = (65, 90, 104)

# Read off DenTunnelProfile.asset, floor index 2. The excavator's dig is BOUNDED
# BY GEOMETRY -- it can only ever open the cells between tier 1 and the reserve
# -- and this file modelled it as unbounded, which is the other half of the same
# fault. Declared here rather than in sim_den_cavity_growth.py so there is ONE
# copy and the import runs one way only.
RESERVE_MIN = 350
RESERVE_MAX = 400
TIER1_CELLS = 150

# What an EXCAVATOR steals, by tier (canon 42, stage 2b). Half the occupier's
# share, and nothing at all at tier 1 -- a tier-1 kobold den is two bodies, one
# at the face and one at home, so there is nobody left to send. The COUNT that
# hits it is ThievesByTier {0,1,2,3,4} in DenController, which is the occupier's
# forager count halved and floored; the share is the target and the count is the
# knob, exactly as it is for the goblins.
EXCAVATOR_STEAL_SHARE_BY_TIER = [0.0, 0.06, 0.10, 0.15, 0.21]

# How strongly a wider dungeon feeds the diggers. 0 would restore the pure
# clock; 1.0 makes a fast-expanding player face roughly double the dig rate.
EXPANSION_SENSITIVITY = 0.8

# DenController.expansionBaselineCells. The shipped ExpansionMultiplier reads
# TileInfluenceManager.ClaimedTiles.Count and nothing else, so CLAIMED CELLS are
# the driver. This file used gold-per-day as a proxy for it, which was a second
# model of one thing and disagreed with the first: the proxy is constant from
# day one, while a real dungeon starts small and spreads, so the proxy ran an
# excavator's early tiers fast and put tier 4 on day 23 against the coupled
# model's day 34. Proxy deleted.
EXPANSION_BASELINE_CELLS = 900

GRACE_DAYS = 5                    # canon 42

# Horizon the coupling check is judged over, deliberately independent of the
# `days` argument: the argument sizes the display table, and letting it also
# size an assertion makes the verdict a property of how the file was invoked.
# sim_den_cavity_growth.py makes the same split for the same reason.
VERDICT_DAYS = 150


def interval_for(notoriety):
    if notoriety >= NOTORIETY_HIGH:
        return INTERVAL_HIGH
    if notoriety >= NOTORIETY_MED:
        return INTERVAL_MED
    return INTERVAL_LOW


def tier_for(hoard):
    t = 1
    for i in range(1, len(TIER_THRESHOLDS)):
        if hoard >= TIER_THRESHOLDS[i]:
            t = i + 1
    return t


# ---- the loot-supply model, lifted out so there is only ONE of it -------
#
# sim_den_cavity_growth.py needs the same floor gold to measure theft against
# the COUPLED excavator, and a second copy of this arithmetic is a second thing
# to keep in step with AdventurerSpawner. Extracted rather than duplicated; the
# occupier table above is byte-identical across the extraction, which is the
# check that it was a refactor and not a change.

def expansion_multiplier(claimed_cells):
    """DenController.ExpansionMultiplier, mirrored exactly."""
    ratio = claimed_cells / float(max(1, EXPANSION_BASELINE_CELLS))
    return max(0.5, min(1.8, 1.0 + EXPANSION_SENSITIVITY * (ratio - 1.0)))


def kills_on_day(notoriety, kill_rate):
    """Adventurers killed in one day at this notoriety."""
    parties = DAY_SECONDS / interval_for(notoriety)
    return parties * (PARTY_MIN + PARTY_MAX) / 2.0 * kill_rate


def accrue_notoriety(notoriety, kills):
    """DungeonCore.AccrueKillNotoriety, mirrored -- soft-capped per kill."""
    for _ in range(int(kills)):
        headroom = max(0.0, min(1.0, 1.0 - notoriety / NOTORIETY_SOFT_CAP))
        notoriety += KILL_NOTORIETY * (
            NOTORIETY_MIN_FRACTION
            + (1.0 - NOTORIETY_MIN_FRACTION) * headroom)
    return notoriety


def floor_gold_for(kills, monster_loot=True):
    """Loose gold left on the floor by one day of fighting.

    Two streams, and they are NOT interchangeable. Adventurer drops
    (DroppedLoot) are uncontested: only the core takes them, and only after the
    LootAbsorbGate hold. Monster drops (CarriableLoot) are contested, because
    picking them up and carrying them out is what adventurers are FOR -- a den
    body is the third party at that corpse.
    """
    gold = kills * ADV_GOLD_EXPECTED
    if monster_loot:
        gold += (kills * MONSTER_LOSS_RATIO * MONSTER_GOLD_EXPECTED
                 * (1.0 - ADVENTURER_TAKE_OF_MONSTER_LOOT))
    return gold


def run(kind, days, kill_rate, player_gold_per_day, monster_loot,
        verbose=False, reserve=RESERVE_MIN,
        claimed_start=450, claimed_per_day=18, lumps=0,
        steal_share=None):
    """One run.

    Returns (tier_first_reached_day, hoard, raids, stolen, stolen_hoard).

    TIER READS `hoard` ALONE, and for an excavator that is now the whole point.
    Stage 2b gives kobolds a second income -- theft -- and canon 42 keeps it out
    of tier deliberately: hoard stays exactly cellsDug x spoilPerCell plus the
    remains lumps, so "tier 5 IS the completed hole" survives a den that also
    robs, and Den Cavity Report's coupling assertion stays a static bound rather
    than becoming a permanent red. The purse the player is paid on clearing is
    hoard + stolen_hoard; the purse the DEN is sized by is hoard.
    """
    hoard = 0.0
    stolen_hoard = 0.0                # excavators: additive, EXCLUDED FROM TIER
    notoriety = 0.0
    player_gold = 0.0
    raids = 0
    stolen_total = 0.0
    lump_days = set(LUMP_DAYS[:lumps])
    share = (EXCAVATOR_STEAL_SHARE_BY_TIER if steal_share is None
             else steal_share)
    next_raid_in = None
    first_day_at_tier = {}
    dig_headroom = max(0, reserve - TIER1_CELLS)
    claimed = float(claimed_start)

    for day in range(1, days + 1):
        tier = tier_for(hoard)
        first_day_at_tier.setdefault(tier, day)

        # -- the wave, and the loot it leaves on the floor ---------------
        kills = kills_on_day(notoriety, kill_rate)
        notoriety = accrue_notoriety(notoriety, kills)
        floor_gold = floor_gold_for(kills, monster_loot)

        player_gold += player_gold_per_day
        claimed += claimed_per_day

        if day <= GRACE_DAYS:
            if verbose:
                print("  day %2d  dormant (grace)" % day)
            continue

        # -- income ------------------------------------------------------
        if kind == "occupier":
            take = floor_gold * STEAL_SHARE_BY_TIER[tier - 1]
            hoard += take
            stolen_total += take
        else:
            # Excavation is otherwise a PURE CLOCK -- identical on every player
            # profile, which the first run showed plainly and which is the real
            # weakness of digging-as-income. So the rate responds to the
            # player's own expansion on that floor: a sprawling dungeon gives
            # the diggers more rock worth opening and more to run into. Same
            # logic as entry 12A scaling creep by claim count, and it hands the
            # player a lever that is not simply "go and kill it".
            expansion = expansion_multiplier(claimed)
            # BOUNDED BY GEOMETRY. The ledger pays on cells ACTUALLY OPENED
            # (fork 4b), so the dig stops dead when the reserve is spent and the
            # hoard freezes there for ever. Modelling it as an endless clock is
            # what let this file report an excavator hoard of 2073 against the
            # coupled model's 1560, and canon quoted the wrong one.
            dug = min(DIG_CELLS_PER_DAY_BY_TIER[tier - 1] * expansion, dig_headroom)
            dig_headroom -= dug
            hoard += dug * SPOIL_PER_CELL
            # A remains lump is part of the HOARD INVARIANT, not of theft --
            # Print Den Ledger asserts hoard == cellsDug x spoilPerCell +
            # remainsTaken x remainsLump, and this is the second term. Off by
            # default: see LUMP_DAYS for why the median dungeon takes none.
            if day in lump_days:
                hoard += REMAINS_LUMP
            # THEFT (canon 42, stage 2b). Half the occupier's share, carried by
            # bodies exactly as the goblins' is, and paid into a purse that tier
            # cannot see. This is what gives an excavator a payday that keeps
            # growing after its hole is finished -- without it the purse freezes
            # about day 50 and there is no longer any reason to delay clearing.
            theft = floor_gold * share[tier - 1]
            stolen_hoard += theft
            stolen_total += theft

        # -- raids -------------------------------------------------------
        if next_raid_in is None:
            next_raid_in = RAID_INTERVAL_BY_TIER[tier - 1]
        next_raid_in -= 1
        if next_raid_in <= 0:
            raids += 1
            cut = min(RAID_FLAT_BY_TIER[tier - 1],
                      player_gold * RAID_CAP_FRACTION)
            player_gold -= cut
            if kind == "occupier":
                hoard += cut          # the raid cut joins the hoard
            else:
                # AND NOW AN EXCAVATOR'S DOES TOO. Canon 42's rule is that a
                # raid cut in a pot is a decision and a vanished one is a bleed;
                # the Occupier-only gate existed solely because an excavator had
                # no pot that tier could not see. stolenHoard is that pot, so
                # the only pure bleed left in the den system closes here.
                stolen_hoard += cut
            next_raid_in = RAID_INTERVAL_BY_TIER[tier - 1]

        if verbose:
            print("  day %2d  tier %d  hoard %7.0f  stolen %7.0f  "
                  "notoriety %5.1f  raids %d"
                  % (day, tier, hoard, stolen_hoard, notoriety, raids))

    return first_day_at_tier, hoard, raids, stolen_total, stolen_hoard


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 60
    verbose = "--verbose" in sys.argv

    print("Den growth -- %d days simulated" % days)
    print("day = %.0fs (DayNightCycle 180+60); parties continuous once in the "
          "Adventurers stage" % DAY_SECONDS)
    print("tier thresholds: %s" % TIER_THRESHOLDS[1:])
    print()

    profiles = [
        # label,               kill_rate, gold/day, claimed at start, claimed
        #                      per day, monster loot authored.
        #
        # The claim drivers are the ones sim_den_cavity_growth.py uses, so the
        # two files model one dungeon rather than two.
        ("passive dungeon",       0.45,    40.0,  300,  8, True),
        ("typical dungeon",       0.75,    90.0,  450, 18, True),
        ("killer dungeon",        0.95,   160.0,  600, 30, True),
        ("typical, adv drops only", 0.75,  90.0,  450, 18, False),
    ]

    for kind in ("occupier", "excavator"):
        print("== %s ==" % kind.upper())
        print("%-22s %-8s %-8s %-8s %-8s %-8s %-8s %-8s %-7s"
              % ("profile", "T2 day", "T3 day", "T4 day", "T5 day",
                 "hoard", "stolen", "payout", "raids"))
        for label, kr, pg, c0, cpd, ml in profiles:
            # Excavators DO touch loose loot as of stage 2b, so the
            # adventurer-drops-only variant is a real run for them now and is no
            # longer skipped. (The first version skipped on `ml` being true,
            # which silently hid every excavator profile once monster loot
            # became the default -- a filter that hides its subjects and still
            # reports a pass.)
            first, hoard, raids, stolen, purse = run(
                kind, days, kr, pg, ml, verbose,
                claimed_start=c0, claimed_per_day=cpd)
            def d(t):
                return str(first[t]) if t in first else "-"
            print("%-22s %-8s %-8s %-8s %-8s %-8.0f %-8.0f %-8.0f %-7d"
                  % (label, d(2), d(3), d(4), d(5), hoard, purse,
                     hoard + purse, raids))
        print()

    # ---- the record of why the excavator's share is what it is ---------
    #
    # Canon 42 carried a theft table whose TIER COLUMNS WERE A PREDICTION, made
    # under a shape the arc then rejected: theft fed hoard there, so it moved
    # tier. Under the shipped stolenHoard shape it cannot, and this table is
    # the re-derivation -- read the tier columns across and they do not move,
    # which is the claim, not a coincidence.
    print("Excavator theft share -- what each option pays, typical dungeon")
    print("(tier columns are IDENTICAL by construction: theft is excluded "
          "from tier)")
    print("%-26s %-7s %-7s %-7s %-7s %-8s %-8s %-8s"
          % ("share", "T2", "T3", "T4", "T5", "hoard", "stolen", "payout"))
    options = [
        ("none", [0.0] * 5),
        ("0.25x the occupier share", [round(v * 0.25, 4)
                                      for v in STEAL_SHARE_BY_TIER]),
        ("0.5x  (SHIPPED)", EXCAVATOR_STEAL_SHARE_BY_TIER),
        ("full occupier share", list(STEAL_SHARE_BY_TIER)),
    ]
    tier_days = []
    for name, tbl in options:
        first, hoard, _r, _s, purse = run(
            "excavator", days, 0.75, 90.0, True, False,
            claimed_start=450, claimed_per_day=18, steal_share=tbl)
        tier_days.append(tuple(first.get(t) for t in (2, 3, 4, 5)))

        def d(t):
            return str(first[t]) if t in first else "-"

        print("%-26s %-7s %-7s %-7s %-7s %-8.0f %-8.0f %-8.0f"
              % (name, d(2), d(3), d(4), d(5), hoard, purse, hoard + purse))
    print()

    # ---- the checks that decide whether the curve is usable ------------
    print("Verdict checks:")
    bad = False
    if len(set(tier_days)) != 1:
        print("!! THEFT MOVED THE TIER DAYS: %s. stolenHoard is meant to be "
              "invisible to TierOf, so either it is being added to hoard "
              "somewhere or the exclusion has been undone -- and Den Cavity "
              "Report's coupling assertion goes red with it."
              % (sorted(set(tier_days)),))
        bad = True
    else:
        print("   theft leaves the tier days untouched at every share: "
              "T2/T3/T4/T5 = %s." % (tier_days[0],))

    # The other half of the same statement, from the other side: an excavator's
    # hoard must never exceed what geometry can pay for. This is Den Cavity
    # Report's coupling assertion in ledger form, and it is what would fail
    # first if theft were ever credited to hoard by mistake.
    firstc, hoardc, _rc, _sc, _pc = run("excavator", VERDICT_DAYS, 0.75, 90.0,
                                        True, False)
    ceiling = (RESERVE_MIN - TIER1_CELLS) * SPOIL_PER_CELL
    if hoardc > ceiling + 0.5:
        print("!! excavator hoard %.0f exceeds the geometric ceiling of %.0f "
              "(%d diggable cells x %.1f spoil) -- something outside the dig "
              "is paying into hoard." % (hoardc, ceiling,
                                         RESERVE_MIN - TIER1_CELLS,
                                         SPOIL_PER_CELL))
        bad = True
    else:
        print("   excavator hoard tops out at %.0f against a geometric "
              "ceiling of %.0f -- coupled." % (hoardc, ceiling))
    first, hoard, raids, _, _ = run("occupier", days, 0.75, 90.0, True)
    if 2 not in first or first[2] > 20:
        print("!! occupier never reaches tier 2 inside 20 days on a typical "
              "dungeon -- the den would read as inert.")
        bad = True
    if 5 in first and first[5] < 20:
        print("!! occupier hits MAX tier by day %d -- saturates, so the rest of "
              "the run has a flat threat." % first[5])
        bad = True
    firstx, hoardx, _, _, _ = run("excavator", days, 0.75, 90.0, True)
    if 2 not in firstx or firstx[2] > 20:
        print("!! excavator never reaches tier 2 inside 20 days -- digging "
              "income is too thin to drive the shared curve.")
        bad = True
    if 5 in firstx and firstx[5] < 20:
        print("!! excavator saturates by day %d." % firstx[5])
        bad = True
    print()
    print("VERDICT: " + ("curve needs retuning -- see above" if bad else
                         "one shared curve paces both income functions"))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
