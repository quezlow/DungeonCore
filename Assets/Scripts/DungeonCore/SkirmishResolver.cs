using UnityEngine;

/// <summary>
/// Who wins when a kobold digging party breaks onto the dwarven road (canon 42's
/// road breach; canon 44's fourth side).
///
/// PURE STATIC, ON DeepRoadGraph.SeatPatrol's PRECEDENT AND FOR ITS REASON. The
/// den resolves an UNWATCHED breach with this and Road Breach Report measures
/// the beat with it, so the outcome the readout reports and the outcome the game
/// applies cannot be two different rules. Canon 44 already records what the
/// alternative costs: a report that restates the thing it measures confirms
/// itself and nothing else.
///
/// NOTHING HERE IS AUTHORED. Both stat blocks arrive as the PREFABS the game
/// instantiates -- the guard's through DwarvenPatrolController.GuardDefinition,
/// the kobold's through the den profile's scavengerDefinition -- so retuning
/// either moves this with it. That is the whole point: canon 44 sized the gate
/// squad at one body on the assertion that a lone guard loses to four kobolds,
/// and that assertion related two independently serialised fields and was
/// therefore checkable from neither one alone. It was wrong.
///
/// THE MODEL IS WHAT DungeonMonster ACTUALLY DOES, and each simplification is a
/// fact about the two prefabs rather than a convenience:
///
///   - Instantaneous damage on the cooldown. Neither prefab carries a ranged
///     definition, so there is no telegraph windup to model.
///   - Focus fire on both sides. Bodies converge on a nearest target and the
///     fight is a handful of bodies in a tunnel mouth.
///   - No regeneration. It is gated behind regenCooldown seconds since the last
///     wound and these fights resolve in under ten.
///   - THE KOBOLDS STRIKE FIRST, because their prefab authors the longer
///     detectionRange and ScanForHostiles acquires on range alone. That is the
///     pessimistic branch for the road, so a gate that holds here holds in play.
/// </summary>
public static class SkirmishResolver
{
    /// <summary>True when the den takes the road.
    ///
    /// A STALEMATE IS THE ROAD HOLDING. The guard is replaced at the next dawn
    /// and the party withdrawn at it, so a fight neither side can finish leaves
    /// nothing behind -- which is the road winning by the only measure that
    /// persists.</summary>
    public static bool TakesTheRoad(DungeonMonster guardPrefab, DungeonMonster koboldPrefab,
                                    int guards, int kobolds)
    {
        if (guardPrefab == null || koboldPrefab == null) return false;
        if (guards <= 0) return true;
        if (kobolds <= 0) return false;

        float gMax = Mathf.Max(1f, guardPrefab.MaxHP);
        float gHit = Mathf.Max(0f, guardPrefab.AttackDamage);
        float gCd = Mathf.Max(0.05f, guardPrefab.AttackCooldown);
        float kMax = Mathf.Max(1f, koboldPrefab.MaxHP);
        float kHit = Mathf.Max(0f, koboldPrefab.AttackDamage);
        float kCd = Mathf.Max(0.05f, koboldPrefab.AttackCooldown);
        if (gHit <= 0f && kHit <= 0f) return false;

        var gHp = new float[guards];
        var kHp = new float[kobolds];
        var gNext = new float[guards];
        var kNext = new float[kobolds];
        for (int i = 0; i < guards; i++) { gHp[i] = gMax; gNext[i] = gCd * 0.5f; }
        for (int i = 0; i < kobolds; i++) { kHp[i] = kMax; kNext[i] = 0f; }

        const float Dt = 0.02f;
        const float Limit = 600f;
        for (float t = 0f; t < Limit; t += Dt)
        {
            if (FirstAlive(gHp) < 0) return true;
            if (FirstAlive(kHp) < 0) return false;

            for (int i = 0; i < guards; i++)
            {
                if (gHp[i] <= 0f || t < gNext[i]) continue;
                int hit = FirstAlive(kHp);
                if (hit < 0) return false;
                kHp[hit] -= gHit;
                gNext[i] = t + gCd;
            }
            for (int i = 0; i < kobolds; i++)
            {
                if (kHp[i] <= 0f || t < kNext[i]) continue;
                int hit = FirstAlive(gHp);
                if (hit < 0) return true;
                gHp[hit] -= kHit;
                kNext[i] = t + kCd;
            }
        }
        return false;
    }

    private static int FirstAlive(float[] hp)
    {
        for (int i = 0; i < hp.Length; i++) if (hp[i] > 0f) return i;
        return -1;
    }
}
