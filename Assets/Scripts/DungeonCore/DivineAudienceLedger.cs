using System.Collections.Generic;

/// <summary>
/// Which divine audiences this dungeon has already held (canon 19A). Static, so the
/// record survives the overlay being absent, disabled or destroyed -- the audience is
/// a fact about the run, not about a UI object.
///
/// Keys are the TIER NAME ("Silver", "Gold", "Diamond", "God"), never the enum ordinal:
/// LevelTier may gain values, and a save keyed on ordinals would silently re-point.
///
/// Held is recorded when the god ARRIVES, not when the last line lands. A player who
/// quits mid-speech has still had the audience; re-firing it on load would replay a
/// beat they already sat through, which is worse than losing the tail of one.
/// </summary>
public static class DivineAudienceLedger
{
    private static readonly HashSet<string> held = new HashSet<string>();

    public static string KeyFor(LevelTier tier) => tier.ToString();

    public static bool IsHeld(LevelTier tier) => held.Contains(KeyFor(tier));

    public static void MarkHeld(LevelTier tier) => held.Add(KeyFor(tier));

    public static int HeldCount => held.Count;

    public static List<string> GatherSave() => new List<string>(held);

    /// <summary>Restore, then reconcile in silence. A save that predates this feature
    /// carries no keys at all, so every tier the core has ALREADY passed through is
    /// marked held rather than queued: those audiences happened in fiction, and the
    /// alternative is four gods arriving in a row on the next level-up. History is not
    /// an event to announce (the Deeds precedent). Idempotent -- levels never fall.</summary>
    public static void RestoreFromSave(List<string> saved, int flatLevel)
    {
        held.Clear();
        if (saved != null)
            foreach (string key in saved)
                if (!string.IsNullOrEmpty(key)) held.Add(key);

        LevelTier current = LevelTierUtil.FromFlatLevel(flatLevel).tier;
        foreach (LevelTier tier in DivineAudienceScript.AudienceTiers)
            if (tier <= current) held.Add(KeyFor(tier));
    }

    public static void ResetForNewGame() => held.Clear();
}
