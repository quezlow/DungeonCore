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

REPAIRED A SECOND TIME: sections G, I and J -- the exploratory walk -- were
replaced with a port of Road Breach Report's mirror (the shipped rules: brush
test, never-retrace, block-on-find, the seat rule), after the report measured
the shipped walk at 4.7 per cent on the remains beat against this file's 14.4
and canon 42 recorded the cap sweep as tuned against a model that does not
match the shipped rules. Sections A-F never touched the walk and stand.

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
    STARTER_ROOM_RADIUS,
    TRUNK_WIDTH,
    place_chambers,
    plan_trunk,
    segment_point_distance,
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
import re

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

# DenController, shipped values. DIG_CELLS_PER_DAY_BY_TIER is IMPORTED and is
# already the shipped product -- see the assertion below for why this file no
# longer carries a copy of it.
SPOIL_PER_CELL = 7.8
EXPANSION_BASELINE_CELLS = 900

# DenTunnelProfile.asset, floor index 2.
TUNNEL_WIDTH_AT_MOUTH = 3
TUNNEL_TIP_WIDTH = 2

EXCAVATOR_FLOOR = 2


def _assert_shipped_constants():
    """A cross-check against the SOURCE, which is what a transcription can
    never be.

    THIS FILE WAS UNRUNNABLE FOR A RELEASE AND THE REASON IS WORTH KEEPING.
    It held its own copy of the shipped rates and multiplied the IMPORTED
    DIG_CELLS_PER_DAY_BY_TIER by DIG_SCALE to reach them -- correct while that
    import was the pre-scale base {7,11,17,26,38}. Stage 2b then made
    sim_den_growth.py declare the PRODUCT, so this scaled an already-scaled
    list, computed {0.3,...} and exited at import. Canon 42 records three ways a
    sim fails: destroyed, silently filtered, quietly out of date. This is a
    fourth -- KILLED BY A FIX TO A FILE IT IMPORTS FROM -- and the local copy is
    what made it possible, so the copy is deleted rather than corrected.

    What replaces it reads DenController.cs itself. A check against another
    transcription is a check on the transcription."""
    src = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets',
                       'Scripts', 'DungeonCore', 'DenController.cs')
    if not os.path.exists(src):
        return                       # a Tools/ folder on its own still runs
    text = open(src, encoding='utf-8').read()
    m = re.search(r'DigCellsPerDay\s*=\s*\{([^}]*)\}', text)
    if not m:
        raise SystemExit(
            "DenController.cs no longer declares DigCellsPerDay in a form this "
            "check can read. Fix the pattern rather than deleting the check.")
    shipped = [float(v.strip().rstrip('fF')) for v in m.group(1).split(',')]
    if [round(v, 1) for v in DIG_CELLS_PER_DAY_BY_TIER] != [round(v, 1) for v in shipped]:
        raise SystemExit(
            "sim_den_growth.py derives %s but DenController.cs ships %s. "
            "Re-run sim_den_cavity_growth.py before trusting anything here."
            % (DIG_CELLS_PER_DAY_BY_TIER, shipped))


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
        budget = DIG_CELLS_PER_DAY_BY_TIER[tier - 1] * expansion_multiplier(claimed)

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


# ---- the shipped-rules walk (sections G and J, replaced) -----------------
#
# WHY THE OLD SECTIONS WERE RETIRED. Sections G, I and J walked THROUGH a
# chamber, a road, a site and claimed ground and merely noted them, modelled
# no never-retrace set, and tested a point where the leg tests its 2-wide
# brush. Road Breach Report measured the shipped walk at 4.7 per cent on the
# remains beat against this file's 14.4, and canon 42 records the consequence:
# the cap sweep that chose 2400 over 1107 was tuning a knob against a model
# that does not match the shipped rules. What follows is a port of the
# report's own mirror walk (TESTING/Commands.cs, WalkTheDig) -- seat rule,
# brush test, never-retrace set, hard turn, sense-and-drive on remains, one
# breach per dawn resolved through a ported SkirmishResolver -- validated
# against the report's published 300-seed row before the sweep was trusted
# (1000 seeds here): contact 53.4 against 52.0, first contact d58 against d57,
# stop d106 against d106, cells 2398 against 2384, stuck 0.0 against 0.0,
# chamber-refusal share 1.4 against 1.4. It runs slightly HOT on the remains column (no real sites,
# a straight trunk), so the report stays the real-geometry figure and this is
# the sweep.
#
# DECLARED APPROXIMATIONS, each replacing a real builder the report calls:
#   * the TRUNK is a straight Bresenham chord dilated at width 5 (the real
#     one meanders);
#   * the OUTPOST is one 15x15 square on an in-band chord cell, its chord
#     cells removed from road (a LANE paints nothing); no other sites;
#   * the gate BEAT is +/- gateBeatHalfCells arc cells along the chord.
# Everything else is a port: chamber placement and CA, DenTunnelBuilder.Plan,
# the wobble rasterise, the cavity carve, the remains sampler, the seat rule,
# the walk, the resolver, the dawn arithmetic.

_SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets')

_REF_NAMES = ["cut", "retrace", "clamp", "reserve", "chamber", "site",
              "road", "outpost"]
(_CUT, _R_RETRACE, _R_CLAMP, _R_RESERVE, _R_CHAMBER, _R_SITE, _R_ROAD,
 _R_OUTPOST) = range(8)


def _read_floor2_entry():
    """The authored floor-2 exploratory numbers, read off the ASSET rather
    than transcribed -- this file was unrunnable for a release because it kept
    a copy of a table it also imported, and the header records that. Falls
    back to the shipped values so a Tools/ folder on its own still runs."""
    fallback = dict(exploratoryCellCap=2400, exploratoryBudget=3.0,
                    exploratoryWidth=2, exploratorySenseRadius=30,
                    exploratoryDriftDegrees=12.0, cavityBox=24,
                    cavityMinCells=350, cavityMaxCells=400,
                    cavityTier1Cells=150, runCount=4, endpointClamp=0.85,
                    maxRunFraction=0.90, minRunCells=12, deadEndMin=30,
                    deadEndMax=80, width=3, tipWidth=2, bandInner=0.15,
                    bandOuter=0.65, landingKeepClear=10)
    path = os.path.join(_SRC, 'ScriptableObjects', 'Floors',
                        'DenTunnelProfile.asset')
    if not os.path.exists(path):
        return fallback
    text = open(path, encoding='utf-8').read()
    at = text.find('floorIndex: 2')
    if at < 0:
        return fallback
    block = text[at:]
    nxt = block.find('- floorIndex:', 1)
    if nxt > 0:
        block = block[:nxt]
    out = dict(fallback)
    for key in fallback:
        m = re.search(r'\b%s:\s*([-\d.]+)' % key, block)
        if m:
            v = m.group(1)
            out[key] = float(v) if '.' in v else int(v)
    return out


def _read_stat_overrides(rel, fallback):
    """A prefab variant's (maxHP, attackDamage, attackCooldown), read off its
    m_Modifications when the file is present."""
    path = os.path.join(_SRC, rel)
    if not os.path.exists(path):
        return fallback
    text = open(path, encoding='utf-8').read()
    out = list(fallback)
    for i, key in enumerate(('maxHP', 'attackDamage', 'attackCooldown')):
        m = re.search(r'propertyPath: %s\s*\n\s*value:\s*([-\d.]+)' % key, text)
        if m:
            out[i] = float(m.group(1))
    return tuple(out)


def _read_patrol_numbers():
    """(gateSquadSize, roadSquadSize, gateBeatHalfCells), read off the
    controller's serialised defaults. The scene could retune them; the report
    reads the live instance and stays authoritative for the beat columns."""
    path = os.path.join(_SRC, 'Scripts', 'Floors', 'DwarvenPatrolController.cs')
    out = [1, 2, 60]
    if not os.path.exists(path):
        return out
    text = open(path, encoding='utf-8').read()
    for i, key in enumerate(('gateSquadSize', 'roadSquadSize',
                             'gateBeatHalfCells')):
        m = re.search(r'private int %s = (\d+)' % key, text)
        if m:
            out[i] = int(m.group(1))
    return out


# DwarfGuard Variant.prefab and Kobold Variant.prefab, with the values at the
# time of writing as fallbacks. The kobolds strike first in the resolver
# because their prefab authors the longer detectionRange.
_GUARD = _read_stat_overrides(
    os.path.join('Prefabs', 'Monsters', 'Dwarves', 'DwarfGuard Variant.prefab'),
    (70.0, 16.0, 1.1))
_KOBOLD = _read_stat_overrides(
    os.path.join('Prefabs', 'Monsters', 'Wild', 'Kobold Variant.prefab'),
    (22.0, 6.0, 1.4))
