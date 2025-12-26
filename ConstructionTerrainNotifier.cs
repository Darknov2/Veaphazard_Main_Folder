using UnityEngine;

/// <summary>
/// Component that notifies the ProceduralTerrainGenerator when a construction object
/// is placed, moved, or destroyed, so terrain chunks can be carved around it.
/// 
/// Automatically computes bounds from colliders (preferred) or renderers (fallback).
/// Tracks position/rotation changes and notifies the generator when thresholds are exceeded.
/// </summary>
public class ConstructionTerrainNotifier : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the terrain generator. If null, will be auto-found on Start.")]
    public ProceduralTerrainGenerator terrain;

    [Header("Notification Settings")]
    [Tooltip("If true, notifies the generator immediately on Start (after a frame delay).")]
    public bool notifyOnStart = true;

    [Tooltip("Minimum position change (in world units) before triggering a notification.")]
    public float positionThreshold = 0.1f;

    [Tooltip("Minimum rotation change (in degrees) before triggering a notification.")]
    public float rotationThreshold = 1f;

    [Header("Debug")]
    public bool verbose = false;

    // Cached state for change detection
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private bool hasNotifiedOnce = false;

    private void Start()
    {
        // Auto-find terrain generator if not assigned
        if (terrain == null)
        {
            terrain = Utilities.FindFirstObjectByTypeCompat<ProceduralTerrainGenerator>();
            if (terrain == null && verbose)
            {
                Debug.LogWarning($"[ConstructionTerrainNotifier] No ProceduralTerrainGenerator found in scene for {gameObject.name}");
            }
        }

        // Initialize tracking state
        lastPosition = transform.position;
        lastRotation = transform.rotation;

        // Optional: notify on start (delayed one frame to ensure terrain is ready)
        if (notifyOnStart && terrain != null)
        {
            StartCoroutine(NotifyNextFrame());
        }
    }

    private System.Collections.IEnumerator NotifyNextFrame()
    {
        yield return null;
        NotifyTerrain("OnStart");
    }

    private void Update()
    {
        if (terrain == null) return;

        // Check for significant position or rotation change
        bool posChanged = Vector3.Distance(transform.position, lastPosition) > positionThreshold;
        bool rotChanged = Quaternion.Angle(transform.rotation, lastRotation) > rotationThreshold;

        if (posChanged || rotChanged)
        {
            NotifyTerrain("Movement/Rotation");
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }
    }

    private void OnDestroy()
    {
        // Notify terrain when this construction object is destroyed
        if (terrain != null)
        {
            NotifyTerrain("OnDestroy");
        }
    }

    /// <summary>
    /// Manually trigger a terrain notification. Useful when object is first placed.
    /// </summary>
    public void NotifyTerrainManual()
    {
        NotifyTerrain("Manual");
    }

    private void NotifyTerrain(string reason)
    {
        if (terrain == null) return;

        Bounds bounds = ComputeBounds();
        if (bounds.size.sqrMagnitude < 0.001f)
        {
            if (verbose)
                Debug.LogWarning($"[ConstructionTerrainNotifier] {gameObject.name} has zero bounds, skipping notification.");
            return;
        }

        if (verbose)
            Debug.Log($"[ConstructionTerrainNotifier] Notifying terrain for {gameObject.name} (reason: {reason}), bounds: {bounds}");

        // Call the generator's notification method (will be implemented in ProceduralTerrainGenerator)
        terrain.NotifyConstructionChanged(bounds);
        hasNotifiedOnce = true;
    }

    /// <summary>
    /// Compute world-space bounds from colliders (preferred) or renderers (fallback).
    /// </summary>
    private Bounds ComputeBounds()
    {
        // Try colliders first (more accurate for construction objects)
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds combined = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                combined.Encapsulate(colliders[i].bounds);
            }
            return combined;
        }

        // Fallback to renderers
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combined.Encapsulate(renderers[i].bounds);
            }
            return combined;
        }

        // No colliders or renderers found - use transform position with small default size
        return new Bounds(transform.position, Vector3.one);
    }
}
