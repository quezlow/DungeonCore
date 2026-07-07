using System.Collections.Generic;
using UnityEngine;

public class ItemDictionary : MonoBehaviour
{
    public List<Item> itemPrefabs;
    private Dictionary<int, GameObject> itemDictionary;

    private void Awake()
    {
        itemDictionary = new Dictionary<int, GameObject>();

        // Serialised IDs are the single source of truth. List order no longer
        // matters: entries are validated here, never renumbered.
        for (int i = 0; i < itemPrefabs.Count; i++)
        {
            Item item = itemPrefabs[i];
            if (item == null)
            {
                Debug.LogError($"ItemDictionary: null entry at index {i} - remove or fill it.");
                continue;
            }
            if (item.ID <= 0)
            {
                Debug.LogError($"ItemDictionary: '{item.Name}' has no valid ID (found {item.ID}). " +
                               "Set a unique positive ID on the prefab, or re-run the content generator.");
                continue;
            }
            if (itemDictionary.TryGetValue(item.ID, out GameObject holder))
            {
                Debug.LogError($"ItemDictionary: duplicate ID {item.ID} - '{item.Name}' collides with " +
                               $"'{holder.GetComponent<Item>().Name}'. Give one of them a new unique ID.");
                continue;
            }
            itemDictionary[item.ID] = item.gameObject;
        }
    }

    public GameObject GetItemPrefab(int itemID)
    {
        itemDictionary.TryGetValue(itemID, out GameObject prefab);
        if(prefab == null)
        {
            Debug.LogWarning($"Item with ID {itemID} not found in dictionary");
        }
        return prefab;
    }
}
