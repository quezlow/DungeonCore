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
        // Future migrations go here, e.g.:
        //   migrations[1] = data => { /* convert v1 → v2 */ };
        //   migrations[2] = data => { /* convert v2 → v3 */ };
        //
        // Each delegate is responsible ONLY for the field-level transformations
        // needed for its step. The runner below handles incrementing saveVersion.
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