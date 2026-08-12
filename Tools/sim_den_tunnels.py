#!/usr/bin/env python3
"""
Headless simulation of the DEN TUNNEL substrate (canon 42), built on
sim_holy_quota's band model (imported, not duplicated).

There is no Unity here, so this cannot prove code compiles or renders. What it
CAN prove, ahead of the expensive test cycle, is whether the AGREED design is
geometrically viable on the two den floors -- and the first thing it measures
is the one that worries me, because canon 42 fixed the den to the 15-65 per
cent band while GenerateChambers places chambers UNIFORMLY across the disc and
says so in a comment. If most chambers land outside the band, a network that
must link chambers and stay in band cannot exist, and that is a fork to settle
before a line of C# is written rather than after a generate-and-look cycle.

What it models, and from where:

  BAND          AncientSiteBuilder.Build via sim_holy_quota.band, 0.15-0.65.
  CHAMBERS      GenerateChambers as authored on FloorTemplatePrefab 1:
                minChambers 3 / maxChambers 6, scaled LINEARLY by
                floorRadius / chamberReferenceRadius (150) and clamped to
                chamberCountCeiling 30; centres drawn from a disc shrunk by
                chamberRimMargin 10 and excluding exclusionRadiusFromCenter 4;
                rejected within chamberSpacing 10 of another centre;
                maxAttempts = desired * 6.
  LANDING       ClaimStarterArea's blob at the stair cell, starterRoomRadius 6,
                plus a keep-clear margin (canon 42: tunnels never touch it).
  TRUNK ROAD    PlanTrunk on floor index 2: two rim points at usable radius
                (radius - rimMargin 7), bearings roughly opposed within
                trunkBearingSpread 30 degrees, retried up to 24 times until the
                chord clears coreExclusion + trunkWidth of the centre.
                Carriageway half-width 2 (trunkWidth 5).

  NOT modelled, deliberately: the CA interior of a chamber (the sim links
  centres, and a tunnel that reaches a centre reaches the cave), rivers (they
  are carved after tunnels and simply win, per canon 42's precedence), and the
  wobble of BuildTunnel's centreline (it drifts by at most a cell per step and
  cannot move an endpoint).

Usage:  python3 sim_den_tunnels.py [seeds]
"""

import math
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sim_holy_quota import CORE_EXCLUSION, band

# ---- authored constants, each read off a shipped asset ------------------

MIN_CHAMBERS = 3             # FloorTemplatePrefab 1
MAX_CHAMBERS = 6
CHAMBER_REFERENCE_RADIUS = 150
CHAMBER_COUNT_CEILING = 30
CHAMBER_SPACING = 10
CHAMBER_RIM_MARGIN = 10
EXCLUSION_FROM_CENTRE = 4
STARTER_ROOM_RADIUS = 6      # ClaimStarterArea

BAND_INNER = 0.15            # canon 19 / 42
BAND_OUTER = 0.65

LANDING_KEEP_CLEAR = 10      # starter blob (6) plus slack; a fork below
CLAMP_FRACTION = 0.85        # outer clamp on a tunnel endpoint, x radius
MAX_RUN_FRACTION = 0.90      # longest run the den will drive, x radius
MIN_RUN_CELLS = 12           # nearer than this and the chamber IS the den
TUNNEL_WIDTH = 3             # carriageway-style dilation, mouth
TUNNEL_TIP_WIDTH = 2         # ...tapering to this at the tip

TRUNK_RIM_MARGIN = 7         # RoadNetworkProfile floorIndex 2
TRUNK_WIDTH = 5
TRUNK_BEARING_SPREAD = 30

# floorIndex: (radius, has_trunk_road)
DEN_FLOORS = {1: (150, False), 2: (250, True)}


# ---- chamber placement, mirroring GenerateChambers ----------------------

