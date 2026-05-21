// SpawnPoint.cs
// Place at every entry/exit point in a scene.
// SpawnPointManager finds all of these on load and picks the right one.
//
// Spawn Point ID must exactly match the spawnPointID set on the
// SceneTransitionTrigger in the scene you are arriving FROM.
//
// Mark one SpawnPoint per scene as Is Default.
// That one is used as a fallback for fresh game loads and missing ID matches.
//
// Scene view colours:
//   Green sphere  = default spawn point
//   Yellow sphere = named spawn point

using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [SerializeField] private string spawnPointID;
    [SerializeField] private bool isDefault = false;

    public string SpawnPointID => spawnPointID;
    public bool IsDefault => isDefault;

    public void PlacePlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        if (player == null)
        {
            Debug.LogWarning($"SpawnPoint [{spawnPointID}]: No GameObject tagged 'Player' found.");
            return;
        }

        // Move player to this spawn position
        player.transform.position = transform.position;

        // Zero velocity — without this the player slides after spawning
        // because the Rigidbody2D still carries velocity from walking into the trigger
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.linearVelocity = Vector2.zero;
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        Gizmos.color = isDefault ? Color.green : Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.2f);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f,
            isDefault ? $"[DEFAULT]\n{spawnPointID}" : spawnPointID);
#endif
    }
}
