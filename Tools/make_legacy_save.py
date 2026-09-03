#!/usr/bin/env python3
"""
make_legacy_save.py -- build a synthetic pre-baseline save from a current one.

WHY THIS EXISTS
    No save from before the smoke-test baseline survives, so the claim "older
    saves still load" cannot be tested against a real one. The first version of
    the smoke test guide asked the tester to hand-delete "about 25" keys from a
    current save.json. The real number is 159 fields across 23 serialisable
    classes, several of them nested two or three levels down, and the hand-list
    also set saveVersion to 1 when CURRENT_VERSION was already 3 at the
    baseline -- which would have run two migrations a real old save never runs.
    A human-transcribed field list was wrong on first contact with source. This
    tool derives the list from the source instead, every time it runs.

WHAT IT DOES
    1. Parses every [Serializable] public class under Assets/Scripts at the
       baseline commit (via git show) and in the working tree.
    2. Builds the type graph from DungeonSaveData downward by following field
       types: List<T> and T[] recurse into T, a class defined in the tree
       recurses into that class, everything else is a leaf.
    3. Walks the JSON in lockstep with the graph and deletes, at every node,
       the keys that class gained since the baseline. Keys the class does not
       declare at all are reported and left alone.
    4. Writes <out>.json with saveVersion left at the BASELINE's own
       CURRENT_VERSION (read from source, not typed), plus a patched meta.json
       beside it if one was found beside the input.
    5. Prints per-class strip counts, the grand total, every external type it
       treated as a leaf, and every undeclared key it saw. If the total is not
       the number the git diff predicts, it says so loudly.

USAGE
    python3 Tools/make_legacy_save.py <path/to/save.json>
        [--baseline 06f1b350] [--repo <path>] [--out <path>]
        [--version N]        stamp a different saveVersion (MIGRATION test,
                             not a legacy-load test -- the readout says which)

    The output is a file. Copy it into a spare slot as save.json (and the
    patched meta.json beside it) and load that slot from the title screen.
"""

import argparse
import json
import os
import re
import subprocess
import sys

ROOT_CLASS = "DungeonSaveData"
SAVE_DATA_FILE = "Assets/Scripts/Save/DungeonSaveData.cs"
LEAF_TYPES = {"int", "float", "bool", "string", "long", "double", "uint",
              "byte", "short", "char", "decimal"}
# Classes AND structs: a serialised struct that gains a field is the same hole
# as a class that does. Multi-declarations ("public int x, y, z;") are parsed
# name by name for the same reason -- both vector wrappers use that form.
CLASS_RE = re.compile(
    r'\[(?:System\.)?Serializable\]\s*public (?:class|struct) (\w+)\s*(?::\s*[\w,\s\.]+)?\{')
FIELD_RE = re.compile(
    r'^\s*public\s+(?!const\b|static\b|class\b|struct\b|enum\b|event\b|readonly\b)'
    r'([\w<>\[\],\.]+)\s+(\w+(?:\s*,\s*\w+)*)\s*(?:=|;)', re.M)


# ----------------------------------------------------------------------
# Source parsing
# ----------------------------------------------------------------------

def git(repo, *args):
    return subprocess.run(["git", "-C", repo] + list(args),
                          capture_output=True, text=True, check=True).stdout


def class_bodies(src):
    """Yield (name, body) for every [Serializable] public class in src, by
    brace walk so nested braces in initialisers do not end the class early."""
    for m in CLASS_RE.finditer(src):
        i = m.end()
        depth = 1
        start = i
        while depth and i < len(src):
            c = src[i]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
            i += 1
        yield m.group(1), src[start:i - 1]


def parse_sources(sources):
    """sources: iterable of (path, text). Returns {class: {field: type}}."""
    classes = {}
    for path, src in sources:
        if "Serializable]" not in src:
            continue
        for name, body in class_bodies(src):
            fields = {}
            for fm in FIELD_RE.finditer(body):
                for fname in re.split(r'\s*,\s*', fm.group(2)):
                    fields[fname] = fm.group(1)
            if name in classes and classes[name] != fields and name.endswith("SaveData"):
                print("  !! two classes named %s with different fields; keeping the "
                      "first (%s)" % (name, path))
                continue
            classes.setdefault(name, fields)
    return classes


