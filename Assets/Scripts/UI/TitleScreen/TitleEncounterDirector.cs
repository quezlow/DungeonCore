using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs the title-screen diorama. A random non-boss monster wanders the bottom of the
/// screen (and follows the cursor when it is near). Every so often a random adventurer
/// walks in from one side, closes on the monster, and they trade blows until one dies.
/// The loser fades out. If the monster dies, the victorious adventurer retreats the way
/// it came and a NEW random monster walks in from the opposite side. Loops forever.
///
/// Drives two TitleFighter puppets (monster + adventurer) — neither has its own AI.
/// </summary>
public class TitleEncounterDirector : MonoBehaviour
{
    [Header("Actors")]
    [SerializeField] private TitleFighter monster;
    [SerializeField] private TitleFighter adventurer;
    [SerializeField] private RectTransform canvasRect;

    [Header("Sources")]
    [SerializeField] private MonsterDefinitionRegistry monsterRegistry;
    [SerializeField] private List<AdventurerDefinition> adventurerTypes = new();

    [Header("Sizing (canvas units)")]
    [SerializeField] private float monsterHeight = 170f;
    [SerializeField] private float adventurerHeight = 170f;

    [Header("Movement (canvas units / sec)")]
    [SerializeField] private float wanderSpeed = 55f;
    [SerializeField] private float walkSpeed = 95f;
    [SerializeField] private float followSpeed = 150f;
    [SerializeField] private float edgePadding = 70f;
    [SerializeField] private float offscreenMargin = 160f;
    [SerializeField] private float fightGap = 130f;
    [SerializeField] private float detectionRadius = 280f;

    [Header("Pacing (sec)")]
    [SerializeField] private float wanderMin = 8f;
    [SerializeField] private float wanderMax = 15f;

    [Header("Combat (tune for ~even odds; monster strikes first)")]
    [SerializeField] private float monsterHP = 36f;
    [SerializeField] private float monsterDamage = 6f;
    [SerializeField] private float monsterAttackInterval = 0.9f;
    [SerializeField] private float adventurerHP = 30f;
    [SerializeField] private float adventurerDamage = 6f;
    [SerializeField] private float adventurerAttackInterval = 1.0f;

    private int wanderDir = 1;

    private float HalfW => canvasRect != null ? canvasRect.rect.width * 0.5f : 960f;
    private float LeftBound => -HalfW + edgePadding;
    private float RightBound => HalfW - edgePadding;

    private void Start()
    {
        if (adventurer != null) adventurer.Hide();
        if (monster == null || canvasRect == null || monsterRegistry == null)
        {
            Debug.LogWarning("[TitleEncounterDirector] Missing references; diorama disabled.");
            return;
        }
        StartCoroutine(RunDiorama());
    }