_GATE_SQUAD, _ROAD_SQUAD, _BEAT_HALF = _read_patrol_numbers()
_DIGGERS_AT_TIER = [1, 1, 2, 3, 4]     # DenController.DiggersByTier
_CLAIM0, _CLAIM_PER_DAY = 450, 12      # Road Breach Report's typical profile
_WALK_DAYS = 200
_OUTPOST_SIDE = 15
_WOBBLE_STEP, _WOBBLE_AMP = 16, 3.0    # the Rasterise call the report makes


def _takes_the_road(guards, kobolds):
    """SkirmishResolver.TakesTheRoad, ported: instantaneous damage on the
    cooldown, focus fire, kobolds strike first, stalemate is the road
    holding."""
    gmax, ghit, gcd = _GUARD
    kmax, khit, kcd = _KOBOLD
    ghp = [gmax] * guards
    khp = [kmax] * kobolds
    gnx = [gcd * 0.5] * guards
    knx = [0.0] * kobolds

    def alive(hp):
        for i, v in enumerate(hp):
            if v > 0:
                return i
        return -1

    t = 0.0
    while t < 600.0:
        if alive(ghp) < 0:
            return True
        if alive(khp) < 0:
            return False
        for i in range(guards):
            if ghp[i] <= 0 or t < gnx[i]:
                continue
            h = alive(khp)
            if h < 0:
                return False
            khp[h] -= ghit
            gnx[i] = t + gcd
        for i in range(kobolds):
            if khp[i] <= 0 or t < knx[i]:
                continue
            h = alive(ghp)
            if h < 0:
                return True
            ghp[h] -= khit
            knx[i] = t + kcd
        t += 0.02
    return False


_SKIRMISH = None


def _skirmish(guards, kobolds):
    global _SKIRMISH
    if _SKIRMISH is None:
        _SKIRMISH = {(g, k): _takes_the_road(g, k)
                     for g in (_GATE_SQUAD, _ROAD_SQUAD) for k in range(1, 5)}
    return _SKIRMISH[(guards, min(4, max(1, kobolds)))]


def _bres(a, b):
    x0, y0 = a
    x1, y1 = b
    dx, dy = abs(x1 - x0), abs(y1 - y0)
    sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
    err = dx - dy
    out = []
    while True:
        out.append((x0, y0))
        if x0 == x1 and y0 == y1:
            return out
        e2 = 2 * err
        if e2 > -dy:
            err -= dy
            x0 += sx
        if e2 < dx:
            err += dx
            y0 += sy


def _dilate(cells, width, clamp_r):
    half = (width - 1) // 2
    extra = (width - 1) - 2 * half
    c2 = clamp_r * clamp_r
    out = set()
    for (cx, cy) in cells:
        for dx in range(-half, half + extra + 1):
            for dy in range(-half, half + extra + 1):
                px, py = cx + dx, cy + dy
                if px * px + py * py > c2:
                    continue
                out.add((px, py))
    return out


def _ca_blob(rng, centre, size):
    """RunChamberCA's shape, ported from the report's CaBlob: 0.45 fill,
    walled border, four smoothing passes at the n >= 5 rule, flood from the
    box centre."""
    walls = [[(x == 0 or y == 0 or x == size - 1 or y == size - 1)
              or rng.random() < 0.45 for y in range(size)] for x in range(size)]
    for _ in range(4):
        nxt = [[False] * size for _ in range(size)]
        for x in range(size):
            for y in range(size):
                n = 0
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        if dx == 0 and dy == 0:
                            continue
                        px, py = x + dx, y + dy
                        if (px < 0 or py < 0 or px >= size or py >= size
                                or walls[px][py]):
                            n += 1
                nxt[x][y] = n >= 5
        walls = nxt
    half = size // 2
    if walls[half][half]:
        return []
    seen = [[False] * size for _ in range(size)]
    out = []
    stack = [(half, half)]
    while stack:
        x, y = stack.pop()
        if x < 0 or y < 0 or x >= size or y >= size or seen[x][y] or walls[x][y]:
            continue
        seen[x][y] = True
        out.append((centre[0] + x - half, centre[1] + y - half))
        stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return out


def _normalise_angle(a):
    while a > math.pi:
        a -= 2.0 * math.pi
    while a < -math.pi:
        a += 2.0 * math.pi
    return a


