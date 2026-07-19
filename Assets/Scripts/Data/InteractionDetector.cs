using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class InteractionDetector : MonoBehaviour
{
    private IInteractable interactableInRange = null;
    public GameObject interactionIcon;

    void Start()
    {
        interactionIcon.SetActive(false);
    }

    private void LateUpdate()
    {
        // Interface references bypass Unity's destroyed-object null, so a
        // smashed target lingers "not null" forever. Prune it every frame
        // and put the prompt icon out over the wreckage.
        if (interactableInRange is UnityEngine.Object o && o == null)
        {
            interactableInRange = null;
            if (interactionIcon != null) interactionIcon.SetActive(false);
        }
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (interactableInRange is UnityEngine.Object gone && gone == null)
            interactableInRange = null;
        if (interactableInRange == null) return;

        interactableInRange.Interact();

        // Interact() may have destroyed the target (crates); re-probe before
        // asking the corpse anything.
        if (interactableInRange is UnityEngine.Object gone2 && gone2 == null)
            interactableInRange = null;

        if (interactableInRange == null || !interactableInRange.CanInteract())
        {
            if (interactionIcon != null) interactionIcon.SetActive(false);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.TryGetComponent(out IInteractable interactable) && interactable.CanInteract())
        {
            interactableInRange = interactable;
            interactionIcon.SetActive(true);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.TryGetComponent(out IInteractable interactable) && interactable == interactableInRange)
        {
            interactableInRange = null;
            interactionIcon.SetActive(false);
        }
    }
}
