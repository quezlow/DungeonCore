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
    [SerializeField] private float      tintDuration = 1.2f;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void OnEnable()  => RoomAnchor.OnRoomValidationChanged += HandleValidation;
    private void OnDisable() => RoomAnchor.OnRoomValidationChanged -= HandleValidation;

    // ── Handler ───────────────────────────────────────────────────

    private void HandleValidation(RoomAnchor anchor, bool isValid)
    {
        if (!isValid) return; // only celebrate successes

        string message = $"{anchor.AssignedRoom.roomName} Complete!";
        Color  color   = anchor.AssignedRoom.validationTintColor;

        if (toastPrefab != null)
            StartCoroutine(ShowToast(anchor.transform.position, message));

        var tiles = anchor.GetRoomTiles();
        if (tiles != null && highlightTilemap != null && highlightTile != null)
            StartCoroutine(FlashTiles(tiles, color));
    }

    // ── Toast ─────────────────────────────────────────────────────

    private IEnumerator ShowToast(Vector3 worldPos, string message)
    {
        var go    = Instantiate(toastPrefab, worldPos + Vector3.up * 0.5f, Quaternion.identity);
        var label = go.GetComponentInChildren<TMP_Text>();
        var cg    = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();

        if (label != null) label.text = message;

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

    private IEnumerator FlashTiles(HashSet<Vector3Int> tiles, Color tintColor)
    {
        // Paint tiles.
        foreach (var cell in tiles)
            highlightTilemap.SetTile(cell, highlightTile);

        // Fade tint in then out.
        float elapsed = 0f;
        while (elapsed < tintDuration)
        {
            elapsed += Time.deltaTime;
            float t  = elapsed / tintDuration;
            float a  = t < 0.3f
                ? Mathf.Lerp(0f, tintColor.a, t / 0.3f)
                : Mathf.Lerp(tintColor.a, 0f, (t - 0.3f) / 0.7f);

            highlightTilemap.color = new Color(tintColor.r, tintColor.g, tintColor.b, a);
            yield return null;
        }

        // Clear tiles.
        foreach (var cell in tiles)
            highlightTilemap.SetTile(cell, null);

        highlightTilemap.color = Color.white;
    }
}
