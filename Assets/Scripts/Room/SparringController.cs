using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the Arena's active lane: idle monsters whose spawners sit in a valid
/// SparringXp room are paired off and trade blows on a fixed cadence. Each
/// exchange plays the attacker's attack animation, deals real chip damage to
/// the defender (never striking below the HP floor fraction) and grants XP to
/// both sparrers. Bruises persist by design -- a sparring monster enters real
/// combat with whatever it has left.
///
/// Active floor only, matching the per-second entity lane. Never touches the
/// monster AI state machine: a monster that acquires a target, wanders off or
/// runs too low simply stops being eligible and the bout ends. Facing flips
/// are deferred (sprite flip interacts with veteran scale multipliers).
///
/// SCENE SETUP: add beside RoomEffectController on the managers object. No wiring.
/// </summary>
public class SparringController : MonoBehaviour
{
    [Tooltip("Seconds between exchanges within a bout.")]
    [SerializeField, Min(0.25f)] private float exchangeInterval = 1.5f;

    [Tooltip("Exchanges per bout before the pair rests.")]
    [SerializeField, Min(1)] private int exchangesPerBout = 6;

    [Tooltip("Rest seconds for a pair after a bout ends.")]
    [SerializeField, Min(0f)] private float pairCooldown = 10f;

    [Tooltip("No sparrer is ever struck below this fraction of max HP.")]
    [SerializeField, Range(0.05f, 0.9f)] private float hpFloorFraction = 0.3f;

    [Tooltip("Chip damage per exchange, multiplied by room tier.")]
    [SerializeField, Min(0f)] private float chipDamagePerExchange = 2f;

    private float timer;
    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<MonsterSpawner> spawnerBuf = new();
    private readonly List<DungeonMonster> ready = new();
    private readonly Dictionary<long, BoutState> bouts = new();
    private readonly List<long> stale = new();

    private class BoutState
    {
        public int exchangesDone;
        public float restUntil;
        public float lastTouched;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        timer += Time.deltaTime;
        if (timer < exchangeInterval) return;
        timer -= exchangeInterval;
        Tick();
    }

    private void Tick()
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        var influence = TileInfluenceManager.Instance;
        if (floor?.Entities == null || influence == null) { Cull(); return; }

        floor.Entities.FillAll(roomBuf);
        if (roomBuf.Count == 0) { Cull(); return; }
        floor.Entities.FillAll(spawnerBuf);

        for (int r = 0; r < roomBuf.Count; r++)
        {
            var anchor = roomBuf[r];
            if (anchor == null || !anchor.IsValid) continue;

            var def = anchor.AssignedRoom;
            if (def == null || def.effects == null) continue;

            // First SparringXp entry on the definition sets this room's rate.
            RoomEffect spar = null;
            for (int e = 0; e < def.effects.Count; e++)
            {
                var f = def.effects[e];
                if (f != null && f.type == RoomEffectType.SparringXp && f.perSecond > 0f) { spar = f; break; }
            }
            if (spar == null) continue;

            var tiles = anchor.GetRoomTiles();
            if (tiles == null || tiles.Count == 0) continue;

            float scale = anchor.EffectScale;
            float chip = chipDamagePerExchange * scale;
            float xpPerExchange = spar.perSecond * scale * exchangeInterval;

            GatherReady(influence, tiles, chip);
            for (int i = 0; i + 1 < ready.Count; i += 2)
                RunExchange(ready[i], ready[i + 1], chip, xpPerExchange);
        }

        Cull();
    }

    /// <summary>Spar-ready monsters whose spawner stands in the room, with HP headroom.</summary>
    private void GatherReady(TileInfluenceManager influence, HashSet<Vector3Int> tiles, float chip)
    {
        ready.Clear();
        for (int s = 0; s < spawnerBuf.Count; s++)
        {
            var sp = spawnerBuf[s];
            if (sp == null || !sp.HasLiveMonster) continue;
            var cell = influence.WorldToCell(sp.transform.position);
            if (!tiles.Contains(cell)) continue;

            var mon = sp.SpawnedMonster;
            if (mon == null || !mon.IsSparReady) continue;
            if (mon.CurrentHP - chip < mon.MaxHP * hpFloorFraction) continue;
            ready.Add(mon);
        }
    }

    private void RunExchange(DungeonMonster a, DungeonMonster b, float chip, float xpPerExchange)
    {
        // GetHashCode on UnityEngine.Object returns the instance id and is
        // not deprecated -- same per-session pair identity, no EntityId churn.
        long key = PairKey(a.GetHashCode(), b.GetHashCode());
        if (!bouts.TryGetValue(key, out var bout))
        {
            bout = new BoutState();
            bouts[key] = bout;
        }
        bout.lastTouched = Time.time;
        if (Time.time < bout.restUntil) return;

        var attacker = (bout.exchangesDone % 2 == 0) ? a : b;
        var defender = (attacker == a) ? b : a;

        attacker.GetComponent<EntityAnimationDriver>()?.OnAttack();
        defender.TakeDamage(chip);   // fires the hurt animation itself
        a.AddXP(xpPerExchange);
        b.AddXP(xpPerExchange);

        bout.exchangesDone++;
        if (bout.exchangesDone >= exchangesPerBout)
        {
            bout.exchangesDone = 0;
            bout.restUntil = Time.time + pairCooldown;
        }
    }

    /// <summary>Order-independent pair key from two instance ids.</summary>
    private static long PairKey(int idA, int idB)
        => idA < idB ? ((long)idA << 32) | (uint)idB : ((long)idB << 32) | (uint)idA;

    /// <summary>Drops bout entries nobody has touched for a while (left room, died, floor switch).</summary>
    private void Cull()
    {
        stale.Clear();
        foreach (var kvp in bouts)
            if (Time.time - kvp.Value.lastTouched > pairCooldown + 30f) stale.Add(kvp.Key);
        for (int i = 0; i < stale.Count; i++) bouts.Remove(stale[i]);
    }
}