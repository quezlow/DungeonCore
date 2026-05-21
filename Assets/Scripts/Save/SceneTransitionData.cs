// SceneTransitionData.cs
// Static "sticky note" passed between scenes during a transition.
// No MonoBehaviour, no scene setup required.
// Cleared by SpawnPointManager after the player is placed.

public static class SceneTransitionData
{
    public static string TargetSpawnPointID { get; set; } = string.Empty;

    public static bool IsSceneTransition => !string.IsNullOrEmpty(TargetSpawnPointID);

    public static void Clear()
    {
        TargetSpawnPointID = string.Empty;
    }
}
