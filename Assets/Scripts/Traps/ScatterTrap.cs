using UnityEngine;

/// <summary>
/// Scatter Trap -- instead of wounding, it breaks a party's formation. Any adventurer who
/// steps on it has their whole party scattered for a spell (BreakFormation): members
/// disperse and the shield wall drops until they re-form. Pure disruption, no damage -- the
/// player's answer to a shield-wall party. Wild monsters have no formation to break.
///
/// No research gate: without formationed parties to break it does nothing, so its value is
/// self-evident and it ships alongside the other traps.
/// </summary>
public class ScatterTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;
        adv.BreakFormation(Definition.scatterSeconds);
    }

    // ApplyEffect(DungeonMonster) inherits the empty default: monsters have no formation.
}