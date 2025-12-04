using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runtime NavMesh baker for procedural terrain.
/// Automatically bakes NavMesh surfaces after procedural terrain generation completes.
/// Can be used with auto-bake enabled, or called explicitly via NavMeshBaker.Bake(GameObject root).
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

    private void Awake()
    {
        instance = this;
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
    /// </summary>
    /// <param name="root">The root GameObject containing the terrain meshes.</param>
    public void BakeFor(GameObject root)
    {
        if (root == null)
        {
            Debug.LogWarning("[NavMeshBaker] Cannot bake NavMesh: root GameObject is null.");
            return;
        }

        // Check if NavMeshSurface type is available
        if (!IsNavMeshSurfaceAvailable())
        {
            Debug.LogWarning("[NavMeshBaker] NavMeshSurface component not available. " +
                "Please install Unity's NavMeshComponents package:\n" +
                "1. Open Package Manager (Window > Package Manager)\n" +
                "2. Click '+' and select 'Add package from git URL'\n" +
                "3. Enter: https://github.com/Unity-Technologies/NavMeshComponents.git\n" +
                "Or clone the repository from https://github.com/Unity-Technologies/NavMeshComponents into your Assets folder.");
            return;
        }

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

    /// <summary>
    /// Static method to bake NavMesh for a given root GameObject.
    /// Can be called from the procedural terrain generator after generation completes.
    /// </summary>
    /// <param name="root">The root GameObject containing the terrain meshes.</param>
    public static void Bake(GameObject root)
    {
        if (root == null)
        {
            Debug.LogWarning("[NavMeshBaker] Cannot bake NavMesh: root GameObject is null.");
            return;
        }

        // Check if NavMeshSurface type is available
        if (!IsNavMeshSurfaceAvailable())
        {
            Debug.LogWarning("[NavMeshBaker] NavMeshSurface component not available. " +
                "Please install Unity's NavMeshComponents package:\n" +
                "1. Open Package Manager (Window > Package Manager)\n" +
                "2. Click '+' and select 'Add package from git URL'\n" +
                "3. Enter: https://github.com/Unity-Technologies/NavMeshComponents.git\n" +
                "Or clone the repository from https://github.com/Unity-Technologies/NavMeshComponents into your Assets folder.");
            return;
        }

        // Get or add NavMeshSurface component
        NavMeshSurface surface = root.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = NavMeshCollectObjects.Children;
        }

        // Build the NavMesh
        surface.BuildNavMesh();

        Debug.Log("[NavMeshBaker] NavMesh baked successfully for: " + root.name);
    }

    /// <summary>
    /// Checks if the NavMeshSurface type is available in the project.
    /// </summary>
    /// <returns>True if NavMeshSurface is available, false otherwise.</returns>
    private static bool IsNavMeshSurfaceAvailable()
    {
        // NavMeshSurface is part of UnityEngine.AI namespace when NavMeshComponents is installed
        // We check by trying to access the type - if it compiles, it's available
        // This method exists primarily for documentation; if the script compiles, NavMeshSurface is available
        try
        {
            var type = typeof(NavMeshSurface);
            return type != null;
        }
        catch
        {
            return false;
        }
    }
}
