using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// RandomObjectSpawner
/// - Supports two categories (Structures and Vegetation).
/// - Each category is now an array of PrefabEntry where each entry contains a prefab and its own count.
/// - Spawns each prefab the specified number of times using the same sampling logic as before.
/// - Creates category sub-folders under the spawn parent:
///     SpawnedStructures/Structures
///     SpawnedStructures/Vegetation
/// - Still creates a NavMeshSurface under the spawn parent and attaches a SpawnedObjectsNavMeshUpdater.
/// 
/// Usage:
/// - For each PrefabEntry set the prefab and the desired spawn count for that specific prefab.
/// - Tweak gridCellSize, maxTriesPerSpawn, edgePadding, etc. as before.
/// </summary>
[DisallowMultipleComponent]
public class RandomObjectSpawner : MonoBehaviour
{
    [System.Serializable]
    public class PrefabEntry
    {
        public string nameHint = "";
        [Tooltip("Prefab to spawn")]
        public GameObject prefab;
        [Tooltip("How many copies of this prefab to attempt to place.")]
        public int count = 1;
        [Tooltip("If true, prefab will receive a random Y rotation on spawn.")]
        public bool randomYRotation = true;
    }

    [Header("References")]
    public ProceduralTerrainGenerator generator;

    [Header("Prefab categories (each entry has its own count)")]
    [Tooltip("Structure prefabs - each entry has an independent count.")]
    public PrefabEntry[] structureEntries;

    [Tooltip("Vegetation prefabs - each entry has an independent count.")]
    public PrefabEntry[] vegetationEntries;

    [Header("Spawn Settings")]
    [Tooltip("Avoid spawning too close to terrain edges (world units).")]
    public float edgePadding = 0f;

    [Header("Grid")]
    public Vector2 gridOriginXZ = Vector2.zero;
    public float gridCellSize = 4f;
    public Vector3 snapOffset = Vector3.zero;

    [Header("Ground Detection (Raycast)")]
    public LayerMask groundMask = ~0;
    public float rayStartPadding = 10f;
    public float maxRayDistance = 2000f;

    [Header("Sampling")]
    [Tooltip("Maximum sampling attempts per single spawn (per object).")]
    public int maxTriesPerSpawn = 40;

    [Header("NavMesh (post-spawn)")]
    public float extraBakeDelay = 0.05f;
    public bool navBakeVerbose = true;

    [Header("Spawn container")]
    public string spawnContainerName = "SpawnedStructures";

    [Header("Object-surface (auto-create)")]
    public string objectSurfaceName = "SpawnedObjectsNavMeshSurface";
    public bool createAndBakeObjectSurface = true;

    [Header("Construction Terrain Integration")]
    [Tooltip("If true, set spawned objects to the construction layer from generator.constructionLayerMask.")]
    public bool enforceConstructionLayer = true;
    [Tooltip("If true, automatically add ConstructionTerrainNotifier to spawned objects.")]
    public bool autoAddConstructionNotifier = true;

    // runtime bounds / grid indices
    private bool hasBounds;
    private Vector3 minBound, maxBound;
    private int gridXMin, gridXMax, gridZMin, gridZMax;

    private void Awake()
    {
        if (generator == null)
            generator = SceneFind.First<ProceduralTerrainGenerator>();
    }

    private void OnEnable()
    {
        if (generator != null)
            generator.OnInitialTerrainReady += HandleInitialReady;
    }

    private void OnDisable()
    {
        if (generator != null)
            generator.OnInitialTerrainReady -= HandleInitialReady;
    }

    private void Start()
    {
        if (generator != null && generator.IsInitialTerrainReady)
            HandleInitialReady();
    }

    private void HandleInitialReady()
    {
        BuildBoundsFromConfig();
        BuildGridIndexRanges();
        StartCoroutine(SpawnInitialRoutine());
    }

    private IEnumerator SpawnInitialRoutine()
    {
        yield return null;

        Transform spawnParent = EnsureSpawnParent();

        // Spawn structure entries under "Structures"
        if (structureEntries != null && structureEntries.Length > 0)
        {
            Transform structuresParent = EnsureCategoryParent(spawnParent, "Structures");
            SpawnEntries(structureEntries, structuresParent);
        }

        // Spawn vegetation entries under "Vegetation"
        if (vegetationEntries != null && vegetationEntries.Length > 0)
        {
            Transform vegetationParent = EnsureCategoryParent(spawnParent, "Vegetation");
            SpawnEntries(vegetationEntries, vegetationParent);
        }

        yield return new WaitForEndOfFrame();
        if (extraBakeDelay > 0f)
            yield return new WaitForSeconds(extraBakeDelay);

        if (createAndBakeObjectSurface && spawnParent != null)
        {
            var surf = EnsureObjectSurfaceOnSpawnParent(spawnParent);
            if (surf != null)
            {
                if (navBakeVerbose) Debug.Log($"[RandomObjectSpawner] Baking object NavMeshSurface '{surf.name}' for spawned objects...");
                surf.BuildNavMesh();
                if (navBakeVerbose) Debug.Log("[RandomObjectSpawner] Object NavMeshSurface bake complete.");
            }
            else
            {
                if (navBakeVerbose) Debug.LogWarning("[RandomObjectSpawner] Failed to create/find object NavMeshSurface under spawn parent.");
            }
        }
    }

