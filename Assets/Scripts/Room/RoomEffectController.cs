using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies RoomDefinition.effects to the entities in valid rooms on the active floor.
///   - LairRegen / TrainingXp / MonsterDamageBuff act on monsters whose spawner sits in the room.
///   - CoreRetaliation zaps adventurers standing in the room (the Throne Room's defence), each
///     hit drawing a coloured pulse from the core to the attacker.
///
/// v1 scope: active floor only (room validation + cell space are per-active-floor).
///
/// SCENE SETUP: add to a persistent object (e.g. GameController). No wiring. Pair with a
/// CorePulse component somewhere for the retaliation pulse visual.
/// </summary>
public class RoomEffectController : MonoBehaviour
{
    [Tooltip("How often effects are applied, in seconds.")]
    [SerializeField, Min(0.05f)] private float tickInterval = 0.5f;

    [Tooltip("How often core retaliation deals damage + fires a pulse, in seconds.")]
    [SerializeField, Min(0.1f)] private float retaliationInterval = 0.35f;

    private float timer;
    private float lastRetaliationTime;
    private bool retaliateNow;

    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<MonsterSpawner> spawnerBuf = new();
    private readonly List<DungeonMonster> inRoom = new();
    private readonly List<DungeonAdventurer> advBuf = new();
    private readonly HashSet<DungeonMonster> buffedLast = new();
    private readonly HashSet<DungeonMonster> nowBuffed = new();

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        timer += Time.deltaTime;
        while (timer >= tickInterval)
        {
            ApplyTick(tickInterval);
            timer -= tickInterval;
        }
    }

    private void ApplyTick(float dt)
    {
        nowBuffed.Clear();

        var floor = FloorManager.Instance?.ActiveFloor;
        var influence = TileInfluenceManager.Instance;
        if (floor?.Entities == null || influence == null) { ClearStaleBuffs(); return; }

        floor.Entities.FillAll(roomBuf);
        if (roomBuf.Count == 0) { ClearStaleBuffs(); return; }

        floor.Entities.FillAll(spawnerBuf);
        retaliateNow = Time.time - lastRetaliationTime >= retaliationInterval;

        for (int r = 0; r < roomBuf.Count; r++)
        {
            var anchor = roomBuf[r];
            if (anchor == null || !anchor.IsValid) continue;

            var def = anchor.AssignedRoom;
            if (def == null || def.effects == null || def.effects.Count == 0) continue;

            var tiles = anchor.GetRoomTiles();
            if (tiles == null || tiles.Count == 0) continue;

            // Live monsters whose spawner sits in this room.
            inRoom.Clear();
            for (int s = 0; s < spawnerBuf.Count; s++)
            {
                var sp = spawnerBuf[s];
                if (sp == null || !sp.HasLiveMonster) continue;
                var cell = influence.WorldToCell(sp.transform.position);
                if (tiles.Contains(cell)) inRoom.Add(sp.SpawnedMonster);
            }

            for (int e = 0; e < def.effects.Count; e++)
            {
                var fx = def.effects[e];
                if (fx == null) continue;

                // Core retaliation acts on adventurers in the room (no monsters required).
                if (fx.type == RoomEffectType.CoreRetaliation)
                {
                    if (retaliateNow) ApplyRetaliation(floor, influence, tiles, fx, anchor.EffectScale);
                    continue;
                }

                // Damage buff sets a live multiplier on the room's monsters.
                if (fx.type == RoomEffectType.MonsterDamageBuff)
                {
                    float mult = Mathf.Max(1f, fx.perSecond);
                    for (int m = 0; m < inRoom.Count; m++)
                    {
                        var mon = inRoom[m];
                        if (mon == null) continue;
                        mon.SetRoomDamageMultiplier(mult);
                        nowBuffed.Add(mon);
                    }
                    continue;
                }

                // Per-second effects on the room's monsters.
                if (fx.perSecond <= 0f || inRoom.Count == 0) continue;
                float amount = fx.perSecond * dt * anchor.EffectScale;
                for (int m = 0; m < inRoom.Count; m++)
                {
                    var mon = inRoom[m];
                    if (mon == null) continue;
                    switch (fx.type)
                    {
                        case RoomEffectType.LairRegen: mon.Heal(amount); break;
                        case RoomEffectType.TrainingXp: mon.AddXP(amount); break;
                    }
                }
            }
        }

        ClearStaleBuffs();
        if (retaliateNow) lastRetaliationTime = Time.time;
    }

    private void ApplyRetaliation(FloorRoot floor, TileInfluenceManager influence,
                                  HashSet<Vector3Int> tiles, RoomEffect fx, float scale)
    {
        if (fx.perSecond <= 0f) return;
        var core = DungeonCore.Instance;
        if (core == null) return;

        float amount = fx.perSecond * retaliationInterval * scale;
        Vector3 corePos = core.transform.position;
        Color colour = core.CoreColor;

        floor.Entities.FillAll(advBuf);
        for (int a = 0; a < advBuf.Count; a++)
        {
            var adv = advBuf[a];
            if (adv == null) continue;
            if (IsPeacefulVisitor(adv)) continue;
            var cell = influence.WorldToCell(adv.transform.position);
            if (!tiles.Contains(cell)) continue;
            adv.TakeDamage(amount);
            CorePulse.Instance?.Fire(corePos, adv.transform.position, colour);
        }
    }

    /// <summary>
    /// Pilgrims and gift-bearers are spared unless the global stance is Aggressive - the same rule
    /// the monsters follow, so the core no longer zaps a worshipper mid-prayer.
    /// </summary>
    private static bool IsPeacefulVisitor(DungeonAdventurer adv)
    {
        if (MonsterAggressionSettings.Global == MonsterAggression.Aggressive) return false;
        return adv.Intent == PartyIntent.Pilgrim || adv.Intent == PartyIntent.GiftGiver;
    }

    // Damage buffs are transient: clear the multiplier on any monster no longer in a buffing room.
    private void ClearStaleBuffs()
    {
        foreach (var mon in buffedLast)
            if (mon != null && !nowBuffed.Contains(mon))
                mon.SetRoomDamageMultiplier(1f);
        buffedLast.Clear();
        foreach (var mon in nowBuffed) buffedLast.Add(mon);
    }
}