using UnityEngine;

/// <summary>
/// Data asset for one core spell. Authored by
/// Dungeon Core -> Generate Spell Content into Resources/Spells, which is
/// where SpellBook loads them from -- the WorldEventDirector precedent, so a
/// new spell is an asset and needs no scene wiring and no registry drag.
///
/// SHARED SHAPE. Every spell is cast at a cell on the active floor and acts
/// over a radius for a duration. Keeping the shape identical across the
/// roster is deliberate: the power budget is then four dials (radius,
/// duration, magnitude, mana) rather than an argument about which verb is
/// stronger, exactly as the trapworks balances on mana/cap/damage/cooldown.
/// Do not add a spell whose shape has to be special-cased in the caster
/// without also recording why here.
/// </summary>
[CreateAssetMenu(fileName = "NewSpellDefinition", menuName = "Dungeon/Spell Definition")]
public class SpellDefinition : ScriptableObject
{
    /// <summary>
    /// Which arm of SpellCaster resolves this spell.
    ///
    /// APPEND ONLY. This serialises into the spell assets as an int, exactly
    /// as TrapDefinition.TrapBehaviour and TraderStockCatalog.StockType do; a
    /// reorder would silently re-type every authored spell. The six affinity
    /// effects land here in the second half of the arc.
    /// </summary>
    public enum SpellEffect
    {
        Lash = 0,       // burst at the cell: damage plus a hurl outward
        Knit = 1,       // heal the dungeon's own monsters in the radius
        Rally = 2,      // every spawner in the radius retargets on the cell
    }

    [Header("Identity")]
    [Tooltip("Stable id. Never rename after ship -- the cooldown ledger and the " +
             "generator both key on it.")]
    public string id = "spell";

    public string displayName = "Spell";

    [TextArea(2, 4)]
    public string description;

    [Tooltip("Node icon. Null-safe -- the picker renders a plain block.")]
    public Sprite icon;

    [Header("Access")]
    [Tooltip("UnlockState key that must be set before this spell lists in the picker. " +
             "Research spells carry a tech.* node key; god-given spells carry a bare " +
             "spell.* key that no node owns (the dwarven-trap precedent, canon 28A).")]
    public string requiredUnlockKey = "";

    [Tooltip("None = neutral, every core may cast it. Otherwise the spell is exclusive " +
             "to the matching core: hidden from every other core's picker, and refused " +
             "at cast as a backstop. Mirrors the trapworks type-lock rule.")]
    public DungeonType affinity = DungeonType.None;

    [Header("Cost")]
    public float manaCost = 10f;

    [Tooltip("Seconds before this spell may be cast again. Mana alone is not a brake: " +
             "a Diamond core holds 3840 mana and would machine-gun the cheap spells. " +
             "0 = no cooldown, which is correct only for spells that issue an order " +
             "rather than spend an effect.")]
    [Min(0f)] public float cooldownSeconds = 1.5f;

    [Header("Shape")]
    [Tooltip("World-space radius of the effect around the cast cell.")]
    [Min(0.1f)] public float radius = 1.6f;

    [Tooltip("Seconds the effect lasts. Instant effects ignore this.")]
    [Min(0f)] public float durationSeconds = 0f;

    [Tooltip("The one number the effect turns on: damage for Lash, health for Knit. " +
             "Rally ignores it.")]
    public float magnitude = 10f;

    [Tooltip("Secondary dial. Lash: the hurl distance. Unused elsewhere.")]
    public float secondary = 1.2f;

    [Header("Behaviour")]
    public SpellEffect effect = SpellEffect.Lash;

    [Tooltip("EXPLICIT toggle, not derived from the effect. Orders are pause-legal; " +
             "effects are not -- the pause rule is that pause permits selection, " +
             "navigation and orders, and forbids anything that spends mana or changes " +
             "world state. Rally is the only spell that should carry this. Deriving it " +
             "from the effect enum would make a future order-spell silently pause-illegal.")]
    public bool castableWhilePaused = false;

    /// <summary>One-line stats for the picker. Mirrors TrapDefinition.GetStatLines.</summary>
    public string StatLine()
    {
        switch (effect)
        {
            case SpellEffect.Lash:
                return "Damage " + magnitude.ToString("0") + " (radius " + radius.ToString("0.#")
                     + ")\nHurl " + secondary.ToString("0.#")
                     + "\nCooldown " + cooldownSeconds.ToString("0.#") + "s";
            case SpellEffect.Knit:
                return "Heals " + magnitude.ToString("0") + " (radius " + radius.ToString("0.#")
                     + ")\nCooldown " + cooldownSeconds.ToString("0.#") + "s";
            case SpellEffect.Rally:
                return "Radius " + radius.ToString("0.#")
                     + "\nCooldown " + cooldownSeconds.ToString("0.#") + "s";
            default:
                return "Radius " + radius.ToString("0.#");
        }
    }
}
