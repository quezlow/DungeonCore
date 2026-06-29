using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Listens to RoomAnchor.OnRoomValidationChanged and plays two simultaneous effects:
///   1. Toast — a brief fading text notification above the room anchor.
///   2. Tile tint — all tiles in the validated room briefly flash the room's colour.
///
/// SETUP
///   - Attach to any persistent manager in the scene.
///   - toastPrefab: a prefab with a TMP_Text (CanvasGroup for fade). Use a World Space Canvas.
///   - highlightTilemap: an empty Tilemap on its own sorting layer above the floor (z=1).
///     Used exclusively by this script for the tint flash. Leave it empty at edit time.
///   - highlightTile: a plain white/transparent TileBase. The tint colour is applied
///     per-frame via Tilemap.color, so any opaque tile asset works.
/// </summary>
public class RoomToastNotifier : MonoBehaviour
{
    [Header("Toast")]
    [SerializeField] private GameObject toastPrefab;       // TMP_Text + CanvasGroup
    [SerializeField] private float      toastDuration = 2f;
    [SerializeField] private float      toastRiseSpeed = 0.5f;

    [Header("Tile Tint")]
    [SerializeField] private Tilemap    highlightTilemap;
    [SerializeField] private TileBase   highlightTile;
    [SerializeField] private float tintDuration = 1.2f;

    [Header("Failure")]
    [SerializeField] private Color failColor = new(0.91f, 0.27f, 0.38f, 1f);

    // ── Lifecycle ─────────────────────────────────────────────────

    private void OnEnable()  => RoomAnchor.OnRoomValidationChanged += HandleValidation;
    private void OnDisable() => RoomAnchor.OnRoomValidationChanged -= HandleValidation;

    // ── Handler ───────────────────────────────────────────────────

    private void HandleValidation(RoomAnchor anchor, bool isValid)
    {
        if (DungeonSaveController.IsLoading) return;
        if (!isValid)
        {
            // Failure feedback — a brief red toast naming what's missing.
            if (toastPrefab != null && anchor.AssignedRoom != null)
            {
                string reason = string.IsNullOrEmpty(anchor.LastFailReason)
                    ? $"{anchor.AssignedRoom.roomName} incomplete"
                    : anchor.LastFailReason;
                StartCoroutine(ShowToast(anchor.transform.position, reason, ColorblindPalette.Invalid(failColor)));
            }
            return;
        }

        string message = $"{anchor.AssignedRoom.roomName} Complete!";
        Color color = ColorblindPalette.Valid(anchor.AssignedRoom.validationTintColor);

        if (toastPrefab != null)
            StartCoroutine(ShowToast(anchor.transform.position, message, color));

        var tiles = anchor.GetRoomTiles();
        var tm = FloorManager.Instance?.ActiveFloor?.HighlightTilemap ?? highlightTilemap;
        if (tiles != null && tm != null && highlightTile != null)
            StartCoroutine(FlashTiles(tiles, color, tm));
    }

    // ── Toast ─────────────────────────────────────────────────────

    private IEnumerator ShowToast(Vector3 worldPos, string message, Color textColor)
    {
        var go = Instantiate(toastPrefab, worldPos + Vector3.up * 0.5f, Quaternion.identity);
        var label = go.GetComponentInChildren<TMP_Text>();
        var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();

        if (label != null) { label.text = message; label.color = textColor; }

        float elapsed = 0f;
        while (elapsed < toastDuration)
        {
            elapsed += Time.deltaTime;
            float t  = elapsed / toastDuration;

            cg.alpha = t < 0.2f
                ? Mathf.Lerp(0f, 1f, t / 0.2f)           // fade in
                : Mathf.Lerp(1f, 0f, (t - 0.2f) / 0.8f); // fade out

            go.transform.position += Vector3.up * toastRiseSpeed * Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    // ── Tile Tint Flash ───────────────────────────────────────────

    private IEnumerator FlashTiles(HashSet<Vector3Int> tiles, Color tintColor, Tilemap tm)
    {
        if (tm == null) yield break;

        // Paint tiles.
        foreach (var cell in tiles)
            tm.SetTile(cell, highlightTile);

        // Fade tint in then out.
        float elapsed = 0f;
        while (elapsed < tintDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / tintDuration;
            float a = t < 0.3f
                ? Mathf.Lerp(0f, tintColor.a, t / 0.3f)
                : Mathf.Lerp(tintColor.a, 0f, (t - 0.3f) / 0.7f);

            tm.color = new Color(tintColor.r, tintColor.g, tintColor.b, a);
            yield return null;
        }

        // Clear tiles.
        foreach (var cell in tiles)
            tm.SetTile(cell, null);

        tm.color = Color.white;
    }
}
