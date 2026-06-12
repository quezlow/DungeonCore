using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of save-data migration delegates. Each migration mutates a
/// deserialized DungeonSaveData from one version into the next.
///
/// Currently the schema is at v1 and no migrations are registered. When a
/// non-additive schema change is made (renamed/removed/semantically-changed
/// field), bump DungeonSaveData.CURRENT_VERSION and register a migration here.
///
/// Additive changes (new fields, new list types) do not require an explicit
/// migration — JsonUtility's default-tolerant deserialization handles them.
///
/// Migration delegates are invoked in sequence: v1 → v2, v2 → v3, …, until the
/// data's saveVersion matches DungeonSaveData.CURRENT_VERSION.
/// </summary>
public static class SaveMigrationRegistry
{
    /// <summary>Mutates the deserialized data in place from one version to the next.</summary>
    public delegate void MigrationStep(DungeonSaveData data);

    /// <summary>Keyed by "from" version. Value migrates fromVersion → fromVersion + 1.</summary>
    private static readonly Dictionary<int, MigrationStep> migrations = new();

    static SaveMigrationRegistry()
    {
        // v1 → v2 (Influence/Mining Decoupling Phase 1).
        //
        // v1 stored a single TileInfluenceSaveData.ownedTiles list per floor —
        // semantically "what the player owns and has dug out". v2 introduces
        // separate claimedTiles (cells inside influence) and minedTiles (cells
        // dug out). In Phase 1 these are identical, so we copy the legacy list
        // into both. The legacy field is cleared to keep the file clean.
        migrations[1] = data =>
        {
            if (data?.floors == null) return;

            foreach (var floor in data.floors)
            {
                var t = floor?.tileData;
                if (t == null) continue;

                // Defensive copy in case both lists somehow exist (shouldn't, but
                // belt-and-braces — prefer the legacy list if claimedTiles is empty,
                // otherwise leave any existing v2-shaped data alone).
                bool needsMigration = t.ownedTiles != null
                                  && t.ownedTiles.Count > 0
                                  && (t.claimedTiles == null || t.claimedTiles.Count == 0);

                if (needsMigration)
                {
                    t.claimedTiles = new List<SerializableVector3Int>(t.ownedTiles);
                    t.minedTiles = new List<SerializableVector3Int>(t.ownedTiles);
                    t.ownedTiles.Clear();
                    Debug.Log($"[SaveMigrationRegistry] v1→v2 floor {floor.floorIndex}: " +
                              $"migrated {t.claimedTiles.Count} tiles into both claimed and mined.");
                }
                else
                {
                    // Ensure non-null lists so the loader can iterate safely.
                    if (t.claimedTiles == null) t.claimedTiles = new List<SerializableVector3Int>();
                    if (t.minedTiles == null) t.minedTiles = new List<SerializableVector3Int>();
                    if (t.ownedTiles == null) t.ownedTiles = new List<SerializableVector3Int>();
                }
            }
        };

        // v2 → v3 (Influence/Mining Decoupling Phase 3).
        //
        // v2 stored the count of claimed tiles in DungeonCoreSaveData.ownedTileCount
        // (the field name was a Phase 2 semantic-drift artifact — the value tracked
        // claimed tiles, just under the old name). v3 renames the field to
        // claimedTileCount. Copy the legacy field into the new one and zero the
        // old. JsonUtility doesn't honor [FormerlySerializedAs] so we do this by
        // hand.
        migrations[2] = data =>
        {
            if (data?.coreData == null) return;

            if (data.coreData.ownedTileCount > 0 && data.coreData.claimedTileCount == 0)
            {
                data.coreData.claimedTileCount = data.coreData.ownedTileCount;
                data.coreData.ownedTileCount = 0;
                Debug.Log($"[SaveMigrationRegistry] v2→v3 core: migrated {data.coreData.claimedTileCount} into claimedTileCount.");
            }
        };
    }

    /// <summary>
    /// Brings <paramref name="data"/> up to DungeonSaveData.CURRENT_VERSION by
    /// applying registered migrations in sequence. Returns true on success;
    /// false if a required migration step is missing.
    ///
    /// Saves predating the saveVersion field deserialize with saveVersion == 0;
    /// these are treated as an implicit v1 (schema is byte-identical) and
    /// stamped without invoking a migration delegate.
    /// </summary>
    public static bool MigrateToCurrent(DungeonSaveData data)
    {
        if (data == null) return false;

        if (data.saveVersion == 0)
        {
            data.saveVersion = 1;
            Debug.Log("[SaveMigrationRegistry] Save had no version stamp — treating as implicit v1.");
        }

        while (data.saveVersion < DungeonSaveData.CURRENT_VERSION)
        {
            int from = data.saveVersion;
            if (!migrations.TryGetValue(from, out var step))
            {
                Debug.LogError($"[SaveMigrationRegistry] No migration registered for v{from} → v{from + 1}. Cannot continue.");
                return false;
            }
            Debug.Log($"[SaveMigrationRegistry] Migrating save: v{from} → v{from + 1}.");
            step(data);
            data.saveVersion = from + 1;
        }
        return true;
    }
}