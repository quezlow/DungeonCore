using UnityEngine;

/// <summary>
/// One material pattern -- a boolean discovery the core can learn. Patterns
/// carry no stockpile: once known, the core reconstitutes the material from
/// mana. The unlock flag itself lives in UnlockState under Key; this asset is
/// the display/data side read by the codex, the discovery manager and (later)
/// research tree nodes on the Architecture path.
/// </summary>
[CreateAssetMenu(fileName = "Pattern", menuName = "Dungeon Core/Pattern Definition")]
public class PatternDefinition : ScriptableObject
{
    public enum PatternBand
    {
        Terrain = 0,     // deterministic first-claim discoveries
        Common = 1,      // loot bands mirror the loot Rarity ladder
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
        Legendary = 5,
        Reserved = 6,    // future channels (trader / avatar / events)
    }

    [Tooltip("Stable id. The UnlockState key is 'pattern.' + this id -- never rename after ship.")]
    public string id;

    [Tooltip("Name shown in the codex and in discovery alerts.")]
    public string displayName;

    public PatternBand band = PatternBand.Common;

    [Tooltip("Codex icon. Rendered near-black while undiscovered. Null-safe.")]
    public Sprite icon;

    [Tooltip("Atmospheric hint shown on the silhouetted (undiscovered) row.")]
    [TextArea] public string sourceHint;

    [Tooltip("Flavour line shown once discovered, above the learned-from note.")]
    [TextArea] public string discoveryNote;

    /// <summary>Full UnlockState key for this pattern.</summary>
    public string Key => "pattern." + id;
}