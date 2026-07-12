using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Event-driven, all-floors census of the state-lane room effects: GoldCapBonus,
/// Attractor, RespawnSpeed, ManaRegen and TrapDamage. Per-second entity effects
/// stay with RoomEffectController (active floor only) and SparringController.
///
/// Recounts when a room validates/invalidates or upgrades, plus a short
/// heartbeat that also covers load, demolition and anything without an event.
/// Consumers read the static surface; census absence degrades safely
/// (uncapped gold, no bonuses, x1 multipliers).
///
/// SCENE SETUP: add to the persistent managers object (beside RespawnTicker).
/// No wiring.
/// </summary>
public class RoomEffectCensus : MonoBehaviour
{
    [Tooltip("Gold the core can hold with no Treasuries standing.")]
    [SerializeField, Min(0)] private int baseGoldCap = 500;

    [Tooltip("Safety heartbeat: seconds between forced recounts (covers load and demolition).")]
    [SerializeField, Min(0.5f)] private float heartbeatSeconds = 2f;

    [Tooltip("Hard ceiling on the summed Forge bonus (0.5 = +50% trap damage at most).")]
    [SerializeField, Min(0f)] private float trapDamageMaxBonus = 0.5f;

    // -- Static read surface (safe defaults when no census exists) -----------

    public static int GoldCap { get; private set; } = int.MaxValue;
    public static float TrapDamageMultiplier { get; private set; } = 1f;
    public static float ManaRegenPerSecond { get; private set; }

    /// <summary>Fires when cap, trap or mana aggregates change (HUD refresh hook).</summary>
    public static event System.Action OnCensusChanged;

    private static readonly Dictionary<AdventurerType, float> attractor = new();
    private static readonly List<(HashSet<Vector3Int> tiles, float multiplier)> chambers = new();
    private static bool dirty = true;

    /// <summary>Summed attractor weight for one adventurer type, all floors.</summary>
    public static float GetAttractorBonus(AdventurerType type)
        => attractor.TryGetValue(type, out float b) ? b : 0f;

    /// <summary>Respawn-speed multiplier for a spawner: above 1 inside a valid Spawn Chamber.</summary>
    public static float GetRespawnMultiplier(MonsterSpawner spawner)
    {
        if (spawner == null || chambers.Count == 0) return 1f;
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return 1f;
        var cell = influence.WorldToCell(spawner.transform.position);
        for (int i = 0; i < chambers.Count; i++)
            if (chambers[i].tiles != null && chambers[i].tiles.Contains(cell))
                return chambers[i].multiplier;
        return 1f;
    }

    /// <summary>External nudge for systems without an event (rarely needed).</summary>
    public static void MarkDirty() => dirty = true;

    // -- Lifecycle ------------------------------------------------------------

    private float heartbeat;
    private readonly List<RoomAnchor> roomBuf = new();

    private void OnEnable()
    {
        RoomAnchor.OnRoomValidationChanged += HandleRoomChanged;
        RoomAnchor.OnRoomUpgraded += HandleRoomUpgraded;
        dirty = true;
    }

    private void OnDisable()
    {
        RoomAnchor.OnRoomValidationChanged -= HandleRoomChanged;
        RoomAnchor.OnRoomUpgraded -= HandleRoomUpgraded;

        // Fall back to safe defaults so a missing census never strangles the game.
        GoldCap = int.MaxValue;
        TrapDamageMultiplier = 1f;
        ManaRegenPerSecond = 0f;
        attractor.Clear();
        chambers.Clear();
    }

    private void HandleRoomChanged(RoomAnchor anchor, bool valid) => dirty = true;
    private void HandleRoomUpgraded(RoomAnchor anchor) => dirty = true;

    private void Update()
    {
        heartbeat += Time.unscaledDeltaTime;
        if (heartbeat >= heartbeatSeconds) { heartbeat = 0f; dirty = true; }
        if (!dirty) return;
        dirty = false;
        Recount();
    }

    private void Recount()
    {
        int cap = baseGoldCap;
        float trapBonus = 0f;
        float mana = 0f;
        attractor.Clear();
        chambers.Clear();

        var fm = FloorManager.Instance;
        if (fm != null)
        {
            foreach (var floor in fm.AllFloors)
            {
                if (floor?.Entities == null) continue;
                floor.Entities.FillAll(roomBuf);
                for (int r = 0; r < roomBuf.Count; r++)
                {
                    var anchor = roomBuf[r];
                    if (anchor == null || !anchor.IsValid || anchor.AssignedRoom == null) continue;
                    var fx = anchor.AssignedRoom.effects;
                    if (fx == null) continue;

                    float scale = anchor.EffectScale;
                    for (int e = 0; e < fx.Count; e++)
                    {
                        var f = fx[e];
                        if (f == null || f.perSecond <= 0f) continue;
                        switch (f.type)
                        {
                            case RoomEffectType.GoldCapBonus:
                                cap += Mathf.RoundToInt(f.perSecond * scale);
                                break;
                            case RoomEffectType.Attractor:
                                attractor.TryGetValue(f.attractorTarget, out float b);
                                attractor[f.attractorTarget] = b + f.perSecond * scale;
                                break;
                            case RoomEffectType.RespawnSpeed:
                                var tiles = anchor.GetRoomTiles();
                                if (tiles != null) chambers.Add((tiles, 1f + f.perSecond * scale));
                                break;
                            case RoomEffectType.ManaRegen:
                                mana += f.perSecond * scale;
                                break;
                            case RoomEffectType.TrapDamage:
                                trapBonus += f.perSecond * scale;
                                break;
                        }
                    }
                }
            }
        }

        float newTrapMult = 1f + Mathf.Min(trapBonus, trapDamageMaxBonus);

        // Attractor and chamber lists refresh silently; the change event exists
        // for the HUD readouts, which only show cap, trap and mana aggregates.
        bool changed = cap != GoldCap
                       || !Mathf.Approximately(newTrapMult, TrapDamageMultiplier)
                       || !Mathf.Approximately(mana, ManaRegenPerSecond);

        GoldCap = cap;
        TrapDamageMultiplier = newTrapMult;
        ManaRegenPerSecond = mana;

        if (changed)
        {
            OnCensusChanged?.Invoke();
            DungeonCore.Instance?.NotifyManaRegenDisplay();
        }
    }
}