#!/usr/bin/env python3
"""
Headless simulation of the KOBOLD DIGGER (canon 42, the den arc's outstanding
half): how far a digger has to travel, how long the approach reads for, and --
the question that turned out to matter most -- whether it has anywhere to go.

WHY THIS FILE EXISTS. NotifyRemainsExcavated has had no caller since it
shipped, so sim_den_cavity_growth.py modelled the remains lump at ZERO and said
in its own header that the day the digger pass gives it one, it is re-run. This
is that day. Before the ledger can be re-run, two things it never measured have
to be: WHERE the remains are, and HOW LONG the walk to one is.

THE FINDING THAT SHOULD HAVE COME FIRST, AND DID NOT.

TerrainTypeMap.GetBuriedSites accepts a cell only in Stone or Granite.
ComputeRadialBand is CHEBYSHEV and puts Dirt below 0.30 of the radius, Sand
below 0.55, Stone below 0.80 and Granite beyond. So a buried remains CANNOT
EXIST inside 0.55 of the floor radius -- not rarely, never.

Canon 42 holds the kobolds to remains inside entry 19's 15-65 per cent band, so
their legal targets live in the sliver where the two agree. The band is
EUCLIDEAN (DenTunnelBuilder.Plan uses Dist, which is a sqrt) and the terrain is
CHEBYSHEV, so the overlap is not a clean annulus and is measured here rather
than derived.

The consequence is a design one, not a tuning one: on most seeds the contested
discovery beat -- which canon calls "the first thing in DCR that punishes
slowness rather than aggression" -- has no target and never fires at all.

MODELLING NOTES, each a deliberate simplification with its reason:

  * Terrain PATCHES are ignored. GenerateNew scatters 3-6 patches of 5-12
    cells, so at most 72 cells on a floor-2 disc of ~196,000 -- under 0.04 per
    cent, and below the noise of any figure printed here. They can in
    principle put Stone inside the Sand band; they cannot move a distribution.
  * OSSUARY remains are ignored. BuriedRemainsController appends one guaranteed
    cell per placed Ossuary, in site masonry rather than by sampling, so those
    are not subject to the terrain filter at all. They are a partial mitigation
    for the finding above and are called out as such rather than modelled: an
    Ossuary is one archetype among several and is not guaranteed to be placed.
  * The DIG is modelled as a straight run at the tunnel section the profile
    authors. A wobbling centreline is longer than a straight one, so every
    travel figure here is a FLOOR on the true cost.

Usage:  python3 sim_den_digger.py [seeds]
"""

import math
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Imported, never copied -- the arc brief's standing rule, and the reason the
# figures below can be trusted to describe the shipped build rather than a
# second transcription of it.
from sim_den_tunnels import (
    BAND_INNER,
    BAND_OUTER,
    DEN_FLOORS,
    EXCLUSION_FROM_CENTRE,
    place_chambers,
)
from sim_den_cavity import (
    CHAMBER_SEAT_CLEARANCE,
    EXCAVATOR_MAX_CELLS,
    EXCAVATOR_TIER1_CELLS,
    pick_anchor,
)
from sim_den_growth import (
    DIG_CELLS_PER_DAY_BY_TIER,
    EXPANSION_SENSITIVITY,
    GRACE_DAYS,
    TIER_THRESHOLDS,
    tier_for,
)

import random

# ---- read off shipped assets and source ---------------------------------

# TerrainTypeMap: the radial ladder, and the rim it subtracts before sampling.
BAND_DIRT = 0.30
BAND_SAND = 0.55
BAND_STONE = 0.80
MAX_RING_THICKNESS = 5

# BuriedRemainsController.
SITES_PER_FLOOR = 2
MIN_DIST_FROM_CENTRE = 6
GET_BURIED_SITES_ATTEMPTS = 600        # TerrainTypeMap's own attempt ceiling

# DenController, shipped values. DIG_CELLS_PER_DAY_BY_TIER is the ORIGINAL
# {7,11,17,26,38}; fork 4b scaled it when the number acquired a second consumer.
DIG_SCALE = 0.22
SHIPPED_DIG_CELLS_PER_DAY = [1.5, 2.4, 3.7, 5.7, 8.4]
SPOIL_PER_CELL = 7.8
EXPANSION_BASELINE_CELLS = 900

