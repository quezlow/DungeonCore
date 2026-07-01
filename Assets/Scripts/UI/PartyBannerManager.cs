using UnityEngine;

/// <summary>
/// Spawns and configures a PartyBanner for a tracked party. Named parties get an
/// intent-coloured bar; pinned standard parties get a random bar from a separate
/// pool, and that choice is stored on the party (and its save record) so it stays
/// identical across return visits and reloads.
///
/// SCENE SETUP: place on a GameObject in the gameplay scene; assign the banner
/// prefab, the three intent bars, and the pinned pool (your UI_Elements bars).
/// </summary>
public class PartyBannerManager : MonoBehaviour
{
    public static PartyBannerManager Instance { get; private set; }

    [SerializeField] private PartyBanner bannerPrefab;

    [Header("Intent bars")]
    [SerializeField] private Sprite pilgrimBar;    // Pilgrim  — blue
    [SerializeField] private Sprite giftGiverBar;  // GiftGiver — green
    [SerializeField] private Sprite destroyerBar;  // Destroyer — red

    [Header("Pinned pool (random for pinned standard parties)")]
    [SerializeField] private Sprite[] pinnedPool;  // e.g. white / purple / yellow-green

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Show a banner for a tracked party (no-op if it already has one).</summary>
    public void ShowBanner(AdventurerParty party)
    {
        if (party == null || party.hasBanner || bannerPrefab == null) return;
        if (party.CurrentLead() == null) return;

        Sprite bar = HasNamed(party) ? IntentBar(party.Intent) : PinnedBar(party);
        if (bar == null) return;

        var banner = Instantiate(bannerPrefab);
        banner.Initialise(party, bar, TrackedPartyRegistry.LabelFor(party));
        party.hasBanner = true;
    }

    private static bool HasNamed(AdventurerParty p)
    {
        foreach (var m in p.Members) if (m.named) return true;
        return false;
    }

    private Sprite IntentBar(PartyIntent intent) => intent switch
    {
        PartyIntent.Pilgrim => pilgrimBar,
        PartyIntent.GiftGiver => giftGiverBar,
        PartyIntent.Destroyer => destroyerBar,
        _ => destroyerBar,
    };

    private Sprite PinnedBar(AdventurerParty party)
    {
        if (pinnedPool == null || pinnedPool.Length == 0) return null;
        if (party.bannerColorIndex < 0 || party.bannerColorIndex >= pinnedPool.Length)
            party.bannerColorIndex = Random.Range(0, pinnedPool.Length);
        return pinnedPool[party.bannerColorIndex];
    }
}