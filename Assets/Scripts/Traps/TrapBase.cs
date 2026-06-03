using UnityEngine;

/// <summary>
/// Base class for all placed trap instances. Subclassed by SpikeTrap, PitfallTrap, etc.
///
/// LIFECYCLE
///   - Instantiated by DungeonBuildController via TrapDefinition.prefab
///   - Initialise() sets the cell and registers in TrapRegistry
///   - Adventurers calling FollowPath() check the registry on each waypoint advance
///   - On trigger, OnAdventurerEntered() runs the subclass's effect logic
///   - Cooldown prevents repeated triggers within a short window
///   - On destroy, unregisters from TrapRegistry
///
/// FLAGGED STATE
///   When a Rogue (or any canDetectTraps adventurer) detects this trap, it gets
///   flagged. Flagged cells are returned by TrapRegistry.GetFlaggedCells() and
///   used by DungeonPathfinder.FindPath() to route around them.
/// </summary>
public abstract class TrapBase : MonoBehaviour
{
    // Set by DungeonBuildController immediately after Instantiate().
    public TrapDefinition Definition   { get; protected set; }
    public Vector3Int     OccupiedCell { get; protected set; }
    public bool           IsFlagged    { get; private set; }

    private float lastTriggerTime = -999f;

    // ── Lifecycle ─────────────────────────────────────────────────

    /// <summary>Called by DungeonBuildController and the save restore path.</summary>
    public virtual void Initialise(TrapDefinition def, Vector3Int cell)
    {
        Definition   = def;
        OccupiedCell = cell;

        GetComponentInParent<FloorRoot>()?.TrapRegistry?.Register(this);
        Debug.Log($"[TrapBase] Initialised {def?.trapName} at cell {cell}. " +
          $"Registry size: {TrapRegistry.Instance != null}");
    }

    protected virtual void OnDestroy()
    {
        GetComponentInParent<FloorRoot>()?.TrapRegistry?.Unregister(this);
    }

    // ── Trigger Logic ─────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonAdventurer.FollowPath() when an adventurer enters this trap's cell.
    /// Handles cooldown and flagged-state checks before delegating to ApplyEffect().
    /// </summary>
    public void OnAdventurerEntered(DungeonAdventurer adv)
    {
        if (Definition == null) return;
        if (IsFlagged) return; // adventurer party knows about it, skip
        if (Time.time - lastTriggerTime < Definition.cooldown) return;

        lastTriggerTime = Time.time;
        ApplyEffect(adv);
        Debug.Log($"[Trap] {Definition.trapName} triggered on adventurer at {OccupiedCell}.");
    }

    /// <summary>
    /// Subclasses implement the trap's specific effect (damage, slow, etc.).
    /// </summary>
    /// 
    public void TriggerExternally(DungeonAdventurer adv)
    {
        if (Definition == null || adv == null) return;
        // Bypasses cooldown AND flagged state — the adventurer stepped on the
        // PLATE, not on this trap directly, so neither restriction applies.
        ApplyEffect(adv);
        Debug.Log($"[Trap] {Definition.trapName} triggered externally at {OccupiedCell}.");
    }

    protected abstract void ApplyEffect(DungeonAdventurer adv);

    // ── Flagged State ─────────────────────────────────────────────

    /// <summary>
    /// Called by an adventurer who successfully detects this trap.
    /// Once flagged, subsequent pathfinding routes around it and no future
    /// adventurer triggers it.
    /// </summary>
    public void Flag()
    {
        if (IsFlagged) return;
        IsFlagged = true;
        Debug.Log($"[Trap] {Definition.trapName} at {OccupiedCell} flagged.");
        TrapRegistry.Instance?.NotifyFlaggedChanged();
    }

    // ── Factory ───────────────────────────────────────────────────

    /// <summary>
    /// Instantiates the right TrapBase subclass component on the placed prefab.
    /// Called by DungeonBuildController immediately after Instantiate().
    /// </summary>
    public static TrapBase EnsureBehaviour(GameObject placedPrefab, TrapDefinition def)
    {
        var existing = placedPrefab.GetComponent<TrapBase>();
        if (existing != null) return existing;

        return def.behaviour switch
        {
            TrapDefinition.TrapBehaviour.SpikeTrap => placedPrefab.AddComponent<SpikeTrap>(),
            TrapDefinition.TrapBehaviour.Pitfall => placedPrefab.AddComponent<PitfallTrap>(),
            TrapDefinition.TrapBehaviour.Warning => placedPrefab.AddComponent<WarningTrap>(),
            TrapDefinition.TrapBehaviour.PressurePlate => placedPrefab.AddComponent<PressurePlateTrap>(),
            _ => placedPrefab.AddComponent<SpikeTrap>(),
        };
    }

}
