#!/usr/bin/env python3
"""
PROTOTYPE for the gate squad's beat, before any C# is written.

WHY THIS EXISTS. Road Breach Report measured the fault (gate rail is the TRUNK
on 0 of 300 seeds; mean rail 19 walk cells against an authored beat window of
+/-60) but it cannot answer what a REPLACEMENT rule would do, because the
replacement does not exist yet. Two things have to be true before the C# is
worth writing:

  1. A graph-distance ball of radius 60, seated by the graded preference
     (Trunk > paints > any), actually reaches and covers a useful stretch of
     trunk.
  2. The squad, turning UNIFORMLY AT RANDOM among in-beat options at each
     junction, actually SPENDS its time on the trunk rather than being absorbed
     bouncing between the short rails clustered at the gatehouse.

(2) is the probabilistic half and is the reason this is a sim rather than
arithmetic. A beat set that geometrically covers 40 per cent of the trunk is
worthless if the walker spends 80 per cent of its steps in a 19-cell lane.

THE GRAPH IS SYNTHESISED FROM THE REPORT'S OWN MEASURED SHAPE, not invented:
  - mean walkable road across all rails ....... 439 cells
  - gate rail length .......................... 19 cells (mean)
  - gate rail kind ............................ LANE 199 / SPUR 101 / TRUNK 0
  - floor index 2 site budget ................. 1-2 ordinary + 3-4 holy + outpost
  - road mode ................................. Trunk (one rim-to-rim chord)
so the trunk is chopped by the site pass into several Trunk-kind rails, with a
Lane threading each laned site and a Spur hanging off each spur-seated one.
Nothing here is a claim about the game; it is a claim about the ALGORITHM.
"""

import random
from collections import deque

# -- the synthesised floor ---------------------------------------------------

TOTAL_WALK = 439      # measured mean, all rails
SHORT_RAIL = 19       # measured mean gate rail length
BEAT_RADIUS = 60      # gateBeatHalfCells, authored

TRUNK, SPUR, LANE = "Trunk", "Spur", "Lane"


class Rail:
    def __init__(self, kind, n, node_a, node_b):
        self.kind = kind
        self.n = n            # walk cell count
        self.a = node_a       # node at index 0
        self.b = node_b       # node at index n-1


