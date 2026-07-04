using System;
using UnityEngine;

/// <summary>
/// The core's moral alignment, dark (-100) to good (+100). It shifts from how the
/// dungeon treats those who enter: slaying the peaceful or the fleeing pushes dark;
/// letting adventurers profit and leave alive pushes good. The chosen affinity sets
/// the starting lean - a Dark core begins dark-ward, a Light core good-ward. Persisted.
///
/// Reward-based gains (adventurers leaving alive WITH loot) diminish as alignment
/// climbs, so gold can buy redemption toward neutral but not sainthood - the flat
/// gains from sparing lives carry the last stretch.
///
/// SCENE SETUP: put this on the persistent manager GameObject (alongside FactionSystem).
/// No inspector references required; all shifts are serialized with working defaults.
/// </summary>
public class AlignmentSystem : MonoBehaviour
{
    public static AlignmentSystem Instance { get; private set; }

    [Header("Bounds")]
    [SerializeField] private float min = -100f;
    [SerializeField] private float max = 100f;

    [Header("Affinity starting lean")]
    [SerializeField] private float darkStart = -30f;
    [SerializeField] private float lightStart = 30f;

    [Header("Action shifts")]
    [Tooltip("Slaying a pilgrim or a cleric - a peaceful / holy soul.")]
    [SerializeField] private float killPeaceful = -8f;
    [Tooltip("Slaying an adventurer that was already fleeing.")]
    [SerializeField] private float killFleeing = -5f;
    [Tooltip("Slaying an ordinary combatant that came to fight.")]
    [SerializeField] private float killCombatant = -2f;
    [SerializeField] private float tribute = -3f;
    [SerializeField] private float pilgrimage = 6f;
    [Tooltip("An adventurer leaving the dungeon alive (flat, earned - does not diminish).")]
    [SerializeField] private float leaveAlive = 2f;

    [Header("Loot reward (diminishes toward good)")]
    [Tooltip("Alignment per gold an adventurer carries out alive. Full below neutral, tapering to nothing near +max.")]
    [SerializeField] private float lootPerGold = 0.02f;

    private float alignment;
    private bool started;

    public float Alignment => alignment;
    public static event Action<float> OnAlignmentChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Fresh game: lean by affinity. A load calls RestoreFromSave (which sets started),
        // so the loaded value wins regardless of Start/restore ordering.
        if (started) return;
        started = true;
        var t = DungeonCore.Instance != null ? DungeonCore.Instance.DungeonType : DungeonType.None;
        alignment = t == DungeonType.Dark ? darkStart : t == DungeonType.Light ? lightStart : 0f;
        alignment = Mathf.Clamp(alignment, min, max);
        OnAlignmentChanged?.Invoke(alignment);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Shift alignment by a flat amount and clamp.</summary>
    public void Shift(float delta)
    {
        alignment = Mathf.Clamp(alignment + delta, min, max);
        OnAlignmentChanged?.Invoke(alignment);
    }

    // Outcome hooks - called alongside the faction hooks at the same sites.

    public void OnAdventurerKilled(AdventurerType type, CombatClass cls, bool wasFleeing)
    {
        bool peaceful = type == AdventurerType.Pilgrim || cls == CombatClass.Cleric;
        Shift(peaceful ? killPeaceful : wasFleeing ? killFleeing : killCombatant);
    }

    public void OnPilgrimage() => Shift(pilgrimage);
    public void OnTribute() => Shift(tribute);

    /// <summary>An adventurer left alive: a flat good nudge plus a loot-scaled reward
    /// that diminishes toward good - forgiveness is buyable, sainthood is earned.</summary>
    public void OnAdventurerLeftAlive(int lootCarried)
    {
        float gain = leaveAlive;
        if (lootCarried > 0 && lootPerGold > 0f)
        {
            float taper = Mathf.Clamp01(1f - Mathf.Max(0f, alignment) / max);
            gain += lootCarried * lootPerGold * taper;
        }
        Shift(gain);
    }

    /// <summary>Desecration input for Holy Ground (stub; wired when that feature lands).</summary>
    public void Desecrate(float amount) => Shift(-Mathf.Abs(amount));

    public AlignmentSaveData GetSaveData() => new AlignmentSaveData { alignment = alignment };

    public void RestoreFromSave(AlignmentSaveData data)
    {
        if (data == null) return;
        alignment = Mathf.Clamp(data.alignment, min, max);
        started = true;
        OnAlignmentChanged?.Invoke(alignment);
    }
}

[Serializable]
public class AlignmentSaveData
{
    public float alignment;
}