using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// InputTerrainBrush (precise, visibility-aware center-screen raycast)
/// - Performs RaycastAll and selects the first visible, front-facing surface under the crosshair.
/// - Avoids selecting geometry that is behind/inside other visible surfaces (prevents ray going through to cave holes).
/// - Left click = dig (remove), Right click = place (add). Dig adds groundItem to inventory; place consumes items.
/// </summary>
[DisallowMultipleComponent]
public class InputTerrainBrush : MonoBehaviour
{
    [Header("References")]
    public TerrainModificationManager modificationManager; // auto-find if null
    public Camera playerCamera; // optional, falls back to Camera.main

    [Header("Brush")]
    public float brushRadius = 2f;
    public float brushStrength = 10f;
    public float defaultFallbackDistance = 5f;

    [Header("Aim offset (screen normalized, -0.5..0.5)")]
    [Range(-0.5f, 0.5f)] public float aimXOffset = 0f;
    [Range(-0.5f, 0.5f) ] public float aimYOffset = 0f;

    [Header("Raycast")]
    [Tooltip("Layers to raycast against. Make sure this includes the layer(s) used by your visible terrain surface.")]
    public LayerMask raycastMask = ~0;
    public float maxRayDistance = 200f;

    [Header("Dig / Place integration (direct inventory)")]
    public ItemData groundItem;
    public int pickupAmountPerUnitStrength = 1;
    public int placeCostPerUnitStrength = 1;
    public Inventory playerInventory;
    public HotbarUI hotbarUI;
    public string playerTag = "Player";

    [Header("Safety / Hit requirements")]
    public bool requireRaycastHit = true;
    public bool requireSelectedSlotCheck = true;
    public string requiredHitTag = "";
    public bool requireHitHasModificationManager = false;

    [Header("Visibility")]
    [Tooltip("If true, the chosen hit must be within camera frustum and front-facing (normal towards camera).")]
    public bool requireVisibleAndFrontFacing = true;

    [Header("Misc")]
    public bool debugLogs = false;

    // preview
    private bool lastHitValid = false;
    private Vector3 lastHitPoint = Vector3.zero;

    private void Awake()
    {
        if (playerCamera == null && Camera.main != null) playerCamera = Camera.main;
        if (modificationManager == null)
            modificationManager = SceneFind.First<TerrainModificationManager>();
    }

    private void Update()
    {
        if (requireSelectedSlotCheck && !IsSelectedSlotAllowed()) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (TrySelectVisibleHit(out Vector3 worldPos, out RaycastHit hitInfo))
            {
                if (!IsHitAllowed(hitInfo)) return;
                float strength = -Mathf.Abs(brushStrength);
                ApplyBrush(worldPos, strength);
                HandleAfterDig(worldPos, strength);
            }
            else
            {
                if (debugLogs) Debug.Log("[InputTerrainBrush] No valid visible hit (dig cancelled).");
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            if (TrySelectVisibleHit(out Vector3 worldPos, out RaycastHit hitInfo))
            {
                if (!IsHitAllowed(hitInfo)) return;

                float requestedStrength = Mathf.Abs(brushStrength);
                if (groundItem == null)
                {
                    Debug.LogWarning("[InputTerrainBrush] groundItem not assigned; cannot place.");
                    return;
                }

                EnsurePlayerInventory();

                int requiredItems = Mathf.CeilToInt(requestedStrength * placeCostPerUnitStrength);
                if (requiredItems <= 0)
                {
                    ApplyBrush(worldPos, requestedStrength);
                    return;
                }

                int removed = 0;
                if (playerInventory != null) removed = playerInventory.RemoveItem(groundItem, requiredItems);

                if (removed <= 0)
                {
                    if (debugLogs) Debug.Log("[InputTerrainBrush] Not enough ground items to place.");
                    return;
                }

                float appliedStrength = requestedStrength * (removed / (float)requiredItems);
                ApplyBrush(worldPos, appliedStrength);
            }
            else
            {
                if (debugLogs) Debug.Log("[InputTerrainBrush] No valid visible hit (place cancelled).");
            }
        }
    }

