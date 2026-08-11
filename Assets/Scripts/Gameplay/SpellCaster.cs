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
    private static readonly List<Vector3Int> summonCells = new List<Vector3Int>();
    private static readonly List<Vector3Int> excavateCells = new List<Vector3Int>();

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
            case SpellDefinition.SpellEffect.Summon: return Summon(def, floor, cell, at);
            case SpellDefinition.SpellEffect.Excavate: return Excavate(def, floor, cell);
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

    // -- Summon (Fire, charge only) -------------------------------------------

    /// <summary>
    /// Kethra's thralls. TRANSIENT spawners, and that is not a shortcut -- it is
    /// the only shape that works. MonsterSpawner.TickRespawn refuses outright
    /// while a threshold-crossed adventurer walks the floor
    /// (FloorIntrusion.AnyOnFloor), so a working that hastened respawns would do
    /// precisely nothing in the one situation anybody would ever cast it in.
    ///
    /// A transient holds no capacity, never respawns, self-destructs with its
    /// monster and is skipped by DungeonSaveController -- which is exactly the
    /// contract a summon wants, and it is why a summon cannot be saved mid-life.
    /// A save taken with thralls on the board reloads without them, the same
    /// ruling section 30 makes for a bolt in flight.
    ///
    /// The shared four dials carry it: magnitude is HOW MANY, durationSeconds is
    /// HOW LONG. Both are read through SpellBook, so a Fire core gets the god's
    /// full measure and a borrowed cast gets 0.6 of the reach and the hold.
    /// </summary>
    private static bool Summon(SpellDefinition def, FloorRoot floor, Vector3Int cell, Vector3 at)
    {
        var body = def.summonDefinition;
        if (body == null)
        {
            Debug.LogWarning("[Spells] '" + def.id + "' resolves as Summon with no "
                + "summonDefinition assigned. Nothing can be raised, so the cast is "
                + "refused rather than billed.");
            return false;
        }

        var build = DungeonBuildController.Instance;
        var influence = floor.TileInfluence;
        if (build == null || influence == null) return false;

        int want = Mathf.Max(1, Mathf.RoundToInt(def.magnitude));
        float life = SpellBook.EffectiveDuration(def);
        if (life <= 0f) return false;   // a thrall with no lifetime is a leak, not a summon

        // Scattered across STANDABLE ground inside the ring, one to a cell, falling
        // back to the cast cell when the ring offers nothing (canon 41). The
        // pathfinder's own rule is the only honest test of where a body may go --
        // mirroring it here would drift the day somebody edits the overhang case --
        // and a stack of bodies on a single cell reads as a bug even when it is not.
        float reach = SpellBook.EffectiveRadius(def);
        int span = Mathf.Max(0, Mathf.CeilToInt(reach));
        summonCells.Clear();
        for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            {
                var c = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                Vector3 w = influence.CellToWorld(c);
                if (Vector2.Distance(w, at) > reach) continue;
                if (!DungeonPathfinder.IsWalkable(floor, w)) continue;
                summonCells.Add(c);
            }

        int raised = 0;
        for (int i = 0; i < want; i++)
        {
            Vector3Int spot = cell;
            if (summonCells.Count > 0)
            {
                int pick = Random.Range(0, summonCells.Count);
                spot = summonCells[pick];
                summonCells.RemoveAt(pick);
            }
            if (build.SpawnTransientMinion(floor, body, spot, life) != null) raised++;
        }
        summonCells.Clear();
        return raised > 0;
    }

    // -- Excavate (neutral, charge only) --------------------------------------

    /// <summary>
    /// The dwarven setting charge. Opens CLAIMED, unmined stone inside the ring and
    /// nothing else: it is a faster shovel, never a land grab, so influence still
    /// has to be pushed there first and the ring still costs what it costs.
    ///
    /// IT ITERATES UNTIL NOTHING MOVES. TileInfluenceManager.MineTile enforces a
    /// FRONTIER rule -- a cell yields only when it is orthogonally next to already
    /// carved ground, or to a river, which counts as open. A single pass over the
    /// ring would open the rim nearest the existing dig and leave everything behind
    /// it standing, so each pass re-runs while the pass before it moved anything.
    ///
    /// THE FRONTIER TEST IS MIRRORED HERE rather than left to MineTile, for the same
    /// reason DungeonBuildController.CanMineCell mirrors it: MineTile BARKS "the
    /// stone will not yield there" on a refusal, and a working that just blew a hole
    /// in the wall must not also scold the player about the cells outside the
    /// frontier that it correctly declined to touch.
    ///
    /// Every opened cell runs the normal mined path, so holy ground still unseals
    /// and dwarven spoil still accrues. Buying setting charges from the Deep Holds
    /// and then owing them more spoil for using them is not an accident.
    /// </summary>
    private static bool Excavate(SpellDefinition def, FloorRoot floor, Vector3Int cell)
    {
        var influence = floor.TileInfluence;
        if (influence == null) return false;
        var features = floor.FeatureGenerator;

        float reach = SpellBook.EffectiveRadius(def);
        int span = Mathf.Max(0, Mathf.CeilToInt(reach));
        Vector3 centre = influence.CellToWorld(cell);

        excavateCells.Clear();
        for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            {
                var c = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                if (Vector2.Distance(influence.CellToWorld(c), centre) > reach) continue;
                if (!influence.IsTileClaimed(c)) continue;      // never a land grab
                if (influence.IsTileMined(c)) continue;
                if (features != null && features.IsRiver(c)) continue;   // water is not dug
                excavateCells.Add(c);
            }

        int opened = 0;
        bool moved = true;
        while (moved && excavateCells.Count > 0)
        {
            moved = false;
            for (int i = excavateCells.Count - 1; i >= 0; i--)
            {
                var c = excavateCells[i];
                if (!HasOpenNeighbour(influence, features, c)) continue;
                influence.MineTile(c);
                // Trust the ledger, not the call: MineTile returns void and has
                // refusals of its own (bounds, uncleared chamber). If the cell did
                // not open, leave it in the list and let a later pass try again.
                if (!influence.IsTileMined(c)) continue;
                excavateCells.RemoveAt(i);
                opened++;
                moved = true;
            }
        }
        excavateCells.Clear();
        return opened > 0;
    }

    /// <summary>MineTile's frontier rule, mirrored so a correct refusal never barks.</summary>
    private static bool HasOpenNeighbour(TileInfluenceManager influence,
                                         TerrainFeatureGenerator features, Vector3Int c)
    {
        for (int i = 0; i < Orthogonal.Length; i++)
        {
            Vector3Int n = c + Orthogonal[i];
            if (influence.IsTileMined(n)) return true;
            if (features != null && features.IsRiver(n)) return true;
        }
        return false;
    }

    private static readonly Vector3Int[] Orthogonal =
        { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
}
