using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// Watches the transform children on this GameObject and rebuilds the assigned NavMeshSurface
/// when children are added/removed. Debounced to avoid repeated immediate rebuilds.
/// Attach this to the spawn container GameObject (the parent of spawned objects).
/// </summary>
[DisallowMultipleComponent]
public class SpawnedObjectsNavMeshUpdater : MonoBehaviour
{
    [Tooltip("NavMeshSurface to rebuild when children change. If null the component will try to find one in children.")]
    public NavMeshSurface targetSurface;

    [Tooltip("Debounce time (seconds) to wait after a child change before rebuilding.")]
    public float debounceSeconds = 0.15f;

    [Tooltip("Extra small delay after end-of-frame before rebuilding (lets spawned objects finalize meshes).")]
    public float extraBakeDelay = 0.05f;

    [Tooltip("Enable verbose debug logs.")]
    public bool verbose = true;

    Coroutine rebuildCoroutine;

    // Called by Unity when the transform's children change (add/remove/reparent).
    private void OnTransformChildrenChanged()
    {
        if (!enabled) return;

        // Restart debounce timer
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        rebuildCoroutine = StartCoroutine(DebouncedRebuildCoroutine());
    }

    // Public: request a rebuild immediately (cancels debounce)
    public void RequestImmediateRebuild()
    {
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        StartCoroutine(ImmediateRebuildCoroutine());
    }

    // Public: request a rebuild after a custom delay
    public void RequestRebuildAfterDelay(float delaySeconds)
    {
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        rebuildCoroutine = StartCoroutine(RebuildAfterDelayCoroutine(delaySeconds));
    }

    private IEnumerator DebouncedRebuildCoroutine()
    {
        yield return new WaitForSeconds(debounceSeconds);
        yield return new WaitForEndOfFrame();
        if (extraBakeDelay > 0f) yield return new WaitForSeconds(extraBakeDelay);
        DoRebuild();
        rebuildCoroutine = null;
    }

    private IEnumerator ImmediateRebuildCoroutine()
    {
        yield return null; // allow one frame
        if (extraBakeDelay > 0f) yield return new WaitForSeconds(extraBakeDelay);
        DoRebuild();
    }

    private IEnumerator RebuildAfterDelayCoroutine(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        yield return new WaitForEndOfFrame();
        if (extraBakeDelay > 0f) yield return new WaitForSeconds(extraBakeDelay);
        DoRebuild();
        rebuildCoroutine = null;
    }

    private void DoRebuild()
    {
        if (targetSurface == null)
        {
            // try to auto-find a NavMeshSurface under this GameObject
            targetSurface = GetComponentInChildren<NavMeshSurface>(true);
        }

        if (targetSurface == null)
        {
            if (verbose) Debug.LogWarning("[SpawnedObjectsNavMeshUpdater] No NavMeshSurface assigned or found under spawn container. Skipping rebuild.");
            return;
        }

        if (verbose) Debug.Log("[SpawnedObjectsNavMeshUpdater] Rebuilding object NavMeshSurface due to children change.");
        targetSurface.BuildNavMesh();
    }

    // Optional: on start ensure initial state is baked
    private void Start()
    {
        // If there are already children, optionally trigger an initial rebuild (debounced)
        if (transform.childCount > 0)
        {
            if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
            rebuildCoroutine = StartCoroutine(DebouncedRebuildCoroutine());
        }
    }
}