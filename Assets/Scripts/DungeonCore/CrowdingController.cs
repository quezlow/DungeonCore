using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Pressed rule: dungeon monsters massed on mined ground OUTSIDE any valid
/// room fight poorly. Four or more of the core's own creatures within two
/// cells of one another in the corridors deal reduced damage and darken until
/// the press breaks - rooms are where a garrison belongs. Adventurers are
/// exempt on purpose: a marching party debuffed in transit would reward
/// tunnel-spam, the exact habit this rule exists to break. Their corridor
/// treatment arrives with the formation layer.
///
/// SCENE SETUP: one component on a persistent object (e.g. GameController).
/// No wiring. Sweeps every floor; owned monsters are reached through their
/// spawners, mirroring RoomEffectController.
/// </summary>
public class CrowdingController : MonoBehaviour
{
    [Tooltip("Seconds between crowding sweeps.")]
    [SerializeField, Min(0.1f)] private float tickInterval = 0.5f;
    [Tooltip("Monsters within this many cells of one another count toward a press (Chebyshev).")]
    [SerializeField, Min(1)] private int pressRadiusCells = 2;
    [Tooltip("This many clustered corridor monsters (self included) triggers the press.")]
    [SerializeField, Min(2)] private int pressThreshold = 4;
    [Tooltip("Damage-dealt multiplier while pressed.")]
    [SerializeField, Range(0.1f, 1f)] private float pressedDamageMultiplier = 0.75f;
    [Tooltip("Sprite shade multiplied in while pressed.")]
    [SerializeField] private Color pressedShade = new Color(0.72f, 0.68f, 0.78f);

    private float timer;
    private readonly List<RoomAnchor> anchorBuf = new();
    private readonly List<MonsterSpawner> spawnerBuf = new();
    private readonly HashSet<Vector3Int> roomTiles = new();
    private readonly List<DungeonMonster> corridor = new();
    private readonly List<Vector3Int> corridorCells = new();

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        timer += Time.deltaTime;
        if (timer < tickInterval) return;
        timer = 0f;
        Tick();
    }

    private void Tick()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return;

        foreach (var floor in fm.AllFloors)
        {
            if (floor == null || floor.Entities == null) continue;
            var infl = floor.TileInfluence;
            if (infl == null) continue;

            // Valid room footprints on this floor.
            roomTiles.Clear();
            floor.Entities.FillAll(anchorBuf);
            for (int i = 0; i < anchorBuf.Count; i++)
            {
                var a = anchorBuf[i];
                if (a == null || !a.IsValid) continue;
                var tiles = a.GetRoomTiles();
                if (tiles != null) roomTiles.UnionWith(tiles);
            }

            // The core's own creatures standing in the corridors.
            corridor.Clear();
            corridorCells.Clear();
            floor.Entities.FillAll(spawnerBuf);
            for (int i = 0; i < spawnerBuf.Count; i++)
            {
                var sp = spawnerBuf[i];
                if (sp == null || !sp.HasLiveMonster) continue;
                var m = sp.SpawnedMonster;
                if (m == null || !m.ServesDungeon) continue;

                var cell = infl.WorldToCell(m.transform.position);
                if (!infl.IsTileMined(cell) || roomTiles.Contains(cell))
                {
                    m.SetCrowdPenalty(false, pressedDamageMultiplier, pressedShade);
                    continue;
                }
                corridor.Add(m);
                corridorCells.Add(cell);
            }

            // Chebyshev cluster count; the roster is small, so n squared is fine.
            for (int i = 0; i < corridor.Count; i++)
            {
                int near = 0;
                for (int j = 0; j < corridor.Count; j++)
                {
                    var d = corridorCells[i] - corridorCells[j];
                    if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= pressRadiusCells) near++;
                }
                bool pressed = near >= pressThreshold;
                if (pressed) WispCompanion.Instance?.Speak("pressed_first");
                corridor[i].SetCrowdPenalty(pressed, pressedDamageMultiplier, pressedShade);
            }
        }
    }
}
