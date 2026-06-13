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
///   used by DungeonPathfinder.FindPath() to route around them. Monsters DO NOT
///   detect or flag traps (T4) — they always blunder in unless the trap is
///   already flagged by an adventurer, in which case the flagged-state check
///   below skips the trigger uniformly.
///
/// DAY 31 PART 3C — WILD MONSTER PATH
///   OnMonsterEntered(DungeonMonster) mirrors OnAdventurerEntered.
///   Cooldown is SHARED between the adventurer and monster paths — if an
///   adventurer just sprung the trap, a monster walking through during the
///   cooldown won't re-fire it. ApplyEffect(DungeonMonster) is virtual with
///   an empty default so warning traps and pressure plates do nothing for
///   monsters by default; only damage traps override.
/// </summary>
public abstract class TrapBase : MonoBehaviour, IFloorEntity
{
    // Set by DungeonBuildController immediately after Instantiate().
    public TrapDefinition Definition { get; protected set; }
    public Vector3Int OccupiedCell { get; protected set; }
    public bool IsFlagged { get; private set; }

    private float lastTriggerTime = -999f;

    // ── Lifecycle ─────────────────────────────────────────────────

    public virtual void Initialise(TrapDefinition def, Vector3Int cell)
    {
        Definition = def;
        OccupiedCell = cell;

        var floor = GetComponentInParent<FloorRoot>();
        floor?.TrapRegistry?.Register(this);
        floor?.Entities?.Register(this);
        Debug.Log($"[TrapBase] Initialised {def?.trapName} at cell {cell}.");
    }

    protected virtual void OnDestroy()
    {
        var floor = GetComponentInParent<FloorRoot>();
        floor?.TrapRegistry?.Unregister(this);
        floor?.Entities?.Unregister(this);
    }

    // ── Adventurer Trigger ────────────────────────────────────────

    public void OnAdventurerEntered(DungeonAdventurer adv)
    {
        if (Definition == null) return;
        if (IsFlagged) return;
        if (Time.time - lastTriggerTime < Definition.cooldown) return;

        lastTriggerTime = Time.time;
        ApplyEffect(adv);
        Debug.Log($"[Trap] {Definition.trapName} triggered on adventurer at {OccupiedCell}.");
    }

    public void TriggerExternally(DungeonAdventurer adv)
    {
        if (Definition == null || adv == null) return;
        ApplyEffect(adv);
        Debug.Log($"[Trap] {Definition.trapName} triggered externally at {OccupiedCell}.");
    }

    protected abstract void ApplyEffect(DungeonAdventurer adv);

    // ── Monster Trigger (DAY 31 PART 3C) ──────────────────────────

    /// <summary>
    /// Called by DungeonMonster.CheckTrapStep() when a WILD monster's tracked
    /// cell becomes this trap's cell. Player monsters bypass their own traps
    /// (per T2) — DungeonMonster guards on IsWild before invoking this.
    /// Shares the cooldown clock with OnAdventurerEntered.
    /// </summary>
    public void OnMonsterEntered(DungeonMonster m)
    {
        if (Definition == null) return;
        if (m == null) return;
        if (IsFlagged) return;
        if (Time.time - lastTriggerTime < Definition.cooldown) return;

        lastTriggerTime = Time.time;
        ApplyEffect(m);
        Debug.Log($"[Trap] {Definition.trapName} triggered on wild monster at {OccupiedCell}.");
    }

    /// <summary>
    /// Pressure plate path for monsters. Bypasses cooldown AND flagged state
    /// (matches TriggerExternally(DungeonAdventurer) semantics — the monster
    /// stepped on the plate, not on this trap directly).
    /// </summary>
    public void TriggerExternallyMonster(DungeonMonster m)
    {
        if (Definition == null || m == null) return;
        ApplyEffect(m);
        Debug.Log($"[Trap] {Definition.trapName} triggered externally on monster at {OccupiedCell}.");
    }

    /// <summary>
    /// DAY 31 PART 3C — Subclasses override to define a per-monster effect.
    /// Default is no-op so WarningTrap (intel-only) and PressurePlateTrap
    /// (effect-via-link) do nothing for monsters without any code changes.
    /// </summary>
    protected virtual void ApplyEffect(DungeonMonster m) { }

    // ── Flagged State ─────────────────────────────────────────────

    public void Flag()
    {
        if (IsFlagged) return;
        IsFlagged = true;
        Debug.Log($"[Trap] {Definition.trapName} at {OccupiedCell} flagged.");
        TrapRegistry.Instance?.NotifyFlaggedChanged();
    }

    // ── Factory ───────────────────────────────────────────────────

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