    private Sprite SpriteFromPrefabOf(MonsterDefinition def)
    {
        if (def == null) return null;
        if (def.prefab != null)
        {
            var sr = def.prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null) return sr.sprite;
        }
        return def.icon;
    }

    private Sprite RandomMonsterSprite()
    {
        var pool = new List<MonsterDefinition>();
        foreach (var d in monsterRegistry.All)
            if (d != null && !(d is BossVariantDefinition)) pool.Add(d);
        if (pool.Count == 0) return null;
        return SpriteFromPrefabOf(pool[Random.Range(0, pool.Count)]);
    }

    private Sprite RandomAdventurerSprite()
    {
        if (adventurerTypes.Count == 0) return null;
        var def = adventurerTypes[Random.Range(0, adventurerTypes.Count)];
        if (def != null && def.prefab != null)
        {
            var sr = def.prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null && sr.sprite != null) return sr.sprite;
        }
        return null;
    }

    private IEnumerator RunDiorama()
    {
        yield return IntroduceMonster(Random.value < 0.5f ? -1 : 1);

        while (true)
        {
            yield return WanderPhase(Random.Range(wanderMin, wanderMax));

            int advFrom = Random.value < 0.5f ? -1 : 1;
            yield return AdventurerEnters(advFrom);

            bool monsterDied = false;
            yield return Fight(result => monsterDied = result);

            if (monsterDied)
            {
                yield return AdventurerLeaves(advFrom);
                yield return IntroduceMonster(-advFrom);
            }
            // else: adventurer already faded out; the same monster resumes wandering
        }
    }

    private IEnumerator IntroduceMonster(int fromSide)
    {
        monster.Configure(RandomMonsterSprite(), monsterHeight, monsterHP, monsterDamage);
        monster.SetX(fromSide * (HalfW + offscreenMargin));
        monster.Show();
        float target = Random.Range(LeftBound * 0.5f, RightBound * 0.5f);
        while (!monster.StepToward(target, walkSpeed, Time.unscaledDeltaTime)) yield return null;
        wanderDir = Random.value < 0.5f ? -1 : 1;
    }

    private IEnumerator WanderPhase(float duration)
    {
        float t = duration;
        while (t > 0f)
        {
            float dt = Time.unscaledDeltaTime;
            t -= dt;

            Vector2 cur = default;
            bool haveCursor = Mouse.current != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, Mouse.current.position.ReadValue(), null, out cur);
            Vector2 monLocal = canvasRect.InverseTransformPoint(monster.transform.position);

            if (haveCursor && Vector2.Distance(monLocal, cur) <= detectionRadius)
            {
                float targetX = Mathf.Clamp(cur.x, LeftBound, RightBound);
                monster.StepToward(targetX, followSpeed, dt, 24f);
            }
            else
            {
                if (monLocal.x <= LeftBound) wanderDir = 1;
                else if (monLocal.x >= RightBound) wanderDir = -1;
                monster.StepToward(monster.X + wanderDir * 120f, wanderSpeed, dt, 0.5f);
            }
            yield return null;
        }
    }

    private IEnumerator AdventurerEnters(int fromSide)
    {
        adventurer.Configure(RandomAdventurerSprite(), adventurerHeight, adventurerHP, adventurerDamage);
        adventurer.SetX(fromSide * (HalfW + offscreenMargin));
        adventurer.Show();

        while (true)
        {
            float targetX = Mathf.Clamp(monster.X + fromSide * fightGap, LeftBound, RightBound);
            bool reached = adventurer.StepToward(targetX, walkSpeed, Time.unscaledDeltaTime, 6f);
            monster.FaceToward(adventurer.X);
            if (reached) break;
            yield return null;
        }
        monster.FaceToward(adventurer.X);
        adventurer.FaceToward(monster.X);
    }

    private IEnumerator Fight(System.Action<bool> onResolved)
    {
        float mTimer = monsterAttackInterval * 0.6f;
        float aTimer = adventurerAttackInterval;

        while (!monster.IsDead && !adventurer.IsDead)
        {
            float dt = Time.unscaledDeltaTime;
            mTimer -= dt;
            aTimer -= dt;

            if (mTimer <= 0f && !monster.IsDead && !adventurer.IsDead)
            {
                mTimer = monsterAttackInterval;
                monster.FaceToward(adventurer.X);
                StartCoroutine(monster.Lunge(adventurer.X >= monster.X ? 1 : -1));
                adventurer.TakeHit(monster.AttackDamage);
            }
            if (aTimer <= 0f && !monster.IsDead && !adventurer.IsDead)
            {
                aTimer = adventurerAttackInterval;
                adventurer.FaceToward(monster.X);
                StartCoroutine(adventurer.Lunge(monster.X >= adventurer.X ? 1 : -1));
                monster.TakeHit(adventurer.AttackDamage);
            }
            yield return null;
        }

        if (adventurer.IsDead) yield return adventurer.FadeOut();
        bool monsterDied = monster.IsDead;
        if (monsterDied) yield return monster.FadeOut();
        onResolved?.Invoke(monsterDied);
    }

    private IEnumerator AdventurerLeaves(int towardSide)
    {
        float exitX = towardSide * (HalfW + offscreenMargin);
        while (!adventurer.StepToward(exitX, walkSpeed, Time.unscaledDeltaTime, 6f)) yield return null;
        adventurer.Hide();
    }
}