using System;

/// <summary>
/// How a monster decides what to attack.
///
///   Defensive  — won't start fights during its normal routine; retaliates if
///                damaged, and still engages while defending the core or under
///                an explicit Attack-Here order.
///   Normal     — attacks hostile adventurers and rival monsters, but spares
///                Pilgrims (peaceful visitors).
///   Aggressive — attacks everything, Pilgrims included.
///
/// Per-monster resolution: an individual override wins; otherwise wild monsters
/// default Aggressive and player-owned monsters follow the global toggle below.
/// </summary>
public enum MonsterAggression
{
    Defensive,
    Normal,
    Aggressive,
}

/// <summary>
/// Global default stance for all player-owned monsters that don't individually
/// override it. Runtime-only for now (resets to Normal each session); it is read
/// fresh on every hostile scan, so changing it takes effect immediately. Wild
/// monsters ignore this and default to Aggressive.
///
/// Writes go through Set() so the change event fires — the HUD subscribes to it
/// to keep its active highlight correct no matter who flips the stance.
/// </summary>
public static class MonsterAggressionSettings
{
    public static MonsterAggression Global { get; private set; } = MonsterAggression.Normal;

    /// <summary>Raised whenever the global stance changes. UI subscribes to refresh.</summary>
    public static event Action OnChanged;

    /// <summary>Set the global stance (no-op if unchanged); fires OnChanged.</summary>
    public static void Set(MonsterAggression stance)
    {
        if (stance == Global) return;
        Global = stance;
        OnChanged?.Invoke();
    }
}