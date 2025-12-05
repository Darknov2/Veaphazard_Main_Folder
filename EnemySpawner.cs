using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
#if UNITY_2022_2_OR_NEWER
using Unity.AI.Navigation;
#endif

/// <summary>
/// Spawns enemy prefabs on top of the procedural marching-cubes terrain
/// AFTER the terrain has finished its initial generation and (optionally) NavMesh is ready.
/// 
/// Key features:
/// - Subscribes to ProceduralTerrainGenerator.OnInitialTerrainReady OR waits for manual NotifyTerrainReady() call
/// - Supports multiple enemy prefabs with random selection
/// - Picks random XZ inside the terrain footprint from ProceduralTerrainConfig
/// - Uses Terrain.SampleHeight or raycasts down to find the surface
/// - Optional slope check, NavMesh validation, and normal alignment
/// - Places NavMeshAgents onto the NavMesh using NavMesh.SamplePosition and agent.Warp
/// 
/// IMPORTANT: Each enemy script must find the player on its own (e.g., via tag lookup 
/// or FindObjectOfType). The spawner only handles terrain placement and navmesh readiness;
/// it does NOT set player references on spawned enemies.
/// 
/// Note: Enemies are instantiated directly in the scene without a parent transform.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("References")]
    public ProceduralTerrainGenerator generator;
    
    [Tooltip("Optional reference to the procedural terrain root. Used for Terrain.SampleHeight if available, and optionally for NavMesh baking.")]
    public Transform terrainRoot;

    [Header("Prefabs")]
    [Tooltip("Array of enemy prefabs to spawn. A random non-null prefab is selected each spawn.")]
    public GameObject[] enemyPrefabs;

    [Header("Spawn Timing")]
    [Tooltip("How many enemies to spawn once terrain is ready.")]
    public int initialSpawnCount = 10;
    [Tooltip("Optionally spawn waves over time.")]
    public bool spawnWaves = false;
    public float waveIntervalSeconds = 15f;
    public int perWaveCount = 3;
    [Tooltip("Maximum alive enemies if waves are enabled (0 = unlimited).")]
    public int aliveCap = 30;

    [Header("Procedural Terrain Coordination")]
    [Tooltip("When true, spawner waits for NotifyTerrainReady() to be called before spawning. When false, uses generator.OnInitialTerrainReady event.")]
    public bool waitForTerrainGeneration = false;
    [Tooltip("Max seconds to wait for terrain ready signal before proceeding with a warning.")]
    public float terrainReadyTimeout = 60f;
    [Tooltip("If true and terrainRoot has a NavMeshSurface, call BuildNavMesh() when terrain becomes ready.")]
    public bool buildNavMeshOnNotify = false;

    [Header("Placement")]
    [Tooltip("Terrain layers to raycast against when searching for ground (used if no Terrain component found on terrainRoot).")]
    public LayerMask terrainLayerMask = ~0;
    [Tooltip("Avoid spawning too close to the terrain edges (world units).")]
    public float edgePadding = 2f;
    [Tooltip("Maximum allowed ground slope for a valid spawn (degrees).")]
    [Range(0f, 89f)] public float maxSlope = 45f;
    [Tooltip("Small lift above the hit point to avoid clipping.")]
    public float spawnYOffset = 0.05f;
    [Tooltip("Rotate spawned enemy so its up aligns to the ground normal.")]
    public bool alignToGroundNormal = true;
    [Tooltip("Align forward to a random direction on the ground plane.")]
    public bool randomYawOnGround = true;

    [Header("NavMesh Readiness")]
    [Tooltip("If true, wait for NavMesh triangulation to be available before spawning.")]
    public bool requireNavMeshBeforeSpawning = false;
    [Tooltip("Max seconds to wait for NavMesh before proceeding with a warning.")]
    public float navMeshWaitTimeout = 30f;
    [Tooltip("Distance used with NavMesh.SamplePosition to snap spawned agents to the NavMesh.")]
    public float navMeshSampleMaxDistance = 2f;
    [Tooltip("NavMesh areas bitmask (default = all areas).")]
    public int navMeshAreaMask = NavMesh.AllAreas;
    [Tooltip("If true, log a warning when an agent can't be placed on the NavMesh.")]
    public bool warnWhenAgentNotOnNavMesh = true;

    [Header("Sampling")]
    [Tooltip("Tries this many positions per enemy before giving up.")]
    public int maxTriesPerSpawn = 25;

    [Header("Events")]
    [Tooltip("Invoked when spawning is about to begin (after terrain and NavMesh are ready).")]
    public UnityEvent onSpawningStarted;
    [Tooltip("Invoked when the initial spawn batch is complete.")]
    public UnityEvent onInitialSpawnComplete;

    // Backwards compatibility alias for groundMask
    [System.Obsolete("Use terrainLayerMask instead.")]
    public LayerMask groundMask { get => terrainLayerMask; set => terrainLayerMask = value; }
    
    // Backwards compatibility alias for navMeshMaxSnapDistance
    [System.Obsolete("Use navMeshSampleMaxDistance instead.")]
    public float navMeshMaxSnapDistance { get => navMeshSampleMaxDistance; set => navMeshSampleMaxDistance = value; }
    
    // Backwards compatibility alias for requireNavMesh
    [System.Obsolete("Use requireNavMeshBeforeSpawning instead.")]
    public bool requireNavMesh { get => requireNavMeshBeforeSpawning; set => requireNavMeshBeforeSpawning = value; }

    // Cached config bounds
    private bool hasBounds;
    private Vector3 minBound, maxBound;

    private readonly List<GameObject> alive = new List<GameObject>(128);
    private Coroutine waveRoutine;
    
    // Terrain ready state
    private bool terrainReady = false;
    private bool spawningStarted = false;

    private void Reset()
    {
        generator = GetComponent<ProceduralTerrainGenerator>();
        if (generator != null)
            terrainRoot = generator.transform;
    }

    private void Awake()
    {
        if (generator == null)
            generator = FindFirstObjectByType<ProceduralTerrainGenerator>();
        
        // Auto-assign terrainRoot from generator if not set
        if (terrainRoot == null && generator != null)
            terrainRoot = generator.transform;
    }

    private void OnEnable()
    {
        // Only subscribe to generator event if not using manual terrain ready notification
        if (!waitForTerrainGeneration && generator != null)
            generator.OnInitialTerrainReady += HandleInitialReady;
    }

    private void OnDisable()
    {
        if (generator != null)
            generator.OnInitialTerrainReady -= HandleInitialReady;

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
        }
    }

    private void Start()
    {
        if (waitForTerrainGeneration)
        {
            // Wait for manual NotifyTerrainReady() call with optional timeout
            StartCoroutine(WaitForTerrainReadyRoutine());
        }
        else
        {
            // If terrain was already generated (e.g., scene reloaded during play)
            if (generator != null && generator.IsInitialTerrainReady)
                HandleInitialReady();
        }
    }

    /// <summary>
    /// Call this method from your terrain generator when terrain generation (and optional NavMesh bake) is complete.
    /// This signals the spawner to begin spawning enemies.
    /// </summary>
    public void NotifyTerrainReady()
    {
        if (terrainReady) return; // Already notified
        
        terrainReady = true;
        
        // Optionally build NavMesh on terrainRoot if it has a NavMeshSurface
        if (buildNavMeshOnNotify)
        {
            TryBuildNavMeshOnTerrainRoot();
        }
        
        HandleInitialReady();
    }

    private IEnumerator WaitForTerrainReadyRoutine()
    {
        float startTime = Time.time;
        
        while (!terrainReady)
        {
            if (terrainReadyTimeout > 0f && Time.time - startTime >= terrainReadyTimeout)
            {
                Debug.LogWarning($"[EnemySpawner] Terrain ready timeout ({terrainReadyTimeout}s) reached. Proceeding with spawn attempt.");
                terrainReady = true;
                HandleInitialReady();
                yield break;
            }
            yield return null;
        }
    }

    private void TryBuildNavMeshOnTerrainRoot()
    {
        if (terrainRoot == null) return;
        
#if UNITY_2022_2_OR_NEWER
        var navMeshSurface = terrainRoot.GetComponent<NavMeshSurface>();
        if (navMeshSurface != null)
        {
            navMeshSurface.BuildNavMesh();
            Debug.Log("[EnemySpawner] Built NavMesh on terrain root.");
        }
#else
        // For older Unity versions, try to find NavMeshSurface via reflection or log a message
        var surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
        if (surfaceType != null)
        {
            var surface = terrainRoot.GetComponent(surfaceType);
            if (surface != null)
            {
                var buildMethod = surfaceType.GetMethod("BuildNavMesh");
                if (buildMethod != null)
                {
                    buildMethod.Invoke(surface, null);
                    Debug.Log("[EnemySpawner] Built NavMesh on terrain root.");
                }
            }
        }
#endif
    }

    private void HandleInitialReady()
    {
        if (spawningStarted) return; // Prevent double invocation
        spawningStarted = true;
        
        BuildBoundsFromConfig();
        StartCoroutine(SpawnInitialRoutine());

        if (spawnWaves)
        {
            if (waveRoutine != null) StopCoroutine(waveRoutine);
            waveRoutine = StartCoroutine(WaveRoutine());
        }
    }

    private IEnumerator SpawnInitialRoutine()
    {
        // Wait for NavMesh if required
        if (requireNavMeshBeforeSpawning)
        {
            yield return StartCoroutine(WaitForNavMeshRoutine());
        }
        else
        {
            // Give one frame for any NavMesh bake to complete if it runs right after terrain ready
            yield return null;
        }
        
        // Fire spawning started event
        onSpawningStarted?.Invoke();
        
        SpawnMany(initialSpawnCount);
        
        // Fire initial spawn complete event
        onInitialSpawnComplete?.Invoke();
    }

    private IEnumerator WaitForNavMeshRoutine()
    {
        float startTime = Time.time;
        
        while (!IsNavMeshReady())
        {
            if (navMeshWaitTimeout > 0f && Time.time - startTime >= navMeshWaitTimeout)
            {
                Debug.LogWarning($"[EnemySpawner] NavMesh wait timeout ({navMeshWaitTimeout}s) reached. Proceeding with spawn attempt.");
                yield break;
            }
            yield return null;
        }
        
        Debug.Log("[EnemySpawner] NavMesh is ready. Proceeding with spawning.");
    }

    /// <summary>
    /// Checks if NavMesh triangulation is available (at least one vertex exists).
    /// </summary>
    private bool IsNavMeshReady()
    {
        var triangulation = NavMesh.CalculateTriangulation();
        return triangulation.vertices != null && triangulation.vertices.Length > 0;
    }

    private IEnumerator WaveRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.1f, waveIntervalSeconds));
        while (true)
        {
            if (aliveCap <= 0 || CountAlive() < aliveCap)
                SpawnMany(perWaveCount);
            yield return wait;
        }
    }

    private int CountAlive()
    {
        // Clean nulls
        for (int i = alive.Count - 1; i >= 0; i--)
            if (alive[i] == null) alive.RemoveAt(i);
        return alive.Count;
    }

    /// <summary>
    /// Spawns the specified count of enemies using random prefab selection.
    /// Each enemy is placed on the terrain surface and its NavMeshAgent is placed onto the NavMesh.
    /// </summary>
    public void SpawnMany(int count)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogWarning("[EnemySpawner] No enemy prefabs assigned.");
            return;
        }
        if (!hasBounds) BuildBoundsFromConfig();

        // Collect valid (non-null) prefabs
        var validPrefabs = new List<GameObject>();
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            if (enemyPrefabs[i] != null)
                validPrefabs.Add(enemyPrefabs[i]);
            else
                Debug.LogWarning($"[EnemySpawner] Enemy prefab at index {i} is null. Skipping.");
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("[EnemySpawner] All enemy prefab slots are null. Cannot spawn.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (!TryFindSpawnPoint(out Vector3 pos, out Quaternion rot)) continue;

            // Random prefab selection from valid prefabs
            var prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            var go = Instantiate(prefab, pos, rot); // no parent

            alive.Add(go);

            // Diagnostics: check for missing script slots on instantiated prefab
            DiagnoseMissingScripts(go);

            // Place NavMeshAgent onto the NavMesh
            TryPlaceAgentOnNavMesh(go);
        }
    }

    /// <summary>
    /// Gets a random spawn position on the terrain surface.
    /// Uses Terrain.SampleHeight if terrainRoot has a Terrain component,
    /// otherwise raycasts down using terrainLayerMask.
    /// Falls back to spawner's Y if neither succeeds.
    /// </summary>
    private Vector3 GetRandomSpawnPositionOnTerrain()
    {
        if (!hasBounds) BuildBoundsFromConfig();
        
        float x = Random.Range(minBound.x, maxBound.x);
        float z = Random.Range(minBound.z, maxBound.z);
        float y = transform.position.y; // Fallback Y
        
        // Try Terrain.SampleHeight if terrainRoot has Terrain component
        if (terrainRoot != null)
        {
            var terrain = terrainRoot.GetComponent<Terrain>();
            if (terrain != null)
            {
                y = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                return new Vector3(x, y + spawnYOffset, z);
            }
        }
        
        // Fallback: raycast down to find ground
        float topY = hasBounds ? maxBound.y + 50f : transform.position.y + 100f;
        Vector3 rayOrigin = new Vector3(x, topY, z);
        
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Infinity, terrainLayerMask, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
        }
        
        return new Vector3(x, y + spawnYOffset, z);
    }

    /// <summary>
    /// Attempts to place a NavMeshAgent onto the NavMesh using NavMesh.SamplePosition and agent.Warp.
    /// </summary>
    /// <param name="instance">The spawned enemy instance.</param>
    private void TryPlaceAgentOnNavMesh(GameObject instance)
    {
        if (instance == null) return;
        
        var agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) return;
        
        Vector3 pos = instance.transform.position;
        
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, navMeshSampleMaxDistance, navMeshAreaMask))
        {
            // Warp the agent to the valid NavMesh position
            agent.Warp(hit.position);
        }
        else
        {
            if (warnWhenAgentNotOnNavMesh)
            {
                Debug.LogWarning($"[EnemySpawner] Could not place agent '{instance.name}' on NavMesh at position {pos}. " +
                    $"Consider increasing navMeshSampleMaxDistance ({navMeshSampleMaxDistance}) or ensuring NavMesh is baked.");
            }
        }
    }

    /// <summary>
    /// Diagnostics helper: logs any missing script slots on the instantiated prefab.
    /// </summary>
    private void DiagnoseMissingScripts(GameObject instance)
    {
        if (instance == null) return;
        
        var components = instance.GetComponentsInChildren<Component>(true);
        foreach (var component in components)
        {
            if (component == null)
            {
                Debug.LogWarning($"[EnemySpawner] Missing script component detected on '{instance.name}' or its children. " +
                    "This may cause issues. Please fix the prefab by removing the missing script reference.");
            }
        }
    }

    private void BuildBoundsFromConfig()
    {
        hasBounds = false;
        if (generator == null || generator.config == null) return;

        var cfg = generator.config;

        float cellXZ = cfg.chunkSizeXZ * cfg.voxelScale;
        float cellY  = cfg.chunkSizeY  * cfg.voxelScale;

        float width  = cfg.chunksX * cellXZ;
        float length = cfg.chunksZ * cellXZ;
        float height = cfg.chunksY * cellY;

        minBound = cfg.worldOffset;
        maxBound = cfg.worldOffset + new Vector3(width, height, length);

        // Edge padding (XZ only)
        minBound.x += edgePadding;
        minBound.z += edgePadding;
        maxBound.x -= edgePadding;
        maxBound.z -= edgePadding;

        hasBounds = true;
    }

    private bool TryFindSpawnPoint(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (!hasBounds) return false;

        // Raycast start height slightly above the top of terrain stack
        float topY = maxBound.y + 50f;

        float minUpDot = Mathf.Cos(Mathf.Deg2Rad * Mathf.Clamp(maxSlope, 0f, 89f));

        for (int t = 0; t < Mathf.Max(1, maxTriesPerSpawn); t++)
        {
            float x = Random.Range(minBound.x, maxBound.x);
            float z = Random.Range(minBound.z, maxBound.z);
            Vector3 rayOrigin = new Vector3(x, topY, z);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Infinity, terrainLayerMask, QueryTriggerInteraction.Ignore))
            {
                // Slope check
                if (Vector3.Dot(hit.normal, Vector3.up) < minUpDot)
                    continue;

                Vector3 spawnPos = hit.point + hit.normal * Mathf.Max(0f, spawnYOffset);

                // Optional NavMesh validation/snap (uses the class-level requireNavMeshBeforeSpawning flag for spawn-point validation)
                // Note: This is separate from requireNavMeshBeforeSpawning which controls initial wait
                if (NavMesh.SamplePosition(spawnPos, out NavMeshHit nmh, navMeshSampleMaxDistance, navMeshAreaMask))
                {
                    spawnPos = nmh.position;
                }

                // Orientation
                Quaternion upAlign = alignToGroundNormal
                    ? Quaternion.FromToRotation(Vector3.up, hit.normal)
                    : Quaternion.identity;

                Quaternion yaw = Quaternion.identity;
                if (randomYawOnGround)
                {
                    // Pick a random forward projected onto the ground plane
                    Vector3 f = Random.onUnitSphere;
                    f = Vector3.ProjectOnPlane(f, hit.normal).normalized;
                    if (f.sqrMagnitude < 1e-3f) f = Vector3.forward;
                    yaw = Quaternion.LookRotation(f, alignToGroundNormal ? hit.normal : Vector3.up);
                }

                position = spawnPos;
                rotation = yaw * upAlign;
                return true;
            }
        }

        return false;
    }
}