#!/usr/bin/env python3
"""
DCR -- BAND CONNECTIVITY: THE LIGHTER SQUARES ALONG THE EDGE

BuildRoadFeatherBand walked the road with Orth4 -- 4-connectivity -- while
ApplyRoadFogFade collects its fade set from the 8-neighbourhood. A road cell
joined to the band only DIAGONALLY (common at a carriageway's edge and at every
bend) therefore had its fog thinned by its diagonal neighbour's depth while the
walk never reached it. No band entry means no baseLight entry, which means the
shadow never darkened it, so it rendered as a bright road tile: the lighter
squares strung along the band's edge in screenshot 116.

Two changes, one fix and one guard:

  - The walk uses the 8-neighbourhood, matching the set the fade actually
    covers. Anything the fade can light, the walk now reaches and lights.
  - ApplyRoadFogFade refuses to thin fog over a ROAD cell that is not in the
    band. If the two ever disagree again the road stays dark rather than
    rendering undarkened, which is the failure mode worth defaulting to.

Run from the repo root:  python3 fix_band_connectivity.py
"""

import os
import sys

REPO = os.getcwd()

FEATGEN = "Assets/Scripts/Floors/TerrainFeatureGenerator.cs"
CANON   = "Docs/DESIGN_CANON.md"


def read(rel):
    path = os.path.join(REPO, rel)
    if not os.path.isfile(path):
        sys.exit("MISSING FILE: %s" % rel)
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    crlf = b"\r\n" in raw
    return raw.decode("utf-8").replace("\r\n", "\n"), bom, crlf


