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
///   flagged. Flagged cells carry a high Dijkstra step cost (TrapRegistry.
///   FlaggedPathCost) so adventurers detour around them when a cheaper route
///   exists — but a flagged trap still FIRES on any adventurer forced across
///   it. Awareness buys avoidance, not immunity; disarming is the Rogue's
///   coming answer. The wild-monster path keeps its flagged-state skip.
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
    public bool IsDisarmed { get; private set; }

    /// <summary>Fires whenever any trap is placed or destroyed, so live panels can refresh.</summary>
    public static event System.Action OnTrapsChanged;

    private float lastTriggerTime = -999f;

    // ── Lifecycle ─────────────────────────────────────────────────

    public virtual void Initialise(TrapDefinition def, Vector3Int cell)
    {
        Definition = def;
        OccupiedCell = cell;

        var floor = GetComponentInParent<FloorRoot>();

        floor?.TrapRegistry?.Register(this);
        floor?.Entities?.Register(this);
        OnTrapsChanged?.Invoke();
    }

    protected virtual void OnDestroy()
    {
        if (Definition != null && Definition.capacityCost > 0)
            DungeonCore.Instance?.ReturnCapacity(Definition.capacityCost);

        var floor = GetComponentInParent<FloorRoot>();
        floor?.TrapRegistry?.Unregister(this);
        floor?.Entities?.Unregister(this);
        OnTrapsChanged?.Invoke();
    }

    /// <summary>Player-initiated removal. Refunds half the placement mana and
    /// destroys the trap; the capacity it held is returned in OnDestroy.</summary>
    public void RemoveByPlayer()
    {
        if (Definition != null && DungeonCore.Instance != null)
            DungeonCore.Instance.AddMana(Definition.manaCost * 0.5f);
        Destroy(gameObject);
    }

    // ── Adventurer Trigger ────────────────────────────────────────

    public void OnAdventurerEntered(DungeonAdventurer adv)
    {
        if (Definition == null) return;
        if (IsDisarmed) return;
        if (Time.time - lastTriggerTime < Definition.cooldown) return;

        lastTriggerTime = Time.time;
        ApplyEffect(adv);
        BanterLines.ReactTrap(adv);
        Debug.Log($"[Trap] {Definition.trapName} triggered on adventurer at {OccupiedCell}.");
    }

    public void TriggerExternally(DungeonAdventurer adv)
    {
        if (Definition == null || adv == null) return;
        if (IsDisarmed) return;
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
        if (IsDisarmed) return;
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
        if (IsDisarmed) return;
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
        GetComponentInParent<FloorRoot>()?.TrapRegistry?.NotifyFlaggedChanged();
    }

    /// <summary>
    /// A Rogue has neutralised this trap. It no longer fires for anyone, and its
    /// flagged path-cost is cleared so the party walks the cell at normal cost.
    /// Reversed by ResetArmed() the moment the floor empties of adventurers.
    /// </summary>
    public void Disarm()
    {
        if (IsDisarmed) return;
        IsDisarmed = true;
        IsFlagged = false;
        Debug.Log($"[Trap] {Definition.trapName} at {OccupiedCell} disarmed.");
        GetComponentInParent<FloorRoot>()?.TrapRegistry?.NotifyFlaggedChanged();
    }

    /// <summary>
    /// Re-arms this trap and forgets all awareness of it: disarmed becomes armed
    /// and flagged becomes hidden. Called for every trap on a floor the instant
    /// that floor clears of adventurers. The caller fires one NotifyFlaggedChanged().
    /// </summary>
    public void ResetArmed()
    {
        IsDisarmed = false;
        IsFlagged = false;
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