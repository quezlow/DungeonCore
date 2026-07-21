using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The wisp companion in the dungeon: a small floating sprite that bobs near
/// the core and speaks contextual tutorial lines when things first happen -
/// the first party arriving, first blood, the first monster lost, notoriety
/// crossing a threshold. Lines queue, hold, and can be skipped with any key.
///
/// One-shot lines are remembered by id across saves, so the tutorial never
/// repeats on Continue. Wire the sprite child, the panel, and a WispScript
/// asset; the world sprite floats, the panel speaks.
/// </summary>
public class WispCompanion : MonoBehaviour
{
    public static WispCompanion Instance { get; private set; }

    [Header("Float")]
    [Tooltip("The wisp's world sprite - bobs gently in place.")]
    [SerializeField] private Transform floatSprite;
    [SerializeField] private float bobAmplitude = 0.15f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float driftAmplitude = 0.08f;
    [SerializeField] private float driftSpeed = 0.7f;
    [Header("Roam")]
    [Tooltip("Slow wander around the home point, in world units, on top of the bob/drift.")]
    [SerializeField] private float roamRadius = 1.6f;
    [Tooltip("How quickly the wander re-targets. Lower is lazier.")]
    [SerializeField] private float roamSpeed = 0.25f;

    [Header("Panel")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text panelText;
    [SerializeField] private float fadeSeconds = 0.35f;
    [SerializeField] private float holdSeconds = 3.2f;

    [Header("Opening")]
    [Tooltip("Plays the arrive_* sequence the first time a fresh dungeon loads.")]
    [SerializeField] private bool playOpeningOnFreshLoad = true;
    [SerializeField] private float openingStartDelay = 1.5f;

    [Header("Triggers")]
    [Tooltip("Notoriety at or above this fires the spike line once.")]
    [SerializeField] private float notorietySpikeThreshold = 25f;

    [Header("Ambient barks")]
    [Tooltip("Idle bark cadence, seconds. Random between these.")]
    [SerializeField] private float barkMinInterval = 12f;
    [SerializeField] private float barkMaxInterval = 20f;
    [Tooltip("When on, the roll leans toward the core's temperament instead of pure random.")]
    [SerializeField] private bool matchCoreType = false;

    [Header("Data")]
    [SerializeField] private WispScript script;

    private Vector3 spriteHome;
    private float roamSeed;
    private readonly HashSet<string> spoken = new();
    // Queued speech carries the resolved text plus an optional once-id (for the
    // ambient one-shots). Tutorial lines enqueue text directly with no id.
    private struct Utterance { public string id; public string text; }
    private readonly Queue<Utterance> queue = new();
    private bool speaking;
    private bool notorietyHooked;
    private WispPersonality personality;
    private bool personalityRolled;
    private float nextBarkTime;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (floatSprite != null) spriteHome = floatSprite.localPosition;
        roamSeed = UnityEngine.Random.value * 100f;
        if (panel != null)
        {
            panel.alpha = 0f;
            panel.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        DungeonAdventurer.OnFirstPartySpawned += HandleFirstParty;
        DungeonAdventurer.OnAnyAdventurerSlain += HandleFirstBlood;
        DungeonMonster.OnAnyMonsterSlain += HandleFirstMonsterLost;
    }

    private void OnDisable()
    {
        DungeonAdventurer.OnFirstPartySpawned -= HandleFirstParty;
        DungeonAdventurer.OnAnyAdventurerSlain -= HandleFirstBlood;
        DungeonMonster.OnAnyMonsterSlain -= HandleFirstMonsterLost;
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnNotorietyChanged -= HandleNotoriety;
    }

    private void Start()
    {
        // The core may come alive a frame or two after us; hook when it exists.
        StartCoroutine(HookNotorietyWhenReady());

        // The save restore populates 'spoken' a frame or two after Start, so
        // deciding here would replay the opening every load. Defer one frame.
        if (playOpeningOnFreshLoad) StartCoroutine(PlayOpeningIfFirstTime());

        if (!personalityRolled) RollPersonality();
        nextBarkTime = Time.time + UnityEngine.Random.Range(barkMinInterval, barkMaxInterval);
    }

    // Weighted temperament table. Common temperaments land often; Ancient and
    // Reverent are the rare "oh, I got the unusual one" rolls. Weights are
    // relative - tune freely.
    private static readonly (WispPersonality personality, int weight)[] RollTable =
    {
        (WispPersonality.Wry, 5),
        (WispPersonality.Grim, 5),
        (WispPersonality.Eager, 5),
        (WispPersonality.Nervous, 5),
        (WispPersonality.Feral, 5),
        (WispPersonality.Ancient, 1),
        (WispPersonality.Reverent, 1),
    };

    /// <summary>Roll the wisp's temperament once. Weighted-random by default;
    /// leaning toward the core's nature when matchCoreType is on. Persisted.</summary>
    private void RollPersonality()
    {
        if (matchCoreType && DungeonCore.Instance != null)
        {
            switch (DungeonCore.Instance.DungeonType)
            {
                case DungeonType.Dark: personality = WispPersonality.Grim; break;
                case DungeonType.Earth: personality = WispPersonality.Feral; break;
                case DungeonType.Fire:
                case DungeonType.Air: personality = WispPersonality.Eager; break;
                case DungeonType.Light: personality = WispPersonality.Reverent; break;
                default: personality = WispPersonality.Wry; break;
            }
        }
        else
        {
            personality = WeightedRoll();
        }
        personalityRolled = true;
    }

    private static WispPersonality WeightedRoll()
    {
        int total = 0;
        foreach (var entry in RollTable) total += entry.weight;

        int pick = UnityEngine.Random.Range(0, total);
        foreach (var entry in RollTable)
        {
            if (pick < entry.weight) return entry.personality;
            pick -= entry.weight;
        }
        return WispPersonality.Wry; // unreachable; satisfies the compiler
    }

    private IEnumerator HookNotorietyWhenReady()
    {
        float timeout = 5f;
        while (DungeonCore.Instance == null && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (DungeonCore.Instance != null && !notorietyHooked)
        {
            DungeonCore.Instance.OnNotorietyChanged += HandleNotoriety;
            notorietyHooked = true;
        }
    }

    private void Update()
    {
        if (floatSprite == null) return;
        // A slow, lazy wander around home (incommensurate sine axes trace an
        // open path so it never sits still), with the tight bob/drift on top.
        float rx = Mathf.Sin(Time.time * roamSpeed + roamSeed) * roamRadius;
        float ry = Mathf.Sin(Time.time * roamSpeed * 0.73f + roamSeed * 1.7f) * roamRadius * 0.6f;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        float drift = Mathf.Sin(Time.time * driftSpeed) * driftAmplitude;
        floatSprite.localPosition = spriteHome + new Vector3(rx + drift, ry + bob, 0f);

        TryIdleBark();
    }

    // Ambient personality barks: gentle cadence, hushed during tutorial lines,
    // combat, and pause. Routed through the shared floating-bark spawner so they
    // drift up over the wisp exactly like adventurer chatter.
    private IEnumerator PlayOpeningIfFirstTime()
    {
        yield return null;   // let DungeonSaveController restore spoken lines
        yield return null;
        if (!HasSpoken("arrive_1")) StartCoroutine(PlayOpening());
    }

    private void TryIdleBark()
    {
        if (speaking) return;                       // never step on a tutorial line
        if (PauseController.IsGamePaused) return;
        if (Time.time < nextBarkTime) return;
        nextBarkTime = Time.time + UnityEngine.Random.Range(barkMinInterval, barkMaxInterval);

        if (AnyAdventurerInCombat()) return;        // stay quiet mid-fight
        if (script == null) return;

        string line = script.RandomBark(personality);
        if (string.IsNullOrEmpty(line)) return;

        WispScript.BarkSet set = script.BarksFor(personality);
        Color tint = set != null ? set.tint : Color.white;
        Vector3 at = floatSprite != null ? floatSprite.position : transform.position;
        BarkSpawner.Spawn(at, line, tint);
    }

    private static bool AnyAdventurerInCombat()
    {
        var floor = FloorManager.Instance?.ActiveFloor;
        if (floor?.Entities == null) return false;
        var buf = new List<DungeonAdventurer>();
        floor.Entities.FillAll(buf);
        for (int i = 0; i < buf.Count; i++)
            if (buf[i] != null && buf[i].IsInCombat) return true;
        return false;
    }

    // ------------------------------------------------------------ public API

    /// <summary>Queue a line by id. One-shot lines already heard are skipped.</summary>
    public void Speak(string id)
    {
        if (script == null || string.IsNullOrEmpty(id)) return;
        WispScript.Line line = script.Get(id);
        if (line == null) return;
        if (line.once && HasSpoken(id)) return;

        queue.Enqueue(new Utterance { id = id, text = line.text });
        if (!speaking) StartCoroutine(DrainQueue());
    }

    /// <summary>Speak a line of raw text (the tutorial's path). No once-tracking:
    /// the TutorialDirector decides what plays and when.</summary>
    public void SpeakLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        queue.Enqueue(new Utterance { text = text });
        if (!speaking) StartCoroutine(DrainQueue());
    }

    public bool HasSpoken(string id) => spoken.Contains(id);

    /// <summary>Save hook: the ids already heard.</summary>
    public List<string> GetSpokenForSave() => new List<string>(spoken);

    /// <summary>Load hook: restore heard ids so the tutorial never repeats.</summary>
    public void RestoreSpokenFromSave(List<string> ids)
    {
        spoken.Clear();
        if (ids != null)
            foreach (string id in ids) spoken.Add(id);
    }

    /// <summary>Save hook: the rolled temperament as an int (-1 if somehow unrolled).</summary>
    public int GetPersonalityForSave() => personalityRolled ? (int)personality : -1;

    /// <summary>Load hook: restore the temperament so the wisp's voice stays stable.</summary>
    public void RestorePersonalityFromSave(int value)
    {
        if (value < 0) return;
        personality = (WispPersonality)value;
        personalityRolled = true;
    }

    // ------------------------------------------------------------ triggers

    private void HandleFirstParty() => Speak("first_party");
    private void HandleFirstBlood() => Speak("first_blood");
    private void HandleFirstMonsterLost() => Speak("first_monster_lost");

    private void HandleNotoriety(float value)
    {
        if (value >= notorietySpikeThreshold) Speak("notoriety_spike");
    }

    private IEnumerator PlayOpening()
    {
        yield return new WaitForSeconds(openingStartDelay);
        Speak("arrive_1");
        Speak("arrive_mana");
        Speak("arrive_influence");
        Speak("arrive_await");
    }

    // ------------------------------------------------------------ speech

    private IEnumerator DrainQueue()
    {
        speaking = true;
        while (queue.Count > 0)
        {
            Utterance u = queue.Dequeue();
            if (string.IsNullOrEmpty(u.text)) continue;

            // Mark one-shot ids spoken as shown, so a mid-line quit still remembers.
            if (!string.IsNullOrEmpty(u.id))
            {
                WispScript.Line line = script != null ? script.Get(u.id) : null;
                if (line != null && line.once) spoken.Add(u.id);
            }

            yield return ShowLine(u.text);
        }
        yield return HidePanel();
        speaking = false;
    }

    private IEnumerator ShowLine(string text)
    {
        if (panel == null || panelText == null) yield break;

        panelText.text = text;
        panel.gameObject.SetActive(true);
        yield return Fade(panel.alpha, 1f);

        // Lines land by time or by the action they ask for -- never by a
        // stray press. Tutorial integrity outranks impatience.
        float t = 0f;
        while (t < holdSeconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator HidePanel()
    {
        if (panel == null) yield break;
        yield return Fade(panel.alpha, 0f);
        panel.gameObject.SetActive(false);
    }

    private IEnumerator Fade(float from, float to)
    {
        if (panel == null) yield break;
        float t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            panel.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
            yield return null;
        }
        panel.alpha = to;
    }

}