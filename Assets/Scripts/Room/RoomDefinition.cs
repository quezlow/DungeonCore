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

    [Tooltip("Flavour / purpose line shown at the top of the room tooltip.")]
    [TextArea] public string description = "";

    [Tooltip("What this room does mechanically, in plain words. Shown under the description.")]
    [TextArea] public string effectSummary = "";

    [Tooltip("Colour used to tint tiles during the validation flash effect.")]
    public Color validationTintColor = new(0.6f, 1f, 0.6f, 0.6f);

    [Header("Size Requirements")]
    [Tooltip("Minimum number of owned tiles the flood-fill must find " +
             "for this room type to validate.")]
    public int minTileCount = 9;

    [Tooltip("Maximum owned tiles allowed (0 = no maximum). The Throne Room uses this to cap its size.")]
    public int maxTileCount = 0;

    [Tooltip("If true, the room only validates when it encloses the dungeon core cell. Used by the Throne Room.")]
    public bool requiresCore = false;

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

    [Tooltip("Optional research gate: if set, the room is hidden from the picker "
           + "until this UnlockState key (usually tech.<node id>) is understood.")]
    public string requiredTechKey = "";

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
    LairRegen,          // HP per second for monsters whose spawner sits in the room
    TrainingXp,         // XP per second toward Veteran for those monsters
    MonsterDamageBuff,  // multiplies the attack damage of those monsters (perSecond = multiplier, e.g. 1.5)
    CoreRetaliation,    // the core zaps adventurers standing in the room (perSecond = damage/sec) + a pulse
    LibraryResearch,    // research points per DAY at dawn (handled by ResearchController, not the tick loop)
    GoldCapBonus,       // gold capacity added to the global cap (perSecond = capacity, x tier); census lane
    Attractor,          // spawn-roll weight for one adventurer type (perSecond = weight, x tier); see attractorTarget; census lane
    RespawnSpeed,       // respawn-speed bonus for spawners standing in the room (perSecond = bonus per tier); census lane
    SparringXp,         // XP per second granted to sparring pairs by SparringController (perSecond = XP/sec, x tier)
    AdventurerSlow,     // slows intruders standing in the room (perSecond = fraction removed per tier, e.g. 0.1)
    ManaRegen,          // mana per second added to the core's regeneration (perSecond = mana/sec, x tier); census lane
    TrapDamage,         // global trap-damage bonus while valid (perSecond = bonus per tier, e.g. 0.1); census lane
}

[Serializable]
public class RoomEffect
{
    public RoomEffectType type = RoomEffectType.LairRegen;

    [Tooltip("Magnitude. Per-second rates for LairRegen, TrainingXp, CoreRetaliation, " +
             "SparringXp and ManaRegen; attack-damage multiplier for MonsterDamageBuff " +
             "(e.g. 1.5); stored capacity for GoldCapBonus; spawn-roll weight for " +
             "Attractor; per-tier bonus for RespawnSpeed and TrapDamage; slow fraction " +
             "for AdventurerSlow. Every magnitude scales with room tier except " +
             "MonsterDamageBuff, which applies flat.")]
    [Min(0f)]
    public float perSecond = 1f;

    [Tooltip("Attractor effects only: the adventurer type this room draws. " +
             "Ignored by every other effect type.")]
    public AdventurerType attractorTarget = AdventurerType.Pilgrim;
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