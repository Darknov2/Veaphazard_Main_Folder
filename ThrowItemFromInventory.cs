using System.Collections;
using UnityEngine;

/// <summary>
/// Throw the currently selected hotbar item from the Inventory/Hotbar UI when the configured key is released.
/// Hold the key to charge throw strength (longer hold = stronger).
///
/// This version uses the strongly-typed ItemData.placePrefab field (no reflection).
/// - The ItemData scriptable object must have its placePrefab assigned for the item to be thrown.
/// - If placePrefab is null, the optional fallbackPrefab will be used (if assigned).
/// </summary>
[DisallowMultipleComponent]
public class ThrowItemFromInventory : MonoBehaviour
{
    [Header("Input")]
    public KeyCode throwKey = KeyCode.R;

    [Header("References")]
    public Inventory inventory;
    public HotbarUI hotbarUI;
    public Transform spawnPoint;              // defaults to Camera.main if null
    public GameObject fallbackPrefab;         // used when ItemData.placePrefab is not assigned

    [Header("Throw settings")]
    public float minForce = 3f;
    public float maxForce = 20f;
    public float maxChargeTime = 2f;          // seconds to reach maxForce
    [Tooltip("Adds a slight upward component to the throw direction")]
    public float upwardBias = 0.2f;
    public float spinTorque = 0.5f;

    [Header("Collision safety")]
    public float ignorePlayerCollisionDuration = 0.25f;

    [Header("Inventory behavior")]
    public bool consumeOneOnThrow = true;     // remove one from the slot when thrown

    // internals
    bool isCharging = false;
    float chargeStartTime;
    Collider[] playerColliders;

    void Start()
    {
        if (spawnPoint == null)
        {
            if (Camera.main != null)
                spawnPoint = Camera.main.transform;
            else
                spawnPoint = transform;
        }

        // find player colliders to temporarily ignore collisions with thrown items
        playerColliders = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        if (Input.GetKeyDown(throwKey))
        {
            isCharging = true;
            chargeStartTime = Time.time;
        }

        if (isCharging && Input.GetKeyUp(throwKey))
        {
            float held = Time.time - chargeStartTime;
            float t = Mathf.Clamp01(held / maxChargeTime);
            DoThrow(t);
            isCharging = false;
        }
    }

    /// <summary>
    /// Public helper for UI: current normalized charge 0..1 (useful to drive a charge bar)
    /// </summary>
    public float GetCurrentCharge01()
    {
        if (!isCharging) return 0f;
        return Mathf.Clamp01((Time.time - chargeStartTime) / maxChargeTime);
    }

    void DoThrow(float normalizedCharge)
    {
        if (inventory == null || hotbarUI == null)
        {
            Debug.LogWarning("[ThrowItemFromInventory] Inventory or HotbarUI is not assigned.");
            return;
        }

        int sel = hotbarUI.GetSelectedIndex();
        if (sel < 0 || sel >= inventory.slots.Count)
        {
            Debug.LogWarning("[ThrowItemFromInventory] Selected index out of range.");
            return;
        }

        var slot = inventory.slots[sel];
        if (slot.IsEmpty)
        {
            // nothing to throw
            return;
        }

        // Use ItemData.placePrefab directly (strongly-typed)
        GameObject prefab = (slot.item != null) ? slot.item.placePrefab : null;
        if (prefab == null)
            prefab = fallbackPrefab;

        if (prefab == null)
        {
            Debug.LogWarning("[ThrowItemFromInventory] No prefab found for the selected item and no fallback assigned.");
            return;
        }

        // spawn and propel
        Vector3 spawnPos = spawnPoint.position + spawnPoint.forward * 0.6f;
        Quaternion spawnRot = spawnPoint.rotation;
        GameObject thrown = Instantiate(prefab, spawnPos, spawnRot);

        // ensure collider exists (recommended that prefab has colliders)
        Collider[] thrownCols = thrown.GetComponentsInChildren<Collider>();
        if (thrownCols == null || thrownCols.Length == 0)
        {
            Debug.LogWarning("[ThrowItemFromInventory] Thrown prefab has no Collider. It will not interact properly with physics.");
        }

        // ensure rigidbody
        Rigidbody rb = thrown.GetComponent<Rigidbody>();
        if (rb == null) rb = thrown.AddComponent<Rigidbody>();

        float force = Mathf.Lerp(minForce, maxForce, normalizedCharge);

        // compute direction with slight upward bias
        Vector3 dir = (spawnPoint.forward + spawnPoint.up * upwardBias).normalized;
        rb.AddForce(dir * force, ForceMode.Impulse);

        if (spinTorque != 0f)
            rb.AddTorque(Random.insideUnitSphere * spinTorque, ForceMode.Impulse);

        // temporarily ignore collisions between thrown object and player
        if (playerColliders != null && playerColliders.Length > 0)
            StartCoroutine(TemporarilyIgnorePlayerCollision(thrown, playerColliders, ignorePlayerCollisionDuration));

        // consume one item from inventory slot
        if (consumeOneOnThrow)
        {
            inventory.RemoveFromSlot(sel, 1); // Inventory.RefreshUI is called by RemoveFromSlot
        }
    }

    System.Collections.IEnumerator TemporarilyIgnorePlayerCollision(GameObject thrownObject, Collider[] playerCols, float seconds)
    {
        if (thrownObject == null) yield break;

        var thrownCols = thrownObject.GetComponentsInChildren<Collider>();
        if (thrownCols == null || thrownCols.Length == 0) yield break;

        foreach (var pc in playerCols)
        {
            if (pc == null) continue;
            foreach (var tc in thrownCols)
            {
                if (tc == null) continue;
                Physics.IgnoreCollision(pc, tc, true);
            }
        }

        yield return new WaitForSeconds(seconds);

        foreach (var pc in playerCols)
        {
            if (pc == null) continue;
            foreach (var tc in thrownCols)
            {
                if (tc == null) continue;
                if (pc != null && tc != null)
                    Physics.IgnoreCollision(pc, tc, false);
            }
        }
    }
}