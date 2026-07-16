using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Grows the procgen forest zone inside the Forest scene: a grass tilemap with a
/// cleared road from the dungeon-side arrival point to the hand-built edge,
/// scattered trees/rocks/decor, reserved camp zones, and inert resource-node
/// stubs (rarer types farther from the arrival). Deterministic from the run's
/// world seed (RunContext) so it rebuilds identically every load with no save
/// plumbing -- the SurfaceApronGenerator idiom.
///
/// SCENE SETUP (Forest scene):
///   Under the scene Grid, add a Tilemap + TilemapRenderer named "ForestTilemap".
///   Add empty siblings "ForestProps", "ForestCamps", "ForestNodes".
///   Add a SpawnPoint with id "FromDungeonEntrance" at the dungeon-side edge and
///   mark another SpawnPoint Is Default. Put this script anywhere in the scene,
///   wire the fields, and assign a ForestZoneProfile.
/// </summary>
public class ForestZoneGenerator : MonoBehaviour
{
    private const int FOREST_SALT = 0x0F0235;   // keeps the forest stream off the floor streams

    [Header("References")]
    [SerializeField] private ForestZoneProfile profile;
    [SerializeField] private Tilemap forestTilemap;
    [SerializeField] private Transform propParent;
    [SerializeField] private Transform campParent;
    [SerializeField] private Transform nodeParent;

    [Header("Anchor")]
    [Tooltip("Spawn point the zone is measured from (the dungeon-side arrival).")]
    [SerializeField] private string arrivalSpawnId = "FromDungeonEntrance";
    [Tooltip("Local +depth direction: toward the hand-built forest edge.")]
    [SerializeField] private Vector2Int forward = new Vector2Int(0, 1);

    private bool generated;

    private void Start()
    {
        int seed;
        if (RunContext.HasWorldSeed) seed = RunContext.WorldSeed;
        else if (!TryReadSeedFromSlot(out seed))
        {
            Debug.LogWarning("[ForestZoneGenerator] No world seed available; skipping generation.");
            return;
        }
        Generate(unchecked(seed ^ FOREST_SALT));
        generated = true;
    }

    // Optional hardening: read worldSeed straight from the active slot's save file
    // if the dungeon never initialised in this session. Left as a stub hook --
    // wire it to SlotPaths.SavePath(SaveSlotManager.Instance.ActiveSlotId) +
    // JsonUtility.FromJson<DungeonSaveData> if you need it.
    private bool TryReadSeedFromSlot(out int seed) { seed = 0; return false; }

