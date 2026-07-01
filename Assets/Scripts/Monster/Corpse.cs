using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A corpse left behind by a slain adventurer (and, later, humanoid monsters).
/// It lingers briefly, then fades — unless a necromancer raises it first.
/// Raising consumes the corpse (Claim). Source-agnostic: anything can spawn one
/// at a death position; only necromancers consume them.
///
/// PREFAB SETUP: this script + a SpriteRenderer. Fill Sprite Variants with a few
/// corpse/bones sprites and one is chosen at random per corpse; leave it empty to
/// just use the SpriteRenderer's own sprite.
/// </summary>
public class Corpse : MonoBehaviour
{
    /// <summary>All un-consumed corpses currently in the scene, for necromancer scans.</summary>
    public static readonly List<Corpse> Active = new();

    [Tooltip("Optional pool of corpse sprites; one is picked at random when the corpse appears. " +
             "Empty = use the SpriteRenderer's current sprite.")]
    [SerializeField] private Sprite[] spriteVariants;

    [Tooltip("Randomly mirror the sprite left/right for extra variety (free, no art needed).")]
    [SerializeField] private bool randomFlipX = true;

    [Tooltip("Seconds a corpse lingers before fading if no necromancer raises it.")]
    [SerializeField] private float lifetime = 20f;

    private bool claimed;
    public bool Claimed => claimed;

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

    private void Start() => Invoke(nameof(Expire), lifetime);

    private void Expire() { if (!claimed) Destroy(gameObject); }

    /// <summary>Consume this corpse (a necromancer has raised it). Idempotent.</summary>
    public void Claim()
    {
        if (claimed) return;
        claimed = true;
        CancelInvoke();
        Destroy(gameObject);
    }
}