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

    [ContextMenu("Test Cycle Global Monster Aggression")]
    void TestCycleAggression()
    {
        int n = System.Enum.GetValues(typeof(MonsterAggression)).Length;
        MonsterAggressionSettings.Set((MonsterAggression)(((int)MonsterAggressionSettings.Global + 1) % n));
        Debug.Log($"[Commands] Global monster aggression = {MonsterAggressionSettings.Global}");
    }
}
