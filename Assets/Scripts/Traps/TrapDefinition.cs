using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data asset for a trap type.
/// Create via: right-click → Create → Dungeon → Trap Definition
///
/// trapBehaviour determines which TrapBase subclass component is added to the
/// instantiated prefab — see TrapBase.EnsureBehaviour().
///
/// Future tiers (Day 25) add Pressure Plate, etc. — add new TrapBehaviour
/// enum values and corresponding TrapBase subclasses without changing this asset.
/// </summary>
[CreateAssetMenu(fileName = "NewTrapDefinition",
                 menuName = "Dungeon/Trap Definition")]
public class TrapDefinition : ScriptableObject
{
    public enum TrapBehaviour
    {
        SpikeTrap,    // damage on step
        Pitfall,      // damage + brief slow
        PressurePlate,
        Warning,      // Warning,
        CaptureTrap,  // snares the victim in place for capture (no damage)
        ScatterTrap,  // breaks a party's formation (no damage)
        Crossbow,       // sentry: watches a span of hall and looses real bolts
        Fireball,       // burst of flame around the cell, with a clinging burn
        IceSpikes,      // frost-bitten spikes: wound plus a cold that all but stills
        EarthSpikes,    // stone rams upward: heavy wound and a hurl backward
        GaleVent,       // a hammer of wind: hurled back, formation broken
        BlindingFlash,  // searing light: quarrels forgotten, trap-sense burned out
        UmbralSnare,    // clinging dark: recoil, slowness, senses dimmed
        SleepDart,      // a quiet needle: no wound, target lost, all but stilled
        SiphonRune,     // a tithing mark: small wound, mana returned to the core
    }

    [Header("Identity")]
    public string trapName = "Trap";
    public TrapBehaviour behaviour = TrapBehaviour.SpikeTrap;

    [Header("Access")]
    [Tooltip("Research key that must be unlocked before this trap lists in the picker. " +
             "Empty = available with trap mode itself (the spike-trap node).")]
    public string requiredTechKey = "";

    [Tooltip("None = neutral, every core may place it. Otherwise the trap is exclusive " +
             "to the matching core type: hidden from every other core's picker and tree, " +
             "and refused at placement as a backstop.")]
    public DungeonType affinity = DungeonType.None;

    [Header("Flagged Behaviour")]
    [Tooltip("Rogues can neutralise this trap once it is flagged.")]
    public bool disarmable = true;

    [Tooltip("Flagged cells of this trap carry the Dijkstra detour cost. Off for intel " +
             "traps, plates and sentries, whose cells are safe to walk.")]
    public bool detoursWhenFlagged = true;

    [Header("Prefab")]
    [Tooltip("Base prefab. Must have a TrapBase subclass component plus a SpriteRenderer.")]
    public TrapBase prefab;

    [Header("Placement")]
    public float manaCost = 8f;
    [Tooltip("Capacity this trap occupies while placed. Returned when the trap is removed.")]
    [Min(0)] public int capacityCost = 2;

    [Header("Trigger")]
    [Tooltip("Damage dealt to the adventurer on trigger.")]
    public float damage = 12f;

    [Tooltip("Seconds before the trap can fire again after triggering.")]
    public float cooldown = 3f;

    [Header("Pitfall Slow Effect (Pitfall only)")]
    [Tooltip("Movement speed multiplier applied on trigger (1.0 = no slow).")]
    public float slowMultiplier = 0.4f;
    [Tooltip("Duration of the slow effect in seconds.")]
    public float slowDuration = 2f;

    [Header("Capture (Capture Trap only)")]
    [Tooltip("Seconds a snared adventurer is pinned before the dungeon claims them into a " +
             "cell -- the window their party has to cut them loose. Uncapturable types take " +
             "the slow above instead.")]
    public float captureHoldSeconds = 10f;

    [Header("Scatter (Scatter Trap only)")]
    [Tooltip("Seconds a party's formation stays broken after stepping on the trap -- the shield wall is down and members disperse for this long.")]
    public float scatterSeconds = 5f;

    [Header("Sentry (Crossbow only)")]
    [Tooltip("World-space range the sentry watches and shoots across.")]
    public float sentryRange = 3.5f;
    [Tooltip("Bolt flight speed.")]
    public float projectileSpeed = 10f;
    [Tooltip("Bolt tint over the built-in glow sprite.")]
    public Color projectileTint = Color.white;

