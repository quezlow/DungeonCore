#!/usr/bin/env python3
"""
Headless simulation of EXCAVATOR CAVITY GROWTH (canon 42, den cavity half B).

sim_den_cavity.py measured the HOLE. sim_den_growth.py measured the LEDGER.
Neither measured the thing half B creates by joining them: DigCellsPerDay now
drives GEOMETRY as well as hoard, and geometry has a hard ceiling the ledger
never had -- reserveCells minus cavityTier1Cells.

THE COLLISION THIS FILE FOUND, AND WHY THE SHIPPED NUMBERS COULD NOT STAND.
At the shipped spoilPerCell of 1.4, an excavator's LIFETIME income is the
200-250 cells it can ever dig times 1.4 -- 280 to 350 hoard against a tier-5
threshold of 1400. Paying the ledger on cells ACTUALLY opened therefore capped
every excavator at tier 3, froze its hoard on day 23-40, and made the tier-4
and tier-5 rows of the raid table dead content. The failure is invisible in
play: a den that has stopped earning looks exactly like a den earning slowly.

THE RESOLUTION (fork 4b). Couple the two and re-tune the EXCAVATOR'S OWN
knobs. spoilPerCell is read in exactly one place, inside the DenKind.Excavator
branch of EarnByDigging, so moving it cannot touch the occupier -- the shared
thresholds, the shared raid table and the occupier's measured pacing all stand
exactly as canon shipped them. Only two excavator numbers move.

WHAT THE COUPLING BUYS, AND IT IS THE REASON TO PREFER IT: tier 5 becomes THE
COMPLETED HOLE. The geometry and the ledger stop being two accounts of one den
that can drift; a den at full strength is visibly a den that has finished
digging, and a stalled dig is visibly a stalled ledger.

TWO CONSTRAINTS, AND THE FIRST IS THE ONE A SINGLE SAMPLE WOULD HAVE MISSED:

  1. THE RESERVE IS A BAND, 350-400, so the lifetime dig budget is 200-250
     cells depending on the seed. Sizing spoil against the MAXIMUM would leave
     every reserve-350 den permanently short of tier 5 -- a seed-dependent
     cliff, invisible, and exactly the "a maximum is the least stable
     statistic" trap canon 42 records. Spoil is therefore solved against the
     MINIMUM reserve; a wider hole overshoots into a tier that caps anyway.
  2. THE REMAINS LUMP NOW HAS A CALLER, AND THIS FILE HAS BEEN RE-RUN, which
     is what its own header used to promise for the day the digger pass
     landed. Section R below is that re-run. THE ANSWER IS THAT THE NUMBERS
     STAND: on a typical or killer dungeon the lump changes the tier-5 day by
     NOTHING AT ALL, because the first find lands about day 65 and tier 5 is
     already reached on day 49 or day 40 -- the lump arrives after the race is
     over and only pads the hoard. Only a passive dungeon moves, and only by
     one day, which is less than the reserve BAND already moves it.

  3. KOBOLD THEFT CANNOT REACH THE COUPLING AT ALL (stage 2b). Section T.
     Theft is paid into stolenHoard, tier reads hoard alone, and Den Cavity
     Report's assertion is a static bound on diggable cells times spoil -- so
     it is the same number at every share. That is the property canon 42
     rejected the first shape for lacking.

Usage:  python3 sim_den_cavity_growth.py [days]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim_den_growth import (
    DIG_CELLS_PER_DAY_BASE,
    DIG_SCALE,
    EXCAVATOR_STEAL_SHARE_BY_TIER,
    GRACE_DAYS,
    LUMP_DAYS,
    REMAINS_LUMP,
    RESERVE_MAX,
    RESERVE_MIN,
    SPOIL_PER_CELL,
    TIER1_CELLS,
    TIER_THRESHOLDS,
    expansion_multiplier,
    tier_for,
)

# EVERY CONSTANT ABOVE IS IMPORTED RATHER THAN RESTATED, and this file used to
# restate four of them. The import runs ONE WAY: sim_den_growth.py holds what was
# read off source and off DenTunnelProfile.asset, and this file SOLVES and
# SWEEPS against it. main() then asserts that what it solves equals what the
# other file declares as shipped -- so a re-tune here that never reaches the
# other file fails loudly instead of leaving two sims quietly disagreeing about
# the same den, which is exactly what happened for two releases.

# Canon 42 records the excavator reaching tier 5 about day 49. Coupled, that
# day is now also the day the hole is finished, so it is the single target
# both knobs are solved against.
TARGET_TIER5_DAY = 49

# Horizon the VERDICT is judged over, deliberately independent of the `days`
# argument. The argument sizes the display table; letting it also size the
# assertions made the verdict a property of how the file was invoked rather
# than of the design -- the default of 90 days reported FAIL because a hermit
# who claims almost nothing floors the expansion multiplier at 0.5 and needs
# 94 days, which is the multiplier working correctly rather than a fault. A
# standing regression test that fails on its own default is worse than no test,
# because the next reader learns to ignore it.
VERDICT_HORIZON = 150


def run(days, claimed_start, claimed_per_day, reserve, spoil, dig_scale):
    """One COUPLED excavator run: the ledger is paid on cells actually opened.

    Returns (first_day_at_tier, hoard, cells_open, day_reserve_spent).
    """
    hoard = 0.0
    cells_open = TIER1_CELLS
    headroom = reserve - TIER1_CELLS
    claimed = float(claimed_start)
    first_day_at_tier = {}
    day_spent = None

    for day in range(1, days + 1):
        tier = tier_for(hoard)
        first_day_at_tier.setdefault(tier, day)
        claimed += claimed_per_day

        if day <= GRACE_DAYS:
            continue

        wanted = (DIG_CELLS_PER_DAY_BASE[tier - 1] * dig_scale
                  * expansion_multiplier(claimed))
        opened = min(wanted, headroom)
        headroom -= opened
        cells_open += opened
        hoard += opened * spoil          # COUPLED: intent pays nothing

        if headroom <= 0 and day_spent is None:
            day_spent = day

    return first_day_at_tier, hoard, cells_open, day_spent


PROFILES = [
    # label,            claimed at start, claimed added per day
    ("passive dungeon",      300,   8),
    ("typical dungeon",      450,  18),
    ("killer dungeon",       600,  30),
]


# Fraction of the SMALLEST hole that must be dug to reach tier 5. Not 1.0, and
# the first draft of this file proved why: solving spoil so the threshold is met
# by the very last cell put two of six profiles at tier 4 for ever on a hoard of
# 1399.9999999999998. That is float accumulation over forty-odd partial days,
# and the shipped ledger is a C# FLOAT rather than a double, so its error is
# larger still. A den that digs out its entire hole and is then denied the tier
# it earned is the invisible failure this whole arc is about. The margin also
# reads better: tier 5 arrives as the dig NEARS completion rather than on the
# final shovelful, so the beat survives a seed that rolled a wide hole.
TIER5_AT_FRACTION_OF_SMALLEST = 0.90


def solve_spoil():
    """Spoil that reaches tier 5 at TIER5_AT_FRACTION_OF_SMALLEST of the
    smallest hole, rounded to a tidy authored figure."""
    exact = TIER_THRESHOLDS[4] / (
        TIER5_AT_FRACTION_OF_SMALLEST * (RESERVE_MIN - TIER1_CELLS))
    return round(exact + 0.049, 1)      # round UP to a tenth, never down


def sweep_dig_scale(spoil, days):
    """Scale on DigCellsPerDay landing the typical dungeon's tier 5 nearest the
    day canon already recorded. Swept rather than solved: the expansion
    multiplier makes cells-per-day depend on the player, so there is no closed
    form."""
    best = None
    for step in range(5, 121):
        scale = step / 100.0
        first, _, _, _ = run(days, 450, 18, RESERVE_MIN, spoil, scale)
        if 5 not in first:
            continue
        miss = abs(first[5] - TARGET_TIER5_DAY)
        if best is None or miss < best[1]:
            best = (scale, miss, first[5])
    return best


def table(spoil, dig_scale, days):
    print("%-22s %-7s %-7s %-7s %-7s %-8s %-8s %s"
          % ("profile", "T2 day", "T3 day", "T4 day", "T5 day",
             "hoard", "cells", "hole finished"))
    worst = {"t5_missing": 0, "t5_early": None, "t5_late": None}
    for label, c0, cpd in PROFILES:
        for reserve in (RESERVE_MIN, RESERVE_MAX):
            first, hoard, cells, spent = run(
                days, c0, cpd, reserve, spoil, dig_scale)

            def d(t):
                return str(first[t]) if t in first else "-"

            if 5 not in first:
                worst["t5_missing"] += 1
            else:
                e, l = worst["t5_early"], worst["t5_late"]
                worst["t5_early"] = first[5] if e is None else min(e, first[5])
                worst["t5_late"] = first[5] if l is None else max(l, first[5])

            print("%-22s %-7s %-7s %-7s %-7s %-8.0f %-8.0f %s"
                  % ("%s r%d" % (label, reserve),
                     d(2), d(3), d(4), d(5), hoard, cells,
                     ("day %d" % spent) if spent else "not finished"))
    return worst


def run_with_lumps(days, c0, cpd, reserve, spoil, dig_scale, lumps):
    """The same coupled loop, with remains lumps credited on their measured
    days. Kept separate from run() rather than adding a parameter to it, so the
    shipped curve above is still solved and swept against digging ALONE."""
    hoard, cells_open = 0.0, TIER1_CELLS
    headroom = reserve - TIER1_CELLS
    claimed, first, day_spent = float(c0), {}, None
    on = set(LUMP_DAYS[:lumps])
    for day in range(1, days + 1):
        tier = tier_for(hoard)
        first.setdefault(tier, day)
        claimed += cpd
        if day <= GRACE_DAYS:
            continue
        wanted = (DIG_CELLS_PER_DAY_BASE[tier - 1] * dig_scale
                  * expansion_multiplier(claimed))
        opened = min(wanted, headroom)
        headroom -= opened
        cells_open += opened
        hoard += opened * spoil
        if day in on:
            hoard += REMAINS_LUMP
        if headroom <= 0 and day_spent is None:
            day_spent = day
    return first, hoard, day_spent


def report_lumps(spoil, dig_scale):
    """DOES THE REMAINS LUMP BREAK "TIER 5 IS THE COMPLETED HOLE"?

    It has to be asked, because the lump is credited OUTSIDE the cells-opened
    coupling -- exactly the shape canon 42 rejected for kobold theft, where
    uncoupling a third of the hoard would have made Den Cavity Report's coupling
    assertion a permanent red. The difference is size and timing: 120 a lump
    against a 1400 threshold, arriving after the race on every profile that is
    not passive."""
    print("R. THE REMAINS LUMP AGAINST THE COUPLING  (lump %.0f, days %s)"
          % (REMAINS_LUMP, ", ".join(str(d) for d in LUMP_DAYS)))
    print("   %-26s %-8s %-8s %-8s %s"
          % ("profile", "lumps", "T5 day", "hole", "verdict"))
    worst = 0
    for label, c0, cpd in PROFILES:
        for reserve in (RESERVE_MIN, RESERVE_MAX):
            base = None
            for lumps in (0, 1, 2, 3):
                first, _h, spent = run_with_lumps(
                    VERDICT_HORIZON, c0, cpd, reserve, spoil, dig_scale, lumps)
                t5 = first.get(5)
                if lumps == 0:
                    base = t5
                    continue
                shift = (base - t5) if (base and t5) else 0
                worst = max(worst, shift)
                print("   %-26s %-8d %-8s %-8s %s"
                      % ("%s r%d" % (label.split()[0], reserve), lumps,
                         t5 if t5 else "-", spent if spent else "-",
                         "unchanged" if shift == 0 else "%d day(s) earlier" % shift))
    print()
    print("   Worst movement: %d day(s). The numbers STAND -- no re-tune." % worst)
    print("   The coupling assertion in Den Cavity Report is a static bound on")
    print("   diggable cells times spoil and is untouched by a lump either way.")
    print()


def report_theft(spoil):
    """DOES KOBOLD THEFT BREAK THE COUPLING?  (canon 42, stage 2b)

    It has to be asked here as well as in sim_den_growth.py, because this file
    owns the coupling assertion Den Cavity Report runs. The answer is that it
    CANNOT, and the reason is structural rather than numerical: theft is paid
    into stolenHoard, tier reads hoard alone, and the assertion is a STATIC
    BOUND on diggable cells times spoil. Nothing on the theft side appears in
    it at any share.

    That is precisely the property canon 42 rejected the first shape for
    lacking. Theft credited to hoard would have uncoupled a third to two thirds
    of a typical excavator's purse and turned this assertion permanently red --
    which this entry already records as worse than no check at all.
    """
    print("T. KOBOLD THEFT AGAINST THE COUPLING  (stage 2b)")
    ceiling = (RESERVE_MIN - TIER1_CELLS) * spoil
    top = TIER_THRESHOLDS[4]
    ok = ceiling >= top
    print("   the assertion: (%d - %d) diggable cells x %.1f spoil = %.0f "
          "against a tier-5 threshold of %.0f -- %s"
          % (RESERVE_MIN, TIER1_CELLS, spoil, ceiling, top,
             "OK" if ok else "FAIL"))
    print("   theft share by tier: %s  (half the occupier's, nothing at tier 1)"
          % (EXCAVATOR_STEAL_SHARE_BY_TIER,))
    print("   stolenHoard is ADDITIVE and appears in neither term, so the bound")
    print("   above is the same number at every share. sim_den_growth.py holds")
    print("   the ledger-side proof: the tier days are identical with theft at")
    print("   none, 0.25x, 0.5x and the full occupier share.")
    print()
    return ok


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 90

    spoil = solve_spoil()
    print("Excavator cavity growth, COUPLED (canon 42 fork 4b)")
    print("table over %d days; VERDICT judged over %d" % (days, VERDICT_HORIZON))
    print("reserve %d-%d, tier 1 opens %d, so the lifetime dig budget is "
          "%d-%d cells" % (RESERVE_MIN, RESERVE_MAX, TIER1_CELLS,
                           RESERVE_MIN - TIER1_CELLS, RESERVE_MAX - TIER1_CELLS))
    print("the base table for the sweep is %s; the shipped rates are the "
          "solved ones below" % (DIG_CELLS_PER_DAY_BASE,))
    print()
    print("Solved against the SMALLEST hole, never the largest:")
    print("   spoilPerCell = %d / (%.2f x %d cells) = %.1f   (declared "
          "shipped %.1f)"
          % (TIER_THRESHOLDS[4], TIER5_AT_FRACTION_OF_SMALLEST,
             RESERVE_MIN - TIER1_CELLS, spoil, SPOIL_PER_CELL))

    best = sweep_dig_scale(spoil, VERDICT_HORIZON)
    if best is None:
        print("!! no dig scale reaches tier 5 -- the sweep found nothing.")
        return 1
    dig_scale, miss, t5 = best
    scaled = [round(c * dig_scale, 1) for c in DIG_CELLS_PER_DAY_BASE]
    print("   DigCellsPerDay x%.2f -> %s   (declared shipped x%.2f)"
          % (dig_scale, scaled, DIG_SCALE))
    print("   typical dungeon reaches tier 5 on day %d against canon's %d"
          % (t5, TARGET_TIER5_DAY))

    # THE CROSS-CHECK THAT WAS MISSING FOR TWO RELEASES. This file solves the
    # spoil and sweeps the scale; sim_den_growth.py declares what DenController
    # actually ships. Nothing compared them, so that file went on modelling the
    # pre-coupling excavator -- printing a hoard of 2073 against this file's
    # 1560 -- and canon 42 recorded a theft table measured on the wrong one.
    # A drift is a FAILURE here, not a footnote.
    drift = False
    if abs(spoil - SPOIL_PER_CELL) > 0.05:
        print("!! solved spoil %.2f against sim_den_growth's declared %.2f -- "
              "one of the two has been re-tuned and the other has not."
              % (spoil, SPOIL_PER_CELL))
        drift = True
    if abs(dig_scale - DIG_SCALE) > 0.005:
        print("!! swept dig scale x%.3f against sim_den_growth's declared "
              "x%.3f -- same fault, other knob." % (dig_scale, DIG_SCALE))
        drift = True
    if not drift:
        print("   both agree with sim_den_growth.py's declared shipped values.")
    print()

    worst = table(spoil, dig_scale, VERDICT_HORIZON)

    print()
    print("Verdict checks:")
    bad = False
    if worst["t5_missing"]:
        print("!! %d of %d profile/reserve combinations never reach tier 5 -- "
              "a seed-dependent cliff is exactly what solving against the "
              "minimum reserve was meant to remove."
              % (worst["t5_missing"], 2 * len(PROFILES)))
        bad = True
    else:
        print("   every profile and BOTH reserve bounds reach tier 5: "
              "day %d to %d." % (worst["t5_early"], worst["t5_late"]))

    # Tier 5 arriving in the first fortnight would flatten the rest of the run,
    # which is the check sim_den_growth already applies to the occupier.
    if worst["t5_early"] is not None and worst["t5_early"] < 20:
        print("!! tier 5 by day %d on the fastest profile -- saturates."
              % worst["t5_early"])
        bad = True

    first, hoard, cells, spent = run(VERDICT_HORIZON, 450, 18, RESERVE_MIN, spoil, dig_scale)
    if spent is None:
        print("!! the smallest hole is never finished inside %d days, so the "
              "coupling's whole point -- tier 5 IS the completed hole -- does "
              "not land." % VERDICT_HORIZON)
        bad = True
    elif 5 in first:
        print("   typical/r%d: tier 5 on day %d, hole finished day %d -- "
              "%d days apart." % (RESERVE_MIN, first[5], spent,
                                  abs(spent - first[5])))

    # The six rows above are two reserve BOUNDS, and canon 42 is explicit that
    # a fork must never rest on the extremes of a distribution. Real seeds land
    # anywhere in the band, and a hermit who claims almost nothing floors the
    # expansion multiplier at 0.5 -- the slowest dig the game can produce.
    report_lumps(spoil, dig_scale)
    if not report_theft(spoil):
        bad = True

    print()
    print("Robustness sweep -- every reserve in the band, plus a hermit:")
    swept = 0
    failed = []
    latest = 0
    for reserve in range(RESERVE_MIN, RESERVE_MAX + 1):
        for label, c0, cpd in PROFILES + [("hermit", 60, 1)]:
            first, _, _, spent = run(VERDICT_HORIZON, c0, cpd, reserve, spoil, dig_scale)
            swept += 1
            if 5 not in first:
                failed.append((label, reserve))
            else:
                latest = max(latest, first[5])
    if failed:
        print("!! %d of %d combinations never reach tier 5, worst: %s"
              % (len(failed), swept, failed[:4]))
        bad = True
    else:
        print("   %d combinations, all reach tier 5, latest day %d."
              % (swept, latest))

    print()
    print("VERDICT: %s" % ("FAIL -- see the flagged rows above." if bad
                           else "OK"))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