    /// <summary>
    /// Performs a RaycastAll along the camera center ray and selects the first hit that is visible
    /// and front-facing (normal pointing toward camera). Falls back to nearest hit if visibility test disabled.
    /// Returns true if selection was successful and outputs world position + RaycastHit.
    /// </summary>
    private bool TrySelectVisibleHit(out Vector3 outWorldPos, out RaycastHit outHit)
    {
        outWorldPos = Vector3.zero;
        outHit = default;

        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null)
        {
            if (debugLogs) Debug.LogWarning("[InputTerrainBrush] No camera assigned.");
            return false;
        }

        float sx = Mathf.Clamp01(0.5f + aimXOffset);
        float sy = Mathf.Clamp01(0.5f + aimYOffset);
        Vector3 screenPoint = new Vector3(cam.pixelWidth * sx, cam.pixelHeight * sy, 0f);
        Ray ray = cam.ScreenPointToRay(screenPoint);

        int maskInt = raycastMask.value;

        // RaycastAll to get all intersections along view direction (closest first)
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, maskInt, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            if (debugLogs) Debug.Log("[InputTerrainBrush] RaycastAll found no hits on raycastMask.");
            return requireRaycastHit ? false : TryUseFallbackPoint(ray, out outWorldPos, out outHit);
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // If we don't require visibility/front-facing, simply pick the closest hit
        if (!requireVisibleAndFrontFacing)
        {
            outHit = hits[0];
            outWorldPos = outHit.point;
            StoreLastHit(outWorldPos);
            if (debugLogs) Debug.Log($"[InputTerrainBrush] Selected nearest hit: {outHit.collider.gameObject.name}");
            return true;
        }

