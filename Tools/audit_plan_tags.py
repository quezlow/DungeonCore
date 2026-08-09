#!/usr/bin/env python3
# audit_plan_tags.py -- header/tag audit over every authored site plan.
# Transcribes AncientSitePlanLibrary's header handling branch-for-branch:
# same key set, same value spellings, same silent-skip behaviours -- which is
# the point, since the silent skips are what this audit exists to catch.
import os, re, sys

PLANS = "Assets/ScriptableObjects/Sites/Plans"

ARCHETYPES = ["SunkenPlaza","CollapsedArchive","Ossuary","BrokenAqueduct",
              "HollowSanctum","SealedGate","GuardPost","TollHouse",
              "DwarvenVillage","ChurchSeal","SealedCrypt","WardChapel",
              "BlessedSpring","DeadCoreVault"]
ANCHORS = ["Free","Junction","AlongRoad","RoadEnd","Crossing"]
ANCHOR_FOR = {  # AncientSiteProfile.AnchorFor, default Free
    "SunkenPlaza":"Junction","CollapsedArchive":"AlongRoad",
    "BrokenAqueduct":"Crossing","SealedGate":"RoadEnd","GuardPost":"AlongRoad",
    "TollHouse":"AlongRoad","DwarvenVillage":"AlongRoad"}
KNOWN_KEYS = {"archetype","name","anchor","rotate","general",
              "anchor_required","anchor_on","doors"}
BOOLISH = {"yes","no","true","false","0","1"}
GLYPHS = set("#.=^+~X ")

def canon(s):  # the parser's Replace(" ","").Replace("_","").lower()
    return s.replace(" ","").replace("_","").lower()

ARCH_LOOKUP = {canon(a): a for a in ARCHETYPES}
ANCH_LOOKUP = {canon(a): a for a in ANCHORS}

