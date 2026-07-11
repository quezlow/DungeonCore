using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One node box on the research canvas. Instantiated and positioned by
/// ResearchTreeUI; carries no logic beyond display binding.
///
/// PREFAB SETUP (210 x 78 box):
///   Root -- Image (background) + Button (targets the background) + this script
///     Icon      -- Image (left, ~48px, preserve aspect)
///     NameLabel -- TMP (top right of icon)
///     SubLabel  -- TMP (smaller, under the name: cost/duration or the hint)
///     Badge     -- small Image tab (top-right corner) with BadgeLabel TMP
/// Wire all six references below.
/// </summary>
public class ResearchNodeView : MonoBehaviour
{
    public enum NodeState { Locked, Revealed, Understood }

    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text subLabel;
    [SerializeField] private GameObject badge;
    [SerializeField] private TMP_Text badgeLabel;

    public Button Button;
    public TechNodeDefinition Node { get; private set; }

    public void Bind(TechNodeDefinition node, NodeState state, bool active, bool queued,
                     Color cLocked, Color cRevealed, Color cUnderstood, Color iconSilhouette)
    {
        Node = node;

        Color bg = state == NodeState.Understood ? cUnderstood
                 : state == NodeState.Revealed ? cRevealed : cLocked;
        if (background != null) background.color = bg;

        if (icon != null)
        {
            icon.sprite = node.icon;
            icon.enabled = node.icon != null;
            icon.color = state == NodeState.Locked ? iconSilhouette : Color.white;
        }

        if (nameLabel != null)
            nameLabel.text = state == NodeState.Locked ? "???" : node.displayName;

        if (subLabel != null)
        {
            if (state == NodeState.Locked)
                subLabel.text = node.hiddenHint;
            else
            {
                int cost = ResearchController.Instance != null
                    ? ResearchController.Instance.CostFor(node) : node.pointCost;
                subLabel.text = cost + " pts  /  " + node.durationDays
                    + (node.durationDays == 1 ? " day" : " days");
            }
        }

        bool underway = active || queued;
        if (badge != null) badge.SetActive(underway);
        if (badgeLabel != null && underway)
            badgeLabel.text = active ? "UNDERWAY" : "QUEUED";
    }
}