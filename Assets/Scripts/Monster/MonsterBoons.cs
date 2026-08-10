using UnityEngine;

/// <summary>
/// Transient boons a core spell lays on one of its own monsters: sharper
/// strikes, quicker feet, tougher hide. Added on demand by SpellCaster and
/// never by hand.
///
/// NO Update, DELIBERATELY. Each boon is a multiplier plus an expiry stamp,
/// and the getters compare against Time.time when read -- so an expired boon
/// costs nothing, a dungeon full of boons costs nothing per frame, and there
/// is no tick to fall out of step with the clock. The component simply sits
/// inert once its stamps pass.
///
/// TRANSIENT, like every other affliction and the spell cooldowns themselves
/// (the section-30 precedent): none of this survives a save. A boon is a few
/// seconds of a fight, and a fight does not survive a save either.
///
/// RECASTING REFRESHES, IT DOES NOT STACK. Grant takes the stronger multiplier
/// and the later expiry rather than multiplying them together. Stacking would
/// make spamming one spell into the correct play at high mana, which is the
/// opposite of what the cooldowns exist to prevent.
///
/// Read as one more factor in the chains that already exist on DungeonMonster:
/// attackDamage * roomDamage * globalDamage * crowdDamage * mastery * BOON.
/// </summary>
public class MonsterBoons : MonoBehaviour
{
    private float damageMult = 1f, damageUntil = -999f;
    private float speedMult = 1f, speedUntil = -999f;
    private float takenMult = 1f, takenUntil = -999f;

    public enum BoonKind
    {
        Damage = 0,       // strikes harder      (Fire)
        Speed = 1,        // moves and swings faster (Air)
        DamageTaken = 2,  // takes less          (Earth)
    }

    /// <summary>Multiplier on outgoing damage; 1 when nothing is running.</summary>
    public float DamageMultiplier => Time.time < damageUntil ? damageMult : 1f;

    /// <summary>Multiplier on move speed; 1 when nothing is running.</summary>
    public float SpeedMultiplier => Time.time < speedUntil ? speedMult : 1f;

    /// <summary>Multiplier on incoming damage; 1 when nothing is running.
    /// Below 1 is armour, above 1 is vulnerability.</summary>
    public float DamageTakenMultiplier => Time.time < takenUntil ? takenMult : 1f;

    /// <summary>True while any boon is live -- for the picker, tooltips and the
    /// Print Spell State report.</summary>
    public bool AnyActive =>
        Time.time < damageUntil || Time.time < speedUntil || Time.time < takenUntil;

    public void Grant(BoonKind kind, float multiplier, float seconds)
    {
        if (seconds <= 0f) return;
        float until = Time.time + seconds;
        switch (kind)
        {
            case BoonKind.Damage:
                // Stronger multiplier and later expiry, taken independently:
                // a long weak boon must not be shortened by a brief strong one.
                damageMult = Mathf.Max(damageMult >= 1f ? damageMult : 1f, multiplier);
                damageUntil = Mathf.Max(damageUntil, until);
                break;
            case BoonKind.Speed:
                speedMult = Mathf.Max(speedMult >= 1f ? speedMult : 1f, multiplier);
                speedUntil = Mathf.Max(speedUntil, until);
                break;
            case BoonKind.DamageTaken:
                // Lower is better here, so "stronger" is the smaller number.
                takenMult = Time.time < takenUntil ? Mathf.Min(takenMult, multiplier) : multiplier;
                takenUntil = Mathf.Max(takenUntil, until);
                break;
        }
    }
}