# DenTunnelProfile.asset, floor index 2.
TUNNEL_WIDTH_AT_MOUTH = 3
TUNNEL_TIP_WIDTH = 2

EXCAVATOR_FLOOR = 2


def _assert_shipped_constants():
    """A cross-check rather than a transcription. sim_den_cavity_growth.py
    solved DIG_SCALE and the asset carries the product; if either moves without
    the other, this file must not quietly keep using the stale one."""
    derived = [round(v * DIG_SCALE, 1) for v in DIG_CELLS_PER_DAY_BY_TIER]
    if derived != SHIPPED_DIG_CELLS_PER_DAY:
        raise SystemExit(
            "DIG_CELLS_PER_DAY_BY_TIER * %.2f = %s, but DenController ships %s. "
            "Re-run sim_den_cavity_growth.py before trusting anything here."
            % (DIG_SCALE, derived, SHIPPED_DIG_CELLS_PER_DAY))


# ---- terrain, mirroring ComputeRadialBand -------------------------------

def terrain_norm(dx, dy, radius):
    """CHEBYSHEV over the floor radius. The metric is the whole point: it is
    not the Euclidean one the den band uses, so the two regions do not nest."""
    return max(abs(dx), abs(dy)) / float(radius)


def is_stone_or_granite(dx, dy, radius):
    return terrain_norm(dx, dy, radius) >= BAND_SAND


# ---- remains, mirroring GetBuriedSites ----------------------------------

def buried_sites(seed, radius, count=SITES_PER_FLOOR):
    """TerrainTypeMap.GetBuriedSites: uniform over a SQUARE of usable half-width,
    Chebyshev-rejected near the centre, terrain-filtered, first `count` kept.

    Note it never rejects against the circular floor at all, and never consults
    the bedrock rim -- both faithful to source."""
    rng = random.Random(seed ^ 0x0DDB0135)
    usable = radius - MAX_RING_THICKNESS
    sites = []
    for _ in range(GET_BURIED_SITES_ATTEMPTS):
        if len(sites) >= count:
            break
        dx = rng.randint(-usable, usable)
        dy = rng.randint(-usable, usable)
        if max(abs(dx), abs(dy)) < MIN_DIST_FROM_CENTRE:
            continue
        if not is_stone_or_granite(dx, dy, radius):
            continue
        if (dx, dy) in sites:
            continue
        sites.append((dx, dy))
    return sites


def band_bounds(radius):
    """Plan's own arithmetic: inner clamped to the core exclusion, outer plain."""
    inner = max(EXCLUSION_FROM_CENTRE + 2, int(round(radius * BAND_INNER)))
    outer = int(round(radius * BAND_OUTER))
    return inner, outer


def in_band(site, inner, outer):
    """EUCLIDEAN, matching DenTunnelBuilder.Dist."""
    d = math.hypot(site[0], site[1])
    return inner <= d <= outer


# ---- the dig ------------------------------------------------------------

def cavity_radius_cells(cell_count):
    """Radius of an equal-area disc. The cavity is a CA blob, not a disc, so
    this is an approximation -- used only to subtract the stretch of the run
    that lies inside the hole the den already has."""
    return math.sqrt(cell_count / math.pi)


def cells_to_dig(distance, width_mouth, width_tip):
    """A run's cell cost at the authored section. Tapers mouth-to-tip exactly as
    DenTunnelBuilder rasterises, so the mean width is the average of the two."""
    return distance * (width_mouth + width_tip) / 2.0


def expansion_multiplier(claimed_cells):
    ratio = claimed_cells / float(max(1, EXPANSION_BASELINE_CELLS))
    return max(0.5, min(1.8, 1.0 + EXPANSION_SENSITIVITY * (ratio - 1.0)))


PROFILES = [
    # (name, claimed at day 0, claimed gained per day)
    ("hermit",   250,  4),
    ("typical",  450,  12),
    ("sprawler", 700,  26),
]


