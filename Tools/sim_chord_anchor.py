#!/usr/bin/env python3
"""
Headless proof of CHORD ANCHORING -- the geometry stages 2 and 3 rest on.

There is no C# compiler in the container, so this cannot prove code compiles.
What it CAN prove, and what two expensive test cycles have already been spent
discovering on screen, is whether placing a site against a straight CHORD --
before any road is drawn -- actually removes the problems that placing it
against a rasterised carriageway created.

THE CHANGE BEING TESTED

  today   scatter nodes -> choose chords -> DRAW polylines -> place sites
          against the drawn cells, then subtract, truncate and patch

  tested  scatter nodes -> choose chords -> PLACE SITES against the chords ->
          draw polylines that already respect them

Against a chord the heading is exact, so nothing is estimated; the site can be
TURNED to face the chord rather than rolled and rejected; and the chord is
SPLIT at the site's gates rather than the road being cut out of the building
afterwards. The three questions that decides are:

  Q1  With rotation chosen to face the chord instead of rolled, how big is the
      worst elbow at a gate, and does a straight approach hold it inside the
      30-degree budget the shipped cone already accepts?
  Q2  Does every laned plan give a walkable gate-to-gate route through its
      authored lane, in the orientation the chord picks?
  Q3  Do the resulting rails -- approach in, lane through, approach out -- keep
      off masonry, everywhere, on every chord bearing?

Q3 is the one that matters. Walkers ARE the centreline: DwarvenCaravanController
and DwarvenPatrolController step along DeepRoadGraph.Rail.walk, which is
Centreline(road), and nothing tests whether a walk cell is a road cell. Once a
site stops yielding its footprint, a centreline on masonry is a dwarf in a wall.

It ports, faithfully to the C#:
  line / centreline     -- RoadNetworkBuilder, including the dedupe
  scatter_junctions,
  spanning_tree_edges,
  extra_loop_edges,
  chord_accepted        -- RoadNetworkBuilder's planner, so the chords under
                           test are the ones the game will choose
  rot_local             -- AncientSiteBuilder.RotateLocal
  parse_plan,
  build_door_runs       -- AncientSitePlanLibrary, glyph for glyph
  walkable              -- the drape: a floor cell is walkable only when y+1
                           AND y+2 are also floor

Reported per plan: how many chord placements routed, how many refused and why,
the worst elbow, and -- the number that must be zero -- how many centreline
cells landed on masonry.

Run it from anywhere:  python3 Tools/sim_chord_anchor.py [repo-root] [trials]
"""

import math
import os
import sys
from collections import deque

# ---- constants, from the shipped assets ----------------------------------

CORE_EXCLUSION = 8
TRUNK_WIDTH = 5

# The gate mouth budget. NOT the shipped 30, and the change is deliberate.
#
# Thirty came from DoorFacingCos -- a test of which door BEARINGS a rasterised
# road could serve, not of how sharp a corner a walker may turn. Nothing at
# runtime consumes corner sharpness: DwarfWalkerPuppet sets flipX from the sign
# of dx and bobs, with no heading interpolation, and the authored village lanes
# already contain 90-degree street corners that have shipped for months. So the
# budget is the corner the game already draws, and the real failures are priced
# separately below.
MOUTH_BUDGET_DEG = 90.0

# A turn past this is a road reversing on itself -- the 141 and 180 degree
# doublebacks. Distinct from a sharp corner and always a fault.
DOUBLEBACK_DEG = 90.0

# Leaving a gate: run this far along the door's own normal before turning, so
# the road arrives square rather than clipping the jamb.
SQUARE_ENTRY = 3

# One halving waypoint on the bisector. Splitting the turn across two waypoints
# roughly halves each of them.
SPLIT_ARM = 8

# Straight run behind the arm. Without a tail the arm lands beside the chord
# endpoint rather than on the line to it, and the turn measured 76 degrees on an
# eleven-cell stub. MIN_STUB must exceed SQUARE_ENTRY + SPLIT_ARM + TAIL, not
# just the first two.
TAIL = 8

