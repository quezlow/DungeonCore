#!/usr/bin/env python3
"""
sim_appeal_weights.py -- headless check of the Dungeon Appeal Ledger maths.

Mirrors DungeonAppealLedger.Recompute and the spawner's civilian-lane
application exactly, then asserts direction and clamps across authored
scenarios. Deterministic: shares are computed analytically from weights,
no RNG. Run: python3 Tools/sim_appeal_weights.py  (exit 0 = green).
Rerun whenever the ledger maths or the spawner application changes.
"""

WINDOW_DAYS = 3
GRACE_RATE = 0.25
MAX_DETERRENCE = 0.6
APPEAL_PER_GOLD = 0.02
APPEAL_CAP = 3.0

BASE_DELVER, BASE_DESTROYER, BASE_PILGRIM, BASE_GIFTGIVER = 5.0, 3.0, 2.0, 1.0


def clamp01(x):
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


class Ledger:
    """Mirror of DungeonAppealLedger's window + Recompute."""

    def __init__(self):
        self.slain = [0] * WINDOW_DAYS
        self.resolved = [0] * WINDOW_DAYS
        self.gold = [0] * WINDOW_DAYS
        self.civ_mult = 1.0
        self.appeal = 0.0

    def ingest(self, slain, resolved, gold):
        self.slain[0] += slain
        self.resolved[0] += resolved
        self.gold[0] += gold
        self.recompute()

    def dawn(self):
        self.slain = [0] + self.slain[:WINDOW_DAYS - 1]
        self.resolved = [0] + self.resolved[:WINDOW_DAYS - 1]
        self.gold = [0] + self.gold[:WINDOW_DAYS - 1]
        self.recompute()

    def recompute(self):
        s, r, g = sum(self.slain), sum(self.resolved), sum(self.gold)
        rate = (s / r) if r > 0 else 0.0
        over = clamp01((rate - GRACE_RATE) / max(0.0001, 1.0 - GRACE_RATE))
        self.civ_mult = 1.0 - over * clamp01(MAX_DETERRENCE)
        self.appeal = min(APPEAL_CAP, g * APPEAL_PER_GOLD)


def intent_shares(ledger, noto=0.0, rep=0.0):
    w_del = max(0.0, BASE_DELVER)
    w_des = max(0.0, BASE_DESTROYER + noto * 0.03)
    w_pil = max(0.0, BASE_PILGRIM + rep * 0.04)
    w_gif = max(0.0, BASE_GIFTGIVER + rep * 0.02)

    w_del = (w_del + ledger.appeal) * ledger.civ_mult
    w_pil *= ledger.civ_mult
    w_gif *= ledger.civ_mult

    total = w_del + w_des + w_pil + w_gif
    return {"delver": w_del / total, "destroyer": w_des / total,
            "pilgrim": w_pil / total, "giftgiver": w_gif / total}


fails = []


def check(label, cond, detail=""):
    if not cond:
        fails.append("%s  %s" % (label, detail))
    print("%s  %s %s" % ("PASS" if cond else "FAIL", label, detail))


base = Ledger()
base_shares = intent_shares(base)
check("baseline neutral", abs(base.civ_mult - 1.0) < 1e-9 and base.appeal == 0.0,
      "mult=%.3f appeal=%.3f" % (base.civ_mult, base.appeal))

blood = Ledger()
blood.ingest(slain=9, resolved=10, gold=0)
expect_det = clamp01((0.9 - GRACE_RATE) / (1 - GRACE_RATE)) * MAX_DETERRENCE
check("bloodbath deterrence value", abs((1 - blood.civ_mult) - expect_det) < 1e-9,
      "det=%.3f expect=%.3f" % (1 - blood.civ_mult, expect_det))
bs = intent_shares(blood)
check("bloodbath thins civilians",
      bs["delver"] < base_shares["delver"] and bs["pilgrim"] < base_shares["pilgrim"]
      and bs["giftgiver"] < base_shares["giftgiver"],
      "delver %.3f->%.3f" % (base_shares["delver"], bs["delver"]))