def place_chambers(rng, radius):
    """GenerateChambers' loop: uniform in a shrunk disc, spacing-rejected."""
    scale = max(1.0, radius / float(CHAMBER_REFERENCE_RADIUS))
    rolled = rng.randint(MIN_CHAMBERS, MAX_CHAMBERS)
    desired = min(max(int(round(rolled * scale)), 1), CHAMBER_COUNT_CEILING)

    disc = max(EXCLUSION_FROM_CENTRE + 1, radius - CHAMBER_RIM_MARGIN)
    centres = []
    attempts = 0
    while len(centres) < desired and attempts < desired * 6:
        attempts += 1
        # PickRandomCellInDisc: rejection-sample the annulus.
        dx = rng.randint(-disc, disc)
        dy = rng.randint(-disc, disc)
        d2 = dx * dx + dy * dy
        if d2 > disc * disc or d2 < EXCLUSION_FROM_CENTRE * EXCLUSION_FROM_CENTRE:
            continue
        if any((dx - x) ** 2 + (dy - y) ** 2 < CHAMBER_SPACING ** 2
               for x, y in centres):
            continue
        centres.append((dx, dy))
    return centres, desired


# ---- the trunk road, mirroring PlanTrunk -------------------------------

def plan_trunk(rng, radius):
    """Returns (a, b) rim endpoints of the trunk chord, or None."""
    usable = max(CORE_EXCLUSION + 4, radius - TRUNK_RIM_MARGIN)
    spread = math.radians(TRUNK_BEARING_SPREAD)
    clearance = EXCLUSION_FROM_CENTRE + TRUNK_WIDTH
    for _ in range(24):
        start = rng.random() * 2.0 * math.pi
        end = start + math.pi + (rng.random() - 0.5) * 2.0 * spread
        a = (round(usable * math.cos(start)), round(usable * math.sin(start)))
        b = (round(usable * math.cos(end)), round(usable * math.sin(end)))
        if perp_distance(a, b, (0, 0)) >= clearance:
            return a, b
    return None


def perp_distance(a, b, p):
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    return abs(dy * (px - ax) - dx * (py - ay)) / math.hypot(dx, dy)


def segment_point_distance(a, b, p):
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    t = ((px - ax) * dx + (py - ay) * dy) / float(dx * dx + dy * dy)
    t = max(0.0, min(1.0, t))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


# ---- the measurements ---------------------------------------------------

def in_band(p, inner, outer):
    d2 = p[0] * p[0] + p[1] * p[1]
    return inner * inner <= d2 <= outer * outer


def segment_clears(a, b, p, keep):
    return segment_point_distance(a, b, p) >= keep


def run_floor(fi, radius, has_road, seeds):
    inner, outer = band(radius, BAND_INNER, BAND_OUTER, CHAMBER_RIM_MARGIN)

    total_chambers = []
    band_chambers = []
    starved = 0            # fewer than 2 in-band chambers: no network possible
    thin = 0               # exactly 2: a single link, no network
    landing_conflicts = 0  # an in-band chamber sitting on the stair landing
    road_reachable = 0
    road_gap = []
    span = []

    for s in range(seeds):
        rng = random.Random(s * 7919 + fi)
        centres, _desired = place_chambers(rng, radius)
        total_chambers.append(len(centres))

        # The stair landing: the player places it, but generation cannot know
        # where, so the pessimistic model is that it lands anywhere the player
        # can reach -- sampled uniformly in the inner band, where a descending
        # player actually is.
        for _ in range(64):
            lx = rng.randint(-inner, inner)
            ly = rng.randint(-inner, inner)
            if lx * lx + ly * ly <= inner * inner:
                landing = (lx, ly)
                break
        else:
            landing = (0, 0)

        band_set = [c for c in centres if in_band(c, inner, outer)]
        band_chambers.append(len(band_set))

        if len(band_set) < 2:
            starved += 1
        elif len(band_set) == 2:
            thin += 1

        for c in band_set:
            if math.hypot(c[0] - landing[0], c[1] - landing[1]) < \
                    STARTER_ROOM_RADIUS + LANDING_KEEP_CLEAR:
                landing_conflicts += 1
                break

        if len(band_set) >= 2:
            span.append(max(math.hypot(p[0] - q[0], p[1] - q[1])
                            for p in band_set for q in band_set))

        if has_road:
            chord = plan_trunk(rng, radius)
            if chord is not None and band_set:
                a, b = chord
                nearest = min(segment_point_distance(a, b, c) for c in band_set)
                road_gap.append(nearest)
                # A spur reaching the carriageway from the nearest in-band
                # chamber: cheap if the gap is short, absurd if it crosses the
                # floor. 60 cells is the yardstick -- roughly the spurMinLength
                # the road builder itself authors (30) doubled.
                if nearest <= 60:
                    road_reachable += 1

    return dict(
        floor=fi, radius=radius, band=(inner, outer),
        chambers_mean=statistics.mean(total_chambers),
        band_mean=statistics.mean(band_chambers),
        band_min=min(band_chambers),
        starved_pct=100.0 * starved / seeds,
        thin_pct=100.0 * thin / seeds,
        landing_pct=100.0 * landing_conflicts / seeds,
        span_mean=statistics.mean(span) if span else 0.0,
        road_gap_mean=statistics.mean(road_gap) if road_gap else None,
        road_gap_worst=max(road_gap) if road_gap else None,
        road_pct=100.0 * road_reachable / seeds if has_road else None,
    )