# The shortest stub worth calling an approach. Sweeping this against the mouth
# angle and the placement rate: 12 keeps 95/98/100 per cent of floor 2/3/4
# chords, 20 keeps 87/96/100, 32 keeps 68/87/99. Twenty is where the tail stops
# being the binding constraint without spending a third of floor 2.
MIN_STUB = 20

# How far outside a gate to assume carved road when deciding whether the
# threshold is walkable. Only ever used for that test; the real approach is
# drawn separately. Any value past SQUARE_ENTRY + 2 gives the same answer.
CARVE_PROBE = 16

ROADS = {
    2: dict(trunk_width=5, rim_margin=7, meander_step=32, meander_amp=5,
            junctions=7, junction_spacing=60, loops=3, rim_trunks=3,
            min_angle=25.0, min_sep=20.0, radius=250),
    3: dict(trunk_width=5, rim_margin=7, meander_step=32, meander_amp=5,
            junctions=4, junction_spacing=90, loops=1, rim_trunks=2,
            min_angle=25.0, min_sep=20.0, radius=400),
    4: dict(trunk_width=5, rim_margin=7, meander_step=24, meander_amp=6,
            junctions=7, junction_spacing=90, loops=4, rim_trunks=3,
            min_angle=25.0, min_sep=20.0, radius=600),
}

UNLISTED = {"DwarvenVillage_TheHearthOfTheDeep",
            "HollowSanctum_ThePilgrimsWay", "_SYMBOLS"}


# ---- ports: geometry ------------------------------------------------------

def line(a, b):
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


def centreline(polyline):
    out, seen = [], set()
    for i in range(len(polyline) - 1):
        for p in line(polyline[i], polyline[i + 1]):
            if p not in seen:
                seen.add(p)
                out.append(p)
    return out


def sq_dist(a, b):
    return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2


def on_circle(centre, radius, angle):
    return (centre[0] + int(round(radius * math.cos(angle))),
            centre[1] + int(round(radius * math.sin(angle))))


def point_to_segment(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    ls = dx * dx + dy * dy
    if ls <= 0:
        return math.sqrt(sq_dist(p, a))
    t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / ls))
    return math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy))


def segment_distance(p1, p2, q1, q2):
    return min(point_to_segment(p1, q1, q2), point_to_segment(p2, q1, q2),
               point_to_segment(q1, p1, p2), point_to_segment(q2, p1, p2))


def angle_between(frm, to1, frm2, to2):
    b1 = math.atan2(to1[1] - frm[1], to1[0] - frm[0])
    b2 = math.atan2(to2[1] - frm2[1], to2[0] - frm2[0])
    d = abs(b1 - b2) % (2.0 * math.pi)
    return min(d, 2.0 * math.pi - d)


def chord_accepted(a, b, na, nb, placed, cfg):
    min_rad = cfg["min_angle"] * math.pi / 180.0
    for e in placed:
        if na >= 0 and (na == e["na"] or na == e["nb"]):
            other = e["b"] if na == e["na"] else e["a"]
            if angle_between(a, b, a, other) < min_rad:
                return False
        if nb >= 0 and (nb == e["na"] or nb == e["nb"]):
            other = e["b"] if nb == e["na"] else e["a"]
            if angle_between(b, a, b, other) < min_rad:
                return False
    for e in placed:
        shares = (na >= 0 and (na == e["na"] or na == e["nb"])) or \
                 (nb >= 0 and (nb == e["na"] or nb == e["nb"]))
        if shares:
            continue
        if segment_distance(a, b, e["a"], e["b"]) < cfg["min_sep"]:
            return False
    return True


