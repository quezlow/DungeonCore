using UnityEngine;
using UnityEngine.EventSystems;

public class ItemDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    Transform originalParent;
    CanvasGroup canvasGroup;


    void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }
    public void OnBeginDrag(PointerEventData eventData)
    {
        originalParent = transform.parent; //save original parent
        transform.SetParent(transform.root); //Above other canvas
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0.6f; //semi transparent during drag
    }

    public void OnDrag(PointerEventData eventData)
    {
        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true; //enables raycasts
        canvasGroup.alpha = 1f; //no longer transparent

        Slot dropSlot = eventData.pointerEnter?.GetComponent<Slot>(); //slot where ite is dropped
        if(dropSlot == null)
        {
            GameObject dropItem = eventData.pointerEnter;
            if( dropItem != null )
            {
                dropSlot = dropItem.GetComponentInParent<Slot>();
            }
        }

        Slot originalSlot = originalParent.GetComponent<Slot>();

        if(dropSlot != null)
        {
            if(dropSlot.currentItem != null)
            {
                //slot has an item - swap items
                dropSlot.currentItem.transform.SetParent(originalSlot.transform);
                originalSlot.currentItem = dropSlot.currentItem;
                dropSlot.currentItem.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            else
            {
                originalSlot.currentItem = null;
            }

            //move item into drop slot
            transform.SetParent(dropSlot.transform);
            dropSlot.currentItem = gameObject;
        }
        else
        {
            //no slot under drop point
            transform.SetParent(originalParent);
        }

        GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
    }
}
