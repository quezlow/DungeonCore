using UnityEngine;

/// <summary>
/// Camera-only surface scout. Active only when RunContext.ScoutMode is set (the
/// scout HUD button sets it before loading the Forest scene). Moves a camera
/// target out from the entrance spawn, clamped to the researched reach radius,
/// draining the mana budget faster the farther it looks. When the budget is
/// spent -- or the player recalls -- it writes the spend back and returns to the
/// gameplay scene. The normal walking body (dev travel button) never sets
/// ScoutMode, so this stays dormant during ordinary Forest visits.
/// </summary>
public class ScoutController : MonoBehaviour
{
    [SerializeField] private ScoutTierProfile profile;
    [Tooltip("Spawn point the scout is measured from -- reuse the forest arrival.")]
    [SerializeField] private string originSpawnId = "FromDungeonEntrance";
    [Tooltip("The transform the scene camera follows in scout mode (e.g. a vcam Follow target, or the camera itself).")]
    [SerializeField] private Transform cameraTarget;
    [Tooltip("Optional: player root to hide while scouting.")]
    [SerializeField] private GameObject playerRoot;

    private Vector3 origin;
    private float maxRadius;
    private bool active;

    private void Start()
    {
        if (!RunContext.ScoutMode) { enabled = false; return; }   // ordinary visit; stay dormant
        if (profile == null || cameraTarget == null)
        {
            Debug.LogError("[ScoutController] Missing profile or camera target.");
            RecallImmediate();
            return;
        }

        if (playerRoot != null) playerRoot.SetActive(false);

        origin = ResolveOrigin();
        cameraTarget.position = origin;
        maxRadius = profile.MaxRadius();
        active = true;
    }

    private void Update()
    {
        if (!active) return;

        // Pan (reuse your input axes; arrows/WASD shown literally for clarity).
        float ix = Input.GetAxisRaw("Horizontal");
        float iy = Input.GetAxisRaw("Vertical");
        Vector3 next = cameraTarget.position
                     + new Vector3(ix, iy, 0f).normalized * profile.panSpeed * Time.unscaledDeltaTime;

        // Clamp to the researched reach.
        Vector3 rel = next - origin;
        if (rel.magnitude > maxRadius) rel = rel.normalized * maxRadius;
        cameraTarget.position = origin + rel;

        // Distance-based drain: cheap at the origin, dear at the edge.
        float dist = rel.magnitude;
        float rate = profile.baseCostPerSecond + profile.costPerUnitDistance * dist;
        RunContext.ScoutSpend += rate * Time.unscaledDeltaTime;

        if (RunContext.ScoutSpend >= RunContext.ScoutManaBudget) { Recall("Your sight fails -- the mana is spent."); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { Recall(null); return; }
    }

    // Hook a HUD "Return" button here too.
    public void Recall(string reason)
    {
        if (!active) return;
        active = false;
        RunContext.ScoutSpend = Mathf.Min(RunContext.ScoutSpend, RunContext.ScoutManaBudget);
        if (!string.IsNullOrEmpty(reason)) AlertsLog.Instance?.AddAlert(reason, Vector3.zero);
        RecallImmediate();
    }

    private void RecallImmediate()
    {
        string back = string.IsNullOrEmpty(RunContext.ScoutReturnScene) ? "Dungeon_Level_0" : RunContext.ScoutReturnScene;
        RunContext.EndScout();
        SceneLoader.FadeToScene(back);
    }

    private Vector3 ResolveOrigin()
    {
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
            if (sp.SpawnPointID == originSpawnId) return sp.transform.position;
        return transform.position;
    }
}