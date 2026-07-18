// SpawnPointManager.cs
// Place ONE of these in every scene.
// On Start, reads the sticky note (SceneTransitionData), finds the matching
// SpawnPoint, and places the player there.
//
// Execution order 510 — runs after SaveController (500) so it overrides
// any player position that SaveController may have restored from the save file.
// The spawn point always wins during a scene transition.

using UnityEngine;

[DefaultExecutionOrder(510)]
public class SpawnPointManager : MonoBehaviour
{
    private void Start()
    {
        //Debug.Log($"SpawnPointManager.Start() - IsSceneTransition: {SceneTransitionData.IsSceneTransition}");

        if (!SceneTransitionData.IsSceneTransition) return;

        SpawnPoint[] allSpawnPoints = FindObjectsByType<SpawnPoint>();
        //Debug.Log($"Found {allSpawnPoints.Length} SpawnPoints in scene");

        //foreach (var sp in allSpawnPoints)
        //{
        //    Debug.Log($"  - SpawnPoint: ID='{sp.SpawnPointID}', IsDefault={sp.IsDefault}");
        //}

        SpawnPoint targetPoint = null;
        SpawnPoint defaultPoint = null;

        foreach (SpawnPoint sp in allSpawnPoints)
        {
            if (sp.IsDefault) defaultPoint = sp;
            if (sp.SpawnPointID == SceneTransitionData.TargetSpawnPointID) targetPoint = sp;
        }

        //Debug.Log($"Target ID looking for: '{SceneTransitionData.TargetSpawnPointID}'");
        //Debug.Log($"Target found: {targetPoint != null}");
        //Debug.Log($"Default found: {defaultPoint != null}");


        // The doors were authored against the marker OBJECT names; several
        // SpawnPoint id fields have since drifted (copy-paste renames). Fall
        // back to a name match so a drifted id can never strand an arrival
        // on the scene default.
        if (targetPoint == null)
        {
            foreach (SpawnPoint sp in allSpawnPoints)
                if (sp.gameObject.name == SceneTransitionData.TargetSpawnPointID) { targetPoint = sp; break; }
        }

        SpawnPoint chosen = targetPoint ?? defaultPoint;

        if (chosen != null)
        {
            //Debug.Log($"Placing player at: {chosen.SpawnPointID}");
            chosen.PlacePlayer();
        }
        else
        {
            Debug.LogWarning("No spawn point available!");
        }

        SceneTransitionData.Clear();
    }
}