    [Header("Burst (Fireball / Blinding Flash)")]
    [Tooltip("World-space radius of the burst around the trap cell.")]
    public float burstRadius = 1.6f;

    [Header("Burn (Fireball only)")]
    [Tooltip("Damage per second of the clinging burn.")]
    public float burnDps = 4f;
    [Tooltip("Seconds the burn clings.")]
    public float burnSeconds = 3f;

    [Header("Knockback (Earth Spikes / Gale Vent / Umbral Snare)")]
    [Tooltip("Knockback distance applied away from the trap.")]
    public float knockbackForce = 1.5f;

    [Header("Blind (Blinding Flash / Sleep Dart)")]
    [Tooltip("Seconds the victim is all but stilled after losing its quarrel.")]
    public float blindHaltSeconds = 1.5f;
    [Tooltip("Seconds a flashed victim cannot detect or disarm traps.")]
    public float blindSenseSeconds = 8f;

    [Header("Dimmed Senses (Umbral Snare only)")]
    [Tooltip("Multiplier on the victim's monster-detection range while dimmed.")]
    public float senseDampMultiplier = 0.5f;
    [Tooltip("Seconds the dimming lasts.")]
    public float senseDampSeconds = 6f;

    [Header("Siphon (Siphon Rune only)")]
    [Tooltip("Mana returned to the core each trigger. Adventurers only -- wild " +
             "monsters take the wound but pay no tithe.")]
    public float manaGain = 10f;

    [Header("Visuals")]
    public Sprite icon;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;

    /// <summary>
    /// Returns one-line stat strings for display in TrapSelectionUI.
    /// Behaviour-specific stats (e.g. slow for Pitfall) are included only
    /// when relevant.
    /// </summary>
    public List<string> GetStatLines()
    {
        var lines = new List<string>();

        if (behaviour == TrapBehaviour.CaptureTrap)
        {
            lines.Add($"Holds: {captureHoldSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.ScatterTrap)
        {
            lines.Add($"Scatters: {scatterSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.Crossbow)
        {
            lines.Add($"Damage: {damage:0}");
            lines.Add($"Range: {sentryRange:0.#}");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.Fireball)
        {
            lines.Add($"Damage: {damage:0} (radius {burstRadius:0.#})");
            lines.Add($"Burn: {burnDps:0.#}/s for {burnSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.IceSpikes)
        {
            lines.Add($"Damage: {damage:0}");
            int freeze = Mathf.RoundToInt((1f - slowMultiplier) * 100f);
            lines.Add($"Freeze: {freeze}% for {slowDuration:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.EarthSpikes)
        {
            lines.Add($"Damage: {damage:0}");
            lines.Add($"Hurl: {knockbackForce:0.#}");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.GaleVent)
        {
            lines.Add($"Damage: {damage:0}");
            lines.Add($"Hurl: {knockbackForce:0.#}");
            lines.Add($"Scatters: {scatterSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.BlindingFlash)
        {
            lines.Add($"Sear: {damage:0} (radius {burstRadius:0.#})");
            lines.Add($"Stills: {blindHaltSeconds:0.#}s");
            lines.Add($"Blinds trap-sense: {blindSenseSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.UmbralSnare)
        {
            int slowPct = Mathf.RoundToInt((1f - slowMultiplier) * 100f);
            lines.Add($"Slow: {slowPct}% for {slowDuration:0.#}s");
            lines.Add($"Dims senses: {senseDampSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.SleepDart)
        {
            lines.Add($"Stills: {blindHaltSeconds:0.#}s");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        if (behaviour == TrapBehaviour.SiphonRune)
        {
            lines.Add($"Damage: {damage:0}");
            lines.Add($"Mana tithe: {manaGain:0}");
            lines.Add($"Cooldown: {cooldown:0.#}s");
            return lines;
        }

        lines.Add($"Damage: {damage:0}");
        lines.Add($"Cooldown: {cooldown:0.#}s");

        if (behaviour == TrapBehaviour.Pitfall)
        {
            int slowPercent = Mathf.RoundToInt((1f - slowMultiplier) * 100f);
            lines.Add($"Slow: {slowPercent}% for {slowDuration:0.#}s");
        }

        return lines;
    }
}