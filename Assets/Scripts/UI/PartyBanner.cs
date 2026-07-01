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

    private AdventurerParty party;
    private int originalSize;

    public void Initialise(AdventurerParty p, Sprite barSprite, string text)
    {
        party = p;
        originalSize = (p != null && p.Members.Count > 0) ? p.Members.Count : 1;
        if (bar != null && barSprite != null) bar.sprite = barSprite;
        if (label != null) label.text = text;
    }

    private void LateUpdate()
    {
        if (party == null) { Destroy(gameObject); return; }

        var lead = party.CurrentLead();
        // Drop once a majority of the party is gone (died or fled), or none remain.
        if (lead == null || party.LiveCount() * 2 < originalSize) { Destroy(gameObject); return; }

        transform.position = lead.transform.position + worldOffset;
    }
}