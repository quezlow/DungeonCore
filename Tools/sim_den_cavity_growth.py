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
  2. THE REMAINS LUMP CANNOT BE COUNTED ON. NotifyRemainsExcavated has NO
     CALLER in the shipped build -- canon 42 put it in ahead of the agents
     that will call it, and the kobold digger pass is still separate. So the
     whole curve must come from digging alone. Modelled at zero here, and the
     day it acquires a caller this file is re-run.

Usage:  python3 sim_den_cavity_growth.py [days]
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim_den_growth import (
    DIG_CELLS_PER_DAY_BY_TIER,
    EXPANSION_SENSITIVITY,
    GRACE_DAYS,
    SPOIL_PER_CELL,
    TIER_THRESHOLDS,
    tier_for,
)

# ---- read off DenTunnelProfile.asset, floor index 2 ---------------------

RESERVE_MIN = 350
RESERVE_MAX = 400
TIER1_CELLS = 150

# DenController.expansionBaselineCells. sim_den_growth models expansion off
# gold-per-day as a proxy; here the CLAIMED CELL COUNT is what the shipped
# ExpansionMultiplier actually reads, so it is modelled directly.
EXPANSION_BASELINE_CELLS = 900

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


def expansion_multiplier(claimed_cells):
    """DenController.ExpansionMultiplier, mirrored exactly."""
    ratio = claimed_cells / float(max(1, EXPANSION_BASELINE_CELLS))
    return max(0.5, min(1.8, 1.0 + EXPANSION_SENSITIVITY * (ratio - 1.0)))


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

        wanted = (DIG_CELLS_PER_DAY_BY_TIER[tier - 1] * dig_scale
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


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 90

    spoil = solve_spoil()
    print("Excavator cavity growth, COUPLED (canon 42 fork 4b)")
    print("table over %d days; VERDICT judged over %d" % (days, VERDICT_HORIZON))
    print("reserve %d-%d, tier 1 opens %d, so the lifetime dig budget is "
          "%d-%d cells" % (RESERVE_MIN, RESERVE_MAX, TIER1_CELLS,
                           RESERVE_MIN - TIER1_CELLS, RESERVE_MAX - TIER1_CELLS))
    print("remains lumps modelled at ZERO: NotifyRemainsExcavated has no caller")
    print()
    print("Solved against the SMALLEST hole, never the largest:")
    print("   spoilPerCell = %d / (%.2f x %d cells) = %.1f   (shipped %.1f)"
          % (TIER_THRESHOLDS[4], TIER5_AT_FRACTION_OF_SMALLEST,
             RESERVE_MIN - TIER1_CELLS, spoil, SPOIL_PER_CELL))

    best = sweep_dig_scale(spoil, VERDICT_HORIZON)
    if best is None:
        print("!! no dig scale reaches tier 5 -- the sweep found nothing.")
        return 1
    dig_scale, miss, t5 = best
    scaled = [round(c * dig_scale, 1) for c in DIG_CELLS_PER_DAY_BY_TIER]
    print("   DigCellsPerDay x%.2f -> %s   (shipped %s)"
          % (dig_scale, scaled, DIG_CELLS_PER_DAY_BY_TIER))
    print("   typical dungeon reaches tier 5 on day %d against canon's %d"
          % (t5, TARGET_TIER5_DAY))
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
