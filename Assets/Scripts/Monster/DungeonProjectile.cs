using UnityEngine;

/// <summary>
/// A travel-time projectile loosed at the end of a ranged attacker's telegraph. It
/// carries the full attack payload (damage, impact number, knockback, formation
/// break, grudge record, and side-specific credit callbacks), so a shooter that
/// falls mid-flight still lands the shot it loosed. Flight is a straight line aimed
/// at the target's position at fire time -- a target that moves away dodges, and the
/// bolt flies on to fizzle at the first solid cell or just past its aim point.
///
/// Purely transient: never serialized. A mid-flight save simply drops the bolt, the
/// same ruling as a mid-windup telegraph. No pooling -- fire rates are one bolt per
/// attacker per cooldown, the DamageNumberSpawner instantiation precedent.
///
/// The built-in soft-glow sprite is generated at runtime (the selection-ring
/// pattern) and tinted per definition; a definition may override it with a bespoke
/// sprite (projectileSprite) once one exists.
/// </summary>
public class DungeonProjectile : MonoBehaviour
{
    /// <summary>Everything the shot applies on impact, captured at fire time.</summary>
    public struct Payload
    {
        public float damage;
        /// <summary>True when the shooter is an adventurer or a wild creature.
        /// Rides the payload because the bolt outlives the shot: by impact the
        /// shooter may be dead, and asking it then would credit nobody.</summary>
        public bool fromOutsider;
        public FloatingDamageNumber.DamageType numberType;
        /// <summary>Attacker type name for the grudge record; empty = no record
        /// (adventurer-fired bolts -- grudges are an adventurer-side ledger).</summary>
        public string sourceName;
        public float knockbackForce;
        public float knockbackMinDamage;
        public bool breaksFormation;
        public float breakSeconds;
        /// <summary>Invoked after damage lands, hit or kill alike (e.g. taunt peel).</summary>
        public System.Action<IMonsterTarget> onHit;
        /// <summary>Invoked only when the impact killed the target (credit, XP, titles).</summary>
        public System.Action<IMonsterTarget> onKill;
    }

    private const float HitRadius = 0.4f;      // generous, so ambient jitter does not whiff
    private const float OvershootSlack = 1.5f; // how far past the aim point a miss flies

    private FloorRoot floor;
    private IMonsterTarget target;
    private Object targetRef;                  // Unity-null check for a destroyed target
    private Payload payload;
    private Vector2 dir;
    private float speed;
    private float travelled;
    private float maxTravel;

    private static Sprite builtInBolt;

    /// <summary>Loose a bolt from origin at the target. Aim is fixed at fire time.</summary>
    public static void Fire(FloorRoot floor, Vector3 origin, IMonsterTarget target,
        float speed, Color tint, Sprite spriteOverride, Payload payload)
    {
        if (target == null || !target.IsAlive) return;

        Vector3 aim = target.Transform.position;
        Vector2 dir = ((Vector2)(aim - origin));
        float aimDist = dir.magnitude;
        dir = aimDist > 0.001f ? dir / aimDist : Vector2.right;

        var go = new GameObject("Projectile");
        if (floor != null) go.transform.SetParent(floor.transform, true);
        go.transform.position = origin + (Vector3)(dir * 0.35f);
        go.transform.rotation = Quaternion.Euler(0f, 0f,
            Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = spriteOverride != null ? spriteOverride : BuiltInBolt();
        sr.color = tint;
        sr.sortingLayerName = "Player";   // a world entity, per the sorting layer contract

        var p = go.AddComponent<DungeonProjectile>();
        p.floor = floor;
        p.target = target;
        p.targetRef = target as Object;
        p.payload = payload;
        p.dir = dir;
        p.speed = Mathf.Max(0.1f, speed);
        p.maxTravel = aimDist + OvershootSlack;
    }

    /// <summary>True when nothing solid stands between two points on a floor. Samples
    /// DungeonPathfinder.IsWalkable along the line, so walls and overhangs block a
    /// shot, rivers do not, and bodies never do -- the shield wall's mitigation IS the
    /// front-rank-blocks-for-the-rear fiction, so bolts pass through bodies.</summary>
    public static bool HasLineOfSight(FloorRoot floor, Vector3 from, Vector3 to)
    {
        if (floor == null) return true;
        float dist = Vector2.Distance(from, to);
        if (dist < 0.001f) return true;
        Vector2 dir = ((Vector2)(to - from)).normalized;
        const float step = 0.45f;
        for (float d = step; d < dist; d += step)
            if (!DungeonPathfinder.IsWalkable(floor, from + (Vector3)(dir * d)))
                return false;
        return true;
    }

    private void Update()
    {
        float stride = speed * Time.deltaTime;
        transform.position += (Vector3)(dir * stride);
        travelled += stride;

        // A live target within the hit radius is struck -- even slightly off the
        // original aim line, so a generous radius forgives ambient jitter.
        if (targetRef != null && target.IsAlive
            && Vector2.Distance(transform.position, target.Transform.position) <= HitRadius)
        {
            Impact(target);
            return;
        }

        // Missed (or the target died): fly on and fizzle at solid rock or past the aim.
        if (travelled >= maxTravel
            || !DungeonPathfinder.IsWalkable(floor, transform.position))
            Destroy(gameObject);
    }

    /// <summary>Land the payload. Mirrors the melee DealAttackDamage bookkeeping and
    /// ordering: number, grudge record, typed damage, formation break, knockback,
    /// then the side-specific callbacks.</summary>
    private void Impact(IMonsterTarget hit)
    {
        var hitObj = hit as Object;
        DamageNumberSpawner.Spawn(payload.damage, hit.Transform.position, payload.numberType);

        if (hit is DungeonAdventurer adv)
        {
            if (!string.IsNullOrEmpty(payload.sourceName))
                adv.RecordDamagedBy(payload.sourceName, payload.damage);
            adv.TakeDamage(payload.damage, DamageKind.Ranged, payload.fromOutsider);
            if (payload.breaksFormation && hitObj != null)
                adv.BreakFormation(payload.breakSeconds);
        }
        else
        {
            hit.TakeDamage(payload.damage, payload.fromOutsider);
        }

        if (payload.knockbackForce > 0f && payload.damage >= payload.knockbackMinDamage
            && hitObj != null)
            hit.ApplyKnockback(transform.position, payload.knockbackForce);

        payload.onHit?.Invoke(hit);
        if (!hit.IsAlive) payload.onKill?.Invoke(hit);
        Destroy(gameObject);
    }

    private static Sprite BuiltInBolt()
    {
        if (builtInBolt != null) return builtInBolt;
        const int size = 24;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float c = (size - 1) * 0.5f, radius = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(1f - d / radius);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));   // soft radial falloff
            }
        tex.Apply();
        builtInBolt = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), 48f);
        return builtInBolt;
    }
}
