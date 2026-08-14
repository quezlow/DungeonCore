using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A dwarf who walks: the shared puppet under villagers, patrols and caravan
/// members (canon 19, The Living Holds). One SpriteRenderer, a world-point
/// path, flipX by direction and a small sine bob -- the merchant's puppet
/// pattern grown one mechanic, because three separate copies of "move a sprite
/// along points" is how the three drift apart.
///
/// NO LONGER "DELIBERATELY NOT AN ENTITY" -- and the sentence that stood here
/// saying so is worth remembering rather than quietly deleting. Fork 5 of the
/// Living Holds ruled that dwarves were not combat entities and that a rob was
/// a verb rather than a fight. Canon 44 REVERSES that, because floor index 3's
/// siege needs villagers who can die for the village to fall, and a hold that
/// cannot be lost is scenery.
///
/// SO THIS IS A MOVEMENT OVERRIDE NOW, not a body. A dwarf is a DungeonMonster
/// with MonsterAllegiance.Faction; the puppet drives it while it is walking and
/// SUSPENDS while combat holds it. The contract every owner keeps:
///
///   - freeze the walker while DungeonMonster.CombatHoldsBody is true;
///   - on release, SnapTo the body's actual position and re-path from the cell
///     it now stands on. A path resumed at its old DistanceAlong would teleport
///     the body back to where the fight began, because the fight moved the
///     transform and this class never saw it happen.
///
/// The alternative was splitting the puppet so only patrols became mortal,
/// which was proposed and REJECTED: villagers and caravan members ride the same
/// class, and an invulnerable caravan is what lets free murder route around the
/// toll's one priced choice. Everything is mortal; scarcity prices the murder.
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
    /// the toll vignette, an open action panel. A frozen walker still PINS
    /// its transform each frame -- that is the point of it.</summary>
    public bool Frozen;

    /// <summary>Combat has the body; this class must not touch the transform
    /// at all until it is handed back.
    ///
    /// DISTINCT FROM Frozen, AND THE DIFFERENCE IS THE WHOLE OVERRIDE. Frozen
    /// still writes `transform.position = LogicalPosition` every frame, which
    /// is correct for a night camp and catastrophic for a fight: a guard
    /// chasing a kobold would be dragged back to his last walked cell on
    /// every frame, standing on the spot swinging at nothing. Suspended
    /// writes NOTHING. The owner sets it from DungeonMonster.CombatHoldsBody
    /// and calls SnapTo when it clears.</summary>
    public bool Suspended;

    /// <summary>Bob amplitude in world units; 0 disables. Sells walking with a
    /// single static sprite until animated dwarves exist.</summary>
    public float BobAmplitude = 0.045f;

    public bool HasPath => points.Count > 1;
    public float PathLength { get; private set; }
    public float DistanceAlong => distance;
    public bool Arrived => !HasPath || distance >= PathLength - 0.001f;

    /// <summary>Logical position on the path (no bob).</summary>
    public Vector3 LogicalPosition { get; private set; }

    /// <summary>Attach the override to a body that ALREADY EXISTS -- a
    /// DungeonMonster prefab instance -- rather than building a bare sprite.
    ///
    /// Create() below makes its own GameObject and its own SpriteRenderer,
    /// which is right for a villager or a caravan walker that is nothing but a
    /// sprite. A mortal body brings its own renderer, animator, collider and
    /// status bars, and adding a second SpriteRenderer to it would draw the
    /// dwarf twice. This finds the one already there.</summary>
    public static DwarfWalkerPuppet AttachTo(GameObject host)
    {
        var puppet = host.AddComponent<DwarfWalkerPuppet>();
        puppet.sr = host.GetComponentInChildren<SpriteRenderer>();
        puppet.LogicalPosition = host.transform.position;
        return puppet;
    }

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
        if (Frozen || Suspended || !HasPath) return;
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
        // Suspended writes nothing at all -- see the field for why this is not
        // the same as Frozen.
        if (Suspended) return;

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

    /// <summary>Adopt a world position outright, discarding any path.
    ///
    /// THE RESYNC HALF OF THE COMBAT OVERRIDE. Movement here is
    /// distance-along-path and LogicalPosition is derived from it, so this class
    /// has no way to learn that something else moved the transform -- and combat
    /// does exactly that, every frame it holds the body. Without this an owner
    /// resuming after a fight would drag the body back along a stale path from
    /// wherever it started. ClearPath rather than keepDistance: the old route
    /// began somewhere this body no longer stands.</summary>
    public void SnapTo(Vector3 worldPos)
    {
        ClearPath();
        LogicalPosition = worldPos;
        transform.position = worldPos;
        bobPhase = 0f;
    }

    /// <summary>Face a world position without moving (patrols watching).</summary>
    public void Face(Vector3 worldPos)
    {
        if (sr == null) return;
        float dx = worldPos.x - LogicalPosition.x;
        if (Mathf.Abs(dx) > 0.01f) sr.flipX = dx < 0f;
    }
}
