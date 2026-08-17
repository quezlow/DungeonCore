#!/usr/bin/env python3
"""Headless simulation of the WorldEventDirector dawn logic.

Models the exact dawn sequence the C# will implement:
  1. tick active timed effects (decrement, expire at zero, recompute mults)
  2. decrement the global cooldown
  3. gather eligible events (minDay, gates, per-event cooldown, suppression)
  4. roll the global daily fire chance; on success pick one by weight
  5. fire: instant effects apply at once, timed effects join the active list;
     the fired event's cooldown and the global cooldown re-arm

Run: python3 Tools/sim_world_events.py
Exit 0 with all checks green, 1 otherwise. Rerun whenever the tuning
defaults or the dawn ordering change; the C# must mirror this file.
"""

import random
import sys

# -- Tuning defaults (must match WorldEventDirector + the three assets) --

DAILY_FIRE_CHANCE = 0.25
GLOBAL_COOLDOWN_DAYS = 3

# Canon 51: the deep pilgrimage occupies the road for roughly this many days
# per journey (gate leg + transit + deep leg, with night camps). The C# gate
# is state == Idle rather than a day count; this constant is the sim's
# honest approximation of that occupancy.
PILGRIMAGE_JOURNEY_DAYS = 5

EVENTS = {
    # id: (minDay, minNotoriety, minRating, cooldownDays, weight,
    #      durationDays, kind, magnitude, hostile)
    "we_murrain":       (15, 0, 0, 10, 1.0, 3, "RespawnRate",   0.5, False),
    "we_pilgrim_surge": (10, 0, 0,  8, 1.0, 2, "CivilianWeight", 1.5, False),
    "we_tremor":        ( 6, 0, 0,  6, 1.5, 0, "GrantGold",      0.0, False),
    # Canon 51: the deep pilgrimage. Instant kind - the effect hands the
    # journey to DwarvenPilgrimageController; the director's cooldowns own
    # the cadence and the controller's CanBegin (modelled below as the
    # occupancy counter plus the can_begin flag) gates eligibility.
    "we_deep_pilgrimage": (12, 0, 0,  9, 1.0, 0, "BeginPilgrimage", 0.0, False),
}

MINDAY, MINNOTO, MINRATING, CD, WEIGHT, DURATION, KIND, MAG, HOSTILE = range(9)


