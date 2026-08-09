#!/usr/bin/env python3
"""
Headless simulation of the site-relations placement pass, built ON
sim_holy_quota's band/fill model (imported, not duplicated).

There is no C# compiler in the container, so this cannot prove code compiles.
What it CAN prove, ahead of the expensive test cycle, is whether the CONFIRMED
relation design is geometrically viable at the authored minSpacing:

  1. PAIRS. A pair partner seats NEAR its primary (the pair-gap), exempt from
     TooClose against the primary alone. The trap this measures: a partner
     standing pair-gap from its primary can stand (minSpacing - pair-gap) from
     a THIRD site, so the exemption must not leak -- every seat is checked
     against everything except the primary, and footprints must stay disjoint
     because anchor spacing no longer guarantees it inside a pair.
  2. REQUIRES/PREFERS NEAR. Nearest-neighbour distances under TooClose start
     AT minSpacing, so any 'near' radius below it can never be satisfied.
     This measures satisfaction at 1.25x / 1.5x / 2.0x minSpacing to ground
     the default.
  3. QUOTAS. The holy minimums and the general minimum must hold with
     relations active in the same fill. Same 0.5% shortfall threshold as
     sim_holy_quota.
  4. EXCLUDES. Symmetric ban, enforced by stripping the banned plans from the
     pools the moment the first side places. Verified never co-placed, and the
     general shortfall is re-measured with the thinner pool.

Sites are modelled as axis-aligned squares of half-extent 8..14 (the authored
range: holy plans 21-23 cells run ~10, the biggest general plans ~14) for the
pair footprint-disjointness test only; everywhere else the anchor model of
sim_holy_quota stands.
"""

import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim_holy_quota import (CORE_EXCLUSION, SAMPLE_TRIES, FLOORS,
                            band, too_close, try_pick)

GENERAL_ATTEMPTS_PER_SITE = 12
HOLY_ATTEMPTS_PER_SITE = 24
PARTNER_SEAT_TRIES = 32      # samples for a partner around its primary
PREFER_TRIES = 48            # biased samples before prefers_near falls back

PAIR_GAPS = (16, 24, 32)     # candidate pair-gap defaults, cells
NEAR_FACTORS = (1.25, 1.5, 2.0)  # candidate near radii, x minSpacing


def half_extent(plan_id):
    """Deterministic 8..14 half-extent per plan, so a 'plan' has a size."""
    return 8 + (plan_id * 7919) % 7


def squares_overlap(a, ha, b, hb):
    return abs(a[0] - b[0]) <= ha + hb and abs(a[1] - b[1]) <= ha + hb


def too_close_except(c, used, sq, exempt_idx):
    for i, u in enumerate(used):
        if i == exempt_idx:
            continue
        dx = c[0] - u[0]
        dy = c[1] - u[1]
        if dx * dx + dy * dy < sq:
            return True
    return False


def seat_partner(rng, primary, gap, inner, outer, used, sq, hp, hq):
    """A partner seat: distance ~gap from the primary at a random bearing,
    in band, footprint-disjoint from the primary, TooClose against everything
    EXCEPT the primary (which is used[-1] when this is called)."""
    import math
    lo2, hi2 = inner * inner, outer * outer
    for _ in range(PARTNER_SEAT_TRIES):
        ang = rng.random() * 2.0 * math.pi
        d = gap * (0.8 + 0.4 * rng.random())
        c = (primary[0] + int(round(math.cos(ang) * d)),
             primary[1] + int(round(math.sin(ang) * d)))
        r2 = c[0] * c[0] + c[1] * c[1]
        if r2 < lo2 or r2 > hi2:
            continue
        if squares_overlap(primary, hp, c, hq):
            continue
        if too_close_except(c, used, sq, len(used) - 1):
            continue
        return c
    return None


def pick_near(rng, partner_anchors, radius, inner, outer, used, sq):
    """The prefers_near biased pick: samples in an annulus [minSpacing-ish,
    radius] around an already-placed partner, normal rejections apply."""
    import math
    lo2, hi2 = inner * inner, outer * outer
    for _ in range(PREFER_TRIES):
        p = partner_anchors[rng.randrange(len(partner_anchors))]
        ang = rng.random() * 2.0 * math.pi
        d = radius * (0.6 + 0.4 * rng.random())
        c = (p[0] + int(round(math.cos(ang) * d)),
             p[1] + int(round(math.sin(ang) * d)))
        r2 = c[0] * c[0] + c[1] * c[1]
        if r2 < lo2 or r2 > hi2:
            continue
        if too_close(c, used, sq):
            continue
        return c
    return None


def within(c, anchors, radius):
    r2 = radius * radius
    for a in anchors:
        dx = c[0] - a[0]
        dy = c[1] - a[1]
        if dx * dx + dy * dy <= r2:
            return True
    return False


class Counters(dict):
    def bump(self, k):
        self[k] = self.get(k, 0) + 1


