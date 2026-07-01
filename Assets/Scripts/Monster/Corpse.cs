using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A corpse left behind by a slain adventurer (and, later, humanoid monsters).
/// It lingers briefly, then fades — unless a necromancer raises it first.
/// Raising consumes the corpse (Claim). Source-agnostic: anything can spawn one
/// at a death position; only necromancers consume them.
///
/// PREFAB SETUP: this script + a SpriteRenderer (a corpse/bones sprite). No
/// collider needed — necromancers find corpses via the static Active list.
/// </summary>
public class Corpse : MonoBehaviour
{
    /// <summary>All un-consumed corpses currently in the scene, for necromancer scans.</summary>
    public static readonly List<Corpse> Active = new();

    [Tooltip("Seconds a corpse lingers before fading if no necromancer raises it.")]
    [SerializeField] private float lifetime = 20f;

    private bool claimed;
    public bool Claimed => claimed;

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