def _pick_free_bearing(rng, runs, den):
    min_sep = math.radians(40.0)
    best = rng.random() * 2.0 * math.pi
    best_worst = -1.0
    for _ in range(24):
        cand = rng.random() * 2.0 * math.pi
        worst = math.pi
        for (_a, b, _cid) in runs:
            ang = math.atan2(b[1] - den[1], b[0] - den[0])
            off = abs(_normalise_angle(cand - ang))
            worst = min(worst, off)
        if worst >= min_sep:
            return cand
        if worst > best_worst:
            best_worst, best = worst, cand
    return best


def _plan_den(rng, radius, centres, landing, cfg):
    """DenTunnelBuilder.Plan, ported: anchor uniform in band clear of the
    landing and of chamber seats, eligible chambers nearest first, dead ends
    fill on spread bearings with the shrink-rather-than-surrender retry."""
    inner = max(8 + 2, int(round(radius * cfg['bandInner'])))
    outer = int(round(radius * cfg['bandOuter']))
    keep = STARTER_ROOM_RADIUS + cfg['landingKeepClear']
    den = None
    for _ in range(96):
        dx = rng.randint(-outer, outer)
        dy = rng.randint(-outer, outer)
        d2 = dx * dx + dy * dy
        if d2 < inner * inner or d2 > outer * outer:
            continue
        if math.hypot(dx - landing[0], dy - landing[1]) < keep:
            continue
        if any(math.hypot(dx - cx, dy - cy) < CHAMBER_SEAT_CLEARANCE
               for cx, cy in centres):
            continue
        den = (dx, dy)
        break
    if den is None:
        return None
    clamp_r = radius * cfg['endpointClamp']
    max_run = radius * cfg['maxRunFraction']
    eligible = []
    for i, c in enumerate(centres):
        if math.hypot(c[0], c[1]) > clamp_r:
            continue
        d = math.hypot(c[0] - den[0], c[1] - den[1])
        if d < cfg['minRunCells'] or d > max_run:
            continue
        if segment_point_distance(den, c, landing) < keep:
            continue
        eligible.append(i)
    eligible.sort(key=lambda i: math.hypot(centres[i][0] - den[0],
                                           centres[i][1] - den[1]))
    runs = [(den, centres[i], i) for i in eligible[:cfg['runCount']]]
    for _ in range(cfg['runCount'] - len(runs)):
        placed = False
        for _attempt in range(16):
            if placed:
                break
            bearing = _pick_free_bearing(rng, runs, den)
            ln = min(rng.randint(cfg['deadEndMin'], cfg['deadEndMax']),
                     int(round(max_run)))
            for shrink in range(3):
                if placed:
                    break
                try_len = max(cfg['minRunCells'], ln >> shrink)
                stop = (den[0] + int(round(try_len * math.cos(bearing))),
                        den[1] + int(round(try_len * math.sin(bearing))))
                if math.hypot(stop[0], stop[1]) > clamp_r:
                    continue
                if segment_point_distance(den, stop, landing) < keep:
                    continue
                runs.append((den, stop, -1))
                placed = True
    return den, runs


def _wobble(rng, a, b):
    line = [a]
    dx, dy = b[0] - a[0], b[1] - a[1]
    ln = math.hypot(dx, dy)
    if ln < _WOBBLE_STEP * 2:
        line.append(b)
        return line
    px, py = -dy / ln, dx / ln
    knots = max(1, int(round(ln / _WOBBLE_STEP)))
    for k in range(1, knots):
        t = k / float(knots)
        taper = math.sin(t * math.pi)
        off = (rng.random() * 2.0 - 1.0) * _WOBBLE_AMP * taper
        line.append((int(round(a[0] + dx * t + px * off)),
                     int(round(a[1] + dy * t + py * off))))
    line.append(b)
    return line


def _centreline(poly):
    seen = set()
    out = []
    for i in range(len(poly) - 1):
        for p in _bres(poly[i], poly[i + 1]):
            if p not in seen:
                seen.add(p)
                out.append(p)
    return out


def _taper_cells(line, mouth_w, tip_w, clamp_r):
    cells = set()
    n = len(line)
    for i, c in enumerate(line):
        t = i / float(n - 1) if n > 1 else 0.0
        w = max(tip_w, int(round(mouth_w + (tip_w - mouth_w) * t)))
        cells |= _dilate([c], w, clamp_r)
    return cells


