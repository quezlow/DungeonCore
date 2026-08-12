using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// A short spoken line that appears above an adventurer's head, drifts up a little, holds so it
/// can be read, then fades. Modelled on FloatingDamageNumber.
///
/// PREFAB SETUP:
///   FloatingBarkText (this script, Canvas - World Space, scale ~0.01)
///   +-- Text (TextMeshProUGUI - centre aligned, no raycast target; optional backing panel)
/// </summary>
[RequireComponent(typeof(Canvas))]
public class FloatingBarkText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private float riseHeight = 0.4f;
    [SerializeField] private float lifetime = 2.6f;
    [SerializeField] private float fadeStartFraction = 0.7f;

    private void Awake()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingLayerName = "WorldUI";
        canvas.sortingOrder = 22;   // just above the damage numbers
        if (TryGetComponent<UnityEngine.UI.GraphicRaycaster>(out var gr)) Destroy(gr);

        // Same fog rule as the damage numbers: WorldUI is above the fog, so a
        // bark from something the player cannot see would be a voice from an
        // empty corridor.
        if (!FloorRoot.IsRevealedWorld(transform.position)) { Destroy(gameObject); return; }
    }

    public void Initialise(string text, Color colour)
    {
        if (label == null) { Debug.LogError("FloatingBarkText: label is not assigned."); return; }
        label.text = text;
        label.color = colour;
        StartCoroutine(Rise(colour));
    }

    private IEnumerator Rise(Color baseColour)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + new Vector3(0f, riseHeight, 0f);
        float elapsed = 0f;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);
            transform.position = Vector3.Lerp(startPos, endPos, Mathf.SmoothStep(0f, 1f, t));
            float fadeT = Mathf.InverseLerp(fadeStartFraction, 1f, t);
            label.color = new Color(baseColour.r, baseColour.g, baseColour.b, 1f - fadeT);
            yield return null;
        }
        Destroy(gameObject);
    }
}