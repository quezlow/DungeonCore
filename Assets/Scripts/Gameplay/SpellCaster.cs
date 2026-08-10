using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves a cast. One arm per SpellEffect; every arm gathers over a radius
/// through FloorEntityRegistry.WithinRadius, the same call the burst traps
/// use, so spells and traps agree on what "in the blast" means.
///
/// LASH IS NOT A PROJECTILE. A travelling bolt was designed and dropped: a
/// projectile needs an origin, the only sensible origin is the core, and the
/// core is routinely hundreds of cells from the fight -- so
/// DungeonProjectile.HasLineOfSight would refuse nearly every cast and the
/// spell would read as broken. The core's will arrives where it is pointed.
/// It keeps the shared area shape as a result, which is the other reason.
///
/// WILDS TAKE THE WOUND BUT NOT THE BOON. Lash hits anything hostile in the
/// ring, wild monsters included (the fireball-rune ruling). Knit heals only
/// the dungeon's own -- healing the wilds fighting your monsters would be a
/// pure own-goal with no way to avoid it.
/// </summary>
public static class SpellCaster
{
    private static readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();
    private static readonly List<DungeonMonster> monBuf = new List<DungeonMonster>();
    private static readonly HashSet<MonsterSpawner> calledBuf = new HashSet<MonsterSpawner>();

    /// <summary>
    /// Applies the spell at a cell. The caller has already checked
    /// availability, cooldown, mana and cell validity -- this only resolves
    /// the effect and reports whether anything was actually done, so a cast
    /// that finds nothing can refund rather than charge for air.
    /// </summary>
    public static bool Resolve(SpellDefinition def, FloorRoot floor, Vector3Int cell)
    {
        if (def == null || floor == null || floor.Entities == null) return false;
        var influence = floor.TileInfluence;
        if (influence == null) return false;
        Vector3 at = influence.CellToWorld(cell);

        switch (def.effect)
        {
            case SpellDefinition.SpellEffect.Lash: return Lash(def, floor, at);
            case SpellDefinition.SpellEffect.Knit: return Knit(def, floor, at);
            case SpellDefinition.SpellEffect.Rally: return Rally(def, floor, cell, at);
            case SpellDefinition.SpellEffect.BoonDamage:
                return Boon(def, floor, at, MonsterBoons.BoonKind.Damage);
            case SpellDefinition.SpellEffect.BoonHaste:
                return Boon(def, floor, at, MonsterBoons.BoonKind.Speed);
            case SpellDefinition.SpellEffect.BoonArmour:
                return Boon(def, floor, at, MonsterBoons.BoonKind.DamageTaken);
            case SpellDefinition.SpellEffect.Pull: return Pull(def, floor, at);
            case SpellDefinition.SpellEffect.Rout: return Rout(def, floor, at);
            case SpellDefinition.SpellEffect.Vulnerable: return Vulnerable(def, floor, at);
            default: return false;
        }
    }

    // -- Lash ----------------------------------------------------------------

