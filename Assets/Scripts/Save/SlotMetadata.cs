using System;
using UnityEngine;

/// <summary>
/// DAY 34 — Lightweight per-slot metadata sidecar. Written alongside the
/// full save file so the title screen can display slot info without
/// deserialising entire saves.
///
/// File: Saves/slot_{N}/meta.json
/// </summary>
[Serializable]
public class SlotMetadata
{
    public int slotId;
    public string dungeonName = "Unnamed Dungeon";
    public DungeonType dungeonType = DungeonType.None;
    public int dungeonLevel = 1;
    public int currentDay = 1;
    public string lastPlayedIsoUtc = "";   // DateTime.UtcNow.ToString("o")
    public int saveVersion = 0;            // mirrors DungeonSaveData.saveVersion

    /// <summary>Parsed last-played time, or DateTime.MinValue if unparseable.</summary>
    public DateTime LastPlayedUtc =>
        DateTime.TryParse(lastPlayedIsoUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d
            : DateTime.MinValue;
}