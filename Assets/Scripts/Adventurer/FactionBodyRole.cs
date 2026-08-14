/// <summary>
/// What a faction's mortal body DOES for its people, and therefore what its
/// death costs the dungeon (canon 44).
///
/// THE ROLE RIDES THE BODY AND THE PRICE LIVES IN FactionSystem. The three
/// numbers could as easily have sat on the three controllers that own the
/// bodies, and that is exactly how they would drift: a villager quietly costing
/// less than a guard, in a file nobody reads beside the bands the figures were
/// sized against. Canon 42 records the same discipline for ThievesByTier and
/// ExcavatorStealShare -- the knob and the thing it tunes live together.
///
/// NOT AUTHORED AND NOT SERIALISED. The role is passed to
/// InitialiseAsFactionBody by whichever controller made the body, so it cannot
/// be set wrong on an asset and cannot be reordered into a save. It carries no
/// append-only warning for that reason.
/// </summary>
public enum FactionBodyRole
{
    /// <summary>Armed, on a road or a wall. -10.</summary>
    Guard,

    /// <summary>Unarmed, at home. -15: MORE than a soldier, deliberately.</summary>
    Villager,

    /// <summary>On the road with the cargo. -25, matching the robbery.</summary>
    CaravanMember,
}
