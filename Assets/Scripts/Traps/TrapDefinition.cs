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
    }

    [Header("Identity")]
    public string trapName = "Trap";
    public TrapBehaviour behaviour = TrapBehaviour.SpikeTrap;

    [Header("Prefab")]
    [Tooltip("Base prefab. Must have a TrapBase subclass component plus a SpriteRenderer.")]
    public TrapBase prefab;

    [Header("Placement")]
    public float manaCost = 8f;

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
        var lines = new List<string>
        {
            $"Damage: {damage:0}",
            $"Cooldown: {cooldown:0.#}s",
        };

        if (behaviour == TrapBehaviour.Pitfall)
        {
            int slowPercent = Mathf.RoundToInt((1f - slowMultiplier) * 100f);
            lines.Add($"Slow: {slowPercent}% for {slowDuration:0.#}s");
        }

        return lines;
    }
}