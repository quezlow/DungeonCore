using UnityEngine;

/// <summary>
/// The faction-vs-faction relationship matrix and the encounter resolver - the data layer a
/// future forest / dungeon-exterior encounter system will consult when two faction groups meet.
///
/// RELATIONSHIPS: only the lawful establishment against the heretic Cultists is hostile -
/// {Guild &lt;-&gt; Cultists} and {Holy Order &lt;-&gt; Cultists}. Mercenaries take no side.
/// Everything else is Neutral. (The dungeon's OWN standing with each faction lives in
/// FactionSystem; this is orthogonal - it is how the factions feel about EACH OTHER.)
///
/// OUTCOME: each faction carries a martial-strength rating. A hostile encounter resolves to
/// the clearly-stronger side (Victor), or Contested when they are matched. Ratings are equal
/// by default, so faction identity alone yields Contested - the real winner emerges from the
/// size/strength of the specific groups, which the caller passes in once real entities exist.
///
/// IN-DUNGEON (interim): until the procgen forest is built, the only faction fight that
/// actually plays out is Hero vs Cultist inside the dungeon (see EngagesInsideDungeon).
/// Regular delvers stay out of it; patrols, camps, and forest encounters are all deferred.
/// </summary>
public static class FactionRelations
{
    public enum Relationship { Allied, Neutral, Hostile }
    public enum EncounterResult { NoConflict, Contested, Victor }

    // Baseline martial strength per faction (1..10). Equal by default - raise one to give it
    // an inherent edge over another. Group size/strength layers on top at encounter time.
    private static int Strength(FactionId f) => f switch
    {
        FactionId.AdventurersGuild => 8,
        FactionId.HolyOrder => 8,
        FactionId.MercenaryCompany => 8,
        FactionId.Cultists => 8,
        _ => 5,
    };

    // Effective strengths within this band count as evenly matched -> Contested.
    private const float ContestedBand = 1.5f;

    /// <summary>How two factions regard each other.</summary>
    public static Relationship Between(FactionId a, FactionId b)
    {
        if (a == b) return Relationship.Neutral;   // a faction is not hostile to itself
        bool cultistPair = (a == FactionId.Cultists) ^ (b == FactionId.Cultists);
        bool lawful = a == FactionId.AdventurersGuild || a == FactionId.HolyOrder
                   || b == FactionId.AdventurersGuild || b == FactionId.HolyOrder;
        return (cultistPair && lawful) ? Relationship.Hostile : Relationship.Neutral;
    }

    public static bool AreHostile(FactionId a, FactionId b) => Between(a, b) == Relationship.Hostile;

    /// <summary>Resolve a meeting between two factions, weighted by the strength of the specific
    /// groups involved (pass 1 for a faction-level check with no group data). Returns the
    /// outcome; when the result is Victor, 'victor' names the winning faction.</summary>
    public static EncounterResult ResolveEncounter(FactionId a, float groupStrengthA,
                                                   FactionId b, float groupStrengthB,
                                                   out FactionId victor)
    {
        victor = a;
        if (!AreHostile(a, b)) return EncounterResult.NoConflict;

        float sa = Strength(a) * Mathf.Max(0.01f, groupStrengthA);
        float sb = Strength(b) * Mathf.Max(0.01f, groupStrengthB);
        if (Mathf.Abs(sa - sb) <= ContestedBand) return EncounterResult.Contested;

        victor = sa > sb ? a : b;
        return EncounterResult.Victor;
    }

    /// <summary>Faction-level convenience: resolve on baseline strengths alone (no group sizes).</summary>
    public static EncounterResult ResolveEncounter(FactionId a, FactionId b, out FactionId victor)
        => ResolveEncounter(a, 1f, b, 1f, out victor);

    // -- In-dungeon interim scope -------------------------------------------------
    // Until the procgen forest exists, only Heroes and Cultists brawl inside the dungeon.

    public static bool IsDungeonBrawler(AdventurerType t)
        => t == AdventurerType.Hero || t == AdventurerType.Cultist;

    /// <summary>True if these two adventurer TYPES fight when they meet inside the dungeon:
    /// both must be dungeon brawlers (Hero/Cultist) and their factions must be hostile.</summary>
    public static bool EngagesInsideDungeon(AdventurerType a, AdventurerType b)
        => IsDungeonBrawler(a) && IsDungeonBrawler(b)
           && AreHostile(AdventurerTypeInfo.FactionOf(a), AdventurerTypeInfo.FactionOf(b));
}