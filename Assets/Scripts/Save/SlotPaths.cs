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

    /// <summary>Prologue checkpoint written by SaveController while the player is still mortal.</summary>
    public static string ProloguePath(int slotId) => Path.Combine(SlotFolder(slotId), "prologue.json");

    public static bool SlotHasSave(int slotId) => File.Exists(SavePath(slotId));
    public static bool SlotHasMeta(int slotId) => File.Exists(MetaPath(slotId));
    public static bool SlotHasPrologue(int slotId) => File.Exists(ProloguePath(slotId));
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
            if (!SlotHasSave(i) && !SlotHasPrologue(i)) continue;
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

    // -- Smoke-test snapshots (canon 56) ---------------------------
    //
    // A smoke test that spans sessions has to RE-ENTER a state, and rebuilding
    // a level 24 dungeon by hand is the expensive part of that. A snapshot is a
    // byte copy of a slot's files parked under a label, so restoring is a copy
    // back rather than a replay.
    //
    // This lives here rather than with the test commands because this file
    // already owns every save path, the slot enumeration and the delete. A
    // second module resolving save paths is how two modules drift apart.

    public static string SnapshotsRoot => Path.Combine(SavesRoot, "snapshots");

    public static string SnapshotFolder(string label) =>
        Path.Combine(SnapshotsRoot, SanitiseLabel(label));

    /// <summary>The files a slot owns, named rather than globbed. save.json.tmp
    /// is deliberately absent: it is a half-written save caught mid atomic swap,
    /// and copying one into a snapshot would preserve the single state the
    /// atomic write exists to make unreachable.</summary>
    private static readonly string[] SnapshotFiles =
        { "save.json", "save.json.bak", "meta.json", "prologue.json" };

    /// <summary>Labels become folder names, so anything that is not a letter,
    /// digit, dash or underscore folds to an underscore. An empty label resolves
    /// to "unnamed" rather than to the snapshots root itself, which a later
    /// restore would then try to read as though it were a slot.</summary>
    public static string SanitiseLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "unnamed";
        var sb = new System.Text.StringBuilder(label.Length);
        foreach (char c in label.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        return sb.Length == 0 ? "unnamed" : sb.ToString();
    }

    public static bool SnapshotExists(string label) =>
        File.Exists(Path.Combine(SnapshotFolder(label), "save.json"));

    /// <summary>Snapshot labels present on disk, sorted. Empty array, never
    /// null. A folder without a save.json is not counted -- a half-copied
    /// snapshot that listed as available would restore an unloadable slot.</summary>
    public static string[] SnapshotLabels()
    {
        if (!Directory.Exists(SnapshotsRoot)) return new string[0];
        var dirs = Directory.GetDirectories(SnapshotsRoot);
        var list = new System.Collections.Generic.List<string>(dirs.Length);
        foreach (var d in dirs)
        {
            if (File.Exists(Path.Combine(d, "save.json")))
                list.Add(Path.GetFileName(d));
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    /// <summary>Reads a snapshot's meta.json so a listing can name what it holds.
    /// Null if missing or unreadable.</summary>
    public static SlotMetadata ReadSnapshotMetadata(string label)
    {
        string path = Path.Combine(SnapshotFolder(label), "meta.json");
        if (!File.Exists(path)) return null;
        try { return JsonUtility.FromJson<SlotMetadata>(File.ReadAllText(path)); }
        catch (Exception e)
        {
            Debug.LogWarning($"[SlotPaths] Failed to read snapshot meta '{label}': {e.Message}");
            return null;
        }
    }

    /// <summary>Copy a slot's files into a labelled snapshot. Returns the result
    /// in words, following canon 51's test-hook pattern -- a hook that returns
    /// void teaches nothing when nothing happens.</summary>
    public static string CaptureSnapshot(int slotId, string label)
    {
        if (slotId < MIN_SLOT_ID || slotId > MAX_SLOT_ID)
            return "refused: slot " + slotId + " is outside " + MIN_SLOT_ID + ".." + MAX_SLOT_ID + ".";
        if (!SlotHasSave(slotId) && !SlotHasPrologue(slotId))
            return "refused: slot " + slotId + " holds no save and no prologue -- nothing to capture.";

        string dest = SnapshotFolder(label);
        try
        {
            Directory.CreateDirectory(dest);
            int copied = 0;
            foreach (var name in SnapshotFiles)
            {
                string src = Path.Combine(SlotFolder(slotId), name);
                if (!File.Exists(src)) continue;
                File.Copy(src, Path.Combine(dest, name), overwrite: true);
                copied++;
            }
            var meta = ReadMetadata(slotId);
            string desc = meta != null
                ? " (" + meta.dungeonName + ", level " + meta.dungeonLevel
                  + ", day " + meta.currentDay + ")"
                : "";
            return "captured slot " + slotId + desc + " to '" + SanitiseLabel(label)
                 + "', " + copied + " file(s).";
        }
        catch (Exception e)
        {
            return "FAILED: " + e.Message;
        }
    }

    /// <summary>Copy a labelled snapshot back over a slot. Every file the slot
    /// owns is removed first, so a stale prologue.json cannot linger and send
    /// the boot path down the wrong branch -- but only those NAMED files, never
    /// a recursive folder delete, so a mistake here cannot reach anything the
    /// slot does not own.</summary>
    public static string RestoreSnapshot(int slotId, string label)
    {
        if (slotId < MIN_SLOT_ID || slotId > MAX_SLOT_ID)
            return "refused: slot " + slotId + " is outside " + MIN_SLOT_ID + ".." + MAX_SLOT_ID + ".";
        if (!SnapshotExists(label))
            return "refused: no snapshot named '" + SanitiseLabel(label) + "'. Run List Snapshots.";

        string src = SnapshotFolder(label);
        try
        {
            // The safety net. Whatever the slot currently holds is parked under
            // _prerestore first, so restoring over a state you meant to keep is
            // recoverable -- once. It is a net, not a history.
            if (SlotHasSave(slotId) || SlotHasPrologue(slotId))
                CaptureSnapshot(slotId, "_prerestore");

            EnsureSlotFolder(slotId);
            foreach (var name in SnapshotFiles)
            {
                string path = Path.Combine(SlotFolder(slotId), name);
                if (File.Exists(path)) File.Delete(path);
            }
            // The half-written temp belongs to the state being replaced.
            string tmp = TmpPath(slotId);
            if (File.Exists(tmp)) File.Delete(tmp);

            int copied = 0;
            foreach (var name in SnapshotFiles)
            {
                string from = Path.Combine(src, name);
                if (!File.Exists(from)) continue;
                File.Copy(from, Path.Combine(SlotFolder(slotId), name), overwrite: true);
                copied++;
            }
            return "restored '" + SanitiseLabel(label) + "' into slot " + slotId
                 + ", " + copied + " file(s). Previous contents parked at '_prerestore'.";
        }
        catch (Exception e)
        {
            return "FAILED: " + e.Message;
        }
    }
}