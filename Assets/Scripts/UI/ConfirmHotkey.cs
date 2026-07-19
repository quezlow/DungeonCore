using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Presses the target button when Enter (or numpad Enter) is pressed while
/// this panel is active. Attach to a dialog root and drag its confirm button
/// in; keeps every confirm prompt keyboard-friendly without touching the
/// EventSystem's selection state.
/// </summary>
public class ConfirmHotkey : MonoBehaviour
{
    [SerializeField] private Button target;

    private void Update()
    {
        if (target == null || !target.gameObject.activeInHierarchy || !target.interactable) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            target.onClick.Invoke();
    }
}
