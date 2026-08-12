#!/usr/bin/env python3
"""
Headless simulation of the DEN CAVITY (canon 42), built on sim_den_tunnels'
chamber placement and sim_holy_quota's band model -- imported, not duplicated.

There is no Unity here, so this cannot prove code compiles or renders. What it
CAN prove, ahead of the expensive test cycle, is whether the agreed cavity is
geometrically viable on the two den floors, and it opens by measuring the thing
canon got wrong.

CANON 19'S CHAMBER COMPARATOR IS WRONG, AND EVERY LATER NUMBER LEANS ON IT.
Canon 19 says a cave chamber is "roughly 100--200" cells and canon 42 justifies
the cavity's 250-400 band and the excavator's 600 cap against that figure. The
figure was never measured. RunChamberCA at the shipped parameters -- box 8-14,
fill 0.45, four smoothing iterations, all read off FloorTemplatePrefab 1 -- has
a median of about 48 and CANNOT EXCEED 133, because box 14 is the largest roll
and its interior is 12x12. So a 300-cell hole is roughly 6x the median chamber
and over 2x the largest chamber that can exist, not the 1.5-3x canon implies.

The targets survive anyway, on canon 19's OTHER rule from the same paragraph --
"keep a span near twice the chamber box size, not five times it" -- which was
derived from the actual span-62 plaza failure and is sound. This sim reports
against the span rule and prints the measured chamber figures beside it, so the
canon correction rides on numbers rather than on an assertion.

What it models, and from where:

  CHAMBERS      sim_den_tunnels.place_chambers (imported) for the centres, plus
                RunChamberCA reimplemented here for the INTERIORS -- which
                sim_den_tunnels deliberately did not model, because linking
                centres never needed them. The comparator does.
  BAND          DenTunnelBuilder.Plan's OWN arithmetic, not sim_holy_quota.band:
                Plan clamps the inner edge to coreExclusion + 2 and does NOT
                clamp the outer edge by the rim margin. Mirroring Plan matters
                more than sharing a helper here.
  ANCHOR        Plan's 96-sample rejection loop, plus fork 2's new rejection of
                a seat that would drop the cavity on top of a chamber.
  CAVITY        Fork 1: RunChamberCA's cellular automata for the silhouette,
                then GenerateCoreCavernAndTunnels' top-up/trim to force the
                count into band. Both carvers already ship; neither alone does
                the job -- the CA has no size control at all and the top-up
                disc reads as a bubble.
  RESERVE       Fork 3(c): the excavator reserves its MAXIMUM footprint at
                generation, on the reservedCoreCells precedent, and carves
                within it at runtime. Tier 1 is a sub-blob grown from the
                centre of that reserve.

  NOT modelled, deliberately: rivers (carved after the cavity, and they simply
  win, per canon 42's precedence), the runtime growth's dependence on player
  claiming (it is player state, not geometry -- what IS measured is that the
  reserve is big enough to hold the growth), and the wobble of a tunnel
  centreline (it drifts by at most a cell per step and cannot move an endpoint).

Usage:  python3 sim_den_cavity.py [seeds]
"""

import math
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim_den_tunnels import (
    CHAMBER_SPACING,
    DEN_FLOORS,
    EXCLUSION_FROM_CENTRE,
    LANDING_KEEP_CLEAR,
    MAX_RUN_FRACTION,
    MIN_RUN_CELLS,
    STARTER_ROOM_RADIUS,
    TUNNEL_TIP_WIDTH,
    TUNNEL_WIDTH,
    place_chambers,
    segment_point_distance,
)

# ---- authored constants, each read off a shipped asset ------------------

# RunChamberCA, FloorTemplatePrefab 1.
MIN_CHAMBER_BOX = 8
MAX_CHAMBER_BOX = 14
CA_INITIAL_WALL_CHANCE = 0.45
CA_SMOOTHING_ITERATIONS = 4
MIN_CHAMBER_CELL_COUNT = 6

