/// <summary>
/// Canonical flag strings recorded during the living prologue.
///
/// Inspector fields on FlagInteractable are typed by hand and must match
/// these exactly; code paths (quest hand-in flags, the awakening narration,
/// the ceremony's suggestion tally) should reference the constants instead
/// of retyping strings.
/// </summary>
public static class TutorialFlags
{
    // Light
    public const string HelpHealer = "flag_help_healer";
    public const string LightCandle = "flag_light_candle";
    public const string GiveAlms = "flag_give_alms";

    // Dark
    public const string SmashCrates = "flag_smash_crates";
    public const string TakeOffering = "flag_take_offering";

    // Fire
    public const string Bellows = "flag_bellows";
    public const string Quench = "flag_quench";

    // Water
    public const string DrawWell = "flag_draw_well";
    public const string FillJug = "flag_fill_jug";
    public const string FreeNet = "flag_free_net";

    // Air
    public const string MillClimb = "flag_mill_climb";
    public const string FreePigeon = "flag_free_pigeon";

    // Earth
    public const string DigGrave = "flag_dig_grave";
    public const string DigRow = "flag_dig_row";
    public const string HaulStones = "flag_haul_stones";

    // Old faith - narration only, weights no element
    public const string PrayShrine = "flag_pray_shrine";

    // Easter eggs
    public const string FossilFound = "flag_fossil_found";
    public const string FossilDelivered = "flag_fossil_delivered";
    public const string RepairMill = "flag_repair_mill";

    /// <summary>Written by the ceremony at commit. Its presence means the player
    /// LIVED the prologue; its absence means they skipped it entirely. Without
    /// this the two states are indistinguishable, and an empty-handed life reads
    /// exactly like a life that never happened.</summary>
    public const string Lived = "flag_lived";

    /// <summary>The fifteen flags that weigh an affinity. Mirrors the rows in
    /// AffinityMapping (both derive from the constants above); a core with none
    /// of these is empty-handed. Egg flags and the shrine vote for nothing and
    /// are deliberately absent.</summary>
    public static readonly string[] AffinityFlags =
    {
        Bellows, Quench,
        DrawWell, FillJug, FreeNet,
        MillClimb, FreePigeon,
        DigGrave, DigRow, HaulStones,
        HelpHealer, LightCandle, GiveAlms,
        SmashCrates, TakeOffering,
    };
}