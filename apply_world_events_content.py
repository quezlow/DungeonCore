#!/usr/bin/env python3
"""apply_world_events_content.py -- delivery script 2 of 2 for the
Random World Events framework (canon 37). Run AFTER
apply_world_events_framework.py.

Creates:
  Assets/Scripts/Editor/WorldEventContentGenerator.cs
  Docs/DCR_Guide_World_Events.html
Edits:
  Assets/Scripts/Monster/RespawnTicker.cs        (respawn multiplier hook)
  Assets/Scripts/Adventurer/AdventurerSpawner.cs (both civMult sites)
  Assets/Scripts/TESTING/Commands.cs             (Print World Events)
  Docs/DESIGN_CANON.md   (entry 37; entry-18 bullet flip; two hygiene
                          riders: entry-35 refiled out from under the
                          APPENDIX marker, Contents gains 35/36/37)
  Docs/DCR_Guide_Content_Authoring.html          (map row, Rev 9, chapter 34)

Run from the repo root:  python3 apply_world_events_content.py
All edits stage in memory; any failed assertion leaves the tree untouched.
Idempotent: a second run aborts cleanly.

AFTER PUSHING, TWO MANUAL UNITY STEPS -- BOTH FAIL SILENTLY IF SKIPPED:
  1. Add the WorldEventDirector component to the persistent manager
     GameObject (beside HolyOrderStrike and the other threat managers).
  2. Run Dungeon Core -> Generate World Events once.
Then right-click Commands -> Print World Events to prove both took.
"""
import io, os, sys

ROOT = os.path.dirname(os.path.abspath(__file__))

GEN = """using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the world event assets under Resources/Events/World, following the
/// wisp-quest generator's pattern: list-driven, idempotent (existing assets
/// are updated in place, references survive), rerun after any spec change.
///
/// Adding an event on an existing effect kind is ONE spec row here plus a
/// regenerate - no code elsewhere. A new effect kind is a new enum value on
/// WorldEventEffectKind (append-only) plus one case in
/// WorldEventDirector.Fire. Cadence maths lives in
/// Tools/sim_world_events.py; mirror any tuning change there and rerun it.
/// </summary>
public static class WorldEventContentGenerator
{
    private struct Spec
    {
        public string id, msg;
        public AlertCategory cat;
        public AlertSeverity sev;
        public bool hostile;
        public int minDay, cooldown, duration, goldMin, goldMax;
        public float minNotoriety, minRating, weight, magnitude;
        public WorldEventEffectKind kind;
    }

    private static readonly Spec[] specs =
    {
        // The murrain: a sickness thins the broods; respawns run at half
        // pace for three days. Warning-severity Threat - it hurts, but no
        // party is marching.
        new Spec
        {
            id = "we_murrain",
            msg = "A murrain creeps through the broods, little core. The " +
                  "flesh mends slowly while it lingers.",
            cat = AlertCategory.Threat, sev = AlertSeverity.Warning,
            minDay = 15, cooldown = 10, weight = 1f,
            kind = WorldEventEffectKind.RespawnRate,
            magnitude = 0.5f, duration = 3,
        },
        // The pilgrim surge: word of wonders below swells the civilian
        // lanes half again for two days. Stacks multiplicatively beside the
        // appeal ledger's civilian multiplier at the same weight sites.
        new Spec
        {
            id = "we_pilgrim_surge",
            msg = "Word of wonders below has spread, little core. The " +
                  "faithful and the curious come thick on the roads.",
            cat = AlertCategory.Discovery, sev = AlertSeverity.Info,
            minDay = 10, cooldown = 8, weight = 1f,
            kind = WorldEventEffectKind.CivilianWeight,
            magnitude = 1.5f, duration = 2,
        },
        // The tremor: the earth shifts and a seam of old glitter falls
        // within reach - an instant gold grant, nothing more.
        new Spec
        {
            id = "we_tremor",
            msg = "The deep rock groans and shifts, little core, and a " +
                  "seam of old glitter falls within your reach.",
            cat = AlertCategory.Discovery, sev = AlertSeverity.Info,
            minDay = 6, cooldown = 6, weight = 1.5f,
            kind = WorldEventEffectKind.GrantGold,
            magnitude = 1f, goldMin = 40, goldMax = 80,
        },
    };

    [MenuItem("Dungeon Core/Generate World Events")]
    public static void Generate()
    {
        const string dir = "Assets/Resources/Events/World";
        Directory.CreateDirectory(dir);

        foreach (var s in specs)
        {
            string assetPath = dir + "/" + s.id + ".asset";
            var e = AssetDatabase.LoadAssetAtPath<WorldEventDefinition>(assetPath);
            if (e == null)
            {
                e = ScriptableObject.CreateInstance<WorldEventDefinition>();
                AssetDatabase.CreateAsset(e, assetPath);
            }

            e.alertMessage = s.msg;
            e.alertCategory = s.cat;
            e.alertSeverity = s.sev;
            e.hostile = s.hostile;
            e.minDay = s.minDay;
            e.minNotoriety = s.minNotoriety;
            e.minRating = s.minRating;
            e.cooldownDays = s.cooldown;
            e.weight = s.weight;
            e.effectKind = s.kind;
            e.magnitude = s.magnitude;
            e.durationDays = s.duration;
            e.goldMin = s.goldMin;
            e.goldMax = s.goldMax;

            EditorUtility.SetDirty(e);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[WorldEventContentGenerator] Authored " + specs.Length
            + " world events under " + dir + ".");
    }
}
"""

