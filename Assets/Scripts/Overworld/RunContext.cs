// Assets/Scripts/Save/RunContext.cs
/// <summary>
/// Cross-scene run context. Carries values that overworld scenes need from the
/// dungeon simulation without a persistent MonoBehaviour. Set by
/// DungeonSaveController when the world seed becomes known (new game and load);
/// read by surface generators that rebuild deterministically from it.
/// </summary>
public static class RunContext
{
    public static bool HasWorldSeed { get; private set; }
    public static int WorldSeed { get; private set; }

    public static void SetWorldSeed(int seed)
    {
        WorldSeed = seed;
        HasWorldSeed = true;
    }

    // --- Surface scouting (set when leaving to scout, read on return) ---
    public static bool ScoutMode;          // true while a scout trip is in flight
    public static float ScoutManaBudget;    // mana available at scout start
    public static float ScoutSpend;         // mana spent this session (applied on return)
    public static string ScoutReturnScene;   // gameplay scene to return to

    public static void BeginScout(float manaBudget, string returnScene)
    {
        ScoutMode = true;
        ScoutManaBudget = manaBudget;
        ScoutSpend = 0f;
        ScoutReturnScene = returnScene;
    }

    public static void EndScout() { ScoutMode = false; }
}