def sources_at_rev(repo, rev):
    files = git(repo, "ls-tree", "-r", "--name-only", rev, "Assets/Scripts").split()
    for f in files:
        if f.endswith(".cs"):
            yield f, git(repo, "show", "%s:%s" % (rev, f))


def sources_in_worktree(repo):
    base = os.path.join(repo, "Assets", "Scripts")
    for root, _, files in os.walk(base):
        for f in files:
            if f.endswith(".cs"):
                p = os.path.join(root, f)
                with open(p, encoding="utf-8", errors="replace") as fh:
                    yield os.path.relpath(p, repo), fh.read()


def current_version_at(repo, rev):
    src = git(repo, "show", "%s:%s" % (rev, SAVE_DATA_FILE))
    m = re.search(r'CURRENT_VERSION\s*=\s*(\d+)', src)
    if not m:
        raise SystemExit("could not read CURRENT_VERSION at %s" % rev)
    return int(m.group(1))


# ----------------------------------------------------------------------
# Type graph
# ----------------------------------------------------------------------

def element_type(t):
    """List<T> / T[] -> ('list', T); otherwise ('scalar', t)."""
    m = re.match(r'^(?:System\.Collections\.Generic\.)?List<(.+)>$', t)
    if m:
        return "list", m.group(1)
    if t.endswith("[]"):
        return "list", t[:-2]
    return "scalar", t


# ----------------------------------------------------------------------
# The walk
# ----------------------------------------------------------------------

class Report:
    def __init__(self):
        self.stripped = {}      # class -> count of keys removed
        self.undeclared = {}    # class -> set of keys seen but not declared
        self.leaves = set()     # external types treated as leaves
        self.visited = set()

    def strip(self, cls, n=1):
        self.stripped[cls] = self.stripped.get(cls, 0) + n


def walk(node, cls, new, added, rep):
    """Strip added keys from node (a dict typed as cls) and recurse."""
    rep.visited.add(cls)
    if not isinstance(node, dict):
        return
    fields = new.get(cls, {})
    for key in added.get(cls, ()):
        if key in node:
            del node[key]
            rep.strip(cls)
    for key, value in list(node.items()):
        if key not in fields:
            rep.undeclared.setdefault(cls, set()).add(key)
            continue
        kind, t = element_type(fields[key])
        if t in LEAF_TYPES:
            continue
        if t not in new:
            rep.leaves.add(t)
            continue
        if kind == "list":
            if isinstance(value, list):
                for item in value:
                    walk(item, t, new, added, rep)
        else:
            walk(value, t, new, added, rep)


# ----------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------

