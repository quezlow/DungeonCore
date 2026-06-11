using System;
using System.IO;
using UnityEngine;

/// <summary>
/// DAY 34 — One-time migration of the pre-slot loose save file into slot 1.
///
/// On first launch with the slot system, if the legacy single file
/// "{persistentDataPath}/DungeonSaveData.json" exists AND no Saves/ folder
/// exists yet, this migrator moves it into Saves/slot_1/save.json and
/// synthesises a meta.json from the loaded save data.
///
/// Safe to call repeatedly — does nothing if either condition isn't met.
/// </summary>
public static class ExistingSaveMigrator
{
    private static bool hasRun = false;

    public static void RunIfNeeded()
    {
        if (hasRun) return;
        hasRun = true;

        string legacySavePath = Path.Combine(Application.persistentDataPath, "DungeonSaveData.json");
        string legacyBakPath = legacySavePath + ".bak";
        string legacyTmpPath = legacySavePath + ".tmp";

        if (!File.Exists(legacySavePath))
        {
            Debug.Log("[ExistingSaveMigrator] No legacy save to migrate.");
            return;
        }

        if (Directory.Exists(SlotPaths.SavesRoot))
        {
            Debug.Log("[ExistingSaveMigrator] Saves/ folder already exists — skipping migration to avoid clobbering.");
            return;
        }

        try
        {
            SlotPaths.EnsureSavesRoot();
            SlotPaths.EnsureSlotFolder(1);

            // Read first so we can synthesise meta
            string json = File.ReadAllText(legacySavePath);
            var data = JsonUtility.FromJson<DungeonSaveData>(json);
            if (data == null) throw new Exception("Legacy save deserialised to null.");

            // Move main + sidecars
            File.Move(legacySavePath, SlotPaths.SavePath(1));
            if (File.Exists(legacyBakPath)) File.Move(legacyBakPath, SlotPaths.BakPath(1));
            if (File.Exists(legacyTmpPath)) File.Delete(legacyTmpPath);

            // Synthesise meta.json
            var meta = new SlotMetadata
            {
                slotId = 1,
                dungeonName = string.IsNullOrWhiteSpace(data.dungeonName) ? "Imported Save" : data.dungeonName,
                dungeonType = data.coreData != null ? data.coreData.dungeonType : DungeonType.None,
                dungeonLevel = data.coreData != null ? data.coreData.dungeonLevel : 1,
                currentDay = data.dayNightData != null ? Mathf.Max(1, data.dayNightData.currentDay) : 1,
                lastPlayedIsoUtc = DateTime.UtcNow.ToString("o"),
                saveVersion = data.saveVersion == 0 ? 1 : data.saveVersion,
            };
            File.WriteAllText(SlotPaths.MetaPath(1), JsonUtility.ToJson(meta, prettyPrint: true));

            Debug.Log("[ExistingSaveMigrator] Migrated legacy DungeonSaveData.json → Saves/slot_1/.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ExistingSaveMigrator] Migration failed: {e.Message}. Legacy file left in place.");
        }
    }
}