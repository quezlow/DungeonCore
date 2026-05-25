using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(0)]

public class TileInfluenceManager : MonoBehaviour
{
    public static TileInfluenceManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap claimableTilemap;   // adjacent highlight

    [Header("Tile Assets")]
    [SerializeField] private TileBase claimableTile;

    [Header("Settings")]
    [SerializeField] private float passiveExpansionInterval = 30f;

    // ── State ─────────────────────────────────────────────────────
    private readonly HashSet<Vector3Int> ownedTiles = new();
    private readonly HashSet<Vector3Int> claimableTiles = new();

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Events ────────────────────────────────────────────────────
    public event Action<int> OnTileCountChanged; // (newTotalOwned)

    // ── Internal ──────────────────────────────────────────────────
    private Camera mainCamera;
    private Coroutine passiveExpansionCoroutine;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        mainCamera = Camera.main;

        if (DungeonCore.Instance == null || DungeonTerrain.Instance == null)
        {
            Debug.LogError("TileInfluenceManager: Missing DungeonCore or DungeonTerrain instance.");
            return;
        }

        Vector3Int detectedCore = DungeonTerrain.Instance.CoreCell;
        ClaimTile(detectedCore);
        StartPassiveExpansion();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
    }

    // ── Claiming ──────────────────────────────────────────────────

    /// <summary>Claims a tile, updates neighbours, notifies DungeonCore.</summary>
    public void ClaimTile(Vector3Int pos, bool silent = false)
    {
        if (ownedTiles.Contains(pos)) return;
        if (!DungeonTerrain.Instance.IsWithinBounds(pos)) return;

        ownedTiles.Add(pos);
        claimableTiles.Remove(pos);

        DungeonTerrain.Instance.RevealTile(pos);
        claimableTilemap.SetTile(pos, null);

        foreach (Vector3Int dir in Neighbours)
        {
            Vector3Int neighbour = pos + dir;
            if (ownedTiles.Contains(neighbour)) continue;
            if (claimableTiles.Contains(neighbour)) continue;
            if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue;

            claimableTiles.Add(neighbour);
            claimableTilemap.SetTile(neighbour, claimableTile);
        }

        if (!silent)
        {
            DungeonCore.Instance?.AddOwnedTiles(1);
            OnTileCountChanged?.Invoke(ownedTiles.Count);
        }
    }

    /// <summary>Removes a tile from ownership (e.g. Destroyer consequence).</summary>
    public void UnclaimTile(Vector3Int pos)
    {
        if (!ownedTiles.Contains(pos)) return;

        ownedTiles.Remove(pos);
        DungeonTerrain.Instance?.RefogTile(pos);

        // Clean up claimable tiles that are no longer adjacent to any owned tile
        RebuildClaimableSet();

        DungeonCore.Instance?.RemoveOwnedTiles(1);
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }

    // ── Passive Expansion ─────────────────────────────────────────

    private void StartPassiveExpansion()
    {
        if (passiveExpansionCoroutine != null) StopCoroutine(passiveExpansionCoroutine);
        passiveExpansionCoroutine = StartCoroutine(PassiveExpansionRoutine());
    }

    private IEnumerator PassiveExpansionRoutine()
    {
        while (true)
        {
            float elapsed = 0f;
            while (elapsed < passiveExpansionInterval)
            {
                if (!PauseController.IsGamePaused)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (claimableTiles.Count == 0) continue;
            int index = UnityEngine.Random.Range(0, claimableTiles.Count);
            Vector3Int target = claimableTiles.ElementAt(index);
            ClaimTile(target);
        }
    }

    // ── Add OnBoundsExpanded() — called by DungeonTerrain on level up ──
    /// <summary>Rechecks claimable ring against new bounds after expansion.</summary>
    public void OnBoundsExpanded()
    {
        // Any owned tile that now has a newly in-bounds neighbour should gain a claimable entry
        foreach (Vector3Int owned in ownedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue;

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>Rebuilds claimable set from scratch — used after unclaiming.</summary>
    private void RebuildClaimableSet()
    {
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        foreach (Vector3Int owned in ownedTiles)
        {
            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int neighbour = owned + dir;
                if (ownedTiles.Contains(neighbour)) continue;
                if (claimableTiles.Contains(neighbour)) continue;
                if (!DungeonTerrain.Instance.IsWithinBounds(neighbour)) continue; // ← add this

                claimableTiles.Add(neighbour);
                claimableTilemap.SetTile(neighbour, claimableTile);
            }
        }
    }

    /// <summary>Converts a world position to a tilemap cell coordinate.</summary>
    public Vector3Int WorldToCell(Vector3 worldPos)
        => claimableTilemap.WorldToCell(worldPos);

    /// <summary>Converts a tilemap cell to the world-space centre of that cell.</summary>
    public Vector3 CellToWorld(Vector3Int cell)
        => claimableTilemap.GetCellCenterWorld(cell);

    public bool IsTileOwned(Vector3Int pos) => ownedTiles.Contains(pos);
    public bool IsTileClaimable(Vector3Int pos) => claimableTiles.Contains(pos);
    public int OwnedTileCount => ownedTiles.Count;

    // ── Save / Load ───────────────────────────────────────────────

    public TileInfluenceSaveData GetSaveData()
    {
        return new TileInfluenceSaveData
        {
            ownedTiles = ownedTiles.Select(SerializableVector3Int.From).ToList()
        };
    }

    public void LoadSaveData(TileInfluenceSaveData data)
    {
        ownedTiles.Clear();
        claimableTiles.Clear();
        claimableTilemap.ClearAllTiles();

        foreach (var tile in data.ownedTiles)
            ClaimTile(tile.ToVector3Int(), silent: true); // ← DungeonCore count already restored

        // Fire count event once with final total
        OnTileCountChanged?.Invoke(ownedTiles.Count);
    }
}

// ── Save Data ─────────────────────────────────────────────────────

[Serializable]
public class TileInfluenceSaveData
{
    public List<SerializableVector3Int> ownedTiles;
}

// Vector3Int wrapper — JsonUtility can't serialize Vector3Int directly
[Serializable]
public class SerializableVector3Int
{
    public int x, y, z;
    public Vector3Int ToVector3Int() => new Vector3Int(x, y, z);
    public static SerializableVector3Int From(Vector3Int v) => new() { x = v.x, y = v.y, z = v.z };
}