using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Watches for a left-click on an adventurer and opens the AdventurerStatsPanel on it.
/// Active only in the neutral build mode (BuildMode.None), never over UI, and only
/// while the AdventurerStats feature is unlocked (stub-gated). Requires each
/// adventurer to carry a trigger Collider2D (a CircleCollider2D with Is Trigger on)
/// so the click is picked up by Physics2D.OverlapPointAll.
/// </summary>
public class AdventurerInspectController : MonoBehaviour
{
    private Camera cam;

    private void Awake() { cam = Camera.main; }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (!UnlockState.IsUnlocked(UnlockState.AdventurerStats)) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        world.z = 0f;

        var hits = Physics2D.OverlapPointAll(world);
        foreach (var h in hits)
        {
            if (h == null) continue;
            var adv = h.GetComponentInParent<DungeonAdventurer>();
            if (adv != null) { AdventurerStatsPanel.Instance?.Show(adv); return; }
        }
    }
}