# DenTunnelProfile: the shipped entries for floor index 1 and 2.
BAND_INNER = 0.15
BAND_OUTER = 0.65
RUN_COUNT = {1: 3, 2: 4}
DEN_KIND = {1: 'Occupier', 2: 'Excavator'}

# Canon 42's cavity sizes, confirmed by the designer this session.
OCCUPIER_MIN_CELLS = 250
OCCUPIER_MAX_CELLS = 400
EXCAVATOR_TIER1_CELLS = 150
EXCAVATOR_MAX_CELLS = 600

# Fork 2: a seat nearer than this to a chamber centre is rejected, so a real
# cavity is never replaced by a 48-cell chamber standing in for it. Sized as
# cavity radius (~11 at 400 cells) + the largest chamber radius (~7) + margin.
CHAMBER_SEAT_CLEARANCE = 20

# CA box per den kind, READ OFF THE SWEEP this file prints, not chosen. The
# figure that matters is how much the size clamp has to correct afterwards: a
# box whose raw yield already lands in band keeps the CA's own silhouette,
# while a box that overshoots gets trimmed farthest-cell-first and comes out
# rounder the more it is cut.
CA_BOX = {'Occupier': 22, 'Excavator': 28}

ORTH4 = ((1, 0), (-1, 0), (0, 1), (0, -1))


# ---- the shipped cellular automata, reimplemented ----------------------

def run_ca(rng, size, fill=CA_INITIAL_WALL_CHANCE,
           iterations=CA_SMOOTHING_ITERATIONS):
    """RunChamberCA: bordered box, random fill, smooth on a >=5 wall rule,
    then flood the open region containing the centre. Returns local (x, y)
    offsets from the box centre, or an empty list when the centre closed."""
    walls = [[True] * size for _ in range(size)]
    for x in range(1, size - 1):
        row = walls[x]
        for y in range(1, size - 1):
            row[y] = rng.random() < fill

    for _ in range(iterations):
        nxt = [[False] * size for _ in range(size)]
        for x in range(size):
            nrow = nxt[x]
            for y in range(size):
                n = 0
                for dx in (-1, 0, 1):
                    nx = x + dx
                    if nx < 0 or nx >= size:
                        n += 3
                        continue
                    wrow = walls[nx]
                    for dy in (-1, 0, 1):
                        if dx == 0 and dy == 0:
                            continue
                        ny = y + dy
                        if ny < 0 or ny >= size:
                            n += 1
                        elif wrow[ny]:
                            n += 1
                nrow[y] = n >= 5
        walls = nxt

    half = size // 2
    if walls[half][half]:
        return []

    seen = [[False] * size for _ in range(size)]
    stack = [(half, half)]
    out = []
    while stack:
        x, y = stack.pop()
        if x < 0 or y < 0 or x >= size or y >= size:
            continue
        if seen[x][y] or walls[x][y]:
            continue
        seen[x][y] = True
        out.append((x - half, y - half))
        stack.append((x + 1, y))
        stack.append((x - 1, y))
        stack.append((x, y + 1))
        stack.append((x, y - 1))
    return out


def chamber_cells(rng):
    """One chamber exactly as GenerateChambers rolls it, as local offsets.
    Returns [] for the rolls the shipped loop discards."""
    box = rng.randint(MIN_CHAMBER_BOX, MAX_CHAMBER_BOX)
    cells = run_ca(rng, box)
    if len(cells) < MIN_CHAMBER_CELL_COUNT:
        return []
    return cells


# ---- fork 1: the cavity carve ------------------------------------------

