using UnityEngine;
using UnityEngine.EventSystems;

public class ItemDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    Transform originalParent;
    CanvasGroup canvasGroup;

    public float minDropDistance = 2f;
    public float maxDropDistance = 3f;


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
            //If drop location is outside of inventory
            if (!IsWithinInventory(eventData.position))
            {
                //Drop item
                DropItem(originalSlot);
            }
            else
            {
                //no slot under drop point
                //snap back to original slot
                transform.SetParent(originalParent);
            }
        }

        GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
    }

    bool IsWithinInventory(Vector2 mousePosition)
    {
        RectTransform inventoryRect = originalParent.parent.GetComponent<RectTransform>();
        return RectTransformUtility.RectangleContainsScreenPoint(inventoryRect, mousePosition);
    }

    void DropItem(Slot originalSlot)
    {
        originalSlot.currentItem = null;

        //find player to drop nearby
        Transform playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
        if(playerTransform == null)
        {
            Debug.Log("Missing 'Player' tag.");
            return;
        }
        //random drop position
        Vector2 dropOffset = Random.insideUnitCircle.normalized * Random.Range(minDropDistance, maxDropDistance);
        Vector2 dropPosition = (Vector2)playerTransform.position + dropOffset;

        //instantiate drop item
        GameObject dropItem = Instantiate(gameObject, dropPosition, Quaternion.identity);
        dropItem.GetComponent<BounceEffect>().StartBounce();

        //destroy ui item
        Destroy(gameObject);
    }
}
