using UnityEngine;

/// <summary>
/// Notifies ProceduralTerrainGenerator when construction objects move, rotate, or are created/destroyed.
/// Automatically finds the terrain generator and debounces notifications to avoid spam.
/// </summary>
public class ConstructionTerrainNotifier : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ProceduralTerrainGenerator to notify. If null, will auto-find on Start.")]
    public ProceduralTerrainGenerator terrain;

    [Header("Notification Thresholds")]
    [Tooltip("Minimum position change (meters) to trigger notification.")]
    public float positionThreshold = 0.1f;
    [Tooltip("Minimum rotation change (degrees) to trigger notification.")]
    public float rotationThreshold = 5f;
    [Tooltip("Minimum time (seconds) between notifications (debounce).")]
    public float notifyCooldown = 0.5f;
    [Tooltip("If true, notify terrain immediately on Start.")]
    public bool notifyOnStart = true;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private float lastNotifyTime = -999f;

    private void Start()
    {
        // Auto-find terrain if not assigned
        if (terrain == null)
        {
            terrain = SceneFind.First<ProceduralTerrainGenerator>();
            if (terrain == null)
            {
                Debug.LogWarning($"[ConstructionTerrainNotifier] No ProceduralTerrainGenerator found in scene for {gameObject.name}");
            }
        }

        lastPosition = transform.position;
        lastRotation = transform.rotation;

        if (notifyOnStart)
        {
            NotifyTerrainImmediate();
        }
    }

    private void Update()
    {
        if (terrain == null) return;

        // Check if position or rotation changed beyond threshold
        bool posChanged = Vector3.Distance(transform.position, lastPosition) > positionThreshold;
        bool rotChanged = Quaternion.Angle(transform.rotation, lastRotation) > rotationThreshold;

        if ((posChanged || rotChanged) && Time.time >= lastNotifyTime + notifyCooldown)
        {
            NotifyTerrainImmediate();
        }
    }

    private void OnDestroy()
    {
        // Notify terrain that this object is being removed
        NotifyTerrainImmediate();
    }

    /// <summary>
    /// Immediately notify the terrain of this object's bounds, ignoring cooldown.
    /// </summary>
    public void NotifyTerrainImmediate()
    {
        if (terrain == null) return;

        Bounds bounds = ComputeBounds();
        if (bounds.size.sqrMagnitude > 0.001f)
        {
            terrain.NotifyConstructionChanged(bounds);
            lastNotifyTime = Time.time;
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }
    }

    /// <summary>
    /// Compute bounds from colliders (preferred) or renderers (fallback).
    /// </summary>
    private Bounds ComputeBounds()
    {
        // Try colliders first (more accurate for construction)
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
            return bounds;
        }

        // Fallback to renderers
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        // No colliders or renderers, use transform position with small default size
        return new Bounds(transform.position, Vector3.one * 0.5f);
    }
}