def days_to_reach(tunnel_cells, reserve, claimed_start, claimed_per_day,
                  dedicated_rate_fraction, days_cap=400):
    """Runs the dawn loop until the tunnel is dug, returning the day it lands
    and the state of the reserve when it does.

    dedicated_rate_fraction is the share of the day's dig that goes to the
    TUNNEL rather than the reserve. 0.0 is 'never digs a tunnel'; 1.0 is 'the
    reserve stops while the tunnel runs'. Fork 1 asks which of these the arc
    ships, and the answer is visible in the two columns this returns."""
    hoard = 0.0
    carry_tunnel = 0.0
    carry_reserve = 0.0
    dug_tunnel = 0
    open_cells = EXCAVATOR_TIER1_CELLS
    claimed = claimed_start
    arrived = None

    for day in range(1, days_cap + 1):
        claimed += claimed_per_day
        if day <= GRACE_DAYS:
            continue

        tier = tier_for(hoard)
        budget = SHIPPED_DIG_CELLS_PER_DAY[tier - 1] * expansion_multiplier(claimed)

        # Values at or below 1 are a SHARE of the day's dig (the tunnel competes
        # with the hole). Values above 1 are an ADDITIVE budget expressed as a
        # multiple of the tier rate (the tunnel is its own crew). The two models
        # behave very differently and the sweep in F is why: a share freezes the
        # tier, because the ledger pays on reserve cells and nothing else.
        if arrived is None:
            if dedicated_rate_fraction <= 1.0:
                to_tunnel = budget * dedicated_rate_fraction
                to_reserve = budget - to_tunnel
            else:
                to_tunnel = budget * dedicated_rate_fraction
                to_reserve = budget
        else:
            to_tunnel = 0.0
            to_reserve = budget

        carry_tunnel += to_tunnel
        whole = int(carry_tunnel)
        carry_tunnel -= whole
        if whole > 0 and arrived is None:
            dug_tunnel += whole
            if dug_tunnel >= tunnel_cells:
                arrived = day

        carry_reserve += to_reserve
        whole = int(carry_reserve)
        carry_reserve -= whole
        if whole > 0:
            opened = min(whole, reserve - open_cells)
            open_cells += opened
            # PAID ON CELLS OPENED (fork 4b). Tunnel cells are deliberately NOT
            # paid here -- that is the recommendation this file exists to test,
            # and the alternative is modelled by the caller passing them in.
            hoard += opened * SPOIL_PER_CELL

    return arrived, open_cells, hoard


# ---- reporting ----------------------------------------------------------

def measure_availability(seeds, radius):
    inner, outer = band_bounds(radius)
    counts = {0: 0, 1: 0, 2: 0}
    nearest_norms = []
    for s in range(seeds):
        sites = buried_sites(s * 7919 + EXCAVATOR_FLOOR, radius)
        good = [x for x in sites if in_band(x, inner, outer)]
        counts[min(2, len(good))] += 1
        for g in good:
            nearest_norms.append(math.hypot(g[0], g[1]) / float(radius))
    return counts, nearest_norms, inner, outer


def sweep_target_band(seeds, radius):
    """What relaxing the kobolds' TARGET band buys. This is a different claim
    from moving the den's own placement band: the den still sits inside 15-65
    per cent, it simply digs further out for something worth reaching.

    Entry 19's measurement is about where a plausible late run REACHES, which
    is what justifies not scattering content past 65 per cent. A dig that goes
    out to fetch is not content placed out there -- the beat still happens where
    the player is, because the hole and the tunnel mouth are in band."""
    rows = []
    inner, _ = band_bounds(radius)
    for outer_frac in (0.65, 0.70, 0.75, 0.80, 0.90, 1.00):
        outer = int(round(radius * outer_frac))
        have = 0
        for s in range(seeds):
            sites = buried_sites(s * 7919 + EXCAVATOR_FLOOR, radius)
            if any(in_band(x, inner, outer) for x in sites):
                have += 1
        rows.append((outer_frac, 100.0 * have / seeds))
    return rows


