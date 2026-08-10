using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The core's roster of spells: what exists, what this core may cast, and
/// what is off cooldown.
///
/// SELF-POPULATING. Spell assets are loaded from Resources/Spells the first
/// time anything asks, the WorldEventDirector precedent -- so there is no
/// registry asset to drag and no Inspector slot to leave empty. An empty
/// folder means no spells, which is a correct and quiet state rather than a
/// null-reference.
///
/// COOLDOWNS ARE TRANSIENT. Kept in a static ledger keyed on spell id and
/// stamped from Time.time, so they pause with the clock exactly as trap
/// cooldowns do, and are never serialised -- the section-30 ruling that a
/// mid-flight bolt and a mid-windup telegraph are both dropped by a save
/// applies to a half-elapsed cooldown for the same reason. The ledger is
/// cleared from DungeonBuildController.Awake, so a new scene never inherits
/// the previous run's timers.
/// </summary>
public static class SpellBook
{
    private const string SpellFolder = "Spells";

    private static readonly List<SpellDefinition> all = new List<SpellDefinition>();
    private static readonly Dictionary<string, float> readyAt = new Dictionary<string, float>();
    private static bool loaded;

    /// <summary>Raised when the castable roster may have changed (an unlock landed).
    /// The picker rebuilds off this rather than polling.</summary>
    public static event System.Action OnRosterChanged;

    static SpellBook()
    {
        // A research completion or a god's grant both land as an UnlockState
        // key, so one subscription covers every way a spell can arrive.
        UnlockState.OnChanged += _ => OnRosterChanged?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        all.Clear();
        all.AddRange(Resources.LoadAll<SpellDefinition>(SpellFolder));
        all.Sort((a, b) =>
        {
            if (a == null || b == null) return 0;
            // Neutral craft first, then the core's own signature -- the picker
            // reads as "what anyone can do" then "what your god gave you".
            int aa = a.affinity == DungeonType.None ? 0 : 1;
            int bb = b.affinity == DungeonType.None ? 0 : 1;
            if (aa != bb) return aa - bb;
            return string.CompareOrdinal(a.id, b.id);
        });
    }

    /// <summary>Every authored spell, castable or not.</summary>
    public static IReadOnlyList<SpellDefinition> All
    {
        get { EnsureLoaded(); return all; }
    }

    public static SpellDefinition GetById(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].id == id) return all[i];
        return null;
    }

    /// <summary>True when this core may hold the spell at all: its affinity matches
    /// (or the spell is neutral) and its unlock key is set. The affinity half is the
    /// trapworks type-lock rule -- another core's signature never lists.</summary>
    public static bool IsAvailable(SpellDefinition def)
    {
        if (def == null) return false;
        var core = DungeonCore.Instance;
        if (def.affinity != DungeonType.None)
        {
            if (core == null || core.DungeonType != def.affinity) return false;
        }
        if (string.IsNullOrEmpty(def.requiredUnlockKey)) return true;
        return UnlockState.IsUnlocked(def.requiredUnlockKey);
    }

    /// <summary>Fills the buffer with every spell this core may cast right now.</summary>
    public static int FillAvailable(List<SpellDefinition> outBuf)
    {
        EnsureLoaded();
        outBuf.Clear();
        for (int i = 0; i < all.Count; i++)
            if (IsAvailable(all[i])) outBuf.Add(all[i]);
        return outBuf.Count;
    }

    /// <summary>True when the core holds any spell at all. This -- not a single
    /// node key -- lights the CAST tab, so a god's grant at a tier-up is castable
    /// even by a core that never researched the neutral trunk. Gating the tab on
    /// the trunk instead would hand a player a spell they could not reach.</summary>
    public static bool AnySpellKnown
    {
        get
        {
            EnsureLoaded();
            for (int i = 0; i < all.Count; i++)
                if (IsAvailable(all[i])) return true;
            return false;
        }
    }

    // -- Deepening (the god's later grants) ----------------------------------

    // Radius and duration ONLY. A god's hand reaching further reads as a god;
    // a bigger damage number reads as a stat bump, and it would also mean
    // retuning the whole affinity roster three times over instead of once.
    private static readonly float[] TierRadius = { 1f, 1f, 1.25f, 1.5f };
    private static readonly float[] TierDuration = { 1f, 1f, 1.3f, 1.6f };

    /// <summary>1, 2 or 3. Always 1 for a working with no deepeningKeyBase --
    /// the neutral craft never deepens, because nobody grants it.</summary>
    public static int TierOf(SpellDefinition def)
    {
        if (def == null || string.IsNullOrEmpty(def.deepeningKeyBase)) return 1;
        if (UnlockState.IsUnlocked(def.deepeningKeyBase + ".t3")) return 3;
        if (UnlockState.IsUnlocked(def.deepeningKeyBase + ".t2")) return 2;
        return 1;
    }

    public static float EffectiveRadius(SpellDefinition def)
        => def == null ? 0f : def.radius * TierRadius[TierOf(def)];

    public static float EffectiveDuration(SpellDefinition def)
        => def == null ? 0f : def.durationSeconds * TierDuration[TierOf(def)];

    // -- Cooldowns -----------------------------------------------------------

    public static bool IsReady(SpellDefinition def)
    {
        if (def == null) return false;
        if (def.cooldownSeconds <= 0f) return true;
        return !readyAt.TryGetValue(def.id, out float t) || Time.time >= t;
    }

    /// <summary>Seconds still to run, 0 when ready. For the picker readout.</summary>
    public static float CooldownRemaining(SpellDefinition def)
    {
        if (def == null || def.cooldownSeconds <= 0f) return 0f;
        if (!readyAt.TryGetValue(def.id, out float t)) return 0f;
        return Mathf.Max(0f, t - Time.time);
    }

    public static void StampCooldown(SpellDefinition def)
    {
        if (def == null || def.cooldownSeconds <= 0f) return;
        readyAt[def.id] = Time.time + def.cooldownSeconds;
    }

    /// <summary>Drops every running cooldown. Called from the build controller's
    /// Awake so a fresh scene or a load never starts with stale timers.</summary>
    public static void ClearCooldowns() => readyAt.Clear();
}
