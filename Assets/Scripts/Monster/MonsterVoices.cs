/// <summary>
/// How a monster sounds when it barks. Set per MonsterDefinition so a rat squeaks, a skeleton
/// rattles, and a goblin actually speaks - and a spider says nothing at all.
///
///   Silent   - no idle growls, no taunts. Insects, oozes, anything without a throat.
///   Beast    - wordless: growls, snarls, snuffles. Rats, wolves, bears.
///   Undead   - dry bone and grave-dust. Skeletons, wights.
///   Humanoid - real words. Goblins, orcs, anything that can hold a conversation.
/// </summary>
public enum MonsterVoice
{
    Silent,
    Beast,
    Undead,
    Humanoid,
}