using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surfaces the hidden DungeonRating as a named guild grade. When an Inspector
/// completes a visit it ASSESSES the dungeon - snapshotting the current rating and
/// mapping it to a tier ("Unremarkable" ... "Legendary"). The snapshot holds until
/// the next assessment, so matched adventurer teams face a stable grade rather than
/// a number that drifts every second. Persisted.
///
/// Two-stage assessment: a BACKEND flag (the rank is known, used to size responses
/// and matched teams) and a PLAYER-VISIBLE flag (the badge + announcement). An
/// Inspector that leaves alive sets both. An Inspector slain inside sets only the
/// backend rank - the badge stays "Unassessed" until a Guild kill-team investigates
/// and departs, when RevealToPlayer lifts the veil.
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

    private bool assessed;         // backend: the rank is known
    private bool playerAssessed;   // visible: the player has been shown the grade
    private float assessedRating;

    /// <summary>Backend: the rank has been taken (drives matched-team sizing).</summary>
    public bool HasBeenAssessed => assessed;
    /// <summary>Visible: the player has been shown a grade (gates matched teams + the badge).</summary>
    public bool PlayerHasBeenAssessed => playerAssessed;
    public float AssessedRating => assessedRating;

    /// <summary>Fires whenever the assessed state changes (or a load restores one).</summary>
    public static event Action OnAssessed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Full assessment - an Inspector left alive. Snapshots the rank, reveals it
    /// to the player, and announces it.</summary>
    public void Assess()
    {
        Snapshot();
        assessed = true;
        playerAssessed = true;
        Announce();
        OnAssessed?.Invoke();
    }

    /// <summary>Backend-only - the Inspector was slain. The Guild knows the rank (to size
    /// its response) but the player is not yet told.</summary>
    public void AssessBackendOnly()
    {
        Snapshot();
        assessed = true;
        OnAssessed?.Invoke();
    }

    /// <summary>Lift the veil once the kill-team's investigation departs.</summary>
    public void RevealToPlayer()
    {
        if (!assessed || playerAssessed) return;
        playerAssessed = true;
        Announce();
        OnAssessed?.Invoke();
    }

    private void Snapshot()
        => assessedRating = DungeonRating.Instance != null ? DungeonRating.Instance.CurrentRating : 0f;

    private void Announce()
    {
        AlertsLog.Instance?.AddAlert(
            $"The Guild has taken your measure: this dungeon is rated {TierNameFor(assessedRating)}.",
            DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : Vector3.zero,
            FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0,
            AlertCategory.System);
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

    /// <summary>The current grade name, or "Unassessed" until the player is shown one.</summary>
    public string CurrentTierName => playerAssessed ? TierNameFor(assessedRating) : "Unassessed";

    public GradeSystemSaveData GetSaveData()
        => new GradeSystemSaveData { assessed = assessed, playerAssessed = playerAssessed, assessedRating = assessedRating };

    public void RestoreFromSave(GradeSystemSaveData data)
    {
        if (data == null) return;
        assessed = data.assessed;
        assessedRating = data.assessedRating;
        // A save taken mid-investigation (rank known, not yet revealed) completes the
        // reveal on load rather than leaving the player stuck Unassessed.
        playerAssessed = data.playerAssessed || data.assessed;
        OnAssessed?.Invoke();
    }
}

[Serializable]
public class GradeSystemSaveData
{
    public bool assessed;
    public bool playerAssessed;
    public float assessedRating;
}