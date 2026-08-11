using UnityEngine;

/// <summary>
/// The pause rule, in one place (canon 39).
///
///   Pause permits DECIDING. It forbids ACTING.
///
/// Deciding is selection, navigation, browsing, orders, and commitments that
/// touch nothing but a ledger -- research, trade. Acting is anything that
/// reaches an entity standing on the board or a cell of the tilemap: placing,
/// removing, spawning, damaging, healing, retyping, channelling.
///
/// This helper exists because the audit that produced canon 39 found the same
/// rule written eleven different ways, and three of them inverted: the OPENER
/// was gated and the committing button behind it was not, so pausing before
/// you clicked locked you out while pausing after gave you free rein. Gate
/// the commit through here and that class of defect cannot recur.
///
/// Gate the ACTION, never the opener. A panel must always open, browse and
/// inspect while the world is held; only the button that commits refuses.
/// </summary>
public static class PauseGate
{
    /// <summary>The wisp's standing refusal when the world is held.</summary>
    public const string HeldReason = "Not while the world is held.";

    /// <summary>True when the world is frozen and acting is forbidden.</summary>
    public static bool Held => PauseController.IsGamePaused;

    /// <summary>
    /// True when the caller may act on the world. Fills a wisp-voice reason
    /// on false so the caller can toast it wherever it has a position.
    /// </summary>
    public static bool CanAct(out string reason)
    {
        if (PauseController.IsGamePaused)
        {
            reason = HeldReason;
            return false;
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// Refuse an action at a world position. Returns TRUE when the action was
    /// refused, so call sites read: if (PauseGate.RefuseAt(pos)) return;
    /// </summary>
    public static bool RefuseAt(Vector3 worldPos, string reason = null)
    {
        if (!PauseController.IsGamePaused) return false;
        BuildFeedback.Reject(worldPos, string.IsNullOrEmpty(reason) ? HeldReason : reason);
        return true;
    }

    /// <summary>
    /// Refuse an action that has no world position of its own (a HUD button).
    /// Speaks through the alert log at the core. Returns TRUE when refused.
    /// </summary>
    public static bool RefuseAtCore(string reason = null)
    {
        if (!PauseController.IsGamePaused) return false;
        Vector3 at = DungeonCore.Instance != null
            ? DungeonCore.Instance.transform.position : Vector3.zero;
        AlertsLog.Instance?.AddAlert(
            string.IsNullOrEmpty(reason) ? HeldReason : reason, at, -1, AlertCategory.System);
        return true;
    }
}
