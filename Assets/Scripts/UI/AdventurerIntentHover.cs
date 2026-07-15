using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Reveals a party's Intent as a cursor tooltip when the player hovers an
/// adventurer, but only once the Oracle Chamber unlock is understood
/// (UnlockState.OracleChamber). Until then it is a no-op and intent stays hidden.
///
/// Reuses the shared TooltipController. Hovering the game world and hovering UI
/// never overlap, and this only hides the tooltip it raised itself. Requires
/// each adventurer to carry a trigger Collider2D (the same one the click-to-
/// inspect flow uses).
///
/// SCENE SETUP: one component on the managers/HUD object, beside
/// AdventurerInspectController. Needs the scene's TooltipController present.
/// </summary>
public class AdventurerIntentHover : MonoBehaviour
{
    [Tooltip("How often (seconds, unscaled) to re-test what's under the cursor.")]
    [SerializeField] private float hoverPollSeconds = 0.05f;

    private Camera cam;
    private bool showing;
    private float pollTimer;

    private void Awake() { cam = Camera.main; }

    private void Update()
    {
        // Gated on the Oracle Chamber unlock — no reveal before it is understood.
        if (!UnlockState.IsUnlocked(UnlockState.OracleChamber)) { ClearIfShowing(); return; }
        if (Mouse.current == null || TooltipController.Instance == null) { ClearIfShowing(); return; }

        // Never fight the build-menu tooltips for the cursor while it is over UI.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        { ClearIfShowing(); return; }

        pollTimer -= Time.unscaledDeltaTime;   // unscaled so it works while paused
        if (pollTimer > 0f) return;
        pollTimer = hoverPollSeconds;

        if (cam == null) cam = Camera.main;
        if (cam == null) { ClearIfShowing(); return; }

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        world.z = 0f;

        DungeonAdventurer found = null;
        var hits = Physics2D.OverlapPointAll(world);
        foreach (var h in hits)
        {
            if (h == null) continue;
            var adv = h.GetComponentInParent<DungeonAdventurer>();
            if (adv != null) { found = adv; break; }
        }

        if (found == null) { ClearIfShowing(); return; }

        showing = true;
        TooltipController.Instance.Show(TitleFor(found), BodyFor(found));
    }

    private void ClearIfShowing()
    {
        if (!showing) return;
        showing = false;
        TooltipController.Instance?.Hide();
    }

    private static string TitleFor(DungeonAdventurer adv)
    {
        string name = adv.DisplayName;
        return string.IsNullOrEmpty(name) ? adv.Type.ToString() : name + " · " + adv.Type;
    }

    private static string BodyFor(DungeonAdventurer adv)
        => "Intent: " + IntentLabel(adv.Intent) + "\n" + IntentBlurb(adv.Intent);

    /// <summary>Player-facing intent name. Shared so the stats panel can reuse it.</summary>
    public static string IntentLabel(PartyIntent intent) => intent switch
    {
        PartyIntent.Pilgrim => "Pilgrim",
        PartyIntent.GiftGiver => "Gift Giver",
        PartyIntent.Destroyer => "Destroyer",
        PartyIntent.Delver => "Delver",
        _ => intent.ToString(),
    };

    private static string IntentBlurb(PartyIntent intent) => intent switch
    {
        PartyIntent.Pilgrim => "Comes to worship the core, then leaves in peace.",
        PartyIntent.GiftGiver => "Bears tribute for the core before acting.",
        PartyIntent.Destroyer => "Beelines the core — here to end it.",
        PartyIntent.Delver => "Hunts monsters for loot and glory, then departs.",
        _ => "",
    };
}