    private Vector3Int ArrivalCell()
    {
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.SpawnPointID == arrivalSpawnId)
                return forestTilemap.WorldToCell(sp.transform.position);
        return forestTilemap.WorldToCell(transform.position);   // fallback: this object
    }

    private void Generate(int seed)
    {
        if (profile == null || forestTilemap == null || profile.grassTile == null)
        {
            Debug.LogError("[ForestZoneGenerator] Missing profile, tilemap, or grass tile.");
            return;
        }

        forestTilemap.ClearAllTiles();
        ClearChildren(propParent);
        ClearChildren(campParent);
        ClearChildren(nodeParent);

        var rng = new System.Random(seed);
        Vector3Int origin = ArrivalCell();
        Vector2 fwd = ((Vector2)forward).normalized;
        Vector2 side = new Vector2(-fwd.y, fwd.x);   // perpendicular (across the road)

        int halfW = profile.zoneWidth / 2;

        // --- Camp zones (reserved first, so ground/scatter can respect them) ---
        var camps = new List<(Vector3Int cell, float radius)>();
        Vector3Int mainCell = StepCells(origin, fwd, profile.mainCampForwardOffset);
        SpawnCamp("camp.main", mainCell, profile.mainCampRadius);
        camps.Add((mainCell, profile.mainCampRadius));

        for (int i = 0; i < profile.satelliteCampCount; i++)
        {
            Vector3Int c = default; bool ok = false;
            for (int attempt = 0; attempt < 24 && !ok; attempt++)
            {
                int alongMin = profile.mainCampForwardOffset + profile.satelliteMinDistanceFromMain / 2;
                int along = alongMin + rng.Next(Mathf.Max(1, profile.zoneDepth - alongMin));
                int lateral = rng.Next(-halfW + 2, halfW - 1);
                c = StepCells(StepCells(origin, fwd, along), side, lateral);
                ok = FarEnough(c, camps, profile.satelliteMinDistanceFromMain);
            }
            if (!ok) continue;
            SpawnCamp($"camp.sat.{i + 1}", c, profile.satelliteCampRadius);
            camps.Add((c, profile.satelliteCampRadius));
        }

        // --- Ground + road + scatter ---
        for (int d = 0; d <= profile.zoneDepth; d++)
            for (int w = -halfW; w <= halfW; w++)
            {
                Vector3Int cell = StepCells(StepCells(origin, fwd, d), side, w);

                bool onRoad = Mathf.Abs(w) <= profile.pathHalfWidth;
                bool inClearing = Mathf.Abs(w) <= profile.pathClearance;

                if (onRoad && profile.roadTile != null) forestTilemap.SetTile(cell, profile.roadTile);
                else forestTilemap.SetTile(cell, profile.grassTile);

                if (inClearing) continue;
                if (InAnyCamp(cell, camps)) continue;
                if (rng.NextDouble() >= profile.scatterDensity) continue;

                var prefab = PickScatter(rng);
                if (prefab != null)
                    Instantiate(prefab, forestTilemap.GetCellCenterWorld(cell),
                                Quaternion.identity, propParent).name = prefab.name;
            }

        // --- Resource nodes (distance-rarity gradient) ---
        PlaceNodes(rng, origin, fwd, side, halfW, camps);

        Debug.Log($"[ForestZoneGenerator] Forest grown: {profile.zoneWidth}x{profile.zoneDepth}, " +
                  $"{camps.Count} camp zones.");
    }

    private void PlaceNodes(System.Random rng, Vector3Int origin, Vector2 fwd, Vector2 side,
                            int halfW, List<(Vector3Int cell, float radius)> camps)
    {
        if (profile.nodeTypes == null || profile.nodeTypes.Count == 0 || profile.nodeCount <= 0) return;

        var placed = new List<Vector3Int>();
        int guard = profile.nodeCount * 20;

        while (placed.Count < profile.nodeCount && guard-- > 0)
        {
            int d = rng.Next(1, profile.zoneDepth + 1);
            int w = rng.Next(-halfW + 1, halfW);
            Vector3Int cell = StepCells(StepCells(origin, fwd, d), side, w);

            if (Mathf.Abs(w) <= profile.pathClearance) continue;      // keep the road clear
            if (InAnyCamp(cell, camps)) continue;                     // camps host their own later
            if (!FarEnoughCells(cell, placed, profile.nodeMinSpacing)) continue;

            float dist01 = Mathf.Clamp01((float)d / profile.zoneDepth);
            var type = PickNodeType(rng, dist01);
            if (type == null) continue;

            var go = type.stubPrefab != null
                ? Instantiate(type.stubPrefab, forestTilemap.GetCellCenterWorld(cell),
                              Quaternion.identity, nodeParent)
                : new GameObject(type.displayName);
            if (type.stubPrefab == null)
            {
                go.transform.SetParent(nodeParent, false);
                go.transform.position = forestTilemap.GetCellCenterWorld(cell);
            }
            var stub = go.GetComponent<ResourceNodeStub>() ?? go.AddComponent<ResourceNodeStub>();
            stub.Init(type.nodeKey);
            placed.Add(cell);
        }
    }

    // Weighted pick among node types whose band contains dist01.
    private ResourceNodeType PickNodeType(System.Random rng, float dist01)
    {
        float total = 0f;
        foreach (var t in profile.nodeTypes)
            if (dist01 >= t.minSpawnDistance01 && dist01 <= t.maxSpawnDistance01) total += t.weight;
        if (total <= 0f) return null;

        double roll = rng.NextDouble() * total;
        foreach (var t in profile.nodeTypes)
        {
            if (dist01 < t.minSpawnDistance01 || dist01 > t.maxSpawnDistance01) continue;
            roll -= t.weight;
            if (roll <= 0d) return t;
        }
        return null;
    }

    private GameObject PickScatter(System.Random rng)
    {
        var pool = new List<GameObject>();
        if (profile.treePrefabs != null) pool.AddRange(profile.treePrefabs);
        if (profile.rockPrefabs != null) pool.AddRange(profile.rockPrefabs);
        if (profile.decorPrefabs != null) pool.AddRange(profile.decorPrefabs);
        pool.RemoveAll(p => p == null);
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }

    private void SpawnCamp(string id, Vector3Int cell, float radius)
    {
        GameObject go = profile.campZonePrefab != null
            ? Instantiate(profile.campZonePrefab, forestTilemap.GetCellCenterWorld(cell),
                          Quaternion.identity, campParent)
            : new GameObject(id);
        if (profile.campZonePrefab == null)
        {
            go.transform.SetParent(campParent, false);
            go.transform.position = forestTilemap.GetCellCenterWorld(cell);
        }
        var marker = go.GetComponent<CampZoneMarker>() ?? go.AddComponent<CampZoneMarker>();
        marker.Init(id, radius);
    }

    // --- helpers ---
    private static Vector3Int StepCells(Vector3Int from, Vector2 dir, int n)
        => from + new Vector3Int(Mathf.RoundToInt(dir.x * n), Mathf.RoundToInt(dir.y * n), 0);

    private bool InAnyCamp(Vector3Int cell, List<(Vector3Int cell, float radius)> camps)
    {
        foreach (var c in camps)
            if ((cell - c.cell).magnitude <= c.radius) return true;
        return false;
    }

    private static bool FarEnough(Vector3Int cell, List<(Vector3Int cell, float radius)> camps, float min)
    {
        foreach (var c in camps)
            if ((cell - c.cell).magnitude < min) return false;
        return true;
    }

    private static bool FarEnoughCells(Vector3Int cell, List<Vector3Int> cells, float min)
    {
        foreach (var c in cells)
            if ((cell - c).magnitude < min) return false;
        return true;
    }

    private static void ClearChildren(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }
}