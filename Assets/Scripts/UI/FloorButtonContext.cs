using UnityEngine;
using UnityEngine.EventSystems;

// Added to each floor-selector button at runtime by FloorSelectorHUD.
// Fires OnRightClick(floorIndex) on right-click; left-click stays with the Button.
public class FloorButtonContext : MonoBehaviour, IPointerClickHandler
{
    public int FloorIndex;
    public System.Action<int> OnRightClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
            OnRightClick?.Invoke(FloorIndex);
    }
}