GUIDE = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>DCR Guide: World Events</title>
<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@500;700&family=Crimson+Text:ital,wght@0,400;0,600;1,400&family=JetBrains+Mono:wght@400;600&display=swap" rel="stylesheet">
<style>
  :root { --bg:#0d0d1a; --panel:#16162a; --panel2:#1c1c33; --accent:#e94560; --gold:#c8902a; --text:#d8d4c8; --dim:#8a8798; --code-bg:#111122; }
  * { box-sizing:border-box; }
  body { margin:0; background:var(--bg); color:var(--text); font-family:'Crimson Text',serif; font-size:18px; line-height:1.6; }
  .progress-wrap { position:sticky; top:0; z-index:50; background:var(--bg); border-bottom:1px solid #2a2a44; padding:10px 24px; }
  .progress-bar { height:8px; background:var(--panel2); border-radius:4px; overflow:hidden; }
  .progress-fill { height:100%; width:0; background:linear-gradient(90deg,var(--accent),var(--gold)); transition:width .25s; }
  .progress-text { font-family:'JetBrains Mono',monospace; font-size:12px; color:var(--dim); margin-top:6px; display:flex; justify-content:space-between; }
  .reset-btn { background:none; border:1px solid var(--dim); color:var(--dim); font-family:'JetBrains Mono',monospace; font-size:11px; padding:2px 10px; border-radius:4px; cursor:pointer; }
  .reset-btn:hover { border-color:var(--accent); color:var(--accent); }
  .container { max-width:920px; margin:0 auto; padding:24px; }
  h1 { font-family:'Cinzel',serif; color:var(--gold); font-size:34px; margin:18px 0 4px; }
  .subtitle { color:var(--dim); font-style:italic; margin-bottom:22px; }
  details { background:var(--panel); border:1px solid #2a2a44; border-radius:8px; margin:14px 0; overflow:hidden; }
  summary { font-family:'Cinzel',serif; font-size:20px; color:var(--accent); padding:14px 18px; cursor:pointer; user-select:none; }
  summary:hover { background:var(--panel2); }
  .body { padding:4px 22px 18px; border-top:1px solid #2a2a44; }
  code { font-family:'JetBrains Mono',monospace; font-size:14.5px; background:var(--code-bg); color:var(--gold); padding:1px 6px; border-radius:4px; }
  pre { background:var(--code-bg); border:1px solid #2a2a44; border-radius:6px; padding:14px; overflow-x:auto; font-family:'JetBrains Mono',monospace; font-size:13.5px; line-height:1.5; color:#c8d4e8; }
  pre code { background:none; padding:0; color:inherit; }
  .step { display:flex; gap:12px; align-items:flex-start; background:var(--panel2); border-radius:6px; padding:10px 14px; margin:10px 0; }
  .step input[type="checkbox"] { margin-top:6px; accent-color:var(--accent); width:16px; height:16px; flex-shrink:0; }
  .step label { cursor:pointer; }
  .callout { border-left:3px solid var(--gold); background:var(--panel2); padding:10px 16px; margin:12px 0; border-radius:0 6px 6px 0; }
  .callout.warn { border-left-color:var(--accent); }
  .wisp { color:#9ec9e8; font-style:italic; }
  b { color:#f0ead8; }
  table { border-collapse:collapse; width:100%; margin:10px 0; font-size:16px; }
  th, td { border:1px solid #2a2a44; padding:6px 10px; text-align:left; }
  th { font-family:'Cinzel',serif; color:var(--gold); font-weight:500; background:var(--panel2); }
body{overflow-wrap:break-word;}
code,kbd{overflow-wrap:anywhere;}
pre{max-width:100%;overflow-x:auto;}
table{max-width:100%;}
th,td{overflow-wrap:anywhere;}
td code{white-space:normal;}
.step{min-width:0;}
.step label{min-width:0;}
.step pre{max-width:100%;}
summary{min-width:0;}
summary h1,summary h2,summary h3{min-width:0;overflow-wrap:anywhere;}
</style>
</head>
<body>
<div class="progress-wrap">
  <div class="progress-bar"><div class="progress-fill" id="fill"></div></div>
  <div class="progress-text"><span id="ptext">0 / 0 steps complete</span><button class="reset-btn" id="reset">reset checkmarks</button></div>
</div>
<div class="container">
<h1>World Events</h1>
<p class="subtitle">Canon 37 &mdash; the world's weather: a data-driven dispatcher rolling small random events at dawn, so a new event is an asset entry rather than a component. Ships with the murrain, the pilgrim surge, and the tremor.</p>

<details open>
<summary>1. What shipped, in one breath</summary>
<div class="body">
  <p><code>WorldEventDirector</code> (one component beside the threat managers) self-populates from
  <code>Resources/Events/World</code> and rolls each dawn: tick the active timed effects, burn the
  global cooldown, gather eligible events, roll a daily fire chance of <b>0.25</b>, then one
  weighted draw. Global cooldown <b>3 days</b> between any two events; the tuning lands
  <b>4&ndash;5 events per 30 eligible days</b>, validated across seeds by
  <code>Tools/sim_world_events.py</code> (14 checks). The C# mirrors that file's dawn ordering
  exactly &mdash; the sim is the specification.</p>
  <p><b>Deliberately greenfield.</b> The four bespoke threats (<code>HolyOrderStrike</code>,
  <code>MercenaryContract</code>, <code>NobleRetaliation</code>, <code>WildMonsterEvent</code>) are
  byte-identical to before this arc: each is a tuned state machine, and a registry generic enough
  to hold them would be a rewrite of tuned behaviour for no player-visible gain. The Wandering
  Merchant keeps its own arrival controller. The <code>hostile</code> flag on a definition is the
  slot a future assault-shaped event uses to honour <code>SuppressMidGameThreats</code>; none of
  the v1 trio carries it, so the world keeps its weather even through the climax.</p>
  <div class="callout">No autosave on fire. These are weather, not assaults &mdash; the threat
  components autosave because a raid is a run-defining moment; a two-day pilgrim surge is not.</div>
</div>
</details>

<details>
<summary>2. Scene setup &mdash; two manual steps, both silent when skipped</summary>
<div class="body">
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c2s1"><label for="dcr-worldevents-v1-c2s1"><b>Add the component.</b> <code>WorldEventDirector</code> goes on the persistent manager GameObject, beside <code>HolyOrderStrike</code> and the other threat managers. Its two inspector fields (<code>dailyFireChance</code> 0.25, <code>globalCooldownDays</code> 3) carry the sim-validated defaults &mdash; retune through chapter 5, not by feel.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c2s2"><label for="dcr-worldevents-v1-c2s2"><b>Generate the assets.</b> Dungeon Core &rarr; <b>Generate World Events</b> authors the three definitions under <code>Assets/Resources/Events/World</code>. The generator is idempotent &mdash; rerun it after any spec change and existing assets update in place.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c2s3"><label for="dcr-worldevents-v1-c2s3"><b>Prove both took.</b> Right-click the <code>Commands</code> component &rarr; <b>Print World Events</b>. It names every loaded event with its fire history, or tells you the folder came up empty. No component or no assets means no events and <b>no error</b> &mdash; the wisp-asset lesson, so run the print before wondering why nothing ever happens.</label></div>
</div>
</details>

<details>
<summary>3. The dawn machine</summary>
<div class="body">
  <p>The order matters and is fixed; <code>sim_world_events.py</code> asserts it:</p>
  <pre><code>1. tick active timed effects   (decrement, expire, recompute multipliers)
2. global cooldown holds?      (decrement and stop -- nothing rolls today)
3. gather eligible events      (gates, per-event cooldown, not already
                                active; climax strips HOSTILE events only)
4. roll dailyFireChance        (0.25 -- most dawns are quiet)
5. one weighted draw, fire     (event + global cooldowns re-arm)</code></pre>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c3s1"><label for="dcr-worldevents-v1-c3s1"><b>Timed effects count the fire day as day one.</b> A 3-day murrain fired at dawn 20 holds through dawns 21 and 22 and expires at the dawn-23 tick. The per-event cooldown is clamped to at least the duration in <code>OnValidate</code>, so an effect can never re-fire over itself.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c3s2"><label for="dcr-worldevents-v1-c3s2"><b>Day 1 is never heard.</b> <code>DayNightCycle</code> (execution order -90) fires its day-1 <code>OnDayStarted</code> in its own <code>Start</code>, before the director's <code>Start</code> subscribes &mdash; the threats share this idiom deliberately. With <code>minDay</code> floors of 6+, nothing is lost.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c3s3"><label for="dcr-worldevents-v1-c3s3"><b>Consumers read two statics that default to 1.</b> <code>RespawnRateMultiplier</code> multiplies into <code>RespawnTicker</code>'s per-spawner tick beside the room-effect multiplier; <code>CivilianWeightMultiplier</code> multiplies beside <code>DungeonAppealLedger.CivilianMultiplier</code> at <b>both</b> intent-weight sites in <code>AdventurerSpawner</code> (roll + foresight), so the WavePreviewHUD stays honest. Both hooks are inert until events exist.</label></div>
</div>
</details>

<details>
<summary>4. The v1 trio</summary>
<div class="body">
  <table>
    <tr><th>Event</th><th>Gates</th><th>Cadence</th><th>Effect</th><th>Alert</th></tr>
    <tr><td><code>we_murrain</code></td><td>day 15+</td><td>cd 10, weight 1.0</td><td>respawn x0.5 for 3 days</td><td>Threat / Warning</td></tr>
    <tr><td><code>we_pilgrim_surge</code></td><td>day 10+</td><td>cd 8, weight 1.0</td><td>civilian lanes x1.5 for 2 days</td><td>Discovery / Info</td></tr>
    <tr><td><code>we_tremor</code></td><td>day 6+</td><td>cd 6, weight 1.5</td><td>instant 40&ndash;80 gold</td><td>Discovery / Info</td></tr>
  </table>
  <p class="wisp">"A murrain creeps through the broods, little core. The flesh mends slowly while it lingers."</p>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c4s1"><label for="dcr-worldevents-v1-c4s1"><b>Each one exercises a different framework limb.</b> The murrain proves timed-effect persistence and the respawn hook; the surge proves a second consumer stacking multiplicatively beside the appeal ledger; the tremor proves the instant path with zero new hooks. When you smoke-test, that is the coverage you are actually buying.</label></div>
  <div class="callout warn"><b>The tremor is the honest reshape of "earthquake vein reveal".</b> No mineral veins exist anywhere in the codebase &mdash; that design was a resource system wearing an event's clothes. An abandoned free-loot chest was rejected too: chests are player-placed bait feeding the Treasure-Hunter tier scan and the appeal loop, and a world-spawned one needs a placement solver while muddying a tuned economy.</div>
</div>
</details>

<details>
<summary>5. Authoring, tuning, verifying</summary>
<div class="body">
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c5s1"><label for="dcr-worldevents-v1-c5s1"><b>New event, existing kind: assets only.</b> One <code>Spec</code> row in <code>Editor/WorldEventContentGenerator.cs</code>, regenerate, done. The asset name IS the save-facing id &mdash; renaming one orphans its cooldown history, so pick it once. Full recipe: Content Authoring guide, chapter 34.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c5s2"><label for="dcr-worldevents-v1-c5s2"><b>New kind: one enum value, one switch case.</b> Append to <code>WorldEventEffectKind</code> (append-only &mdash; it serialises into .asset files) and add the case in <code>WorldEventDirector.Fire</code>, the single place kinds become behaviour.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c5s3"><label for="dcr-worldevents-v1-c5s3"><b>Retune through the sim.</b> Any change to gates, weights, <code>dailyFireChance</code> or the global cooldown: mirror it in <code>Tools/sim_world_events.py</code>, rerun (<code>python3 Tools/sim_world_events.py</code>), and read the cadence check &mdash; 4&ndash;5 events per 30 eligible days is the feel of the system in one number. The sim is the specification; the C# follows it, never the reverse.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c5s4"><label for="dcr-worldevents-v1-c5s4"><b>Save/load is the trap the code already dodged.</b> <code>DayNightCycle.LoadSaveData</code> deliberately never re-fires <code>OnDayStarted</code>, so <code>RestoreFromSave</code> recomputes the multipliers itself &mdash; without that a saved murrain would load cured. Verify in play: save mid-murrain, reload, and <b>Print World Events</b> must still show it ACTIVE with days left and respawn x0.5.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c5s5"><label for="dcr-worldevents-v1-c5s5"><b>Persistence shape.</b> <code>WorldEventsSaveData</code> (additive on <code>DungeonSaveData</code>, parallel lists &mdash; JsonUtility takes no dictionaries) keys everything by string id: a retired event's ledger entry is harmless on load, and an active effect whose asset is gone is dropped. New-game reset rides <code>InitializeNewGame</code> beside the merchant's.</label></div>
</div>
</details>

<details>
<summary>6. Update the Canon</summary>
<div class="body">
  <p>The delivery script applies all of this to <code>Docs/DESIGN_CANON.md</code>; nothing here is
  a manual step. Recorded for the ritual:</p>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c6s1"><label for="dcr-worldevents-v1-c6s1"><b>Entry 37 added</b> ("Random World Events (The World's Weather)"), before the Appendix: the dawn machine, the greenfield decision, the effect-kind boundary, the two consumer statics, the v1 trio, persistence, the two silent scene steps, key files, and the rejections (threat migration, vein reveal as designed, the free chest).</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c6s2"><label for="dcr-worldevents-v1-c6s2"><b>Entry 18's deferred bullet flipped</b> to SHIPPED with a pointer at entry 37 &mdash; the "no scheduler, event registry or data-driven authoring surface" sentence is no longer true and no longer said.</label></div>
  <div class="step"><input type="checkbox" id="dcr-worldevents-v1-c6s3"><label for="dcr-worldevents-v1-c6s3"><b>Two hygiene riders.</b> Entry 35 (Monster Mutations) had been filed <em>under</em> the <code># APPENDIX</code> marker by an earlier insert &mdash; refiled in numeric order between 34 and 36. And the Contents list, which stopped at 34, gains lines for 35, 36 and 37.</label></div>
</div>
</details>

</div>
<script>
(function () {
  var boxes = Array.prototype.slice.call(document.querySelectorAll('input[type="checkbox"]'));
  var fill = document.getElementById('fill');
  var text = document.getElementById('ptext');
  var reset = document.getElementById('reset');

  function refresh() {
    var done = boxes.filter(function (b) { return b.checked; }).length;
    var pct = boxes.length ? Math.round(100 * done / boxes.length) : 0;
    fill.style.width = pct + '%';
    text.textContent = done + ' / ' + boxes.length + ' steps complete';
  }

  boxes.forEach(function (b) {
    try {
      if (localStorage.getItem(b.id) === '1') b.checked = true;
    } catch (e) {}
    b.addEventListener('change', function () {
      try { localStorage.setItem(b.id, b.checked ? '1' : '0'); } catch (e) {}
      refresh();
    });
  });

  reset.addEventListener('click', function () {
    boxes.forEach(function (b) {
      b.checked = false;
      try { localStorage.removeItem(b.id); } catch (e) {}
    });
    refresh();
  });

  refresh();
})();
</script>
</body>
</html>
"""

ENTRY37 = """## 37. Random World Events (The World's Weather)

Status: BUILT. Verified: pending smoke test.

The deferred framework from entry 18, revisited and shipped: a data-driven
dispatcher so a new world event is an asset entry, not a component.
`Gameplay/WorldEventDirector.cs` (one component beside the threat managers)
self-populates from `Resources/Events/World` (authored by Dungeon Core ->
Generate World Events, `Editor/WorldEventContentGenerator.cs`) and rolls at
dawn: tick active timed effects, burn the global cooldown, gather eligible
events (minDay / minNotoriety / minRating gates, per-event cooldown, not
already active; climax suppression strips HOSTILE-flagged events only), roll
the daily fire chance (0.25), then one weighted draw. Global cooldown 3 days
between any two events; the tuning lands 4-5 events per 30 eligible days,
validated by `Tools/sim_world_events.py` (14 checks: gates, both cooldowns,
no self-overlap, weighted proportions, cadence band, determinism, save/load
mid-effect without refire, expiry, hostile-only suppression). The C# mirrors
that file's dawn ordering exactly; rerun it when the tuning or the ordering
changes.

**Deliberately greenfield.** The four bespoke threats (`HolyOrderStrike`,
`MercenaryContract`, `NobleRetaliation`, `WildMonsterEvent`) are untouched:
each is a tuned state machine of its own, and folding them into a generic
registry would rewrite tuned behaviour for no player-visible gain. The
Wandering Merchant keeps its own arrival controller. The `hostile` flag on a
definition exists so a future assault-shaped event honours
`SuppressMidGameThreats`; none of the v1 trio carries it.

**Effects are the honest data boundary.** `WorldEventEffectKind`
(append-only -- it serialises into .asset files) names what an event does;
the director's `Fire` switch is the single place kinds become behaviour. A
new event on an existing kind is one generator spec row plus a regenerate; a
new kind is one enum value plus one switch case. Timed kinds hold a
multiplier for durationDays (the fire day counts as the first); the
per-event cooldown is clamped to at least the duration so an effect can
never overlap itself, and same-kind effects from DIFFERENT events stack
multiplicatively by design.

**Consumers read two cached statics** that default to 1 with no instance, so
the hooks are inert until events exist: `RespawnRateMultiplier` multiplies
into `RespawnTicker`'s per-spawner tick beside the room-effect multiplier,
and `CivilianWeightMultiplier` multiplies beside
`DungeonAppealLedger.CivilianMultiplier` at BOTH intent-weight sites in
`AdventurerSpawner` (roll + foresight) so WavePreviewHUD stays honest -- the
appeal ledger's same-sites rule applied again.

**The v1 trio:** `we_murrain` (day 15+, cooldown 10, respawn x0.5 for 3
days; Threat/Warning), `we_pilgrim_surge` (day 10+, cooldown 8, civilian
x1.5 for 2 days; Discovery/Info), `we_tremor` (day 6+, cooldown 6, weight
1.5, instant 40-80 gold; Discovery/Info). No autosave on fire: these are
weather, not assaults -- the threat components autosave because a raid is a
run-defining moment.

**Persistence:** `WorldEventsSaveData` (additive on `DungeonSaveData`;
parallel lists, JsonUtility takes no dictionaries) carries the global
cooldown, per-event lastFiredDay / timesFired keyed by STRING id (the asset
name, never an enum index -- a retired event's entry is harmless on load,
and an active effect whose asset is gone is dropped), and the active effects
with days remaining. Restore recomputes the multipliers immediately, because
`DayNightCycle.LoadSaveData` deliberately never re-fires OnDayStarted --
without that a saved murrain would load cured. New-game reset rides
`DungeonSaveController.InitializeNewGame` beside the merchant's, since the
director carries scheduling state exactly as the merchant does. Diagnostics:
a log line per fire and per expiry, and "Print World Events" in `Commands`.

**Scene setup is two manual steps** and both fail silently if skipped: the
`WorldEventDirector` component goes on the persistent manager GameObject
beside the threat managers, and Dungeon Core -> Generate World Events must
run once to author the three assets. No component or no assets means no
events and no error -- the wisp-asset lesson.

**Key files:** `Gameplay/WorldEventDefinition.cs`,
`Gameplay/WorldEventDirector.cs`, `Editor/WorldEventContentGenerator.cs`,
`Monster/RespawnTicker.cs` (one-line hook),
`Adventurer/AdventurerSpawner.cs` (the two civMult sites),
`TESTING/Commands.cs`, `Save/DungeonSaveData.cs`,
`Save/DungeonSaveController.cs`, `Tools/sim_world_events.py`.

**Rejected:** migrating the shipped threats (above). An "earthquake vein
reveal" as first designed -- no mineral veins exist anywhere in the
codebase, so it was a resource system wearing an event's clothes; the
tremor's instant gold grant is its honest reshape. An abandoned free-loot
chest -- chests are player-placed bait feeding the Treasure-Hunter tier
scan and the appeal loop, and a world-spawned one needs a placement solver
while muddying a tuned economy.

---

"""

CHAPTER34 = """  <details>
    <summary>34. World Events (The World's Weather)</summary>
    <div class="body">
      <div class="why">Canon 37. Small random events -- a murrain, a pilgrim surge, a tremor --
      rolled at dawn from data. A new event on an existing effect kind is ONE spec row in the
      generator; the four bespoke threats and the merchant stay bespoke by design and are not
      authored here. Cadence maths lives in <code>Tools/sim_world_events.py</code>; the director
      mirrors that file's dawn ordering exactly.</div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c34s1"><label for="dcr-authoring-v1-c34s1"><b>Adding an event.</b> One <code>Spec</code> row in <code>Editor/WorldEventContentGenerator.cs</code>: id (the asset name IS the save-facing id -- rename an asset and its cooldown history is orphaned, so pick the id once), the wisp-voiced alert line, category + severity, the gates (<code>minDay</code> / <code>minNotoriety</code> / <code>minRating</code>, 0 = ungated), <code>cooldownDays</code>, <code>weight</code>, and the effect. Then Dungeon Core &rarr; Generate World Events. The generator is idempotent: existing assets update in place.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c34s2"><label for="dcr-authoring-v1-c34s2"><b>The effect-kind boundary.</b> <code>RespawnRate</code> and <code>CivilianWeight</code> hold <code>magnitude</code> for <code>durationDays</code> (the fire day counts as the first); <code>GrantGold</code> rolls <code>goldMin..goldMax</code> once; <code>None</code> fires the alert alone -- pure flavour is a legal event. A NEW kind is code: one value appended to <code>WorldEventEffectKind</code> (append-only -- it serialises into .asset files) plus one case in <code>WorldEventDirector.Fire</code>, the single place kinds become behaviour.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c34s3"><label for="dcr-authoring-v1-c34s3"><b>Cooldown covers duration by construction.</b> <code>OnValidate</code> clamps <code>cooldownDays</code> up to <code>durationDays</code>, so a timed effect can never re-fire over itself. Same-kind effects from DIFFERENT events stack multiplicatively -- if you author a second respawn-touching event, decide whether a murrain plus your event at once reads fair before shipping it.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c34s4"><label for="dcr-authoring-v1-c34s4"><b>Two scene steps, both silent when skipped.</b> The <code>WorldEventDirector</code> component must sit on the persistent manager GameObject beside the threat managers, and the generator must have run once. No component or an empty <code>Resources/Events/World</code> means no events and NO error -- the wisp-asset lesson. "Print World Events" in <code>Commands</code> is the check: it names every loaded event or says the folder came up empty.</label></div>

      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c34s5"><label for="dcr-authoring-v1-c34s5"><b>Retune with the sim, not in the inspector.</b> Any change to gates, weights, the daily chance (0.25) or the global cooldown (3): mirror it in <code>Tools/sim_world_events.py</code> and rerun -- the cadence-band check (4-5 events per 30 eligible days) is the feel of the whole system in one number. <code>hostile</code> marks an event the endgame climax should silence; the v1 trio carries none, and an assault-shaped event later must.</label></div>
    </div>
  </details>"""


EDITS = [
    ("Assets/Scripts/Monster/RespawnTicker.cs",
     "                s.TickRespawn(dt * RoomEffectCensus.GetRespawnMultiplier(s));\n",
     "                // World events (the murrain) slow every brood at once; room\n"
     "                // effects stay per-spawner. Both default to 1.\n"
     "                s.TickRespawn(dt * RoomEffectCensus.GetRespawnMultiplier(s)\n"
     "                              * WorldEventDirector.RespawnRateMultiplier);\n"),
    ("Assets/Scripts/Adventurer/AdventurerSpawner.cs",
     "        // Keep in sync with RollIntent: appeal ledger shaping.\n"
     "        float civMult = DungeonAppealLedger.CivilianMultiplier;\n",
     "        // Keep in sync with RollIntent: appeal ledger + world event shaping.\n"
     "        float civMult = DungeonAppealLedger.CivilianMultiplier\n"
     "                        * WorldEventDirector.CivilianWeightMultiplier;\n"),
    ("Assets/Scripts/Adventurer/AdventurerSpawner.cs",
     "        // sparser rather than empty.\n"
     "        float civMult = DungeonAppealLedger.CivilianMultiplier;\n",
     "        // sparser rather than empty. World events (the pilgrim surge)\n"
     "        // multiply in beside the ledger at BOTH weight sites, so the\n"
     "        // WavePreviewHUD foresight stays honest.\n"
     "        float civMult = DungeonAppealLedger.CivilianMultiplier\n"
     "                        * WorldEventDirector.CivilianWeightMultiplier;\n"),
    ("Assets/Scripts/TESTING/Commands.cs",
     "    [ContextMenu(\"Print Appeal Ledger\")]\n"
     "    void TestPrintAppealLedger() => DungeonAppealLedger.PrintAppeal();\n",
     "    [ContextMenu(\"Print Appeal Ledger\")]\n"
     "    void TestPrintAppealLedger() => DungeonAppealLedger.PrintAppeal();\n\n"
     "    [ContextMenu(\"Print World Events\")]\n"
     "    void TestPrintWorldEvents() => WorldEventDirector.PrintState();\n"),
    ("Docs/DCR_Guide_Content_Authoring.html",
     "      <tr><td>Site relations</td><td>headers in the plan <code>.txt</code> (ch 33)</td><td><code>ScriptableObjects/Sites/Plans</code></td><td>nothing -- resolved at placement time; audited by <code>Tools/audit_plan_tags.py</code></td><td>n/a</td></tr>",
     "      <tr><td>Site relations</td><td>headers in the plan <code>.txt</code> (ch 33)</td><td><code>ScriptableObjects/Sites/Plans</code></td><td>nothing -- resolved at placement time; audited by <code>Tools/audit_plan_tags.py</code></td><td>n/a</td></tr>\n"
     "      <tr><td>World event</td><td><code>Dungeon/World Event Definition</code> (or the generator, ch 34)</td><td><code>Resources/Events/World</code></td><td>nothing -- <code>WorldEventDirector</code> self-populates</td><td>asset name (save-facing id)</td></tr>"),
    ("Docs/DCR_Guide_Content_Authoring.html",
     " Rider: <code>_SYMBOLS.txt</code> gains the relation headers.</div>",
     " Rider: <code>_SYMBOLS.txt</code> gains the relation headers.\n"
     "      <br><b>Rev 9 (2026-08-09):</b> chapter 34 added -- World Events (canon 37): the generator recipe, the effect-kind boundary, and the two silent-failure scene steps. Chapter 0 map row added.</div>"),
    ("Docs/DESIGN_CANON.md",
     "34. The Core's Own Past (Persisted Life, Memory Echoes)\n\n**Appendix**",
     "34. The Core's Own Past (Persisted Life, Memory Echoes)\n"
     "35. Monster Mutations (Bestiary upgrade line)\n"
     "36. Built Walls and the Sealed Way\n"
     "37. Random World Events (The World's Weather)\n\n**Appendix**"),
    ("Docs/DESIGN_CANON.md",
     "- *Random world events framework:* DEFERRED, to be revisited. What exists is\n"
     "  three bespoke recurring threats, each its own component --\n"
     "  `HolyOrderStrike`, `MercenaryContract`, `WildMonsterEvent` (entry 8).\n"
     "  There is no scheduler, event registry or data-driven authoring surface,\n"
     "  and the Wandering Merchant runs its own arrival controller rather than\n"
     "  riding a shared one.\n",
     "- *Random world events framework:* SHIPPED -- see entry 37. The\n"
     "  dispatcher, registry and data-driven authoring surface exist\n"
     "  (`WorldEventDirector` + assets under `Resources/Events/World`); the\n"
     "  bespoke threats stayed bespoke by design, and the Wandering Merchant\n"
     "  keeps its own arrival controller.\n"),
]


def load(rel):
    raw = io.open(os.path.join(ROOT, rel), "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    txt = raw.decode("utf-8")
    crlf = "\r\n" in txt
    if crlf:
        txt = txt.replace("\r\n", "\n")
    return txt, crlf, bom


def store(rel, txt, crlf, bom):
    if crlf:
        txt = txt.replace("\n", "\r\n")
    data = txt.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(os.path.join(ROOT, rel), "wb").write(data)


def refile_entry_35(txt):
    """Hygiene rider: an earlier insert filed entry 35 UNDER the # APPENDIX
    marker. Move it into numeric order between 34 and 36. Index-based
    extraction with count==1 assertions on every marker."""
    start = "# APPENDIX\n\n## 35. Monster Mutations (Bestiary upgrade line)\n"
    if txt.count(start) != 1:
        return None, f"entry-35 start marker count {txt.count(start)}"
    i = txt.index(start)
    end_marker = "\n## A. Content Registries and Authoring Keys"
    if txt.count(end_marker) != 1:
        return None, f"entry-35 end marker count {txt.count(end_marker)}"
    j = txt.index(end_marker, i)
    block35 = txt[i + len("# APPENDIX\n\n"):j].rstrip("\n")
    txt = txt[:i] + "# APPENDIX\n" + txt[j:]
    dest = "---\n\n## 36. Built Walls and the Sealed Way\n"
    if txt.count(dest) != 1:
        return None, f"entry-36 destination anchor count {txt.count(dest)}"
    txt = txt.replace(dest, "---\n\n" + block35 + "\n\n---\n\n## 36. Built Walls and the Sealed Way\n")
    return txt, None


def main():
    canon_txt, canon_crlf, canon_bom = load("Docs/DESIGN_CANON.md")
    if "## 37. Random World Events" in canon_txt:
        print("Already applied -- aborting with the tree untouched.")
        return 0

    if "WorldEventsSaveData" not in load("Assets/Scripts/Save/DungeonSaveData.cs")[0]:
        print("REFUSED: run apply_world_events_framework.py first.")
        return 1

    new_files = {
        "Assets/Scripts/Editor/WorldEventContentGenerator.cs": GEN,
        "Docs/DCR_Guide_World_Events.html": GUIDE,
    }
    for rel in new_files:
        if os.path.exists(os.path.join(ROOT, rel)):
            print(f"REFUSED: {rel} already exists.")
            return 1

    # Stage all plain edits in memory, asserting every anchor first.
    staged = {}
    for rel, old, new in EDITS:
        if rel in staged:
            txt, crlf, bom = staged[rel]
        elif rel == "Docs/DESIGN_CANON.md":
            txt, crlf, bom = canon_txt, canon_crlf, canon_bom
        else:
            txt, crlf, bom = load(rel)
        n = txt.count(old)
        if n != 1:
            print(f"ANCHOR FAULT in {rel}: expected 1, found {n}. Nothing written.")
            return 1
        staged[rel] = (txt.replace(old, new), crlf, bom)

    # Canon: refile entry 35, then insert entry 37 before the APPENDIX marker.
    txt, crlf, bom = staged["Docs/DESIGN_CANON.md"]
    txt, err = refile_entry_35(txt)
    if err:
        print(f"ANCHOR FAULT in Docs/DESIGN_CANON.md: {err}. Nothing written.")
        return 1
    anchor = "---\n\n# APPENDIX\n"
    if txt.count(anchor) != 1:
        print(f"ANCHOR FAULT in Docs/DESIGN_CANON.md: APPENDIX anchor count {txt.count(anchor)}. Nothing written.")
        return 1
    txt = txt.replace(anchor, "---\n\n" + ENTRY37 + "# APPENDIX\n")
    staged["Docs/DESIGN_CANON.md"] = (txt, crlf, bom)

    # Authoring guide: insert chapter 34 before the closing container div.
    txt, crlf, bom = staged["Docs/DCR_Guide_Content_Authoring.html"]
    tail_anchor = "  \n</div>\n\n<script>"
    if txt.count(tail_anchor) != 1:
        print(f"ANCHOR FAULT in Docs/DCR_Guide_Content_Authoring.html: tail anchor count {txt.count(tail_anchor)}. Nothing written.")
        return 1
    txt = txt.replace(tail_anchor, CHAPTER34 + "\n\n  \n</div>\n\n<script>")
    staged["Docs/DCR_Guide_Content_Authoring.html"] = (txt, crlf, bom)

    # Validate embedded C# before writing.
    for a, b in (("{", "}"), ("(", ")"), ("[", "]")):
        if GEN.count(a) != GEN.count(b):
            print(f"UNBALANCED {a}{b} in embedded generator. Nothing written.")
            return 1
    if [c for c in GEN if ord(c) > 127]:
        print("NON-ASCII in embedded generator. Nothing written.")
        return 1

    # All checks passed: write everything, then report.
    for rel, bodytext in new_files.items():
        path = os.path.join(ROOT, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        io.open(path, "w", encoding="utf-8", newline="\n").write(bodytext)
    for rel, (t, c, b) in staged.items():
        store(rel, t, c, b)

    print("apply_world_events_content: applied.")
    print("  created: Editor/WorldEventContentGenerator.cs, Docs/DCR_Guide_World_Events.html")
    print("  edited:  RespawnTicker, AdventurerSpawner (2 sites), Commands,")
    print("           DESIGN_CANON.md (entry 37 + bullet flip + 2 hygiene riders),")
    print("           DCR_Guide_Content_Authoring.html (map row, Rev 9, chapter 34)")
    print("")
    print("REMEMBER THE TWO MANUAL UNITY STEPS (silent if skipped):")
    print("  1. WorldEventDirector component -> persistent manager GameObject")
    print("  2. Dungeon Core -> Generate World Events")
    print("Prove both with Commands -> Print World Events.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