def build_floor(n_laned, n_spurred, rng, jitter=True):
    """One rim-to-rim trunk, chopped by the site pass.

    A LANED site replaces a middle stretch: ...trunk | lane | trunk...
    A SPUR site splits the trunk at its take-off and hangs a dead-end spur
    off that node.  Node ids are integers; rim ends get their own nodes and
    are never shared, which is what makes StepOneCell find RoadStopsDead.
    """
    sites = [LANE] * n_laned + [SPUR] * n_spurred
    rng.shuffle(sites)

    short_total = 0
    shorts = []
    for _ in sites:
        n = rng.randint(SHORT_RAIL - 6, SHORT_RAIL + 6) if jitter else SHORT_RAIL
        shorts.append(max(4, n))
        short_total += shorts[-1]

    trunk_total = max(60, TOTAL_WALK - short_total)
    pieces = len(sites) + 1
    # Split the trunk length into `pieces` chunks, none degenerate.
    cuts = sorted(rng.sample(range(20, trunk_total - 20), pieces - 1)) \
        if pieces > 1 else []
    bounds = [0] + cuts + [trunk_total]
    chunk = [bounds[i + 1] - bounds[i] for i in range(pieces)]

    rails = []
    node = 0
    prev_node = node          # rim node at the start of the trunk
    node += 1
    gate_seat = None          # (rail index, cell index) of the outpost anchor

    # Which site is the outpost. Its gate is what the squad is seated at.
    outpost = rng.randrange(len(sites))

    for i, kind in enumerate(sites):
        # trunk chunk running in to this site
        mid_a = node; node += 1
        rails.append(Rail(TRUNK, chunk[i], prev_node, mid_a))
        trunk_in = len(rails) - 1

        if kind == LANE:
            mid_b = node; node += 1
            rails.append(Rail(LANE, shorts[i], mid_a, mid_b))
            lane_idx = len(rails) - 1
            prev_node = mid_b
            if i == outpost:
                gate_seat = (lane_idx, shorts[i] // 2)
        else:
            dead = node; node += 1
            rails.append(Rail(SPUR, shorts[i], mid_a, dead))
            spur_idx = len(rails) - 1
            prev_node = mid_a
            if i == outpost:
                gate_seat = (spur_idx, shorts[i] // 2)

    rails.append(Rail(TRUNK, chunk[-1], prev_node, node))   # final rim chunk
    return rails, gate_seat


# -- the rules under test ----------------------------------------------------

def adjacency(rails):
    adj = {}
    for i, r in enumerate(rails):
        adj.setdefault(r.a, []).append((i, True))
        adj.setdefault(r.b, []).append((i, False))
    return adj


def seat(rails, near_rail, near_index, graded=True):
    """FORK 1. Graded preference: nearest Trunk, else nearest painting rail,
    else nearest rail at all.  'Nearest' here is graph distance from the
    anchor's own nearest walk cell, which stands in for the world-distance
    NearestWalkCell does in the game -- the ORDER is what is being tested,
    not the metric.

    graded=False reproduces the shipped rule (whatever is nearest, full stop).
    """
    if not graded:
        return near_rail, near_index

    dist = ball(rails, near_rail, near_index, radius=10 ** 9, kinds=None)
    for wanted in ((TRUNK,), (TRUNK, SPUR), None):
        best, best_d = None, None
        for (ri, ci), d in dist.items():
            if wanted is not None and rails[ri].kind not in wanted:
                continue
            if best_d is None or d < best_d:
                best, best_d = (ri, ci), d
        if best is not None:
            return best
    return near_rail, near_index


def ball(rails, rail, index, radius, kinds):
    """FORK 2. The BEAT SET: graph-distance BFS from one walk cell, out to
    `radius` steps, over rails whose kind is in `kinds` (None = all).

    Graph distance, NOT Euclidean, and that is the whole point: a Euclidean
    ball counts a cell five world-cells away that is three hundred walk-cells
    around the network, which the squad can never reach inside its beat, and
    the coverage figure would be a lie.

    A rail end steps to every other rail at that node, entering at that rail's
    near end, at cost 1 -- the graph's endpoint clusters sit within
    JunctionMergeRadius of each other and Route() already stitches across them.
    """
    adj = adjacency(rails)
    seen = {(rail, index): 0}
    q = deque([(rail, index)])
    while q:
        ri, ci = q.popleft()
        d = seen[(ri, ci)]
        if d >= radius:
            continue
        nxt = []
        r = rails[ri]
        if ci - 1 >= 0:
            nxt.append((ri, ci - 1))
        if ci + 1 < r.n:
            nxt.append((ri, ci + 1))
        # junction hop
        for end_node, at_end in ((r.a, False), (r.b, True)):
            on_that_end = (ci == (r.n - 1 if at_end else 0))
            if not on_that_end:
                continue
            for (oi, at_start) in adj.get(end_node, ()):
                if oi == ri:
                    continue
                if kinds is not None and rails[oi].kind not in kinds:
                    continue
                nxt.append((oi, 0 if at_start else rails[oi].n - 1))
        for cell in nxt:
            if cell not in seen:
                seen[cell] = d + 1
                q.append(cell)
    return seen


def walk_occupancy(rails, beat, start, steps, rng):
    """StepOneCell under the new rule: step along the rail; on leaving the
    beat set or the rail, try a junction turn filtered to in-beat entries,
    else reverse.  Counts steps by rail kind."""
    adj = adjacency(rails)
    ri, ci = start
    direction = 1
    counts = {TRUNK: 0, SPUR: 0, LANE: 0}
    visited = set()
    for _ in range(steps):
        counts[rails[ri].kind] += 1
        visited.add((ri, ci))
        nxt = ci + direction
        r = rails[ri]
        off_rail = nxt < 0 or nxt >= r.n
        if not off_rail and (ri, nxt) in beat:
            ci = nxt
            continue
        turned = False
        if off_rail:
            node = r.b if direction > 0 else r.a
            opts = []
            for (oi, at_start) in adj.get(node, ()):
                if oi == ri:
                    continue
                entry = 0 if at_start else rails[oi].n - 1
                if (oi, entry) not in beat:
                    continue
                opts.append((oi, entry, 1 if at_start else -1))
            if opts:
                ri, ci, direction = opts[rng.randrange(len(opts))]
                turned = True
        if not turned:
            direction = -direction
    return counts, visited


# -- the sweep ---------------------------------------------------------------

def pct(a, b):
    return 100.0 * a / b if b else 0.0


def run(seeds=2000, lane_transit=True, graded=True, bounded=True):
    rng_master = random.Random(20260814)
    cov, occ_t, occ_l, occ_s = [], [], [], []
    seat_kind = {TRUNK: 0, SPUR: 0, LANE: 0}
    beat_cells, trunk_cells, reach_fail = [], [], 0

    kinds = None if lane_transit else (TRUNK, SPUR)

    for s in range(seeds):
        rng = random.Random(rng_master.randrange(1 << 30))
        # 199:101 lane:spur on the gate rail -> roughly two laned per spurred
        n_laned = rng.randint(2, 4)
        n_spurred = rng.randint(1, 3)
        rails, gate = build_floor(n_laned, n_spurred, rng)

        sr, si = seat(rails, gate[0], gate[1], graded=graded)
        seat_kind[rails[sr].kind] += 1

        if bounded:
            beat = set(ball(rails, sr, si, BEAT_RADIUS, kinds).keys())
        else:
            # SHIPPED RULE: index window on the seat rail only, no crossing.
            lo = max(0, si - BEAT_RADIUS)
            hi = min(rails[sr].n - 1, si + BEAT_RADIUS)
            beat = {(sr, i) for i in range(lo, hi + 1)}

        total_trunk = sum(r.n for r in rails if r.kind == TRUNK)
        in_beat_trunk = sum(1 for (ri, _) in beat if rails[ri].kind == TRUNK)
        if in_beat_trunk == 0:
            reach_fail += 1
        cov.append(pct(in_beat_trunk, total_trunk))
        beat_cells.append(len(beat))
        trunk_cells.append(total_trunk)

        counts, _ = walk_occupancy(rails, beat, (sr, si), 4000, rng)
        tot = sum(counts.values())
        occ_t.append(pct(counts[TRUNK], tot))
        occ_l.append(pct(counts[LANE], tot))
        occ_s.append(pct(counts[SPUR], tot))

    def mean(xs):
        return sum(xs) / len(xs)

    label = ("bounded-set" if bounded else "index-window") \
        + (", graded seat" if graded else ", nearest seat") \
        + (", lane transit" if lane_transit and bounded else
           (", no lane transit" if bounded else ""))
    print("  %-46s" % label
          + " seat T/S/L %3d/%3d/%3d" % (seat_kind[TRUNK], seat_kind[SPUR], seat_kind[LANE])
          + "  beat %5.1f cells" % mean(beat_cells)
          + "  trunk %5.1f" % mean(trunk_cells)
          + "  COVER %5.1f%%" % mean(cov)
          + "  occupancy T %4.1f%% S %4.1f%% L %4.1f%%" % (mean(occ_t), mean(occ_s), mean(occ_l))
          + "  never-reaches-trunk %d" % reach_fail)


def run_split(seeds=2000, lane_transit=True, radius=BEAT_RADIUS):
    """The same measurement, split by whether the OUTPOST itself is laned or
    spur-seated -- 199 to 101 in the report, so both halves are real and the
    lane-transit amendment can only matter to one of them at its own gate."""
    rng_master = random.Random(20260814)
    buckets = {LANE: [[], [], 0], SPUR: [[], [], 0]}   # cover, trunk occ, n
    for _ in range(seeds):
        rng = random.Random(rng_master.randrange(1 << 30))
        rails, gate = build_floor(rng.randint(2, 4), rng.randint(1, 3), rng)
        outpost_kind = rails[gate[0]].kind
        sr, si = seat(rails, gate[0], gate[1], graded=True)
        kinds = None if lane_transit else (TRUNK, SPUR)
        beat = set(ball(rails, sr, si, radius, kinds).keys())
        total_trunk = sum(r.n for r in rails if r.kind == TRUNK)
        in_beat = sum(1 for (ri, _) in beat if rails[ri].kind == TRUNK)
        counts, _ = walk_occupancy(rails, beat, (sr, si), 4000, rng)
        tot = sum(counts.values())
        b = buckets[outpost_kind]
        b[0].append(pct(in_beat, total_trunk))
        b[1].append(pct(counts[TRUNK], tot))
        b[2] += 1
    for kind in (LANE, SPUR):
        c, o, n = buckets[kind]
        if n == 0:
            continue
        print("    outpost %-5s n=%4d  COVER %5.1f%%  trunk occupancy %5.1f%%"
              % (kind, n, sum(c) / n, sum(o) / n))


def sweep_radius(seeds=800):
    """Is 60 the right knob? Printed as a curve rather than retuned blind."""
    for radius in (30, 45, 60, 80, 100, 140):
        rng_master = random.Random(4242)
        cov, cells = [], []
        for _ in range(seeds):
            rng = random.Random(rng_master.randrange(1 << 30))
            rails, gate = build_floor(rng.randint(2, 4), rng.randint(1, 3), rng)
            sr, si = seat(rails, gate[0], gate[1], graded=True)
            beat = set(ball(rails, sr, si, radius, None).keys())
            total_trunk = sum(r.n for r in rails if r.kind == TRUNK)
            cov.append(pct(sum(1 for (ri, _) in beat
                               if rails[ri].kind == TRUNK), total_trunk))
            cells.append(len(beat))
        print("    radius %3d  beat %6.1f cells  COVER %5.1f%% of trunk"
              % (radius, sum(cells) / len(cells), sum(cov) / len(cov)))


if __name__ == "__main__":
    print("BEAT SET PROTOTYPE -- 2000 synthesised floor-index-2 graphs")
    print("  cover = fraction of the floor's TRUNK walk cells inside the beat set")
    print("  occupancy = where 4000 steps of the random-turn walk are actually spent")
    print()
    print("SHIPPED RULE, for the baseline the report already measured:")
    run(bounded=False, graded=False)
    print()
    print("FORK 1 ALONE (graded seat, still an index window on that rail):")
    run(bounded=False, graded=True)
    print()
    print("FORK 2 ALONE (nearest seat, graph-distance beat set):")
    run(bounded=True, graded=False, lane_transit=True)
    print()
    print("BOTH, lane EXCLUDED from the set (the fork as first stated):")
    run(bounded=True, graded=True, lane_transit=False)
    print()
    print("BOTH, lane admitted as TRANSIT (the amendment):")
    run(bounded=True, graded=True, lane_transit=True)
    print()
    print("THE AMENDMENT, split by whether the OUTPOST is laned or spur-seated:")
    print("  lane EXCLUDED from the set:")
    run_split(lane_transit=False)
    print("  lane admitted as TRANSIT:")
    run_split(lane_transit=True)
    print()
    print("IS 60 THE RIGHT KNOB? gateBeatHalfCells swept, lane transit on:")
    sweep_radius()