def _carve_cavity(rng, den, cfg):
    raw = None
    for _ in range(8):
        r = _ca_blob(rng, den, max(8, cfg['cavityBox']))
        if r:
            raw = r
            break
    if raw is None:
        return None
    s = set(raw)
    safety = 0
    while len(s) < cfg['cavityMinCells'] and safety < 4000:
        safety += 1
        cand = []
        for (x, y) in s:
            for p in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if p not in s:
                    cand.append(p)
        if not cand:
            break
        s.add(cand[rng.randrange(len(cand))])
    while len(s) > cfg['cavityMaxCells']:
        far, mx = den, -1
        for c in s:
            if c == den:
                continue
            sq = (c[0] - den[0]) ** 2 + (c[1] - den[1]) ** 2
            if sq > mx:
                mx, far = sq, c
        if far == den:
            break
        s.discard(far)
    s.add(den)
    t1 = max(1, min(cfg['cavityTier1Cells'], len(s)))
    open_c = {den}
    queue = [den]
    qi = 0
    while qi < len(queue) and len(open_c) < t1:
        x, y = queue[qi]
        qi += 1
        for p in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if len(open_c) >= t1:
                break
            if p in s and p not in open_c:
                open_c.add(p)
                queue.append(p)
    return s, open_c


def _build_shipped_world(seed, radius, cfg):
    """One seed's floor, in the report's order: trunk, outpost, chambers,
    landing, den plan, rasterised runs, cavity, remains."""
    rng = random.Random(seed * 7919 + EXCAVATOR_FLOOR)
    chord = plan_trunk(rng, radius)
    if chord is None:
        return None
    line = _bres(*chord)
    road = _dilate(line, TRUNK_WIDTH, radius)

    inband = [i for i, c in enumerate(line)
              if radius * cfg['bandInner'] <= math.hypot(c[0], c[1])
              <= radius * cfg['bandOuter']]
    if not inband:
        return None
    oi = inband[rng.randrange(len(inband))]
    ox, oy = line[oi]
    half = _OUTPOST_SIDE // 2
    outpost = set((ox + dx, oy + dy) for dx in range(-half, half + 1)
                  for dy in range(-half, half + 1))
    road -= outpost

    centres = place_chambers(rng, radius)[0]
    chamber = set()
    for cc in centres:
        for c in _ca_blob(rng, cc, rng.randint(8, 14)):
            if c not in road and c not in outpost:
                chamber.add(c)

    inner = int(round(radius * cfg['bandInner']))
    landing = (0, 0)
    for _ in range(64):
        lx = rng.randint(-inner, inner)
        ly = rng.randint(-inner, inner)
        if lx * lx + ly * ly <= inner * inner:
            landing = (lx, ly)
            break

    plan = _plan_den(rng, radius, centres, landing, cfg)
    if plan is None:
        return None
    den, runs = plan
    ras_clamp = radius - 10
    tunnels = []
    owned_tunnel = set()
    for (ra, rb, cid) in runs:
        tl = _centreline(_wobble(rng, ra, rb))
        if len(tl) < 2:
            continue
        tunnels.append((tl, cid))
        owned_tunnel |= _taper_cells(tl, cfg['width'], cfg['tipWidth'],
                                     ras_clamp)
    if not tunnels:
        return None

    carve = _carve_cavity(rng, den, cfg)
    if carve is None:
        return None
    reserve_all, open_c = carve

    remains = buried_sites(seed * 7919 + EXCAVATOR_FLOOR, radius)
    return dict(road=road, outpost=outpost, chamber=chamber,
                reserve=reserve_all - open_c,
                owned=road | outpost | chamber | open_c | owned_tunnel,
                remains=[tuple(r) for r in remains],
                tunnels=tunnels, chord=line, outpost_s=oi)


def _can_cut(w, cell, clamp2, width, on_leg):
    if cell in on_leg:
        return _R_RETRACE
    half = (width - 1) // 2
    extra = (width - 1) - 2 * half
    for dx in range(-half, half + extra + 1):
        for dy in range(-half, half + extra + 1):
            p = (cell[0] + dx, cell[1] + dy)
            if p[0] * p[0] + p[1] * p[1] > clamp2:
                return _R_CLAMP
            if p in w['reserve']:
                return _R_RESERVE
            if p in w['chamber']:
                return _R_CHAMBER
            if p in w['outpost']:
                return _R_OUTPOST
            if p in w['road']:
                return _R_ROAD
    return _CUT