    private static bool Lash(SpellDefinition def, FloorRoot floor, Vector3 at)
    {
        int struck = 0;
        float dmg = def.magnitude;

        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), advBuf);
        for (int i = 0; i < advBuf.Count; i++)
        {
            var a = advBuf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            DamageNumberSpawner.Spawn(dmg, a.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            // Hurl before the wound: a killing blow must not shove a destroyed
            // component, the ordering the fireball rune learned the hard way.
            if (def.secondary > 0f)
                ((IMonsterTarget)a).ApplyKnockback(at, def.secondary);
            a.TakeDamage(dmg);
            struck++;
        }

        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), monBuf, x => x.IsWild);
        for (int i = 0; i < monBuf.Count; i++)
        {
            var m = monBuf[i];
            if (m == null) continue;
            DamageNumberSpawner.Spawn(dmg, m.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            if (def.secondary > 0f)
                ((IMonsterTarget)m).ApplyKnockback(at, def.secondary);
            m.TakeDamage(dmg);
            struck++;
        }

        return struck > 0;
    }

    // -- Knit ----------------------------------------------------------------

    private static bool Knit(SpellDefinition def, FloorRoot floor, Vector3 at)
    {
        int healed = 0;
        // The dungeon's own only. A wild in the ring is very often the thing
        // your monsters are fighting.
        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), monBuf, x => !x.IsWild);
        for (int i = 0; i < monBuf.Count; i++)
        {
            var m = monBuf[i];
            if (m == null) continue;
            if (m.CurrentHP >= m.MaxHP) continue;   // nothing to give, nothing to charge for
            m.Heal(def.magnitude);
            healed++;
        }
        return healed > 0;
    }

    // -- Boons: Fire, Earth, Air ---------------------------------------------

    /// <summary>Lays a timed multiplier on the dungeon's own inside the ring.
    /// Wilds are excluded: they are not yours to strengthen, and half of them
    /// are what your monsters are currently fighting.</summary>
    private static bool Boon(SpellDefinition def, FloorRoot floor, Vector3 at,
                             MonsterBoons.BoonKind kind)
    {
        int touched = 0;
        float seconds = SpellBook.EffectiveDuration(def);
        if (seconds <= 0f) return false;

        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), monBuf, x => !x.IsWild);
        for (int i = 0; i < monBuf.Count; i++)
        {
            var m = monBuf[i];
            if (m == null) continue;
            m.EnsureBoons().Grant(kind, def.magnitude, seconds);
            touched++;
        }
        return touched > 0;
    }

    // -- Pull (Water) ---------------------------------------------------------

    /// <summary>Undertow. A pull needs no new primitive: ApplyKnockback shoves a
    /// body AWAY from a point, so shoving it away from a point mirrored across
    /// the cast cell drags it toward the cell instead. The force is clamped to
    /// the distance so nothing is flung out the far side of the mark.</summary>
    private static bool Pull(SpellDefinition def, FloorRoot floor, Vector3 at)
    {
        int pulled = 0;
        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), advBuf);
        for (int i = 0; i < advBuf.Count; i++)
        {
            var a = advBuf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            Vector2 pos = a.transform.position;
            Vector2 mark = at;
            float dist = Vector2.Distance(pos, mark);
            if (dist < 0.05f) continue;              // already on the mark
            Vector2 mirrored = pos + (pos - mark);
            ((IMonsterTarget)a).ApplyKnockback(mirrored, Mathf.Min(def.secondary, dist));
            pulled++;
        }
        return pulled > 0;
    }

    // -- Rout (Dark) ----------------------------------------------------------

    /// <summary>Terror. Everything that breaks leaves ALIVE, and that is the
    /// price of the working, not a flaw in it: no kill notoriety, their loot
    /// walks out with them, and the alignment shift for one that left alive is
    /// applied by the exit path as usual. ForceRetreat refuses the Suicidal and
    /// the Pinned and says so by returning false, so a cast that finds only
    /// those is billed for nothing.</summary>
    private static bool Rout(SpellDefinition def, FloorRoot floor, Vector3 at)
    {
        int broken = 0;
        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), advBuf);
        for (int i = 0; i < advBuf.Count; i++)
        {
            var a = advBuf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            if (a.ForceRetreat()) broken++;
        }
        return broken > 0;
    }

    // -- Vulnerable (Light) ---------------------------------------------------

    /// <summary>The Buried Sun. Marks bodies rather than amplifying a source, so
    /// traps, chests, monsters and the core all land harder on a marked body for
    /// the duration.</summary>
    private static bool Vulnerable(SpellDefinition def, FloorRoot floor, Vector3 at)
    {
        int marked = 0;
        float seconds = SpellBook.EffectiveDuration(def);
        if (seconds <= 0f) return false;

        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), advBuf);
        for (int i = 0; i < advBuf.Count; i++)
        {
            var a = advBuf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            a.ApplyVulnerable(def.magnitude, seconds);
            marked++;
        }
        return marked > 0;
    }

    // -- Rally ---------------------------------------------------------------

    private static bool Rally(SpellDefinition def, FloorRoot floor, Vector3Int cell, Vector3 at)
    {
        int called = 0;
        calledBuf.Clear();

        // Gathered by MONSTER, not by spawner. The spawner is a fixed point the
        // monster may be a long way from -- a garrison that has wandered two
        // rooms over is exactly the garrison this spell exists to call back, and
        // gathering by spawner would miss it while catching an empty muster room
        // whose occupant is elsewhere. Wilds are not yours to command.
        floor.Entities.WithinRadius(at, SpellBook.EffectiveRadius(def), monBuf, x => !x.IsWild);
        for (int i = 0; i < monBuf.Count; i++)
        {
            var m = monBuf[i];
            if (m == null) continue;
            var s = m.Spawner;
            if (s == null) continue;              // invaders and climax spawns have none
            if (!calledBuf.Add(s)) continue;      // several of a kind share one spawner
            // Exactly the call the right-click Attack-Here order makes, so a
            // rallied monster reverts to its underlying order mode on arrival
            // through the shipped ClearAttackTarget path. No new order state.
            s.SetAttackTarget(cell);
            called++;
        }
        return called > 0;
    }
}
