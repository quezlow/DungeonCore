using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data asset defining what makes a room of a given type valid.
/// Create via: right-click → Create → Dungeon → Room Definition
///
/// KNOWN ROOM TYPES (from roadmap p.8)
///   Library       — shelves, desk, seating  (min 12 tiles)
///   Barracks      — beds                    (min  9 tiles)
///   Shrine        — altar + open space      (min  9 tiles)
///   Oracle Chamber — TechNode room          (min 12 tiles)
///   Boss Room     — boss spawner present    (min 16 tiles, requiresBossSpawner = true)
///
/// Full room list is TBD per the design doc. Add new RoomDefinition assets
/// as room types are decided — no code changes required here.
/// </summary>
[CreateAssetMenu(fileName = "NewRoomDefinition",
                 menuName = "Dungeon/Room Definition")]
public class RoomDefinition : ScriptableObject
{
    [Header("Identity")]
    public string roomName = "Room";

    [Tooltip("Colour used to tint tiles during the validation flash effect.")]
    public Color validationTintColor = new(0.6f, 1f, 0.6f, 0.6f);

    [Header("Size Requirements")]
    [Tooltip("Minimum number of owned tiles the flood-fill must find " +
             "for this room type to validate.")]
    public int minTileCount = 9;

    [Header("Required Furniture")]
    [Tooltip("Each entry specifies a furniture type and the minimum count required.")]
    public List<FurnitureRequirement> requiredFurniture = new();

    [Header("Boss Requirement")]
    [Tooltip("If true, the room must contain a MonsterSpawner whose definition " +
             "is a BossVariantDefinition. Used by the Boss Room type.")]
    public bool requiresBossSpawner = false;

    [Header("TechNode Unlock")]
    [Tooltip("Optional. If set, this room unlocks a capability when validated. " +
             "Leave empty for rooms with no TechNode effect.")]
    public string techNodeUnlockKey = "";

    [Tooltip("Human-readable description of what this TechNode unlocks. " +
             "Displayed in the room label tooltip (Day 25+).")]
    public string techNodeDescription = "";

    [Header("Upgrades")]
    [Tooltip("Maximum upgrade tier (1 = no upgrades). Higher tiers scale this room's effects.")]
    [Min(1)] public int maxTier = 3;

    [Tooltip("Gold cost for tier 1→2. Each further tier costs this × the current tier.")]
    [Min(0)] public int upgradeBaseCost = 100;

    [Header("Mechanical Effects")]
    [Tooltip("Effects applied while this room is valid. Lair = HP regen for the " +
             "room's monsters; Training = XP over time toward Veteran. More types later.")]
    public List<RoomEffect> effects = new();
}

public enum RoomEffectType
{
    LairRegen,   // HP per second for monsters whose spawner sits in the room
    TrainingXp,  // XP per second toward Veteran for those monsters
}

[Serializable]
public class RoomEffect
{
    public RoomEffectType type = RoomEffectType.LairRegen;

    [Tooltip("Per-second magnitude: HP/sec for LairRegen, XP/sec for TrainingXp.")]
    [Min(0f)]
    public float perSecond = 1f;
}

[Serializable]
public class FurnitureRequirement
{
    [Tooltip("The furniture type required.")]
    public FurnitureDefinition furnitureType;

    [Tooltip("Minimum number of this furniture type that must be present.")]
    [Min(1)]
    public int minimumCount = 1;
}