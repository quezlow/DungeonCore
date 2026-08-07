#!/usr/bin/env python3
"""
Headless simulation of lane routing: a road that meets a laned site enters at a
door, threads the authored lane, and leaves by the most-opposite door.

There is no C# compiler in the container, so this cannot prove the code
compiles. What it CAN prove -- and what the expensive test cycle would otherwise
have to discover with a caravan walking through a wall -- is the property the
whole feature rests on:

    after the polyline is rewritten, does the RE-DERIVED centreline stay off
    the site's masonry, on every bearing and in every orientation the plan is
    allowed to take?

That is the open question rather than a formality, for two reasons.

First, walkers ARE the centreline. DwarvenCaravanController and
DwarvenPatrolController step cell by cell along DeepRoadGraph.Rail.walk, which
is Centreline(road), and nothing anywhere tests whether a walk cell is a road
cell. Once a site stops yielding its footprint, a centreline that clips masonry
is a dwarf inside a wall.

Second, the line is RE-DRAWN rather than copied. Bresenham restarted at an
interior lattice point does not reproduce the tail of the original -- the
truncation sim measured 45,858 of 51,081 random restarts diverging -- so the
approach segments either side of a site are new geometry, not kept geometry.

It ports, faithfully to the C#:
  line              -- RoadNetworkBuilder.Line, the both-axes-per-iteration
                       variant, which is where the restart divergence lives
  centreline        -- including the dedupe (first occurrence wins) and the
                       brokenGapCells tail trim
  rot_local         -- AncientSiteBuilder.RotateLocal
  parse_plan        -- AncientSitePlanLibrary.Parse, glyph for glyph
  build_door_runs   -- AncientSitePlanLibrary.BuildDoorRuns, including the
                       outward-normal rule
  walkable          -- the drape: a floor cell is walkable only when y+1 AND
                       y+2 are also floor

Run it from anywhere:  python3 Tools/sim_lane_routing.py [repo-root]

Reported per plan: how many placements routed, how many fell back and why, the
worst waypoint count added to the save, and -- the number that matters -- how
many re-drawn centreline cells landed on masonry. That last figure must be
zero. Anything else is a walker in a wall.
"""

import os
import sys

# ---- from the shipped profiles and constants ------------------------------

TRUNK_WIDTH = 5                 # RoadNetworkProfile, floors 2-4
ROAD_HEADING_RADIUS = 6         # AncientSiteBuilder.RoadHeadingRadius
DOOR_FACING_COS = 0.8660        # AncientSiteBuilder.DoorFacingCos, 30 degrees

# R3: how far off the door the road may cross before routing gives up. The
# road's own width. Measured: every laned plan puts its door mids within two
# cells of the plan's centre axis except CollapsedArchive_TheBurnedWing at
# eight, and the AlongRoad anchor lands the plan centre on a carriageway cell,
# so the door is already on the road. Five covers anchor jitter (the anchor is
# sampled from the DILATED carriageway, so it sits up to two off the centreline)
# plus the door's own offset.
DOOR_TOLERANCE = TRUNK_WIDTH