def measure_remedies(fi, radius, seeds):
    """Two candidate fixes for the band/chamber disagreement, measured rather
    than argued.

    A -- RELAX ENDPOINTS. The den anchor stays in band; tunnels may reach any
         chamber. Cheap to state, but reveal is influence-touch only and canon
         19 measures a plausible late run as reaching roughly the inner 65 per
         cent, so any stretch beyond the band is a stretch nobody unfogs. The
         number that matters is what FRACTION of the network sits out there.

    D -- SELF-CONTAINED NETWORK. Tunnels are their own geometry inside the
         band -- den plus a few nodes, the road builder's own shape -- and
         chambers are joined opportunistically wherever one happens to fall
         near a run. Robust to chamber placement by construction; the number
         that matters is how many chambers it still touches.
    """
    inner, outer = band(radius, BAND_INNER, BAND_OUTER, CHAMBER_RIM_MARGIN)

    a_out_frac = []
    a_len = []
    a_reach = []
    ac_endpoints = []
    e_links = []
    e_deadends = []
    e_len = []
    e_cells = []
    d_touched = []
    d_nodes = 3 if radius <= 150 else 4

    for s in range(seeds):
        rng = random.Random(s * 7919 + fi + 1013904223)
        centres, _ = place_chambers(rng, radius)
        if not centres:
            continue

        # Den anchor: uniform in band, as canon 42 fixes it.
        den = None
        for _ in range(96):
            dx = rng.randint(-outer, outer)
            dy = rng.randint(-outer, outer)
            if in_band((dx, dy), inner, outer):
                den = (dx, dy)
                break
        if den is None:
            continue

        # --- Remedy A: link the three nearest chambers, band or not.
        nearest = sorted(centres, key=lambda c: math.hypot(c[0] - den[0],
                                                           c[1] - den[1]))[:3]
        out_cells = total_cells = 0
        for c in nearest:
            steps = int(math.hypot(c[0] - den[0], c[1] - den[1]))
            a_len.append(steps)
            a_reach.append(math.hypot(c[0], c[1]) / float(radius))
            for i in range(max(1, steps)):
                t = i / float(max(1, steps))
                p = (den[0] + (c[0] - den[0]) * t, den[1] + (c[1] - den[1]) * t)
                total_cells += 1
                if not in_band(p, inner, outer):
                    out_cells += 1
        a_out_frac.append(100.0 * out_cells / max(1, total_cells))

        # --- Remedy A-clamped: the same, but a chamber past CLAMP_FRACTION of
        # the radius is not worth a tunnel -- it is inside the bedrock rim's
        # approach and past anything the player reaches. Measures what the
        # clamp COSTS: how often it leaves fewer than two endpoints.
        keep = [c for c in nearest
                if math.hypot(c[0], c[1]) <= radius * CLAMP_FRACTION]
        ac_endpoints.append(len(keep))

        # --- Remedy E: the shape that CANNOT starve. The den throws a fixed
        # number of runs on spread bearings. A run that finds a chamber inside
        # the clamp ends there; a run that finds none ends in the rock as a
        # DEAD END, which is what an unfinished dig looks like and is content
        # rather than failure. Chamber count therefore changes the FLAVOUR of a
        # network, never whether one exists.
        runs_wanted = 3 if radius <= 150 else 4
        # Eligible chambers FIRST, nearest out, then dead ends fill the rest.
        # Choosing by bearing was tried and dropped: it discarded a perfectly
        # good chamber for sitting off the run's assigned heading, which cost
        # floor index 1 a chamber link on a quarter of seeds for nothing.
        eligible = [c for c in centres
                    if math.hypot(c[0], c[1]) <= radius * CLAMP_FRACTION
                    and MIN_RUN_CELLS <= math.hypot(c[0] - den[0], c[1] - den[1])
                            <= radius * MAX_RUN_FRACTION]
        eligible.sort(key=lambda c: math.hypot(c[0] - den[0], c[1] - den[1]))
        chosen = eligible[:runs_wanted]
        links = len(chosen)
        deadends = runs_wanted - links
        for c in chosen:
            e_len.append(math.hypot(c[0] - den[0], c[1] - den[1]))
        for _ in range(deadends):
            e_len.append(min(radius * 0.30, 30 + rng.random() * 50))
        e_cells.append(sum(
            int(l * (TUNNEL_WIDTH + TUNNEL_TIP_WIDTH) / 2.0)
            for l in e_len[-runs_wanted:]))
        e_links.append(links)
        e_deadends.append(deadends)

        # --- Remedy D: nodes scattered in band, runs between them.
        nodes = [den]
        for _ in range(d_nodes):
            for _try in range(96):
                dx = rng.randint(-outer, outer)
                dy = rng.randint(-outer, outer)
                if in_band((dx, dy), inner, outer) and \
                        all(math.hypot(dx - n[0], dy - n[1]) > outer * 0.35
                            for n in nodes):
                    nodes.append((dx, dy))
                    break
        runs = [(nodes[i], nodes[i + 1]) for i in range(len(nodes) - 1)]
        # A chamber is JOINED when a run passes within a short spur of its
        # centre. 20 cells is a spur the length of a chamber's own box.
        touched = sum(1 for c in centres
                      if any(segment_point_distance(a, b, c) <= 20
                             for a, b in runs))
        d_touched.append(touched)

    return dict(
        a_out_frac=statistics.mean(a_out_frac) if a_out_frac else 0.0,
        a_len=statistics.mean(a_len) if a_len else 0.0,
        a_reach_mean=statistics.mean(a_reach) if a_reach else 0.0,
        a_reach_worst=max(a_reach) if a_reach else 0.0,
        ac_thin_pct=100.0 * sum(1 for n in ac_endpoints if n < 2)
                    / max(1, len(ac_endpoints)),
        d_touched=statistics.mean(d_touched) if d_touched else 0.0,
        d_none_pct=100.0 * sum(1 for t in d_touched if t == 0)
                   / max(1, len(d_touched)),
        e_links=statistics.mean(e_links) if e_links else 0.0,
        e_deadends=statistics.mean(e_deadends) if e_deadends else 0.0,
        e_len=statistics.mean(e_len) if e_len else 0.0,
        e_cells=statistics.mean(e_cells) if e_cells else 0.0,
        e_cells_worst=max(e_cells) if e_cells else 0,
        e_nolink_pct=100.0 * sum(1 for n in e_links if n == 0)
                     / max(1, len(e_links)),
    )


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    print("Den tunnel substrate -- %d seeds per floor" % seeds)
    print("band %.2f-%.2f of radius; chambers uniform across the disc"
          % (BAND_INNER, BAND_OUTER))
    print()
    print("%-6s %-7s %-12s %-9s %-9s %-6s %-9s %-8s %-8s"
          % ("floor", "radius", "band", "chambers", "in-band", "worst",
             "starved%", "thin%", "landing%"))

    results = []
    for fi in sorted(DEN_FLOORS):
        radius, has_road = DEN_FLOORS[fi]
        r = run_floor(fi, radius, has_road, seeds)
        results.append(r)
        print("%-6d %-7d %-12s %-9.2f %-9.2f %-6d %-9.1f %-8.1f %-8.1f"
              % (fi, r['radius'], "%d-%d" % r['band'], r['chambers_mean'],
                 r['band_mean'], r['band_min'], r['starved_pct'],
                 r['thin_pct'], r['landing_pct']))

    print()
    for r in results:
        if r['road_gap_mean'] is not None:
            print("floor %d trunk road: nearest in-band chamber is %.1f cells "
                  "from the carriageway on average (worst %.1f); a spur of 60 "
                  "cells or less reaches it on %.1f%% of seeds."
                  % (r['floor'], r['road_gap_mean'], r['road_gap_worst'],
                     r['road_pct']))
        print("floor %d widest in-band chamber pair: %.1f cells apart on average."
              % (r['floor'], r['span_mean']))

    print()
    print("Candidate remedies, measured:")
    for r in results:
        fi = r['floor']
        radius = DEN_FLOORS[fi][0]
        m = measure_remedies(fi, radius, seeds)
        print("  floor %d  A (link nearest chambers, band or not): "
              "%.1f%% of tunnel length falls OUTSIDE the band, "
              "mean run %.0f cells; endpoints reach %.2f of radius on "
              "average, worst %.2f"
              % (fi, m['a_out_frac'], m['a_len'], m['a_reach_mean'],
                 m['a_reach_worst']))
        print("  floor %d  A clamped at %.2f of radius:                "
              "leaves fewer than two endpoints on %.1f%% of seeds"
              % (fi, CLAMP_FRACTION, m['ac_thin_pct']))
        print("  floor %d  E (fixed runs; chamber or dead end):    "
              "%.2f chamber links + %.2f dead ends per den, mean run %.0f "
              "cells; NO chamber link at all on %.1f%% of seeds"
              % (fi, m['e_links'], m['e_deadends'], m['e_len'],
                 m['e_nolink_pct']))
        print("           carves %.0f cells per den on average (worst %d) -- "
              "compare a cave chamber at 100-200 and the disc at %d"
              % (m['e_cells'], m['e_cells_worst'],
                 int(math.pi * radius * radius)))
        print("  floor %d  D (self-contained in-band network):     "
              "touches %.2f chambers on average; touches NONE on %.1f%% of seeds"
              % (fi, m['d_touched'], m['d_none_pct']))

    print()
    fail = False
    for r in results:
        fi = r['floor']
        m = measure_remedies(fi, DEN_FLOORS[fi][0], seeds)
        if m['e_nolink_pct'] > 5.0:
            print("!! floor %d: shape E leaves the den with NO chamber link on "
                  "%.1f%% of seeds." % (fi, m['e_nolink_pct']))
            fail = True
        if m['e_cells'] > 2500:
            print("!! floor %d: shape E carves %.0f cells per den -- past the "
                  "site scale entry 19 warned about." % (fi, m['e_cells']))
            fail = True
    print()
    print("FINDING: the AGREED fork 7 (link chambers, stay in band) is NOT")
    print("viable -- chambers are placed uniformly across the disc, so floor")
    print("index 1 has fewer than two in-band chambers on 30.8% of seeds.")
    print()
    print("VERDICT: " + ("shape E does not hold at the tuned values"
                         if fail else
                         "shape E (fixed runs; chamber where one is in range, "
                         "dead end where none is) holds on both floors"))
    return 1 if fail else 0


if __name__ == '__main__':
    sys.exit(main())
