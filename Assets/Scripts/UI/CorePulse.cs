using System.Collections;
using UnityEngine;

/// <summary>
/// Draws brief colour "pulse" beams - a line from one world point to another that fades and
/// vanishes. Used by the Throne Room's core retaliation (a pulse from the core to each attacker
/// it zaps, in the core's type colour). Self-contained: add this component to a persistent
/// object and it builds its own beams on demand - no wiring, no prefab.
/// </summary>
public class CorePulse : MonoBehaviour
{
    public static CorePulse Instance { get; private set; }

    [SerializeField] private float duration = 0.3f;
    [SerializeField] private float width = 0.14f;
    [SerializeField] private string sortingLayer = "WorldUI";
    [SerializeField] private int sortingOrder = 50;

    private Material beamMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) beamMaterial = new Material(shader);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Fire a fading beam from 'from' to 'to' in the given colour.</summary>
    public void Fire(Vector3 from, Vector3 to, Color colour)
    {
        var go = new GameObject("CorePulseBeam");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        if (beamMaterial != null) lr.material = beamMaterial;
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startWidth = lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.textureMode = LineTextureMode.Stretch;
        if (!string.IsNullOrEmpty(sortingLayer)) lr.sortingLayerName = sortingLayer;
        lr.sortingOrder = sortingOrder;
        lr.startColor = lr.endColor = colour;

        StartCoroutine(FadeAndDie(go, lr, colour));
    }

    private IEnumerator FadeAndDie(GameObject go, LineRenderer lr, Color colour)
    {
        float t = 0f;
        float d = Mathf.Max(0.05f, duration);
        while (t < d)
        {
            t += Time.deltaTime;
            if (lr != null)
            {
                float a = Mathf.Lerp(1f, 0f, t / d);
                var c = new Color(colour.r, colour.g, colour.b, a);
                lr.startColor = lr.endColor = c;
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }
}