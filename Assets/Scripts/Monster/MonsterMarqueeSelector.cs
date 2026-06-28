using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// RTS marquee box-select for monsters (Claim mode only). Arms on an empty-ground
/// left-press; if the press is on a unit it bows out so the click-select handles it.
/// On release, selects every spawner whose (visible) position falls inside the box.
/// Hold Shift to add to the current selection. Builds on Part 1's SelectSet.
///
/// SCENE SETUP: add to a persistent UI/manager object. Wire `marqueeRect` to a UI
/// Image under the dungeon canvas (anchor + pivot bottom-left, Raycast Target OFF).
/// </summary>
public class MonsterMarqueeSelector : MonoBehaviour
{
    [SerializeField] private RectTransform marqueeRect;
    [SerializeField] private Camera worldCamera;
    [Tooltip("Screen pixels of movement before a press counts as a drag.")]
    [SerializeField] private float dragThreshold = 8f;

    private Canvas canvas;
    private Vector2 startScreen;
    private bool armed;
    private bool dragging;

    private readonly List<MonsterSpawner> spawnerBuf = new();
    private readonly List<MonsterSpawner> picked = new();

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (marqueeRect != null)
        {
            canvas = marqueeRect.GetComponentInParent<Canvas>();
            marqueeRect.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (DungeonBuildController.Instance == null
            || DungeonBuildController.Instance.CurrentMode != BuildMode.Claim)
        { CancelDrag(); return; }

        if (mouse.leftButton.wasPressedThisFrame) TryArm(mouse);
        else if (armed && mouse.leftButton.isPressed) UpdateDrag(mouse);
        else if (armed && mouse.leftButton.wasReleasedThisFrame) FinishDrag(mouse);
    }

    private void TryArm(Mouse mouse)
    {
        armed = false; dragging = false;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (worldCamera == null) return;

        Vector2 screen = mouse.position.ReadValue();
        Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        world.z = 0f;

        // Pressing on a unit? Let the click-select handle it — no marquee.
        var hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (c.GetComponentInParent<DungeonMonster>() != null) return;
            if (c.GetComponentInParent<MonsterSpawner>() != null) return;
        }

        armed = true;
        startScreen = screen;
    }

    private void UpdateDrag(Mouse mouse)
    {
        Vector2 screen = mouse.position.ReadValue();
        if (!dragging)
        {
            if (Vector2.Distance(screen, startScreen) < dragThreshold) return;
            dragging = true;
            if (marqueeRect != null) marqueeRect.gameObject.SetActive(true);
        }
        DrawBox(startScreen, screen);
    }

    private void FinishDrag(Mouse mouse)
    {
        if (dragging)
            SelectInBox(startScreen, mouse.position.ReadValue(), ShiftHeld());
        CancelDrag();
    }

    private void CancelDrag()
    {
        armed = false; dragging = false;
        if (marqueeRect != null) marqueeRect.gameObject.SetActive(false);
    }

    private static bool ShiftHeld()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    private void DrawBox(Vector2 a, Vector2 b)
    {
        if (marqueeRect == null) return;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (scale <= 0f) scale = 1f;
        Vector2 lo = Vector2.Min(a, b) / scale;
        Vector2 size = new Vector2(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) / scale;
        marqueeRect.anchoredPosition = lo;
        marqueeRect.sizeDelta = size;
    }

    private void SelectInBox(Vector2 a, Vector2 b, bool additive)
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null || worldCamera == null) return;

        Vector2 lo = Vector2.Min(a, b);
        Vector2 hi = Vector2.Max(a, b);
        var box = new Rect(lo.x, lo.y, hi.x - lo.x, hi.y - lo.y);

        floor.Entities.FillAll(spawnerBuf);
        picked.Clear();
        for (int i = 0; i < spawnerBuf.Count; i++)
        {
            var sp = spawnerBuf[i];
            if (sp == null) continue;

            Vector3 worldPos = (sp.HasLiveMonster && sp.SpawnedMonster != null)
                ? sp.SpawnedMonster.transform.position
                : sp.transform.position;

            Vector3 screen = worldCamera.WorldToScreenPoint(worldPos);
            if (box.Contains(new Vector2(screen.x, screen.y))) picked.Add(sp);
        }

        // Empty box + replace = clear; empty box + additive = leave selection alone.
        if (picked.Count > 0 || !additive)
            SpawnerSelectionController.Instance?.SelectSet(picked, additive);
    }
}