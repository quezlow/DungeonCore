using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surfaces the hidden DungeonRating as a named guild grade. When an Inspector
/// completes a visit it ASSESSES the dungeon - snapshotting the current rating and
/// mapping it to a tier ("Unremarkable" ... "Legendary"). The snapshot holds until
/// the next assessment, so matched adventurer teams (built later) face a stable
/// grade rather than a number that drifts every second. Persisted.
///
/// SCENE SETUP: put this on the persistent manager GameObject (alongside DungeonRating,
/// FactionSystem, ...). Populate the Tiers list, lowest threshold first.
/// </summary>
public class GradeSystem : MonoBehaviour
{
    public static GradeSystem Instance { get; private set; }

    [Serializable]
    public class GradeTier
    {
        public string tierName = "Unremarkable";
        [Tooltip("Minimum snapshot rating to reach this tier. List tiers low to high.")]
        public float minRating = 0f;
    }

    [Header("Grade tiers (list low to high)")]
    [SerializeField] private List<GradeTier> tiers = new();

    private bool assessed;
    private float assessedRating;

    public bool HasBeenAssessed => assessed;
    public float AssessedRating => assessedRating;

    /// <summary>Fires whenever a fresh assessment lands (or a load restores one).</summary>
    public static event Action OnAssessed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Snapshot the live rating as the assessed grade and announce it. Called
    /// when an Inspector completes a visit.</summary>
    public void Assess()
    {
        assessedRating = DungeonRating.Instance != null ? DungeonRating.Instance.CurrentRating : 0f;
        assessed = true;

        string tier = TierNameFor(assessedRating);
        AlertsLog.Instance?.AddAlert(
            $"The Guild has taken your measure: this dungeon is rated {tier}.",
            DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : Vector3.zero,
            FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0,
            AlertCategory.System);

        OnAssessed?.Invoke();
    }

    /// <summary>The tier name for a rating - the highest tier whose threshold it clears.</summary>
    public string TierNameFor(float rating)
    {
        string name = (tiers != null && tiers.Count > 0) ? tiers[0].tierName : "Unrated";
        if (tiers != null)
            foreach (var t in tiers)
                if (t != null && rating >= t.minRating) name = t.tierName;
        return name;
    }

    /// <summary>The current grade name, or "Unassessed" before the first inspection.</summary>
    public string CurrentTierName => assessed ? TierNameFor(assessedRating) : "Unassessed";

    public GradeSystemSaveData GetSaveData()
        => new GradeSystemSaveData { assessed = assessed, assessedRating = assessedRating };

    public void RestoreFromSave(GradeSystemSaveData data)
    {
        if (data == null) return;
        assessed = data.assessed;
        assessedRating = data.assessedRating;
        OnAssessed?.Invoke();
    }
}

[Serializable]
public class GradeSystemSaveData
{
    public bool assessed;
    public float assessedRating;
}