def measure_travel(seeds, radius, outer_frac):
    inner, _ = band_bounds(radius)
    outer = int(round(radius * outer_frac))
    cav_r = cavity_radius_cells(EXCAVATOR_MAX_CELLS)
    dists, cells = [], []
    starved = 0
    for s in range(seeds):
        rng = random.Random(s * 7919 + EXCAVATOR_FLOOR)
        # place_chambers returns (centres, boxes); the anchor picker wants the
        # centres alone, exactly as sim_den_cavity calls it.
        centres = place_chambers(rng, radius)[0]
        anchor, _i, _o, _rej = pick_anchor(rng, radius, centres,
                                           CHAMBER_SEAT_CLEARANCE)
        if anchor is None:
            continue
        sites = buried_sites(s * 7919 + EXCAVATOR_FLOOR, radius)
        good = [x for x in sites if in_band(x, inner, outer)]
        if not good:
            starved += 1
            continue
        d = min(math.hypot(g[0] - anchor[0], g[1] - anchor[1]) for g in good)
        d = max(0.0, d - cav_r)
        dists.append(d)
        cells.append(cells_to_dig(d, TUNNEL_WIDTH_AT_MOUTH, TUNNEL_TIP_WIDTH))
    return dists, cells, starved


def pct(values, p):
    if not values:
        return 0.0
    s = sorted(values)
    return s[min(len(s) - 1, int(p * len(s)))]


def explore(seed, radius, detect, drift_deg, runs, cap_cells,
            clamp_fraction=0.85):
    """A PERSISTENT random walk, not a pure one, and the distinction is the
    whole model. A pure random walk barely leaves its origin -- expected
    displacement grows as sqrt(n), so a thousand cells of digging would end
    thirty cells from the den and the tunnel would read as a scribble rather
    than as a prospecting run. BuildTunnel already wobbles a bearing rather
    than re-rolling one, so persistence is the shipped idiom as well as the
    legible one.

    Each run holds a heading and perturbs it by a small angle per cell. A run
    that reaches the endpoint clamp turns rather than stopping: the rim is
    bedrock and a dig that walked into it would stall invisibly.

    Returns (cells_cut_at_first_find, found), where found is False if the cap
    was reached without any target passing within `detect`."""
    rng = random.Random(seed ^ 0x51E2C0DE)
    inner, outer = band_bounds(radius)
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    anchor, _i, _o, _r = pick_anchor(
        random.Random(seed * 7919 + EXCAVATOR_FLOOR), radius, centres,
        CHAMBER_SEAT_CLEARANCE)
    if anchor is None:
        return None, False

    targets = buried_sites(seed * 7919 + EXCAVATOR_FLOOR, radius)
    if not targets:
        return None, False

    clamp = radius * clamp_fraction
    drift = math.radians(drift_deg)
    heads = []
    for r in range(runs):
        theta = rng.uniform(0.0, 2.0 * math.pi)
        heads.append([float(anchor[0]), float(anchor[1]), theta])

    cut = 0
    while cut < cap_cells:
        for h in heads:
            h[2] += rng.uniform(-drift, drift)
            nx = h[0] + math.cos(h[2])
            ny = h[1] + math.sin(h[2])
            # Turn at the clamp rather than stalling in the rim.
            if math.hypot(nx, ny) > clamp:
                h[2] += math.pi * rng.uniform(0.5, 1.5)
                continue
            h[0], h[1] = nx, ny
            cut += 1
            for t in targets:
                if math.hypot(t[0] - h[0], t[1] - h[1]) <= detect:
                    return cut, True
    return cut, False


def report_exploration(seeds, radius):
    print("G. EXPLORATORY DIGGING -- does a wandering tunnel find anything?")
    print("   Cells cut before a target comes within the sense radius. The cap")
    print("   is 1200 cells: at the recommended crawlway budget that is already")
    print("   far past the day-49 mark, so 'not found' means not found in play.")
    print()
    print("   runs  sense  found%   cells to find (median / p75)")
    for runs in (1, 2, 3):
        for detect in (8, 15, 25, 40):
            found, cells = 0, []
            for s in range(seeds):
                cut, ok = explore(s, radius, detect, drift_deg=12,
                                  runs=runs, cap_cells=1200)
                if ok:
                    found += 1
                    cells.append(cut)
            print("   %-5d %-6d %6.1f%%  %s"
                  % (runs, detect, 100.0 * found / seeds,
                     ("%.0f / %.0f" % (pct(cells, 0.5), pct(cells, 0.75)))
                     if cells else "-"))
    print()
    print("   Cells are TOTAL across all runs, so a den pushing three tunnels")
    print("   pays three times as much rock for the same wall-clock day. The")
    print("   budget is what converts this column into days, not the run count.")
    print()



