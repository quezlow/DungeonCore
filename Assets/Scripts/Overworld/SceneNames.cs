using UnityEngine;

public class SceneNames : MonoBehaviour
{
    public enum GameScene
    {
        CaveEntrance,
        City,
        Dungeon_Level_0,
        Dungeon_Level_1,
        Dungeon_Level_2,
        Dungeon_Level_3,
        // Orphaned: the Forest scene is retired (the surface is radial,
        // inside Dungeon_Level_0). Do NOT remove this entry: the enum
        // int-serialises in scene triggers, so deleting a middle value
        // re-targets every hand-placed door after it.
        Forest,
        Interiors,
        Plains,
        TutorialForest,
        TutorialTown
    }
}
