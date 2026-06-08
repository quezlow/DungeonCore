using TMPro;
using UnityEngine;

/// <summary>
/// DAY 31 PART 3B — World-space visual indicator above a MonsterSpawner.
///
/// Shows a countdown timer while the spawner is mid-respawn, swaps to a
/// "!" icon if respawn is currently blocked by a nearby hostile, and
/// hides entirely while a monster is alive.
///
/// PREFAB SETUP — child of the spawner prefab:
///   SpawnerRespawnIndicator (Canvas — World Space, scale ~0.01)
///   ├── CountdownRoot     (GameObject, holds the timer label)
///   │   └── CountdownLabel (TMP_Text, centre aligned)
///   └── BlockedRoot       (GameObject with a "!" TMP_Text or Image)
///
/// Inspector wiring:
///   spawner          → leave null; auto-resolves via GetComponentInParent
///   countdownRoot    → CountdownRoot GameObject
///   countdownLabel   → CountdownLabel TMP_Text
///   blockedRoot      → BlockedRoot GameObject
///   worldOffset      → roughly half the spawner sprite height
///
/// SAFE TO OMIT — the indicator is optional. The spawner exposes
/// IsRespawning, IsBlocked, and RespawnTimerRemaining for any custom UI.
/// </summary>
public class SpawnerRespawnIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MonsterSpawner spawner;
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownLabel;
    [SerializeField] private GameObject blockedRoot;

    [Header("Layout")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.7f, 0f);

    private void Awake()
    {
        if (spawner == null) spawner = GetComponentInParent<MonsterSpawner>();
        HideAll();
    }

    private void LateUpdate()
    {
        if (spawner == null) { HideAll(); return; }

        transform.position = spawner.transform.position + worldOffset;

        if (!spawner.IsRespawning)
        {
            HideAll();
            return;
        }

        if (spawner.IsBlocked)
        {
            if (countdownRoot != null) countdownRoot.SetActive(false);
            if (blockedRoot != null) blockedRoot.SetActive(true);
        }
        else
        {
            if (countdownRoot != null) countdownRoot.SetActive(true);
            if (blockedRoot != null) blockedRoot.SetActive(false);
            if (countdownLabel != null)
                countdownLabel.text = Mathf.CeilToInt(spawner.RespawnTimerRemaining).ToString();
        }
    }

    private void HideAll()
    {
        if (countdownRoot != null) countdownRoot.SetActive(false);
        if (blockedRoot != null) blockedRoot.SetActive(false);
    }
}