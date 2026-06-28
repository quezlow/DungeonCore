using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies RoomDefinition.effects to monsters housed in valid rooms on the active
/// floor. A spawner counts as "in" a room when its cell falls inside the room's
/// validated tile set; that spawner's live monster receives the room's effects.
///
/// v1 scope: active floor only (room validation + cell space are per-active-floor,
/// matching RoomValidator). Rooms on other floors resume when you switch to them.
///
/// SCENE SETUP: add to a persistent object (e.g. GameController). No wiring.
/// </summary>
public class RoomEffectController : MonoBehaviour
{
    [Tooltip("How often effects are applied, in seconds.")]
    [SerializeField, Min(0.05f)] private float tickInterval = 0.5f;

    private float timer;

    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<MonsterSpawner> spawnerBuf = new();
    private readonly List<DungeonMonster> inRoom = new();

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
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return;

        var influence = TileInfluenceManager.Instance;
        if (influence == null) return;

        floor.Entities.FillAll(roomBuf);
        if (roomBuf.Count == 0) return;

        floor.Entities.FillAll(spawnerBuf);

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
            if (inRoom.Count == 0) continue;

            for (int e = 0; e < def.effects.Count; e++)
            {
                var fx = def.effects[e];
                if (fx == null || fx.perSecond <= 0f) continue;
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
    }
}