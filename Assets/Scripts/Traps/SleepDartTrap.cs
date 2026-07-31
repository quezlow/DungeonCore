using UnityEngine;

/// <summary>
/// Sleep Dart -- a quiet needle. No wound: the victim forgets its quarrel and
/// all but stills for a few seconds (the blind primitive with no trap-sense
/// suppression). The capture trap's quieter cousin -- it takes a moment, not
/// a prisoner. Wild monsters are unaffected by ruling; a wild stepper spends
/// the charge.
/// </summary>
public class SleepDartTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        adv.ApplyBlind(ScaledDuration(Definition.blindHaltSeconds), 0f);
    }
}