        // Precompute frustum planes
        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);

        // Iterate hits and select the first that:
        //  - has a Renderer or TerrainCollider and whose bounds are in frustum
        //  - AND whose surface normal faces the camera (dot > 0)
        foreach (var h in hits)
        {
            var col = h.collider;
            if (col == null) continue;

            // prefer visible test on renderer bounds if available
            bool inFrustum = false;
            var rend = col.GetComponent<Renderer>();
            if (rend != null)
            {
                inFrustum = GeometryUtility.TestPlanesAABB(frustum, rend.bounds);
            }
            else
            {
                // accept TerrainCollider or colliders without renderer if their bounds are in frustum
                inFrustum = GeometryUtility.TestPlanesAABB(frustum, col.bounds);
            }

            if (!inFrustum)
            {
                // not visible in camera frustum => skip
                if (debugLogs) Debug.Log($"[InputTerrainBrush] Skipping hit (out of frustum): {col.gameObject.name}");
                continue;
            }

            // ensure the surface normal faces the camera
            Vector3 toCamera = (cam.transform.position - h.point).normalized;
            float facingDot = Vector3.Dot(h.normal, toCamera);

            if (facingDot <= 0f)
            {
                if (debugLogs) Debug.Log($"[InputTerrainBrush] Skipping hit (backface): {col.gameObject.name} dot={facingDot:F3}");
                continue;
            }

            // Passed both tests — choose this hit
            outHit = h;
            outWorldPos = h.point;
            StoreLastHit(outWorldPos);
            if (debugLogs) Debug.Log($"[InputTerrainBrush] Selected visible/front-facing hit: {col.gameObject.name} at {outWorldPos}");
            return true;
        }

        // No visible/front-facing hit found
        if (debugLogs) Debug.Log("[InputTerrainBrush] No visible/front-facing hit found among RaycastAll results.");

        // Fallback: if requireRaycastHit is false, use fallback point; otherwise fail
        return requireRaycastHit ? false : TryUseFallbackPoint(ray, out outWorldPos, out outHit);
    }

    private bool TryUseFallbackPoint(Ray ray, out Vector3 outWorldPos, out RaycastHit outHit)
    {
        outHit = default;
        outWorldPos = ray.origin + ray.direction.normalized * defaultFallbackDistance;
        StoreLastHit(outWorldPos);
        if (debugLogs) Debug.Log("[InputTerrainBrush] Using fallback forward point.");
        return true;
    }

    private void StoreLastHit(Vector3 wp)
    {
        lastHitValid = true;
        lastHitPoint = wp;
    }

    private bool IsHitAllowed(RaycastHit hit)
    {
        if (!string.IsNullOrEmpty(requiredHitTag))
        {
            if (!hit.transform.CompareTag(requiredHitTag) && !hit.transform.root.CompareTag(requiredHitTag))
            {
                if (debugLogs) Debug.Log($"[InputTerrainBrush] Hit does not have required tag '{requiredHitTag}'; rejecting.");
                return false;
            }
        }

        if (requireHitHasModificationManager)
        {
            var tmm = hit.transform.GetComponentInParent<TerrainModificationManager>();
            if (tmm == null)
            {
                if (debugLogs) Debug.Log("[InputTerrainBrush] Hit lacks a TerrainModificationManager parent; rejecting.");
                return false;
            }
        }

        return true;
    }

    private void ApplyBrush(Vector3 worldPos, float strength)
    {
        if (modificationManager == null)
        {
            if (debugLogs) Debug.LogWarning("InputTerrainBrush: TerrainModificationManager not assigned/found.");
            return;
        }
        modificationManager.ApplySphere(worldPos, brushRadius, strength);
    }

    private void HandleAfterDig(Vector3 worldPos, float strength)
    {
        if (strength >= 0f) return;
        if (groundItem == null)
        {
            Debug.LogWarning("[InputTerrainBrush] groundItem not assigned; dig produced no item.");
            return;
        }

        int amount = Mathf.CeilToInt(Mathf.Abs(strength) * Mathf.Max(1, pickupAmountPerUnitStrength));
        if (amount <= 0) return;

        EnsurePlayerInventory();

        if (playerInventory == null)
        {
            Debug.LogWarning("[InputTerrainBrush] No Inventory found to grant dug items to.");
            return;
        }

        int leftover = playerInventory.AddItem(groundItem, amount);
        if (leftover > 0)
            Debug.Log($"[InputTerrainBrush] Inventory full: {leftover} dug item(s) could not be added.");
    }

    private void EnsurePlayerInventory()
    {
        if (playerInventory != null) return;

        if (!string.IsNullOrEmpty(playerTag))
        {
            var playerGo = GameObject.FindWithTag(playerTag);
            if (playerGo != null)
            {
                playerInventory = playerGo.GetComponentInChildren<Inventory>() ?? playerGo.GetComponent<Inventory>();
                if (playerInventory != null) return;
            }
        }

        playerInventory = SceneFind.First<Inventory>();
    }

    private void EnsureHotbarUI()
    {
        if (hotbarUI != null) return;
        hotbarUI = SceneFind.First<HotbarUI>();
    }

    private bool IsSelectedSlotAllowed()
    {
        EnsureHotbarUI();
        if (hotbarUI == null) return true;

        int sel = hotbarUI.GetSelectedIndex();
        var slot = hotbarUI.GetSlot(sel);
        if (slot == null) return true;

        var item = slot.GetItem();
        if (item == null) return true;

        return item == groundItem;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null) return;

        Vector3 preview;
        if (lastHitValid) preview = lastHitPoint;
        else
        {
            float sx = Mathf.Clamp01(0.5f + aimXOffset);
            float sy = Mathf.Clamp01(0.5f + aimYOffset);
            Vector3 screenPoint = new Vector3(cam.pixelWidth * sx, cam.pixelHeight * sy, 0f);
            Ray ray = cam.ScreenPointToRay(screenPoint);
            preview = ray.origin + ray.direction.normalized * defaultFallbackDistance;
        }

        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
        Gizmos.DrawSphere(preview, brushRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(preview, brushRadius);
    }
#endif
}