/// <summary>
/// Lightweight, non-MonoBehaviour grouping for a single spawned wave of
/// adventurers (Day 35). Created once per AdventurerSpawner.SpawnParty() and
/// shared by every member of that wave via DungeonAdventurer.Initialise().
///
/// Holds the party-wide Intent plus one-shot latches so party-level effects
/// fire exactly once regardless of how many members trigger them:
///   exitBonusApplied — guards the Pilgrim Notoriety reduction so a multi-
///                      pilgrim party only calms the dungeon a single time.
///
/// Deliberately minimal. The Phase 4 party banner / named-adventurer tracking
/// will extend this; nothing here needs to change for that.
/// </summary>
public class AdventurerParty
{
    public PartyIntent Intent { get; }

    /// <summary>Set true by the first member that completes a peaceful
    /// pilgrimage exit, so the Notoriety reduction is applied only once.</summary>
    public bool exitBonusApplied = false;

    public AdventurerParty(PartyIntent intent)
    {
        Intent = intent;
    }
}