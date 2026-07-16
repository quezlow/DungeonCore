using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Material pattern discovery manager (scene singleton; lives with the other
/// global managers).
///
/// Live channels in this build:
///   - Terrain first-claim: TileInfluenceManager.ClaimTile notifies on every
///     live (non-silent) claim; the first claim of each terrain type teaches
///     its pattern deterministically. Bedrock teaches nothing.
///   - Adventurer loot: DroppedLoot.Absorb notifies with the drop's rarity;
///     a per-rarity chance teaches one random unlearned pattern from that
///     band. Exhausted bands fizzle silently -- the trader (future channel)
///     is the designed catch-up valve. Tribute coin flourishes ride the same
///     path as Common.
/// Reserved channels (trader / avatar / events) exist as catalog entries
/// only; nothing unlocks them yet.
///
/// Unlock flags live in UnlockState under 'pattern.' keys. Learned-from
/// notes live here and persist via DungeonSaveController.
/// </summary>
public class PatternDiscovery : MonoBehaviour
{
    public static PatternDiscovery Instance { get; private set; }

    [Header("Catalog")]
    [SerializeField] private PatternCatalog catalog;

    [Header("Loot Channel Chances")]
    [Tooltip("Chance an absorbed drop of each rarity teaches a pattern from its band. Expected drops to finish a band = band size / chance.")]
    [Range(0f, 1f)][SerializeField] private float chanceCommon = 0.10f;
    [Range(0f, 1f)][SerializeField] private float chanceUncommon = 0.20f;
    [Range(0f, 1f)][SerializeField] private float chanceRare = 0.35f;
    [Range(0f, 1f)][SerializeField] private float chanceEpic = 0.60f;
    [Range(0f, 1f)][SerializeField] private float chanceLegendary = 1f;

    // Learned-from notes, keyed by full pattern key. Persisted by the save
    // controller; static so they survive scene reloads within a session.
    private static readonly Dictionary<string, string> learnedFrom = new();

    public PatternCatalog Catalog => catalog;

    private void Awake()
    {
        // Global manager: newest instance wins, no destroy guard (project
        // manager convention).
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // -- Channel entry points ----------------------------------------------

    /// <summary>Called by TileInfluenceManager on every live claim.</summary>
    public static void NotifyTerrainClaimed(TerrainType type, Vector3 worldPos, int floorIndex)
    {
        if (Instance == null || Instance.catalog == null) return;
        if (DungeonSaveController.IsLoading) return;

        string id = TerrainPatternId(type);
        if (id == null) return;

        var def = Instance.catalog.GetByKey("pattern." + id);
        if (def == null || UnlockState.IsUnlocked(def.Key)) return;

        Instance.Learn(def,
            "Unearthed from " + TerrainDisplayName(type) + ".",
            worldPos, floorIndex, announce: true);
    }

    /// <summary>Called by DroppedLoot when the core absorbs a drop.</summary>
    public static void NotifyLootAbsorbed(Rarity rarity, Vector3 worldPos)
    {
        if (Instance == null || Instance.catalog == null) return;
        if (DungeonSaveController.IsLoading) return;

        if (Random.value > Instance.ChanceFor(rarity)) return;

        var pool = Instance.catalog.UndiscoveredInBand(BandFor(rarity));
        if (pool.Count == 0) return;   // exhausted band: fizzle silently

        var def = pool[Random.Range(0, pool.Count)];
        Instance.Learn(def,
            "Gleaned from the fallen (" + rarity + " spoils).",
            worldPos, FloorIndexFromWorld(worldPos), announce: true);
    }

    /// <summary>Event channel: the fall of a named hero teaches Gravegold (once).</summary>
    public static void NotifyNamedHeroFelled(string heroName, Vector3 worldPos)
    {
        if (Instance == null || Instance.catalog == null) return;
        if (DungeonSaveController.IsLoading) return;

        var def = Instance.catalog.GetByKey("gravegold");
        if (def == null || UnlockState.IsUnlocked(def.Key)) return;

        Instance.Learn(def, "Taken from the fall of " + heroName + ".",
            worldPos, FloorIndexFromWorld(worldPos), announce: true);
    }

    /// <summary>
    /// Deterministic terrain catch-up: grants the terrain pattern for every
    /// terrain type already inside claimed territory, silently. Runs on new
    /// game (floor bootstraps may claim the starter area before the reset)
    /// and on load (heals saves that predate the pattern system).
    /// </summary>
    public static void CatchUpTerrain()
    {
        if (Instance == null || Instance.catalog == null) return;
        var fm = FloorManager.Instance;
        if (fm == null) return;

        for (int i = 0; i <= fm.MaxAllowedFloorIndex; i++)
        {
            var floor = fm.GetFloor(i);
            if (floor == null || floor.TileInfluence == null || floor.TerrainTypeMap == null) continue;

            foreach (var cell in floor.TileInfluence.ClaimedTiles)
            {
                string id = TerrainPatternId(floor.TerrainTypeMap.GetTerrainAt(cell));
                if (id == null) continue;
                var def = Instance.catalog.GetByKey("pattern." + id);
                if (def == null || UnlockState.IsUnlocked(def.Key)) continue;
                Instance.Learn(def, "Known from the old delvings.", Vector3.zero, -1, announce: false);
            }
        }
    }

    // -- Core learn ----------------------------------------------------------

    private void Learn(PatternDefinition def, string source, Vector3 worldPos,
                       int floorIndex, bool announce)
    {
        learnedFrom[def.Key] = source;   // note first, so OnChanged readers see it
        UnlockState.Unlock(def.Key);

        if (!announce) return;

        // The wisp is the player-facing feel of a discovery, independent of the
        // alert ledger (which is gated behind its own research). The alert stays
        // as the codex-history record and shows once alerts are unlocked.
        WispCompanion.Instance?.Speak("pattern_learned");
        AlertsLog.Instance?.AddAlert(
            "A pattern settles into the core: " + def.displayName + ".",
            worldPos, floorIndex, AlertCategory.Discovery);
    }

    private float ChanceFor(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return chanceUncommon;
            case Rarity.Rare: return chanceRare;
            case Rarity.Epic: return chanceEpic;
            case Rarity.Legendary: return chanceLegendary;
            default: return chanceCommon;
        }
    }

    private static PatternDefinition.PatternBand BandFor(Rarity r)
    {
        switch (r)
        {
            case Rarity.Uncommon: return PatternDefinition.PatternBand.Uncommon;
            case Rarity.Rare: return PatternDefinition.PatternBand.Rare;
            case Rarity.Epic: return PatternDefinition.PatternBand.Epic;
            case Rarity.Legendary: return PatternDefinition.PatternBand.Legendary;
            default: return PatternDefinition.PatternBand.Common;
        }
    }

    private static string TerrainPatternId(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.Dirt: return "packed_earth";
            case TerrainType.Sand: return "quarry_sand";
            case TerrainType.Stone: return "rough_stone";
            case TerrainType.Granite: return "veined_granite";
            case TerrainType.Ruins: return "ancient_masonry";
            case TerrainType.HolyGround: return "hallowed_stone";
            default: return null;   // Bedrock teaches nothing
        }
    }

    private static string TerrainDisplayName(TerrainType type)
    {
        switch (type)
        {
            case TerrainType.HolyGround: return "holy ground";
            default: return type.ToString().ToLowerInvariant();
        }
    }

    private static int FloorIndexFromWorld(Vector3 worldPos)
    {
        // Floors are offset by floorIndex * -2000 on Y (see FloorRoot).
        return Mathf.Max(0, Mathf.RoundToInt(-worldPos.y / 2000f));
    }

    // -- Learned-from persistence --------------------------------------------

    public static string LearnedFromNote(string key)
        => learnedFrom.TryGetValue(key, out var s) ? s : null;

    public static List<PatternNoteSaveData> GetNotesForSave()
    {
        var list = new List<PatternNoteSaveData>();
        foreach (var kv in learnedFrom)
            list.Add(new PatternNoteSaveData { key = kv.Key, source = kv.Value });
        return list;
    }

    public static void RestoreNotes(List<PatternNoteSaveData> notes)
    {
        learnedFrom.Clear();
        if (notes == null) return;
        foreach (var n in notes)
            if (n != null && !string.IsNullOrEmpty(n.key)) learnedFrom[n.key] = n.source;
    }

    public static void ClearNotes() => learnedFrom.Clear();
}