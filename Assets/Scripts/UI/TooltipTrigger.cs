using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to any UI element to show the shared tooltip on hover. Set content in the
/// Inspector (static elements like build-submenu category buttons) or via SetContent()
/// when an element is built from data (e.g. RoomTypePickerUI entries).
/// </summary>
public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, TextArea] private string title;
    [SerializeField, TextArea] private string body;
    [SerializeField] private float delay = 0.3f;

    private bool hovering;
    private bool shown;
    private float timer;

    public void SetContent(string newTitle, string newBody)
    {
        title = newTitle;
        body = newBody;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        shown = false;
        timer = 0f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        if (shown && TooltipController.Instance != null) TooltipController.Instance.Hide();
        shown = false;
    }

    private void Update()
    {
        if (!hovering || shown) return;
        timer += Time.unscaledDeltaTime;
        if (timer >= delay)
        {
            shown = true;
            if (TooltipController.Instance != null) TooltipController.Instance.Show(title, body);
        }
    }

    private void OnDisable()
    {
        hovering = false;
        if (shown && TooltipController.Instance != null) TooltipController.Instance.Hide();
        shown = false;
    }
}