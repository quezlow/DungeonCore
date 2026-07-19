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
using static SceneNames;

// Runs after SpawnPointManager (510) so the arming check in Start sees the
// player at his PLACED position, not his pre-transition one.
[DefaultExecutionOrder(530)]
public class SceneTransitionTrigger : MonoBehaviour
{
    [SerializeField] private GameScene targetScene;
    [SerializeField] private string spawnPointID;

    /// <summary>
    /// Runtime initialiser for generated triggers. Procgen features (the
    /// city gate at the road's seeded end) sit at seed-dependent positions,
    /// so their triggers cannot be hand-placed in the scene.
    /// </summary>
    public void Configure(GameScene scene, string spawnId)
    {
        targetScene = scene;
        spawnPointID = spawnId;
    }

    // A spawn point can place the player already overlapping this trigger
    // (the interaction circle alone spans 1.5 units). That deferred overlap
    // must never fire a bounce-back transition: the trigger arms only once
    // the player has been observed OUTSIDE it after the scene settles.
    private bool armed = true;

    private void Start()
    {
        Physics2D.SyncTransforms();
        var player = GameObject.FindGameObjectWithTag("Player");
        var mine = GetComponent<Collider2D>();
        var pcol = player != null ? player.GetComponent<Collider2D>() : null;
        if (mine != null && pcol != null && mine.bounds.Intersects(pcol.bounds))
            armed = false;
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player")) armed = true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        if (collision.isTrigger) return;   // only the solid body opens doors -- the interaction circle reaches 1.5 units early
        if (!armed) return;
        if (SceneLoader.IsHandlingTransition) return;

        if (SceneLoader.Instance == null)
        {
            Debug.LogError($"SceneTransitionTrigger on '{gameObject.name}': SceneLoader not found. " +
                           "Make sure a SceneLoader GameObject exists in this scene.");
            return;
        }

        SceneLoader.Instance.TransitionToScene(targetScene.ToString(), spawnPointID);
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
