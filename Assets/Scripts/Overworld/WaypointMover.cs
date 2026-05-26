using System.Collections;
using UnityEngine;

public class WaypointMover : MonoBehaviour
{
    public Transform waypointParent;
    public float moveSpeed = 2f;
    public float waitTime = 2f;
    public bool loopWaypoints = true;

    private Transform[] waypoints;
    private int currentWaypointIndex;
    private bool isWaiting;
    private Animator animator;

    private float lastInputX;
    private float lastInputY;

    void Start()
    {
        // Null-safe — works with or without an Animator attached
        animator = GetComponent<Animator>();
        waypoints = new Transform[waypointParent.childCount];
        for (int i = 0; i < waypointParent.childCount; i++)
        {
            waypoints[i] = waypointParent.GetChild(i);
        }
    }

    void Update()
    {
        if (PauseController.IsGamePaused || isWaiting)
        {
            SetBool("isWalking", false);
            SetFloat("LastInputX", lastInputX);
            SetFloat("LastInputY", lastInputY);
            return;
        }

        MoveToWaypoint();
    }

    void MoveToWaypoint()
    {
        Transform target = waypoints[currentWaypointIndex];
        Vector2 direction = (target.position - transform.position).normalized;

        if (direction.magnitude > 0f)
        {
            lastInputX = direction.x;
            lastInputY = direction.y;
        }

        transform.position = Vector2.MoveTowards(
            transform.position, target.position, moveSpeed * Time.deltaTime);

        SetFloat("InputX", direction.x);
        SetFloat("InputY", direction.y);
        SetBool("isWalking", direction.magnitude > 0f);

        if (Vector2.Distance(transform.position, target.position) < 0.1f)
        {
            StartCoroutine(waitAtWaypoint());
        }
    }

    IEnumerator waitAtWaypoint()
    {
        isWaiting = true;
        SetBool("isWalking", false);
        SetFloat("LastInputX", lastInputX);
        SetFloat("LastInputY", lastInputY);

        yield return new WaitForSeconds(waitTime);

        currentWaypointIndex = loopWaypoints
            ? (currentWaypointIndex + 1) % waypoints.Length
            : Mathf.Min(currentWaypointIndex + 1, waypoints.Length - 1);

        isWaiting = false;
    }

    // ── Null-safe animator helpers ────────────────────────────────
    private void SetBool(string param, bool value)
    {
        if (animator != null) animator.SetBool(param, value);
    }

    private void SetFloat(string param, float value)
    {
        if (animator != null) animator.SetFloat(param, value);
    }
}