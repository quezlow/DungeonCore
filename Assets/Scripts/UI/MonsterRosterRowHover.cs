using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Relays pointer enter/exit on a roster row to the picker's detail pane. Added at
/// runtime by MonsterSelectionUI.SpawnRow, so the row prefab needs no extra component.
/// </summary>
public class MonsterRosterRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private MonsterSelectionUI owner;
    private MonsterDefinition def;
    private bool mystery;

    public void Bind(MonsterSelectionUI owner, MonsterDefinition def, bool mystery)
    {
        this.owner = owner;
        this.def = def;
        this.mystery = mystery;
    }

    public void OnPointerEnter(PointerEventData _) => owner?.PreviewRow(def, mystery);

    public void OnPointerExit(PointerEventData _) => owner?.ClearPreview();
}
