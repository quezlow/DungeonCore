using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A dwarf who walks: the shared puppet under villagers, patrols and caravan
/// members (canon 19, The Living Holds). One SpriteRenderer, a world-point
/// path, flipX by direction and a small sine bob -- the merchant's puppet
/// pattern grown one mechanic, because three separate copies of "move a sprite
/// along points" is how the three drift apart.
///
/// DELIBERATELY NOT AN ENTITY. It is never registered with FloorEntityRegistry,
/// so ScanForHostiles cannot see it, traps cannot fire on it, and nothing in
/// the combat layer knows it exists. Fork 5 of the arc: dwarves are not combat
/// entities; a rob is a verb, not a fight.
///
/// Movement is distance-along-path, not waypoint-index: owners that derive
/// position from a saved walking clock (the caravan) set DistanceAlong
/// directly, and owners that just walk (villagers, patrols) call Advance. The
/// bob is applied at render time on top of the logical position so distance
/// arithmetic never sees it.
///
/// Uses scaled Time.deltaTime throughout, so pause (timeScale 0) and the game
/// speed control hold every walker in lockstep with the day clock -- the same
/// clock the caravan's travel is authored against.
/// </summary>
public class DwarfWalkerPuppet : MonoBehaviour
{
    private SpriteRenderer sr;
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> cumulative = new List<float>();
    private float distance;
    private float bobPhase;

    /// <summary>World units per second along the path.</summary>
    public float Speed = 1.2f;

    /// <summary>Halted walkers hold position and stop bobbing: night camps,
    /// the toll vignette, an open action panel.</summary>
    public bool Frozen;

    /// <summary>Bob amplitude in world units; 0 disables. Sells walking with a
    /// single static sprite until animated dwarves exist.</summary>
    public float BobAmplitude = 0.045f;

    public bool HasPath => points.Count > 1;
    public float PathLength { get; private set; }
    public float DistanceAlong => distance;
    public bool Arrived => !HasPath || distance >= PathLength - 0.001f;

    /// <summary>Logical position on the path (no bob).</summary>
    public Vector3 LogicalPosition { get; private set; }

    public static DwarfWalkerPuppet Create(string name, Sprite sprite,
        string sortingLayerName, int sortingOrder, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.position = at;
        var puppet = go.AddComponent<DwarfWalkerPuppet>();
        puppet.sr = go.AddComponent<SpriteRenderer>();
        puppet.sr.sprite = sprite;
        puppet.sr.sortingLayerName = sortingLayerName;
        puppet.sr.sortingOrder = sortingOrder;
        puppet.LogicalPosition = at;
        return puppet;
    }

    public void SetSprite(Sprite sprite) { if (sr != null) sr.sprite = sprite; }

    /// <summary>Replace the path. The puppet snaps to its start unless
    /// keepDistance is set (the caravan re-deriving mid-leg on load).</summary>
    public void SetPath(List<Vector3> worldPoints, bool keepDistance = false)
    {
        points.Clear();
        cumulative.Clear();
        PathLength = 0f;
        if (worldPoints != null)
            foreach (var p in worldPoints)
            {
                if (points.Count > 0)
                    PathLength += Vector3.Distance(points[points.Count - 1], p);
                points.Add(p);
                cumulative.Add(PathLength);
            }
        if (!keepDistance) distance = 0f;
        distance = Mathf.Clamp(distance, 0f, PathLength);
        Apply();
    }

    public void ClearPath()
    {
        points.Clear(); cumulative.Clear();
        PathLength = 0f; distance = 0f;
    }

    /// <summary>Advance by seconds of walking at Speed. Owners with their own
    /// clock (the caravan) use SetDistance instead.</summary>
    public void Advance(float dt)
    {
        if (Frozen || !HasPath) return;
        SetDistance(distance + Speed * dt);
    }

    public void SetDistance(float d)
    {
        distance = Mathf.Clamp(d, 0f, PathLength);
        Apply();
    }

    private void Apply()
    {
        if (!HasPath)
        {
            transform.position = LogicalPosition;
            return;
        }
        // Binary search the cumulative table for the segment the distance
        // falls in, then lerp inside it.
        int lo = 0, hi = cumulative.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (cumulative[mid] < distance) lo = mid + 1; else hi = mid;
        }
        int i = Mathf.Max(1, lo);
        float segStart = cumulative[i - 1];
        float segLen = Mathf.Max(0.0001f, cumulative[i] - segStart);
        float t = Mathf.Clamp01((distance - segStart) / segLen);
        Vector3 pos = Vector3.Lerp(points[i - 1], points[i], t);

        if (sr != null)
        {
            float dx = points[i].x - points[i - 1].x;
            if (Mathf.Abs(dx) > 0.01f) sr.flipX = dx < 0f;
        }
        LogicalPosition = pos;
    }

    private void Update()
    {
        // Render-time bob only; LogicalPosition is the truth every owner reads.
        if (!Frozen && HasPath && !Arrived && BobAmplitude > 0f)
        {
            bobPhase += Time.deltaTime * 10f;
            transform.position = LogicalPosition
                + Vector3.up * (Mathf.Abs(Mathf.Sin(bobPhase)) * BobAmplitude);
        }
        else
        {
            transform.position = LogicalPosition;
        }
    }

    /// <summary>Face a world position without moving (patrols watching).</summary>
    public void Face(Vector3 worldPos)
    {
        if (sr == null) return;
        float dx = worldPos.x - LogicalPosition.x;
        if (Mathf.Abs(dx) > 0.01f) sr.flipX = dx < 0f;
    }
}
