using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns enemy prefabs on top of the procedural marching-cubes terrain
/// AFTER the terrain has finished its initial generation.
/// 
/// - Subscribes to ProceduralTerrainGenerator.OnInitialTerrainReady
/// - Picks random XZ inside the terrain footprint from ProceduralTerrainConfig
/// - Raycasts down to the terrain to find the surface
/// - Optional slope check, NavMesh validation, and normal alignment
/// 
/// Note: Removed the 'Enemies Parent' reference. Enemies are now instantiated directly
/// in the scene without a parent transform.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("References")]
    public ProceduralTerrainGenerator generator;

    [Header("Prefabs")]
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

    [Header("Placement")]
    [Tooltip("Terrain layers to raycast against when searching for ground.")]
    public LayerMask groundMask = ~0;
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

    [Header("NavMesh Validation")]
    [Tooltip("If true, only accept positions that are on/near the NavMesh.")]
    public bool requireNavMesh = false;
    [Tooltip("Max distance to snap a valid ground point to the NavMesh.")]
    public float navMeshMaxSnapDistance = 2f;
    [Tooltip("NavMesh areas bitmask (default = all areas).")]
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Sampling")]
    [Tooltip("Tries this many positions per enemy before giving up.")]
    public int maxTriesPerSpawn = 25;

    // Cached config bounds
    private bool hasBounds;
    private Vector3 minBound, maxBound;

    private readonly List<GameObject> alive = new List<GameObject>(128);
    private Coroutine waveRoutine;

    private void Reset()
    {
        generator = GetComponent<ProceduralTerrainGenerator>();
    }

    private void Awake()
    {
        if (generator == null)
            generator = FindFirstObjectByType<ProceduralTerrainGenerator>();
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

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
        }
    }

    private void Start()
    {
        // If terrain was already generated (e.g., scene reloaded during play)
        if (generator != null && generator.IsInitialTerrainReady)
            HandleInitialReady();
    }

    private void HandleInitialReady()
    {
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
        // give one frame for any NavMesh bake to complete if it runs right after terrain ready
        yield return null;
        SpawnMany(initialSpawnCount);
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

    private void SpawnMany(int count)
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;
        if (!hasBounds) BuildBoundsFromConfig();

        for (int i = 0; i < count; i++)
        {
            if (!TryFindSpawnPoint(out Vector3 pos, out Quaternion rot)) continue;

            var prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];
            var go = Instantiate(prefab, pos, rot); // no parent

            alive.Add(go);

            // If the prefab has a NavMeshAgent, make sure it's on the NavMesh
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null && !agent.isOnNavMesh)
            {
                // If we required NavMesh, this should already be valid; but just in case:
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, navMeshMaxSnapDistance, navMeshAreaMask))
                    go.transform.position = hit.position;
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

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Infinity, groundMask, QueryTriggerInteraction.Ignore))
            {
                // Slope check
                if (Vector3.Dot(hit.normal, Vector3.up) < minUpDot)
                    continue;

                Vector3 spawnPos = hit.point + hit.normal * Mathf.Max(0f, spawnYOffset);

                // Optional NavMesh validation/snap
                if (requireNavMesh)
                {
                    if (!NavMesh.SamplePosition(spawnPos, out NavMeshHit nmh, navMeshMaxSnapDistance, navMeshAreaMask))
                        continue; // try another point

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