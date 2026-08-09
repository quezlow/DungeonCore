#!/usr/bin/env python3
# Headless model of the multi-floor severance flood (canon 36 follow-up).
# Floors are small grids; walls are solid cells; stairs are same-cell links
# between adjacent floors (the HandleStairTraversal pairing rule). The flood
# seeds the CORE floor at the heart and spreads through any stair whose cell
# is reachable, in both directions. Verdict: entrance cell in floor 0's set.
# Mirrors ReachabilityDirector.Recompute; re-run when the hop rule changes.

def flood(grid, solid, start):
    W, H = grid
    if start in solid: return set()
    seen, q = {start}, [start]
    while q:
        x, y = q.pop()
        for nx, ny in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
            if 0 <= nx < W and 0 <= ny < H and (nx,ny) not in solid and (nx,ny) not in seen:
                seen.add((nx,ny)); q.append((nx,ny))
    return seen

def multi_floor_reach(floors, stairs, core_floor, core_cell):
    reach = {i: set() for i in floors}
    work = [(core_floor, core_cell)]
    while work:
        f, seed = work.pop()
        if seed in reach[f]: continue
        grid, solid = floors[f]
        reach[f] |= flood(grid, solid, seed)
        for a, b, cell in stairs:
            for src, dst in ((a,b),(b,a)):
                if src == f and cell in reach[f] and dst in floors and cell not in reach[dst]:
                    work.append((dst, cell))
    return reach

G = (10, 10)
def wall_col(x, y0, y1): return {(x, y) for y in range(y0, y1+1)}

floors = {0: (G, set()), 1: (G, set())}
stairs = [(0, 1, (5, 5))]
r = multi_floor_reach(floors, stairs, 1, (2, 2))
assert (0, 0) in r[0], "A: entrance should be reachable"

floors = {0: (G, wall_col(3, 0, 9)), 1: (G, set())}
r = multi_floor_reach(floors, stairs, 1, (2, 2))
assert (0, 0) not in r[0], "B: severed on floor 0"
assert (5, 5) in r[0], "B: stair side of floor 0 still joined to the heart"

floors = {0: (G, set()), 1: (G, wall_col(4, 0, 9))}
r = multi_floor_reach(floors, stairs, 1, (2, 2))
assert (0, 0) not in r[0] and len(r[0]) == 0, "C: floor 0 gets no seed at all"

floors = {0: (G, set()), 1: (G, wall_col(4, 0, 8))}
stairs2 = [(0, 1, (5, 5)), (0, 1, (1, 1))]
r = multi_floor_reach(floors, stairs2, 1, (2, 2))
assert (0, 0) in r[0], "D: second stair carries the route"

floors = {0: (G, wall_col(3, 0, 9))}
r = multi_floor_reach(floors, [], 0, (5, 5))
assert (0, 0) not in r[0], "E: baseline severed"
r = multi_floor_reach({0: (G, set())}, [], 0, (5, 5))
assert (0, 0) in r[0], "E2: baseline open"

floors = {0: (G, set()), 1: (G, wall_col(6, 0, 9)), 2: (G, set())}
stairs3 = [(0, 1, (8, 5)), (1, 2, (2, 5))]
r = multi_floor_reach(floors, stairs3, 2, (1, 1))
assert (0, 0) not in r[0], "F: middle floor wall severs"
floors = {0: (G, set()), 1: (G, wall_col(6, 0, 8)), 2: (G, set())}
r = multi_floor_reach(floors, stairs3, 2, (1, 1))
assert (0, 0) in r[0], "F2: gap restores the route"

floors = {0: (G, wall_col(5, 0, 9)), 1: (G, set())}
stairsG = [(0, 1, (2, 2)), (0, 1, (8, 8))]
r = multi_floor_reach(floors, stairsG, 1, (4, 4))
assert (0, 0) in r[0] and (9, 9) in r[0], "G: both pockets joined via the web"

print("all cross-floor severance cases pass")
