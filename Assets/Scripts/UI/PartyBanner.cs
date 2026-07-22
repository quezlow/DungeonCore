using TMPro;
using UnityEngine;

/// <summary>
/// World-space banner above a tracked party: a coloured bar sprite + the party's
/// name, following the party's current lead. Re-homes to the next survivor as
/// members are lost and removes itself once the party drops below half its original
/// size (deaths and flees both count). Spawned + configured by PartyBannerManager;
/// mirrors EntityStatusBars' follow pattern.
///
/// PREFAB SETUP:
///   PartyBanner (this script)
///   |-- Bar    (SpriteRenderer — the coloured bar  → bar)
///   |-- Label  (TMP_Text — the party name          → label)
/// </summary>
public class PartyBanner : MonoBehaviour
{
    [SerializeField] private SpriteRenderer bar;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.3f, 0f);
    [Tooltip("Clearance above the lead's sprite top when it stands taller than the " +
             "fallback offset — keeps the name clear of big sprites and their status bars.")]
    [SerializeField] private float spriteClearance = 0.75f;
    [Tooltip("World-space width the ribbon is scaled to, whatever the source sprite's " +
             "pixel size — keeps every pool sprite the same size on screen. 0 = native.")]
    [SerializeField] private float targetWorldWidth = 3f;

    /// <summary>The party this banner follows — lets the manager find and remove
    /// a banner when its party is unpinned.</summary>
    public AdventurerParty Party => party;

    private AdventurerParty party;
    private int originalSize;
    private DungeonAdventurer cachedLead;
    private SpriteRenderer leadSprite;

    public void Initialise(AdventurerParty p, Sprite barSprite, string text)
    {
        party = p;
        originalSize = (p != null && p.Members.Count > 0) ? p.Members.Count : 1;
        if (bar != null && barSprite != null) bar.sprite = barSprite;
        if (label != null) label.text = text;

        // Normalize the ribbon to a fixed world width so every bar sprite —
        // intent-coloured or pinned-pool — renders the same size on screen,
        // whatever its native pixel dimensions or import PPU.
        if (bar != null && bar.sprite != null && targetWorldWidth > 0f)
        {
            float nativeWidth = bar.sprite.bounds.size.x;
            if (nativeWidth > 0.01f)
            {
                float s = targetWorldWidth / nativeWidth;
                bar.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        // The banner is a WORLD object (the bar is a SpriteRenderer), but the
        // label is a TextMeshProUGUI, which only draws through a Canvas. The
        // prefab's Canvas cannot be set to World Space in the Inspector -- inside
        // Prefab Mode it reads as nested and Unity hides Render Mode -- and it is
        // instantiated parentless, so it would come up Screen Space Overlay and
        // render the banner across the screen. Set it here, where the bar's own
        // sorting is known, and keep the text one step above the ribbon.
        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            if (bar != null)
            {
                canvas.sortingLayerID = bar.sortingLayerID;
                canvas.sortingOrder = bar.sortingOrder + 1;
            }
        }

        // The label is a child of the banner root at local origin, so it
        // already rides the ribbon as the root follows the party lead. DO NOT
        // stamp its world position here -- doing so before the first LateUpdate
        // decouples it from the moving root, which left the name adrift beside
        // an empty ribbon. Only its sorting needs setting.
        if (label != null && bar != null)
        {
            label.transform.localPosition = Vector3.zero;
            var labelRenderer = label.GetComponent<Renderer>();
            if (labelRenderer != null)
            {
                labelRenderer.sortingLayerID = bar.sortingLayerID;
                labelRenderer.sortingOrder = bar.sortingOrder + 1;
            }
        }
    }

    private void LateUpdate()
    {
        if (party == null) { Destroy(gameObject); return; }

        var lead = party.CurrentLead();
        // Drop once a majority of the party is gone (died or fled), or none remain.
        // A broken anonymous party stops being worth a banner, but a named
        // champion's banner is HIS -- losing his escort must not erase it while
        // he still walks. DisplayName covers named Nobles too, not just Heroes.
        bool namedAlive = false;
        foreach (var m in party.LiveMembers)
            if (m != null && !string.IsNullOrEmpty(m.DisplayName)) { namedAlive = true; break; }

        if (lead == null || (!namedAlive && party.LiveCount() * 2 < originalSize))
        {
            Destroy(gameObject);
            return;
        }

        if (lead != cachedLead)
        {
            cachedLead = lead;
            leadSprite = lead.GetComponentInChildren<SpriteRenderer>();
        }

        Vector3 basePos = lead.transform.position;
        float y = basePos.y + worldOffset.y;
        if (leadSprite != null && leadSprite.sprite != null)
            y = Mathf.Max(y, leadSprite.bounds.max.y + spriteClearance);
        transform.position = new Vector3(basePos.x + worldOffset.x, y, basePos.z + worldOffset.z);
    }
}