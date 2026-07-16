using UnityEngine;

/// <summary>
/// TEST ONLY. Loads the Forest scene and drops the Player-tagged character at the
/// dungeon-side arrival spawn. Stands in for the avatar walking the cave-entrance
/// tunnel until the avatar exists. Wire to a HUD Button OnClick; remove or gate
/// behind a debug flag before release.
/// </summary>
public class DevForestTravelButton : MonoBehaviour
{
    [SerializeField] private string forestSpawnPointId = "FromDungeonEntrance";

    public void TravelToForest()
    {
        if (SceneLoader.Instance == null)
        {
            Debug.LogError("[DevForestTravelButton] No SceneLoader in scene.");
            return;
        }
        SceneLoader.Instance.TransitionToScene(
            SceneNames.GameScene.Forest.ToString(), forestSpawnPointId);
    }
}