def top_up_and_trim(rng, cells, lo, hi, centre=(0, 0)):
    """GenerateCoreCavernAndTunnels' size clamp, lifted whole: grow outward to
    random 4-adjacent cells while under the floor, then drop the cell farthest
    from the centre while over the ceiling. Returns (cells, added, removed)."""
    cellset = set(cells)
    added = 0
    removed = 0

    safety = 0
    while len(cellset) < lo and safety < 4000:
        safety += 1
        candidates = []
        for (x, y) in cellset:
            for dx, dy in ORTH4:
                p = (x + dx, y + dy)
                if p not in cellset:
                    candidates.append(p)
        if not candidates:
            break
        cellset.add(candidates[rng.randrange(len(candidates))])
        added += 1

    while len(cellset) > hi:
        far = None
        far_d2 = -1
        for c in cellset:
            if c == centre:
                continue
            d2 = (c[0] - centre[0]) ** 2 + (c[1] - centre[1]) ** 2
            if d2 > far_d2:
                far_d2 = d2
                far = c
        if far is None:
            break
        cellset.discard(far)
        removed += 1

    return cellset, added, removed


def carve_cavity(rng, box, lo, hi, retries=8):
    """Fork 1 end to end: CA silhouette, then the size clamp. Returns
    (cells, raw_ca_yield, added, removed).

    A dead centre is RETRIED rather than discarded, and that is the difference
    between this and GenerateChambers. A chamber that closes is simply not
    placed -- the loop tries another spot and the floor carries one fewer cave.
    The cavity is GUARANTEED (canon 42: one den per floor, guaranteed rather
    than chance-rolled), so there is no "one fewer" to fall back on: a dead roll
    here would hand the den an empty set, the clamp would have nothing to grow
    from, and the floor would carry a den with no hole in it."""
    for _ in range(retries):
        raw = run_ca(rng, box)
        if raw:
            cells, added, removed = top_up_and_trim(rng, raw, lo, hi)
            return cells, len(raw), added, removed
    return set(), 0, 0, 0


def grow_from_centre(cells, target):
    """Fork 3(c): the tier-1 carve inside a reserved footprint. Breadth-first
    from the centre so the sub-blob is connected and contains the origin --
    the runtime dig picks unclaimed frontier cells, which is player state and
    is NOT modelled; what is modelled is that the reserve can hold it."""
    if not cells:
        return set()
    frontier = [(0, 0)] if (0, 0) in cells else [min(cells, key=lambda c: c[0] ** 2 + c[1] ** 2)]
    out = set(frontier)
    i = 0
    while len(out) < target and i < len(frontier):
        x, y = frontier[i]
        i += 1
        for dx, dy in ORTH4:
            p = (x + dx, y + dy)
            if p in cells and p not in out:
                out.add(p)
                frontier.append(p)
                if len(out) >= target:
                    break
    return out


# ---- geometry measurements ---------------------------------------------

def span_of(cells):
    """Largest extent in either axis: the number canon 19's span rule bounds."""
    if not cells:
        return 0
    xs = [c[0] for c in cells]
    ys = [c[1] for c in cells]
    return max(max(xs) - min(xs), max(ys) - min(ys)) + 1


def compactness(cells):
    """Span over the span of a perfect disc of the same area. 1.0 is a circle;
    higher is a straggling blob. Guards against a carve that hits its cell
    count by growing a tendril rather than a room."""
    if not cells:
        return 0.0
    ideal = 2.0 * math.sqrt(len(cells) / math.pi)
    return span_of(cells) / ideal


def dilate_run(a, b, width, tip):
    """A tunnel run's cells, on DenTunnelBuilder's contract: Bresenham
    centreline, dilated per cell at that cell's own tapered width with a square
    brush. Consecutive dilations overlap, which is what keeps a 2-wide tip
    4-connected across a diagonal step."""
    line = list(bresenham(a, b))
    cells = set()
    n = len(line)
    for i, (x, y) in enumerate(line):
        t = i / float(n - 1) if n > 1 else 0.0
        w = max(tip, int(round(width + (tip - width) * t)))
        half = w // 2
        extra = w - half - 1
        for dx in range(-half, extra + 1):
            for dy in range(-half, extra + 1):
                cells.add((x + dx, y + dy))
    return cells


