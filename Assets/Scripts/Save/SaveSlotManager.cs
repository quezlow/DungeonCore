using System;
using UnityEngine;

/// <summary>
/// DAY 34 — Persistent (DontDestroyOnLoad) singleton that owns the currently
/// active save slot ID and any pending new-game payload (name + type) flowing
/// from the title screen into the gameplay scene.
///
/// ActiveSlotId is mirrored to PlayerPrefs (DCR.Slots.ActiveSlotId) so it
/// survives a full app restart — used by the Continue button.
///
/// PLACEMENT: Add this script to an empty GameObject "SaveSlotManager" in
/// the TitleScreen scene. It will persist into the gameplay scene.
/// </summary>
[DefaultExecutionOrder(-100)]
public class SaveSlotManager : MonoBehaviour
{
    public static SaveSlotManager Instance { get; private set; }

    private const string PREFS_ACTIVE_SLOT = "DCR.Slots.ActiveSlotId";

    /// <summary>Currently active slot ID (1..10), or 0 if none selected.</summary>
    public int ActiveSlotId { get; private set; }

    /// <summary>
    /// Pending new-game payload set when the player completes the new-game flow
    /// on the title screen. Consumed by DungeonCore.Awake (type) and
    /// DungeonSaveController.InitializeNewGame (name) on first scene load,
    /// then cleared.
    /// </summary>
    public PendingNewGameData PendingNewGame { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // DAY 34 — Migrate any legacy DungeonSaveData.json into slot 1 BEFORE
        // we scan slot metadata for the title screen. Idempotent — also called
        // from DungeonSaveController.Awake as a safety net for editor workflows
        // that launch straight into the gameplay scene.
        ExistingSaveMigrator.RunIfNeeded();

        ActiveSlotId = PlayerPrefs.GetInt(PREFS_ACTIVE_SLOT, 0);
        if (ActiveSlotId < SlotPaths.MIN_SLOT_ID || ActiveSlotId > SlotPaths.MAX_SLOT_ID)
            ActiveSlotId = 0;
    }

    public void SetActiveSlot(int slotId)
    {
        if (slotId < SlotPaths.MIN_SLOT_ID || slotId > SlotPaths.MAX_SLOT_ID)
        {
            Debug.LogError($"[SaveSlotManager] Invalid slot ID {slotId}");
            return;
        }
        ActiveSlotId = slotId;
        PlayerPrefs.SetInt(PREFS_ACTIVE_SLOT, slotId);
        PlayerPrefs.Save();
    }

    public void ClearActiveSlot()
    {
        ActiveSlotId = 0;
        PlayerPrefs.DeleteKey(PREFS_ACTIVE_SLOT);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Called by the title screen when starting a new game. Sets the active
    /// slot and stashes the pending name + type for DungeonCore + DungeonSaveController
    /// to read on scene load.
    /// </summary>
    public void BeginNewGame(int slotId, string dungeonName, DungeonType dungeonType)
    {
        SetActiveSlot(slotId);
        PendingNewGame = new PendingNewGameData
        {
            dungeonName = string.IsNullOrWhiteSpace(dungeonName)
                ? "Unnamed Dungeon"
                : dungeonName.Trim(),
            dungeonType = dungeonType
        };
    }

    public void ClearPendingNewGame() => PendingNewGame = null;
}

[Serializable]
public class PendingNewGameData
{
    public string dungeonName;
    public DungeonType dungeonType;
}