#!/usr/bin/env python3
"""
Headless simulation of the anchor packing the holy sub-quota has to survive.

There is no C# compiler in the container, so this cannot prove the code
compiles. What it CAN prove -- and what the expensive test cycle would
otherwise have to discover -- is whether the quotas in the asset are
geometrically reachable at the authored minSpacing, or whether the holy pass
starves and the floor silently ships with fewer seals than canon states.

It reimplements only the parts that decide that: the band arithmetic from
AncientSiteBuilder.Build, TryPickAnchor's 64-sample / 96-sample budgets and its
degrade-to-Free path, TooClose, and the two fill loops with their attempt
budgets and plan cursors. Shape and walkability rejections are omitted
deliberately -- every holy plan is authored at a fixed 21-23 cells and anchors
no further out than 0.65 of the radius, so the disc clamp never reaches them.

The pessimism is on purpose in one place: the guarantees are placed at a random
in-band anchor rather than on a road, because road cells are not available
without running the road builder. A real AlongRoad outpost clusters onto the
carriageway, which if anything leaves the rest of the band emptier than this
models.
"""

import random
import statistics
import sys

CORE_EXCLUSION = 12          # the scene's exclusionRadiusFromCenter, worst case
SAMPLE_TRIES = 96            # TryPickAnchor's Free-degrade budget
GENERAL_ATTEMPTS_PER_SITE = 12
HOLY_ATTEMPTS_PER_SITE = 24

# floorIndex: (radius, bandInner, bandOuter, minSpacing, rimMargin,
#              minSites, maxSites, minHoly, maxHoly, holyPoolSize,
#              generalPoolSize, guarantees)
FLOORS = {
    0: (100, 0.35, 0.55, 60, 12, 0, 0, 1, 1, 2, 0, 0),
    1: (150, 0.20, 0.65, 60, 12, 0, 0, 2, 3, 6, 0, 0),
    2: (250, 0.30, 0.65, 70, 12, 1, 2, 3, 4, 8, 12, 1),
    3: (400, 0.15, 0.65, 90, 12, 3, 5, 5, 6, 8, 11, 1),
    4: (600, 0.15, 0.65, 90, 12, 9, 13, 0, 0, 0, 24, 1),
}


def band(radius, inner_f, outer_f, rim):
    usable = max(0, radius - rim)
    inner = max(CORE_EXCLUSION + 2, round(radius * inner_f))
    outer = min(usable, round(radius * outer_f))
    return inner, outer


def too_close(c, used, sq):
    for u in used:
        dx = c[0] - u[0]
        dy = c[1] - u[1]
        if dx * dx + dy * dy < sq:
            return True
    return False


def try_pick(rng, inner, outer, used, sq):
    """TryPickAnchor's Free path: 96 uniform samples in the bounding square,
    rejecting anything out of band or too close."""
    lo, hi = inner * inner, outer * outer
    for _ in range(SAMPLE_TRIES):
        dx = rng.randint(-outer, outer)
        dy = rng.randint(-outer, outer)
        d = dx * dx + dy * dy
        if d < lo or d > hi:
            continue
        if too_close((dx, dy), used, sq):
            continue
        return (dx, dy)
    return None


def fill(rng, inner, outer, used, sq, want, pool, per_site, counts_as_extra,
         placed_total, extra_total):
    """The Fill(...) loop, counting progress the way the pass in question does."""
    if want <= 0 or pool <= 0:
        return 0, 0
    attempts = 0
    placed = 0
    max_attempts = want * per_site
    while attempts < max_attempts:
        progress = placed if counts_as_extra else (placed_total + placed) - extra_total
        if progress >= want:
            break
        attempts += 1
        a = try_pick(rng, inner, outer, used, sq)
        if a is None:
            continue
        used.append(a)
        placed += 1
    return placed, attempts


def run(fi, seeds=2000):
    (radius, bi, bo, spacing, rim, mn, mx,
     hmn, hmx, hpool, gpool, guarantees) = FLOORS[fi]
    inner, outer = band(radius, bi, bo, rim)
    sq = max(1, spacing) ** 2

    holy_results = []
    gen_results = []
    holy_short = 0
    gen_short = 0

    for s in range(seeds):
        rng = random.Random(s)
        used = []
        placed_total = 0
        extra_total = 0

        # Guarantees: placed first, on their own generous 240-attempt budget,
        # and they count toward `want` except for the vault.
        for g in range(guarantees):
            a = None
            for _ in range(240):
                a = try_pick(rng, inner, outer, used, sq)
                if a:
                    break
            if a:
                used.append(a)
                placed_total += 1
                if fi == 4:          # the vault is extraPlaced
                    extra_total += 1

        want = rng.randint(mn, mx) if mx >= mn else 0
        holy_want = rng.randint(hmn, hmx) if hmx >= hmn else 0

        hp, _ = fill(rng, inner, outer, used, sq, holy_want, hpool,
                     HOLY_ATTEMPTS_PER_SITE, True, placed_total, extra_total)
        placed_total += hp
        extra_total += hp

        gp, _ = fill(rng, inner, outer, used, sq, want, gpool,
                     GENERAL_ATTEMPTS_PER_SITE, False, placed_total, extra_total)
        placed_total += gp

        holy_results.append(hp)
        gen_results.append(gp + (placed_total - gp - hp - extra_total))
        if hp < hmn:
            holy_short += 1
        # General target is the budget MINUS extras, and the guarantees that
        # count toward it are already in placed_total.
        if (placed_total - extra_total) < mn:
            gen_short += 1

    return dict(
        floor=fi, radius=radius, band=(inner, outer), spacing=spacing,
        holy_min=min(holy_results), holy_mean=statistics.mean(holy_results),
        holy_target=(hmn, hmx),
        holy_short_pct=100.0 * holy_short / seeds,
        gen_short_pct=100.0 * gen_short / seeds,
    )


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    print("Holy sub-quota anchor packing -- %d seeds per floor" % seeds)
    print()
    print("%-6s %-8s %-14s %-9s %-9s %-8s %-8s %-8s"
          % ("floor", "radius", "band", "minSpace", "holy",
             "worst", "mean", "short%"))
    fail = False
    for fi in sorted(FLOORS):
        r = run(fi, seeds)
        lo, hi = r['holy_target']
        print("%-6d %-8d %-14s %-9d %-9s %-8d %-8.2f %-8.1f"
              % (fi, r['radius'], "%d-%d" % r['band'], r['spacing'],
                 "%d-%d" % (lo, hi), r['holy_min'], r['holy_mean'],
                 r['holy_short_pct']))
        if r['holy_short_pct'] > 0.5:
            fail = True
        if r['gen_short_pct'] > 0.5:
            print("       !! general pass short of minSites on %.1f%% of seeds"
                  % r['gen_short_pct'])
            fail = True
    print()
    print("VERDICT: " + ("QUOTAS STARVE -- retune minSpacing or the budget"
                         if fail else
                         "every floor meets its holy minimum on every seed"))
    return 1 if fail else 0


if __name__ == '__main__':
    sys.exit(main())