class Director:
    """Mirror of the C# dawn state machine."""

    def __init__(self, rng, notoriety=0.0, rating=0.0, suppress=False,
                 pilg_can_begin=True):
        self.rng = rng
        self.notoriety = notoriety
        self.rating = rating
        self.suppress = suppress
        # Canon 51: the controller-side departure gate, as the sim sees it.
        # pilg_active mirrors "a journey is in flight"; pilg_can_begin folds
        # every other CanBegin refusal (hold fallen, no destination, climax).
        self.pilg_active = 0
        self.pilg_can_begin = pilg_can_begin
        self.global_cd = 0
        self.last_fired = {}     # id -> day
        self.times_fired = {}    # id -> count
        self.active = {}         # id -> days remaining
        self.log = []            # (day, id)

    # -- cached multiplier recompute, exactly as the C# will do it --
    def respawn_mult(self):
        m = 1.0
        for eid in self.active:
            if EVENTS[eid][KIND] == "RespawnRate":
                m *= EVENTS[eid][MAG]
        return m

    def civilian_mult(self):
        m = 1.0
        for eid in self.active:
            if EVENTS[eid][KIND] == "CivilianWeight":
                m *= EVENTS[eid][MAG]
        return m

    def eligible(self, eid, day):
        e = EVENTS[eid]
        if day < e[MINDAY]:
            return False
        if e[MINNOTO] > 0 and self.notoriety < e[MINNOTO]:
            return False
        if e[MINRATING] > 0 and self.rating < e[MINRATING]:
            return False
        if eid in self.active:
            return False
        last = self.last_fired.get(eid)
        if last is not None and day - last < e[CD]:
            return False
        if e[KIND] == "BeginPilgrimage":
            # Mirrors WorldEventDirector.Eligible consulting
            # DwarvenPilgrimageController.CanBegin: no journey in flight and
            # the controller's own gates open.
            if self.pilg_active > 0 or not self.pilg_can_begin:
                return False
        return True

    def dawn(self, day):
        # 1. tick active effects (and the pilgrimage occupancy beside them)
        if self.pilg_active > 0:
            self.pilg_active -= 1
        for eid in list(self.active):
            self.active[eid] -= 1
            if self.active[eid] <= 0:
                del self.active[eid]
        # 2. global cooldown
        if self.global_cd > 0:
            self.global_cd -= 1
            return
        # 3. eligibility (climax suppression strips hostile events only)
        pool = [eid for eid in EVENTS
                if self.eligible(eid, day)
                and not (self.suppress and EVENTS[eid][HOSTILE])]
        if not pool:
            return
        # 4. daily chance + weighted pick
        if self.rng.random() >= DAILY_FIRE_CHANCE:
            return
        weights = [EVENTS[eid][WEIGHT] for eid in pool]
        eid = self.rng.choices(pool, weights=weights, k=1)[0]
        # 5. fire
        self.fire(eid, day)

    def fire(self, eid, day):
        e = EVENTS[eid]
        if e[KIND] == "BeginPilgrimage":
            self.pilg_active = PILGRIMAGE_JOURNEY_DAYS
        if e[DURATION] > 0:
            self.active[eid] = e[DURATION]
        self.last_fired[eid] = day
        self.times_fired[eid] = self.times_fired.get(eid, 0) + 1
        self.global_cd = GLOBAL_COOLDOWN_DAYS
        self.log.append((day, eid))

    # -- save/load round trip: state only, no dawn refire --
    def save(self):
        return (self.global_cd, dict(self.last_fired),
                dict(self.times_fired), dict(self.active), self.pilg_active)

    @classmethod
    def load(cls, rng, blob, notoriety=0.0, rating=0.0):
        d = cls(rng, notoriety, rating)
        d.global_cd, d.last_fired, d.times_fired, d.active = (
            blob[0], dict(blob[1]), dict(blob[2]), dict(blob[3]))
        d.pilg_active = blob[4] if len(blob) > 4 else 0
        return d


# -- Checks ----------------------------------------------------------

FAILURES = []


def check(name, ok, detail=""):
    tag = "ok  " if ok else "FAIL"
    print(f"[{tag}] {name}" + (f" -- {detail}" if detail else ""))
    if not ok:
        FAILURES.append(name)


def run(seed, days=200, notoriety=0.0, rating=0.0, suppress=False,
        pilg_can_begin=True):
    d = Director(random.Random(seed), notoriety, rating, suppress,
                 pilg_can_begin)
    mult_trace = []
    for day in range(1, days + 1):
        d.dawn(day)
        mult_trace.append((day, d.respawn_mult(), d.civilian_mult()))
    return d, mult_trace


