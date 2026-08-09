using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient banter. On a gentle global cadence it picks a random idle speaker on the active floor -
/// an adventurer (solo mutter, or a two-line exchange with a nearby party-mate) or, less often, a
/// monster (a solo growl). Never fires in combat; pauses with the game. Attach to a persistent
/// object and pair with a BarkSpawner. Combat taunts and moment-reactions are fired by the
/// entities themselves - separate from this ambient loop.
/// </summary>
public class AdventurerBanterManager : MonoBehaviour
{
    [SerializeField, Min(1f)] private float minInterval = 6f;
    [SerializeField, Min(1f)] private float maxInterval = 11f;
    [SerializeField, Range(0f, 1f)] private float pairChance = 0.4f;         // chance an adventurer bark is a 2-line exchange
    [SerializeField, Range(0f, 1f)] private float monsterBarkWeight = 0.25f;  // chance a given bark is a monster growl
    [SerializeField] private float pairMateRange = 3.5f;
    [SerializeField] private float pairResponseDelay = 1.4f;

    private float nextTime;
    private readonly List<DungeonAdventurer> advBuf = new();
    private readonly List<DungeonMonster> monBuf = new();
    private readonly List<DungeonAdventurer> advEligible = new();
    private readonly List<DungeonMonster> monEligible = new();

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Time.time < nextTime) return;
        nextTime = Time.time + Random.Range(minInterval, maxInterval);
        TrySpeak();
    }

    private void TrySpeak()
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return;

        advBuf.Clear(); monBuf.Clear();
        floor.Entities.FillAll(advBuf);
        floor.Entities.FillAll(monBuf);

        advEligible.Clear();
        for (int i = 0; i < advBuf.Count; i++)
            if (advBuf[i] != null && advBuf[i].CanBanter) advEligible.Add(advBuf[i]);
        monEligible.Clear();
        for (int i = 0; i < monBuf.Count; i++)
            if (monBuf[i] != null && monBuf[i].CanBanter) monEligible.Add(monBuf[i]);

        bool haveAdv = advEligible.Count > 0;
        bool haveMon = monEligible.Count > 0;
        if (!haveAdv && !haveMon) return;

        // Monsters are the rarer seasoning; adventurers do the bulk of the chatter.
        bool monster = haveMon && (!haveAdv || Random.value < monsterBarkWeight);
        if (monster)
        {
            var growler = monEligible[Random.Range(0, monEligible.Count)];
            growler.Say(BanterLines.RandomGrowl(growler.Voice), BanterLines.MonsterBark);
            return;
        }

        var speaker = advEligible[Random.Range(0, advEligible.Count)];

        // A party stood at a sealed way talks about the seal, not the weather
        // (canon 36). Same cadence and pair odds as ordinary banter -- only
        // the pools change, so the loiter reads as the same people in a
        // different mood rather than a scripted announcement.
        if (speaker.SealLoitering)
        {
            if (Random.value < pairChance)
            {
                var sealMate = FindMate(speaker);
                if (sealMate != null)
                {
                    var blockedPair = BanterLines.RandomBlockedPair();
                    if (blockedPair != null && blockedPair.Length >= 2)
                    {
                        speaker.Say(blockedPair[0], BanterLines.Banter);
                        StartCoroutine(Reply(sealMate, blockedPair[1]));
                        return;
                    }
                }
            }
            speaker.Say(BanterLines.Pick(BanterLines.Blocked), BanterLines.Banter);
            return;
        }

        // Two-line exchange with a nearby party-mate.
        if (Random.value < pairChance)
        {
            var mate = FindMate(speaker);
            if (mate != null)
            {
                var pair = BanterLines.RandomPair();
                if (pair != null && pair.Length >= 2)
                {
                    speaker.Say(pair[0], BanterLines.Banter);
                    StartCoroutine(Reply(mate, pair[1]));
                    return;
                }
            }
        }

        // Solo line: a rare egg, or a faction/generic mutter.
        bool egg = Random.value < BanterLines.RareEggChance;
        string line = egg
            ? BanterLines.Pick(BanterLines.RareEggs)
            : BanterLines.RandomSolo(AdventurerTypeInfo.FactionOf(speaker.Type));
        speaker.Say(line, egg ? BanterLines.Egg : BanterLines.Banter);
    }

    private DungeonAdventurer FindMate(DungeonAdventurer speaker)
    {
        var party = speaker.Party;
        if (party == null) return null;
        var members = party.LiveMembers;
        DungeonAdventurer best = null;
        float bestSq = pairMateRange * pairMateRange;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            if (m == null || m == speaker || !m.CanBanter) continue;
            float sq = (m.transform.position - speaker.transform.position).sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = m; }
        }
        return best;
    }

    private IEnumerator Reply(DungeonAdventurer mate, string line)
    {
        yield return new WaitForSeconds(pairResponseDelay);
        if (mate != null && mate.CanBanter) mate.Say(line, BanterLines.Banter);
    }
}