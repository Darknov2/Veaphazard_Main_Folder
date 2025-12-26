using UnityEngine;

/// <summary>
/// Component that notifies ProceduralTerrainGenerator when a construction object moves, rotates, or is destroyed
/// so that the terrain can recarve around it.
/// </summary>
public class ConstructionTerrainNotifier : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The terrain generator to notify. If null, will try to find one on Start.")]
    public ProceduralTerrainGenerator terrain;

    [Header("Settings")]
    [Tooltip("If true, notify terrain immediately on Start.")]
    public bool notifyOnStart = true;

    [Tooltip("Minimum movement distance (world units) before triggering a notification.")]
    public float movementNotifyThreshold = 0.5f;

    [Tooltip("Minimum rotation change (degrees) before triggering a notification.")]
    public float rotationNotifyThreshold = 15f;

    [Tooltip("Minimum time (seconds) between notifications to avoid spamming.")]
    public float notifyCooldown = 0.5f;

    // Internal state
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private float lastNotifyTime = -1000f;

    private void Start()
    {
        // Try to find terrain if not assigned
        if (terrain == null)
        {
            terrain = SceneFind.First<ProceduralTerrainGenerator>();
        }

        // Record initial transform
        lastPosition = transform.position;
        lastRotation = transform.rotation;

        // Notify immediately if requested
        if (notifyOnStart && terrain != null)
        {
            NotifyTerrainImmediate();
        }
    }

    private void Update()
    {
        if (terrain == null) return;

        // Check if cooldown has expired
        if (Time.time - lastNotifyTime < notifyCooldown) return;

        // Check for significant movement
        float moveDist = Vector3.Distance(transform.position, lastPosition);
        if (moveDist >= movementNotifyThreshold)
        {
            NotifyTerrainImmediate();
            return;
        }

        // Check for significant rotation
        float angleDiff = Quaternion.Angle(transform.rotation, lastRotation);
        if (angleDiff >= rotationNotifyThreshold)
        {
            NotifyTerrainImmediate();
            return;
        }
    }

    private void OnDestroy()
    {
        // Notify terrain one last time when destroyed (wrapped in try/catch for safety)
        try
        {
            if (terrain != null)
            {
                NotifyTerrainImmediate();
            }
        }
        catch (System.Exception)
        {
            // Silently ignore errors during destruction
        }
    }

    /// <summary>
    /// Immediately notify the terrain generator about this object's bounds.
    /// </summary>
    public void NotifyTerrainImmediate()
    {
        if (terrain == null) return;

        Bounds bounds = ComputeBounds();
        terrain.NotifyConstructionChanged(bounds);

        // Update tracking state
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastNotifyTime = Time.time;
    }

    /// <summary>
    /// Compute the world-space bounds of this object.
    /// Prefers collider bounds, falls back to renderer bounds.
    /// </summary>
    private Bounds ComputeBounds()
    {
        // Try to get bounds from colliders first
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                b.Encapsulate(colliders[i].bounds);
            }
            return b;
        }

        // Fallback to renderer bounds
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        // Last resort: small bounds around transform position
        return new Bounds(transform.position, Vector3.one * 2f);
    }
}
