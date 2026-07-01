using UnityEngine;

/// <summary>
/// Drives an entity's Animator from its transform movement — walk state + 4-directional
/// facing, mirroring the avatar's parameter convention — and exposes trigger hooks the
/// owning entity calls for attack / hurt / death. Add to any animated entity (adventurer,
/// monster) alongside an Animator whose controller uses these parameters:
///
///   isWalking  (bool)   — moving vs idle
///   InputX/InputY       (float) — current move direction (for walk blend trees)
///   LastInputX/LastInputY (float) — last facing (for idle blend trees)
///   Attack / Hurt / Die (trigger)
///
/// Every call is null-safe against a missing Animator or an unassigned controller, so this
/// component is inert until the sprite sheets + Animator Controller are authored.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EntityAnimationDriver : MonoBehaviour
{
    [Tooltip("Movement (units/sec) below which the entity reads as idle.")]
    [SerializeField] private float moveThreshold = 0.05f;

    private Animator animator;
    private Vector3 lastPos;
    private Vector2 lastFacing = Vector2.down;

    private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
    private static readonly int InputXHash = Animator.StringToHash("InputX");
    private static readonly int InputYHash = Animator.StringToHash("InputY");
    private static readonly int LastInputXHash = Animator.StringToHash("LastInputX");
    private static readonly int LastInputYHash = Animator.StringToHash("LastInputY");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int HurtHash = Animator.StringToHash("Hurt");
    private static readonly int DieHash = Animator.StringToHash("Die");

    private void Awake()
    {
        animator = GetComponent<Animator>();
        lastPos = transform.position;
    }

    private void LateUpdate()
    {
        if (animator == null) return;

        Vector2 delta = (Vector2)(transform.position - lastPos);
        lastPos = transform.position;

        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        bool walking = speed > moveThreshold;
        animator.SetBool(IsWalkingHash, walking);

        if (walking)
        {
            Vector2 dir = delta.normalized;
            lastFacing = dir;
            animator.SetFloat(InputXHash, dir.x);
            animator.SetFloat(InputYHash, dir.y);
        }

        animator.SetFloat(LastInputXHash, lastFacing.x);
        animator.SetFloat(LastInputYHash, lastFacing.y);
    }

    public void OnAttack() { if (animator != null) animator.SetTrigger(AttackHash); }
    public void OnHurt() { if (animator != null) animator.SetTrigger(HurtHash); }
    public void OnDeath() { if (animator != null) animator.SetTrigger(DieHash); }
}