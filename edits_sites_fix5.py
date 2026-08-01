#!/usr/bin/env python3
"""
Sites: restore the reveal halo.

Run from the repo root, AFTER edits_sites_fix4.py:
    python3 edits_sites_fix5.py

WHAT WENT WRONG
  fix4 correctly stopped revealing masonry the wall renderer never paints --
  the buried cells inside a thick wall, which showed as bare floor tile. But it
  removed the one-cell reveal HALO at the same time, and the halo was never the
  problem.

  Two independent things decide whether a cell looks like a wall:
    PAINTED  -- CaveWallRenderer gives a solid cell a cap and face when it is
                claimed or 8-adjacent to a MINED cell.
    REVEALED -- the fog tile over it has been cleared.
  A cell needs BOTH. Painted but fogged is invisible; revealed but unpainted is
  bare floor.

  Site masonry only ever borders carved floor on its skin, so the deep cells are
  never painted -- that was the void bug. But the natural rock AROUND a site IS
  8-adjacent to the carved floor, so it IS painted, and fix4 left it fogged.
  Measured across the 24 plans at span 34: 683 wall cells painted and never
  unfogged. That is the missing exterior perimeter.

THE RULE, AND IT IS EXACT
  Reveal the carved cells and their eight-neighbourhood. Nothing else.
  That set is precisely {mined floor} + {every cell the renderer will paint}:
    - painted but still fogged ... 0
    - revealed but never painted . 0
  The masonry skin is a subset of the halo, so the separate skin pass is now
  redundant and is removed with it.
"""

import os
import sys

ROOT = os.getcwd()
EDITS = []
_cache = {}
_eol_crlf = {}
_had_bom = {}


def fail(msg):
    print("ABORT: " + msg)
    sys.exit(1)


def read(path):
    if path not in _cache:
        full = os.path.join(ROOT, path)
        if not os.path.exists(full):
            fail("missing file: " + path)
        with open(full, 'rb') as fh:
            raw = fh.read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        if bom:
            raw = raw[3:]
        text = raw.decode('utf-8')
        _had_bom[path] = bom
        _eol_crlf[path] = '\r\n' in text
        _cache[path] = text.replace('\r\n', '\n')
    return _cache[path]


def encode_for(path, text):
    out = text.replace('\r\n', '\n')
    if _eol_crlf.get(path):
        out = out.replace('\n', '\r\n')
    data = out.encode('utf-8')
    if _had_bom.get(path):
        data = b'\xef\xbb\xbf' + data
    return data


def edit(path, old, new, label):
    src = read(path)
    n = src.count(old)
    if n != 1:
        hint = ""
        if n == 0 and old.replace(' ', '') in src.replace(' ', ''):
            hint = "\n       (text present, whitespace differs)"
        fail("anchor [%s] in %s matched %d times, expected 1%s" % (label, path, n, hint))
    EDITS.append((path, old, new, label))


GEN = "Assets/Scripts/Floors/TerrainFeatureGenerator.cs"

if not os.path.exists(os.path.join(ROOT, GEN)):
    fail("run from the repo root")
if "No border halo" not in read(GEN):
    fail("edits_sites_fix4.py has not been applied to this tree.\n"
         "       Run it first, then this script.")


# ------------------------------------------------- the reveal rule

edit(GEN,
     """        // No border halo. RevealWithBorder lights the ring around a chamber so its
        // rock frames it, but a site's ring IS its masonry, and halo-revealing it
        // would undo the skin-only rule below and put the bare-floor slabs back.
        foreach (var sv in site.cells) terrain.RevealTile(sv.ToVector3Int());""",
     """        // Carved floor plus its one-cell halo, and NOTHING else. Two separate
        // things decide whether a cell reads as a wall:
        //
        //   PAINTED  -- CaveWallRenderer caps and faces a solid cell when it is
        //               claimed or 8-adjacent to a MINED cell.
        //   REVEALED -- the fog over it has been cleared.
        //
        // A cell needs both. Revealed but unpainted shows the bare floor tile
        // underneath; painted but fogged is simply invisible, which is what left
        // sites with open floor and no wall attached to it.
        //
        // The halo is EXACTLY the set the renderer will paint, because "painted"
        // is defined as 8-adjacency to mined floor and the carved cells are the
        // mined floor. So this reveals every wall cell and not one cell more:
        // measured over the 24 plans, zero painted-but-fogged and zero
        // revealed-but-unpainted. The masonry skin is a subset of the halo, which
        // is why the separate skin pass that used to sit below is gone.
        //
        // Deeper masonry stays dark, exactly like the unexcavated rock it is drawn
        // as, and mining through the skin reveals the next layer by the ordinary
        // route.
        RevealWithBorder(terrain, site.cells);""",
     "reveal: restore the halo")

edit(GEN,
     """        // Masonry is revealed but never opened: the player sees the wall, and
        // mining it is a deliberate act that pays out ancient_masonry.
        //
        // Only the SKIN is revealed -- masonry that touches this site's carved
        // floor. CaveWallRenderer paints a solid cell only when it is claimed or
        // 8-adjacent to a MINED cell, and site masonry is never mined, so a cell
        // buried inside a thick wall is never painted at all. Revealing it anyway
        // stripped its fog and left bare floor tile showing where a wall should
        // be. Fog is one-way, so there is no correcting that afterwards; the cells
        // simply must not be revealed. Everything deeper stays dark, exactly like
        // the unexcavated rock it is drawn as, and mining through the skin reveals
        // the next layer by the ordinary route.
        if (site.ruinsCells != null)
        {
            var carved = new HashSet<Vector3Int>();
            foreach (var sv in site.cells) carved.Add(sv.ToVector3Int());

            foreach (var sv in site.ruinsCells)
            {
                var c = sv.ToVector3Int();
                if (!TouchesAny(c, carved)) continue;
                terrain.RevealTile(c);
            }
        }

""",
     """        // Masonry needs no pass of its own. The skin -- the only masonry the
        // renderer ever paints -- is already inside the halo above, and anything
        // deeper must stay fogged or it shows as bare floor.

""",
     "reveal: drop the redundant skin pass")

