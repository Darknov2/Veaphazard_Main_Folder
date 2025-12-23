using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// TreePhysicsActivator_Compound
/// - Creates a runtime compound Rigidbody for falling group, assigns activator to TreeSegments in each segment subtree
///   by calling SetActivator(TreePhysicsActivator_Compound) when available (fast and safe), otherwise falls back
///   to setting the 'activator' MonoBehaviour field or property via reflection.
/// - Preserves per-segment drop data across grouping by attaching SegmentDropInfo before reparenting.
/// - After ungrouping it now REMOVES TreeSegment components from the restored pieces so they won't re-trigger grouping
///   or produce the "No activator found..." fallback behavior. Pickup recreation (if any) happens before component removal.
/// </summary>
[DisallowMultipleComponent]
public class TreePhysicsActivator_Compound : MonoBehaviour
{
    [Header("Segment detection")]
    public Rigidbody[] segmentsExplicit;
    public bool destroyChildRigidbodies = true;
    public bool setContinuousCollision = true;
    public float fallingGroupMass = 0f;
    public Transform fallingGroupParent;

    [Header("Broken stump")]
    public bool destroyBrokenStumpWhenGrouping = true;

    [Header("Auto-ungroup (restore children)")]
    public float ungroupDelay = 6f;
    public bool recreateRigidbodies = true;
    public bool recreateColliders = true;
    public float defaultLeafMass = 0.2f;
    public float defaultLogMass = 2.0f;
    public bool estimateMassFromBounds = true;
    public bool removeGroupRigidbody = true;
    public bool removeChildJoints = true;
    public CollisionDetectionMode childCollisionDetection = CollisionDetectionMode.ContinuousDynamic;

    [Header("Misc")]
    public bool debugLogs = false;

    // helper component used to persist drop info across grouping
    // attached to segment GameObjects before reparenting and read/removed after ungrouping
    public class SegmentDropInfo : MonoBehaviour
    {
        public ItemData item;
        public int amount;
    }

    private List<Rigidbody> orderedSegments = new List<Rigidbody>();
    private readonly List<GameObject> runtimeFallingGroups = new List<GameObject>();

    void Awake()
    {
        RefreshSegments();
    }

    public void RefreshSegments()
    {
        orderedSegments.Clear();

        if (segmentsExplicit != null && segmentsExplicit.Length > 0)
        {
            orderedSegments.AddRange(segmentsExplicit.Where(r => r != null));
        }
        else
        {
            var rbs = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            foreach (var rb in rbs)
            {
                if (rb == null) continue;
                if (rb.gameObject == gameObject) continue;
                orderedSegments.Add(rb);
            }
        }

        orderedSegments = orderedSegments.Where(r => r != null).ToList();
        orderedSegments.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
    }

    public void ActivatePhysicsAbove(Transform brokenSegment)
    {
        if (orderedSegments == null || orderedSegments.Count == 0) RefreshSegments();

        int startIndex = 0;
        if (brokenSegment != null)
        {
            startIndex = orderedSegments.FindIndex(rb => rb != null && (rb.transform == brokenSegment || rb.transform.IsChildOf(brokenSegment)));
            if (startIndex >= 0) startIndex += 1;
            else
            {
                float by = brokenSegment.position.y;
                startIndex = orderedSegments.FindIndex(rb => rb != null && rb.transform.position.y > by);
                if (startIndex < 0) return;
            }
        }
        else startIndex = 0;

        if (startIndex < 0 || startIndex >= orderedSegments.Count) return;

        var groupSegments = orderedSegments.Skip(startIndex).Where(rb => rb != null).ToList();
        if (groupSegments.Count == 0) return;

        CreateCompoundFallingGroup(groupSegments, brokenSegment);
    }