def scatter_junctions(rng, centre, usable, cfg):
    nodes = []
    inner = CORE_EXCLUSION + 10
    outer = max(inner + 1, int(usable * 0.85))
    sq = cfg["junction_spacing"] ** 2
    for _ in range(cfg["junctions"] * 40):
        if len(nodes) >= cfg["junctions"]:
            break
        r = math.sqrt(rng.random()) * outer
        if r < inner:
            continue
        ang = rng.random() * 2.0 * math.pi
        cell = (centre[0] + int(round(r * math.cos(ang))),
                centre[1] + int(round(r * math.sin(ang))))
        if any(sq_dist(n, cell) < sq for n in nodes):
            continue
        nodes.append(cell)
    return nodes


def spanning_tree_edges(nodes):
    edges, n = [], len(nodes)
    if n < 2:
        return edges
    in_tree = [False] * n
    in_tree[0] = True
    for _ in range(1, n):
        best, bf, bt = None, -1, -1
        for i in range(n):
            if not in_tree[i]:
                continue
            for j in range(n):
                if in_tree[j]:
                    continue
                d = sq_dist(nodes[i], nodes[j])
                if best is None or d < best:
                    best, bf, bt = d, i, j
        if bt < 0:
            break
        in_tree[bt] = True
        edges.append((bf, bt))
    return edges


def extra_loop_edges(nodes, count):
    if count <= 0 or len(nodes) < 3:
        return []
    tree = {(min(a, b), max(a, b)) for a, b in spanning_tree_edges(nodes)}
    cands = []
    for i in range(len(nodes)):
        for j in range(i + 1, len(nodes)):
            if (i, j) in tree:
                continue
            cands.append((sq_dist(nodes[i], nodes[j]), i, j))
    cands.sort(key=lambda t: t[0])
    return [(i, j) for _, i, j in cands[:count]]


def plan_network(rng, centre, usable, cfg):
    """RoadNetworkBuilder.PlanNetwork -- chords only, nothing drawn."""
    nodes = scatter_junctions(rng, centre, usable, cfg)
    chords, placed = [], []
    if len(nodes) < 2:
        return nodes, chords
    for a, b in spanning_tree_edges(nodes):
        placed.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
        chords.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
    loops = 0
    for a, b in extra_loop_edges(nodes, cfg["loops"] * 3):
        if loops >= cfg["loops"]:
            break
        if not chord_accepted(nodes[a], nodes[b], a, b, placed, cfg):
            continue
        loops += 1
        placed.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
        chords.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
    rim = 0
    for ni in sorted(range(len(nodes)), key=lambda i: -sq_dist(nodes[i], centre)):
        if rim >= cfg["rim_trunks"]:
            break
        frm = nodes[ni]
        bearing = math.atan2(frm[1] - centre[1], frm[0] - centre[0])
        to = on_circle(centre, usable, bearing)
        if sq_dist(to, frm) < 64:
            continue
        if not chord_accepted(frm, to, ni, -1, placed, cfg):
            continue
        rim += 1
        placed.append(dict(a=frm, b=to, na=ni, nb=-1))
        chords.append(dict(a=frm, b=to, na=ni, nb=-1))
    return nodes, chords


# ---- ports: plans ---------------------------------------------------------

def rot_local(p, rot, mirror):
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


