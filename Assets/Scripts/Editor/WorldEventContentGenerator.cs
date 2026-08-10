using System.IO;
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
