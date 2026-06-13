using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pressure plate. Scans for adventurers and wild monsters within a
/// configurable radius each frame. When triggered, fires the linked trap
/// (stored by cell) once.
///
/// LINKING
///   The link is set by the player via TrapLinkPickerUI, which opens when the
///   player left-clicks the placed plate. The link is saved as Vector3Int cell
///   so it survives save/load without object reference issues. At trigger time,
///   the linked trap is looked up via TrapRegistry.GetTrapAt(linkedCell).
///
/// EXTERNAL TRIGGER SEMANTICS
///   The linked trap fires via TrapBase.TriggerExternally(adv) for adventurers
///   or TrapBase.TriggerExternallyMonster(m) for monsters. Both BYPASS the
///   linked trap's own cooldown AND its flagged state — the adventurer/monster
///   stepped on the PLATE, not on the linked trap directly. The plate's own
///   cooldown governs the cadence.
///
/// DAY 31 PART 3C — Wild monsters now trigger pressure plates (T5).
///   Adventurer scan runs first; if no adventurer is in range, the wild-monster
///   scan runs. Player monsters are excluded — only IsWild monsters trigger.
///
/// PREFAB SETUP
///   PressurePlateTrap (this script + SpriteRenderer)
///   Add a CircleCollider2D (Is Trigger) used by Physics2D.OverlapPoint for
///   click detection (linking flow). Trigger radius is separate from this collider.
/// </summary>
public class PressurePlateTrap : TrapBase
{
    [Header("Pressure Plate")]
    [Tooltip("World-space radius within which adventurers or wild monsters trigger the plate.")]
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
        ScanForTargetsInRadius();
    }

    // ── Linking ───────────────────────────────────────────────────

    public void SetLink(Vector3Int cell)
    {
        linkedCell = cell;
        hasLink = true;
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
        Vector3 world = Camera.main.ScreenToWorldPoint(screen);
        world.z = 0f;

        if (myCollider.OverlapPoint(world))
            TrapLinkPickerUI.Instance?.Open(this);
    }

    // ── Trigger Scan ──────────────────────────────────────────────

    private void ScanForTargetsInRadius()
    {
        if (Definition == null) return;
        if (IsFlagged) return;
        if (Time.time - lastTriggerTimePublic < Definition.cooldown) return;

        var floor = GetComponentInParent<FloorRoot>();
        if (floor?.Entities == null) return;
        Vector3 myPos = transform.position;

        // Adventurers — only on THIS floor (bug fix: previous code had no floor filter).
        var adv = floor.Entities.Nearest<DungeonAdventurer>(myPos, triggerRadius);
        if (adv != null)
        {
            FireLinkedTrapForAdventurer(adv);
            lastTriggerTimePublic = Time.time;
            return;
        }

        // Wild monsters — same floor only.
        var m = floor.Entities.Nearest<DungeonMonster>(myPos, triggerRadius, x => x.IsWild);
        if (m != null)
        {
            FireLinkedTrapForMonster(m);
            lastTriggerTimePublic = Time.time;
            return;
        }
    }

    /// <summary>
    /// Tracks cooldown locally on the plate. (TrapBase.lastTriggerTime is private,
    /// and the plate's trigger logic differs from TrapBase's cell-entry flow.)
    /// </summary>
    private float lastTriggerTimePublic = -999f;

    private void FireLinkedTrapForAdventurer(DungeonAdventurer adv)
    {
        if (!hasLink || TrapRegistry.Instance == null) return;

        var linked = TrapRegistry.Instance.GetTrapAt(linkedCell);
        if (linked == null)
        {
            Debug.LogWarning($"[PressurePlate] Link broken at {OccupiedCell} — no trap at {linkedCell}.");
            return;
        }

        Debug.Log($"[PressurePlate] Plate at {OccupiedCell} fired linked trap at {linkedCell} (adventurer).");
        linked.TriggerExternally(adv);
    }

    private void FireLinkedTrapForMonster(DungeonMonster m)
    {
        if (!hasLink || TrapRegistry.Instance == null) return;

        var linked = TrapRegistry.Instance.GetTrapAt(linkedCell);
        if (linked == null)
        {
            Debug.LogWarning($"[PressurePlate] Link broken at {OccupiedCell} — no trap at {linkedCell}.");
            return;
        }

        Debug.Log($"[PressurePlate] Plate at {OccupiedCell} fired linked trap at {linkedCell} (wild monster).");
        linked.TriggerExternallyMonster(m);
    }

    // ── Trap Effect Overrides ─────────────────────────────────────

    /// <summary>
    /// The plate itself has no direct effect — its effect is firing the linked trap.
    /// Cell-based triggers via TrapBase.OnAdventurerEntered are ignored.
    /// </summary>
    protected override void ApplyEffect(DungeonAdventurer adv) { /* intentionally empty */ }

    /// <summary>
    /// DAY 31 PART 3C — Symmetric no-op for monsters. The plate fires its
    /// linked trap externally; it never directly damages anyone.
    /// </summary>
    protected override void ApplyEffect(DungeonMonster m) { /* intentionally empty */ }

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