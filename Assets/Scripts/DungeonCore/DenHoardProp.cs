using UnityEngine;

/// <summary>
/// The visible pile in a den cavity (canon 42). A den's tier is legible off two
/// things -- how full the hole is and how big the hoard is -- and for an
/// EXCAVATOR this is the only one of the two that exists: floor index 2 authors
/// no scavengerDefinition and PopulationBudget returns zero off Occupier, so a
/// kobold den has no bodies to count. On that floor the pile and the growing
/// hole are the whole of the signal.
///
/// PURE VISUAL SKIN. No collider, no interaction, nothing pathfinding or mining
/// reads. Canon 42 leaves the hoard inert deliberately: adventurers are hostile
/// to dens and a Treasure Hunter grabs the first thing it finds, so a reachable
/// pile is a strong beat -- and one that collides with ClearDen paying the whole
/// hoard out, and with den gold being exempt from the outflow ledgers. That is
/// its own arc, not a field on this component.
/// </summary>
[DisallowMultipleComponent]
public class DenHoardProp : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Sprite[] tierSprites;

    /// <summary>The tier currently on screen, so the dawn poll can skip the
    /// common case. -1 rather than 0 because tier 1 is a real tier a den holds
    /// from the moment it wakes, so 0 would be indistinguishable from "not yet
    /// shown" -- the ambiguous-default trap this project bans.</summary>
    private int shownTier = -1;

    /// <summary>Hands the prop its art and its starting tier. Called by
    /// TerrainFeatureGenerator on reveal, and again on the load path.</summary>
    public void Bind(Sprite[] sprites, int tier)
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        tierSprites = sprites;
        shownTier = -1;
        SetTier(tier);
    }

    /// <summary>Shows the sprite for a tier. Cheap to call every dawn: returns
    /// immediately unless the tier actually moved.</summary>
    public void SetTier(int tier)
    {
        if (tier == shownTier) return;
        shownTier = tier;

        if (spriteRenderer == null) return;

        // An unassigned slot DISABLES the renderer rather than showing Unity's
        // magenta or a stale sprite from the tier below. Half the ten slots may
        // legitimately be empty for a while -- the art is authored per floor and
        // per tier -- and a den whose pile silently stops growing at tier 3 is
        // a worse lie than a den with no pile at all.
        Sprite s = (tierSprites != null && tier >= 1 && tier <= tierSprites.Length)
            ? tierSprites[tier - 1] : null;
        spriteRenderer.sprite = s;
        spriteRenderer.enabled = s != null;
    }
}
