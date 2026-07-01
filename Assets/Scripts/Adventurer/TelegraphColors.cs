using UnityEngine;

/// <summary>
/// Per-class windup cue colours — a code stand-in for the distinct animation tells
/// (Mage charge-up, Tank shield-raise, Rogue crouch) until those clips exist. Monsters
/// share one warning colour. Tune freely.
/// </summary>
public static class TelegraphColors
{
    public static readonly Color Monster = new Color(1f, 0.45f, 0.2f);   // orange warning

    public static Color ForClass(CombatClass c) => c switch
    {
        CombatClass.Mage => new Color(0.45f, 0.55f, 1f),   // arcane blue
        CombatClass.Tank => new Color(1f, 0.8f, 0.3f),     // amber (shield)
        CombatClass.Rogue => new Color(1f, 0.35f, 0.35f),   // red (backstab)
        CombatClass.Fighter => new Color(1f, 0.6f, 0.3f),     // martial orange
        CombatClass.Cleric => new Color(1f, 0.95f, 0.6f),    // pale gold
        CombatClass.Explorer => new Color(0.5f, 1f, 0.5f),     // green
        _ => new Color(1f, 0.6f, 0.3f),
    };
}