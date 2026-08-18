#!/usr/bin/env python3
"""
deliver_symbols_torch.py -- the 't' glyph in the plan authors' quick reference.

_SYMBOLS.txt is never parsed -- plans are an explicit TextAsset list on
AncientSiteProfile, not a folder scan -- so this is documentation only and
cannot break a build. It is also the file somebody actually has open while
drawing a plan, which is exactly why a glyph missing from it is a glyph nobody
uses. Canon 54 shipped the vocabulary; this puts it where it gets read.

Three edits:

  1. A 't' TORCH entry in GLYPHS, sitting after 'o' DECOR because the two are
     neighbours in every sense -- same ground, adjacent glyphs, easily confused
     in a dense plan. Written at the depth of the entries around it: what it is,
     what it does that decor does not, and the two things an author cannot
     control (the colour, and the dormant-until-claimed rule).

  2. A rule under THE RULES THAT CATCH PEOPLE OUT. Light in this game has no
     occlusion, which makes torch placement counter-intuitive in a specific
     way: against a wall is RIGHT and mid-floor is nearly useless. That is not
     a glyph fact, it is a lighting fact, and it belongs beside the drape.

  3. Canon 54's key-files list gains the file, so the next person to change the
     glyph vocabulary knows this reference exists and has to move with it.

Deliberately NOT recorded here: radius, target and halo figures. Entry 53's
rule is that the ladder lives in the accessors and the readout, and a tuned
number written into a text file nobody parses is a number that will be wrong
within a week.
"""

import os
import sys


def resolve_repo():
    if len(sys.argv) > 1:
        return os.path.abspath(sys.argv[1])
    env = os.environ.get("DCR_REPO")
    if env:
        return os.path.abspath(env)
    here = os.path.abspath(os.path.dirname(__file__))
    while True:
        if os.path.isdir(os.path.join(here, "Assets")) and \
           os.path.isfile(os.path.join(here, "Docs", "DESIGN_CANON.md")):
            return here
        parent = os.path.dirname(here)
        if parent == here:
            break
        here = parent
    return "/home/claude/DungeonCore"


REPO = resolve_repo()

SYMBOLS = "Assets/ScriptableObjects/Sites/Plans/_SYMBOLS.txt"
CANON = "Docs/DESIGN_CANON.md"

_files = {}
_originals = {}
_meta = {}


def fail(msg):
    sys.stderr.write("ABORT: %s\n" % msg)
    sys.stderr.write("Nothing was written. The tree is clean.\n")
    sys.exit(1)


def read(rel):
    if rel in _files:
        return _files[rel]
    path = os.path.join(REPO, rel)
    if not os.path.isfile(path):
        fail("missing file %s" % rel)
    with open(path, "rb") as fh:
        raw = fh.read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    if bom:
        raw = raw[3:]
    crlf = b"\r\n" in raw
    text = raw.decode("utf-8").replace("\r\n", "\n")
    _meta[rel] = (crlf, bom, text.endswith("\n"))
    _files[rel] = text
    _originals[rel] = text
    return text


def anchor(rel, needle, label):
    text = read(rel)
    n = text.count(needle)
    if n != 1:
        fail("anchor '%s' in %s matched %d times, expected exactly 1" % (label, rel, n))
    return text


def replace_once(rel, needle, repl, label):
    text = anchor(rel, needle, label)
    _files[rel] = text.replace(needle, repl, 1)


def check_ascii(name, text):
    for i, ch in enumerate(text):
        if ord(ch) > 126 or (ord(ch) < 32 and ch not in "\n\t"):
            line = text.count("\n", 0, i) + 1
            fail("non-ASCII 0x%04x in inserted %s at line %d" % (ord(ch), name, line))


def check_width(name, text, limit=79):
    """_SYMBOLS.txt is read in a monospace editor at a fixed width. Every line
    in the file sits inside 79 columns; an inserted block that does not would
    wrap and make the glyph table unreadable, which is the file's only job."""
    for i, line in enumerate(text.split("\n"), 1):
        if len(line) > limit:
            fail("inserted %s line %d is %d columns, over the %d the file keeps"
                 % (name, i, len(line), limit))


# ============================================================ GLYPH ENTRY

GLYPH_ANCHOR = """      AncientSiteProfile.siteDecor's piecePrefab; no art is wired yet.

  ' ' SPACE -- not part of the site at all."""

GLYPH_REPL = """      AncientSiteProfile.siteDecor's piecePrefab; no art is wired yet.

  t   TORCH. Floor plus a marker: a sconce or brazier the site was lit by.
      Rides the same placement transform as 'o', so torches rotate and
      mirror with the plan.

      A SEPARATE GLYPH, not an overload of 'o'. A plan may want both -- an
      ossuary with its bone piles AND its sconces -- and the two spawn
      different prefabs with different lifecycles: decor is inert dressing,
      a torch listens for a claim.

      TWO THINGS YOU DO NOT CHOOSE. The COLOUR comes from the archetype:
      cold blue on the Church seals and the vault, warm on every Buried Age
      site, warm gold on the living dwarven holds. That split is canon 21's
      line between the deep-faith's own ruins and the Church that sealed
      them, so do not try to work around it in a plan. The LIT STATE comes
      from the same place: everything spawns DORMANT and lights when the
      player claims that cell, except the dwarven village and the gatehouse,
      which start lit because somebody lives there.

      A drawn torch is therefore a reward for taking the place. It latches
      on EVER-claimed, so a breach recede never snuffs one.

      Wire the prefab on AncientSiteProfile.torchPrefab; no art is wired
      yet, and until it is, Commands / Log Point Lights reports the torch
      cells a floor declares against the zero it spawned and names the
      empty slot.

  ' ' SPACE -- not part of the site at all."""

