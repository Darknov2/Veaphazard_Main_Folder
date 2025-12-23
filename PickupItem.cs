using UnityEngine;

/// <summary>
/// PickupItem: world pickup that references an ItemData asset.
/// - TryPickup allows code-driven pickup (e.g. pressing E).
/// - Auto-pickup behaviors:
///     * If the player enters the trigger, the pickup will attempt to give the item to the player's Inventory (legacy behavior).
///     * When "pickOnContact" is enabled the pickup will also attempt to be collected on CollisionEnter (physical collision)
///       and TriggerEnter (for triggers) when the colliding object has an Inventory or matches playerTag.
///     * Use TryPickup(inv) directly to perform a pickup from code.
/// - TryPickup returns leftover amount (0 if fully picked up).
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour
{
    [Tooltip("Item asset this pickup represents")]
    public ItemData item;

    [Tooltip("Number of items in this pickup")]
    public int amount = 1;

    [Tooltip("Destroy this pickup GameObject when fully picked up")]
    public bool destroyOnPickup = true;

    [Tooltip("Optional: tag used to identify the player for automatic trigger pickups")]
    public string playerTag = "Player";

    [Header("Contact pickup (optional)")]
    [Tooltip("If true, the pickup will attempt collection when physically contacted (CollisionEnter) or touched via triggers.")]
    public bool pickOnContact = false;

    [Tooltip("If true and pickOnContact is enabled, CharacterController collisions will also be considered if the controller's root has an Inventory.")]
    public bool acceptCharacterControllerContact = true;

    // Guard: avoid re-entrancy / racing double pickups
    bool processingPickup = false;

    // Called when player enters a trigger — legacy auto-pickup behavior (same as before).
    private void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            var inv = other.GetComponentInChildren<Inventory>() ?? other.GetComponentInParent<Inventory>();
            if (inv != null)
            {
                TryPickupAndHandleDestroy(inv);
                return;
            }
        }

        // If pickOnContact is enabled, allow other triggers to attempt pickup (e.g. NPCs, other colliders)
        if (pickOnContact)
        {
            var inv2 = other.GetComponentInChildren<Inventory>() ?? other.GetComponentInParent<Inventory>();
            if (inv2 != null)
            {
                TryPickupAndHandleDestroy(inv2);
            }
        }
    }

    // Physical collision contact
    private void OnCollisionEnter(Collision collision)
    {
        if (!pickOnContact) return;
        if (collision == null) return;

        // Prefer Inventory on the collider/colliding object
        var other = collision.collider;
        var inv = other.GetComponentInChildren<Inventory>() ?? other.GetComponentInParent<Inventory>();
        if (inv != null)
        {
            TryPickupAndHandleDestroy(inv);
            return;
        }

        // Fallback: if collider belongs to the player tag, try find player's inventory by root
        if (!string.IsNullOrEmpty(playerTag))
        {
            var root = other.transform.root;
            if (root != null && root.CompareTag(playerTag))
            {
                var invRoot = root.GetComponentInChildren<Inventory>() ?? root.GetComponent<Inventory>();
                if (invRoot != null)
                {
                    TryPickupAndHandleDestroy(invRoot);
                    return;
                }
            }
        }
    }

    // Public helper to be called by external code (for example, CharacterController.OnControllerColliderHit on the player)
    // Caller should pass the Inventory (if known) or the Collider that contacted this pickup.
    public void TryPickupFromCollider(Collider contactingCollider)
    {
        if (!pickOnContact || contactingCollider == null) return;

        // If the contacting collider belongs to a CharacterController and acceptCharacterControllerContact is true,
        // attempt to get Inventory from its root.
        var controller = contactingCollider.GetComponentInParent<CharacterController>();
        if (controller != null && acceptCharacterControllerContact)
        {
            var inv = controller.GetComponentInChildren<Inventory>() ?? controller.GetComponent<Inventory>() ?? contactingCollider.GetComponentInChildren<Inventory>() ?? contactingCollider.GetComponentInParent<Inventory>();
            if (inv != null)
            {
                TryPickupAndHandleDestroy(inv);
                return;
            }

            // fallback: try root's inventory by tag
            var root = contactingCollider.transform.root;
            if (root != null)
            {
                var invRoot = root.GetComponentInChildren<Inventory>() ?? root.GetComponent<Inventory>();
                if (invRoot != null)
                {
                    TryPickupAndHandleDestroy(invRoot);
                    return;
                }
            }
        }

        // Otherwise try to get Inventory from the collider itself
        var inv2 = contactingCollider.GetComponentInChildren<Inventory>() ?? contactingCollider.GetComponentInParent<Inventory>();
        if (inv2 != null)
        {
            TryPickupAndHandleDestroy(inv2);
            return;
        }

        // Last resort: try to find player by tag and its inventory
        if (!string.IsNullOrEmpty(playerTag))
        {
            var playerGo = GameObject.FindWithTag(playerTag);
            if (playerGo != null)
            {
                var invRoot = playerGo.GetComponentInChildren<Inventory>() ?? playerGo.GetComponent<Inventory>();
                if (invRoot != null)
                {
                    TryPickupAndHandleDestroy(invRoot);
                    return;
                }
            }
        }
    }

    // Attempt to add this pickup's items to the provided Inventory.
    // Returns leftover amount (0 if fully added).
    public int TryPickup(Inventory inv)
    {
        if (inv == null || item == null || amount <= 0) return amount;

        // Prevent multiple concurrent pickup attempts
        if (processingPickup) return amount;
        processingPickup = true;

        int leftover = inv.AddItem(item, amount);

        processingPickup = false;
        return leftover;
    }

    // Internal helper: try pickup and destroy/update this GameObject as necessary
    private void TryPickupAndHandleDestroy(Inventory inv)
    {
        if (inv == null) return;
        if (processingPickup) return;

        int leftover = TryPickup(inv);
        if (leftover <= 0)
        {
            // consumed
            amount = 0;
            if (destroyOnPickup)
                Destroy(gameObject);
        }
        else
        {
            // partially added or cannot add
            amount = leftover;
            // Optionally you might want to give feedback here (sound/effect) even if not fully taken.
        }
    }
}