def _seat_leg(w, line, clamp2, width):
    """TrySeatLeg, ported: walk back past every cell another feature owns,
    then a brush width more, then eight bearings from straight on outwards."""
    empty = set()
    i = len(line) - 1
    while i > 0 and (line[i] in w['chamber'] or line[i] in w['outpost']
                     or line[i] in w['road']):
        i -= 1
    i = max(0, i - width)
    while i >= 0:
        at = line[i]
        prev = line[max(0, i - 1)]
        along = 0.0 if at == prev else math.atan2(at[1] - prev[1],
                                                  at[0] - prev[0])
        for k in range(8):
            b = along + math.pi * 0.25 * k
            step = (at[0] + int(round(math.cos(b))),
                    at[1] + int(round(math.sin(b))))
            if step == at:
                continue
            if _can_cut(w, step, clamp2, width, empty) != _CUT:
                continue
            return i, b
        i -= 1
    return None


def _on_beat(w, cell):
    best, bi = None, -1
    for i, c in enumerate(w['chord']):
        d = (c[0] - cell[0]) ** 2 + (c[1] - cell[1]) ** 2
        if best is None or d < best:
            best, bi = d, i
    return abs(bi - w['outpost_s']) <= _BEAT_HALF


def _shipped_walk(seed, w, radius, cap, budget, sense, cfg, days=_WALK_DAYS):
    """WalkTheDig, ported dawn for dawn. Returns a dict of counters -- every
    refusal split by kind, because a dig that spends half its cap and cannot
    say why is the shape this project has paid for before."""
    rng = random.Random(seed ^ 0x51E2C0DE)
    r = dict(met_road=False, first_road=0, remains=0, cells=0, stop=0,
             dead_end=False, boxed=0, short=0, worked=0,
             refusals=[0] * 8, breaches=0, den_won=0, guard_won=0,
             abandoned=0)

    clamp_r = radius * cfg['endpointClamp']
    clamp2 = clamp_r * clamp_r
    width = max(2, cfg['exploratoryWidth'])
    drift = math.radians(cfg['exploratoryDriftDegrees'])

    ranked = sorted(w['tunnels'],
                    key=lambda t: len(t[0]) + (100000 if t[1] < 0 else 0),
                    reverse=True)
    seat = None
    for (line, cid) in ranked:
        s = _seat_leg(w, line, clamp2, width)
        if s is not None:
            seat = (line[s[0]], s[1], cid)
            break
    if seat is None:
        return r
    mouth, heading, cid = seat
    r['dead_end'] = cid < 0

    on_leg = {mouth}
    cut = set()
    x, y = float(mouth[0]), float(mouth[1])
    claimed, hoard = float(_CLAIM0), 0.0
    open_c, carry = float(cfg['cavityTier1Cells']), 0.0
    taken = set()
    driving, drive_to = False, (0, 0)

    for day in range(1, days + 1):
        claimed += _CLAIM_PER_DAY
        if day <= GRACE_DAYS:
            continue
        if w['remains'] and len(taken) >= len(w['remains']):
            r['stop'] = day
            break
        if cap and len(cut) >= cap:
            r['stop'] = day
            break

        tier = tier_for(hoard)
        reserve_budget = DIG_CELLS_PER_DAY_BY_TIER[tier - 1] \
            * expansion_multiplier(claimed)
        opened = min(reserve_budget, cfg['cavityMaxCells'] - open_c)
        if opened > 0:
            open_c += opened
            hoard += opened * SPOIL_PER_CELL

        carry += reserve_budget * budget
        rock = int(carry)
        carry -= rock
        if cap:
            rock = min(rock, cap - len(cut))
        if rock <= 0:
            continue

        spent, blocked, guard = 0, 0, rock * 4 + 32
        boxed, took_one = False, False
        breached_today = False
        breach_at = (0, 0)

        while spent < rock and guard > 0:
            guard -= 1
            if driving:
                heading = math.atan2(drive_to[1] - y, drive_to[0] - x)
            else:
                heading += (rng.random() * 2.0 - 1.0) * drift
            nx, ny = x + math.cos(heading), y + math.sin(heading)
            cell = (int(round(nx)), int(round(ny)))

            kind = _can_cut(w, cell, clamp2, width, on_leg)
            if kind != _CUT:
                r['refusals'][kind] += 1
                if kind == _R_ROAD and not r['met_road']:
                    r['met_road'] = True
                    r['first_road'] = day
                if kind == _R_ROAD and not breached_today:
                    breached_today = True
                    breach_at = (int(round(x)), int(round(y)))
                heading += math.pi * (0.5 + rng.random())
                driving = False
                blocked += 1
                if blocked > 64:
                    boxed = True
                    break
                continue
            blocked = 0

            x, y = nx, ny
            on_leg.add(cell)
            half = (width - 1) // 2
            extra = (width - 1) - 2 * half
            for dx in range(-half, half + extra + 1):
                for dy in range(-half, half + extra + 1):
                    p = (cell[0] + dx, cell[1] + dy)
                    if p[0] * p[0] + p[1] * p[1] > clamp2:
                        continue
                    if p in w['owned'] or p in cut:
                        continue
                    cut.add(p)
                    spent += 1

            if driving and cell == drive_to:
                driving = False
                if cell not in taken:
                    taken.add(cell)
                    r['remains'] += 1
                    took_one = True
                heading = rng.random() * 2.0 * math.pi
            elif not driving:
                best, tgt = None, None
                for rem in w['remains']:
                    if rem in taken:
                        continue
                    d = ((rem[0] - cell[0]) ** 2 + (rem[1] - cell[1]) ** 2)
                    if d > sense * sense:
                        continue
                    if best is None or d < best:
                        best, tgt = d, rem
                if tgt is not None:
                    driving, drive_to = True, tgt

        r['worked'] += 1
        if boxed:
            r['boxed'] += 1
        if spent < rock:
            r['short'] += 1

        abandon = False
        if breached_today:
            r['breaches'] += 1
            guards = _GATE_SQUAD if _on_beat(w, breach_at) else _ROAD_SQUAD
            party = _DIGGERS_AT_TIER[tier - 1]
            if party > 0:
                if _skirmish(guards, party):
                    r['den_won'] += 1
                else:
                    r['guard_won'] += 1
                    abandon = True

        if took_one or boxed or abandon:
            on_leg = {(int(round(x)), int(round(y)))}
        if abandon:
            heading += math.pi
            r['abandoned'] += 1

    r['cells'] = len(cut)
    return r


