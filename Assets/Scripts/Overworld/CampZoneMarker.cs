using UnityEngine;

/// <summary>
/// A reserved camp region on the surface. Placed by SurfaceZoneGenerator.
/// Carries a stable, immutable id the camp-formation system binds its
/// survivor-count flags to. Holds no camp state itself.
/// </summary>
public class CampZoneMarker : MonoBehaviour
{
    [SerializeField] private string zoneId;   // immutable: "camp.main", "camp.sat.1"...
    [SerializeField] private float radius = 5f;

    public string ZoneId => zoneId;
    public float Radius => radius;

    public void Init(string id, float r) { zoneId = id; radius = r; }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.6f, 0.15f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}