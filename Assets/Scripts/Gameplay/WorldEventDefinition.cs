using UnityEngine;

/// <summary>
/// What an event DOES when it fires. Multiplier kinds (RespawnRate,
/// CivilianWeight) hold for durationDays; GrantGold is instant; None fires
/// the alert alone (pure flavour).
///
/// Values serialise as ints into .asset files, so this enum is APPEND-ONLY:
/// never reorder or remove, exactly as the save-facing enums. A new kind is
/// a new value here plus one case in WorldEventDirector.Fire - that switch
/// is the single place effects become behaviour.
/// </summary>
public enum WorldEventEffectKind
{
    None = 0,
    RespawnRate = 1,     // dungeon monster respawn speed multiplier (timed)
    CivilianWeight = 2,  // civilian intent lane multiplier (timed)
    GrantGold = 3,       // one-shot gold grant to the core
}

/// <summary>
/// One authored world event: the gates that make it eligible, the weight the
/// dawn roll draws it by, and the effect it fires. Assets live under
/// Resources/Events/World so WorldEventDirector self-populates - authored by
/// Dungeon Core -> Generate World Events (Editor/WorldEventContentGenerator).
///
/// A new event on an existing effect kind is assets-only: one spec row in the
/// generator, regenerate, done. Predicates are FIELDS, not code, so authoring
/// never touches the director. Cadence maths lives in
/// Tools/sim_world_events.py - rerun it whenever gates, weights, or the dawn
/// ordering change; the director must mirror that file.
/// </summary>
[CreateAssetMenu(fileName = "WorldEvent", menuName = "Dungeon/World Event Definition")]
public class WorldEventDefinition : ScriptableObject
{
    /// <summary>Save-facing identity is the asset name. String ids, never enum
    /// indices, so authored events can come and go across versions - a save
    /// naming an event that no longer exists is skipped on load, not a fault.</summary>
    public string Id => name;

    [Header("Alert (wisp voice)")]
    [Tooltip("The line the alert speaks when the event fires.")]
    [TextArea] public string alertMessage;
    public AlertCategory alertCategory = AlertCategory.Discovery;
    public AlertSeverity alertSeverity = AlertSeverity.Info;

    [Tooltip("Hostile events are stripped from the dawn pool while the endgame " +
             "climax suppresses mid-game threats. None of the v1 trio is hostile; " +
             "this is the slot a future assault-shaped event rides.")]
    public bool hostile;

    [Header("Gates (0 = no gate)")]
    [Tooltip("First day the event can fire. The director misses day 1 by " +
             "subscription order (the threats' shared idiom), so 2 is the " +
             "effective floor.")]
    [Min(1)] public int minDay = 1;
    [Tooltip("Notoriety at or above which the event is eligible. 0 = ungated.")]
    [Min(0f)] public float minNotoriety;
    [Tooltip("DungeonRating at or above which the event is eligible. 0 = ungated.")]
    [Min(0f)] public float minRating;

    [Header("Cadence")]
    [Tooltip("Dawns between this event's fires. Clamped to at least " +
             "durationDays so a timed effect can never overlap itself.")]
    [Min(1)] public int cooldownDays = 6;
    [Tooltip("Relative draw weight among eligible events on a fire day.")]
    [Min(0f)] public float weight = 1f;

    [Header("Effect")]
    public WorldEventEffectKind effectKind = WorldEventEffectKind.None;
    [Tooltip("Multiplier applied while a RespawnRate / CivilianWeight effect " +
             "holds. Ignored by other kinds.")]
    [Min(0f)] public float magnitude = 1f;
    [Tooltip("Days a multiplier effect holds (the fire day counts as the " +
             "first). Multiplier kinds treat values below 1 as 1.")]
    [Min(0)] public int durationDays;
    [Tooltip("GrantGold: inclusive roll range.")]
    [Min(0)] public int goldMin;
    [Min(0)] public int goldMax;

    private void OnValidate()
    {
        // A cooldown shorter than the duration would let an effect re-fire
        // over itself; the sim asserts the same invariant (check 4).
        if (cooldownDays < durationDays) cooldownDays = durationDays;
        if (goldMax < goldMin) goldMax = goldMin;
    }
}
