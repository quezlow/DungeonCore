// SceneTransitionTrigger.cs
// Attach to any doorway, staircase, or zone boundary that loads a new scene.
// Requires a Collider2D set to Is Trigger on the same GameObject.
//
// Inspector fields:
//   Target Scene    — exact name of the Unity scene to load (must be in Build Settings)
//   Spawn Point ID  — SpawnPoint ID in the TARGET scene where the player lands
//
// Naming convention example:
//   Door in TutorialTown → Interiors:
//     Target Scene   = "Interiors"
//     Spawn Point ID = "Inn_Entry"
//
//   Return door inside the inn (Interiors scene) → TutorialTown:
//     Target Scene   = "TutorialTown"
//     Spawn Point ID = "Inn_Exit"

using UnityEngine;

public class SceneTransitionTrigger : MonoBehaviour
{
    [SerializeField] private string targetScene;
    [SerializeField] private string spawnPointID;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogWarning($"SceneTransitionTrigger on '{gameObject.name}': Target Scene is empty.");
            return;
        }

        if (SceneLoader.Instance == null)
        {
            Debug.LogError($"SceneTransitionTrigger on '{gameObject.name}': SceneLoader not found. " +
                           "Make sure a SceneLoader GameObject exists in this scene.");
            return;
        }

        SceneLoader.Instance.TransitionToScene(targetScene, spawnPointID);
    }

    // Draws a cyan outline and destination label in the Scene view
    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f,
            $"→ {targetScene}\n  [{spawnPointID}]");
#endif
    }
}