def run_floor(fi, gap, near_factor, seeds):
    """One floor, relations active. The synthetic relation set, chosen to
    exercise every confirmed fork on the floors that have both pools:

      general plan 0  @pair -> holy plan 0        (cross-pool, chapel/crypt)
      general plan 1  @pair -> general plan 2     (same-pool)
      general plan 3  @excludes general plan 4    (symmetric)
      general plan 5  @requires_near HOLY         (hard)
      general plan 6  @prefers_near  HOLY         (soft)

    On floor 4 (no holy pool) the cross-pool pair and both near relations
    fall back to targeting the guarantee, which is placed first -- the same
    'guarantees count as placed' rule the fork confirmed.
    """
    (radius, bi, bo, spacing, rim, mn, mx,
     hmn, hmx, hpool, gpool, guarantees) = FLOORS[fi]
    inner, outer = band(radius, bi, bo, rim)
    sq = max(1, spacing) ** 2
    near_r = spacing * near_factor

    c = Counters()
    holy_short = 0
    gen_short = 0
    spacing_viol = 0
    overlap_viol = 0
    excl_viol = 0
    pair_tried = 0
    pair_done = 0
    reqnear_tried = 0
    reqnear_ok = 0
    prefnear_tried = 0
    prefnear_biased = 0

    for s in range(seeds):
        rng = random.Random(s * 7 + fi)
        used = []            # anchors, in placement order
        halves = []          # matching half-extents
        kinds = []           # 'guar' | 'holy' | 'gen' | 'partner'
        pair_of = {}         # partner index -> primary index
        placed_total = 0
        extra_total = 0
        holy_anchors = []
        gen_ids_placed = set()
        holy_ids_placed = set()

        gen_pool = list(range(gpool))
        holy_pool = list(range(100, 100 + hpool))
        rng.shuffle(gen_pool)
        rng.shuffle(holy_pool)

        def place(anchor, h, kind):
            used.append(anchor)
            halves.append(h)
            kinds.append(kind)

        # Guarantees first, 240 attempts, exactly as the quota sim does.
        for g in range(guarantees):
            a = None
            for _ in range(240):
                a = try_pick(rng, inner, outer, used, sq)
                if a:
                    break
            if a:
                place(a, 14, 'guar')
                placed_total += 1
                if fi == 4:
                    extra_total += 1

        want = rng.randint(mn, mx) if mx >= mn else 0
        holy_want = rng.randint(hmn, hmx) if hmx >= hmn else 0

        # Holy pass -- untouched by relations (no holy plan carries one),
        # which is itself the assertion: relations live on the plans that
        # have them and change nothing else.
        hplaced = 0
        attempts = 0
        cursor = 0
        while attempts < holy_want * HOLY_ATTEMPTS_PER_SITE and hplaced < holy_want:
            attempts += 1
            if not holy_pool:
                break
            pid = holy_pool[cursor % len(holy_pool)]
            cursor += 1
            a = try_pick(rng, inner, outer, used, sq)
            if a is None:
                c.bump('HolyNoAnchor')
                continue
            place(a, half_extent(pid), 'holy')
            holy_anchors.append(a)
            holy_ids_placed.add(pid)
            holy_pool.remove(pid)
            hplaced += 1
            placed_total += 1
            extra_total += 1

        # 'Near' targets: holy sites, or the guarantee where no holy exists.
        near_targets = holy_anchors if holy_anchors else \
            [used[i] for i in range(len(used)) if kinds[i] == 'guar']

        # General pass with relations.
        gplaced = 0
        attempts = 0
        cursor = 0
        excluded = set()
        while attempts < want * GENERAL_ATTEMPTS_PER_SITE:
            if (placed_total - extra_total) >= (mn if want == 0 else want):
                break
            if not gen_pool:
                c.bump('GeneralPoolExhausted')
                break
            attempts += 1
            pid = gen_pool[cursor % len(gen_pool)]
            cursor += 1

            if pid in excluded:
                # Stripped rather than attempted; counted for the report but
                # this branch never runs because stripping removes from the
                # pool -- the assertion below fails if it ever does.
                c.bump('ExcludedStillInPool')
                continue

            # requires_near: hard -- refuse this attempt if no target in range.
            if pid == 5:
                reqnear_tried += 1
                a = pick_near(rng, near_targets, near_r, inner, outer, used, sq) \
                    if near_targets else None
                if a is None or not within(a, near_targets, near_r):
                    c.bump('RequiresNearUnmet')
                    continue
                reqnear_ok += 1
            # prefers_near: soft -- biased pick first, free pick as fallback.
            elif pid == 6 and near_targets:
                prefnear_tried += 1
                a = pick_near(rng, near_targets, near_r, inner, outer, used, sq)
                if a is not None:
                    prefnear_biased += 1
                else:
                    a = try_pick(rng, inner, outer, used, sq)
            else:
                a = try_pick(rng, inner, outer, used, sq)

            if a is None:
                c.bump('GeneralNoAnchor')
                continue

            place(a, half_extent(pid), 'gen')
            gen_ids_placed.add(pid)
            gen_pool.remove(pid)
            gplaced += 1
            placed_total += 1

            # excludes: symmetric strip the moment the first side places.
            if pid == 3 and 4 in gen_pool:
                gen_pool.remove(4)
                excluded.add(4)
                c.bump('ExcludedStripped')
            elif pid == 4 and 3 in gen_pool:
                gen_pool.remove(3)
                excluded.add(3)
                c.bump('ExcludedStripped')

            # pair: place the partner in the same attempt, riding extraPlaced.
            partner = None
            if pid == 0:
                # Cross-pool: the partner must still be ON OFFER (fork: a
                # relation never summons an out-of-pool plan).
                if holy_pool:
                    partner = ('holy', holy_pool[0])
                else:
                    c.bump('PairPartnerNotInPool')
            elif pid == 1:
                if 2 in gen_pool:
                    partner = ('gen', 2)
                else:
                    c.bump('PairPartnerNotInPool')
            if partner is not None:
                pair_tried += 1
                pool_name, ppid = partner
                hq = half_extent(ppid)
                b = seat_partner(rng, a, gap, inner, outer, used, sq,
                                 halves[-1], hq)
                if b is None:
                    c.bump('PairPartnerNoSeat')
                else:
                    place(b, hq, 'partner')
                    pair_of[len(used) - 1] = len(used) - 2
                    if pool_name == 'holy':
                        holy_pool.remove(ppid)
                        holy_anchors.append(b)
                    else:
                        gen_pool.remove(ppid)
                        gen_ids_placed.add(ppid)
                    placed_total += 1
                    extra_total += 1
                    pair_done += 1

        # -- Per-seed assertions -----------------------------------------
        if hplaced < hmn:
            holy_short += 1
        if (placed_total - extra_total) < mn:
            gen_short += 1
        if 3 in gen_ids_placed and 4 in gen_ids_placed:
            excl_viol += 1
        # Spacing must hold for every anchor pair EXCEPT a pair's own two.
        for i in range(len(used)):
            for j in range(i + 1, len(used)):
                if pair_of.get(j) == i or pair_of.get(i) == j:
                    # The exempt pair: footprints must still be disjoint.
                    if squares_overlap(used[i], halves[i], used[j], halves[j]):
                        overlap_viol += 1
                    continue
                dx = used[i][0] - used[j][0]
                dy = used[i][1] - used[j][1]
                if dx * dx + dy * dy < sq:
                    spacing_viol += 1

    return dict(
        floor=fi, gap=gap, near_factor=near_factor,
        holy_short_pct=100.0 * holy_short / seeds,
        gen_short_pct=100.0 * gen_short / seeds,
        spacing_viol=spacing_viol, overlap_viol=overlap_viol,
        excl_viol=excl_viol,
        pair_pct=100.0 * pair_done / max(1, pair_tried),
        reqnear_pct=100.0 * reqnear_ok / max(1, reqnear_tried),
        prefnear_pct=100.0 * prefnear_biased / max(1, prefnear_tried),
        counters=c,
    )


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 1000
    floors = [fi for fi in sorted(FLOORS) if FLOORS[fi][10] > 0]  # general pool
    print("Site relations packing -- %d seeds per cell" % seeds)
    fail = False

    print("\n-- PAIR GAP (near factor fixed 1.5) "
          "-------------------------------------")
    print("%-6s %-5s %-7s %-7s %-9s %-9s %-9s %-7s"
          % ("floor", "gap", "pair%", "holy!", "gen!", "spacing", "overlap",
             "excl"))
    for fi in floors:
        for gap in PAIR_GAPS:
            r = run_floor(fi, gap, 1.5, seeds)
            print("%-6d %-5d %-7.1f %-7.2f %-9.2f %-9d %-9d %-7d"
                  % (fi, gap, r['pair_pct'], r['holy_short_pct'],
                     r['gen_short_pct'], r['spacing_viol'],
                     r['overlap_viol'], r['excl_viol']))
            if (r['holy_short_pct'] > 0.5 or r['gen_short_pct'] > 0.5
                    or r['spacing_viol'] or r['overlap_viol']
                    or r['excl_viol']
                    or r['counters'].get('ExcludedStillInPool')):
                fail = True

    print("\n-- NEAR RADIUS (gap fixed 24) "
          "-------------------------------------------")
    print("%-6s %-8s %-10s %-10s" % ("floor", "factor", "req_near%",
                                     "pref_biased%"))
    for fi in floors:
        for nf in NEAR_FACTORS:
            r = run_floor(fi, 24, nf, seeds)
            print("%-6d %-8.2f %-10.1f %-10.1f"
                  % (fi, nf, r['reqnear_pct'], r['prefnear_pct']))

    print("\nVERDICT: " + ("RELATIONS BREAK THE PACKING -- see failures above"
                           if fail else
                           "GREEN -- quotas hold, spacing never leaks, "
                           "footprints stay disjoint, excludes never co-place"))
    return 1 if fail else 0


if __name__ == '__main__':
    sys.exit(main())
