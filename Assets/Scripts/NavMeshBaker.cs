using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runtime NavMesh baker for procedural terrain.
/// Automatically bakes NavMesh surfaces after procedural terrain generation completes.
/// Can be used with auto-bake enabled, or called explicitly via NavMeshBaker.Bake(GameObject root).
/// 
/// Requires Unity's NavMeshComponents package to be installed.
/// </summary>
public class NavMeshBaker : MonoBehaviour
{
    [Header("Auto-Bake Settings")]
    [Tooltip("If true, automatically bakes NavMesh on Start after finding the terrain.")]
    public bool autoBake = true;

    [Tooltip("If set, uses this as the terrain root. Otherwise, searches by tag.")]
    public GameObject terrainRoot;

    [Tooltip("Tag used to find the procedural terrain if terrainRoot is not set.")]
    public string tagToFind = "ProceduralTerrain";

    [Tooltip("Delay in seconds before baking to ensure mesh data is ready.")]
    public float bakeDelay = 0.5f;

    [Header("NavMeshSurface Configuration")]
    [Tooltip("Specifies which objects to collect for baking (All, Volume, or Children).")]
    public NavMeshCollectObjects collectObjects = NavMeshCollectObjects.Children;

    [Tooltip("If true, uses the layer mask to filter objects.")]
    public bool useLayers = false;

    [Tooltip("Layer mask for filtering objects when useLayers is true.")]
    public LayerMask layerMask = ~0;

    private static NavMeshBaker instance;

    /// <summary>
    /// Gets the current instance of NavMeshBaker, if one exists.
    /// </summary>
    public static NavMeshBaker Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[NavMeshBaker] Multiple NavMeshBaker instances detected. Using the first one.");
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Start()
    {
        if (autoBake)
        {
            StartCoroutine(AutoBakeCoroutine());
        }
    }

    /// <summary>
    /// Coroutine that waits for the terrain to be available and then bakes the NavMesh.
    /// </summary>
    private IEnumerator AutoBakeCoroutine()
    {
        // Wait for end of frame to ensure all Start methods have run
        yield return new WaitForEndOfFrame();

        GameObject root = terrainRoot;

        // If no terrainRoot is set, try to find by tag
        if (root == null && !string.IsNullOrEmpty(tagToFind))
        {
            root = GameObject.FindWithTag(tagToFind);

            // If still not found, wait a bit and try again
            float timeout = 10f;
            float elapsed = 0f;
            while (root == null && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
                root = GameObject.FindWithTag(tagToFind);
            }
        }

        if (root == null)
        {
            Debug.LogWarning("[NavMeshBaker] Could not find terrain root. " +
                "Set terrainRoot directly or ensure a GameObject with tag '" + tagToFind + "' exists.");
            yield break;
        }

        // Wait for the optional bake delay
        if (bakeDelay > 0f)
        {
            yield return new WaitForSeconds(bakeDelay);
        }

        // Perform the bake
        BakeFor(root);
    }

    /// <summary>
    /// Bakes the NavMesh for the specified root GameObject.
    /// Adds or retrieves a NavMeshSurface component and builds the navmesh.
    /// Uses the instance configuration settings for collectObjects and layerMask.
    /// </summary>
    /// <param name="root">The root GameObject containing the terrain meshes.</param>
    public void BakeFor(GameObject root)
    {
        if (root == null)
        {
            Debug.LogWarning("[NavMeshBaker] Cannot bake NavMesh: root GameObject is null.");
            return;
        }

        BakeInternal(root, collectObjects, useLayers, layerMask);
    }

    /// <summary>
    /// Static method to bake NavMesh for a given root GameObject.
    /// Can be called from the procedural terrain generator after generation completes.
    /// Uses default settings: collectObjects = Children, no layer filtering.
    /// </summary>
    /// <param name="root">The root GameObject containing the terrain meshes.</param>
    public static void Bake(GameObject root)
    {
        Bake(root, NavMeshCollectObjects.Children, false, ~0);
    }

    /// <summary>
    /// Static method to bake NavMesh for a given root GameObject with custom settings.
    /// Can be called from the procedural terrain generator after generation completes.
    /// </summary>
    /// <param name="root">The root GameObject containing the terrain meshes.</param>
    /// <param name="collectObjects">Specifies which objects to collect for baking.</param>
    /// <param name="useLayers">If true, uses the layer mask to filter objects.</param>
    /// <param name="layerMask">Layer mask for filtering objects when useLayers is true.</param>
    public static void Bake(GameObject root, NavMeshCollectObjects collectObjects, bool useLayers, LayerMask layerMask)
    {
        if (root == null)
        {
            Debug.LogWarning("[NavMeshBaker] Cannot bake NavMesh: root GameObject is null.");
            return;
        }

        BakeInternal(root, collectObjects, useLayers, layerMask);
    }

    /// <summary>
    /// Internal shared baking logic used by both instance and static methods.
    /// </summary>
    private static void BakeInternal(GameObject root, NavMeshCollectObjects collectObjects, bool useLayers, LayerMask layerMask)
    {
        // Get or add NavMeshSurface component
        NavMeshSurface surface = root.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = root.AddComponent<NavMeshSurface>();
        }

        // Configure the surface
        surface.collectObjects = collectObjects;
        
        if (useLayers)
        {
            surface.layerMask = layerMask;
        }

        // Build the NavMesh
        surface.BuildNavMesh();

        Debug.Log("[NavMeshBaker] NavMesh baked successfully for: " + root.name);
    }
}
