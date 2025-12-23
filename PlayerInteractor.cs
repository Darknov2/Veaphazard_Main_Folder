using System;
using UnityEngine;
using TMPro;

/// <summary>
/// PlayerInteractor (centralized center-screen raycast + pickup/break handling)
/// - Performs a center-screen raycast and exposes a public RaycastCenter(...) API so other scripts (PlayerController etc.)
///   can use the same raycast logic and masks.
/// - Handles interaction key (pickupKey) to either Break a TreeSegment (if present) or pick up a PickupItem.
/// - Supports auto-pickup on trigger/collision when enabled.
/// - Defensive about Inventory / PickupItem API differences and logs when debugLogs is enabled.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Input / Raycast")]
    public KeyCode pickupKey = KeyCode.E;
    public float maxDistance = 3f;
    public Camera aimCamera;
    public LayerMask pickupLayerMask = Physics.DefaultRaycastLayers;

    [Header("Inventory / Player")]
    public Inventory playerInventory;
    public string playerTag = "Player";

    [Header("UI Prompt (optional)")]
    public TextMeshProUGUI promptText;
    public string promptFormat = "Press {0} to pick up {1} x{2}";

    [Header("Auto-pickup (collision/trigger)")]
    [Tooltip("Enable automatic pickup on contact for objects on the chosen layer mask.")]
    public bool enableAutoPickup = false;
    [Tooltip("Layers that will be considered for automatic (collision/trigger) pickup.")]
    public LayerMask autoPickupLayerMask = 0;

    [Header("Debug")]
    public bool debugLogs = false;

    // currently targeted pickup (used for prompt)
    private PickupItem currentTargetPickup;

    void Awake()
    {
        if (aimCamera == null && Camera.main != null) aimCamera = Camera.main;
        if (promptText != null) promptText.gameObject.SetActive(false);
    }

    void Update()
    {
        UpdatePrompt();

        // Interaction key: prefer tree breaks when a TreeSegment is present; otherwise try pickup.
        if (Input.GetKeyDown(pickupKey))
        {
            RaycastHit hit;
            if (!RaycastCenter(out hit))
                return;

            // If this is part of a tree (TreeSegment on the hit or any parent), break it
            var treeSeg = hit.transform.GetComponentInParent<TreeSegment>();
            if (treeSeg != null)
            {
                if (debugLogs) Debug.Log($"[PlayerInteractor] Interact: TreeSegment '{treeSeg.gameObject.name}' hit -> calling Break().");
                treeSeg.Break();
                return;
            }

            // Otherwise, try to pick up
            var pickup = hit.transform.GetComponentInParent<PickupItem>() ?? hit.transform.GetComponentInChildren<PickupItem>() ?? hit.transform.GetComponent<PickupItem>();
            if (pickup != null)
            {
                if (debugLogs) Debug.Log($"[PlayerInteractor] Interact: PickupItem '{pickup.gameObject.name}' hit -> attempting pickup.");
                TryPickupExecute(pickup);
                return;
            }

            if (debugLogs) Debug.Log("[PlayerInteractor] Interact: hit nothing pickable or breakable.");
        }
    }

    private void UpdatePrompt()
    {
        if (promptText == null) return;

        var pickup = FindPickupUnderCrosshair();
        if (pickup != null && pickup.item != null)
        {
            promptText.gameObject.SetActive(true);
            promptText.text = string.Format(promptFormat, pickupKey.ToString(), pickup.item.itemName, pickup.amount);
        }
        else
        {
            promptText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Public: performs the center-screen raycast using this interactor's camera and masks.
    /// Returns true and outputs RaycastHit when hit is found.
    /// Optional overrides:
    /// - maxDistanceOverride: if >= 0 uses that distance, otherwise uses this.maxDistance.
    /// - maskOverride: if provided uses that mask, otherwise uses this.pickupLayerMask.
    /// Note: the layer mask is converted to an int bitmask explicitly (mask.value) before calling Physics.Raycast.
    /// </summary>
    public bool RaycastCenter(out RaycastHit hit, float maxDistanceOverride = -1f, LayerMask? maskOverride = null)
    {
        hit = default;
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return false;

        float dist = (maxDistanceOverride >= 0f) ? maxDistanceOverride : maxDistance;
        LayerMask mask = maskOverride.HasValue ? maskOverride.Value : pickupLayerMask;
        int layerMaskInt = mask.value; // explicit int bitmask

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool result = Physics.Raycast(ray, out hit, dist, layerMaskInt, QueryTriggerInteraction.Collide);

        if (debugLogs && result)
        {
            var go = hit.collider != null ? hit.collider.gameObject : null;
            if (go != null) Debug.Log($"[PlayerInteractor] Raycast hit '{go.name}' (layer={LayerMask.LayerToName(go.layer)}) at distance {hit.distance} using mask int {layerMaskInt}");
        }

        return result;
    }

    /// <summary>
    /// Convenience: returns the Collider under the center crosshair or null.
    /// </summary>
    public Collider GetTargetColliderUnderCrosshair(float maxDistanceOverride = -1f, LayerMask? maskOverride = null)
    {
        RaycastHit hit;
        if (RaycastCenter(out hit, maxDistanceOverride, maskOverride))
            return hit.collider;
        return null;
    }

    /// <summary>
    /// Attempt to pick up the given PickupItem into the player's inventory and destroy if configured.
    /// </summary>
    private void TryPickupExecute(PickupItem pickup)
    {
        if (pickup == null) return;

        EnsurePlayerInventory();
        if (playerInventory == null)
        {
            Debug.LogWarning("[PlayerInteractor] No Inventory found to receive pickup.");
            return;
        }

        int leftover = AttemptTryPickup(pickup, playerInventory);
        if (leftover <= 0)
        {
            if (pickup.destroyOnPickup)
            {
                try { Destroy(pickup.gameObject); } catch { }
            }
            if (debugLogs) Debug.Log($"[PlayerInteractor] Picked up '{pickup.item?.name ?? "unknown"}' from '{pickup.gameObject.name}'.");
        }
        else
        {
            pickup.amount = leftover;
            if (debugLogs) Debug.Log($"[PlayerInteractor] Partial pickup: {leftover} remain on '{pickup.gameObject.name}'.");
        }
    }

    private int AttemptTryPickup(PickupItem pickup, Inventory inv)
    {
        if (pickup == null || inv == null) return 0;

        try
        {
            return pickup.TryPickup(inv);
        }
        catch (MissingMethodException)
        {
            // fallback: try reflection if method signature differs
            try
            {
                var method = typeof(PickupItem).GetMethod("TryPickup", new Type[] { typeof(Inventory) });
                if (method != null)
                {
                    var res = method.Invoke(pickup, new object[] { inv });
                    if (res is int r) return r;
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[PlayerInteractor] Reflection TryPickup failed: {ex.Message}");
            }
            return pickup.amount;
        }
        catch (Exception ex)
        {
            if (debugLogs) Debug.LogWarning($"[PlayerInteractor] Exception when calling TryPickup: {ex.Message}");
            return pickup.amount;
        }
    }

    /// <summary>
    /// Raycast and return a PickupItem under the crosshair if present.
    /// </summary>
    private PickupItem FindPickupUnderCrosshair()
    {
        RaycastHit hit;
        if (!RaycastCenter(out hit)) return null;

        var pickup = hit.transform.GetComponentInParent<PickupItem>() ?? hit.transform.GetComponentInChildren<PickupItem>() ?? hit.transform.GetComponent<PickupItem>();
        if (pickup != null) currentTargetPickup = pickup;
        else currentTargetPickup = null;
        return currentTargetPickup;
    }

    private void EnsurePlayerInventory()
    {
        if (playerInventory != null) return;

        if (!string.IsNullOrEmpty(playerTag))
        {
            try
            {
                var playerGo = GameObject.FindWithTag(playerTag);
                if (playerGo != null)
                {
                    playerInventory = playerGo.GetComponentInChildren<Inventory>() ?? playerGo.GetComponent<Inventory>();
                    if (playerInventory != null) return;
                }
            }
            catch { /* ignore */ }
        }

        var sceneFindType = Type.GetType("SceneFind");
        if (sceneFindType != null)
        {
            var method = sceneFindType.GetMethod("First", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method != null)
            {
                try
                {
                    var generic = method.MakeGenericMethod(typeof(Inventory));
                    var result = generic.Invoke(null, new object[] { });
                    playerInventory = result as Inventory;
                    if (playerInventory != null) return;
                }
                catch { /* ignore */ }
            }
        }

#if UNITY_2023_2_OR_NEWER
        playerInventory = UnityEngine.Object.FindFirstObjectByType<Inventory>();
#else
        playerInventory = FindObjectOfType<Inventory>();
#endif
    }

    // Auto-pickup on contact if enabled and layer matches
    void OnTriggerEnter(Collider other)
    {
        TryAutoPickupFromCollider(other);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision != null) TryAutoPickupFromCollider(collision.collider);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit != null && hit.collider != null) TryAutoPickupFromCollider(hit.collider);
    }

    private void TryAutoPickupFromCollider(Collider other)
    {
        if (other == null) return;
        if (!enableAutoPickup) return;
        if (autoPickupLayerMask.value == 0) return;

        if (IsInLayerMask(other.gameObject.layer, autoPickupLayerMask) || PickupOwnerIsInLayerMask(other, autoPickupLayerMask))
        {
            var pickup = other.GetComponent<PickupItem>() ?? other.GetComponentInParent<PickupItem>() ?? other.GetComponentInChildren<PickupItem>();
            if (pickup == null) return;

            EnsurePlayerInventory();
            if (playerInventory == null) return;

            int leftover = AttemptTryPickup(pickup, playerInventory);
            if (leftover <= 0)
            {
                if (pickup.destroyOnPickup)
                {
                    try { Destroy(pickup.gameObject); } catch { }
                }
            }
            else
            {
                pickup.amount = leftover;
            }
        }
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private bool PickupOwnerIsInLayerMask(Collider c, LayerMask mask)
    {
        var pickup = c.GetComponent<PickupItem>() ?? c.GetComponentInParent<PickupItem>() ?? c.GetComponentInChildren<PickupItem>();
        if (pickup == null) return false;
        return IsInLayerMask(pickup.gameObject.layer, mask);
    }

    void OnDrawGizmosSelected()
    {
        Camera cam = aimCamera != null ? aimCamera : Camera.main;
        if (cam == null) return;

        Gizmos.color = Color.yellow;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Gizmos.DrawLine(ray.origin, ray.origin + ray.direction.normalized * maxDistance);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, pickupLayerMask.value, QueryTriggerInteraction.Collide))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(hit.point, 0.08f);
        }
    }
}