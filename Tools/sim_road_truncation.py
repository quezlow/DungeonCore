#!/usr/bin/env python3
"""
Headless simulation of the floor-4 road truncation.

There is no C# compiler in the container, so this cannot prove the code
compiles. What it CAN prove -- and what the expensive test cycle would otherwise
have to discover on screen -- is the property the whole clip rests on:

    after clipping, does the road's RE-DERIVED carriageway still touch the vault?

That is the open question, because the new polyline keeps only the run's two ends
plus the original waypoints strictly inside it, so the line is RE-DRAWN rather
than copied -- and Bresenham restarted at an interior lattice point does not
reproduce the tail of the original. The claim is that the drift is at most one
cell in the minor axis and that the +1 in the clearance radius absorbs it. This
checks that claim rather than asserting it.

It ports, byte-faithfully to the C#:
  Line              -- RoadNetworkBuilder.Line, the exact both-axes-per-iteration
                       variant, which is where the restart divergence lives
  Centreline        -- including the brokenGapCells tail trim
  Dilate            -- the asymmetric half/extra kernel (width 2 reaches +1 only)
  BuildEdgePolyline -- the meander, so the polylines under test have real shape
  TruncateAroundBlocked -- the clip itself

Floor 4's authored road profile: Network mode, trunkWidth 5, spurWidth 2,
meanderStep 24, meanderAmplitude 6, brokenGapCells 6, clampRadius 600.
The vault is a 75x75 solid block, the size of the three authored plans.

Reported per trial: whether any carriageway cell survives inside the vault
footprint, how compact the resulting polyline is, and how much of the road
survived.
"""

import math
import random
import sys

# ---- floor 4, from RoadNetworkProfile.asset and the plans on disk ----------

CLAMP_RADIUS = 600
TRUNK_WIDTH = 5
SPUR_WIDTH = 2
MEANDER_STEP = 24
MEANDER_AMPLITUDE = 6
BROKEN_GAP = 6
VAULT_SPAN = 75
JUNCTION_FILLET_RADIUS = 3      # TerrainFeatureGenerator.junctionFilletRadius
MIN_RUN_FACTOR = 1


def line(a, b):
    """RoadNetworkBuilder.Line -- Bresenham that may step BOTH axes in one
    iteration. Ported exactly, because the divergence on restart is a property
    of this specific form."""
    x0, y0 = a
    x1, y1 = b
    dx, dy = abs(x1 - x0), abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
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


def centreline(polyline, broken_gap):
    """RoadNetworkBuilder.Centreline, dedupe and tail trim included."""
    out, seen = [], set()
    for i in range(len(polyline) - 1):
        for p in line(polyline[i], polyline[i + 1]):
            if p not in seen:
                seen.add(p)
                out.append(p)
    if len(polyline) == 1 and polyline[0] not in seen:
        out.append(polyline[0])
    gap = max(0, min(broken_gap, max(0, len(out) - 1)))
    if gap:
        del out[len(out) - gap:]
    return out


def dilate(cells, width, centre, clamp):
    """RoadNetworkBuilder.Dilate -- note the ASYMMETRIC kernel: at width 2,
    half is 0 and extra is 1, so the road reaches +1 in x and y only."""
    w = max(1, width)
    half = (w - 1) // 2
    extra = (w - 1) - 2 * half
    clamp_sq = clamp * clamp
    out = set()
    for cx, cy in cells:
        for dx in range(-half, half + extra + 1):
            for dy in range(-half, half + extra + 1):
                px, py = cx + dx, cy + dy
                if (px - centre[0]) ** 2 + (py - centre[1]) ** 2 > clamp_sq:
                    continue
                out.add((px, py))
    return out


def build_edge_polyline(rng, a, b, step, amplitude):
    """RoadNetworkBuilder.BuildEdgePolyline -- the meander, pinned at both ends."""
    pts = [a]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.sqrt(dx * dx + dy * dy)
    if length < 2.0:
        pts.append(b)
        return pts
    steps = max(1, round(length / max(4, step)))
    ux, uy = dx / length, dy / length
    px, py = -uy, ux
    walk = 0.0
    for i in range(1, steps):
        t = i / steps
        walk += (rng.random() - 0.5) * 2.0 * amplitude * 0.6
        walk = max(-amplitude, min(amplitude, walk))
        offset = walk * math.sin(math.pi * t)
        pts.append((round(a[0] + dx * t + px * offset),
                    round(a[1] + dy * t + py * offset)))
    pts.append(b)
    return pts


def near_blocked(cell, blocked, reach):
    for dx in range(-reach, reach + 1):
        for dy in range(-reach, reach + 1):
            if (cell[0] + dx, cell[1] + dy) in blocked:
                return True
    return False


