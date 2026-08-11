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

        // -- The six affinity workings. APPENDED, never reordered. --
        BoonDamage = 3,   // Fire  -- yours strike harder
        Pull = 4,         // Water -- theirs are dragged to the cell
        BoonArmour = 5,   // Earth -- yours take less
        BoonHaste = 6,    // Air   -- yours move and swing faster
        Rout = 7,         // Dark  -- theirs turn and run
        Vulnerable = 8,   // Light -- theirs take more from everything

        // -- Charge-only workings (canon 41). APPENDED, never reordered. --
        Summon = 9,       // transient thralls scatter across the ring
        Excavate = 10,    // claimed, unmined stone inside the ring is blasted open
    }

    [Header("Identity")]
    [Tooltip("Stable id. Never rename after ship -- the cooldown ledger and the " +
             "generator both key on it.")]
    public string id = "spell";

    public string displayName = "Spell";

    [TextArea(2, 4)]
    public string description;

    [Tooltip("Where a working the core cannot reach comes FROM, shown on its greyed " +
             "CAST row once it has been heard of (canon 41). A working nobody sells " +
             "and no god grants leaves this empty, and then it simply never lists.")]
    [TextArea(1, 3)]
    public string sourceLine = "";

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

    [Tooltip("Base key for the god's deepening grants, e.g. 'spell.coals_wake'. "
             + "EMPTY means the working never deepens -- an explicit blank rather "
             + "than a magic tier number, so 'not filled in' cannot masquerade as "
             + "'tier one forever'. With it set, the tier is 3 when <base>.t3 is "
             + "unlocked, 2 at <base>.t2, else 1. Deepening widens radius and "
             + "lengthens duration ONLY -- never magnitude.")]
    public string deepeningKeyBase = "";

    [Header("Behaviour")]
    public SpellEffect effect = SpellEffect.Lash;

    [Tooltip("Summon only: the body the working puts on the board. A DEDICATED " +
             "definition rather than a necromancer's risen list -- a summoning is not " +
             "a raising, and sharing the list would tie a god's gift to the tuning of " +
             "a monster that has its own reasons to change. magnitude is HOW MANY and " +
             "durationSeconds is HOW LONG, so the shared four dials still carry it.")]
    public MonsterDefinition summonDefinition;

    [Tooltip("EXPLICIT toggle, not derived from the effect. Orders are pause-legal; " +
             "effects are not -- the pause rule is that pause permits selection, " +
             "navigation and orders, and forbids anything that spends mana or changes " +
             "world state. Rally is the only spell that should carry this. Deriving it " +
             "from the effect enum would make a future order-spell silently pause-illegal.")]
    public bool castableWhilePaused = false;

    /// <summary>One-line stats for the picker. Mirrors TrapDefinition.GetStatLines.
    /// Reads the EFFECTIVE radius and duration, not the authored ones -- after a
    /// god deepens a working the picker must show what it now does, not what it
    /// shipped as.</summary>
    public string StatLine()
    {
        float r = SpellBook.EffectiveRadius(this);
        float dur = SpellBook.EffectiveDuration(this);
        int tier = SpellBook.TierOf(this);
        string deep = tier > 1 ? "\nDeepened " + tier + "/3" : "";
        switch (effect)
        {
            case SpellEffect.Lash:
                return "Damage " + magnitude.ToString("0") + " (radius " + r.ToString("0.#")
                     + ")\nHurl " + secondary.ToString("0.#")
                     + "\nCooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Knit:
                return "Heals " + magnitude.ToString("0") + " (radius " + r.ToString("0.#")
                     + ")\nCooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Rally:
                return "Radius " + r.ToString("0.#")
                     + "\nCooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.BoonDamage:
                return "Yours strike +" + Pct(magnitude) + " for " + dur.ToString("0.#") + "s"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.BoonHaste:
                return "Yours move and swing +" + Pct(magnitude) + " for " + dur.ToString("0.#") + "s"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.BoonArmour:
                return "Yours take -" + Pct(2f - magnitude) + " for " + dur.ToString("0.#") + "s"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Vulnerable:
                return "Theirs take +" + Pct(magnitude) + " for " + dur.ToString("0.#") + "s"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Pull:
                return "Drags them " + secondary.ToString("0.#") + " toward the mark"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Rout:
                return "Theirs turn and run"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Summon:
                return "Raises " + Mathf.Max(1, Mathf.RoundToInt(magnitude))
                     + " for " + dur.ToString("0.#") + "s"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            case SpellEffect.Excavate:
                return "Opens claimed stone inside the ring"
                     + "\nRadius " + r.ToString("0.#")
                     + "   Cooldown " + cooldownSeconds.ToString("0.#") + "s" + deep;
            default:
                return "Radius " + r.ToString("0.#") + deep;
        }
    }

    /// <summary>A multiplier as the percentage change it represents: 1.35 -> "35%".</summary>
    private static string Pct(float multiplier)
        => Mathf.RoundToInt(Mathf.Abs(multiplier - 1f) * 100f).ToString() + "%";
}