# Mirrors TruncateAroundBlocked. The +1 is load-bearing rather than padding:
# it absorbs the one-cell Chebyshev divergence a Bresenham restart can produce.
def clearance(width):
    return (width // 2) + 1


# ---- ports ----------------------------------------------------------------

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


def centreline(polyline, broken_gap=0):
    """RoadNetworkBuilder.Centreline. The dedupe keeps the FIRST occurrence, so
    a meander that revisits a cell reports an index that walks backwards --
    which is why the clip in the C# tests strictly-forward as well as
    strictly-inside."""
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
        out = out[:-gap]
    return out


def rot_local(p, rot, mirror):
    """AncientSiteBuilder.RotateLocal."""
    x = -p[0] if mirror else p[0]
    y = p[1]
    r = rot & 3
    if r == 1:
        return (-y, x)
    if r == 2:
        return (-x, -y)
    if r == 3:
        return (y, -x)
    return (x, y)


def walkable(floorset):
    """The drape. A wall's face renders two cells tall and the pathfinder
    treats what it covers as blocked, so a floor cell is walkable only when
    y+1 AND y+2 are also floor. Always in world +Y, which is why every check
    here is run per orientation rather than once."""
    return set(c for c in floorset
               if (c[0], c[1] + 1) in floorset and (c[0], c[1] + 2) in floorset)


# ---- plan parsing ---------------------------------------------------------

def parse_plan(path):
    """AncientSitePlanLibrary.Parse, glyph for glyph. Returns None on a plan
    with no carved cells, exactly as the C# does."""
    text = open(path, "r", encoding="utf-8", errors="replace").read()
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    hdr, rows = {}, []
    for raw in lines:
        t = raw.strip()
        if t.startswith("//"):
            continue
        if t.startswith("@") and ":" in t:
            hdr[t[1:t.index(":")].strip().lower()] = t[t.index(":") + 1:].strip()
            continue
        if not t and not rows:
            continue
        rows.append(raw)
    while rows and not rows[-1].strip():
        rows.pop()

    floor, wall, heart, door, lane = [], [], [], [], []
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            p = (c, -r)                       # top of the file is NORTH
            if ch == "#":
                wall.append(p)
            elif ch in ".=^":
                floor.append(p)
            elif ch == "+":
                floor.append(p)
                door.append(p)
            elif ch == "~":
                floor.append(p)
                lane.append(p)
            elif ch == "X":
                wall.append(p)
                heart.append(p)
    if not floor:
        return None

    xs = [p[0] for p in floor + wall]
    ys = [p[1] for p in floor + wall]
    off = ((min(xs) + max(xs)) // 2, (min(ys) + max(ys)) // 2)

    def shift(s):
        return [(p[0] - off[0], p[1] - off[1]) for p in s]

    wallset = set(shift(wall))
    rot = hdr.get("rotate", "").lower() not in ("no", "false", "0")
    return {
        "hdr": hdr,
        "name": os.path.basename(path),
        "wall": wallset,
        "floor": [q for q in shift(floor) if q not in wallset],
        "door": shift(door),
        "lane": shift(lane),
        "heart": shift(heart),
        "rotatable": rot,
    }


def build_door_runs(plan):
    """AncientSitePlanLibrary.BuildDoorRuns. `outward` is the perpendicular
    whose neighbour is in NEITHER floor nor wall; if both sides are outside or
    neither is, the run gets a zero normal and is unusable."""
    doors = set(plan["door"])
    inside = set(plan["wall"]) | set(plan["floor"])
    runs, counted = [], set()
    for cell in plan["door"]:
        if cell in counted:
            continue
        horiz = ((cell[0] + 1, cell[1]) in doors) or ((cell[0] - 1, cell[1]) in doors)
        step = (1, 0) if horiz else (0, 1)
        perp = (0, 1) if horiz else (1, 0)
        start = cell
        while (start[0] - step[0], start[1] - step[1]) in doors:
            start = (start[0] - step[0], start[1] - step[1])
        n, c = 0, start
        while c in doors:
            counted.add(c)
            n += 1
            c = (c[0] + step[0], c[1] + step[1])
        mid = (start[0] + step[0] * (n // 2), start[1] + step[1] * (n // 2))
        plus = (mid[0] + perp[0], mid[1] + perp[1])
        minus = (mid[0] - perp[0], mid[1] - perp[1])
        po = plus not in inside and plus not in doors
        mo = minus not in inside and minus not in doors
        out = (0, 0)
        if po and not mo:
            out = perp
        elif mo and not po:
            out = (-perp[0], -perp[1])
        runs.append({"mid": mid, "out": out, "len": n})
    return runs


def touches_lane(run, laneset):
    """SitePlanValidator.TouchesLane: the run's middle on the lane, or within
    three cells inward of it."""
    if run["mid"] in laneset:
        return True
    inward = (-run["out"][0], -run["out"][1])
    if inward == (0, 0):
        return False
    for k in (1, 2, 3):
        if (run["mid"][0] + inward[0] * k, run["mid"][1] + inward[1] * k) in laneset:
            return True
    return False


def run_cells(run):
    step = (0, 1) if run["out"][0] != 0 else (1, 0)
    half = run["len"] // 2
    return [(run["mid"][0] + step[0] * k, run["mid"][1] + step[1] * k)
            for k in range(-half, half + 1)]


# ---- placement, door-anchored --------------------------------------------

# The bend budget for the LATERAL MISS only, which is the part that can be
# bought down by lengthening the approach. It is not the whole corner: a road
# leaves a gate along the wall's normal and its own heading is up to a
# cone-width away, so the corner at the gate mouth is at least DOOR_FACING_COS
# wide no matter what is spent here. Measured worst corner at a 30-degree cone:
# 36.7. At a 15-degree cone: 28.1, but acceptance falls from 12 bearings in 16
# to 8.
MAX_BEND_DEG = 20.0

# Never shorter than two meander steps, so a road that misses by nothing still
# straightens out before the gate rather than arriving at a kink. Measured:
# 24 gives a worst corner of 39.4 degrees, 48 gives 36.7, and past that the
# curve flattens out because what is left is the placement cone.
MIN_APPROACH = 48



def place(plan, runs, road_cell, anchor_run, rot, mirror):
    """AncientSiteBuilder's EmitTransformed plus the door-anchor shift.

    `PlaceDeadCore` does not put the plan's centre on the road cell when a plan
    asks for door anchoring -- it shifts the whole plan by -doorMid so the DOOR
    lands there. The road never moves; the building does. Reproduced exactly,
    because that shift is what puts the entry gate on the carriageway and makes
    the whole bend question a question about the EXIT alone."""
    shift = rot_local(anchor_run["mid"], rot, mirror)
    at = (road_cell[0] - shift[0], road_cell[1] - shift[1])

    def w(p):
        r = rot_local(p, rot, mirror)
        return (at[0] + r[0], at[1] + r[1])

    laneset = set(plan["lane"])
    doors = []
    for run in runs:
        if run["out"] == (0, 0):
            continue
        doors.append({
            "mid": w(run["mid"]),
            "out": rot_local(run["out"], rot, mirror),
            "len": run["len"],
            "cells": [w(c) for c in run_cells(run)],
            "onlane": touches_lane(run, laneset),
            "local": run,
        })
    site = {
        "name": plan["name"],
        "floor": set(w(p) for p in plan["floor"]),
        "wall": set(w(p) for p in plan["wall"]),
        "lane": set(w(p) for p in plan["lane"]),
        "doors": doors,
    }
    site["footprint"] = site["floor"] | site["wall"]
    site["walk"] = walkable(site["floor"])
    return site


def anchor_candidates(plan, runs, heading):
    """Which door runs may be anchored to a road on this heading, and at which
    rotations.

    FromAuthored currently keeps only the FIRST run with a usable normal, and
    its comment says picking the best would need a road heading the function
    does not have. That is right for a vault, which has one door. A laned plan
    has two or four, and anchoring whichever one happened to be scanned first
    means a crossroads village can only ever be entered from the north. So the
    runs are kept as a LIST here and the heading picks among them at placement,
    which is the change the C# needs.

    Undirected, as PlaceDeadCore is: a road has no forward, so a gate facing
    east is served equally by a road heading east or west."""
    laneset = set(plan["lane"])
    local = [r for r in runs if r["out"] != (0, 0) and touches_lane(r, laneset)]
    n = (heading[0] ** 2 + heading[1] ** 2) ** 0.5
    rots = range(4) if plan["rotatable"] else [0]
    mirs = (False, True) if plan["rotatable"] else (False,)
    out = []
    for rot in rots:
        for mirror in mirs:
            for r in local:
                o = rot_local(r["out"], rot, mirror)
                dot = abs(o[0] * heading[0] + o[1] * heading[1]) / n
                if dot >= DOOR_FACING_COS:
                    out.append((r, rot, mirror))
    return out


# ---- the router -----------------------------------------------------------

def bfs(corridor, starts, targets):
    from collections import deque
    prev, q = {}, deque()
    for s in starts:
        if s in corridor and s not in prev:
            prev[s] = None
            q.append(s)
    while q:
        c = q.popleft()
        if c in targets:
            path = []
            while c is not None:
                path.append(c)
                c = prev[c]
            return path[::-1]
        for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nxt = (c[0] + d[0], c[1] + d[1])
            if nxt in corridor and nxt not in prev:
                prev[nxt] = c
                q.append(nxt)
    return None


def corners(path):
    """Waypoints at each direction change and nowhere else. One per cell would
    bloat every save; a 4-neighbour path's corners give axis-aligned segments,
    which Bresenham reproduces exactly."""
    if len(path) < 2:
        return list(path)
    wp = [path[0]]
    for i in range(1, len(path) - 1):
        d0 = (path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1])
        d1 = (path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1])
        if d0 != d1:
            wp.append(path[i])
    wp.append(path[-1])
    return wp


def thresholds(d):
    return [(c[0] - d["out"][0] * k, c[1] - d["out"][1] * k)
            for c in d["cells"] for k in range(1, 5)]


def gate_entry(d, walkset):
    """Where the lane route should start, given the gate the road came in by.

    Straight in from the gate's MIDDLE, taking the first walkable cell along
    the inward normal. Any threshold cell would do for the validator, which
    only asks whether a route exists -- but routing has to lay down a polyline,
    and a start offset sideways along the run makes the segment from the gate
    to the first waypoint DIAGONAL. Measured: that one diagonal stitch put a
    26.6-degree corner immediately inside every gate, and it survived every
    attempt to spend the approach budget on it because it was never in the
    approach. Coming in square costs nothing and removes it.

    Returns nothing at all when the middle of the gate is buried, and that
    REFUSAL is the point. The first cut fell back to the full threshold set,
    which is what the validator uses -- and a threshold cell offset sideways
    along the run puts the stitch back on a diagonal. Measured on the four-gate
    GallowsCourt roundabout: the fallback fired, the stitch went diagonal, and
    the re-drawn line clipped the gallows platform. A route that cannot be
    entered square is a route this router will not lay, and the site falls back
    to subtraction where the author can see it in the report."""
    for k in range(1, 5):
        c = (d["mid"][0] - d["out"][0] * k, d["mid"][1] - d["out"][1] * k)
        if c in walkset:
            return [c]
    return []


def turn_angles(poly):
    """Every direction change in the rewritten polyline, in degrees. The
    sharpest one is what a player sees."""
    import math
    out = []
    for i in range(1, len(poly) - 1):
        ax, ay = poly[i][0] - poly[i - 1][0], poly[i][1] - poly[i - 1][1]
        bx, by = poly[i + 1][0] - poly[i][0], poly[i + 1][1] - poly[i][1]
        na = (ax * ax + ay * ay) ** 0.5
        nb = (bx * bx + by * by) ** 0.5
        if na == 0 or nb == 0:
            continue
        c = max(-1.0, min(1.0, (ax * bx + ay * by) / (na * nb)))
        out.append(math.degrees(math.acos(c)))
    return out


def route(polyline, width, site, entry):
    """Rewrites one road's polyline to enter a gate, thread the lane, leave by
    the most-opposite gate, and bend back onto its own line beyond.

    Returns (new_polyline, note, stats). `note` is None on success and names
    the reason on a fallback -- every exit says WHY, because "the road did not
    route" answered four different ways is how the placement report read before
    its rejection counters existed."""
    import math
    cl = centreline(polyline)
    fp = site["footprint"]
    inside = [i for i, c in enumerate(cl) if c in fp]
    if not inside:
        return polyline, "no crossing", {}
    i_in, i_out = inside[0], inside[-1]

    # WHICH GATE THE ROAD MEETS FIRST.
    #
    # Placement is UNDIRECTED and has to be: a road is travelled both ways, so
    # a gate facing east is served equally by a road heading east or west, and
    # PlaceDeadCore's own heading test uses Abs(Dot) for exactly that reason.
    # ROUTING is directed, because the polyline runs from one end to the other
    # and the rewrite has to be laid down in that order.
    #
    # Conflating the two is what produced 180-degree doublebacks on the first
    # cut of this sim: the anchored gate was used as the entry regardless of
    # which side of the site it sat on, so half of all placements ran the
    # polyline out to the far gate, back through the lane, and out to the far
    # side again. The anchored gate is EXACT either way -- it is sitting on the
    # road cell the anchor came from -- so the only thing that changes is which
    # end carries the bend.
    travel = None
    if i_in > 0:
        back = cl[max(0, i_in - ROAD_HEADING_RADIUS)]
        travel = (cl[i_in][0] - back[0], cl[i_in][1] - back[1])
    if travel is None or travel == (0, 0):
        return polyline, "no travel direction at entry", {}

    opposite = None
    for d in site["doors"]:
        if d is entry or not d["onlane"]:
            continue
        if (d["out"][0] + entry["out"][0], d["out"][1] + entry["out"][1]) == (0, 0):
            opposite = d
            break
    if opposite is None:
        return polyline, "anchored gate has no opposite on the lane", {}

    if entry["out"][0] * travel[0] + entry["out"][1] * travel[1] > 0:
        entry, opposite = opposite, entry

    # EXIT: the MOST-OPPOSITE gate the lane reaches. Not the nearest and not
    # the first: a road entering north and leaving east turns ninety degrees
    # and orphans everything behind it, and a caravan is meant to keep the road
    # it was on.
    corridor = set(site["lane"])
    for d in site["doors"]:
        if not d["onlane"]:
            continue
        for t in thresholds(d):
            corridor.add(t)
    corridor &= site["walk"]

    chosen = opposite
    starts = gate_entry(entry, corridor)
    targets = set(gate_entry(chosen, corridor))
    if not starts or not targets:
        return polyline, "a gate's middle is buried and cannot be entered square", {}
    path = bfs(corridor, starts, targets)
    if path is None:
        return polyline, "no walkable lane route to an opposite gate", {}

    reach = clearance(width)
    approach_in = (entry["mid"][0] + entry["out"][0] * reach,
                   entry["mid"][1] + entry["out"][1] * reach)
    approach_out = (chosen["mid"][0] + chosen["out"][0] * reach,
                    chosen["mid"][1] + chosen["out"][1] * reach)

    # How far each gate misses the line the road was already on.
    #
    # BOTH ends are measured, and that is not symmetry for its own sake. Door
    # anchoring makes exactly ONE gate exact -- the one that was shifted onto
    # the anchor cell -- and which of the pair that is depends on which way the
    # polyline happens to run, because placement is undirected and routing is
    # not. Sizing both approaches from the exit's miss alone put the whole
    # correction on an ingress budgeted for nothing and bent it 57 degrees at
    # the gate mouth. Each end now pays for its own miss.
    def miss_at(p, i):
        return min(((p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2) ** 0.5
                   for c in cl[max(0, i - 8):min(len(cl), i + 9)])

    def span_for(m):
        return max(MIN_APPROACH,
                   int(math.ceil(m / math.tan(math.radians(MAX_BEND_DEG)))))

    miss_in = miss_at(approach_in, i_in)
    miss_out = miss_at(approach_out, i_out)
    miss = max(miss_in, miss_out)

    j = max(0, i_in - span_for(miss_in))
    k = min(len(cl) - 1, i_out + span_for(miss_out))
    if j == 0 or k == len(cl) - 1:
        return polyline, "road too short to bend within its own length", {}

    index = {}
    for i, c in enumerate(cl):
        if c not in index:
            index[c] = i
    kept_before = [p for p in polyline if p in index and index[p] < j]
    kept_after = [p for p in polyline if p in index and index[p] > k]

    # STRAIGHT APPROACHES, and this was measured rather than assumed.
    #
    # A quadratic Bezier was tried first, leaving the gate along the wall's
    # normal and arriving along the road's own heading, on the reasoning that
    # spreading the turn over several waypoints would soften it. It made things
    # WORSE at every setting: 41.6 degrees against 39.4 for a plain straight
    # segment, two extra waypoints a site, and no configuration of sample count
    # or approach length recovered the difference. The control point sits where
    # the two tangents meet, which on a shallow miss is a long way off, so the
    # curve swings wide and puts a sharper corner at the gate than the straight
    # line it replaced. Do not re-add it without re-running this sim.
    #
    # What is left is irreducible: a road leaves a gate along the wall's normal
    # and its own heading is up to a cone-width away, so the corner at the gate
    # mouth IS the placement cone. It is bought down by tightening the cone, not
    # by adding geometry.
    new = (kept_before + [cl[j], approach_in, entry["mid"]]
           + corners(path)
           + [chosen["mid"], approach_out, cl[k]] + kept_after)
    dedup = [new[0]]
    for p in new[1:]:
        if p != dedup[-1]:
            dedup.append(p)

    # The approach segments must not clip the building they are bending around.
    # The gate is on the perimeter, so the last cell of each is expected to be
    # in the footprint; anything before that is the road cutting a corner.
    spill = 0
    segs = [(cl[j], approach_in), (approach_out, cl[k])]
    for a, b in segs:
        for c in line(a, b)[:-1]:
            if c in fp:
                spill += 1

    # Two different turns live in this polyline and only one of them is the
    # feature's cost. The corners INSIDE the lane are the street the author
    # drew -- a right-angled corner in a village is a right-angled corner, and
    # measuring it as a road defect would condemn every crossroads. The turns
    # on the APPROACH are what the bend budget is actually spending, so they
    # are counted on their own.
    approach = [cl[j], approach_in, entry["mid"]]
    egress = [chosen["mid"], approach_out, cl[k]]
    if kept_before:
        approach = [kept_before[-1]] + approach
    if kept_after:
        egress = egress + [kept_after[0]]
    bend = turn_angles(approach) + turn_angles(egress)

    stats = {
        "miss": miss,
        "span": max(i_in - j, k - i_out),
        "maxbend": max(bend) if bend else 0.0,
        "maxturn": max(turn_angles(dedup)) if len(dedup) > 2 else 0.0,
        "spill": spill,
        "waypoints": len(dedup),
    }
    return dedup, None, stats


def verify(new_polyline, site):
    """The property the feature rests on. Re-derive the centreline from the
    rewritten polyline alone -- exactly as RebuildRoadCells does on load -- and
    ask where it lies inside the site. Walkers ARE this list."""
    cl = centreline(new_polyline)
    doorcells = set()
    for d in site["doors"]:
        doorcells.update(d["cells"])
    # A gate's threshold is drape-blocked by definition: the two cells above it
    # are the wall it is cut through. Everything within four cells inward of a
    # gate is therefore expected, and is counted apart from anything that is
    # not, so a real fault cannot hide inside the total.
    gate = set(doorcells)
    for d in site["doors"]:
        for c in d["cells"]:
            for k in range(1, 5):
                gate.add((c[0] - d["out"][0] * k, c[1] - d["out"][1] * k))
    inside = [c for c in cl if c in site["footprint"]]
    unwalk = [c for c in inside if c not in site["walk"] and c not in site["wall"]]
    return {
        "on_masonry": len([c for c in cl if c in site["wall"]]),
        "unwalkable_gate": len([c for c in unwalk if c in gate]),
        "unwalkable_not_gate": len([c for c in unwalk if c not in gate]),
    }


# ---- the sweep ------------------------------------------------------------

# Every bearing a generated road can plausibly present, as integer steps: the
# axes, the diagonals, and the shallow and steep chords between them. Roads are
# rasterised from integer polylines, so these ARE the headings, not samples of
# a continuum.
BEARINGS = [(1, 0), (0, 1), (-1, 0), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
            (3, 1), (1, 3), (3, -1), (1, -3),
            (5, 1), (1, 5), (5, -1), (1, -5)]

JITTER = (-2, -1, 0, 1, 2)      # the anchor comes from the DILATED carriageway


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    plans_dir = os.path.join(root, "Assets", "ScriptableObjects", "Sites", "Plans")
    if not os.path.isdir(plans_dir):
        print("plans not found at %s" % plans_dir)
        print("run from the repo root, or pass the root as an argument")
        return 1

    files = sorted(f for f in os.listdir(plans_dir)
                   if f.endswith(".txt") and not f.startswith("_"))

    print("%-40s %8s %6s %6s %6s %7s %6s %6s" %
          ("plan", "bearings", "routed", "fell", "maxWP", "bend", "miss", "spill"))
    print("-" * 96)

    reasons = {}
    tot_masonry = tot_unwalk = tot_spill = tot_gate = 0
    worst_wp = 0
    worst_turn = worst_bend = 0.0
    worst_span = 0
    crossings = []

    for f in files:
        plan = parse_plan(os.path.join(plans_dir, f))
        if plan is None or not plan["lane"]:
            continue
        runs = build_door_runs(plan)
        laneset = set(plan["lane"])
        if sum(1 for r in runs
               if r["out"] != (0, 0) and touches_lane(r, laneset)) < 2:
            crossings.append(f)
            continue

        ok = fell = 0
        maxwp = spill = 0
        maxturn = maxbend = maxmiss = 0.0
        accepted = 0

        for b in BEARINGS:
            cands = anchor_candidates(plan, runs, b)
            if cands:
                accepted += 1
            for (run, rot, mirror) in cands:
                for jit in JITTER:
                    n = (b[0] ** 2 + b[1] ** 2) ** 0.5
                    px, py = -b[1] / n, b[0] / n
                    cell = (int(round(px * jit)), int(round(py * jit)))
                    far = 400
                    poly = [(cell[0] - b[0] * far, cell[1] - b[1] * far),
                            (cell[0] + b[0] * far, cell[1] + b[1] * far)]

                    site = place(plan, runs, cell, run, rot, mirror)
                    entry = next(d for d in site["doors"] if d["local"] is run)
                    new, note, st = route(poly, TRUNK_WIDTH, site, entry)
                    if note is not None:
                        fell += 1
                        reasons[note] = reasons.get(note, 0) + 1
                        continue
                    ok += 1
                    maxwp = max(maxwp, st["waypoints"])
                    maxturn = max(maxturn, st["maxturn"])
                    maxbend = max(maxbend, st["maxbend"])
                    maxmiss = max(maxmiss, st["miss"])
                    worst_span = max(worst_span, st["span"])
                    spill += st["spill"]
                    v = verify(new, site)
                    tot_masonry += v["on_masonry"]
                    tot_gate += v["unwalkable_gate"]
                    tot_unwalk += v["unwalkable_not_gate"]

        tot_spill += spill
        worst_wp = max(worst_wp, maxwp)
        worst_turn = max(worst_turn, maxturn)
        worst_bend = max(worst_bend, maxbend)
        print("%-40s %4d/%-3d %6d %6d %6d %6.1f%s %6.0f %6d" %
              (f, accepted, len(BEARINGS), ok, fell, maxwp, maxbend,
               " ", maxmiss, spill))

    print()
    print("worst polyline after rewrite: %d waypoints" % worst_wp)
    import math as _m
    cone = _m.degrees(_m.acos(DOOR_FACING_COS))
    print("sharpest APPROACH bend:       %.1f degrees" % worst_bend)
    print("  of which the placement cone is an irreducible %.0f -- the rest is the"
          % cone)
    print("  lateral miss, held to %.0f by MIN_APPROACH." % MAX_BEND_DEG)
    print("sharpest turn anywhere:       %.1f degrees  (lane corners included -- "
          "a street corner is a street corner)" % worst_turn)
    print("longest approach demanded:    %d cells" % worst_span)
    print("centreline cells on masonry:  %d" % tot_masonry)
    print("centreline cells drape-blocked at a gate threshold: %d" % tot_gate)
    print("centreline cells drape-blocked ANYWHERE ELSE:       %d" % tot_unwalk)
    print("approach segments clipping the building:       %d" % tot_spill)
    if crossings:
        print()
        print("CROSSINGS (lane, no gates -- the road keeps its own line by design):")
        for c in crossings:
            print("  %s" % c)
    if reasons:
        print()
        print("FALLBACK REASONS (each sends that site back to today's subtraction):")
        for r, n in sorted(reasons.items(), key=lambda kv: -kv[1]):
            print("  %5d  %s" % (n, r))

    print()
    bad = tot_masonry + tot_spill + tot_unwalk
    if bad == 0:
        print("PASS: no re-drawn centreline touched masonry and no approach "
              "clipped a building.")
        return 0
    print("FAIL: %d masonry contacts, %d approach clips, %d drape-blocked "
          "cells away from a gate." % (tot_masonry, tot_spill, tot_unwalk))
    return 1


if __name__ == "__main__":
    sys.exit(main())
