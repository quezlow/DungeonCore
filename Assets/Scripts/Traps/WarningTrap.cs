using UnityEngine;

/// <summary>
/// A warning trap fires a named location alert to the HUD instead of damaging.
/// The player names each one on placement (e.g. "North Corridor", "Library Door").
///
/// Behaviour is intentionally consistent with regular traps:
///   - Cooldown prevents spam
///   - Flagged by Rogue detection → does not fire, but remains in place
///
/// Pathfinding does NOT route around warning traps when flagged (unlike damaging
/// traps), because they don't harm adventurers — they're purely intel for the
/// player. Adventurers walk through them normally; the trap just stays silent.
/// </summary>
public class WarningTrap : TrapBase
{
    [Header("Warning Trap")]
    [Tooltip("Player-set label for this trap, shown in HUD alerts.")]
    [SerializeField] private string warningLabel = "Unnamed Warning";

    public string WarningLabel => warningLabel;

    /// <summary>Set by the placement flow after the player types a name.</summary>
    public void SetWarningLabel(string label)
    {
        warningLabel = string.IsNullOrWhiteSpace(label) ? "Unnamed Warning" : label;
    }

    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        int floorIdx = GetComponentInParent<FloorRoot>()?.FloorIndex ?? -1;
        AlertsLog.Instance?.AddAlert( $"Adventurer detected at {warningLabel}", transform.position, floorIdx, AlertCategory.Trap);

        Debug.Log($"[WarningTrap] Alert fired: '{warningLabel}' at {OccupiedCell}.");
    }
}
