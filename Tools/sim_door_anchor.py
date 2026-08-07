#!/usr/bin/env python3
"""
Headless simulation of the DOOR-ANCHOR GATE against the site quotas.

There is no C# compiler in the container, so this cannot prove the code
compiles. What it CAN prove -- and what the expensive test cycle would
otherwise have to discover a floor at a time -- is the one question lane
routing's placement half turns on:

    once a plan must find a road whose local heading FACES one of its gates,
    does the floor still fill its quotas, and if not, which counter grows?

Two things are measured, and they are separate questions.

  1. HEADING RESOLUTION. TryRoadHeading needs three or more road cells within
     RoadHeadingRadius. Fed the stride-12 anchor sample it is a lottery on road
     density; fed the undecimated centreline it should resolve essentially
     always. The first table is that comparison, per floor, and it is the whole
     justification for carrying roadHeadingCells at all.

  2. QUOTA SURVIVAL. The placement passes are run under three stamping
     scenarios -- nothing stamped (what ships in 2a), the two WardChapels
     stamped, and every road-anchored laned plan stamped -- so the cost of a
     stamp is a number rather than an argument.

It ports, faithfully to the C#:
  Line / Centreline            -- RoadNetworkBuilder, including the
                                  brokenGapCells tail trim
  ScatterJunctions             -- the sqrt-uniform disc sample and the spacing
  SpanningTreeEdges            -- Prim over squared distance
  ExtraLoopEdges               -- shortest unused pairs
  ChordAccepted                -- the junction-angle and separation rules
  BuildEdgePolyline            -- the sine-enveloped bounded random walk
  JunctionNodes                -- endpoints paired inside the merge radius
  RebuildRoadAnchors           -- stride 12 sample, ends, junctions
  parse_plan / build_door_runs -- AncientSitePlanLibrary, glyph for glyph
  RotateLocal                  -- AncientSiteBuilder
  TryPickAnchor                -- the 64-sample road path and the 96-sample
                                  Free degrade, spacing tested inside
  TryRoadHeading               -- least squares over the cells within radius 6
  the door gate                -- undirected dot against DoorFacingCos, the
                                  -RotateLocal(mid) shift, and the band and
                                  spacing RE-VALIDATION after it

What it deliberately does NOT model: Compose, the disc clamp, the twelve-cell
floor and the walkability guard. Every authored plan is drawn at a fixed size
well inside the band, and no part of this change touches any of those tests --
so including them would add noise to the comparison without moving it.

System.Random and Python's Mersenne Twister do not agree, so no seed here
reproduces a world in the game. That is not what this is for: every figure
below is a distribution over many seeds, and the comparison between scenarios
is run on the SAME seeds.

Run it from anywhere:  python3 Tools/sim_door_anchor.py [repo-root] [seeds]
"""

import math
import os
import random
import sys

# ---- constants, from the shipped assets and source ------------------------

CORE_EXCLUSION = 8              # TerrainFeatureGenerator.exclusionRadiusFromCenter
ROAD_HEADING_RADIUS = 6         # AncientSiteBuilder.RoadHeadingRadius
DOOR_FACING_COS = 0.8660        # AncientSiteBuilder.DoorFacingCos, 30 degrees
SAMPLE_STRIDE = 12              # TerrainFeatureGenerator.RebuildRoadAnchors
JUNCTION_MERGE_RADIUS = 6       # TerrainFeatureGenerator.RoadJunctionMergeRadius
GENERAL_ATTEMPTS_PER_SITE = 12
HOLY_ATTEMPTS_PER_SITE = 24
GUARANTEE_ATTEMPTS = 240
ANCHOR_SAMPLES = 64             # TryPickAnchor's road-source budget
FREE_SAMPLES = 96               # its Free-degrade budget

# RoadNetworkProfile.asset, floors 2-4. mode 2 is Network.
ROADS = {
    2: dict(trunk_width=5, spur_width=2, rim_margin=7, meander_step=32,
            meander_amp=5, junctions=7, junction_spacing=60, loops=3,
            rim_trunks=3, spurs=3, broken_gap=6, spur_min=30, spur_max=80,
            min_angle=25.0, min_sep=20.0),
    3: dict(trunk_width=5, spur_width=2, rim_margin=7, meander_step=32,
            meander_amp=5, junctions=4, junction_spacing=90, loops=1,
            rim_trunks=2, spurs=2, broken_gap=6, spur_min=30, spur_max=80,
            min_angle=25.0, min_sep=20.0),
    4: dict(trunk_width=5, spur_width=2, rim_margin=7, meander_step=24,
            meander_amp=6, junctions=7, junction_spacing=90, loops=4,
            rim_trunks=3, spurs=4, broken_gap=6, spur_min=30, spur_max=80,
            min_angle=25.0, min_sep=20.0),
}

