using UnityEngine;

/// Puts the active dungeon camera into Y-sort (CustomAxis 0,1,0) so per-tile
/// Individual tilemaps and entity SpriteRenderers interleave by depth.
/// Reverts on disable, leaving the overworld camera untouched.
[DisallowMultipleComponent]
public class DungeonCameraSortAxis : MonoBehaviour
{
    [Tooltip("(0,1,0) sorts by Y: lower on screen draws in front.")]
    [SerializeField] private Vector3 sortAxis = new Vector3(0f, 1f, 0f);

    private Camera cam;
    private TransparencySortMode prevMode;
    private Vector3 prevAxis;
    private bool applied;

    void OnEnable() { Apply(); }
    void OnDisable() { Revert(); }

    void Apply()
    {
        cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[DungeonCameraSortAxis] No Camera.main."); return; }
        prevMode = cam.transparencySortMode;
        prevAxis = cam.transparencySortAxis;
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = sortAxis;
        applied = true;
    }

    void Revert()
    {
        if (!applied || cam == null) return;
        cam.transparencySortMode = prevMode;
        cam.transparencySortAxis = prevAxis;
        applied = false;
    }
}