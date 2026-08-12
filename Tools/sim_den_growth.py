#!/usr/bin/env python3
"""
Headless simulation of DEN GROWTH -- hoard, tier and raid pressure (canon 42).

There is no Unity here, so this proves nothing about rendering or pathing. What
it CAN answer, before a line of C# exists, is the only question that decides
whether the feature is worth building: does hoard -> tier -> raid produce a
READABLE escalation on a plausible timescale, or does it saturate in three days
and sit at max for the rest of the run?

THE STRUCTURAL PROPOSAL BEING TESTED. Canon 42 says the goblin hoard feeds tier
and tier feeds raids, but never says what raises a KOBOLD's tier -- they do not
steal. Rather than invent a second system, both dens share ONE ledger, ONE tier
curve and ONE raid table, and differ only in the INCOME FUNCTION:

    Occupier (goblins)   earn by THEFT       -- loose loot on their floor,
                                                plus a cut of the player's gold
                                                on a successful raid.
    Excavator (kobolds)  earn by EXCAVATION  -- spoil per cell dug, plus a lump
                                                for each buried remains taken.

One controller, one save shape, one tier curve, and the verbs stay distinct.
Clearing a den returns its hoard either way.

EVERY INPUT BELOW WAS READ FROM SOURCE, not remembered:

  DayNightCycle          dayDuration 180 + nightDuration 60 = 240s per day.
  WaveStageController    WaveStage is a PROGRESSION (Dormant/Animals/Commoners/
                         Adventurers), not a day/night gate -- so once in the
                         Adventurers stage parties arrive continuously.
  AdventurerSpawner      intervalLow 30 / intervalMedium 20 / intervalHigh 10,
                         banded at notoriety 25 and 75; party size 1-3.
  Adventurer.prefab      LootTable gold 3 (weight 2) and 8 (weight 1), owner
                         Adventurer -> DroppedLoot. Expected 4.667 per kill.
  DungeonCore            killNotoriety 5, notorietySoftCap 100,
                         notorietyMinGainFraction 0.15 (soft-capped accrual).
  Monster prefabs        Authored expected drops, from the shipped weights:
                         Skeleton (2 w3 / 5 w1) = 2.75, Zombie (0 w1 / 2 w2) =
                         1.33, Armored Skeleton (5 w2 / 10 w1) = 6.67. Owner is
                         Monster -> CarriableLoot. The roster is being authored
                         out across 44 prefabs, so monster loot is now modelled
                         as PRESENT by default rather than optional.
  DungeonAdventurer      ScanForLoot picks up CarriableLoot within pickupRadius
                         0.6 and carries it OUT -- so monster drops are
                         CONTESTED and adventurer drops are not. This is the
                         difference between the two streams and the reason they
                         cannot be added together and left at that.
  LootAbsorbGate         loose loot lingers >= 30s, held while adventurers are
                         within 5 units, so a scavenging window exists at all.

Usage:  python3 sim_den_growth.py [days] [--verbose]
"""

import sys

# ---- read from source ---------------------------------------------------

DAY_SECONDS = 180.0 + 60.0        # DayNightCycle
INTERVAL_LOW = 30.0               # AdventurerSpawner
INTERVAL_MED = 20.0
INTERVAL_HIGH = 10.0
NOTORIETY_MED = 25.0
NOTORIETY_HIGH = 75.0
PARTY_MIN, PARTY_MAX = 1, 3
ADV_GOLD_EXPECTED = (3 * 2 + 8 * 1) / 3.0     # 4.667, DroppedLoot

# Authored monster drops, expected value per weighted table. The roster spans
# cheap early undead to richer later monsters; the mean is what a mixed defence
# loses on average.
MONSTER_GOLD_BY_EXAMPLE = {
    "skeleton": (2 * 3 + 5 * 1) / 4.0,          # 2.75
    "zombie": (0 * 1 + 2 * 2) / 3.0,            # 1.33
    "armoured skeleton": (5 * 2 + 10 * 1) / 3.0,  # 6.67
}
MONSTER_GOLD_EXPECTED = sum(MONSTER_GOLD_BY_EXAMPLE.values()) / 3.0   # 3.58

# Player monsters lost per adventurer killed. A defence that trades one monster
# for three adventurers is doing well; one for two is ordinary.
MONSTER_LOSS_RATIO = 0.4

# Share of monster drops an adventurer reaches first. CarriableLoot is what
# adventurers are FOR -- they pick it up and carry it out -- so goblins are the
# third party at that corpse, not the first.
ADVENTURER_TAKE_OF_MONSTER_LOOT = 0.45
KILL_NOTORIETY = 5.0
NOTORIETY_SOFT_CAP = 100.0
NOTORIETY_MIN_FRACTION = 0.15

