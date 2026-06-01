using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class DungeonCameraController : MonoBehaviour
{
    public static DungeonCameraController Instance { get; private set; }


    [Header("References")]
    [SerializeField] private CinemachineCamera cmCam;

    [Header("Pan Settings")]
    [SerializeField] private float keyboardPanSpeed = 8f;
    [SerializeField] private float edgeScrollSpeed = 6f;
    [SerializeField] private float edgeScrollThreshold = 20f;
    [SerializeField] private bool enableEdgeScroll = true;

    [Header("Zoom Settings")]
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float minZoom = 3f;
    [SerializeField] private float maxZoom = 12f;
    [SerializeField] private float zoomSmoothSpeed = 6f;

    [Header("Return to Core")]
    [SerializeField] private float returnDelay = 5f;
    [SerializeField] private float returnSpeed = 4f;

    private Camera mainCamera;

    private Transform coreTransform;
    private float timeSinceLastInput;
    private bool isReturning;
    private float targetZoom;

    private Vector3 dragOrigin;
    private bool isDragging;

    void Start()
    {
        mainCamera = Camera.main;

        GameObject core = GameObject.FindGameObjectWithTag("DungeonCore");
        if (core != null)
            coreTransform = core.transform;

        if (coreTransform != null)
            transform.position = new Vector3(coreTransform.position.x, coreTransform.position.y, transform.position.z);

        if (cmCam == null)
        {
            Debug.LogError("DungeonCameraController: cmCam is not assigned.");
            enabled = false;
            return;
        }

        targetZoom = cmCam.Lens.OrthographicSize;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }


    void Update()
    {
        if (PauseController.IsGamePaused) return;

        HandleZoom();

        bool hadInput = HandlePan();

        if (hadInput)
        {
            timeSinceLastInput = 0f;
            isReturning = false;
        }
        else
        {
            timeSinceLastInput += Time.deltaTime;
            if (timeSinceLastInput >= returnDelay && coreTransform != null)
                isReturning = true;
        }

        if (isReturning)
            ReturnToCore();

        // Smooth zoom
        float currentSize = cmCam.Lens.OrthographicSize;
        var lens = cmCam.Lens;
        lens.OrthographicSize = Mathf.Lerp(currentSize, targetZoom, Time.deltaTime * zoomSmoothSpeed);
        cmCam.Lens = lens;
    }

    private bool HandlePan()
    {
        Vector3 move = Vector3.zero;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard != null)
        {
            float h = 0f, v = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;

            if (Mathf.Abs(h) > 0f || Mathf.Abs(v) > 0f)
                move += new Vector3(h, v, 0f).normalized * keyboardPanSpeed * Time.deltaTime;
        }

        if (enableEdgeScroll && mouse != null
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            Vector2 mousePos = mouse.position.ReadValue();
            if (mousePos.x < edgeScrollThreshold) move.x -= edgeScrollSpeed * Time.deltaTime;
            if (mousePos.x > Screen.width - edgeScrollThreshold) move.x += edgeScrollSpeed * Time.deltaTime;
            if (mousePos.y < edgeScrollThreshold) move.y -= edgeScrollSpeed * Time.deltaTime;
            if (mousePos.y > Screen.height - edgeScrollThreshold) move.y += edgeScrollSpeed * Time.deltaTime;
        }

        if (mouse != null)
        {
            if (mouse.middleButton.wasPressedThisFrame)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                dragOrigin = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
                isDragging = true;
            }

            if (mouse.middleButton.isPressed && isDragging)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                Vector3 dragCurrent = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
                Vector3 dragDelta = dragOrigin - dragCurrent;
                move += new Vector3(dragDelta.x, dragDelta.y, 0f);
                dragOrigin = dragCurrent;
            }

            if (mouse.middleButton.wasReleasedThisFrame)
                isDragging = false;
        }

        bool hadInput = move != Vector3.zero || isDragging;
        if (hadInput)
            transform.position += move;

        return hadInput;
    }

    private void HandleZoom()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetZoom -= scroll * zoomSpeed;
            targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        }
    }

    private void ReturnToCore()
    {
        Vector3 corePos = new Vector3(coreTransform.position.x, coreTransform.position.y, transform.position.z);
        transform.position = Vector3.Lerp(transform.position, corePos, returnSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, corePos) < 0.05f)
        {
            transform.position = corePos;
            isReturning = false;
        }
    }

    public void ForceReturnToCore()
    {
        timeSinceLastInput = returnDelay;
    }

    public void PanTo(Vector3 worldPos)
    {
        transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        timeSinceLastInput = 0f;
        isReturning = false;
    }

}