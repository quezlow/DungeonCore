using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pressure plate. Scans for adventurers within a configurable radius each frame.
/// When triggered, fires the linked trap (stored by cell) once.
///
/// LINKING
///   The link is set by the player via TrapLinkPickerUI, which opens when the
///   player left-clicks the placed plate. The link is saved as Vector3Int cell
///   so it survives save/load without object reference issues. At trigger time,
///   the linked trap is looked up via TrapRegistry.GetTrapAt(linkedCell).
///
/// EXTERNAL TRIGGER SEMANTICS
///   The linked trap fires via TrapBase.TriggerExternally(adv), which BYPASSES
///   both the linked trap's own cooldown AND its flagged state. Rationale: the
///   adventurer is stepping on the plate (which they don't know about), not on
///   the linked trap directly. The plate's cooldown governs the cadence.
///
/// PREFAB SETUP
///   PressurePlateTrap (this script + SpriteRenderer)
///   Add a CircleCollider2D (Is Trigger) used by Physics2D.OverlapPoint for
///   click detection (linking flow). Trigger radius is separate from this collider.
/// </summary>
public class PressurePlateTrap : TrapBase
{
    [Header("Pressure Plate")]
    [Tooltip("World-space radius within which adventurers trigger the plate.")]
    [SerializeField] private float triggerRadius = 1.5f;

    [Tooltip("Cell of the linked trap. Set via TrapLinkPickerUI.")]
    [SerializeField] private Vector3Int linkedCell;

    [Tooltip("True when a valid link has been assigned. Drives the panel/save logic.")]
    [SerializeField] private bool hasLink = false;

    private Collider2D myCollider;

    public bool HasLink => hasLink;
    public Vector3Int LinkedCell => linkedCell;
    public float TriggerRadius => triggerRadius;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        myCollider = GetComponent<Collider2D>();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        HandleLinkClick();
        ScanForAdventurersInRadius();
    }

    // ── Linking ───────────────────────────────────────────────────

    /// <summary>Called by TrapLinkPickerUI when the player selects a target trap.</summary>
    public void SetLink(Vector3Int cell)
    {
        linkedCell = cell;
        hasLink    = true;
        Debug.Log($"[PressurePlate] Linked plate at {OccupiedCell} to trap at {cell}.");
    }

    public void ClearLink()
    {
        hasLink = false;
        Debug.Log($"[PressurePlate] Link cleared at {OccupiedCell}.");
    }

    private void HandleLinkClick()
    {
        if (myCollider == null) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (Camera.main == null) return;

        Vector3 screen = Mouse.current.position.ReadValue();
        Vector3 world  = Camera.main.ScreenToWorldPoint(screen);
        world.z = 0f;

        if (myCollider.OverlapPoint(world))
            TrapLinkPickerUI.Instance?.Open(this);
    }

    // ── Trigger Scan ──────────────────────────────────────────────

    private void ScanForAdventurersInRadius()
    {
        if (Definition == null) return;
        if (IsFlagged) return;
        if (Time.time - lastTriggerTimePublic < Definition.cooldown) return;

        var all = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        float radiusSq = triggerRadius * triggerRadius;

        foreach (var adv in all)
        {
            float dx = adv.transform.position.x - transform.position.x;
            float dy = adv.transform.position.y - transform.position.y;
            if (dx * dx + dy * dy <= radiusSq)
            {
                FireLinkedTrap(adv);
                lastTriggerTimePublic = Time.time;
                return; // one-shot per cooldown
            }
        }
    }

    /// <summary>
    /// Tracks cooldown locally on the plate. (TrapBase.lastTriggerTime is private,
    /// and the plate's trigger logic differs from TrapBase's cell-entry flow.)
    /// </summary>
    private float lastTriggerTimePublic = -999f;

    private void FireLinkedTrap(DungeonAdventurer adv)
    {
        if (!hasLink || TrapRegistry.Instance == null) return;

        var linked = TrapRegistry.Instance.GetTrapAt(linkedCell);
        if (linked == null)
        {
            Debug.LogWarning($"[PressurePlate] Link broken at {OccupiedCell} — no trap at {linkedCell}.");
            return;
        }

        Debug.Log($"[PressurePlate] Plate at {OccupiedCell} fired linked trap at {linkedCell}.");
        linked.TriggerExternally(adv);
    }

    // ── Trap Effect Override ──────────────────────────────────────

    /// <summary>
    /// The plate itself has no direct effect — its effect is firing the linked trap.
    /// Cell-based triggers via TrapBase.OnAdventurerEntered are ignored (radius scan handles it).
    /// </summary>
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        // Intentionally empty. Trigger goes through ScanForAdventurersInRadius → FireLinkedTrap.
    }

    // ── Gizmos ────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);

        if (hasLink && TileInfluenceManager.Instance != null)
        {
            Vector3 linkedWorld = TileInfluenceManager.Instance.CellToWorld(linkedCell);
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.7f);
            Gizmos.DrawLine(transform.position, linkedWorld);
        }
    }
}