# ============================================================ RULE ENTRY

RULE_ANCHOR = """A WALL RECESS IS THREE DEEP. Two leaves a pinched run that seals under the
drape.
"""

RULE_REPL = """A WALL RECESS IS THREE DEEP. Two leaves a pinched run that seals under the
drape.

LIGHT HAS NO OCCLUSION, so a torch AGAINST A WALL is right and one stranded
mid-floor is nearly useless. The lit patch is a plain disc a few cells across
that brightens whatever it covers, walls included -- which is correct, because
stone beside a flame IS lit. What it will not do is pick out a room's shape
from the middle of the floor. Put them where a torch actually was: flanking a
door, along a nave, at the corners of a hall, either side of the heart. And do
not carpet the place -- the discs overlap fast, and a dozen in one hall is
twelve objects buying one visual.
"""

# ============================================================ CANON

CANON_ANCHOR = """`Assets/Editor/SitePlanPreviewWindow.cs` (the `t` swatch),
`Assets/Scripts/TESTING/Commands.cs`."""

CANON_REPL = """`Assets/Editor/SitePlanPreviewWindow.cs` (the `t` swatch),
`Assets/Scripts/TESTING/Commands.cs`,
`Assets/ScriptableObjects/Sites/Plans/_SYMBOLS.txt` (the authors' quick
reference -- never parsed, and therefore easy to leave behind; anything that
changes the glyph vocabulary moves it too)."""


def main():
    if not os.path.isdir(os.path.join(REPO, "Assets")):
        fail("no Assets/ under %s -- wrong repo path" % REPO)

    if "t   TORCH" in read(SYMBOLS):
        fail("_SYMBOLS.txt already documents the 't' glyph -- already applied.")
    if "_SYMBOLS.txt` (the authors' quick" in read(CANON):
        fail("canon 54 already lists _SYMBOLS.txt -- already applied.")
    if "## 54." not in read(CANON):
        fail("canon has no entry 54 -- apply and push the stage 2 script first.")

    check_ascii("glyph entry", GLYPH_REPL)
    check_ascii("rule entry", RULE_REPL)
    check_ascii("canon key files", CANON_REPL)
    check_width("glyph entry", GLYPH_REPL)
    check_width("rule entry", RULE_REPL)

    anchor(SYMBOLS, GLYPH_ANCHOR, "decor tail into space glyph")
    anchor(SYMBOLS, RULE_ANCHOR, "wall recess rule")
    anchor(CANON, CANON_ANCHOR, "canon 54 key files tail")

    replace_once(SYMBOLS, GLYPH_ANCHOR, GLYPH_REPL, "decor tail into space glyph")
    replace_once(SYMBOLS, RULE_ANCHOR, RULE_REPL, "wall recess rule")
    replace_once(CANON, CANON_ANCHOR, CANON_REPL, "canon 54 key files tail")

    # The glyph table's ordering is load-bearing for a reader scanning it: the
    # new entry must sit between 'o' and the space entry, not somewhere else.
    body = _files[SYMBOLS]
    if not (body.index("  o   DECOR") < body.index("  t   TORCH") < body.index("  ' ' SPACE")):
        fail("the 't' entry did not land between 'o' and the space glyph")

    # Whole-file width check, against the width the file ALREADY keeps rather
    # than against a number this script invented. One pre-existing line runs to
    # 80 columns; failing the delivery over somebody else's line would be a
    # validator with an opinion instead of a check. The test that matters is
    # that the edit does not make it worse.
    was = max(len(l) for l in _originals[SYMBOLS].split("\n"))
    now = max(len(l) for l in body.split("\n"))
    if now > was:
        fail("the edit widened _SYMBOLS.txt from %d to %d columns" % (was, now))

    for rel, text in _files.items():
        crlf, bom, trailing = _meta[rel]
        out = text
        if trailing and not out.endswith("\n"):
            out += "\n"
        if not trailing and out.endswith("\n"):
            out = out[:-1]
        if crlf:
            out = out.replace("\n", "\r\n")
        data = out.encode("utf-8")
        if bom:
            data = b"\xef\xbb\xbf" + data
        with open(os.path.join(REPO, rel), "wb") as fh:
            fh.write(data)

    lines = []
    lines.append("APPLIED: the 't' glyph in the plan authors' quick reference.")
    lines.append("repo: %s" % REPO)
    lines.append("")
    lines.append("  %s" % SYMBOLS)
    lines.append("      + 't' TORCH entry, between 'o' and the space glyph")
    lines.append("      + a rule: light has no occlusion, so torches go against walls")
    lines.append("  %s" % CANON)
    lines.append("      + entry 54 key files gains _SYMBOLS.txt, with why it is")
    lines.append("        easy to leave behind")
    lines.append("")
    lines.append("Documentation only -- this file is never parsed, so nothing here")
    lines.append("can change what a plan does. No tuned figures recorded.")
    sys.stdout.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
