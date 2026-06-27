using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Toggles the dungeon HUD on/off with a hotkey (default F9) for clean-view /
/// screenshots. Uses a CanvasGroup so nothing is deactivated — every HUD script
/// keeps running and re-shows cleanly, avoiding the Awake/OnEnable re-init pitfalls
/// of SetActive toggling.
///
/// SCENE SETUP:
///   1. Add this component to any persistent object (e.g. UICanvas_Dungeon).
///   2. Drag the "＝ HUD ＝" group object into `hudRoot`.
///      (A CanvasGroup is added to it automatically at runtime if missing.)
///   3. Leave NightOverlay and panels OUTSIDE that group so they stay visible.
/// </summary>
public class HudToggle : MonoBehaviour
{
    [Tooltip("The HUD container to show/hide (e.g. the \"＝ HUD ＝\" group).")]
    [SerializeField] private GameObject hudRoot;

    [Tooltip("Key that toggles the HUD.")]
    [SerializeField] private Key toggleKey = Key.F9;

    private CanvasGroup group;
    private bool hidden;

    private void Awake()
    {
        if (hudRoot == null) return;
        group = hudRoot.GetComponent<CanvasGroup>();
        if (group == null) group = hudRoot.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || group == null) return;

        if (kb[toggleKey].wasPressedThisFrame)
        {
            hidden = !hidden;
            group.alpha = hidden ? 0f : 1f;
            group.interactable = !hidden;
            group.blocksRaycasts = !hidden;
        }
    }
}