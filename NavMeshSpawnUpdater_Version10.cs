using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// SpawnedObjectsNavMeshUpdaterFast
/// - Rebuilds the object NavMeshSurface when children change.
/// - Rebuilds immediately on removals (fast) and debounces additions.
/// - Use this if you need a faster reaction and want to keep an older updater in the project.
/// </summary>
[DisallowMultipleComponent]
public class SpawnedObjectsNavMeshUpdaterFast : MonoBehaviour
{
    [Tooltip("NavMeshSurface to rebuild when children change. If null the component will try to find one in children.")]
    public NavMeshSurface targetSurface;

    [Tooltip("Debounce time (seconds) to wait after a child change before rebuilding (used for additions/other changes).")]
    public float debounceSeconds = 0.05f;

    [Tooltip("Extra small delay after end-of-frame before rebuilding (lets spawned objects finalize meshes).")]
    public float extraBakeDelay = 0.05f;

    [Tooltip("Minimum time (seconds) between immediate rebuilds triggered by removals.")]
    public float rebuildCooldownSeconds = 0.1f;

    [Tooltip("Enable verbose debug logs.")]
    public bool verbose = true;

    Coroutine rebuildCoroutine;
    private int lastChildCount = -1;
    private float lastImmediateRebuildTime = -999f;

    private void Awake()
    {
        lastChildCount = transform.childCount;
    }

    private void Start()
    {
        // If there are already children, schedule an initial rebuild (debounced)
        if (transform.childCount > 0)
        {
            if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
            rebuildCoroutine = StartCoroutine(DebouncedRebuildCoroutine());
        }
    }

    // Called by Unity when the transform's children change (add/remove/reparent).
    private void OnTransformChildrenChanged()
    {
        if (!enabled) return;

        int current = transform.childCount;
        if (lastChildCount < 0) lastChildCount = current;

        // Removal detected -> rebuild immediately (subject to cooldown)
        if (current < lastChildCount)
        {
            float now = Time.time;
            if (now - lastImmediateRebuildTime >= rebuildCooldownSeconds)
            {
                lastImmediateRebuildTime = now;
                if (verbose) Debug.Log("[SpawnedObjectsNavMeshUpdaterFast] Child REMOVED — triggering immediate rebuild.");
                if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
                rebuildCoroutine = null;
                StartCoroutine(ImmediateRebuildCoroutine());
            }
            else
            {
                if (verbose) Debug.Log("[SpawnedObjectsNavMeshUpdaterFast] Removal detected but within cooldown — using debounced rebuild.");
                if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
                rebuildCoroutine = StartCoroutine(DebouncedRebuildCoroutine());
            }
        }
        else
        {
            // Addition or other change -> debounce
            if (verbose) Debug.Log("[SpawnedObjectsNavMeshUpdaterFast] Child changed (add/modify) — scheduling debounced rebuild.");
            if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
            rebuildCoroutine = StartCoroutine(DebouncedRebuildCoroutine());
        }

        lastChildCount = current;
    }

    // Public: request a rebuild immediately (cancels debounce)
    public void RequestImmediateRebuild()
    {
        if (rebuildCoroutine != null) StopCoroutine(rebuildCoroutine);
        rebuildCoroutine = null;
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
        // run as soon as possible but allow one frame for Unity to update hierarchies
        yield return null;
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
            if (verbose) Debug.LogWarning("[SpawnedObjectsNavMeshUpdaterFast] No NavMeshSurface assigned or found under spawn container. Skipping rebuild.");
            return;
        }

        if (verbose) Debug.Log("[SpawnedObjectsNavMeshUpdaterFast] Rebuilding object NavMeshSurface due to children change.");
        targetSurface.BuildNavMesh();
    }
}