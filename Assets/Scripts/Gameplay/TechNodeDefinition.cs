using System;
using System.Collections.Generic;
using UnityEngine;

public enum ResearchPath
{
    Observation = 0,   // intel / UX gating
    Architecture = 1,  // building unlocks; the only pattern-gated path
    Bestiary = 2,      // monster unlocks
    Sorcery = 3,       // reserved -- ships only if Phase 5 core spells are greenlit
}

/// <summary>
/// One research tree node. The unlock flag lives in UnlockState under Key;
/// this asset carries costs, prerequisites and display data. Purchases are
/// timed projects: spending the points starts the project, completion lands
/// at dawn after durationDays (see ResearchController).
/// </summary>
[CreateAssetMenu(fileName = "TechNode", menuName = "Dungeon/Tech Node")]
public class TechNodeDefinition : ScriptableObject
{
    [Serializable]
    public class RoomUpgradeGate
    {
        [Tooltip("Room whose upgrade this node gates.")]
        public RoomDefinition room;
        [Tooltip("Tiers at or above this number require the node. 2 = gates the first upgrade.")]
        [Min(2)] public int minTier = 2;
    }

    [Tooltip("Stable id. The UnlockState key is 'tech.' + this id -- never rename after ship.")]
    public string id;

    [Tooltip("Optional legacy key override (e.g. 'oracle_chamber'). Leave empty for new nodes.")]
    public string overrideKey = "";

    [Tooltip("Name shown once the node is revealed (one prerequisite away) and in alerts.")]
    public string displayName;

    [Tooltip("Node icon shown on the tree canvas. Null-safe (renders as a plain block).")]
    public Sprite icon;

    public ResearchPath path = ResearchPath.Observation;

    [Tooltip("Tier within the path; presentation and authoring convention only.")]
    [Min(1)] public int tier = 1;

    [Header("Costs")]
    [Min(0)] public int pointCost = 10;

    [Tooltip("Project length in days once started. Completion lands at dawn.")]
    [Min(1)] public int durationDays = 1;

    [Tooltip("Core type whose affinity halves the point cost (points only -- never patterns). None = no discount.")]
    public DungeonType affinity = DungeonType.None;

    [Tooltip("Material patterns that must be known before this node can start. Architecture-path convention.")]
    public List<PatternDefinition> patternRequirements = new();

    [Header("Structure")]
    public List<TechNodeDefinition> prerequisites = new();

    [Tooltip("Unlocked from birth on a new game (the core 'remembering'). Re-locks behind the tutorial wisp later.")]
    public bool bootstrapUnlocked = false;

    [Header("Gates")]
    [Tooltip("Room upgrades this node gates via RoomAnchor.UpgradeGate.")]
    public List<RoomUpgradeGate> upgradeGates = new();

    [Header("Visibility")]
    [Tooltip("When the node appears on the tree at all. Hidden nodes reserve their layout slot but draw nothing -- a genuine surprise until the condition is met.")]
    public VisibilityCondition visibility = VisibilityCondition.Always;

    [Tooltip("For PatternKnown: the pattern whose discovery reveals this node.")]
    public PatternDefinition visibilityPattern;

    [Tooltip("For KeyUnlocked: any UnlockState key (e.g. a buried-skeleton discovery).")]
    public string visibilityKey = "";

    [Tooltip("For KillsOfClass: the RunStats kill-class name and required count.")]
    public string visibilityClassName = "";
    [Min(1)] public int visibilityKillCount = 1;

    public enum VisibilityCondition
    {
        Always = 0,
        PatternKnown = 1,   // a material pattern has been discovered
        KeyUnlocked = 2,    // an arbitrary UnlockState key is set
        KillsOfClass = 3,   // RunStats.KillsByClass[name] >= count
    }

    /// <summary>Whether the node appears on the tree canvas at all.</summary>
    public bool IsVisible()
    {
        switch (visibility)
        {
            case VisibilityCondition.PatternKnown:
                return visibilityPattern != null && UnlockState.IsUnlocked(visibilityPattern.Key);
            case VisibilityCondition.KeyUnlocked:
                return UnlockState.IsUnlocked(visibilityKey);
            case VisibilityCondition.KillsOfClass:
                if (RunStats.Instance == null || string.IsNullOrEmpty(visibilityClassName)) return false;
                RunStats.Instance.KillsByClass.TryGetValue(visibilityClassName, out int kills);
                return kills >= visibilityKillCount;
            default:
                return true;
        }
    }

    [Header("Display")]
    [Tooltip("Atmospheric line shown while the node's name is still hidden.")]
    [TextArea] public string hiddenHint;

    [TextArea] public string description;

    /// <summary>Full UnlockState key for this node.</summary>
    public string Key => string.IsNullOrEmpty(overrideKey) ? "tech." + id : overrideKey;
}