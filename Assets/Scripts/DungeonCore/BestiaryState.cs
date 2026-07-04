using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which monster types the player has "discovered" by slaying one in the wild.
/// A discovery-gated MonsterDefinition (requiresDiscovery) stays out of the build menu
/// until its name appears here - killing the invading animal is how the core learns to
/// field it. Persisted with the save.
///
/// SCENE SETUP: put this on a persistent manager GameObject (alongside the other
/// singletons, e.g. FactionSystem). No inspector references are required.
/// </summary>
public class BestiaryState : MonoBehaviour
{
    public static BestiaryState Instance { get; private set; }

    private readonly HashSet<string> discovered = new();

    /// <summary>Raised (with the monster name) whenever a new type is discovered.</summary>
    public static event Action<string> OnDiscovered;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Static, null-safe convenience for gates: false when no instance exists.</summary>
    public static bool Discovered(string monsterName)
        => Instance != null && Instance.IsDiscovered(monsterName);

    public bool IsDiscovered(string monsterName)
        => !string.IsNullOrEmpty(monsterName) && discovered.Contains(monsterName);

    public void Discover(string monsterName)
    {
        if (string.IsNullOrEmpty(monsterName)) return;
        if (discovered.Add(monsterName))
        {
            Debug.Log($"[Bestiary] Discovered '{monsterName}' - now available to field.");
            OnDiscovered?.Invoke(monsterName);
        }
    }

    public IReadOnlyCollection<string> AllDiscovered => discovered;

    public BestiarySaveData GetSaveData() => new BestiarySaveData { discovered = new List<string>(discovered) };

    public void RestoreFromSave(BestiarySaveData data)
    {
        discovered.Clear();
        if (data?.discovered != null)
            foreach (var n in data.discovered) discovered.Add(n);
    }
}

[Serializable]
public class BestiarySaveData
{
    public List<string> discovered = new();
}