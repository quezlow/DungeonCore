using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single shared tooltip panel for build menus. TooltipTrigger components call Show/Hide.
/// The panel follows the cursor and flips its pivot by screen quadrant so it never runs
/// off-screen. Put one on the HUD canvas as the LAST child (so it draws on top) and leave
/// it active — it self-hides in Awake. Assumes a Screen Space - Overlay canvas.
/// </summary>
public class TooltipController : MonoBehaviour
{
    public static TooltipController Instance { get; private set; }

    [SerializeField] private RectTransform panel;       // themed tooltip panel (Board_Inset), anchored centre
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private RectTransform canvasRect;  // the root canvas RectTransform
    [SerializeField] private Vector2 cursorOffset = new Vector2(16f, 16f);

    private void Awake()
    {
        Instance = this;
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Show(string title, string body)
    {
        if (panel == null) return;
        if (titleLabel != null)
        {
            titleLabel.text = title ?? string.Empty;
            titleLabel.gameObject.SetActive(!string.IsNullOrEmpty(title));
        }
        if (bodyLabel != null)
        {
            bodyLabel.text = body ?? string.Empty;
            bodyLabel.gameObject.SetActive(!string.IsNullOrEmpty(body));
        }
        panel.gameObject.SetActive(true);
        Reposition();
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (panel != null && panel.gameObject.activeSelf) Reposition();
    }

    private void Reposition()
    {
        if (panel == null || canvasRect == null || Mouse.current == null) return;

        Vector2 screen = Mouse.current.position.ReadValue();

        // Flip the pivot toward the cursor's quadrant so the panel stays on screen.
        float px = screen.x > Screen.width * 0.5f ? 1f : 0f;
        float py = screen.y > Screen.height * 0.5f ? 1f : 0f;
        panel.pivot = new Vector2(px, py);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local))
        {
            float ox = px == 0f ? cursorOffset.x : -cursorOffset.x;
            float oy = py == 0f ? cursorOffset.y : -cursorOffset.y;
            panel.anchoredPosition = local + new Vector2(ox, oy);
        }
    }
}