using UnityEngine;

public class Commaands : MonoBehaviour
{
    [ContextMenu("Test Add XP")]
    void TestXP() => DungeonCore.Instance.AddXP(50f);

    [ContextMenu("Test Add Mana")]
    void TestAddMana() => DungeonCore.Instance.AddMana(20f);

    [ContextMenu("Test Remove Mana")]
    void TestRemoveMana() => DungeonCore.Instance.AddMana(-20f);

    [ContextMenu("Test Add Notoriety")]
    void TestNotoriety() => DungeonCore.Instance.AddNotoriety(10f);
}
