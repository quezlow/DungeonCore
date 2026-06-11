using System;
using System.IO;
using UnityEngine;

/// <summary>
/// DAY 34 — Centralised path resolution + slot enumeration for slot-based saves.
///
///   Saves/slot_{N}/save.json
///   Saves/slot_{N}/save.json.bak
///   Saves/slot_{N}/save.json.tmp
///   Saves/slot_{N}/meta.json
///   Saves/slot_{N}/meta.json.tmp
/// </summary>
public static class SlotPaths
{
    public const int MIN_SLOT_ID = 1;
    public const int MAX_SLOT_ID = 10;
    public const int SLOT_COUNT = MAX_SLOT_ID - MIN_SLOT_ID + 1;

    public static string SavesRoot =>
        Path.Combine(Application.persistentDataPath, "Saves");

    public static string SlotFolder(int slotId) =>
        Path.Combine(SavesRoot, $"slot_{slotId}");

    public static string SavePath(int slotId) => Path.Combine(SlotFolder(slotId), "save.json");
    public static string TmpPath(int slotId) => Path.Combine(SlotFolder(slotId), "save.json.tmp");
    public static string BakPath(int slotId) => Path.Combine(SlotFolder(slotId), "save.json.bak");
    public static string MetaPath(int slotId) => Path.Combine(SlotFolder(slotId), "meta.json");
    public static string MetaTmpPath(int slotId) => Path.Combine(SlotFolder(slotId), "meta.json.tmp");

    public static bool SlotHasSave(int slotId) => File.Exists(SavePath(slotId));
    public static bool SlotHasMeta(int slotId) => File.Exists(MetaPath(slotId));
    public static bool SlotIsEmpty(int slotId) => !File.Exists(SavePath(slotId)) && !File.Exists(BakPath(slotId));

    public static void EnsureSlotFolder(int slotId)
    {
        var folder = SlotFolder(slotId);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
    }

    public static void EnsureSavesRoot()
    {
        if (!Directory.Exists(SavesRoot)) Directory.CreateDirectory(SavesRoot);
    }

    /// <summary>Recursively deletes the entire slot folder.</summary>
    public static void DeleteSlot(int slotId)
    {
        var folder = SlotFolder(slotId);
        if (!Directory.Exists(folder)) return;
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception e)
        {
            Debug.LogError($"[SlotPaths] Failed to delete slot {slotId}: {e.Message}");
        }
    }

    /// <summary>
    /// Reads meta.json for a slot. Returns null if missing or unreadable.
    /// Used by the title screen — does NOT touch the full save file.
    /// </summary>
    public static SlotMetadata ReadMetadata(int slotId)
    {
        string path = MetaPath(slotId);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SlotMetadata>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SlotPaths] Failed to read meta for slot {slotId}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scans all slots and returns the ID of the most recently played one
    /// (highest meta.lastPlayedUtc). Returns 0 if no saves exist.
    /// </summary>
    public static int FindMostRecentSlotId()
    {
        int bestSlot = 0;
        DateTime bestTime = DateTime.MinValue;
        for (int i = MIN_SLOT_ID; i <= MAX_SLOT_ID; i++)
        {
            if (!SlotHasSave(i)) continue;
            var meta = ReadMetadata(i);
            if (meta == null) continue;
            if (meta.LastPlayedUtc > bestTime)
            {
                bestTime = meta.LastPlayedUtc;
                bestSlot = i;
            }
        }
        return bestSlot;
    }
}