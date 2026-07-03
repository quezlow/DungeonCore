using UnityEngine;

public class Commands : MonoBehaviour
{
    [Header("Rogue Disarm Test")]
    [Tooltip("Assign the same TrapDefinitionRegistry used by DungeonSaveController.")]
    [SerializeField] private TrapDefinitionRegistry trapRegistry;
    [Tooltip("Name of the spike-trap definition to lay for the wall test.")]
    [SerializeField] private string spikeTrapName = "Spike Trap";
    [Tooltip("Where along the entrance->core approach to place the wall (0 = at core, 1 = at entrance).")]
    [SerializeField, Range(0.1f, 0.9f)] private float wallApproachFraction = 0.45f;

    [ContextMenu("Test Build No-Gap Trap Wall")]
    void TestBuildTrapWall()
    {
        var fm = FloorManager.Instance;
        var core = DungeonCore.Instance;
        if (fm == null || core == null) { Debug.LogWarning("[Commands] No FloorManager or DungeonCore in scene."); return; }
        if (trapRegistry == null) { Debug.LogWarning("[Commands] Assign a TrapDefinitionRegistry on the Commands component first."); return; }

        var spikeDef = trapRegistry.GetByName(spikeTrapName);
        if (spikeDef == null) { Debug.LogWarning($"[Commands] No trap definition named '{spikeTrapName}'."); return; }

        var floor = fm.GetFloor(fm.CoreFloorIndex);
        var influence = floor != null ? floor.TileInfluence : null;
        if (floor == null || influence == null) { Debug.LogWarning("[Commands] Core floor has no TileInfluence."); return; }

        Vector3 coreW = core.transform.position;
        Vector3 entW = DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : coreW + Vector3.down * 10f;
        Vector3 mid = Vector3.Lerp(coreW, entW, wallApproachFraction);

        Vector2 approach = (Vector2)(coreW - entW);
        if (approach.sqrMagnitude < 0.001f) approach = Vector2.up;
        approach.Normalize();
        Vector2 perp = new Vector2(-approach.y, approach.x);
        bool horizontal = Mathf.Abs(perp.x) >= Mathf.Abs(perp.y);
        Vector3Int step = horizontal ? new Vector3Int(1, 0, 0) : new Vector3Int(0, 1, 0);

        Vector3Int center = influence.WorldToCell(mid);
        int placed = LayIfWalkable(floor, influence, spikeDef, center);
        for (int dir = -1; dir <= 1; dir += 2)
        {
            for (int i = 1; i <= 12; i++)
            {
                Vector3Int cell = center + step * (dir * i);
                if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(cell))) break;
                placed += LayIfWalkable(floor, influence, spikeDef, cell);
            }
        }
        Debug.Log($"[Commands] Laid {placed} spike(s) as a no-gap wall centred on {center} ({(horizontal ? "horizontal" : "vertical")}). If 0, nudge wallApproachFraction.");
    }

    int LayIfWalkable(FloorRoot floor, TileInfluenceManager influence, TrapDefinition def, Vector3Int cell)
    {
        if (!DungeonPathfinder.IsWalkable(floor, influence.CellToWorld(cell))) return 0;
        if (floor.TrapRegistry != null && floor.TrapRegistry.GetTrapAt(cell) != null) return 0;
        DungeonBuildController.Instance.RestoreTrap(floor, def, cell, false, false);
        return 1;
    }

    [ContextMenu("Test Clear Traps On Core Floor")]
    void TestClearCoreFloorTraps()
    {
        var fm = FloorManager.Instance;
        if (fm == null) { Debug.LogWarning("[Commands] No FloorManager in scene."); return; }
        var floor = fm.GetFloor(fm.CoreFloorIndex);
        if (floor == null || floor.Entities == null) { Debug.LogWarning("[Commands] No core floor."); return; }
        var traps = floor.Entities.GetAll<TrapBase>();
        int n = 0;
        foreach (var t in traps) { if (t != null) { Destroy(t.gameObject); n++; } }
        Debug.Log($"[Commands] Destroyed {n} trap(s) on the core floor.");
    }

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
