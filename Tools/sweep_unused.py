#!/usr/bin/env python3
"""
Declared-but-unused sweep over the members this pass introduces.

Canon 42 added this to the validation battery after three members shipped dead
(remainsLump, remainsTaken and StealShare) because the offline compiler had been
passed -nowarn:0414, muffling it against the exact fault it was there to find.
A constant can agree with the sim and still be dead: the older check compared
VALUES, not LIVENESS.

Reads every .cs in the tree, counts references to each named member OUTSIDE its
own declaration line, and reports anything referenced nowhere.
"""
import os
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."

# Members introduced or re-purposed by the den populations pass.
MEMBERS = [
    # DungeonMonster
    "denFloorIndex", "scavengeScanInterval", "scavengePickupRadius",
    "denArrivalDistance", "denLoiterRadius", "carriedHaul", "heldNodes",
    "heldSpoilRarities", "denAnchorWorld", "denAnchorKnown",
    "scavengeScanTimer", "scavengePath", "scavengePathIndex",
    "scavengePathRefreshTimer", "scavengeGoal", "scavengeGoalSet",
    "IsDenScavenger", "CarriedHaul", "DenFloorIndex",
    "InitialiseAsDenScavenger", "TickScavenge", "TryClaimNearestDrop",
    "TakeDropAt", "StepTowards", "DropHaulOnDeath", "ForceDepositHaul",
    # DenController
    "populationCheckInterval", "spawnScatter", "haulDropPrefab",
    "haulContentPrefab", "HaulDropPrefab", "HaulContentPrefab",
    "livePopulation", "populationTimer", "LiveOn", "PruneDead", "DespawnAll",
    "SpawnScavenger", "PopulationBudget", "MayForage", "NotifyScavengerDied", "MayForageAny",
    "ResidentsByTier", "ScavengerBudget",
    # save entry
    "heldNodeKeys", "contested", "spokenWaking",
    # loot
    "TakeForCarrying", "IsDenSourced", "MarkDenSourced", "denSourced",
    
    # floor
    "FloorSpacingY", "FloorIndexFromWorld", "IsOnFloor",
    # adventurer / save
    "CarriedDenLootValue", "restoredCarriedDenGold", "carriedDenGold",
    "scavengerDefinition",
]

sources = []
for base, dirs, files in os.walk(os.path.join(ROOT, "Assets")):
    for fn in files:
        if fn.endswith(".cs"):
            p = os.path.join(base, fn)
            sources.append((p, open(p, encoding="utf-8").read()))

DECL = re.compile(r"(private|public|protected|internal|readonly|static|const"
                  r"|SerializeField|Tooltip|Header|class |void |int |float "
                  r"|bool |string |List<|Dictionary<)")

dead, thin = [], []
for m in MEMBERS:
    pat = re.compile(r"\b" + re.escape(m) + r"\b")
    uses = 0
    decl_lines = 0
    for path, text in sources:
        for line in text.splitlines():
            if not pat.search(line):
                continue
            stripped = line.strip()
            # Doc comments and plain comments never count as a use.
            if re.match(r"^(///|//)", stripped):
                continue
            # An attribute-prefixed declaration -- [SerializeField] private X y; --
            # is still a DECLARATION. Skipping every line starting with '[' made
            # the backing field look dead and its own property look like the
            # declaration, which reported two live members as dead. A sweep that
            # cries wolf is worse than none, because the next real one gets waved
            # through.
            decl_part = re.sub(r"^\s*(\[[^\]]*\]\s*)+", "", stripped)
            is_decl = bool(re.search(
                r"(=>|\b(private|public|protected|internal)\b).*\b"
                + re.escape(m) + r"\b", decl_part)) and "(" not in decl_part.split(m)[0][-2:]
            if is_decl and decl_lines == 0:
                decl_lines += 1
                continue
            uses += 1
    if uses == 0:
        dead.append(m)
    elif uses == 1:
        thin.append(m)

print("Declared-but-unused sweep over %d source files\n" % len(sources))
if dead:
    print("*** DEAD -- declared and never referenced: ***")
    for m in dead:
        print("      %s" % m)
else:
    print("  No dead members.")
if thin:
    print("\n  Referenced exactly once (verify this is a real use, not a "
          "second declaration):")
    for m in thin:
        print("      %s" % m)
sys.exit(1 if dead else 0)