def bresenham(a, b):
    x0, y0 = a
    x1, y1 = b
    dx = abs(x1 - x0)
    dy = abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx - dy
    while True:
        yield (x0, y0)
        if x0 == x1 and y0 == y1:
            return
        e2 = 2 * err
        if e2 > -dy:
            err -= dy
            x0 += sx
        if e2 < dx:
            err += dx
            y0 += sy


def breaches(cavity, run_cells):
    """Is the run 4-connected to the cavity after RebuildDenTunnelCells hands
    back every cell the cavity owns? The chamber breach check's question, asked
    at the other end of the run -- and the one that matters more, because a run
    that cannot reach its own den is a den with no way out."""
    outside = run_cells - cavity
    if not outside:
        return True          # wholly swallowed: trivially the same space
    union = outside | cavity
    start = None
    for c in outside:
        start = c
        break
    seen = {start}
    queue = [start]
    while queue:
        x, y = queue.pop()
        if (x, y) in cavity:
            return True
        for dx, dy in ORTH4:
            p = (x + dx, y + dy)
            if p in union and p not in seen:
                seen.add(p)
                queue.append(p)
    return False


# ---- the anchor pick ----------------------------------------------------

def pick_anchor(rng, radius, chamber_centres, seat_clearance):
    """DenTunnelBuilder.Plan's own 96-sample loop, mirrored including its band
    arithmetic -- Plan clamps the inner edge to coreExclusion + 2 and leaves the
    outer edge unclamped by the rim margin -- plus fork 2's chamber rejection.
    Returns (anchor, inner, outer, rejected_for_chamber)."""
    inner = max(EXCLUSION_FROM_CENTRE + 2, int(round(radius * BAND_INNER)))
    outer = int(round(radius * BAND_OUTER))
    if outer <= inner:
        return None, inner, outer, 0

    keep_clear = STARTER_ROOM_RADIUS + LANDING_KEEP_CLEAR
    chamber_rejects = 0

    for _ in range(96):
        dx = rng.randint(-outer, outer)
        dy = rng.randint(-outer, outer)
        d2 = dx * dx + dy * dy
        if d2 < inner * inner or d2 > outer * outer:
            continue
        if math.hypot(dx, dy) < keep_clear:
            continue
        if any(math.hypot(dx - cx, dy - cy) < seat_clearance
               for cx, cy in chamber_centres):
            chamber_rejects += 1
            continue
        return (dx, dy), inner, outer, chamber_rejects
    return None, inner, outer, chamber_rejects


def plan_runs(rng, den, radius, chamber_centres, run_count):
    """Plan's run selection, reduced to what the cavity cares about: where the
    runs go. Chambers nearest-first, dead ends filling the remainder."""
    keep_clear = STARTER_ROOM_RADIUS + LANDING_KEEP_CLEAR
    max_run = radius * MAX_RUN_FRACTION
    clamp_r = radius * 0.85

    eligible = []
    for (cx, cy) in chamber_centres:
        if math.hypot(cx, cy) > clamp_r:
            continue
        d = math.hypot(cx - den[0], cy - den[1])
        if d < MIN_RUN_CELLS or d > max_run:
            continue
        if segment_point_distance(den, (cx, cy), (0, 0)) < keep_clear:
            continue
        eligible.append(((cx, cy), d))
    eligible.sort(key=lambda e: e[1])

    runs = [e[0] for e in eligible[:run_count]]
    for _ in range(run_count - len(runs)):
        placed = False
        for _ in range(16):
            bearing = rng.random() * 2.0 * math.pi
            length = rng.randint(30, 80)
            for shrink in range(3):
                ln = max(MIN_RUN_CELLS, length >> shrink)
                stop = (den[0] + int(round(ln * math.cos(bearing))),
                        den[1] + int(round(ln * math.sin(bearing))))
                if math.hypot(*stop) > clamp_r:
                    continue
                if segment_point_distance(den, stop, (0, 0)) < keep_clear:
                    continue
                runs.append(stop)
                placed = True
                break
            if placed:
                break
    return runs


