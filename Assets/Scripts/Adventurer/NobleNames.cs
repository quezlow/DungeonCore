using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Name pool for noble adventurers and the vengeance parties their deaths summon.
/// A noble reads as "Title Given House" (e.g. "Lord Aldric Ravenscroft"); the House is
/// the single trailing word, which the retaliation reads back to name the avenging family.
///
/// SETUP: Create > Dungeon > Noble Names, drop the asset on the AdventurerSpawner's
/// Noble Names field. The pools below ship with working defaults; edit freely.
/// </summary>
[CreateAssetMenu(fileName = "NobleNames", menuName = "Dungeon/Noble Names")]
public class NobleNames : ScriptableObject
{
    [SerializeField]
    private List<string> maleFirstNames = new()
    { "Aldric", "Cedric", "Roland", "Baldwin", "Percival", "Tristan", "Gareth", "Edmund", "Reginald", "Alaric" };

    [SerializeField]
    private List<string> femaleFirstNames = new()
    { "Seraphine", "Miriel", "Rosalind", "Guinevere", "Eleanor", "Isolde", "Beatrix", "Cordelia", "Vivienne", "Adelaide" };

    [SerializeField]
    private List<string> houseNames = new()
    { "Ravenscroft", "Duskbane", "Thornwood", "Grimmond", "Ashcroft", "Blackmoor", "Wolveshire", "Greymantle", "Hollowell", "Stormvale" };

    /// <summary>A fresh noble: random title, given name, and house.</summary>
    public string Generate()
    {
        bool lady = Random.value < 0.5f;
        string given = Pick(lady ? femaleFirstNames : maleFirstNames, lady ? "Alys" : "John");
        string house = Pick(houseNames, "Ashcroft");
        return $"{(lady ? "Lady" : "Lord")} {given} {house}";
    }

    /// <summary>A vengeful kinsman of a named house: random title + given, house forced.</summary>
    public string GenerateWithHouse(string house)
    {
        if (string.IsNullOrEmpty(house)) return Generate();
        bool lady = Random.value < 0.5f;
        string given = Pick(lady ? femaleFirstNames : maleFirstNames, lady ? "Alys" : "John");
        return $"{(lady ? "Lady" : "Lord")} {given} {house}";
    }

    private static string Pick(List<string> pool, string fallback)
        => (pool != null && pool.Count > 0) ? pool[Random.Range(0, pool.Count)] : fallback;
}