def truncate(polyline, width, broken_gap, blocked, extra_clearance):
    """RoadNetworkBuilder.TruncateAroundBlocked, for one road.

    Returns (new_polyline, new_broken_gap, kind) where kind is one of
    'untouched', 'truncated', 'dropped'."""
    ln = centreline(polyline, broken_gap)
    if not ln:
        return polyline, broken_gap, 'untouched'

    w = max(1, width)
    reach = (w // 2) + 1 + max(0, extra_clearance)
    min_run = max(2, w * MIN_RUN_FACTOR)

    best_start, best_len, run_start = -1, 0, -1
    for i, cell in enumerate(ln):
        if near_blocked(cell, blocked, reach):
            run_start = -1
            continue
        if run_start < 0:
            run_start = i
        length = i - run_start + 1
        if length > best_len:
            best_len, best_start = length, run_start

    if best_len == len(ln):
        return polyline, broken_gap, 'untouched'
    if best_len < min_run:
        return [], 0, 'dropped'

    a, b = best_start, best_start + best_len - 1
    index = {}
    for i, cell in enumerate(ln):
        index.setdefault(cell, i)

    kept = [ln[a]]
    last = a
    for wp in polyline:
        at = index.get(wp)
        if at is None or at <= last or at >= b:
            continue
        kept.append(ln[at])
        last = at
    kept.append(ln[b])
    return kept, 0, 'truncated'


def run_trial(seed):
    """One floor-4-shaped road crossing a 75x75 vault, clipped and re-derived."""
    rng = random.Random(seed)

    # A vault somewhere in the placement band (0.15 to 0.65 of radius 600).
    ang = rng.random() * 2 * math.pi
    rad = rng.uniform(0.15 * 600, 0.65 * 600)
    vx, vy = round(rad * math.cos(ang)), round(rad * math.sin(ang))
    half = VAULT_SPAN // 2
    blocked = {(vx + dx, vy + dy)
               for dx in range(-half, half + 1)
               for dy in range(-half, half + 1)}

    # A road aimed THROUGH it, so the clip is exercised rather than skipped.
    bearing = rng.random() * 2 * math.pi
    reach = rng.uniform(250, 590)
    a = (vx + round(reach * math.cos(bearing)), vy + round(reach * math.sin(bearing)))
    b = (vx - round(reach * math.cos(bearing + rng.uniform(-0.4, 0.4))),
         vy - round(reach * math.sin(bearing + rng.uniform(-0.4, 0.4))))

    width = TRUNK_WIDTH if rng.random() < 0.6 else SPUR_WIDTH
    gap = BROKEN_GAP if rng.random() < 0.5 else 0
    poly = build_edge_polyline(rng, a, b, MEANDER_STEP, MEANDER_AMPLITUDE)

    before_line = centreline(poly, gap)
    if not before_line:
        return None

    new_poly, new_gap, kind = truncate(poly, width, gap, blocked, 0)
    if kind == 'untouched':
        return None                      # the road missed the vault; nothing to check
    if kind == 'dropped':
        return dict(kind=kind, breach=0, waypoints=0,
                    kept=0, orig=len(before_line), poly_before=len(poly))

    after_line = centreline(new_poly, new_gap)
    carriage = dilate(after_line, width, (0, 0), CLAMP_RADIUS)
    breach = len(carriage & blocked)

    # Would widening by the fillet radius have rescued a breach? (The generator
    # re-cuts once at exactly that clearance.)
    rescued = None
    if breach:
        p2, g2, k2 = truncate(poly, width, gap, blocked, JUNCTION_FILLET_RADIUS)
        if k2 == 'dropped':
            rescued = 0
        else:
            rescued = len(dilate(centreline(p2, g2), width,
                                 (0, 0), CLAMP_RADIUS) & blocked)

    return dict(kind=kind, breach=breach, rescued=rescued,
                waypoints=len(new_poly), kept=len(after_line),
                orig=len(before_line), poly_before=len(poly))


def main():
    trials = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
    exercised = breached = dropped = truncated = 0
    worst_breach = 0
    unrescued = 0
    waypoint_max = 0
    waypoint_total = 0
    kept_total = orig_total = 0

    for s in range(trials):
        r = run_trial(s)
        if r is None:
            continue
        exercised += 1
        if r['kind'] == 'dropped':
            dropped += 1
            continue
        truncated += 1
        waypoint_max = max(waypoint_max, r['waypoints'])
        waypoint_total += r['waypoints']
        kept_total += r['kept']
        orig_total += r['orig']
        if r['breach']:
            breached += 1
            worst_breach = max(worst_breach, r['breach'])
            if r['rescued']:
                unrescued += 1

    print("Floor 4 road truncation -- %d seeds, %d exercised the clip"
          % (trials, exercised))
    print()
    print("  truncated                     %d" % truncated)
    print("  dropped (run under road.width) %d" % dropped)
    print()
    print("  CARRIAGEWAY BREACHES INTO THE VAULT")
    print("    at the shipped clearance     %d  (worst %d cell(s))"
          % (breached, worst_breach))
    print("    still breaching after the")
    print("    fillet-widened re-cut        %d" % unrescued)
    print()
    print("  POLYLINE COMPACTNESS")
    print("    worst waypoint count         %d" % waypoint_max)
    print("    mean waypoint count          %.1f"
          % (waypoint_total / max(1, truncated)))
    print("    mean centreline kept         %.0f of %.0f cells"
          % (kept_total / max(1, truncated), orig_total / max(1, truncated)))
    print()
    ok = (unrescued == 0)
    print("VERDICT: " + ("no carriageway survives inside the vault"
                         if ok else
                         "BREACH SURVIVES THE RE-CUT -- the clearance arithmetic "
                         "is wrong"))
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