# ---- the confirmed model: exploratory digging against a POI set ---------

# DenTunnelProfile.asset floor 2 authors 3->2; the crawlway recommendation is
# 2->1. Mean width is what converts centreline cells into rock cut, and rock
# cut is what the budget buys.
CRAWLWAY_MOUTH, CRAWLWAY_TIP = 2, 1
CRAWLWAY_MEAN_WIDTH = (CRAWLWAY_MOUTH + CRAWLWAY_TIP) / 2.0

# TileInfluenceManager.ClaimStarterArea opens at the stair landing, which
# EnsureFloorExists passes as the floor's centre cell, and influence grows
# outward from there. Modelled as a centred disc of equal area -- the real
# shape is corridors and rooms, so this is an APPROXIMATION and a generous one
# for a compact dungeon, mean for a sprawling one.
def claimed_radius(claimed_cells):
    return math.sqrt(max(1, claimed_cells) / math.pi)


# RunChamberCA's median yield is 49 cells (measured in sim_den_cavity.py), so a
# chamber presents roughly this radius to a passing dig.
CHAMBER_EFFECTIVE_RADIUS = math.sqrt(49 / math.pi)


def poi_hit(x, y, targets):
    """First POI within its own sense envelope, or None. Returns the kind so the
    COMPOSITION of finds can be reported -- which is the real question, because
    what a den finds first is the story that floor tells."""
    for kind, tx, ty, reach in targets:
        if math.hypot(tx - x, ty - y) <= reach:
            return kind
    return None


def build_targets(seed, radius, detect, claimed_cells):
    """The confirmed POI list, minus the two this file cannot honestly place.

    ROAD and SITES are NOT modelled. RoadNetworkBuilder's carriageway and the
    Buried Age site placer are not imported here, and inventing their geometry
    would put a guessed number beside measured ones. Both are LARGE targets --
    a trunk road is a line clean across the floor -- so every found-rate below
    is a FLOOR on the true figure, never a ceiling. Recorded rather than
    quietly omitted."""
    out = []
    for t in buried_sites(seed * 7919 + EXCAVATOR_FLOOR, radius):
        out.append(("remains", t[0], t[1], detect))
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    for c in centres:
        out.append(("chamber", c[0], c[1], detect + CHAMBER_EFFECTIVE_RADIUS))
    out.append(("claimed", 0.0, 0.0, detect + claimed_radius(claimed_cells)))
    return out


def explore_poi(seed, radius, detect, drift_deg, days, profile,
                extra_rate, clamp_fraction=0.85):
    """A den's whole prospecting career, dawn by dawn.

    STOPS AND PICKS A NEW BEARING on a find (the confirmed rule), so a run is a
    sequence of legs rather than one walk. The new bearing is re-rolled from the
    point of the find, not from the den -- a kobold that has just broken into
    something carries on from where it stands, and sending it home to start
    again would read as the tunnel being deleted."""
    rng = random.Random(seed ^ 0x51E2C0DE)
    centres = place_chambers(random.Random(seed * 7919 + EXCAVATOR_FLOOR),
                             radius)[0]
    anchor, _i, _o, _r = pick_anchor(
        random.Random(seed * 7919 + EXCAVATOR_FLOOR), radius, centres,
        CHAMBER_SEAT_CLEARANCE)
    if anchor is None:
        return []

    name, claimed, per_day = profile
    lim = radius * clamp_fraction
    drift = math.radians(drift_deg)
    x, y = float(anchor[0]), float(anchor[1])
    theta = rng.uniform(0.0, 2.0 * math.pi)
    carry = 0.0
    hoard = 0.0
    open_cells = EXCAVATOR_TIER1_CELLS
    finds = []
    hit_already = set()

    for day in range(1, days + 1):
        claimed += per_day
        if day <= GRACE_DAYS:
            continue
        tier = tier_for(hoard)
        # The reserve keeps digging and keeps paying; the tunnel is an ADDITIVE
        # budget on top, and pays nothing. That is fork 1 as ruled, and it is
        # what keeps 'tier 5 is the completed hole' true.
        reserve_budget = SHIPPED_DIG_CELLS_PER_DAY[tier - 1] \
            * expansion_multiplier(claimed)
        opened = min(int(reserve_budget), EXCAVATOR_MAX_CELLS - open_cells)
        open_cells += max(0, opened)
        hoard += max(0, opened) * SPOIL_PER_CELL

        carry += reserve_budget * extra_rate / CRAWLWAY_MEAN_WIDTH
        steps = int(carry)
        carry -= steps

        targets = build_targets(seed, radius, detect, claimed)
        for _ in range(steps):
            theta += rng.uniform(-drift, drift)
            nx, ny = x + math.cos(theta), y + math.sin(theta)
            if math.hypot(nx, ny) > lim:
                theta += math.pi * rng.uniform(0.5, 1.5)
                continue
            x, y = nx, ny
            kind = poi_hit(x, y, targets)
            # A chamber already broken into is not a new find; remains are
            # consumed once. Without this a den re-reports the same cave for ever.
            key = (kind, round(x / 8.0), round(y / 8.0))
            if kind and key not in hit_already:
                hit_already.add(key)
                finds.append((day, kind))
                theta = rng.uniform(0.0, 2.0 * math.pi)
    return finds


