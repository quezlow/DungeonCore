using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Dungeon/Scout Tier Profile", fileName = "ScoutTierProfile")]
public class ScoutTierProfile : ScriptableObject
{
    [Serializable]
    public class Tier
    {
        public string unlockKey = "tech.scout_1";  // node completion key
        public float maxRadius = 12f;              // world units from the scout origin
    }

    [Tooltip("Ascending reach. The scout uses the largest radius whose key is unlocked.")]
    public Tier[] tiers;

    [Header("Camera")]
    public float panSpeed = 9f;

    [Header("Mana cost per second while scouting")]
    [Tooltip("Cost right at the origin -- 'looking at the camera spawn costs little'.")]
    public float baseCostPerSecond = 0.4f;
    [Tooltip("Extra cost per world unit of distance from the origin.")]
    public float costPerUnitDistance = 1.2f;

    /// <summary>Largest reach the player has researched. 0 if none.</summary>
    public float MaxRadius()
    {
        float r = 0f;
        if (tiers != null)
            foreach (var t in tiers)
                if (!string.IsNullOrEmpty(t.unlockKey) && UnlockState.IsUnlocked(t.unlockKey))
                    r = Mathf.Max(r, t.maxRadius);
        return r;
    }

    public bool AnyTierUnlocked() => MaxRadius() > 0f;
}