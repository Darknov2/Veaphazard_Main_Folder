using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Attach to your enemy prefab. It follows the Player automatically via NavMeshAgent,
/// without any target reference in the inspector.
/// 
/// How it finds the player:
/// - First tries to find an object with PlayerController
/// - Then falls back to a GameObject tagged "Player"
/// - Finally falls back to Camera.main (or its parent) if available
/// 
/// Notes:
/// - Ensure you have a baked NavMesh on the terrain at runtime.
/// - Make sure your Player object does NOT have a missing MonoBehaviour component in the Inspector.
///   If you see "The referenced script on this Behaviour (Game Object 'Player') is missing!",
///   remove the missing component and add PlayerController.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyFollowPlayer : MonoBehaviour
{
    [Header("Movement")]
    public float repathInterval = 0.25f;
    public float stoppingDistance = 1.5f;

    private NavMeshAgent agent;
    private Transform playerTransform;
    private float nextRepathTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.stoppingDistance = stoppingDistance;

        // Resolve player once on Awake (no public target reference)
        playerTransform = ResolvePlayerTransform();
    }

    private void Update()
    {
        // If player not found yet (e.g. scene still initializing), retry
        if (playerTransform == null)
            playerTransform = ResolvePlayerTransform();

        if (agent == null || playerTransform == null) return;

        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + Mathf.Max(0.05f, repathInterval);
            agent.SetDestination(playerTransform.position);
        }
    }

    private Transform ResolvePlayerTransform()
    {
        // 1) Preferred: PlayerController
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null) return pc.transform;

        // 2) Tag "Player"
        var go = GameObject.FindWithTag("Player");
        if (go != null) return go.transform;

        // 3) Camera main parent or camera
        if (Camera.main != null && Camera.main.transform.parent != null)
            return Camera.main.transform.parent;
        if (Camera.main != null)
            return Camera.main.transform;

        return null;
    }
}