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

# The elbow budget. Kept at the shipped cone's worst case so this is measured
# against what already ships rather than against a number chosen to pass.
ELBOW_BUDGET_DEG = 30.0

# DESIRED straight approach length either side of a site. 48 in the lane
# routing sim, where it took the worst corner from 39.4 degrees to 36.7.
#
# It is a want rather than a rule here, and the chords are the reason. Measured
# over 30 planned networks per floor, the MEDIAN chord is 109 cells on floor
# index 2, 184 on 3 and 253 on 4 -- and a village is 61 to 75 cells across. A
# flat 48 either side plus a hold does not fit on half the chords that exist,
# so the approach takes whatever the chord leaves it and the ELBOW is what gets
# measured. Refusing short chords instead would have hidden the question.
MIN_APPROACH = 48

# The shortest stub worth calling an approach. Below this the site is seated so
# close to a junction that the two are the same place, and the corner belongs to
# the junction rather than to the site.
MIN_STUB = 10

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


def walkable(floorset):
    """The drape. A floor cell is walkable only when y+1 AND y+2 are floor."""
    return {c for c in floorset
            if (c[0], c[1] + 1) in floorset and (c[0], c[1] + 2) in floorset}


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


# ---- the thing under test -------------------------------------------------

def turn_to_face(plan, runs, chord_dir):
    """Q1's core claim: a rotatable plan is TURNED to face the chord rather
    than rolled and rejected.

    Returns (rot, mirror, entry_run, exit_run) or None. The entry run is the
    one whose outward normal best opposes the chord's direction of travel; the
    exit run is the one that best agrees with it. Mirroring is not searched --
    it maps a plan onto itself for these purposes and only doubles the work."""
    best = None
    rots = range(4) if plan["rotatable"] else [0]
    for rot in rots:
        for mirror in (False,):
            outs = []
            for r in runs:
                o = rot_local(r["out"], rot, mirror)
                m = math.hypot(o[0], o[1])
                if m <= 0:
                    continue
                outs.append((r, (o[0] / m, o[1] / m)))
            if len(outs) < 2:
                continue
            # entry: normal most opposed to travel. exit: most aligned.
            ent = min(outs, key=lambda t: t[1][0] * chord_dir[0] + t[1][1] * chord_dir[1])
            ext = max(outs, key=lambda t: t[1][0] * chord_dir[0] + t[1][1] * chord_dir[1])
            if ent[0] is ext[0]:
                continue
            score = (ext[1][0] * chord_dir[0] + ext[1][1] * chord_dir[1]) \
                - (ent[1][0] * chord_dir[0] + ent[1][1] * chord_dir[1])
            if best is None or score > best[0]:
                best = (score, rot, mirror, ent[0], ext[0])
    if best is None:
        return None
    return best[1], best[2], best[3], best[4]


def lane_route(plan, rot, mirror, entry, exit_run):
    """Q2: a walkable route from the entry gate's middle to the exit gate's
    middle, through the AUTHORED lane only.

    Entered SQUARE from the gate's middle, which the lane routing sim
    established: a start offset sideways along the run makes the first segment
    diagonal and puts a fixed 26.6-degree corner just inside every gate that no
    approach budget can touch. Where the middle is buried under the drape the
    route REFUSES rather than sliding sideways."""
    laneset = {rot_local(c, rot, mirror) for c in plan["lane"]}
    doorset = {rot_local(c, rot, mirror) for c in plan["door"]}
    floorset = {rot_local(c, rot, mirror) for c in plan["floor"]}
    walk = walkable(floorset)

    # The DOOR cells belong to the corridor as well as the lane cells. A gate
    # is drawn with '+', not '~', so a lane-only corridor has no cell at the
    # threshold and every route refuses at its own front door -- which is what
    # this sim did until the counters said every plan failed identically, and
    # sixteen plans failing the same way is a bug in the test, not in sixteen
    # plans.
    corridor = (laneset | doorset) & walk

    start = rot_local(entry["mid"], rot, mirror)
    goal = rot_local(exit_run["mid"], rot, mirror)
    if start not in corridor or goal not in corridor:
        return None, "gate middle buried"

    prev = {start: None}
    q = deque([start])
    while q:
        c = q.popleft()
        if c == goal:
            break
        for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (c[0] + d[0], c[1] + d[1])
            if n in corridor and n not in prev:
                prev[n] = c
                q.append(n)
    if goal not in prev:
        return None, "lane not connected gate to gate"

    path = []
    c = goal
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
            d = math.degrees(abs(math.atan2(ay, ax) - math.atan2(by, bx)))
            d = d % 360.0
            out.append(min(d, 360.0 - d))
    return out


