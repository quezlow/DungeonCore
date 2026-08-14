using UnityEngine;

/// <summary>
/// Crossbow Trap -- a sentry, not a snare. It watches its span of hall and
/// looses a real bolt (DungeonProjectile) at the nearest intruder it has a
/// clear line to, on its own cooldown. Bolts land as DamageKind.Ranged inside
/// the projectile, so a marching shield wall mitigates them -- the Scatter
/// Trap is the opener that makes the crossbow bite. Adventurers first; with
/// none in view it will pick off a wild monster that wanders across its line,
/// the pressure-plate precedent.
///
/// Never cell-triggered: walking its cell is safe, so a flagged crossbow adds
/// no detour cost (detoursWhenFlagged off) -- flagging only opens the Rogue's
/// disarm answer. A flagged crossbow keeps shooting until disarmed: awareness
/// buys avoidance, not immunity, and a sentry cannot be avoided by pathing.
/// </summary>
public class CrossbowTrap : TrapBase
{
    // Base keeps its own trigger clock private; a sentry fires on its own.
    private float lastShotTime = -999f;

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Definition == null || IsDisarmed || !TrapsArmed) return;
        if (Time.time - lastShotTime < Definition.cooldown * TrapMastery.CooldownMultiplier) return;

        var floor = GetComponentInParent<FloorRoot>();
        if (floor?.Entities == null) return;

        var adv = floor.Entities.Nearest<DungeonAdventurer>(
            transform.position, Definition.sentryRange,
            a => ((IMonsterTarget)a).IsAlive
                 && DungeonProjectile.HasLineOfSight(floor, transform.position, a.transform.position));
        if (adv != null) { Loose(floor, adv); return; }

        var wild = floor.Entities.Nearest<DungeonMonster>(
            transform.position, Definition.sentryRange,
            x => !x.ServesDungeon && ((IMonsterTarget)x).IsAlive
                 && DungeonProjectile.HasLineOfSight(floor, transform.position, x.transform.position));
        if (wild != null) Loose(floor, wild);
    }

    private void Loose(FloorRoot floor, IMonsterTarget target)
    {
        lastShotTime = Time.time;
        DungeonProjectile.Fire(floor, transform.position, target,
            Definition.projectileSpeed, Definition.projectileTint, null,
            new DungeonProjectile.Payload
            {
                damage = ScaledDamage,
                numberType = FloatingDamageNumber.DamageType.AdventurerHit,
                sourceName = "",
            });
    }

    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        // A sentry is never cell-triggered.
    }
}