    private Transform EnsureSpawnParent()
    {
        if (generator != null)
        {
            Transform t = generator.transform.Find(spawnContainerName);
            if (t != null) return t;

            GameObject go = new GameObject(spawnContainerName);
            go.transform.SetParent(generator.transform, false);
            go.transform.localPosition = Vector3.zero;
            if (navBakeVerbose) Debug.Log($"[RandomObjectSpawner] Created spawn container '{spawnContainerName}' under generator.");
            return go.transform;
        }

        Transform fallback = transform.Find(spawnContainerName);
        if (fallback != null) return fallback;

        GameObject fgo = new GameObject(spawnContainerName);
        fgo.transform.SetParent(transform, false);
        fgo.transform.localPosition = Vector3.zero;
        if (navBakeVerbose) Debug.Log($"[RandomObjectSpawner] Created fallback spawn container '{spawnContainerName}' under spawner.");
        return fgo.transform;
    }

    // Ensure a category sub-parent under the spawn parent (e.g., "Vegetation" or "Structures")
    private Transform EnsureCategoryParent(Transform spawnParent, string categoryName)
    {
        if (spawnParent == null) return null;
        Transform t = spawnParent.Find(categoryName);
        if (t != null) return t;

        GameObject go = new GameObject(categoryName);
        go.transform.SetParent(spawnParent, false);
        go.transform.localPosition = Vector3.zero;
        if (navBakeVerbose) Debug.Log($"[RandomObjectSpawner] Created category container '{categoryName}' under spawn parent.");
        return go.transform;
    }

    // Creates or finds a NavMeshSurface GameObject under spawnParent and configures it to CollectObjects.Children.
    // Also attaches/sets up SpawnedObjectsNavMeshUpdater on spawnParent so child add/remove triggers a rebuild.
    private NavMeshSurface EnsureObjectSurfaceOnSpawnParent(Transform spawnParent)
    {
        if (spawnParent == null) return null;

        Transform existing = spawnParent.Find(objectSurfaceName);
        NavMeshSurface surface;
        if (existing != null)
        {
            surface = existing.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = existing.gameObject.AddComponent<NavMeshSurface>();
        }
        else
        {
            GameObject surfGo = new GameObject(objectSurfaceName);
            surfGo.transform.SetParent(spawnParent, false);
            surfGo.transform.localPosition = Vector3.zero;
            surface = surfGo.AddComponent<NavMeshSurface>();
            if (navBakeVerbose) Debug.Log($"[RandomObjectSpawner] Created object NavMeshSurface '{objectSurfaceName}' under spawn container.");
        }

        surface.collectObjects = CollectObjects.Children;

        // Attach/update the spawn-parent updater so child changes trigger rebuilds
        var updater = spawnParent.GetComponent<SpawnedObjectsNavMeshUpdater>();
        if (updater == null)
        {
            updater = spawnParent.gameObject.AddComponent<SpawnedObjectsNavMeshUpdater>();
            // default settings can be tuned in inspector
            updater.debounceSeconds = 0.15f;
            updater.extraBakeDelay = 0.05f;
            updater.verbose = navBakeVerbose;
        }
        updater.targetSurface = surface;

        return surface;
    }