def parse_plan(path):
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

    floor, wall, door, lane = [], [], [], []
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            p = (c, -r)
            if ch == "#":
                wall.append(p)
            elif ch in ".=^-o":
                floor.append(p)
            elif ch == "+":
                floor.append(p)
                door.append(p)
            elif ch == "~":
                floor.append(p)
                lane.append(p)
            elif ch == "X":
                wall.append(p)
    if not floor:
        return None
    xs = [p[0] for p in floor + wall]
    ys = [p[1] for p in floor + wall]
    off = ((min(xs) + max(xs)) // 2, (min(ys) + max(ys)) // 2)

    def sh(s):
        return [(p[0] - off[0], p[1] - off[1]) for p in s]

    wallset = set(sh(wall))
    return {
        "hdr": hdr, "name": os.path.basename(path)[:-4],
        "wall": wallset,
        "floor": [q for q in sh(floor) if q not in wallset],
        "door": sh(door), "lane": sh(lane),
        "rotatable": hdr.get("rotate", "").lower() not in ("no", "false", "0"),
    }


def build_door_runs(plan):
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


def walkable(cellset):
    """The drape, as TileInfluenceManager.IsUnderOverhang applies it.

    A cell is blocked when the cell one or two NORTH of it is unmined rock.
    Read the argument as "every mined cell in the world", not "the plan's own
    floor": that distinction is the whole correction. Handing this a plan on
    its own makes every cell outside the footprint a drape source, which marks
    every north-facing gate buried and is not what the engine does -- the road
    about to be carved outside that gate is mined, and mined cells never drape.

    Measured cost of getting this wrong: seven of sixteen laned plans reported
    zero placements and about half of all refusals across the rest were this
    artefact, which is where the "gate middles buried at rotations 0 and 2"
    reading of CoffinRow, KneelingHall and GallowsCourt came from.
    """
    return {c for c in cellset
            if (c[0], c[1] + 1) in cellset and (c[0], c[1] + 2) in cellset}


def load_plans(root):
    d = os.path.join(root, "Assets", "ScriptableObjects", "Sites", "Plans")
    out = {}
    for fn in sorted(os.listdir(d)):
        if not fn.endswith(".txt") or fn[:-4] in UNLISTED:
            continue
        p = parse_plan(os.path.join(d, fn))
        if p is None:
            continue
        p["runs"] = [r for r in build_door_runs(p) if r["out"] != (0, 0)]
        out[p["name"]] = p
    return out


# ---- the raster, ported so the sim can prove what it costs ----------------

def build_edge_polyline(rng, a, b, meander_step, amplitude):
    """RoadNetworkBuilder.BuildEdgePolyline, glyph for glyph.

    Ported because Rasterise runs it on EVERY chord. A stage 2 that emits an
    approach as an ordinary RoadChord gets it meandered, and the square entry
    the whole design rests on is destroyed after the geometry was checked.
    Measured: 86 per cent of floor 4's viable approach stubs are long enough
    to meander, at an amplitude of six cells.

    steps == 1 draws straight, which is why short stubs survive by accident.
    """
    pts = [a]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    if length < 2.0:
        pts.append(b)
        return pts
    steps = max(1, int(round(length / max(4, meander_step))))
    ux, uy = dx / length, dy / length
    px, py = -uy, ux
    walk = 0.0
    for i in range(1, steps):
        t = float(i) / steps
        walk += (rng.random() - 0.5) * 2.0 * amplitude * 0.6
        walk = max(-amplitude, min(amplitude, walk))
        off = walk * math.sin(math.pi * t)
        pts.append((int(round(a[0] + dx * t + px * off)),
                    int(round(a[1] + dy * t + py * off))))
    pts.append(b)
    return pts


# ---- the thing under test -------------------------------------------------

def run_cells(run, rot):
    """The rotated cells of one door run, in run order, with its normal."""
    mid = rot_local(run["mid"], rot, False)
    out = rot_local(run["out"], rot, False)
    if out == (0, 0):
        return None, None, None
    step = (0, 1) if out[0] != 0 else (1, 0)
    n = run["len"]
    base = (mid[0] - step[0] * (n // 2), mid[1] - step[1] * (n // 2))
    return ([(base[0] + step[0] * k, base[1] + step[1] * k) for k in range(n)],
            mid, out)


def carve_stub(cell, normal, into, length=CARVE_PROBE, half=2):
    """The carriageway a stub would open outside a gate. Used only to decide
    walkability at the threshold -- the road is mined ground, so it lifts the
    drape off the gate cell exactly as the real approach will."""
    for k in range(1, length + 1):
        cx = cell[0] + normal[0] * k
        cy = cell[1] + normal[1] * k
        for s in range(-half, half + 1):
            into.add((cx, cy + s) if normal[0] != 0 else (cx + s, cy))


_ENTRY_CACHE = {}


def entry_cell(name, plan, run_index, rot):
    """The cell a road actually meets a gate at: the walkable run cell nearest
    the run's middle.

    NOT the middle itself. In a vertical wall the drape runs ALONG the door, so
    a three-cell door has only its southernmost cell walkable -- y+2 is the wall
    above the run. Measured over every plan and rotation: 216 runs, 34 with a
    buried middle, and ZERO with no walkable cell at all, so choosing the cell
    costs no re-authoring and refuses nothing.

    Among the 34 is DeadCoreVault_TheNinefoldCist, which is @rotate: no with one
    east-facing three-cell door. AncientSiteBuilder.TryDoorAnchor seats a site by
    chosen.mid, so that vault is aligned to a cell nothing can stand on.
    """
    key = (name, run_index, rot)
    if key in _ENTRY_CACHE:
        return _ENTRY_CACHE[key]
    run = plan["runs"][run_index]
    floorset = {rot_local(c, rot, False) for c in plan["floor"]}
    cells, mid, out = run_cells(run, rot)
    found = (None, out)
    if cells is not None:
        best = None
        for c in cells:
            world = set(floorset)
            carve_stub(c, out, world)
            if c in walkable(world):
                d = abs(c[0] - mid[0]) + abs(c[1] - mid[1])
                if best is None or d < best[0]:
                    best = (d, c)
        if best is not None:
            found = (best[1], out)
    _ENTRY_CACHE[key] = found
    return found


def turn_to_face(name, plan, chord_dir):
    """Choose the orientation that presents two opposed gates to the chord AND
    can actually be entered at both.

    Orientations are tried in descending facing order and the first one whose
    entry and exit gates both yield a walkable cell wins. Scoring on facing
    alone and refusing afterwards is what produced a flat ~50 per cent refusal
    rate on every rotatable plan: the best-facing quarter turn is buried about
    half the time, and the next-best was never tried.
    """
    runs = plan["runs"]
    ranked = []
    for rot in (range(4) if plan["rotatable"] else [0]):
        outs = []
        for i, r in enumerate(runs):
            o = rot_local(r["out"], rot, False)
            m = math.hypot(o[0], o[1])
            if m <= 0:
                continue
            outs.append((i, (o[0] / m, o[1] / m)))
        if len(outs) < 2:
            continue
        ent = min(outs, key=lambda t: t[1][0] * chord_dir[0] + t[1][1] * chord_dir[1])
        ext = max(outs, key=lambda t: t[1][0] * chord_dir[0] + t[1][1] * chord_dir[1])
        if ent[0] == ext[0]:
            continue
        score = (ext[1][0] * chord_dir[0] + ext[1][1] * chord_dir[1]) \
            - (ent[1][0] * chord_dir[0] + ent[1][1] * chord_dir[1])
        ranked.append((score, rot, ent[0], ext[0]))
    ranked.sort(key=lambda t: -t[0])
    for score, rot, ei, xi in ranked:
        ec, ein = entry_cell(name, plan, ei, rot)
        xc, eout = entry_cell(name, plan, xi, rot)
        if ec is not None and xc is not None:
            return rot, ei, xi, ec, ein, xc, eout
    return None


def footprint_span(plan, rot, chord_dir):
    """How far the whole building reaches along the chord, not how far apart its
    gates are.

    Clamping on the gate-to-gate distance is what put centrelines on masonry.
    A village's gates sit at its face middles, so on a chord at 38 degrees to
    the plan axes the footprint projects about forty cells further than the
    gates do -- far enough that the chord's own endpoint lands INSIDE the
    building, and every approach drawn to it has to cross masonry to arrive.
    Measured: 289 masonry contacts on the gate span, 0 on the footprint.
    """
    pts = set(plan["wall"]) | set(plan["floor"])
    ds = [(rot_local(p, rot, False)[0] * chord_dir[0]
           + rot_local(p, rot, False)[1] * chord_dir[1]) for p in pts]
    return min(ds), max(ds)


def approach(gate, normal, endpoint):
    """Gate to chord end: square out, one halving waypoint, then straight.

    Waypoints are DROPPED rather than shortened when the room is short. An arm
    laid down with no tail behind it lands beside the endpoint instead of on the
    line to it, and the turn there measured 76 degrees on an eleven-cell stub --
    the same family as the 141 and 180 degree doublebacks, and the reason
    MIN_STUB must exceed SQUARE_ENTRY + SPLIT_ARM + TAIL rather than just the
    first two.
    """
    gx, gy = gate
    ex, ey = endpoint
    reach = math.hypot(ex - gx, ey - gy)
    p1 = (int(round(gx + normal[0] * SQUARE_ENTRY)),
          int(round(gy + normal[1] * SQUARE_ENTRY)))
    if reach < SQUARE_ENTRY + TAIL:
        return [endpoint, gate]
    if reach < SQUARE_ENTRY + SPLIT_ARM + TAIL:
        return [endpoint, p1, gate]
    rx, ry = ex - p1[0], ey - p1[1]
    r = math.hypot(rx, ry)
    if r < 1e-9:
        return [endpoint, p1, gate]
    bx, by = normal[0] + rx / r, normal[1] + ry / r
    bl = math.hypot(bx, by)
    if bl < 1e-9:
        return [endpoint, p1, gate]
    p2 = (int(round(p1[0] + bx / bl * SPLIT_ARM)),
          int(round(p1[1] + by / bl * SPLIT_ARM)))
    if math.hypot(ex - p2[0], ey - p2[1]) < TAIL:
        return [endpoint, p1, gate]
    return [endpoint, p2, p1, gate]


def lane_route(plan, rot, entry_c, exit_c, world):
    """A walkable route between the two chosen gate cells, through the authored
    lane only.

    The corridor is (lane | door) & walkable: a gate is drawn '+', not '~', so
    a lane-only corridor has no cell at the threshold and every route refuses at
    its own front door. `world` carries the carved stubs, so the drape is lifted
    off the gate cells exactly as the road will lift it.
    """
    walk = walkable(world)
    laneset = {rot_local(c, rot, False) for c in plan["lane"]}
    doorset = {rot_local(c, rot, False) for c in plan["door"]}
    corridor = (laneset | doorset) & walk
    if entry_c not in corridor or exit_c not in corridor:
        return None, "gate cell outside lane corridor"

    prev = {entry_c: None}
    q = deque([entry_c])
    while q:
        c = q.popleft()
        if c == exit_c:
            break
        for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (c[0] + d[0], c[1] + d[1])
            if n in corridor and n not in prev:
                prev[n] = c
                q.append(n)
    if exit_c not in prev:
        return None, "lane not connected gate to gate"
    path, c = [], exit_c
    while c is not None:
        path.append(c)
        c = prev[c]
    path.reverse()
    return path, None


def corners(path):
    """Reduce a cell path to waypoints at every direction change."""
    if len(path) < 3:
        return list(path)
    out = [path[0]]
    d0 = (path[1][0] - path[0][0], path[1][1] - path[0][1])
    for i in range(1, len(path) - 1):
        d = (path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1])
        if d != d0:
            out.append(path[i])
            d0 = d
    out.append(path[-1])
    return out


def turn_angles(poly):
    out = []
    for i in range(1, len(poly) - 1):
        ax = poly[i][0] - poly[i - 1][0]
        ay = poly[i][1] - poly[i - 1][1]
        bx = poly[i + 1][0] - poly[i][0]
        by = poly[i + 1][1] - poly[i][1]
        if (ax or ay) and (bx or by):
            d = math.degrees(abs(math.atan2(ay, ax) - math.atan2(by, bx))) % 360.0
            out.append(min(d, 360.0 - d))
    return out


def chord_place(name, plan, chord, t, meander, out_rows):
    """One placement: a laned site seated on a chord, the chord split at its two
    gates, both approaches drawn and the whole rail re-derived and verified."""
    a, b = chord["a"], chord["b"]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    if length < 2 * MIN_STUB:
        return "chord shorter than two stubs"
    cdir = (dx / length, dy / length)

    turned = turn_to_face(name, plan, cdir)
    if turned is None:
        return "no orientation presents two enterable opposed gates"
    rot, ei, xi, ec, ein, xc, eout = turned

    dmin, dmax = footprint_span(plan, rot, cdir)
    lead = (ec[0] * cdir[0] + ec[1] * cdir[1]) - dmin
    span = dmax - dmin
    if length < span + 2 * MIN_STUB:
        return "chord too short for this footprint"
    lo = (MIN_STUB + lead) / length
    hi = (length - (span - lead) - MIN_STUB) / length
    if lo > hi:
        return "chord too short for this footprint"
    t = min(max(t, lo), hi)

    px = int(round(a[0] + dx * t))
    py = int(round(a[1] + dy * t))
    place = (px - ec[0], py - ec[1])

    world = {rot_local(c, rot, False) for c in plan["floor"]}
    carve_stub(ec, ein, world)
    carve_stub(xc, eout, world)
    route, why = lane_route(plan, rot, ec, xc, world)
    if route is None:
        return why

    lane_world = [(place[0] + c[0], place[1] + c[1]) for c in route]
    gate_in = (place[0] + ec[0], place[1] + ec[1])
    gate_out = (place[0] + xc[0], place[1] + xc[1])

    poly_in = approach(gate_in, ein, a)
    poly_out = list(reversed(approach(gate_out, eout, b)))

    if meander is not None:
        # What stage 2 gets if an approach is emitted as an ordinary RoadChord:
        # Rasterise sees two endpoints, so the waypoints do not exist at all and
        # BuildEdgePolyline meanders the whole stub. Modelled by throwing the
        # waypoints away rather than by re-meandering them -- each waypoint
        # SEGMENT is shorter than one meander step and would draw straight,
        # which flatters the answer and proves nothing.
        rng, step, amp = meander
        poly_in = build_edge_polyline(rng, a, gate_in, step, amp)
        poly_out = build_edge_polyline(rng, gate_out, b, step, amp)

    masonry = {(place[0] + c[0], place[1] + c[1])
               for c in (rot_local(w, rot, False) for w in plan["wall"])}
    cl = centreline(poly_in) + lane_world + centreline(poly_out)

    mouth = turn_angles(poly_in) + turn_angles(poly_out)
    interior = turn_angles(corners(lane_world))
    full = poly_in + corners(lane_world)[1:-1] + poly_out

    out_rows.append({
        "on_masonry": sum(1 for c in cl if c in masonry),
        "mouth": max(mouth) if mouth else 0.0,
        "interior": max(interior) if interior else 0.0,
        "doubleback": sum(1 for x in turn_angles(full) if x > DOUBLEBACK_DEG),
    })
    return None


# ---- report ---------------------------------------------------------------

def collect_chords(trials):
    import random
    chords = []
    for fi, cfg in ROADS.items():
        for s in range(trials):
            rng = random.Random(7000 + fi * 1000 + s)
            usable = max(CORE_EXCLUSION + 4, cfg["radius"] - cfg["rim_margin"])
            _, cs = plan_network(rng, (0, 0), usable, cfg)
            for c in cs:
                c["floor"] = fi
            chords.extend(cs)
    return chords


def main():
    import random
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    trials = int(sys.argv[2]) if len(sys.argv) > 2 else 20
    plans = load_plans(root)
    laned = {k: v for k, v in plans.items() if v["lane"] and len(v["runs"]) >= 2}
    print("Loaded %d authored plans, %d of them laned with two or more gates\n"
          % (len(plans), len(laned)))

    chords = collect_chords(trials)
    print("chords under test: %d, from %d planned networks across floors 2-4\n"
          % (len(chords), 3 * trials))

    print("%-38s %7s %7s %7s %9s %8s  %s"
          % ("plan", "placed", "refused", "mouth", "interior", "masonry",
             "top refusal"))

    total_masonry = 0
    total_back = 0
    worst_mouth = 0.0
    worst_interior = 0.0
    placed_all = 0
    by_floor = {}

    for name in sorted(laned):
        plan = laned[name]
        rows, refusals = [], {}
        for ch in chords:
            fl = by_floor.setdefault(ch["floor"], {})
            seat = fl.setdefault(name, [0, 0])
            for t in (0.3, 0.5, 0.7):
                before = len(rows)
                why = chord_place(name, plan, ch, t, None, rows)
                if why:
                    refusals[why] = refusals.get(why, 0) + 1
                seat[1] += 1
                seat[0] += len(rows) - before
        mas = sum(r["on_masonry"] for r in rows)
        back = sum(r["doubleback"] for r in rows)
        mouth = max((r["mouth"] for r in rows), default=0.0)
        inter = max((r["interior"] for r in rows), default=0.0)
        total_masonry += mas
        total_back += back
        worst_mouth = max(worst_mouth, mouth)
        worst_interior = max(worst_interior, inter)
        placed_all += len(rows)
        top = max(refusals.items(), key=lambda kv: kv[1])[0] if refusals else "-"
        print("%-38s %7d %7d %6.1f%s %8.1f %8d  %s"
              % (name[:38], len(rows), sum(refusals.values()), mouth,
                 " " if mouth <= MOUTH_BUDGET_DEG else "!", inter, mas, top[:34]))

    # Rasterise is run on the SAME placements, to price fork 4 rather than
    # assert it. An approach emitted as an ordinary RoadChord gets meandered.
    mrows = []
    mrng = random.Random(4242)
    for name in sorted(laned):
        for ch in chords:
            cfg = ROADS[ch["floor"]]
            chord_place(name, laned[name], ch, 0.5,
                        (mrng, cfg["meander_step"], cfg["meander_amp"]), mrows)
    mas_meander = sum(r["on_masonry"] for r in mrows)
    back_meander = sum(r["doubleback"] for r in mrows)

    # Per floor, because "can floor 2 still seat its outpost" is the question
    # the footprint clamp trades against, and a global rate hides it.
    print()
    print("share of chords each plan can seat, by floor:")
    for fi in sorted(by_floor):
        worst = sorted(((100.0 * v[0] / max(1, v[1]), k)
                        for k, v in by_floor[fi].items()))[:3]
        avg = sum(100.0 * v[0] / max(1, v[1]) for v in by_floor[fi].values()) \
            / max(1, len(by_floor[fi]))
        print("  floor %d  mean %3.0f%%   tightest: %s"
              % (fi, avg, ", ".join("%s %.0f%%" % (n[:26], p) for p, n in worst)))

    print()
    print("placements: %d" % placed_all)
    print("worst gate mouth:    %5.1f degrees (budget %.0f)   %s"
          % (worst_mouth, MOUTH_BUDGET_DEG,
             "PASS" if worst_mouth <= MOUTH_BUDGET_DEG else "OVER BUDGET"))
    print("worst lane interior: %5.1f degrees -- authored street corners, not a budget"
          % worst_interior)
    print("doublebacks over %.0f: %d   %s"
          % (DOUBLEBACK_DEG, total_back,
             "PASS" if total_back == 0 else "FAIL -- road reverses"))
    print("centreline cells on masonry: %d   %s"
          % (total_masonry,
             "PASS" if total_masonry == 0 else "FAIL -- walker in a wall"))
    print()
    print("same placements, stubs handed to Rasterise instead of kept as waypoints:")
    print("  masonry %d, doublebacks %d   %s"
          % (mas_meander, back_meander,
             "meander is harmless here" if mas_meander == 0 else
             "FAIL -- approach stubs must not meander"))

    ok = (total_masonry == 0 and total_back == 0
          and worst_mouth <= MOUTH_BUDGET_DEG)
    print()
    print("RESULT: %s" % ("GREEN" if ok else "RED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