# AncientSiteProfile.asset. `pool` and `holy_pool` are archetype ids.
SITES = {
    2: dict(radius=250, band=(0.30, 0.65), spacing=70, rim_margin=12,
            sites=(1, 2), holy=(3, 4), pool=[1, 5, 6, 4, 7],
            holy_pool=[9, 10, 11, 12], all_archetypes=False,
            outpost=True, village=False, vault=False, outpost_archetype=5),
    3: dict(radius=400, band=(0.15, 0.65), spacing=90, rim_margin=12,
            sites=(3, 5), holy=(5, 6), pool=[1, 6, 4, 7],
            holy_pool=[9, 10, 11, 12], all_archetypes=False,
            outpost=False, village=True, vault=False, outpost_archetype=5),
    4: dict(radius=600, band=(0.15, 0.65), spacing=90, rim_margin=12,
            sites=(9, 13), holy=(0, 0), pool=[], holy_pool=[],
            all_archetypes=True,
            outpost=False, village=False, vault=True, outpost_archetype=5),
}

ARCHETYPE_ID = {
    "SunkenPlaza": 0, "CollapsedArchive": 1, "Ossuary": 2, "BrokenAqueduct": 3,
    "HollowSanctum": 4, "SealedGate": 5, "GuardPost": 6, "TollHouse": 7,
    "DwarvenVillage": 8, "ChurchSeal": 9, "SealedCrypt": 10, "WardChapel": 11,
    "BlessedSpring": 12, "DeadCoreVault": 13,
}

# AncientSiteProfile.VariantCountFor: zero marks an authored-only archetype.
ZERO_VARIANT = {8, 9, 10, 11, 12, 13}

# AncientSiteProfile.AnchorFor.
ANCHOR_FOR = {
    0: "Junction", 1: "AlongRoad", 2: "Free", 3: "Crossing", 4: "Free",
    5: "RoadEnd", 6: "AlongRoad", 7: "AlongRoad", 8: "AlongRoad",
    13: "AlongRoad",
}

# The plans on disk that AncientSiteProfile.asset does NOT list. They cannot
# roll in game, so the pools here must not hold them either.
UNLISTED = {
    "DwarvenVillage_TheHearthOfTheDeep",
    "HollowSanctum_ThePilgrimsWay",
    "_SYMBOLS",
}

# The stamping scenarios. A name here is a plan file's stem.
WARD_CHAPELS = {"WardChapel_TheLampChapel", "WardChapel_TheWatchfulNave"}


# ---- ports: road geometry -------------------------------------------------

def line(a, b):
    """RoadNetworkBuilder.Line -- Bresenham that may step BOTH axes in one
    iteration."""
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
    """RoadNetworkBuilder.Centreline, dedupe keeping the first occurrence."""
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