# ---- the knobs this sim exists to choose --------------------------------

# Geometric-ish thresholds. A flat curve would put a den at max tier while the
# player is still on Bronze; a steeper one would never leave tier 1.
TIER_THRESHOLDS = [0, 60, 200, 550, 1400]      # tier 1..5 entry hoard

# Share of the loose loot on their floor that goblins actually reach. Not 1.0:
# the window is 30s, they have to be near, and the player's own monsters are
# competing for the same corpses.
#
# TIER 1 MUST BE NON-ZERO. The first version had 0.0 here and the den could
# never start: no income at tier 1 means no hoard, which means no tier 2, for
# ever. Any income curve gated on its own output needs a non-zero seed, and this
# is the kind of deadlock that looks like "the feature does nothing" in play.
STEAL_SHARE_BY_TIER = [0.06, 0.12, 0.20, 0.30, 0.42]

# Raids: mean days between, and what a successful one takes.
#
# A FLAT tier-scaled amount, CAPPED as a fraction of the player's gold -- not a
# pure percentage. A percentage was measured first and rewarded the wrong thing:
# a player who spent to zero paid nothing at all (0 gold lost against 8,174 for
# a hoarder), so the rule punished SAVING rather than punishing losing. Flat
# always bites; the cap stops it emptying a poor dungeon.
RAID_INTERVAL_BY_TIER = [14.0, 9.0, 6.0, 4.0, 3.0]
RAID_FLAT_BY_TIER = [12.0, 30.0, 65.0, 130.0, 240.0]
RAID_CAP_FRACTION = 0.25

# Excavator income. Dig rate rises with tier, so a kobold den accelerates.
DIG_CELLS_PER_DAY_BY_TIER = [7, 11, 17, 26, 38]
SPOIL_PER_CELL = 1.4
REMAINS_LUMP = 120.0              # a buried remains taken; at most 2 per floor

# How strongly a wider dungeon feeds the diggers. 0 would restore the pure
# clock; 1.0 makes a fast-expanding player face roughly double the dig rate.
EXPANSION_SENSITIVITY = 0.8

GRACE_DAYS = 5                    # canon 42


def interval_for(notoriety):
    if notoriety >= NOTORIETY_HIGH:
        return INTERVAL_HIGH
    if notoriety >= NOTORIETY_MED:
        return INTERVAL_MED
    return INTERVAL_LOW


def tier_for(hoard):
    t = 1
    for i in range(1, len(TIER_THRESHOLDS)):
        if hoard >= TIER_THRESHOLDS[i]:
            t = i + 1
    return t


def run(kind, days, kill_rate, player_gold_per_day, monster_loot,
        verbose=False):
    """One run. Returns (tier_first_reached_day, hoard, raids, stolen)."""
    hoard = 0.0
    notoriety = 0.0
    player_gold = 0.0
    raids = 0
    stolen_total = 0.0
    remains_left = 2
    next_raid_in = None
    first_day_at_tier = {}

    for day in range(1, days + 1):
        tier = tier_for(hoard)
        first_day_at_tier.setdefault(tier, day)

        # -- the wave, and the loot it leaves on the floor ---------------
        interval = interval_for(notoriety)
        parties = DAY_SECONDS / interval
        adventurers = parties * (PARTY_MIN + PARTY_MAX) / 2.0
        kills = adventurers * kill_rate

        # Notoriety accrues soft-capped, exactly as AccrueKillNotoriety does.
        for _ in range(int(kills)):
            headroom = max(0.0, min(1.0, 1.0 - notoriety / NOTORIETY_SOFT_CAP))
            notoriety += KILL_NOTORIETY * (
                NOTORIETY_MIN_FRACTION
                + (1.0 - NOTORIETY_MIN_FRACTION) * headroom)

        # Two streams, and they are NOT interchangeable.
        #
        #   Adventurer drops (DroppedLoot) are uncontested: only the core takes
        #   them, and only after the LootAbsorbGate hold, so a goblin that gets
        #   there competes with nobody.
        #
        #   Monster drops (CarriableLoot) are contested: adventurers pick them up
        #   and carry them out, which is what that class exists for. A goblin is
        #   the third party at that corpse.
        floor_gold = kills * ADV_GOLD_EXPECTED
        if monster_loot:
            monster_deaths = kills * MONSTER_LOSS_RATIO
            raw = monster_deaths * MONSTER_GOLD_EXPECTED
            floor_gold += raw * (1.0 - ADVENTURER_TAKE_OF_MONSTER_LOOT)

        player_gold += player_gold_per_day

        if day <= GRACE_DAYS:
            if verbose:
                print("  day %2d  dormant (grace)" % day)
            continue

        # -- income ------------------------------------------------------
        if kind == "occupier":
            take = floor_gold * STEAL_SHARE_BY_TIER[tier - 1]
            hoard += take
            stolen_total += take
        else:
            # Excavation is otherwise a PURE CLOCK -- identical on every player
            # profile, which the first run showed plainly and which is the real
            # weakness of digging-as-income. So the rate responds to the
            # player's own expansion on that floor: a sprawling dungeon gives
            # the diggers more rock worth opening and more to run into. Same
            # logic as entry 12A scaling creep by claim count, and it hands the
            # player a lever that is not simply "go and kill it".
            expansion = 1.0 + EXPANSION_SENSITIVITY * (player_gold_per_day / 90.0 - 1.0)
            expansion = max(0.5, min(1.8, expansion))
            dug = DIG_CELLS_PER_DAY_BY_TIER[tier - 1] * expansion
            hoard += dug * SPOIL_PER_CELL
            # A remains is reached roughly once the diggings have run a while;
            # modelled as a chance per day that rises with tier.
            if remains_left > 0 and day % max(6, 26 - 5 * tier) == 0:
                hoard += REMAINS_LUMP
                remains_left -= 1

        # -- raids -------------------------------------------------------
        if next_raid_in is None:
            next_raid_in = RAID_INTERVAL_BY_TIER[tier - 1]
        next_raid_in -= 1
        if next_raid_in <= 0:
            raids += 1
            cut = min(RAID_FLAT_BY_TIER[tier - 1],
                      player_gold * RAID_CAP_FRACTION)
            player_gold -= cut
            if kind == "occupier":
                hoard += cut          # the raid cut joins the hoard
            next_raid_in = RAID_INTERVAL_BY_TIER[tier - 1]

        if verbose:
            print("  day %2d  tier %d  hoard %7.0f  notoriety %5.1f  raids %d"
                  % (day, tier, hoard, notoriety, raids))

    return first_day_at_tier, hoard, raids, stolen_total