def chord_place(plan, chord, t, trials_out):
    """One placement: a laned site seated on a chord at parameter t, the chord
    split at its two gates, and the whole rail re-derived and verified.

    Returns a dict of measurements, or a refusal reason."""
    runs = plan["runs"]
    if len(runs) < 2 or not plan["lane"]:
        return {"refused": "not a laned plan with two gates"}

    a, b = chord["a"], chord["b"]
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L < 2 * MIN_STUB:
        return {"refused": "chord shorter than two stubs"}
    cdir = (dx / L, dy / L)

    turned = turn_to_face(plan, runs, cdir)
    if turned is None:
        return {"refused": "no orientation presents two opposed gates"}
    rot, mirror, entry, exit_run = turned

    emid = rot_local(entry["mid"], rot, mirror)
    xmid = rot_local(exit_run["mid"], rot, mirror)

    # How much of the chord the building itself eats, measured gate to gate
    # ALONG the chord. The site has to fit between the two stubs or there is
    # nothing to seat it on.
    span = abs((xmid[0] - emid[0]) * cdir[0] + (xmid[1] - emid[1]) * cdir[1])
    if L < span + 2 * MIN_STUB:
        return {"refused": "chord too short for this plan gate to gate"}

    # Seat the ENTRY gate on the chord, clamped so both stubs survive. t is the
    # requested position; the clamp is what a placement would do rather than
    # discarding the chord.
    lo = MIN_STUB / L
    hi = (L - span - MIN_STUB) / L
    t = min(max(t, lo), hi)
    px = int(round(a[0] + dx * t))
    py = int(round(a[1] + dy * t))
    place = (px - emid[0], py - emid[1])

    gate_in = (place[0] + emid[0], place[1] + emid[1])
    gate_out = (place[0] + xmid[0], place[1] + xmid[1])

    # THE MISS. The exit gate is at a vector the PLAN fixes, not one the chord
    # does, so it does not land on the chord. Each end is sized from its OWN
    # miss -- the lane routing sim measured that conflating them bends an
    # ingress 57 degrees.
    miss_out = point_to_segment(gate_out, a, b)

    route, why = lane_route(plan, rot, mirror, entry, exit_run)
    if route is None:
        return {"refused": why}

    lane_world = [(place[0] + c[0], place[1] + c[1]) for c in route]

    # Approaches: straight, leaving each gate along its own normal, then
    # meeting the chord. MIN_APPROACH out from the gate, then back to the
    # chord end.
    ein = rot_local(entry["out"], rot, mirror)
    eout = rot_local(exit_run["out"], rot, mirror)

    # Each end takes the approach the chord leaves it, up to the want. Sizing
    # both from one end's budget is what bent an ingress 57 degrees in the lane
    # routing sim.
    run_in = max(MIN_STUB, min(MIN_APPROACH, math.dist(a, gate_in) - 1))
    run_out = max(MIN_STUB, min(MIN_APPROACH, math.dist(b, gate_out) - 1))
    approach_in = (int(round(gate_in[0] + ein[0] * run_in)),
                   int(round(gate_in[1] + ein[1] * run_in)))
    approach_out = (int(round(gate_out[0] + eout[0] * run_out)),
                    int(round(gate_out[1] + eout[1] * run_out)))

    poly_in = [a, approach_in, gate_in]
    poly_out = [gate_out, approach_out, b]
    full = poly_in + corners(lane_world)[1:-1] + poly_out

    masonry = {(place[0] + c[0], place[1] + c[1])
               for c in (rot_local(w, rot, mirror) for w in plan["wall"])}
    cl = centreline(poly_in) + lane_world + centreline(poly_out)
    on_masonry = sum(1 for c in cl if c in masonry)

    elbows = turn_angles(full)
    worst = max(elbows) if elbows else 0.0

    trials_out.append({
        "worst_elbow": worst,
        "on_masonry": on_masonry,
        "waypoints": len(full),
        "miss_out": miss_out,
    })
    return {"ok": True}


# ---- report ---------------------------------------------------------------

def main():
    import random
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    trials = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    plans = load_plans(root)
    laned = {k: v for k, v in plans.items() if v["lane"] and len(v["runs"]) >= 2}
    print("Loaded %d authored plans, %d of them laned with two or more gates\n"
          % (len(plans), len(laned)))

    # Collect chords from real planned networks across all three road floors.
    chords = []
    for fi, cfg in ROADS.items():
        for s in range(trials):
            rng = random.Random(7000 + fi * 1000 + s)
            usable = max(CORE_EXCLUSION + 4, cfg["radius"] - cfg["rim_margin"])
            _, cs = plan_network(rng, (0, 0), usable, cfg)
            chords.extend(cs)
    print("chords under test: %d, from %d planned networks across floors 2-4\n"
          % (len(chords), 3 * trials))

    print("%-38s %7s %7s %9s %8s  %s"
          % ("plan", "placed", "refused", "worstElbow", "masonry", "top refusal"))
    total_masonry = 0
    worst_all = 0.0
    for name in sorted(laned):
        plan = laned[name]
        out, refusals = [], {}
        for i, ch in enumerate(chords):
            for t in (0.3, 0.5, 0.7):
                r = chord_place(plan, ch, t, out)
                if "refused" in r:
                    refusals[r["refused"]] = refusals.get(r["refused"], 0) + 1
        placed = len(out)
        worst = max((o["worst_elbow"] for o in out), default=0.0)
        mas = sum(o["on_masonry"] for o in out)
        total_masonry += mas
        worst_all = max(worst_all, worst)
        top = max(refusals.items(), key=lambda kv: kv[1])[0] if refusals else "-"
        print("%-38s %7d %7d %8.1f%s %8d  %s"
              % (name[:38], placed, sum(refusals.values()), worst,
                 " " if worst <= ELBOW_BUDGET_DEG else "!", mas, top[:40]))

    print()
    print("worst elbow anywhere: %.1f degrees (budget %.0f)   %s"
          % (worst_all, ELBOW_BUDGET_DEG,
             "PASS" if worst_all <= ELBOW_BUDGET_DEG else "OVER BUDGET"))
    print("centreline cells on masonry: %d   %s"
          % (total_masonry, "PASS" if total_masonry == 0 else "FAIL -- walker in a wall"))


if __name__ == "__main__":
    main()