def sq_dist(a, b):
    return (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2


def on_circle(centre, radius, angle):
    return (centre[0] + int(round(radius * math.cos(angle))),
            centre[1] + int(round(radius * math.sin(angle))))


def clamp_into_disc(cell, centre, radius):
    dx, dy = cell[0] - centre[0], cell[1] - centre[1]
    d = math.hypot(dx, dy)
    if d <= radius or d < 0.001:
        return cell
    s = radius / d
    return (centre[0] + int(round(dx * s)), centre[1] + int(round(dy * s)))


def point_to_segment(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    len_sq = dx * dx + dy * dy
    if len_sq <= 0.0:
        return math.sqrt(sq_dist(p, a))
    t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len_sq
    t = max(0.0, min(1.0, t))
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
    if cfg["min_angle"] > 0.0:
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
    if cfg["min_sep"] > 0.0:
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
    spacing_sq = cfg["junction_spacing"] ** 2
    tries = 0
    while tries < cfg["junctions"] * 40 and len(nodes) < cfg["junctions"]:
        tries += 1
        r = math.sqrt(rng.random()) * outer
        if r < inner:
            continue
        angle = rng.random() * 2.0 * math.pi
        cell = (centre[0] + int(round(r * math.cos(angle))),
                centre[1] + int(round(r * math.sin(angle))))
        if any(sq_dist(n, cell) < spacing_sq for n in nodes):
            continue
        nodes.append(cell)
    return nodes


def spanning_tree_edges(nodes):
    edges = []
    n = len(nodes)
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
    extra = []
    if count <= 0 or len(nodes) < 3:
        return extra
    tree = set()
    for a, b in spanning_tree_edges(nodes):
        tree.add((min(a, b), max(a, b)))
    cands = []
    for i in range(len(nodes)):
        for j in range(i + 1, len(nodes)):
            if (i, j) in tree:
                continue
            cands.append((sq_dist(nodes[i], nodes[j]), i, j))
    cands.sort(key=lambda t: t[0])
    for _, i, j in cands[:count]:
        extra.append((i, j))
    return extra


def build_edge_polyline(rng, a, b, cfg):
    points = [a]
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    if length < 2.0:
        points.append(b)
        return points
    steps = max(1, int(round(length / max(4, cfg["meander_step"]))))
    ux, uy = dx / length, dy / length
    px, py = -uy, ux
    amplitude = max(0.0, cfg["meander_amp"])
    walk = 0.0
    for i in range(1, steps):
        t = i / steps
        walk += (rng.random() - 0.5) * 2.0 * amplitude * 0.6
        walk = max(-amplitude, min(amplitude, walk))
        offset = walk * math.sin(math.pi * t)
        points.append((int(round(a[0] + dx * t + px * offset)),
                       int(round(a[1] + dy * t + py * offset))))
    points.append(b)
    return points


def build_network(rng, centre, usable, cfg):
    """RoadNetworkBuilder.BuildNetwork. Returns a list of
    (polyline, broken_gap) in the order the C# adds them."""
    nodes = scatter_junctions(rng, centre, usable, cfg)
    roads = []
    if len(nodes) < 2:
        return nodes, roads
    placed = []

    for a, b in spanning_tree_edges(nodes):
        placed.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
        roads.append((build_edge_polyline(rng, nodes[a], nodes[b], cfg), 0))

    loops_placed = 0
    for a, b in extra_loop_edges(nodes, cfg["loops"] * 3):
        if loops_placed >= cfg["loops"]:
            break
        if not chord_accepted(nodes[a], nodes[b], a, b, placed, cfg):
            continue
        loops_placed += 1
        placed.append(dict(a=nodes[a], b=nodes[b], na=a, nb=b))
        roads.append((build_edge_polyline(rng, nodes[a], nodes[b], cfg), 0))

    by_distance = sorted(range(len(nodes)),
                         key=lambda i: -sq_dist(nodes[i], centre))
    rim_placed = 0
    for ni in by_distance:
        if rim_placed >= cfg["rim_trunks"]:
            break
        frm = nodes[ni]
        bearing = math.atan2(frm[1] - centre[1], frm[0] - centre[0])
        to = on_circle(centre, usable, bearing)
        if sq_dist(to, frm) < 64:
            continue
        if not chord_accepted(frm, to, ni, -1, placed, cfg):
            continue
        rim_placed += 1
        placed.append(dict(a=frm, b=to, na=ni, nb=-1))
        roads.append((build_edge_polyline(rng, frm, to, cfg), cfg["broken_gap"]))

    spurs_placed = 0
    for _ in range(cfg["spurs"] * 8):
        if spurs_placed >= cfg["spurs"]:
            break
        ni = rng.randrange(len(nodes))
        frm = nodes[ni]
        bearing = rng.random() * 2.0 * math.pi
        length = rng.randint(cfg["spur_min"], cfg["spur_max"])
        to = clamp_into_disc(
            (frm[0] + int(round(math.cos(bearing) * length)),
             frm[1] + int(round(math.sin(bearing) * length))), centre, usable)
        if sq_dist(to, frm) < 64:
            continue
        if not chord_accepted(frm, to, ni, -1, placed, cfg):
            continue
        spurs_placed += 1
        placed.append(dict(a=frm, b=to, na=ni, nb=-1))
        roads.append((build_edge_polyline(rng, frm, to, cfg), cfg["broken_gap"]))

    return nodes, roads


def rebuild_road_anchors(roads):
    """TerrainFeatureGenerator.RebuildRoadAnchors. Returns the three anchor
    lists PLUS the undecimated centreline the heading test wants."""
    anchors, heading, ends, endpoints, lines = [], [], [], [], []
    for polyline, gap in roads:
        cl = centreline(polyline, gap)
        if not cl:
            continue
        lines.append(cl)
        anchors.extend(cl[i] for i in range(0, len(cl), SAMPLE_STRIDE))
        heading.extend(cl)
        endpoints.append(cl[0])
        endpoints.append(cl[-1])
        if gap > 0:
            ends.append(cl[-1])

    # JunctionNodes: endpoints paired inside the merge radius.
    junctions = []
    r_sq = JUNCTION_MERGE_RADIUS ** 2
    for i in range(len(endpoints)):
        for j in range(i + 1, len(endpoints)):
            if sq_dist(endpoints[i], endpoints[j]) > r_sq:
                continue
            if endpoints[i] not in junctions:
                junctions.append(endpoints[i])
            break

    if not ends and endpoints:
        ends.extend(endpoints)
    return junctions, anchors, heading, ends


# ---- ports: plans ---------------------------------------------------------

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


def parse_plan(path):
    """AncientSitePlanLibrary.Parse, glyph for glyph."""
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

    def shift(s):
        return [(p[0] - off[0], p[1] - off[1]) for p in s]

    wallset = set(shift(wall))
    return {
        "hdr": hdr,
        "name": os.path.basename(path)[:-4],
        "archetype": ARCHETYPE_ID.get(hdr.get("archetype", ""), -1),
        "wall": wallset,
        "floor": [q for q in shift(floor) if q not in wallset],
        "door": shift(door),
        "lane": shift(lane),
        "rotatable": hdr.get("rotate", "").lower() not in ("no", "false", "0"),
        "general": hdr.get("general", "").lower() not in ("no", "false", "0"),
        "anchor_on_door": hdr.get("anchor_on", "").lower() == "door",
        "anchor_override": hdr.get("anchor", ""),
    }


def build_door_runs(plan):
    """AncientSitePlanLibrary.BuildDoorRuns."""
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


def load_plans(root):
    plans = {}
    d = os.path.join(root, "Assets", "ScriptableObjects", "Sites", "Plans")
    for fn in sorted(os.listdir(d)):
        if not fn.endswith(".txt"):
            continue
        stem = fn[:-4]
        if stem in UNLISTED:
            continue
        p = parse_plan(os.path.join(d, fn))
        if p is None or p["archetype"] < 0:
            continue
        p["runs"] = [r for r in build_door_runs(p) if r["out"] != (0, 0)]
        plans[stem] = p
    return plans


# ---- ports: placement -----------------------------------------------------

def in_band(cell, centre, inner, outer):
    d = sq_dist(cell, centre)
    return inner * inner <= d <= outer * outer


def too_close(cell, used, spacing_sq):
    return any(sq_dist(cell, u) < spacing_sq for u in used)


def try_pick_anchor(rng, kind, centre, inner, outer,
                    junctions, road_cells, road_ends, used, spacing_sq):
    """AncientSiteBuilder.TryPickAnchor. anchorRequired is not modelled: no
    shipped plan sets it."""
    source = None
    if kind == "Junction":
        source = junctions
    elif kind in ("AlongRoad", "Crossing"):
        source = road_cells
    elif kind == "RoadEnd":
        source = road_ends

    if source:
        for _ in range(ANCHOR_SAMPLES):
            c = source[rng.randrange(len(source))]
            if not in_band(c, centre, inner, outer):
                continue
            if too_close(c, used, spacing_sq):
                continue
            return c

    for _ in range(FREE_SAMPLES):
        c = (centre[0] + rng.randint(-outer, outer),
             centre[1] + rng.randint(-outer, outer))
        if not in_band(c, centre, inner, outer):
            continue
        if too_close(c, used, spacing_sq):
            continue
        return c
    return None


def try_road_heading(heading_cells_index, at):
    """AncientSiteBuilder.TryRoadHeading, least squares over the cells within
    RoadHeadingRadius. Takes a cell-bucket index rather than a flat list purely
    for speed -- the arithmetic is identical."""
    sxx = syy = sxy = 0.0
    n = 0
    r2 = ROAD_HEADING_RADIUS * ROAD_HEADING_RADIUS
    bx, by = at[0] >> 4, at[1] >> 4
    for gx in range(bx - 1, bx + 2):
        for gy in range(by - 1, by + 2):
            for c in heading_cells_index.get((gx, gy), ()):
                dx, dy = c[0] - at[0], c[1] - at[1]
                if dx * dx + dy * dy > r2:
                    continue
                sxx += float(dx) * dx
                syy += float(dy) * dy
                sxy += float(dx) * dy
                n += 1
    if n < 3:
        return None
    theta = 0.5 * math.atan2(2.0 * sxy, sxx - syy)
    return (math.cos(theta), math.sin(theta))


def bucket(cells):
    idx = {}
    for c in cells:
        idx.setdefault((c[0] >> 4, c[1] >> 4), []).append(c)
    return idx


def try_door_anchor(rng, runs, rot, mirror, anchor, centre, inner, outer,
                    heading_index, used, spacing_sq):
    """The shared helper this delivery extracts. Returns
    (place_at, fail_reason) where fail_reason is None on success,
    'heading' when the door gate refused, and 'space' when the shift left the
    band or collided."""
    if not runs:
        return anchor, None

    heading = try_road_heading(heading_index, anchor)
    if heading is None:
        return None, "heading"

    qualifying = []
    for run in runs:
        ox, oy = rot_local(run["out"], rot, mirror)
        mag = math.hypot(ox, oy)
        if mag <= 0.0:
            continue
        dot = abs(heading[0] * ox / mag + heading[1] * oy / mag)
        if dot >= DOOR_FACING_COS:
            qualifying.append(run)
    if not qualifying:
        return None, "heading"

    run = qualifying[0] if len(qualifying) == 1 \
        else qualifying[rng.randrange(len(qualifying))]

    shift = rot_local(run["mid"], rot, mirror)
    place_at = (anchor[0] - shift[0], anchor[1] - shift[1])

    if not in_band(place_at, centre, inner, outer):
        return None, "space"
    if too_close(place_at, used, spacing_sq):
        return None, "space"
    return place_at, None


# ---- the floor ------------------------------------------------------------

class Tally:
    def __init__(self):
        self.placed = 0
        self.attempts = 0
        self.too_close = 0
        self.no_door_heading = 0


def build_pool(rng, archetypes, plans, stamped, all_archetypes):
    """AncientSiteBuilder.BuildPlanPoolFrom: procedural refs, then the authored
    plans whose archetype is in the roster, then a Fisher-Yates shuffle."""
    roster = list(range(0, 8)) if all_archetypes else list(archetypes)
    pool = []
    for a in roster:
        variants = 0 if a in ZERO_VARIANT else 3
        for _ in range(variants):
            pool.append({"archetype": a, "runs": [], "rotatable": True,
                         "anchor": ANCHOR_FOR.get(a, "Free"), "general": True,
                         "name": ""})
    for stem, p in plans.items():
        if p["archetype"] not in roster:
            continue
        anchor = p["anchor_override"] or ANCHOR_FOR.get(p["archetype"], "Free")
        gated = p["anchor_on_door"] or stem in stamped
        pool.append({"archetype": p["archetype"],
                     "runs": p["runs"] if gated else [],
                     "rotatable": p["rotatable"], "anchor": anchor,
                     "general": p["general"], "name": stem})
    for i in range(len(pool) - 1, 0, -1):
        j = rng.randint(0, i)
        pool[i], pool[j] = pool[j], pool[i]
    return pool


def fill(rng, pool, want, per_site, centre, inner, outer,
         junctions, road_cells, road_ends, heading_index, used, spacing_sq,
         progress_base, counts_as_extra):
    tally = Tally()
    if not pool or want <= 0:
        return tally
    cursor = 0
    max_attempts = want * per_site
    while tally.attempts < max_attempts:
        progress = tally.placed if counts_as_extra else progress_base[0]
        if progress >= want:
            break
        tally.attempts += 1
        plan = pool[cursor % len(pool)]
        cursor += 1

        anchor = try_pick_anchor(rng, plan["anchor"], centre, inner, outer,
                                 junctions, road_cells, road_ends,
                                 used, spacing_sq)
        if anchor is None:
            tally.too_close += 1
            continue

        rot = rng.randrange(4) if plan["rotatable"] else 0
        mirror = plan["rotatable"] and rng.randrange(2) == 0

        place_at, why = try_door_anchor(rng, plan["runs"], rot, mirror, anchor,
                                        centre, inner, outer, heading_index,
                                        used, spacing_sq)
        if place_at is None:
            if why == "heading":
                tally.no_door_heading += 1
            else:
                tally.too_close += 1
            continue

        used.append(place_at)
        tally.placed += 1
        if not counts_as_extra:
            progress_base[0] += 1
    return tally


def guarantee(rng, candidates, centre, inner, outer, junctions, road_cells,
              road_ends, heading_index, used, spacing_sq):
    """The 240-attempt set-piece paths. Returns (placed, heading_rejections)."""
    if not candidates:
        return False, 0
    plan = candidates[rng.randrange(len(candidates))]
    heading_rejects = 0
    for _ in range(GUARANTEE_ATTEMPTS):
        anchor = try_pick_anchor(rng, plan["anchor"], centre, inner, outer,
                                 junctions, road_cells, road_ends,
                                 used, spacing_sq)
        if anchor is None:
            continue
        rot = rng.randrange(4) if plan["rotatable"] else 0
        mirror = plan["rotatable"] and rng.randrange(2) == 0
        place_at, why = try_door_anchor(rng, plan["runs"], rot, mirror, anchor,
                                        centre, inner, outer, heading_index,
                                        used, spacing_sq)
        if place_at is None:
            if why == "heading":
                heading_rejects += 1
            continue
        used.append(place_at)
        return True, heading_rejects
    return False, heading_rejects


def run_floor(fi, seed, plans, stamped, dense_heading=True):
    site = SITES[fi]
    road_cfg = ROADS[fi]
    centre = (0, 0)
    radius = site["radius"]
    rng = random.Random(seed)

    road_usable = max(CORE_EXCLUSION + 4, radius - road_cfg["rim_margin"])
    _, roads = build_network(rng, centre, road_usable, road_cfg)
    junctions, anchors, heading_cells, ends = rebuild_road_anchors(roads)
    heading_index = bucket(heading_cells if dense_heading else anchors)

    usable = max(0, radius - site["rim_margin"])
    inner = max(CORE_EXCLUSION + 2, round(radius * site["band"][0]))
    outer = min(usable, round(radius * site["band"][1]))
    spacing_sq = site["spacing"] ** 2

    want = rng.randint(*site["sites"])
    holy_want = rng.randint(*site["holy"])

    general_pool = build_pool(rng, site["pool"], plans, stamped,
                              site["all_archetypes"])
    holy_pool = build_pool(rng, site["holy_pool"], plans, stamped, False)
    holy_pool = [p for p in holy_pool if p["general"]]

    used = []
    progress = [0]
    out = dict(want=want, holy_want=holy_want, vault=True, village=True,
               outpost=True, vault_heading=0, village_heading=0,
               outpost_heading=0)

    if site["vault"]:
        cands = [p for p in build_pool(rng, [13], plans, stamped, False)]
        out["vault"], out["vault_heading"] = guarantee(
            rng, cands, centre, inner, outer, junctions, anchors, ends,
            heading_index, used, spacing_sq)
    if site["village"]:
        cands = [p for p in build_pool(rng, [8], plans, stamped, False)]
        out["village"], out["village_heading"] = guarantee(
            rng, cands, centre, inner, outer, junctions, anchors, ends,
            heading_index, used, spacing_sq)
        if out["village"]:
            progress[0] += 1
    if site["outpost"]:
        cands = [p for p in general_pool
                 if p["archetype"] == site["outpost_archetype"]]
        cands.sort(key=lambda p: 0 if p["name"] else 1)
        out["outpost"], out["outpost_heading"] = guarantee(
            rng, cands, centre, inner, outer, junctions, anchors, ends,
            heading_index, used, spacing_sq)
        if out["outpost"]:
            progress[0] += 1

    holy = fill(rng, holy_pool, holy_want, HOLY_ATTEMPTS_PER_SITE, centre,
                inner, outer, junctions, anchors, ends, heading_index, used,
                spacing_sq, progress, True)

    general_pool = [p for p in general_pool if p["general"]]
    general = fill(rng, general_pool, want, GENERAL_ATTEMPTS_PER_SITE, centre,
                   inner, outer, junctions, anchors, ends, heading_index, used,
                   spacing_sq, progress, False)

    out["holy_placed"] = holy.placed
    out["holy_no_heading"] = holy.no_door_heading
    out["holy_too_close"] = holy.too_close
    out["general_placed"] = progress[0]
    out["general_no_heading"] = general.no_door_heading
    out["general_too_close"] = general.too_close
    out["heading_cells"] = len(heading_cells)
    out["anchor_cells"] = len(anchors)
    out["junctions"] = len(junctions)
    return out


# ---- reports --------------------------------------------------------------

def heading_table(plans, seeds):
    """The first question: how often does TryRoadHeading resolve at all, fed
    the stride-12 sample versus the full centreline?"""
    print("HEADING RESOLUTION -- TryRoadHeading at a sampled road anchor")
    print("  floor  centreline  stride-12   resolves(thinned)  resolves(dense)")
    for fi in (2, 3, 4):
        road_cfg = ROADS[fi]
        radius = SITES[fi]["radius"]
        thin_ok = dense_ok = trials = 0
        cl_total = an_total = 0
        for s in range(seeds):
            rng = random.Random(10_000 + s)
            usable = max(CORE_EXCLUSION + 4, radius - road_cfg["rim_margin"])
            _, roads = build_network(rng, (0, 0), usable, road_cfg)
            _, anchors, heading, _ = rebuild_road_anchors(roads)
            if not anchors:
                continue
            cl_total += len(heading)
            an_total += len(anchors)
            thin_idx, dense_idx = bucket(anchors), bucket(heading)
            for _ in range(60):
                at = anchors[rng.randrange(len(anchors))]
                trials += 1
                if try_road_heading(thin_idx, at) is not None:
                    thin_ok += 1
                if try_road_heading(dense_idx, at) is not None:
                    dense_ok += 1
        n = max(1, seeds)
        print(f"  {fi:5d}  {cl_total // n:10d}  {an_total // n:9d}   "
              f"{100.0 * thin_ok / max(1, trials):16.1f}%  "
              f"{100.0 * dense_ok / max(1, trials):14.1f}%")
    print()


def quota_table(plans, seeds):
    scenarios = [
        ("2a as specified (nothing stamped)", set()),
        ("+ the two WardChapels stamped", set(WARD_CHAPELS)),
        ("+ every road-anchored laned plan", road_anchored_laned(plans)),
    ]
    for label, stamped in scenarios:
        print(f"SCENARIO: {label}")
        if stamped:
            print("  stamped: " + ", ".join(sorted(stamped)))
        print("  floor  holy placed (min)      general placed (want)   "
              "guarantees   noDoorHeading")
        for fi in (2, 3, 4):
            site = SITES[fi]
            holy_min = site["holy"][0]
            runs = [run_floor(fi, 20_000 + s, plans, stamped)
                    for s in range(seeds)]
            hp = [r["holy_placed"] for r in runs]
            gp = [r["general_placed"] for r in runs]
            wanted = [r["want"] for r in runs]
            short = sum(1 for r in runs if r["holy_placed"] < holy_min)
            gshort = sum(1 for a, b in zip(gp, wanted) if a < b)
            vfail = sum(1 for r in runs if not r["vault"])
            hfail = sum(1 for r in runs if not r["village"])
            ofail = sum(1 for r in runs if not r["outpost"])
            gfail = vfail + hfail + ofail
            ndh = sum(r["holy_no_heading"] + r["general_no_heading"]
                      + r["vault_heading"] + r["village_heading"]
                      + r["outpost_heading"] for r in runs) / max(1, len(runs))
            print(f"  {fi:5d}  {sum(hp)/len(hp):5.2f} of {holy_min}  "
                  f"{short:3d} short   {sum(gp)/len(gp):5.2f} of "
                  f"{sum(wanted)/len(wanted):4.1f}  {gshort:3d} short   "
                  f"{gfail:3d} missed   {ndh:9.1f}/floor"
                  + (f"   [vault {vfail} village {hfail} outpost {ofail}]"
                     if gfail else ""))
        print()


def road_anchored_laned(plans):
    out = set()
    for stem, p in plans.items():
        if not p["lane"] or not p["runs"]:
            continue
        anchor = p["anchor_override"] or ANCHOR_FOR.get(p["archetype"], "Free")
        if anchor in ("AlongRoad", "Junction", "Crossing", "RoadEnd"):
            out.add(stem)
    return out


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    seeds = int(sys.argv[2]) if len(sys.argv) > 2 else 200
    plans = load_plans(root)
    print(f"Loaded {len(plans)} authored plans from {root}\n")
    heading_table(plans, max(20, seeds // 10))
    quota_table(plans, seeds)


if __name__ == "__main__":
    main()