def main():
    days = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 60
    verbose = "--verbose" in sys.argv

    print("Den growth -- %d days simulated" % days)
    print("day = %.0fs (DayNightCycle 180+60); parties continuous once in the "
          "Adventurers stage" % DAY_SECONDS)
    print("tier thresholds: %s" % TIER_THRESHOLDS[1:])
    print()

    profiles = [
        # label,               kill_rate, player gold/day, monster loot authored
        ("passive dungeon",       0.45,    40.0,  True),
        ("typical dungeon",       0.75,    90.0,  True),
        ("killer dungeon",        0.95,   160.0,  True),
        ("typical, adv drops only", 0.75,  90.0,  False),
    ]

    for kind in ("occupier", "excavator"):
        print("== %s ==" % kind.upper())
        print("%-22s %-8s %-8s %-8s %-8s %-8s %-7s"
              % ("profile", "T2 day", "T3 day", "T4 day", "T5 day",
                 "hoard", "raids"))
        for label, kr, pg, ml in profiles:
            # Excavators do not touch loose loot, so the monster-loot variant
            # is the SAME run for them -- show it once rather than four times.
            # (The first version skipped on `ml` being true, which silently hid
            # every excavator profile once monster loot became the default.)
            if kind == "excavator" and not ml:
                continue
            first, hoard, raids, stolen = run(kind, days, kr, pg, ml, verbose)
            def d(t):
                return str(first[t]) if t in first else "-"
            print("%-22s %-8s %-8s %-8s %-8s %-8.0f %-7d"
                  % (label, d(2), d(3), d(4), d(5), hoard, raids))
        print()

    # ---- the checks that decide whether the curve is usable ------------
    print("Verdict checks:")
    bad = False
    first, hoard, raids, _ = run("occupier", days, 0.75, 90.0, True)
    if 2 not in first or first[2] > 20:
        print("!! occupier never reaches tier 2 inside 20 days on a typical "
              "dungeon -- the den would read as inert.")
        bad = True
    if 5 in first and first[5] < 20:
        print("!! occupier hits MAX tier by day %d -- saturates, so the rest of "
              "the run has a flat threat." % first[5])
        bad = True
    firstx, hoardx, _, _ = run("excavator", days, 0.75, 90.0, False)
    if 2 not in firstx or firstx[2] > 20:
        print("!! excavator never reaches tier 2 inside 20 days -- digging "
              "income is too thin to drive the shared curve.")
        bad = True
    if 5 in firstx and firstx[5] < 20:
        print("!! excavator saturates by day %d." % firstx[5])
        bad = True
    print()
    print("VERDICT: " + ("curve needs retuning -- see above" if bad else
                         "one shared curve paces both income functions"))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