def main():
    # 1. no event before its minDay
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        for day, eid in d.log:
            if day < EVENTS[eid][MINDAY]:
                bad.append((seed, day, eid))
    check("no fire before minDay", not bad, str(bad[:3]))

    # 2. per-event cooldown never violated
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        last = {}
        for day, eid in d.log:
            if eid in last and day - last[eid] < EVENTS[eid][CD]:
                bad.append((seed, eid, last[eid], day))
            last[eid] = day
    check("per-event cooldown respected", not bad, str(bad[:3]))

    # 3. global cooldown: no two fires closer than GLOBAL_COOLDOWN_DAYS + 1
    #    (fire day re-arms cd=3, which burns down over the next 3 dawns)
    bad = []
    for seed in range(50):
        d, _ = run(seed)
        for (d1, _), (d2, _) in zip(d.log, d.log[1:]):
            if d2 - d1 <= GLOBAL_COOLDOWN_DAYS:
                bad.append((seed, d1, d2))
    check("global cooldown respected", not bad, str(bad[:3]))

    # 4. timed effects never self-overlap (guaranteed if cd >= duration)
    ok = all(e[CD] >= e[DURATION] for e in EVENTS.values())
    check("per-event cooldown >= duration (no self-overlap)", ok)

    # 5. selection proportions track weights (all eligible, high chance)
    counts = {eid: 0 for eid in EVENTS}
    rng = random.Random(7)
    d = Director(rng)
    pool = list(EVENTS)
    for _ in range(60000):
        weights = [EVENTS[eid][WEIGHT] for eid in pool]
        counts[rng.choices(pool, weights=weights, k=1)[0]] += 1
    total_w = sum(EVENTS[eid][WEIGHT] for eid in pool)
    ok = True
    for eid in pool:
        expect = EVENTS[eid][WEIGHT] / total_w
        got = counts[eid] / 60000
        if abs(got - expect) > 0.02:
            ok = False
    check("weighted selection proportions", ok, str(counts))

    # 6. cadence band: mean events per 30 days (after all minDays) in [3, 6]
    rates = []
    for seed in range(200):
        d, _ = run(seed, days=15 + 30)
        rates.append(sum(1 for day, _ in d.log if day > 15))
    mean = sum(rates) / len(rates)
    check("cadence 3-6 events per 30 eligible days", 3.0 <= mean <= 6.0,
          f"mean {mean:.2f}")

    # 7. determinism with seed
    a, _ = run(42)
    b, _ = run(42)
    check("deterministic per seed", a.log == b.log)

    # 8. zero eligible -> no fire (gates unmet)
    EVENTS_BACKUP = dict(EVENTS)
    for eid in EVENTS:
        e = list(EVENTS[eid])
        e[MINNOTO] = 999
        EVENTS[eid] = tuple(e)
    d, _ = run(1, days=100)
    check("no eligible events -> silent", not d.log)
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 9. minNotoriety gate opens correctly
    e = list(EVENTS["we_tremor"])
    e[MINNOTO] = 50
    EVENTS["we_tremor"] = tuple(e)
    d_lo, _ = run(3, days=100, notoriety=10)
    d_hi, _ = run(3, days=100, notoriety=80)
    lo_fired = any(eid == "we_tremor" for _, eid in d_lo.log)
    hi_fired = any(eid == "we_tremor" for _, eid in d_hi.log)
    check("minNotoriety gate", (not lo_fired) and hi_fired,
          f"low fired={lo_fired} high fired={hi_fired}")
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 10. save/load mid-effect: remaining days continue, no refire on load
    rng = random.Random(9)
    d = Director(rng)
    d.fire("we_murrain", 20)          # duration 3
    d.dawn(21)                        # ticks to 2
    blob = d.save()
    d2 = Director.load(random.Random(9), blob)
    ok = (d2.active.get("we_murrain") == 2
          and abs(d2.respawn_mult() - 0.5) < 1e-9
          and len(d2.log) == 0)
    check("save/load mid-effect resumes without refire", ok,
          f"active={d2.active} mult={d2.respawn_mult()}")

    # 11. expiry restores multiplier to 1
    d2.dawn(22)   # 2 -> 1
    d2.dawn(23)   # 1 -> 0, expires
    check("effect expiry restores multiplier",
          abs(d2.respawn_mult() - 1.0) < 1e-9,
          f"mult={d2.respawn_mult()}")

    # 12. multiplier active exactly duration dawns after the fire dawn
    rng = random.Random(11)
    d = Director(rng)
    d.fire("we_pilgrim_surge", 30)    # duration 2
    m0 = d.civilian_mult()            # active on fire day
    d.dawn(31)
    m1 = d.civilian_mult()            # still active (1 day left)
    d.dawn(32)
    m2 = d.civilian_mult()            # expired
    check("timed effect spans fire day + duration-1 following days",
          abs(m0 - 1.5) < 1e-9 and abs(m1 - 1.5) < 1e-9
          and abs(m2 - 1.0) < 1e-9,
          f"{m0} {m1} {m2}")

    # 13. suppression strips hostile events only; benign ones still fire
    e = list(EVENTS["we_murrain"])
    e[HOSTILE] = True
    EVENTS["we_murrain"] = tuple(e)
    d, _ = run(5, days=200, suppress=True)
    hostile_fired = any(eid == "we_murrain" for _, eid in d.log)
    benign_fired = any(eid != "we_murrain" for _, eid in d.log)
    check("suppression strips hostile only",
          (not hostile_fired) and benign_fired,
          f"hostile={hostile_fired} benign={benign_fired}")
    EVENTS.clear()
    EVENTS.update(EVENTS_BACKUP)

    # 14. active effect blocks its own re-fire even if cd were shorter
    rng = random.Random(13)
    d = Director(rng)
    d.fire("we_murrain", 40)
    check("active effect not re-eligible", not d.eligible("we_murrain", 41))

    # 15. a pilgrimage in flight blocks the next one from firing (canon 51:
    #     the CanBegin consult in Eligible, which the timed-effect active
    #     list cannot cover because a journey is not a timed effect)
    rng = random.Random(17)
    d = Director(rng)
    d.fire("we_deep_pilgrimage", 40)
    blocked = not d.eligible("we_deep_pilgrimage", 41)
    d2 = Director(random.Random(17))
    d2.fire("we_deep_pilgrimage", 40)
    for day in range(41, 41 + PILGRIMAGE_JOURNEY_DAYS):
        d2.dawn(day)   # burns occupancy (and the cooldown holds regardless)
    freed = d2.pilg_active == 0
    check("pilgrimage occupancy blocks refire, then frees", blocked and freed,
          f"blocked={blocked} freed={freed}")

    # 16. pilgrimage fires respect occupancy across long runs: no two
    #     pilgrimage fires closer than the journey length
    bad = []
    for seed in range(60):
        d, _ = run(seed, days=300)
        marks = [day for day, eid in d.log if eid == "we_deep_pilgrimage"]
        for a, b in zip(marks, marks[1:]):
            if b - a < PILGRIMAGE_JOURNEY_DAYS:
                bad.append((seed, a, b))
        if not any(marks) and seed == 0:
            pass
    fired_somewhere = any(
        any(eid == "we_deep_pilgrimage" for _, eid in run(s, days=300)[0].log)
        for s in range(10))
    check("pilgrimage fires spaced by at least the journey", not bad and fired_somewhere,
          f"bad={bad[:3]} fired_somewhere={fired_somewhere}")

    # 17. CanBegin false silences the pilgrimage and nothing else
    d_no, _ = run(21, days=300, pilg_can_begin=False)
    pilg_fired = any(eid == "we_deep_pilgrimage" for _, eid in d_no.log)
    others_fired = any(eid != "we_deep_pilgrimage" for _, eid in d_no.log)
    check("CanBegin false silences pilgrimage only",
          (not pilg_fired) and others_fired,
          f"pilg={pilg_fired} others={others_fired}")

    # 18. save/load mid-journey keeps the occupancy (a loaded save must not
    #     free the road early)
    rng = random.Random(23)
    d = Director(rng)
    d.fire("we_deep_pilgrimage", 50)
    d.dawn(51)
    blob = d.save()
    d3 = Director.load(random.Random(23), blob)
    check("save/load keeps pilgrimage occupancy",
          d3.pilg_active == PILGRIMAGE_JOURNEY_DAYS - 1,
          f"active={d3.pilg_active}")

    print()
    if FAILURES:
        print(f"{len(FAILURES)} check(s) FAILED: {FAILURES}")
        return 1
    print("all checks green")
    return 0


if __name__ == "__main__":
    sys.exit(main())
