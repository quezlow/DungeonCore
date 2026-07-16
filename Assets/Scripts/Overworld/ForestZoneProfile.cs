using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public class ResourceNodeType
{
    public string nodeKey = "node.wood";     // immutable id / save key
    public string displayName = "Wood";
    public GameObject stubPrefab;            // marker visual; harvest comes later
    [Range(0f, 1f)] public float minSpawnDistance01 = 0f;   // 0 = arrival edge
    [Range(0f, 1f)] public float maxSpawnDistance01 = 1f;   // 1 = far/hand-built edge
    [Min(0f)] public float weight = 1f;
}

[CreateAssetMenu(menuName = "Dungeon/Forest Zone Profile", fileName = "ForestZoneProfile")]
public class ForestZoneProfile : ScriptableObject
{
    [Header("Zone shape (cells; local axes anchored at the arrival point)")]
    [Min(8)] public int zoneWidth = 40;     // across the road
    [Min(8)] public int zoneDepth = 30;     // arrival -> hand-built edge
    [Min(1)] public int pathHalfWidth = 1;
    [Min(1)] public int pathClearance = 3;  // tree-free corridor around the road

    [Header("Ground & scatter")]
    public TileBase grassTile;
    [Tooltip("Optional worn-road tile down the centre; null skips the road.")]
    public TileBase roadTile;
    public List<GameObject> treePrefabs = new();
    public List<GameObject> rockPrefabs = new();
    public List<GameObject> decorPrefabs = new();
    [Range(0f, 1f)] public float scatterDensity = 0.18f;

    [Header("Camp zones")]
    [Min(1f)] public float mainCampRadius = 5f;
    [Min(0)] public int mainCampForwardOffset = 8;   // cells from arrival toward edge
    [Min(0)] public int satelliteCampCount = 2;
    [Min(1f)] public float satelliteCampRadius = 3.5f;
    [Min(1)] public int satelliteMinDistanceFromMain = 12;
    public GameObject campZonePrefab;   // optional visual; else an empty is created

    [Header("Resource nodes (order does not matter; bands decide placement)")]
    public List<ResourceNodeType> nodeTypes = new();
    [Min(0)] public int nodeCount = 10;
    [Min(1)] public int nodeMinSpacing = 3;   // cells between nodes
}