check("bloodbath raises destroyer share", bs["destroyer"] > base_shares["destroyer"],
      "destroyer %.3f->%.3f" % (base_shares["destroyer"], bs["destroyer"]))

gen = Ledger()
gen.ingest(slain=1, resolved=10, gold=300)
check("generous: below grace = no deterrence", abs(gen.civ_mult - 1.0) < 1e-9,
      "mult=%.3f" % gen.civ_mult)
check("generous: appeal capped", abs(gen.appeal - APPEAL_CAP) < 1e-9,
      "appeal=%.3f cap=%.1f (raw %.1f)" % (gen.appeal, APPEAL_CAP, 300 * APPEAL_PER_GOLD))
gs = intent_shares(gen)
check("generous lifts delver share", gs["delver"] > base_shares["delver"],
      "delver %.3f->%.3f" % (base_shares["delver"], gs["delver"]))

mix = Ledger()
mix.ingest(slain=5, resolved=10, gold=100)
expect_mix_det = clamp01((0.5 - GRACE_RATE) / (1 - GRACE_RATE)) * MAX_DETERRENCE
check("mixed deterrence", abs((1 - mix.civ_mult) - expect_mix_det) < 1e-9,
      "det=%.3f expect=%.3f" % (1 - mix.civ_mult, expect_mix_det))
check("mixed appeal", abs(mix.appeal - 2.0) < 1e-9, "appeal=%.3f" % mix.appeal)
ms = intent_shares(mix)
check("mixed: pilgrims still thin", ms["pilgrim"] < base_shares["pilgrim"], "")
plain = Ledger()
plain.ingest(slain=5, resolved=10, gold=0)
ps = intent_shares(plain)
check("mixed: gold shields delver vs bloodless mixed", ms["delver"] > ps["delver"],
      "%.3f > %.3f" % (ms["delver"], ps["delver"]))

dec = Ledger()
dec.ingest(slain=10, resolved=10, gold=0)
check("total slaughter hits the cap", abs((1 - dec.civ_mult) - MAX_DETERRENCE) < 1e-9,
      "det=%.3f" % (1 - dec.civ_mult))
partial = []
for day in range(WINDOW_DAYS):
    dec.dawn()
    partial.append(dec.civ_mult)
check("deterrence decays to neutral after window", abs(dec.civ_mult - 1.0) < 1e-9,
      "mults after each dawn: %s" % ", ".join("%.3f" % m for m in partial))
check("window keeps deterrence until it rotates out",
      all(m < 1.0 for m in partial[:-1]), "")

edge = Ledger()
edge.ingest(slain=25, resolved=100, gold=0)
check("rate at grace = zero deterrence", abs(edge.civ_mult - 1.0) < 1e-9,
      "mult=%.3f" % edge.civ_mult)
under = Ledger()
under.ingest(slain=24, resolved=100, gold=0)
check("rate under grace = zero deterrence", abs(under.civ_mult - 1.0) < 1e-9, "")

zero = Ledger()
zero.ingest(slain=0, resolved=0, gold=0)
check("zero resolved is neutral", abs(zero.civ_mult - 1.0) < 1e-9 and zero.appeal == 0.0, "")

for led in (base, blood, gen, mix, dec):
    sh = intent_shares(led)
    check("shares valid for a window", all(0.0 <= v <= 1.0 for v in sh.values())
          and abs(sum(sh.values()) - 1.0) < 1e-9, "")

W_TH_BASE, W_DEL_TYPE = 3.0, 5.0
th_share_base = W_TH_BASE / (W_TH_BASE + W_DEL_TYPE)
th_share_gen = (W_TH_BASE + gen.appeal) / (W_TH_BASE + gen.appeal + W_DEL_TYPE)
check("appeal lifts TH share within Delver", th_share_gen > th_share_base,
      "%.3f -> %.3f" % (th_share_base, th_share_gen))

print()
if fails:
    print("RED: %d failing check(s)." % len(fails))
    raise SystemExit(1)
print("GREEN: all appeal-weight checks pass.")