    // Spawn each entry's prefab the requested number of times
    private void SpawnEntries(PrefabEntry[] entries, Transform parent)
    {
        if (entries == null || entries.Length == 0) return;
        if (!hasBounds) BuildBoundsFromConfig();
        if (!hasBounds) return;
        if (parent == null) return;

        float cell = Mathf.Max(0.0001f, gridCellSize);

        // Precompute all grid cells
        var cells = new List<Vector2Int>();
        for (int gx = gridXMin; gx <= gridXMax; gx++)
            for (int gz = gridZMin; gz <= gridZMax; gz++)
                cells.Add(new Vector2Int(gx, gz));

        if (cells.Count == 0) return;

        // For each entry, attempt to spawn its requested count
        foreach (var entry in entries)
        {
            if (entry == null || entry.prefab == null || entry.count <= 0) continue;

            for (int n = 0; n < entry.count; n++)
            {
                bool placed = false;
                for (int t = 0; t < Mathf.Max(1, maxTriesPerSpawn) && !placed; t++)
                {
                    var cellIndex = cells[Random.Range(0, cells.Count)];
                    float x = gridOriginXZ.x + cellIndex.x * cell;
                    float z = gridOriginXZ.y + cellIndex.y * cell;

                    Vector3 pos = FindGroundGridPosition(x, z, cell);
                    if (!float.IsNaN(pos.y))
                    {
                        Quaternion rot = entry.prefab.transform.rotation;
                        if (entry.randomYRotation)
                        {
                            rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                        }

                        GameObject instance;
                        if (parent != null)
                            instance = Instantiate(entry.prefab, pos, rot, parent);
                        else
                            instance = Instantiate(entry.prefab, pos, rot);

                        // Apply construction layer if enabled
                        if (enforceConstructionLayer && generator != null && generator.constructionLayerMask != 0)
                        {
                            int constructionLayer = GetFirstSetBit(generator.constructionLayerMask);
                            if (constructionLayer >= 0)
                            {
                                SetLayerRecursively(instance, constructionLayer);
                            }
                        }

                        // Add ConstructionTerrainNotifier if enabled
                        if (autoAddConstructionNotifier && generator != null)
                        {
                            var notifier = instance.GetComponent<ConstructionTerrainNotifier>();
                            if (notifier == null)
                            {
                                notifier = instance.AddComponent<ConstructionTerrainNotifier>();
                            }
                            notifier.terrain = generator;
                        }

                        // Notify generator of the spawned instance's bounds
                        if (generator != null)
                        {
                            Bounds bounds = ComputeBounds(instance);
                            if (bounds.size.sqrMagnitude > 0.001f)
                            {
                                generator.NotifyConstructionChanged(bounds);
                            }
                        }

                        placed = true;
                    }
                }
            }
        }
    }

    private Vector3 FindGroundGridPosition(float x, float z, float cellSize)
    {
        float topY = (generator != null && generator.config != null)
            ? generator.config.worldOffset.y + (generator.config.chunksY * generator.config.chunkSizeY * generator.config.voxelScale) + Mathf.Max(0f, rayStartPadding)
            : maxBound.y + Mathf.Max(0f, rayStartPadding);

        Vector3 rayOrigin = new Vector3(x, topY, z);

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Max(1000f, maxRayDistance), groundMask, QueryTriggerInteraction.Ignore))
        {
            float yOrigin = 0f;
            int gridY = Mathf.FloorToInt((hit.point.y - yOrigin) / cellSize);
            float y = yOrigin + gridY * cellSize;
            return new Vector3(x, y, z) + snapOffset;
        }

        return new Vector3(x, float.NaN, z);
    }

    private void BuildBoundsFromConfig()
    {
        hasBounds = false;
        if (generator == null || generator.config == null) return;

        var cfg = generator.config;

        float chunkExtentXZ = cfg.chunkSizeXZ * cfg.voxelScale;
        float chunkExtentY  = cfg.chunkSizeY * cfg.voxelScale;

        float width  = cfg.chunksX * chunkExtentXZ;
        float height = cfg.chunksY * chunkExtentY;
        float length = cfg.chunksZ * chunkExtentXZ;

        Vector3 min = cfg.worldOffset;
        Vector3 max = cfg.worldOffset + new Vector3(width, height, length);

        min.x += edgePadding; min.z += edgePadding;
        max.x -= edgePadding; max.z -= edgePadding;

        minBound = min;
        maxBound = max;
        hasBounds = true;
    }

    private void BuildGridIndexRanges()
    {
        if (!hasBounds) return;
        float cell = Mathf.Max(0.0001f, gridCellSize);

        gridXMin = Mathf.CeilToInt((minBound.x - gridOriginXZ.x) / cell);
        gridXMax = Mathf.FloorToInt((maxBound.x - gridOriginXZ.x) / cell);
        gridZMin = Mathf.CeilToInt((minBound.z - gridOriginXZ.y) / cell);
        gridZMax = Mathf.FloorToInt((maxBound.z - gridOriginXZ.y) / cell);
    }

    /// <summary>
    /// Get the first set bit in a layer mask to determine the construction layer.
    /// </summary>
    private int GetFirstSetBit(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Compute bounds from colliders (preferred) or renderers (fallback).
    /// </summary>
    private Bounds ComputeBounds(GameObject obj)
    {
        if (obj == null) return new Bounds(Vector3.zero, Vector3.zero);

        // Try colliders first
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
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
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        // No colliders or renderers
        return new Bounds(obj.transform.position, Vector3.one * 0.5f);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (structureEntries != null)
        {
            foreach (var e in structureEntries)
            {
                if (e != null && e.count < 0) e.count = 0;
            }
        }
        if (vegetationEntries != null)
        {
            foreach (var e in vegetationEntries)
            {
                if (e != null && e.count < 0) e.count = 0;
            }
        }
        if (gridCellSize <= 0f) gridCellSize = 1f;
        if (maxTriesPerSpawn < 1) maxTriesPerSpawn = 1;
    }
#endif

    // Optional helper: set GameObject and its children to a layer at spawn time.
    public void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}