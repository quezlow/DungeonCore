using UnityEngine;

/// <summary>
/// Applies a completed scout session's mana spend to the core after the gameplay
/// scene has reloaded. Runs after DungeonSaveController (exec order 100) so the
/// core has restored its mana first.
/// </summary>
[DefaultExecutionOrder(200)]
public class ScoutReturnApplier : MonoBehaviour
{
    private void Start()
    {
        if (RunContext.ScoutSpend <= 0f) return;
        if (DungeonCore.Instance == null) return;

        DungeonCore.Instance.SpendMana(RunContext.ScoutSpend);
        RunContext.ScoutSpend = 0f;
        DungeonSaveController.Instance?.RequestAutosave();
    }
}