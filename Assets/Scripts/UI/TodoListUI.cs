using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A player-authored to-do list living in the quest journal (below Completed). The player types
/// an item and adds it, ticks it off (strikethrough), or deletes it. Persists per-slot in the
/// dungeon save. Manages its OWN item container, separate from QuestLogUI's quest list, so the
/// two never clobber each other.
///
/// SCENE SETUP: sits on the quest-log panel alongside QuestLogUI. Assign Content (its own items
/// container, a sibling of the quest list under the same scroll), Todo Item Prefab (a row with a
/// Toggle "Check", a TMP_Text "Label", and a Button "Delete"), Input Field, and Add Button.
/// </summary>
public class TodoListUI : MonoBehaviour
{
    public static TodoListUI Instance { get; private set; }

    [SerializeField] private Transform content;
    [SerializeField] private GameObject todoItemPrefab;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button addButton;

    private readonly List<TodoItemSaveData> items = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (addButton != null) addButton.onClick.AddListener(AddFromInput);
        if (inputField != null) inputField.onSubmit.AddListener(_ => AddFromInput());
        Render();
    }

    public void AddFromInput()
    {
        if (inputField == null) return;
        string text = inputField.text != null ? inputField.text.Trim() : "";
        if (string.IsNullOrEmpty(text)) return;
        items.Add(new TodoItemSaveData { text = text, done = false });
        inputField.text = "";
        inputField.ActivateInputField();   // keep focus for rapid entry
        Render();
        Persist();
    }

    private void ToggleItem(TodoItemSaveData item)
    {
        item.done = !item.done;
        Render();
        Persist();
    }

    private void RemoveItem(TodoItemSaveData item)
    {
        items.Remove(item);
        Render();
        Persist();
    }

    private void Render()
    {
        if (content == null || todoItemPrefab == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        foreach (var item in items)
        {
            var it = item;   // capture for the closures below
            var row = Instantiate(todoItemPrefab, content);

            var check = row.transform.Find("Check")?.GetComponent<Toggle>();
            if (check != null)
            {
                check.SetIsOnWithoutNotify(it.done);
                check.onValueChanged.AddListener(_ => ToggleItem(it));
            }

            var label = row.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null)
            {
                label.text = it.text;
                label.fontStyle = it.done ? FontStyles.Strikethrough : FontStyles.Normal;
            }

            var del = row.transform.Find("Delete")?.GetComponent<Button>();
            if (del != null) del.onClick.AddListener(() => RemoveItem(it));
        }
    }

    private void Persist()
    {
        DungeonSaveController.Instance?.SaveGame();
    }

    // ---- save hooks (called by DungeonSaveController) ----
    public List<TodoItemSaveData> GetSaveData() => new(items);

    public void LoadSaveData(List<TodoItemSaveData> saved)
    {
        items.Clear();
        if (saved != null) items.AddRange(saved);
        Render();
    }
}