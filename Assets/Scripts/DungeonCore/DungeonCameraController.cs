using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class DungeonCameraController : MonoBehaviour
{
    public static DungeonCameraController Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CinemachineCamera cmCam;
    [SerializeField] private CinemachineConfiner2D confiner;

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
    private float currentFloorOriginY = 0f;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        mainCamera = Camera.main;

        var core = GameObject.FindGameObjectWithTag("DungeonCore");
        if (core != null) coreTransform = core.transform;

        if (coreTransform != null)
            transform.position = new Vector3(
                coreTransform.position.x, coreTransform.position.y, transform.position.z);

        if (cmCam == null)
        {
            Debug.LogError("[DungeonCameraController] cmCam not assigned.");
            enabled = false;
            return;
        }

        targetZoom = cmCam.Lens.OrthographicSize;

        if (FloorManager.Instance != null)
        {
            FloorManager.Instance.OnActiveFloorChanged += HandleFloorChanged;
            HandleFloorChanged(FloorManager.Instance.ActiveFloorIndex);
        }
        else
        {
            Debug.LogWarning("[DungeonCameraController] FloorManager.Instance null in Start — " +
                             "floor switching will not move the camera. Check execution order.");
        }
    }

    private void OnDestroy()
    {
        if (FloorManager.Instance != null)
            FloorManager.Instance.OnActiveFloorChanged -= HandleFloorChanged;
    }

    private void Update()
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

        if (isReturning) ReturnToCore();

        var lens = cmCam.Lens;
        lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, targetZoom,
            Time.deltaTime * zoomSmoothSpeed);
        cmCam.Lens = lens;
    }

    // ── Floor Transition ──────────────────────────────────────────

    private void HandleFloorChanged(int floorIndex)
    {
        var floor = FloorManager.Instance?.GetFloor(floorIndex);
        if (floor == null) return;

        Debug.Log($"[CameraController] Moving to floor {floorIndex}, Y={floor.WorldOriginY}");

        currentFloorOriginY = floor.WorldOriginY;

        transform.position = new Vector3(
            transform.position.x, currentFloorOriginY, transform.position.z);

        if (confiner != null && floor.CameraBounds != null)
        {
            confiner.BoundingShape2D = floor.CameraBounds;
            confiner.InvalidateBoundingShapeCache();
        }

        cmCam?.ForceCameraPosition(
            new Vector3(transform.position.x, currentFloorOriginY, -10f),
            Quaternion.identity);

        timeSinceLastInput = 0f;
        isReturning = false;
    }

    // ── Pan ───────────────────────────────────────────────────────

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
            Vector2 mp = mouse.position.ReadValue();
            if (mp.x < edgeScrollThreshold) move.x -= edgeScrollSpeed * Time.deltaTime;
            if (mp.x > Screen.width - edgeScrollThreshold) move.x += edgeScrollSpeed * Time.deltaTime;
            if (mp.y < edgeScrollThreshold) move.y -= edgeScrollSpeed * Time.deltaTime;
            if (mp.y > Screen.height - edgeScrollThreshold) move.y += edgeScrollSpeed * Time.deltaTime;
        }

        if (mouse != null)
        {
            if (mouse.middleButton.wasPressedThisFrame)
            {
                dragOrigin = mainCamera.ScreenToWorldPoint(
                    new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 0f));
                isDragging = true;
            }
            if (mouse.middleButton.isPressed && isDragging)
            {
                Vector3 dragCurrent = mainCamera.ScreenToWorldPoint(
                    new Vector3(mouse.position.ReadValue().x, mouse.position.ReadValue().y, 0f));
                Vector3 delta = dragOrigin - dragCurrent;
                move += new Vector3(delta.x, delta.y, 0f);
                dragOrigin = dragCurrent;
            }
            if (mouse.middleButton.wasReleasedThisFrame) isDragging = false;
        }

        bool hadInput = move != Vector3.zero || isDragging;
        if (hadInput) transform.position += move;
        return hadInput;
    }

    // ── Zoom ──────────────────────────────────────────────────────

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

    // ── Return to Core ────────────────────────────────────────────

    private void ReturnToCore()
    {
        Vector3 target = new Vector3(
            coreTransform.position.x,
            currentFloorOriginY,
            transform.position.z);

        transform.position = Vector3.Lerp(transform.position, target, returnSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, target) < 0.05f)
        {
            transform.position = target;
            isReturning = false;
        }
    }

    // ── Public API ────────────────────────────────────────────────

    public void ForceReturnToCore() { timeSinceLastInput = returnDelay; }

    public void PanTo(Vector3 worldPos)
    {
        transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        timeSinceLastInput = 0f;
        isReturning = false;
    }

    /// <summary>
    /// Day 28. Cross-floor pan. If floorIndex differs from the active floor,
    /// switches floor first (which moves the camera anchor to the new floor's
    /// origin Y), then pans XY to worldPos. Safe to call from UI click handlers.
    /// </summary>
    public void PanTo(Vector3 worldPos, int floorIndex)
    {
        if (FloorManager.Instance != null
            && floorIndex >= 0
            && floorIndex != FloorManager.Instance.ActiveFloorIndex)
        {
            FloorManager.Instance.SwitchToFloor(floorIndex);
        }
        PanTo(worldPos);
    }
}