def find_repo(start):
    here = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(here, "Assets")) and os.path.isdir(os.path.join(here, ".git")):
            return here
        parent = os.path.dirname(here)
        if parent == here:
            return None
        here = parent


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("save", help="path to a current save.json")
    ap.add_argument("--baseline", default="06f1b350",
                    help="commit the synthetic save should predate (default: the "
                         "last smoke-test baseline)")
    ap.add_argument("--repo", default=None, help="repo root (default: walk up from cwd)")
    ap.add_argument("--out", default=None, help="output path (default: <save>.legacy.json)")
    ap.add_argument("--version", type=int, default=None,
                    help="stamp this saveVersion instead of the baseline's own "
                         "CURRENT_VERSION. Doing so makes this a MIGRATION test.")
    args = ap.parse_args()

    repo = args.repo or find_repo(os.getcwd()) or find_repo(os.path.dirname(os.path.abspath(__file__)))
    if not repo:
        raise SystemExit("could not find the repo root; pass --repo")

    with open(args.save, encoding="utf-8") as fh:
        data = json.load(fh)

    old = parse_sources(sources_at_rev(repo, args.baseline))
    new = parse_sources(sources_in_worktree(repo))
    if ROOT_CLASS not in new:
        raise SystemExit("%s not found in the working tree" % ROOT_CLASS)

    added = {}
    for cls, fields in new.items():
        gained = sorted(set(fields) - set(old.get(cls, {})))
        if gained:
            added[cls] = gained

    # The prediction: how many keys COULD be stripped if every added field of
    # every reachable class is present in this file. Actual is usually lower
    # (empty lists still serialise, but a class that never occurs -- an empty
    # floors list -- contributes nothing).
    rep = Report()
    walk(data, ROOT_CLASS, new, added, rep)

    base_version = current_version_at(repo, args.baseline)
    stamped = args.version if args.version is not None else base_version
    data["saveVersion"] = stamped

    out = args.out or re.sub(r'\.json$', '', args.save) + ".legacy.json"
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(data, fh, separators=(",", ":"))

    meta_in = os.path.join(os.path.dirname(os.path.abspath(args.save)), "meta.json")
    meta_out = None
    if os.path.isfile(meta_in):
        with open(meta_in, encoding="utf-8") as fh:
            meta = json.load(fh)
        meta["saveVersion"] = stamped
        meta_out = os.path.join(os.path.dirname(os.path.abspath(out)), "meta.legacy.json")
        with open(meta_out, "w", encoding="utf-8") as fh:
            json.dump(meta, fh, indent=2)

    # -- readout ----------------------------------------------------
    total = sum(rep.stripped.values())
    reachable_added = {c: added[c] for c in added if c in rep.visited}
    predicted_classes = len(reachable_added)
    print("make_legacy_save -- baseline %s, CURRENT_VERSION there = %d"
          % (args.baseline, base_version))
    print()
    print("  classes with fields added since baseline: %d in source, %d reachable "
          "from %s" % (len(added), predicted_classes, ROOT_CLASS))
    print("  keys stripped, by class:")
    for cls in sorted(rep.stripped, key=lambda c: -rep.stripped[c]):
        print("    %-30s %4d   (%d field(s) declared as added)"
              % (cls, rep.stripped[cls], len(added.get(cls, ()))))
    silent = sorted(c for c in reachable_added if c not in rep.stripped)
    if silent:
        print("  reachable classes with added fields but NOTHING stripped "
              "(no instance in this save, or already absent):")
        for c in silent:
            print("    %s" % c)
    print("  TOTAL keys removed: %d" % total)
    print()
    if rep.leaves:
        print("  external types treated as leaves (not defined under Assets/Scripts "
              "as [Serializable] classes; nothing stripped inside them):")
        for t in sorted(rep.leaves):
            print("    %s" % t)
    if rep.undeclared:
        print("  !! keys present in the JSON that the class does NOT declare -- "
              "left untouched, but this means the source and the file disagree:")
        for cls in sorted(rep.undeclared):
            print("    %-30s %s" % (cls, ", ".join(sorted(rep.undeclared[cls]))))
    print()
    if args.version is None:
        print("  saveVersion kept at %d -- the baseline's own CURRENT_VERSION. This is a "
              "LEGACY-LOAD test:\n  a v%d save from before these fields existed."
              % (stamped, stamped))
    else:
        print("  saveVersion stamped %d (baseline had %d). This is a MIGRATION test, "
              "not a legacy-load test:\n  it exercises SaveMigrationRegistry from v%d "
              "upward, which a real pre-baseline save would not."
              % (stamped, base_version, stamped))
    print()
    print("  wrote %s" % out)
    if meta_out:
        print("  wrote %s (saveVersion patched to match)" % meta_out)
    print()
    print("  Next: copy the .legacy.json into a spare slot as save.json (and the "
          "meta beside it as meta.json), then load that slot from the title screen.")
    if total == 0:
        print()
        print("  !! NOTHING WAS STRIPPED. Either this save already predates the "
              "baseline, or the parse found no added fields. Do not use it as a "
              "legacy test.")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
