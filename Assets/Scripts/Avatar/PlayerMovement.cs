using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private bool wasPaused;
    private Animator animator;
    private bool playingFootsteps = false;
    private float footstepSpeed = 0.5f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }
    void Update()
    {
        // Debug.Log("PauseController.IsGamePaused: " + PauseController.IsGamePaused);

        if (PauseController.IsGamePaused)
        {
            wasPaused = true;
            rb.linearVelocity = Vector2.zero;
            animator.SetBool("isWalking", false);
            // Zero the live input floats too: the walk states key on them, so
            // a held key would otherwise play the walk cycle in place through
            // every pause and departure fade (the pre-fade stutter).
            animator.SetFloat("InputX", 0f);
            animator.SetFloat("InputY", 0f);
            StopMovementAnimations();
            StopFootsteps();
            return;
        }

        // First live frame after a pause: a key held straight through a scene
        // load never re-fires Move, leaving the animator's floats at zero and
        // the sprite facing wrong. Push the held direction back in once.
        if (wasPaused)
        {
            wasPaused = false;
            if (moveInput.sqrMagnitude > 0.01f)
            {
                animator.SetFloat("InputX", moveInput.x);
                animator.SetFloat("InputY", moveInput.y);
            }
        }

        rb.linearVelocity = moveInput * moveSpeed;
        animator.SetBool("isWalking", rb.linearVelocity.magnitude > 0);

        if (rb.linearVelocity.magnitude > 0 && !playingFootsteps)
        {
            StartFootsteps();
        }
        else if (rb.linearVelocity.magnitude == 0)
        {
            StopFootsteps();
        }
    }

    public void Move(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            StopMovementAnimations();
        }

        moveInput = context.ReadValue<Vector2>();

        // While paused (dialogue, menus, departure fades) Update keeps the
        // floats zeroed; writing held input here would restart the walk
        // cycle in place mid-fade.
        if (PauseController.IsGamePaused) return;
        wasPaused = false;

        animator.SetFloat("InputX", moveInput.x);
        animator.SetFloat("InputY", moveInput.y);
    }

    void StopMovementAnimations()
    {
        animator.SetBool("isWalking", false);
        animator.SetFloat("LastInputX", moveInput.x);
        animator.SetFloat("LastInputY", moveInput.y);
    }

    void StartFootsteps()
    {
        playingFootsteps = true;
        InvokeRepeating(nameof(PlayFootstep), 0f, footstepSpeed);
    }

    void StopFootsteps()
    {
        playingFootsteps = false;
        CancelInvoke(nameof(PlayFootstep));
    }

    void PlayFootstep()
    {
        SoundEffectManager.Play("Footstep", true);
    }
}
