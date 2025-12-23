using System;
using UnityEngine;

/// <summary>
/// TreeSegment
/// - Prefers a direct TreePhysicsActivator_Compound reference to call ActivatePhysicsAbove.
/// - Provides SetActivator(...) so the activator can register itself directly (preferred).
/// - RequestBreak performs multiple safe fallbacks and only logs warnings in debug builds.
/// </summary>
[DisallowMultipleComponent]
public class TreeSegment : MonoBehaviour
{
    [Tooltip("Optional explicit activator. If null, the script will search parents for a suitable activator component.")]
    public MonoBehaviour activator; // kept for backwards compatibility with existing inspector field

    [Tooltip("If true, destroy this segment GameObject after notifying activator (visual stump).")]
    public bool destroyOnBreak = false;

    [Tooltip("Optional prefab to spawn as a break effect (particles/sound).")]
    public GameObject breakEffectPrefab;

    [Header("Drops")]
    [Tooltip("Item to give the player when this segment is destroyed.")]
    public ItemData dropItem;
    [Tooltip("How many items to grant when destroyed.")]
    public int dropAmount = 1;

    // internal flag to avoid multiple requests
    private bool requested = false;

    /// <summary>
    /// Public API used by other systems to set the activator reference directly.
    /// TreePhysicsActivator_Compound will call this during grouping so segments keep a valid reference.
    /// </summary>
    public void SetActivator(TreePhysicsActivator_Compound a)
    {
        try
        {
            // prefer strongly-typed storage if available: try to set the 'activator' MonoBehaviour field if present
            var field = this.GetType().GetField("activator");
            if (field != null)
            {
                field.SetValue(this, a);
                return;
            }

            // fallback: try property named 'activator'
            var prop = this.GetType().GetProperty("activator", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(this, a, null);
                return;
            }
        }
        catch
        {
            // ignore assignment errors — SetActivator is best-effort
        }
    }

    void Awake()
    {
        // nothing needed here — RequestBreak will locate activator if not assigned
    }

    /// <summary>
    /// Request breaking of this segment. This does NOT immediately destroy the GameObject.
    /// It notifies the activator which will later form the falling compound and optionally remove the stump.
    /// </summary>
    public void RequestBreak()
    {
        if (requested) return;
        requested = true;

        if (breakEffectPrefab != null)
            Instantiate(breakEffectPrefab, transform.position, Quaternion.identity);

        // Try to find and invoke activator
        bool invoked = InvokeActivatorActivatePhysicsAbove();

        if (!invoked)
        {
            // No activator found — perform local fallback (give drop and destroy/hide)
            if (Debug.isDebugBuild)
                Debug.LogWarning("[TreeSegment] No activator found to handle grouping — performing local fallback (give drop / destroy).");

            DoLocalFallback();
        }
    }

    // Backwards-compatible alias
    public void Break() => RequestBreak();

    private bool InvokeActivatorActivatePhysicsAbove()
    {
        // 1) Try explicitly assigned activator (inspector or SetActivator)
        TreePhysicsActivator_Compound found = null;
        try
        {
            if (activator != null)
            {
                found = activator as TreePhysicsActivator_Compound;
                // if the serialized activator is not the correct type, try to detect ActivatePhysicsAbove via reflection (less ideal)
                if (found == null)
                {
                    // try GetComponentInParent fallback below
                    found = null;
                }
            }
        }
        catch { found = null; }

        // 2) Try parent chain
        if (found == null)
        {
            try { found = GetComponentInParent<TreePhysicsActivator_Compound>(); }
            catch { found = null; }
        }

        // 3) Try root's children (handles odd prefab hierarchies)
        if (found == null && transform.root != null)
        {
            try { found = transform.root.GetComponentInChildren<TreePhysicsActivator_Compound>(includeInactive: true); }
            catch { found = null; }
        }

        if (found == null)
        {
            // Not found
            return false;
        }

        // Try to call ActivatePhysicsAbove safely
        try
        {
            found.ActivatePhysicsAbove(this.transform);
            return true;
        }
        catch (Exception ex)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[TreeSegment] Exception when calling activator.ActivatePhysicsAbove: {ex.Message}");
            return false;
        }
    }

    private void DoLocalFallback()
    {
        // give drops to player if configured then destroy
        if (dropItem != null && dropAmount > 0)
            GiveDropToPlayer();

        if (destroyOnBreak)
        {
            try { Destroy(gameObject); } catch { gameObject.SetActive(false); }
        }
        else
        {
            // disable renderers/colliders to mimic removal
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in renderers) { try { r.enabled = false; } catch { } }
            var colliders = GetComponentsInChildren<Collider>(includeInactive: true);
            foreach (var c in colliders) { try { c.enabled = false; } catch { } }
        }
    }

    /// <summary>
    /// Give the configured drop to the player's Inventory.
    /// Non-fatal: if no Inventory found, the method logs a warning in debug builds and continues.
    /// </summary>
    public void GiveDropToPlayer()
    {
        if (dropItem == null || dropAmount <= 0) return;

        Inventory inv = null;

        // try find by player tag first (player GameObject may have Inventory)
        try
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null)
                inv = playerGo.GetComponentInChildren<Inventory>() ?? playerGo.GetComponent<Inventory>();
        }
        catch { }

        // fallback to SceneFind or FindObjectOfType
        if (inv == null)
        {
            var sceneFindType = Type.GetType("SceneFind");
            if (sceneFindType != null)
            {
                var m = sceneFindType.GetMethod("First", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m != null)
                {
                    try
                    {
                        var gen = m.MakeGenericMethod(typeof(Inventory));
                        var res = gen.Invoke(null, new object[] { });
                        inv = res as Inventory;
                    }
                    catch { }
                }
            }
        }

        if (inv == null)
        {
#if UNITY_2023_2_OR_NEWER
            inv = UnityEngine.Object.FindFirstObjectByType<Inventory>();
#else
            inv = FindObjectOfType<Inventory>();
#endif
        }

        if (inv == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning("[TreeSegment] No Inventory found to receive dropped item.");
            return;
        }

        try
        {
            inv.AddItem(dropItem, dropAmount);
        }
        catch (Exception ex)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[TreeSegment] Inventory.AddItem failed: {ex.Message}");
        }
    }
}