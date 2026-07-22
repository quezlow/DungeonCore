using System.Collections.Generic;
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

    // Which parameters this controller actually declares. Writing one it does
    // not have makes Unity log a warning PER CALL -- five writes per entity per
    // frame, each capturing a stack trace when Call Stacks is on, which is what
    // buried LateUpdate once the dungeon filled with entities.
    private readonly HashSet<int> declared = new HashSet<int>();

    private void Awake()
    {
        animator = GetComponent<Animator>();
        lastPos = transform.position;
        CacheParameters();
    }

    private void OnEnable() => CacheParameters();

    private void CacheParameters()
    {
        declared.Clear();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var param in animator.parameters) declared.Add(param.nameHash);
    }

    private bool Has(int hash) => declared.Contains(hash);

    private void LateUpdate()
    {
        // Ready, not just non-null: writing a parameter to an Animator with no
        // controller (or one missing these parameters) makes Unity log a warning
        // PER CALL. Five writes per entity per frame, each capturing a stack
        // trace, is what dragged LateUpdate to tens of milliseconds once the
        // dungeon filled up. The trigger helpers below already guard this way.
        if (!Ready) return;

        Vector2 delta = (Vector2)(transform.position - lastPos);
        lastPos = transform.position;

        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        bool walking = speed > moveThreshold;
        if (Has(IsWalkingHash)) animator.SetBool(IsWalkingHash, walking);

        if (walking)
        {
            Vector2 dir = delta.normalized;
            lastFacing = dir;
            if (Has(InputXHash)) animator.SetFloat(InputXHash, dir.x);
            if (Has(InputYHash)) animator.SetFloat(InputYHash, dir.y);
        }

        if (Has(LastInputXHash)) animator.SetFloat(LastInputXHash, lastFacing.x);
        if (Has(LastInputYHash)) animator.SetFloat(LastInputYHash, lastFacing.y);
    }

    // Some civilian prefabs carry an Animator with no controller; a trigger
    // on those logs a warning per death. Ready checks both.
    private bool Ready => animator != null && animator.runtimeAnimatorController != null;

    public void OnAttack() { if (Ready && Has(AttackHash)) animator.SetTrigger(AttackHash); }
    public void OnHurt()   { if (Ready && Has(HurtHash))   animator.SetTrigger(HurtHash); }
    public void OnDeath()  { if (Ready && Has(DieHash))    animator.SetTrigger(DieHash); }
}