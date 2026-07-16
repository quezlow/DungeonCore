using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Scout the Surface" HUD button. Shows only when a scout tier is researched.
/// Captures current mana as the session budget, saves the dungeon so the budget
/// matches what a return-load will restore, then loads the Forest scene in scout
/// mode (no player spawn).
/// </summary>
[RequireComponent(typeof(Button))]
public class ScoutHudButton : MonoBehaviour
{
    [SerializeField] private ScoutTierProfile profile;
    [SerializeField] private GameObject buttonRoot;   // shown/hidden by availability

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(BeginScout);
    }

    private void OnEnable() { UnlockState.OnChanged += Refresh; Refresh(); }
    private void OnDisable() { UnlockState.OnChanged -= Refresh; }

    private void Refresh(string changedKey = null)
    {
        bool available = profile != null && profile.AnyTierUnlocked();
        if (buttonRoot != null) buttonRoot.SetActive(available);
    }

    private void BeginScout()
    {
        if (DungeonCore.Instance == null || SceneLoader.Instance == null) return;
        if (profile == null || !profile.AnyTierUnlocked()) return;

        float budget = DungeonCore.Instance.CurrentMana;
        string here = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        DungeonSaveController.Instance?.SaveGame();   // budget == the mana a return-load restores
        RunContext.BeginScout(budget, here);
        SceneLoader.FadeToScene(SceneNames.GameScene.Forest.ToString());
    }
}