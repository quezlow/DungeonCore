using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Singleton screen-shake via Cinemachine Impulse (Phase 3 closeout #8).
/// Put this on a persistent object (e.g. GameController) — it requires a
/// CinemachineImpulseSource on the same object. Add a CinemachineImpulseListener
/// to your CinemachineCamera so the camera actually reacts to the impulse.
/// </summary>
[RequireComponent(typeof(CinemachineImpulseSource))]
public class ScreenShake : MonoBehaviour
{
    public static ScreenShake Instance { get; private set; }

    [Tooltip("Impulse force used for a boss death.")]
    [SerializeField] private float bossDeathForce = 1.2f;

    private CinemachineImpulseSource source;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        source = GetComponent<CinemachineImpulseSource>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Fire a shake with an explicit force (scales the source's impulse).</summary>
    public void Shake(float force)
    {
        if (source == null) return;
        source.GenerateImpulseWithForce(force);
    }

    public void ShakeBossDeath() => Shake(bossDeathForce);
}