def _walk_row(label, runs, cap):
    n = len(runs)
    if n == 0:
        return "   %-12s none" % label
    stuck = sum(1 for r in runs if r['cells'] == 0)
    contact = sum(1 for r in runs if r['met_road'])
    remains = sum(1 for r in runs if r['remains'] > 0)
    cells = statistics.mean(r['cells'] for r in runs)
    firsts = sorted(r['first_road'] for r in runs if r['met_road'])
    stops = sorted(r['stop'] for r in runs if r['stop'] > 0)
    bound = sum(1 for r in runs if cap and r['cells'] >= cap)
    return ("   %-12s %5d %6.1f%% %7.1f%% %8.1f%% %6.0f  %-4s %-5s %6.1f%%"
            % (label, n, 100.0 * stuck / n, 100.0 * contact / n,
               100.0 * remains / n, cells,
               ("d%d" % firsts[len(firsts) // 2]) if firsts else "-",
               ("d%d" % stops[len(stops) // 2]) if stops else "-",
               100.0 * bound / n))


def _walk_seeds(seeds, radius, cfg):
    worlds = []
    for s in range(seeds):
        w = _build_shipped_world(s, radius, cfg)
        if w is not None:
            worlds.append((s, w))
    return worlds


def report_shipped_walk(seeds, radius):
    """G. THE SHIPPED-RULES WALK -- validation before anything is swept."""
    cfg = _read_floor2_entry()
    cap = cfg['exploratoryCellCap']
    budget = float(cfg['exploratoryBudget'])
    sense = cfg['exploratorySenseRadius']
    worlds = _walk_seeds(seeds, radius, cfg)
    runs = [_shipped_walk(s, w, radius, cap, budget, sense, cfg)
            for (s, w) in worlds]
    print("G. THE SHIPPED-RULES WALK  (%d/%d seeds usable; authored cap %d, "
          "budget x%.0f, sense %d)" % (len(worlds), seeds, cap, budget, sense))
    print("   Road Breach Report's mirror, ported. Validated at cap 2400 x3 "
          "sense 15 over")
    print("   1000 seeds against the report's published 300-seed row: contact "
          "53.4/52.0,")
    print("   first d58/d57, stop d106/d106, cells 2398/2384, stuck 0.0/0.0, "
          "remains 6.5/4.7.")
    print("   The remains column runs HOT (no real sites, a straight trunk): "
          "treat the")
    print("   report as the real-geometry figure and this file as the sweep.")
    print()
    print("   start        seeds  stuck  contact  remains  cells  1st  stop  "
          " capbound")
    dead = [r for r in runs if r['dead_end']]
    cham = [r for r in runs if not r['dead_end']]
    print(_walk_row("dead end", dead, cap))
    print(_walk_row("chamber ctr", cham, cap))
    print(_walk_row("ALL", runs, cap))
    tot = [0] * 8
    worked = boxed = short = 0
    for r in runs:
        for i in range(1, 8):
            tot[i] += r['refusals'][i]
        worked += r['worked']
        boxed += r['boxed']
        short += r['short']
    ssum = sum(tot[1:]) or 1
    print("   refusals: " + " ".join(
        "%s %.1f%%" % (_REF_NAMES[i], 100.0 * tot[i] / ssum)
        for i in range(1, 8)))
    print("   dawns worked %d per seed, %.1f%% short, %.1f%% boxed; breaches "
          "%d, den won %d,"
          % (worked // max(1, len(runs)), 100.0 * short / max(1, worked),
             100.0 * boxed / max(1, worked),
             sum(r['breaches'] for r in runs),
             sum(r['den_won'] for r in runs)))
    print("   road held %d, legs abandoned %d."
          % (sum(r['guard_won'] for r in runs),
             sum(r['abandoned'] for r in runs)))
    print()


def report_cap_remeasured(seeds, radius):
    """J. THE CAP, RE-MEASURED against the shipped rules. Canon 42's 'The cap
    re-measured' records the ruling this printed: 2400 KEPT on the scale
    argument, the beat restored by sense 15 -> 30 rather than by rock."""
    cfg = _read_floor2_entry()
    sense = cfg['exploratorySenseRadius']
    worlds = _walk_seeds(seeds, radius, cfg)
    print("J. THE CAP, RE-MEASURED  (shipped rules, sense %d, %d seeds per "
          "cell)" % (sense, len(worlds)))
    print("   cap     rate  remains%  contact%  1st    stop   cells  "
          "capbound%")
    for cap in (1107, 1600, 2400, 3200, 0):
        for budget in (2.0, 3.0):
            runs = [_shipped_walk(s, w, radius, cap, budget, sense, cfg)
                    for (s, w) in worlds]
            n = len(runs)
            rem = 100.0 * sum(1 for r in runs if r['remains'] > 0) / n
            con = 100.0 * sum(1 for r in runs if r['met_road']) / n
            firsts = sorted(r['first_road'] for r in runs if r['met_road'])
            stops = sorted(r['stop'] for r in runs if r['stop'] > 0)
            cells = statistics.mean(r['cells'] for r in runs)
            bound = 100.0 * sum(1 for r in runs if cap and r['cells'] >= cap) / n
            print("   %-7s x%-4.0f %8.1f %9.1f  %-5s %-6s %5.0f %9.1f"
                  % (cap if cap else "none", budget, rem, con,
                     ("d%d" % firsts[len(firsts) // 2]) if firsts else "-",
                     ("d%d" % stops[len(stops) // 2]) if stops else "-",
                     cells, bound))
    print()
    print("   The budget stays the PACING knob and the cap the CONTENT knob --")
    print("   x2 against x3 moves the remains column by under half a point at")
    print("   any fixed cap while moving first contact and the stop day by ten")
    print("   to twenty-six days. The 7.3-8.0 and 14.0-14.7 figures the old")
    print("   section J printed were the retired model's; do not quote them.")
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

    report_shipped_walk(min(seeds, 300), radius)
    report_cap_remeasured(min(seeds, 300), radius)

    print("E. THE SHARED-BUDGET QUESTION (fork 1)")
    print("   Reserve headroom is %d cells; the median tunnel is %.0f. A shared"
          % (EXCAVATOR_MAX_CELLS - EXCAVATOR_TIER1_CELLS, median_cells))
    print("   budget therefore spends %.0f%% of an excavator's LIFETIME dig on"
          % (100.0 * median_cells / (EXCAVATOR_MAX_CELLS - EXCAVATOR_TIER1_CELLS)))
    print("   one approach. Column D above shows what that does to the hole.")


if __name__ == "__main__":
    main()