def audit(path):
    name = os.path.basename(path)[:-4]
    text = open(path, "rb").read().replace(b"\r\n", b"\n").decode("utf-8")
    hard, warn, info = [], [], []
    tags = {}
    rotate_no_line = -1
    comments = []
    rows = []
    for ln, raw in enumerate(text.split("\n"), 1):
        t = raw.strip()
        if t.startswith("//"):
            comments.append((ln, t))
            continue
        if t.startswith("@"):
            colon = t.find(":")
            if colon < 0:
                hard.append("line %d: '@' line with no colon -- the parser "
                            "SILENTLY skips it: %r" % (ln, t))
                continue
            key = t[1:colon].strip().lower()
            val = t[colon+1:].strip()
            if key not in KNOWN_KEYS:
                hard.append("line %d: unknown key '@%s:' -- the parser "
                            "SILENTLY ignores it (no default case)" % (ln, key))
                continue
            if key in tags:
                warn.append("line %d: '@%s:' repeated; last one wins" % (ln, key))
            tags[key] = val
            if key == "archetype" and canon(val) not in ARCH_LOOKUP:
                hard.append("line %d: unknown @archetype '%s' -- plan will "
                            "fail to load" % (ln, val))
            if key == "anchor" and canon(val) not in ANCH_LOOKUP:
                hard.append("line %d: unknown @anchor '%s' -- plan will "
                            "fail to load" % (ln, val))
            if key == "anchor_on" and val.lower() not in ("door","centre","center"):
                hard.append("line %d: unknown @anchor_on '%s' -- plan will "
                            "fail to load" % (ln, val))
            if key == "doors" and val.lower() not in ("unmarked","none","marked"):
                hard.append("line %d: unknown @doors '%s' -- plan will "
                            "fail to load" % (ln, val))
            if key in ("rotate","general","anchor_required"):
                if val.lower() not in BOOLISH:
                    warn.append("line %d: '@%s: %s' -- anything but "
                                "no/false/0 silently means YES" % (ln, key, val))
                if key == "rotate" and val.lower() in ("no","false","0"):
                    rotate_no_line = ln
            continue
        if t == "" and not rows:
            continue
        rows.append((ln, raw))
    while rows and rows[-1][1].strip() == "":
        rows.pop()

    # Grid scan.
    glyph_counts = {}
    for ln, raw in rows:
        for ch in raw:
            glyph_counts[ch] = glyph_counts.get(ch, 0) + 1
            if ch not in GLYPHS:
                hard.append("line %d: unknown glyph %r in grid -- the parser "
                            "treats it as ROCK, silently" % (ln, ch))
    doors = glyph_counts.get("+", 0)
    lane = glyph_counts.get("~", 0)
    hearts = glyph_counts.get("X", 0)

    # Required headers.
    if "archetype" not in tags:
        hard.append("missing @archetype -- plan will fail to load")
    if "doors" not in tags:
        hard.append("missing @doors -- Validate Site Plans FAILS it "
                    "(unmarked / none / marked)")
    if "name" not in tags:
        info.append("no @name; display name falls back to the file name")
    if hearts > 1:
        hard.append("%d heart cells; the parser allows exactly one" % hearts)

    # Consistency.
    arch = ARCH_LOOKUP.get(canon(tags.get("archetype","")), None)
    if arch and not name.startswith(arch + "_"):
        info.append("file prefix does not match @archetype '%s'" % arch)
    dp = tags.get("doors","").lower()
    if dp == "none" and doors > 0:
        hard.append("@doors: none but the grid draws %d '+' cell(s)" % doors)
    if dp == "marked" and doors == 0:
        hard.append("@doors: marked but the grid draws no '+' cells")
    if dp == "unmarked" and doors > 0:
        warn.append("UPGRADE: @doors: unmarked but the grid draws %d '+' "
                    "cell(s) -- it has been annotated in fact; stamping it "
                    "'marked' turns the three-cell door rule ON for it" % doors)

    # Engine-truth classification.
    eff_anchor = None
    if canon(tags.get("anchor","")) in ANCH_LOOKUP:
        eff_anchor = ANCH_LOOKUP[canon(tags["anchor"])]
    elif arch:
        eff_anchor = ANCHOR_FOR.get(arch, "Free")
    anchor_on_door = tags.get("anchor_on","").lower() == "door"
    if doors > 0 and lane == 0 and not anchor_on_door:
        warn.append("INERT DOORS: '+' drawn but no lane and no @anchor_on: "
                    "door -- doorAnchors stays empty, the plan takes the "
                    "DOORLESS path and sidles clear of any road")
    if lane > 0 and eff_anchor == "Free":
        warn.append("DECORATIVE LANE: laned but effective anchor is Free "
                    "(%s) -- it never samples a chord in game, so the lane "
                    "never carries a road" %
                    ("explicit @anchor" if "anchor" in tags else
                     "archetype default"))
    if rotate_no_line > 0:
        near = [c for l, c in comments if abs(l - rotate_no_line) <= 3]
        why = " ".join(c for _, c in comments)
        if not re.search(r"rotat|orient|decor|facing|north|drape", why,
                         re.IGNORECASE):
            warn.append("@rotate: no with no comment saying WHY anywhere in "
                        "the file -- the next author cannot tell load-bearing "
                        "from leftover")

    return name, tags, eff_anchor, doors, lane, hearts, hard, warn, info

def main():
    files = sorted(f for f in os.listdir(PLANS)
                   if f.endswith(".txt") and not f.startswith("_"))
    n_hard = n_warn = 0
    for f in files:
        name, tags, eff, doors, lane, hearts, hard, warn, info = \
            audit(os.path.join(PLANS, f))
        cls = ("laned" if lane and doors else
               "doored" if doors else "doorless")
        line = "%-40s %-16s doors=%-3d lane=%-4d heart=%d anchor=%s" % (
            name, "[" + cls + "]", doors, lane, hearts, eff)
        flags = []
        if hard: flags.append("%d HARD" % len(hard))
        if warn: flags.append("%d warn" % len(warn))
        print(line + ("   <-- " + ", ".join(flags) if flags else ""))
        for h in hard: print("      HARD: " + h)
        for w in warn: print("      warn: " + w)
        for i in info: print("      info: " + i)
        n_hard += len(hard); n_warn += len(warn)
    print("\n%d plans, %d hard faults, %d warnings" %
          (len(files), n_hard, n_warn))

if __name__ == "__main__":
    main()