def write(rel, text, bom, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    open(os.path.join(REPO, rel), "wb").write(data)


def sub(text, old, new, label):
    n = text.count(old)
    if n != 1:
        sys.exit("ANCHOR FAIL [%s]: expected 1 occurrence, found %d" % (label, n))
    return text.replace(old, new, 1)


def assert_ascii(s, label):
    for k, ch in enumerate(s):
        if ord(ch) > 127:
            sys.exit("NON-ASCII in inserted text [%s] at %d: %r" % (label, k, ch))


def assert_balanced(text, label):
    pairs = {"{": "}", "(": ")", "[": "]"}
    counts = {k: 0 for k in pairs}
    lc = bc = st = ch_ = vb = False
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if lc:
            if c == "\n":
                lc = False
        elif bc:
            if c == "*" and nxt == "/":
                bc = False
                i += 1
        elif vb:
            if c == '"':
                if nxt == '"':
                    i += 1
                else:
                    vb = False
        elif st:
            if c == "\\":
                i += 1
            elif c == '"':
                st = False
        elif ch_:
            if c == "\\":
                i += 1
            elif c == "'":
                ch_ = False
        else:
            if c == "/" and nxt == "/":
                lc = True
                i += 1
            elif c == "/" and nxt == "*":
                bc = True
                i += 1
            elif c == "@" and nxt == '"':
                vb = True
                i += 1
            elif c == '"':
                st = True
            elif c == "'":
                ch_ = True
            elif c in pairs:
                counts[c] += 1
            elif c in pairs.values():
                for k, v in pairs.items():
                    if v == c:
                        counts[k] -= 1
                        if counts[k] < 0:
                            sys.exit("UNBALANCED '%s' in %s" % (c, label))
        i += 1
    for k, v in counts.items():
        if v != 0:
            sys.exit("UNBALANCED '%s' in %s (net %d)" % (k, label, v))


# ---------------------------------------------------------------- edits

BFS_OLD = """            if (d >= roadPrepareCells) continue;
            for (int k = 0; k < 4; k++)
            {
                var n = cur + Orth4[k];
                if (!seen.Add(n)) continue;
                if (GetFeatureAt(n) != FeatureType.Road) continue;
                depth[n] = d + 1;
                roadFeatherDepth[n] = d + 1;
                queue.Enqueue(n);
            }
"""

BFS_NEW = """            if (d >= roadPrepareCells) continue;

            // EIGHT-neighbour, matching the set ApplyRoadFogFade lights. Walking
            // with Orth4 while the fade collected from the 8-neighbourhood left
            // diagonally-joined road cells -- the edge of a carriageway, every
            // bend -- lit by a neighbour's depth but absent from the band, so the
            // shadow never darkened them and they rendered as bright squares
            // along the edge. Whatever the fade can light, the walk must reach.
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector3Int(cur.x + dx, cur.y + dy, cur.z);
                    if (!seen.Add(n)) continue;
                    if (GetFeatureAt(n) != FeatureType.Road) continue;
                    depth[n] = d + 1;
                    roadFeatherDepth[n] = d + 1;
                    queue.Enqueue(n);
                }
"""

GUARD_OLD = """                    var c = new Vector3Int(kv.Key.x + dx, kv.Key.y + dy, kv.Key.z);
                    if (fog.GetTile(c) == null) continue;   // already revealed
                    fadeScratch.Add(c);
"""

GUARD_NEW = """                    var c = new Vector3Int(kv.Key.x + dx, kv.Key.y + dy, kv.Key.z);
                    if (fog.GetTile(c) == null) continue;   // already revealed

                    // Never thin fog over ROAD the band does not cover. Road is
                    // exempt from IsSolid, so it gets no wall cap, and it is only
                    // darkened if the band put it in the light map -- an
                    // unprepared road cell shown through thin fog is a bright
                    // square. If the walk and the fade ever disagree again, this
                    // fails to DARK, which is the right way round to fail.
                    if (!roadFeatherDepth.ContainsKey(c)
                        && GetFeatureAt(c) == FeatureType.Road) continue;

                    fadeScratch.Add(c);
"""

CANON_OLD = """caps need no special handling; the cap pass snapshots `baseLight`'s keys."""

CANON_NEW = """caps need no special handling; the cap pass snapshots `baseLight`'s keys.

The band walk and the fade must share a CONNECTIVITY. The walk ran on four
neighbours while the fade collected from eight, so road joined to the band only
diagonally -- a carriageway's edge, every bend -- was lit by a neighbour's depth
yet absent from the band, and the shadow never darkened it. Both are eight now,
and the fade additionally refuses to thin over road the band does not cover, so
a future disagreement fails to dark rather than to bright."""


def main():
    guard, _, _ = read(FEATGEN)
    if "EIGHT-neighbour, matching the set" in guard:
        sys.exit("ALREADY APPLIED. Aborting.")
    if "roadFeatherDepth" not in guard:
        sys.exit("PRECONDITION FAIL: prepared band not present.")

    staged = {}

    t, bom, crlf = read(FEATGEN)
    t = sub(t, BFS_OLD, BFS_NEW, "band walk connectivity")
    t = sub(t, GUARD_OLD, GUARD_NEW, "fade unprepared-road guard")
    assert_balanced(t, FEATGEN)
    staged[FEATGEN] = (t, bom, crlf)

    t, bom, crlf = read(CANON)
    t = sub(t, CANON_OLD, CANON_NEW, "canon connectivity note")
    staged[CANON] = (t, bom, crlf)

    for label, blob in (("bfs", BFS_NEW), ("guard", GUARD_NEW), ("canon", CANON_NEW)):
        assert_ascii(blob, label)

    for rel, (text, bom, crlf) in staged.items():
        write(rel, text, bom, crlf)

    print("\n".join([
        "BAND CONNECTIVITY FIXED",
        "",
        "  %s" % FEATGEN,
        "      - band walk now 8-neighbour, matching the fade set",
        "      - fade refuses unprepared road: fails to dark, not to bright",
        "",
        "  %s" % CANON,
        "      - entry 19: the walk and the fade share a connectivity",
        "",
        "TEST: the lighter squares along the band edge should be gone. The band",
        "      widens very slightly at bends, which is the point.",
    ]))


if __name__ == "__main__":
    main()
