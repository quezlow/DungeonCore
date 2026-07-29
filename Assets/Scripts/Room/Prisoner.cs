using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A living captive, taken alive instead of slain. Holds only the identity the
/// prison verbs need -- who they were, what they fought as, whose banner they
/// carried -- so nothing of the live adventurer keeps ticking inside a cell.
///
/// Source-agnostic, exactly like Corpse: anything may create one, and only the
/// PrisonController decides its fate (released, executed, read, or starved).
///
/// PREFAB SETUP: this script + a SpriteRenderer. Fill Sprite Variants with a few
/// captive sprites and one is chosen at random per captive; leave it empty to
/// just use the SpriteRenderer's own sprite.
/// </summary>
public class Prisoner : MonoBehaviour
{
    /// <summary>Every unresolved captive in the scene, for panel and dawn scans.</summary>
    public static readonly List<Prisoner> Active = new();

    [Tooltip("Optional pool of captive sprites; one is picked at random when the captive appears. " +
             "Empty = use the SpriteRenderer's current sprite.")]
    [SerializeField] private Sprite[] spriteVariants;

    [Tooltip("Randomly mirror the sprite left/right for extra variety (free, no art needed).")]
    [SerializeField] private bool randomFlipX = true;

    private string captiveName = "Captive";
    private AdventurerType type = AdventurerType.Mercenary;
    private CombatClass combatClass = CombatClass.Fighter;
    private string className = "";
    private bool named;
    private int daysHeld;
    private bool resolved;

    public string CaptiveName => captiveName;
    public AdventurerType Type => type;
    public CombatClass Class => combatClass;
    public string ClassName => string.IsNullOrEmpty(className) ? combatClass.ToString() : className;

    /// <summary>True for a named captive: their name survives into the corpse, so the
    /// Crypt can gather them if they are executed or left to starve.</summary>
    public bool IsNamed => named;

    public int DaysHeld => daysHeld;
    public bool Resolved => resolved;

    /// <summary>The banner this captive marched under -- what an interrogation reads.</summary>
    public FactionId Faction => AdventurerTypeInfo.FactionOf(type);

    /// <summary>Stamp the identity carried over from the adventurer that was taken.</summary>
    public void Initialise(string name, AdventurerType advType, CombatClass cls,
                           string classLabel, bool isNamed, int held = 0)
    {
        captiveName = string.IsNullOrEmpty(name) ? "Captive" : name;
        type = advType;
        combatClass = cls;
        className = classLabel;
        named = isNamed;
        daysHeld = Mathf.Max(0, held);
    }

    /// <summary>One more dawn endured in the dark. Returns the new total.</summary>
    public int AdvanceDay() => ++daysHeld;

    private void Awake()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;

        if (spriteVariants != null && spriteVariants.Length > 0)
        {
            var pick = spriteVariants[Random.Range(0, spriteVariants.Length)];
            if (pick != null) sr.sprite = pick;
        }
        if (randomFlipX) sr.flipX = Random.value < 0.5f;
    }

    private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    /// <summary>Consume this captive -- a verb has decided them. Idempotent.</summary>
    public void Resolve()
    {
        if (resolved) return;
        resolved = true;
        Destroy(gameObject);
    }
}