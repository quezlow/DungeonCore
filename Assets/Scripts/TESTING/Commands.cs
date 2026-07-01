using UnityEngine;

public class Commands : MonoBehaviour
{
    [ContextMenu("Test Add XP")]
    void TestXP() => DungeonCore.Instance.AddXP(50f);

    [ContextMenu("Test Add Lots of XP")]
    void TestLotsXP() => DungeonCore.Instance.AddXP(500f);

    [ContextMenu("Test Add So Much XP")]
    void TestSoMuchXP() => DungeonCore.Instance.AddXP(10000f);

    [ContextMenu("Test Add Mana")]
    void TestAddMana() => DungeonCore.Instance.AddMana(20f);

    [ContextMenu("Test Refill Mana")]
    void TestRefillMana() => DungeonCore.Instance.AddMana(20000f);

    [ContextMenu("Test Remove Mana")]
    void TestRemoveMana() => DungeonCore.Instance.AddMana(-20f);

    [ContextMenu("Test Add Notoriety")]
    void TestNotoriety() => DungeonCore.Instance.AddNotoriety(10f);

    [ContextMenu("Test Toggle Oracle Chamber Unlock")]
    void TestToggleOracle()
    {
        UnlockState.Toggle(UnlockState.OracleChamber);
        Debug.Log($"[Commands] Oracle Chamber unlocked = {UnlockState.IsUnlocked(UnlockState.OracleChamber)}");
    }

    [ContextMenu("Test Toggle Adventurer Stats Unlock")]
    void TestToggleAdventurerStats()
    {
        UnlockState.Toggle(UnlockState.AdventurerStats);
        Debug.Log($"[Commands] Adventurer Stats unlocked = {UnlockState.IsUnlocked(UnlockState.AdventurerStats)}");
    }

    [ContextMenu("Test Cycle Global Monster Aggression")]
    void TestCycleAggression()
    {
        int n = System.Enum.GetValues(typeof(MonsterAggression)).Length;
        MonsterAggressionSettings.Set((MonsterAggression)(((int)MonsterAggressionSettings.Global + 1) % n));
        Debug.Log($"[Commands] Global monster aggression = {MonsterAggressionSettings.Global}");
    }

    [ContextMenu("Test Force Pending Returns Due Now")]
    void TestForcePendingReturns()
    {
        var reg = TrackedPartyRegistry.Instance;
        if (reg == null) { Debug.Log("[Commands] No TrackedPartyRegistry in scene."); return; }
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        int n = 0;
        foreach (var p in reg.PendingParties) { p.returnDay = day; n++; }
        Debug.Log($"[Commands] {n} pending part(ies) marked due today (day {day}) — next party spawn deploys one.");
    }

    [ContextMenu("Test Grant Pending Survivors 400 XP")]
    void TestGrantPendingSurvivorXp()
    {
        var reg = TrackedPartyRegistry.Instance;
        if (reg == null) { Debug.Log("[Commands] No TrackedPartyRegistry in scene."); return; }
        int n = 0;
        foreach (var p in reg.PendingParties)
            foreach (var m in p.members)
                if (m.survived) { m.xp += 400; n++; }
        Debug.Log($"[Commands] Granted 400 XP to {n} pending survivor(s) — four levels at default tuning.");
    }

    [ContextMenu("Test Dispatch Hero Party")]
    void TestDispatchHero()
    {
        if (AdventurerSpawner.Instance == null) { Debug.Log("[Commands] No AdventurerSpawner in scene."); return; }
        AdventurerSpawner.Instance.DispatchHeroParty();
        Debug.Log("[Commands] Hero party dispatched.");
    }
}