def report_poi(seeds, radius):
    print("I. THE CONFIRMED MODEL -- exploratory digging, full POI set")
    print("   2->1 crawlway, additive tunnel budget, stop-and-new-bearing.")
    print("   ROAD and SITES are not modelled, so every figure is a FLOOR.")
    print()
    for extra_rate in (1.0, 2.0):
        for detect in (8, 15):
            print("   budget x%.0f, sense %d" % (extra_rate, detect))
            print("     profile     any find%%  first find (day)  finds by d150"
                  "   remains ever%")
            for profile in PROFILES:
                anyf, firsts, counts, rem = 0, [], [], 0
                for s in range(seeds):
                    finds = explore_poi(s, radius, detect, 12, 150,
                                        profile, extra_rate)
                    if finds:
                        anyf += 1
                        firsts.append(finds[0][0])
                    counts.append(len(finds))
                    if any(k == "remains" for _d, k in finds):
                        rem += 1
                print("     %-11s %8.1f%%  %-17s %-15.1f %.1f%%"
                      % (profile[0], 100.0 * anyf / seeds,
                         ("%.0f (p75 %.0f)" % (pct(firsts, 0.5),
                                               pct(firsts, 0.75)))
                         if firsts else "-",
                         statistics.mean(counts), 100.0 * rem / seeds))
            print()

    print("   COMPOSITION of finds, typical dungeon, budget x2 sense 15:")
    tally = {}
    for s in range(seeds):
        for _d, kind in explore_poi(s, radius, 15, 12, 150, PROFILES[1], 2.0):
            tally[kind] = tally.get(kind, 0) + 1
    total = float(sum(tally.values())) or 1.0
    for kind in sorted(tally, key=lambda k: -tally[k]):
        print("     %-10s %5.1f%%" % (kind, 100.0 * tally[kind] / total))
    print()


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    _assert_shipped_constants()
    radius = DEN_FLOORS[EXCAVATOR_FLOOR][0]

    print("Kobold digger -- floor index %d, radius %d, %d seeds"
          % (EXCAVATOR_FLOOR, radius, seeds))
    print()

    counts, norms, inner, outer = measure_availability(seeds, radius)
    print("A. TARGET AVAILABILITY  (band %d-%d cells, Euclidean; "
          "remains need Chebyshev >= %.2f of radius)" % (inner, outer, BAND_SAND))
    total = float(sum(counts.values()))
    for k in (0, 1, 2):
        print("   in-band remains = %d : %6.2f%%" % (k, 100.0 * counts[k] / total))
    print("   at least one      : %6.2f%%" % (100.0 * (counts[1] + counts[2]) / total))
    if norms:
        print("   where they land   : %.2f-%.2f of radius (mean %.2f)"
              % (min(norms), max(norms), statistics.mean(norms)))
    print()

    print("B. RELAXING THE TARGET BAND  (den placement unchanged)")
    print("   target outer   seeds with a target")
    for frac, have in sweep_target_band(seeds, radius):
        print("   %-14.2f %6.1f%%" % (frac, have))
    print()

    for frac in (0.65, 0.80):
        dists, cells, starved = measure_travel(seeds, radius, frac)
        if not dists:
            continue
        print("C. TRAVEL at target outer %.2f  (%d of %d seeds have a target)"
              % (frac, len(dists), len(dists) + starved))
        print("   anchor to nearest target, minus the cavity: "
              "median %.0f  mean %.0f  p90 %.0f cells"
              % (pct(dists, 0.5), statistics.mean(dists), pct(dists, 0.9)))
        print("   cells of tunnel to cut at section %d->%d: "
              "median %.0f  mean %.0f  p90 %.0f"
              % (TUNNEL_WIDTH_AT_MOUTH, TUNNEL_TIP_WIDTH,
                 pct(cells, 0.5), statistics.mean(cells), pct(cells, 0.9)))
        print()

    dists, cells, _starved = measure_travel(seeds, radius, 0.80)
    median_cells = pct(cells, 0.5)
    print("D. DAYS TO REACH, median target (%.0f tunnel cells), reserve %d"
          % (median_cells, EXCAVATOR_MAX_CELLS))
    print("   share to tunnel   profile     arrives  reserve open  hoard")
    for share in (0.25, 0.50, 1.00):
        for name, c0, cpd in PROFILES:
            day, open_cells, hoard = days_to_reach(
                median_cells, EXCAVATOR_MAX_CELLS, c0, cpd, share)
            print("   %-17.2f %-11s %-8s %-13d %.0f"
                  % (share, name, day if day else "never", open_cells, hoard))
    print()

    print("F. REMEDY SWEEP -- what lands the approach in a legible window")
    print("   Canon's own arc reaches tier 5 about day 49, so an approach that")
    print("   arrives after that is a race whose finish nobody is still watching.")
    print("   Target window: day 25-60 for a typical dungeon.")
    print()
    print("   section  extra rate  tunnel cells   hermit  typical  sprawler")
    for section in (1, 2, 3):
        # A crawlway rather than a highway: mean width is the whole cost driver,
        # because cells = length x mean width and the length cannot be reduced.
        mouth, tip = section, max(1, section - 1)
        cells_at_section = pct(
            [cells_to_dig(d, mouth, tip) for d in dists], 0.5)
        for extra in (1, 2, 4, 8):
            row = []
            for name, c0, cpd in PROFILES:
                day, _open, _h = days_to_reach(
                    cells_at_section, EXCAVATOR_MAX_CELLS, c0, cpd,
                    dedicated_rate_fraction=extra, days_cap=400)
                row.append(str(day) if day else "never")
            print("   %-8s %-11s %-14.0f %-7s %-8s %-8s"
                  % ("%d->%d" % (mouth, tip), "x%d" % extra,
                     cells_at_section, row[0], row[1], row[2]))
    print()
    print("   'extra rate' is a tunnel budget ON TOP of the cavity dig, expressed")
    print("   as a multiple of the tier's cavity rate. It is additive rather than")
    print("   a share precisely because the share model freezes the tier: income")
    print("   is paid on RESERVE cells only, so diverting the whole budget to a")
    print("   tunnel stops the hoard, stops the tier, and slows the dig that was")
    print("   diverted. Row 'share 1.00' in D is that trap, measured.")
    print()

    report_exploration(min(seeds, 600), radius)
    report_poi(min(seeds, 400), radius)

    print("E. THE SHARED-BUDGET QUESTION (fork 1)")
    print("   Reserve headroom is %d cells; the median tunnel is %.0f. A shared"
          % (EXCAVATOR_MAX_CELLS - EXCAVATOR_TIER1_CELLS, median_cells))
    print("   budget therefore spends %.0f%% of an excavator's LIFETIME dig on"
          % (100.0 * median_cells / (EXCAVATOR_MAX_CELLS - EXCAVATOR_TIER1_CELLS)))
    print("   one approach. Column D above shows what that does to the hole.")


if __name__ == "__main__":
    main()