    private void CreateCompoundFallingGroup(List<Rigidbody> groupSegments, Transform brokenSegment = null)
    {
        if (groupSegments == null || groupSegments.Count == 0) return;

        string name = $"FallingGroup_{gameObject.name}_{Time.frameCount}";
        GameObject groupRoot = new GameObject(name);
        if (fallingGroupParent != null) groupRoot.transform.SetParent(fallingGroupParent, worldPositionStays: false);
        groupRoot.transform.position = groupSegments[0].transform.position;
        groupRoot.transform.rotation = groupSegments[0].transform.rotation;
        groupRoot.transform.localScale = Vector3.one;

        Rigidbody groupRb = groupRoot.AddComponent<Rigidbody>();
        groupRb.isKinematic = false;
        if (setContinuousCollision) groupRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        groupRb.interpolation = RigidbodyInterpolation.Interpolate;

        float summedMass = 0f;
        foreach (var rb in groupSegments)
            if (rb != null) summedMass += rb.mass;
        groupRb.mass = fallingGroupMass > 0f ? fallingGroupMass : Mathf.Max(0.01f, summedMass);

        var segmentTransforms = new List<Transform>();
        var originalParents = new Dictionary<Transform, Transform>();
        foreach (var rb in groupSegments)
        {
            if (rb == null) continue;
            segmentTransforms.Add(rb.transform);
            originalParents[rb.transform] = rb.transform.parent;
        }

        // assign activator reference and persist drop info on TreeSegment components in subtrees BEFORE reparenting
        foreach (var rb in groupSegments)
        {
            if (rb == null) continue;
            Transform segT = rb.transform;

            try
            {
                var treeSegmentsInSubtree = segT.GetComponentsInChildren<TreeSegment>(true);
                foreach (var ts in treeSegmentsInSubtree)
                {
                    if (ts == null) continue;

                    // 1) call SetActivator if available (preferred)
                    var setMethod = ts.GetType().GetMethod("SetActivator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (setMethod != null)
                    {
                        try { setMethod.Invoke(ts, new object[] { this }); }
                        catch (Exception ex) { if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] SetActivator invocation failed on '{ts.gameObject.name}': {ex.Message}"); }
                    }
                    else
                    {
                        // fallback: set 'activator' field/property via reflection (backwards-compat)
                        try
                        {
                            var activatorField = ts.GetType().GetField("activator");
                            if (activatorField != null)
                            {
                                var current = activatorField.GetValue(ts) as MonoBehaviour;
                                if (current == null)
                                {
                                    activatorField.SetValue(ts, this);
                                    if (debugLogs) Debug.Log($"[TreePhysicsActivator] Assigned activator to TreeSegment '{ts.gameObject.name}'.");
                                }
                            }
                            else
                            {
                                var prop = ts.GetType().GetProperty("activator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (prop != null && prop.CanWrite)
                                {
                                    var current = prop.GetValue(ts, null) as MonoBehaviour;
                                    if (current == null)
                                    {
                                        prop.SetValue(ts, this, null);
                                        if (debugLogs) Debug.Log($"[TreePhysicsActivator] Assigned activator(prop) to TreeSegment '{ts.gameObject.name}'.");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Couldn't assign activator to TreeSegment '{ts.gameObject.name}': {ex.Message}");
                        }
                    }

                    // 2) persist drop info (if any) so we can recreate PickupItem at ungroup time
                    try
                    {
                        // try fields 'dropItem' and 'dropAmount' on TreeSegment (common names)
                        var dropField = ts.GetType().GetField("dropItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var amtField = ts.GetType().GetField("dropAmount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        ItemData item = null;
                        int amount = 0;
                        if (dropField != null)
                        {
                            item = dropField.GetValue(ts) as ItemData;
                        }
                        if (amtField != null)
                        {
                            var val = amtField.GetValue(ts);
                            if (val is int) amount = (int)val;
                        }

                        if (item != null && amount > 0)
                        {
                            // add helper component to persist info through reparent/unparent
                            var existing = ts.gameObject.GetComponent<SegmentDropInfo>();
                            if (existing == null)
                            {
                                var info = ts.gameObject.AddComponent<SegmentDropInfo>();
                                info.item = item;
                                info.amount = amount;
                                if (debugLogs) Debug.Log($"[TreePhysicsActivator] Persisted drop info on '{ts.gameObject.name}' (item={item.name}, amount={amount}).");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Couldn't persist drop info for '{ts.gameObject.name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Error while processing subtree '{segT.name}': {ex.Message}");
            }

            // preserve world transform when reparenting
            segT.SetParent(groupRoot.transform, worldPositionStays: true);

            if (destroyChildRigidbodies)
            {
                var joints = segT.GetComponentsInChildren<Joint>(true);
                foreach (var j in joints) Destroy(j);

                var childRbs = segT.GetComponentsInChildren<Rigidbody>(true);
                foreach (var cr in childRbs)
                {
                    if (cr == null) continue;
                    if (cr == groupRb) continue;
                    Destroy(cr);
                }
            }
            else
            {
                var childRbs = segT.GetComponentsInChildren<Rigidbody>(true);
                foreach (var cr in childRbs)
                {
                    if (cr == null) continue;
                    cr.isKinematic = true;
                    cr.detectCollisions = false;
                }
            }
        }

        runtimeFallingGroups.Add(groupRoot);

        if (debugLogs) Debug.Log($"[TreePhysicsActivator_Compound] Created falling group '{groupRoot.name}' with {segmentTransforms.Count} segments. Ungroup in {ungroupDelay}s.");

        if (destroyBrokenStumpWhenGrouping && brokenSegment != null)
        {
            var segComp = brokenSegment.GetComponent<TreeSegment>();
            if (segComp != null)
            {
                try { segComp.GiveDropToPlayer(); } catch (Exception ex) { if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] GiveDropToPlayer exception: {ex.Message}"); }
            }

            var joints = brokenSegment.GetComponentsInChildren<Joint>(true);
            foreach (var j in joints) Destroy(j);

            try { brokenSegment.gameObject.SetActive(false); } catch { }
            try { Destroy(brokenSegment.gameObject, 0.1f); } catch { }
        }

        StartCoroutine(UngroupAfterDelayCoroutine(groupRoot));
    }

    private IEnumerator UngroupAfterDelayCoroutine(GameObject groupRoot)
    {
        if (ungroupDelay > 0f)
            yield return new WaitForSeconds(ungroupDelay);
        else
            yield return null;

        if (groupRoot == null) yield break;

        if (debugLogs) Debug.Log($"[TreePhysicsActivator_Compound] Ungrouping '{groupRoot.name}'");

        if (removeGroupRigidbody)
        {
            var grpRb = groupRoot.GetComponent<Rigidbody>();
            if (grpRb != null) Destroy(grpRb);
        }

        var descendants = groupRoot.GetComponentsInChildren<Transform>(includeInactive: true)
                                   .Where(t => t != null && t != groupRoot.transform)
                                   .ToList();

        // unparent all descendants to scene root (preserve world transform)
        foreach (var d in descendants)
            if (d != null) d.SetParent(null, worldPositionStays: true);

        // ensure physics for every previously-child object and their nested children
        foreach (var d in descendants)
            if (d != null) EnsurePhysicsRecursively(d.gameObject);

        // recreate pickups for segments that had drop info
        foreach (var d in descendants)
        {
            if (d == null) continue;

            try
            {
                // find any persisted drop info in this subtree
                var infos = d.GetComponentsInChildren<SegmentDropInfo>(includeInactive: true);
                foreach (var info in infos)
                {
                    if (info == null) continue;
                    var owner = info.gameObject;

                    try
                    {
                        // Ensure a collider exists on owner (so raycast/pickup can hit)
                        var col = owner.GetComponent<Collider>();
                        if (col == null && recreateColliders)
                        {
                            var rend = owner.GetComponentInChildren<Renderer>();
                            if (rend != null)
                            {
                                var bounds = rend.bounds;
                                var box = owner.AddComponent<BoxCollider>();
                                box.center = owner.transform.InverseTransformPoint(bounds.center);
                                box.size = TransformBoundsSizeToLocal(owner.transform, bounds.size);
                                if (debugLogs) Debug.Log($"[TreePhysicsActivator] Added BoxCollider to '{owner.name}' for pickup recreation.");
                                col = box;
                            }
                            else
                            {
                                var sph = owner.AddComponent<SphereCollider>();
                                sph.radius = 0.25f;
                                if (debugLogs) Debug.Log($"[TreePhysicsActivator] Added SphereCollider to '{owner.name}' for pickup recreation.");
                                col = sph;
                            }
                        }

                        // Add/configure PickupItem on owner
                        var pickup = owner.GetComponent<PickupItem>();
                        if (pickup == null)
                        {
                            pickup = owner.AddComponent<PickupItem>();
                            if (debugLogs) Debug.Log($"[TreePhysicsActivator] Added PickupItem to '{owner.name}' (recreated from SegmentDropInfo).");
                        }

                        // configure pickup with saved data
                        try
                        {
                            pickup.item = info.item;
                            pickup.amount = Mathf.Max(1, info.amount);
                            pickup.destroyOnPickup = true;
                        }
                        catch
                        {
                            // fallback: set via reflection if needed
                            try { typeof(PickupItem).GetField("item")?.SetValue(pickup, info.item); } catch { }
                            try { typeof(PickupItem).GetField("amount")?.SetValue(pickup, Mathf.Max(1, info.amount)); } catch { }
                            try { typeof(PickupItem).GetField("destroyOnPickup")?.SetValue(pickup, true); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Failed to recreate PickupItem on '{owner.name}': {ex.Message}");
                    }
                    finally
                    {
                        // remove the helper component
                        try { Destroy(info); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Error while recreating pickups in descendant '{d.name}': {ex.Message}");
            }
        }

        // Ensure leaf mesh collider overrides include Player layer
        EnsureLeafDescendantsMeshColliderOverridesIncludePlayer(descendants);

        // NOW remove any TreeSegment components from the restored descendants to avoid re-triggering grouping/fallbacks
        foreach (var d in descendants)
        {
            if (d == null) continue;
            try
            {
                var treeSegs = d.GetComponentsInChildren<TreeSegment>(includeInactive: true);
                foreach (var ts in treeSegs)
                {
                    if (ts == null) continue;
                    try
                    {
                        // Destroy the TreeSegment component so it won't respond to player breaks after ungroup
                        Destroy(ts);
                        if (debugLogs) Debug.Log($"[TreePhysicsActivator] Removed TreeSegment component from '{ts.gameObject.name}' after ungroup.");
                    }
                    catch (Exception ex)
                    {
                        if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Failed to destroy TreeSegment on '{ts.gameObject.name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator] Error removing TreeSegment components in descendant '{d.name}': {ex.Message}");
            }
        }

        runtimeFallingGroups.Remove(groupRoot);
        Destroy(groupRoot);

        if (debugLogs) Debug.Log($"[TreePhysicsActivator_Compound] Ungrouped and destroyed '{groupRoot.name}'");
    }

    private void EnsurePhysicsRecursively(GameObject root)
    {
        if (root == null) return;
        var allTransforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
        foreach (var t in allTransforms)
            if (t != null) ConfigureSingleObjectPhysics(t.gameObject);
    }

    private void ConfigureSingleObjectPhysics(GameObject go)
    {
        if (go == null) return;

        if (removeChildJoints)
        {
            var joints = go.GetComponentsInChildren<Joint>(true);
            foreach (var j in joints) Destroy(j);
        }

        var existingCollider = go.GetComponent<Collider>();
        if (existingCollider == null && recreateColliders)
        {
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                var bounds = rend.bounds;
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(bounds.center);
                box.size = TransformBoundsSizeToLocal(go.transform, bounds.size);
            }
            else
            {
                var sph = go.AddComponent<SphereCollider>();
                sph.radius = 0.25f;
            }
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null && recreateRigidbodies)
        {
            rb = go.AddComponent<Rigidbody>();
            rb.mass = EstimateMassForGameObject(go);
            rb.collisionDetectionMode = childCollisionDetection;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = false;
            rb.detectCollisions = true;
        }
        else if (rb != null)
        {
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.collisionDetectionMode = childCollisionDetection;
            if (rb.interpolation == RigidbodyInterpolation.None) rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    // Ensures every descendant tagged "Leaf" that has a MeshCollider includes the Player layer in its include mask
    // and removes it from any exclude mask — robust across Unity versions via reflection.
    private void EnsureLeafDescendantsMeshColliderOverridesIncludePlayer(List<Transform> descendants)
    {
        if (descendants == null || descendants.Count == 0) return;

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer < 0)
        {
            var playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null) playerLayer = playerGo.layer;
        }

        if (playerLayer < 0)
        {
            if (debugLogs) Debug.LogWarning("[TreePhysicsActivator_Compound] Could not resolve Player layer; skipping MeshCollider layer-override updates.");
            return;
        }

        string[] includeCandidates = new[]
        {
            "includeLayers", "includeLayerMask", "includedLayers", "includeMask", "IncludeLayers", "IncludeLayerMask",
            "m_IncludeLayers", "m_IncludeLayerMask", "m_IncludeMask", "m_LayerOverrideInclude", "m_LayerOverrideIncludeMask"
        };

        string[] excludeCandidates = new[]
        {
            "excludeLayers", "excludeLayerMask", "excludedLayers", "excludeMask", "ExcludeLayers", "ExcludeLayerMask",
            "m_ExcludeLayers", "m_ExcludeLayerMask", "m_ExcludeMask", "m_LayerOverrideExclude", "m_LayerOverrideExcludeMask"
        };

        foreach (var t in descendants)
        {
            if (t == null) continue;
            if (!t.CompareTag("Leaf")) continue;

            var meshCol = t.GetComponent<MeshCollider>();
            if (meshCol == null) continue;

            bool changed = false;

            if (TrySetLayerMaskFieldOrProperty(meshCol, includeCandidates, playerLayer, true)) changed = true;
            if (TrySetLayerMaskFieldOrProperty(meshCol, excludeCandidates, playerLayer, false)) changed = true;

            if (!changed && debugLogs)
                Debug.LogWarning($"[TreePhysicsActivator_Compound] Could not find include/exclude masks on MeshCollider '{t.name}'.");
            else if (changed && debugLogs)
                Debug.Log($"[TreePhysicsActivator_Compound] Updated MeshCollider layer overrides for '{t.name}' to include Player layer ({playerLayer}).");

            var rb = t.GetComponent<Rigidbody>();
            if (rb != null && !meshCol.convex)
            {
                if (meshCol.sharedMesh != null)
                {
                    meshCol.convex = true;
                    if (debugLogs) Debug.Log($"[TreePhysicsActivator_Compound] Set MeshCollider.convex=true on '{t.name}' because Rigidbody exists.");
                }
                else if (debugLogs)
                {
                    Debug.LogWarning($"[TreePhysicsActivator_Compound] MeshCollider on '{t.name}' has no sharedMesh; cannot set convex.");
                }
            }
        }
    }

    private bool TrySetLayerMaskFieldOrProperty(object target, string[] candidateNames, int layerIndex, bool setBit)
    {
        if (target == null || candidateNames == null || candidateNames.Length == 0) return false;

        Type t = target.GetType();
        int bit = 1 << layerIndex;

        foreach (var name in candidateNames)
        {
            try
            {
                var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanRead && prop.CanWrite)
                {
                    object current = prop.GetValue(target, null);
                    if (current == null) continue;

                    if (current is LayerMask)
                    {
                        LayerMask lm = (LayerMask)current;
                        int im = lm.value;
                        int newVal = setBit ? (im | bit) : (im & ~bit);
                        if (newVal != im)
                        {
                            prop.SetValue(target, (LayerMask)newVal, null);
                            return true;
                        }
                        return false;
                    }

                    if (current is int)
                    {
                        int im = (int)current;
                        int newVal = setBit ? (im | bit) : (im & ~bit);
                        if (newVal != im)
                        {
                            prop.SetValue(target, newVal, null);
                            return true;
                        }
                        return false;
                    }

                    if (current is uint)
                    {
                        uint im = (uint)current;
                        uint newVal = setBit ? (im | (uint)bit) : (im & ~(uint)bit);
                        if (newVal != im)
                        {
                            prop.SetValue(target, newVal, null);
                            return true;
                        }
                        return false;
                    }
                }

                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object current = field.GetValue(target);
                    if (current == null) continue;

                    if (current is LayerMask)
                    {
                        LayerMask lm = (LayerMask)current;
                        int im = lm.value;
                        int newVal = setBit ? (im | bit) : (im & ~bit);
                        if (newVal != im)
                        {
                            field.SetValue(target, (LayerMask)newVal);
                            return true;
                        }
                        return false;
                    }

                    if (current is int)
                    {
                        int im = (int)current;
                        int newVal = setBit ? (im | bit) : (im & ~bit);
                        if (newVal != im)
                        {
                            field.SetValue(target, newVal);
                            return true;
                        }
                        return false;
                    }

                    if (current is uint)
                    {
                        uint im = (uint)current;
                        uint newVal = setBit ? (im | (uint)bit) : (im & ~(uint)bit);
                        if (newVal != im)
                        {
                            field.SetValue(target, newVal);
                            return true;
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogWarning($"[TreePhysicsActivator_Compound] Reflection attempt for '{name}' failed: {ex.Message}");
            }
        }

        return false;
    }

    private float EstimateMassForGameObject(GameObject go)
    {
        if (!estimateMassFromBounds) return defaultLogMass;
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) return defaultLogMass;
        Vector3 s = rend.bounds.size;
        float volume = Math.Abs(s.x * s.y * s.z);
        if (volume <= 0.001f) return defaultLeafMass;
        float estimated = Mathf.Clamp(volume * 0.5f, defaultLeafMass, defaultLogMass * 4f);
        return estimated;
    }

    private Vector3 TransformBoundsSizeToLocal(Transform tr, Vector3 worldSize)
    {
        Vector3 ls = tr.lossyScale;
        ls.x = Mathf.Approximately(ls.x, 0f) ? 1f : ls.x;
        ls.y = Mathf.Approximately(ls.y, 0f) ? 1f : ls.y;
        ls.z = Mathf.Approximately(ls.z, 0f) ? 1f : ls.z;
        return new Vector3(worldSize.x / ls.x, worldSize.y / ls.y, worldSize.z / ls.z);
    }

#if UNITY_EDITOR
    [ContextMenu("Destroy Runtime Falling Groups (Editor only)")]
    private void DestroyRuntimeGroupsEditor()
    {
        for (int i = runtimeFallingGroups.Count - 1; i >= 0; i--)
        {
            var g = runtimeFallingGroups[i];
            if (g != null) DestroyImmediate(g);
            runtimeFallingGroups.RemoveAt(i);
        }
    }
#endif
}