# ---- box sweep: fork 1's open question ---------------------------------

def sweep_boxes(seeds):
    """What box size does the CA want, to land near the target BEFORE the size
    clamp corrects it? The clamp always hits the band by construction, so the
    honest measure is how much correction it has to do: a carve topped up from
    80 cells to 250 is a bolted-on growth, not a room."""
    print('=' * 72)
    print('FORK 1 -- box sweep: raw CA yield before the size clamp corrects it')
    print('=' * 72)
    print()
    print('  target bands: occupier %d-%d, excavator reserve %d'
          % (OCCUPIER_MIN_CELLS, OCCUPIER_MAX_CELLS, EXCAVATOR_MAX_CELLS))
    print()
    print('  box   median raw   p10    p90    dead    in 250-400   in 550-600')
    print('  ' + '-' * 66)
    for box in range(20, 41, 2):
        rng = random.Random(90210 + box)
        raw = []
        dead = 0
        for _ in range(seeds // 4):
            cells = run_ca(rng, box)
            if not cells:
                dead += 1
            raw.append(len(cells))
        raw_sorted = sorted(raw)
        n = len(raw_sorted)
        occ = sum(1 for r in raw if OCCUPIER_MIN_CELLS <= r <= OCCUPIER_MAX_CELLS)
        exc = sum(1 for r in raw if 550 <= r <= EXCAVATOR_MAX_CELLS)
        print('  %3d   %8d   %5d  %5d   %4.1f%%   %8.1f%%   %8.1f%%'
              % (box, statistics.median(raw_sorted), raw_sorted[n // 10],
                 raw_sorted[n * 9 // 10], 100.0 * dead / n,
                 100.0 * occ / n, 100.0 * exc / n))
    print()


# ---- the comparator -----------------------------------------------------

def measure_chambers(seeds):
    """The number canon 19 asserted and never measured."""
    rng = random.Random(4242)
    counts = []
    dead = 0
    rejected = 0
    for _ in range(seeds):
        box = rng.randint(MIN_CHAMBER_BOX, MAX_CHAMBER_BOX)
        c = len(run_ca(rng, box))
        if c == 0:
            dead += 1
        elif c < MIN_CHAMBER_CELL_COUNT:
            rejected += 1
        else:
            counts.append(c)
    counts.sort()
    n = len(counts)
    print('=' * 72)
    print('THE COMPARATOR -- what a cave chamber ACTUALLY is')
    print('=' * 72)
    print()
    print('  canon 19 asserts "roughly 100--200 for a cave chamber", and canon 42')
    print('  sizes the cavity and its 600 cap against that figure.')
    print()
    print('  measured, %d rolls of RunChamberCA at the shipped parameters:' % seeds)
    print('    accepted        %6.1f%%   (%.1f%% dead centre, %.1f%% under minChamberCellCount)'
          % (100.0 * n / seeds, 100.0 * dead / seeds, 100.0 * rejected / seeds))
    print('    min %d   p10 %d   MEDIAN %d   p90 %d   MAX %d'
          % (counts[0], counts[n // 10], statistics.median(counts),
             counts[n * 9 // 10], counts[-1]))
    print('    mean %.1f' % statistics.mean(counts))
    print()
    print('  200 is unreachable: box 14 is the largest roll and its interior is 12x12.')
    print('  A 300-cell hole is %.1fx the median chamber and %.1fx the largest that'
          % (300.0 / statistics.median(counts), 300.0 / counts[-1]))
    print('  can exist. The 600 cap is %.1fx and %.1fx.'
          % (600.0 / statistics.median(counts), 600.0 / counts[-1]))
    print()
    print('  Canon 19\'s OTHER rule from the same paragraph survives and is the one')
    print('  to re-anchor on: "keep a span near twice the chamber box size, not five')
    print('  times it". Box is 8-14, so the span budget is 16-28 cells.')
    print()
    return counts


# ---- per-floor run ------------------------------------------------------

def run_floor(fi, radius, seeds, box):
    kind = DEN_KIND[fi]
    if kind == 'Occupier':
        lo, hi = OCCUPIER_MIN_CELLS, OCCUPIER_MAX_CELLS
    else:
        lo, hi = EXCAVATOR_MAX_CELLS - 50, EXCAVATOR_MAX_CELLS

    rng = random.Random(1000 + fi)

    counts, spans, compacts = [], [], []
    added_l, removed_l, raw_l = [], [], []
    no_anchor = 0
    chamber_reject_total = 0
    overlap_chamber = 0
    breach_ok = 0
    breach_fail = 0
    tier1_spans, tier1_counts = [], []
    tier1_holds_origin = 0
    chamber_sizes_same_floor = []
    valid = 0

    for _ in range(seeds):
        centres, _ = place_chambers(rng, radius)

        # Chamber interiors, so the cavity is compared against the chambers on
        # its OWN floor rather than against a global distribution.
        blobs = []
        for c in centres:
            local = chamber_cells(rng)
            if not local:
                continue
            blobs.append((c, set((c[0] + x, c[1] + y) for x, y in local)))
            chamber_sizes_same_floor.append(len(local))

        den, inner, outer, rejects = pick_anchor(
            rng, radius, [b[0] for b in blobs], CHAMBER_SEAT_CLEARANCE)
        chamber_reject_total += rejects
        if den is None:
            no_anchor += 1
            continue
        valid += 1

        cells, raw, added, removed = carve_cavity(rng, box, lo, hi)
        world = set((den[0] + x, den[1] + y) for x, y in cells)

        counts.append(len(world))
        spans.append(span_of(cells))
        compacts.append(compactness(cells))
        raw_l.append(raw)
        added_l.append(added)
        removed_l.append(removed)

        for _, blob in blobs:
            if world & blob:
                overlap_chamber += 1
                break

        runs = plan_runs(rng, den, radius, [b[0] for b in blobs], RUN_COUNT[fi])
        for stop in runs:
            run_cells = dilate_run(den, stop, TUNNEL_WIDTH, TUNNEL_TIP_WIDTH)
            if breaches(world, run_cells):
                breach_ok += 1
            else:
                breach_fail += 1

        if kind == 'Excavator':
            tier1 = grow_from_centre(cells, EXCAVATOR_TIER1_CELLS)
            tier1_counts.append(len(tier1))
            tier1_spans.append(span_of(tier1))
            if (0, 0) in tier1:
                tier1_holds_origin += 1

    print('=' * 72)
    print('FLOOR INDEX %d -- radius %d, %s, %d runs, CA box %d'
          % (fi, radius, kind.upper(), RUN_COUNT[fi], box))
    print('=' * 72)
    print()

    if not counts:
        print('  no valid anchor on any seed -- the clearance is too tight.')
        print()
        return

    counts.sort()
    n = len(counts)
    print('  ANCHOR')
    print('    no anchor in 96 samples      %6.2f%%  (%d of %d seeds)'
          % (100.0 * no_anchor / seeds, no_anchor, seeds))
    print('    samples lost to fork 2       %6.2f per seed  (chamber seat clearance %d)'
          % (chamber_reject_total / float(seeds), CHAMBER_SEAT_CLEARANCE))
    print('    cavity still touches chamber %6.2f%%  (of %d valid seeds)'
          % (100.0 * overlap_chamber / valid, valid))
    print()

    print('  CAVITY SIZE')
    print('    target band                  %d-%d cells' % (lo, hi))
    print('    landed in band               %6.2f%%'
          % (100.0 * sum(1 for c in counts if lo <= c <= hi) / n))
    print('    min %d  median %d  max %d' % (counts[0], statistics.median(counts), counts[-1]))
    print()

    print('  HOW HARD THE CLAMP WORKED  (fork 1: correction is the quality signal)')
    print('    raw CA yield   median %d   p10 %d   p90 %d'
          % (statistics.median(raw_l), sorted(raw_l)[len(raw_l) // 10],
             sorted(raw_l)[len(raw_l) * 9 // 10]))
    print('    cells added by top-up   median %d   p90 %d'
          % (statistics.median(added_l), sorted(added_l)[len(added_l) * 9 // 10]))
    print('    cells cut by trim       median %d   p90 %d'
          % (statistics.median(removed_l), sorted(removed_l)[len(removed_l) * 9 // 10]))
    print()

    spans.sort()
    print('  CANON 19 SPAN RULE  (budget 16-28: twice the chamber box size)')
    print('    span   min %d   median %d   p90 %d   max %d'
          % (spans[0], statistics.median(spans), spans[len(spans) * 9 // 10], spans[-1]))
    print('    within budget                %6.2f%%'
          % (100.0 * sum(1 for s in spans if s <= 28) / len(spans)))
    print('    compactness (1.0 = disc)     median %.2f   p90 %.2f'
          % (statistics.median(compacts), sorted(compacts)[len(compacts) * 9 // 10]))
    print()

    if chamber_sizes_same_floor:
        cs = sorted(chamber_sizes_same_floor)
        print('  AGAINST THIS FLOOR\'S OWN CHAMBERS')
        print('    chamber cells  median %d   max %d   (n=%d)'
              % (statistics.median(cs), cs[-1], len(cs)))
        print('    cavity is %.1fx the median chamber, %.1fx the largest'
              % (statistics.median(counts) / statistics.median(cs),
                 statistics.median(counts) / float(cs[-1])))
        print()

    total_runs = breach_ok + breach_fail
    if total_runs:
        print('  SEATING -- is every run 4-connected to the cavity?')
        print('    runs measured                %d' % total_runs)
        print('    breached                     %d  (%.2f%%)'
              % (breach_ok, 100.0 * breach_ok / total_runs))
        print('    SEVERED                      %d  (%.2f%%)'
              % (breach_fail, 100.0 * breach_fail / total_runs))
        print()

    if tier1_counts:
        print('  FORK 3(c) -- tier 1 carve inside the reserved footprint')
        print('    tier 1 target                %d cells' % EXCAVATOR_TIER1_CELLS)
        print('    tier 1 reached               median %d  min %d'
              % (statistics.median(tier1_counts), min(tier1_counts)))
        print('    tier 1 span                  median %d   p90 %d'
              % (statistics.median(tier1_spans),
                 sorted(tier1_spans)[len(tier1_spans) * 9 // 10]))
        print('    holds the run origin         %6.2f%%'
              % (100.0 * tier1_holds_origin / len(tier1_counts)))
        print('    reserve headroom             %d cells held for tiers 2-5'
              % (int(statistics.median(counts)) - EXCAVATOR_TIER1_CELLS))
        print()


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000

    print()
    print('DEN CAVITY SIM (canon 42) -- %d seeds per floor' % seeds)
    print()

    measure_chambers(min(seeds, 4000))
    sweep_boxes(seeds)

    # Box picked FROM the sweep above rather than guessed: the size whose RAW
    # yield already sits in the target band, so the clamp corrects least. A
    # first pass used 28 and 36 and the sweep rejected both -- box 28 yields a
    # median 567 against an occupier ceiling of 400, so every carve was trimmed
    # by about 170 cells and the trim, which always removes the farthest cell
    # from the centre, was circularising the silhouette the CA had just made.
    for fi, (radius, _has_road) in sorted(DEN_FLOORS.items()):
        box = CA_BOX[DEN_KIND[fi]]
        run_floor(fi, radius, seeds, box)


if __name__ == '__main__':
    main()