edit(GEN,
     """    /// <summary>True when any of the eight neighbours is in the set. Used to find
    /// the masonry SKIN of a site: the only masonry the wall renderer will ever
    /// paint, because it is the only masonry touching open floor.</summary>
    private static bool TouchesAny(Vector3Int cell, HashSet<Vector3Int> set)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (set.Contains(new Vector3Int(cell.x + dx, cell.y + dy, cell.z))) return true;
            }
        return false;
    }

""",
     "",
     "reveal: remove now-unused helper")


# ------------------------------------------------- canon

CANON = "Docs/DESIGN_CANON.md"

edit(CANON,
     """**Only the masonry SKIN is revealed.** `CaveWallRenderer` paints a solid cell
only when it is claimed or 8-adjacent to a MINED cell, and site masonry is
never mined -- so masonry buried inside a thick wall is never painted at all.
Revealing it regardless stripped its fog and left bare floor tile showing where
a wall should be, which is what the first build shipped. Fog is one-way, so
there is no correcting it after the fact; those cells must simply not be
revealed. Sites therefore reveal their carved floor and only the masonry
touching it, with no border halo -- the halo exists to frame a chamber in its
surrounding rock, and a site brings its own walls. **Any future feature that
reveals solid cells must check the same thing: revealing rock the wall renderer
will not paint shows the floor tile underneath it.**""",
     """**Reveal is the carved floor plus its one-cell halo, and nothing else.** Two
independent things decide whether a cell reads as a wall. PAINTED:
`CaveWallRenderer` caps and faces a solid cell when it is claimed or 8-adjacent
to a MINED cell. REVEALED: its fog is cleared. A cell needs BOTH, and each
failure mode has its own symptom -- revealed but unpainted shows the bare floor
tile underneath, painted but fogged is invisible.

Sites hit both in turn. The first build revealed every masonry cell, including
the ones buried inside a thick wall that border no open floor and are therefore
never painted: 726 cells of bare floor slab across the roster. The correction
then removed the halo as well, which fogged the natural rock around each site
-- rock that IS 8-adjacent to the carved floor and IS painted: 683 wall cells
rendered and never unfogged, so sites had open floor with no wall attached.

The halo alone is exactly right, because "painted" is defined as 8-adjacency to
mined floor and the carved cells are the mined floor. Measured over the roster:
zero painted-but-fogged, zero revealed-but-unpainted. The masonry skin is a
subset of the halo, so it needs no pass of its own. **Any future feature that
reveals solid cells must satisfy the same invariant: reveal exactly the cells
the wall renderer will paint, no more and no fewer.** Fog is one-way, so
neither error can be corrected after the fact.""",
     "canon: reveal invariant")


# ------------------------------------------------- apply

def brace_balance(text):
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            while i < n and text[i] != '\n':
                i += 1
        elif c == '/' and i + 1 < n and text[i + 1] == '*':
            i += 2
            while i + 1 < n and not (text[i] == '*' and text[i + 1] == '/'):
                i += 1
            i += 2
        elif c in '"\'':
            q = c
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == q:
                    i += 1
                    break
                i += 1
        else:
            out.append(c)
            i += 1
    code = ''.join(out)
    bad = []
    for o, cl, name in (('{', '}', 'brace'), ('(', ')', 'paren'), ('[', ']', 'bracket')):
        d = 0
        for ch in code:
            if ch == o:
                d += 1
            elif ch == cl:
                d -= 1
                if d < 0:
                    bad.append(name + " closes early")
                    break
        if d != 0 and not bad:
            bad.append("%s imbalance (%d)" % (name, d))
    return bad


staged = {}
for path, old, new, label in EDITS:
    src = staged.get(path, read(path))
    staged[path] = src.replace(old, new, 1)

problems = []
for path, text in staged.items():
    if path.endswith('.cs'):
        problems += ["%s: %s" % (path, p) for p in brace_balance(text)]
    if "TouchesAny(" in text:
        problems.append("%s: TouchesAny still referenced after removal" % path)
for path, _o, new_text, label in EDITS:
    if path.endswith('.md'):
        continue
    for ch in new_text:
        if ord(ch) > 127:
            problems.append("%s [%s]: non-ASCII U+%04X" % (path, label, ord(ch)))
            break

print("Sites: restore the reveal halo")
print("  %d edits across %d files" % (len(EDITS), len(staged)))

if problems:
    print("VALIDATION FAILED -- nothing written:")
    for p in problems:
        print("  " + p)
    sys.exit(1)

written = []
for path, text in staged.items():
    with open(os.path.join(ROOT, path), 'wb') as fh:
        fh.write(encode_for(path, text))
    written.append("  ~ " + path)
for line in written:
    print(line)
print("Done. Sites now reveal carved floor plus a one-cell wall ring.")
