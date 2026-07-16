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
}