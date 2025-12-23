using System;
using UnityEngine;

/// <summary>
/// Small compatibility wrapper around Unity object-finding APIs.
/// - Uses newer APIs (FindFirstObjectByType / FindAnyObjectByType / FindObjectsByType) when available.
/// - Falls back to older FindObjectOfType / FindObjectsOfType otherwise.
/// - Suppresses obsolete warnings internally when falling back so callers stop getting CS0618 noise.
/// Usage:
///   var inv = SceneFind.First<Inventory>();
///   var all = SceneFind.All<ItemData>(includeInactive:true);
/// </summary>
public static class SceneFind
{
    /// <summary>Find a single instance (first). Returns null if not found.</summary>
    public static T First<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>Find any single instance (no guaranteed ordering).</summary>
    public static T Any<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindAnyObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }

    /// <summary>
    /// Find all instances of T. includeInactive parameter attempts to include inactive objects when possible.
    /// Returns an array (never null).
    /// </summary>
    public static T[] All<T>(bool includeInactive = false) where T : UnityEngine.Object
    {
#if UNITY_2023_2_OR_NEWER
        // Newer Unity versions expose FindObjectsByType<T>, but signatures vary; try safely.
        try
        {
            return UnityEngine.Object.FindObjectsByType<T>(includeInactive ? UnityEngine.FindObjectsSortMode.None : UnityEngine.FindObjectsSortMode.None);
        }
        catch
        {
            // fall through to fallback
        }
#endif

        // Older Unity fallback: use FindObjectsOfType and, if possible, call the includeInactive overload.
#pragma warning disable CS0618
        try
        {
            return UnityEngine.Object.FindObjectsOfType<T>(includeInactive);
        }
        catch
        {
            return UnityEngine.Object.FindObjectsOfType<T>();
        }
#pragma warning restore CS0618
    }

    /// <summary>Non-generic single-type lookup. Returns first matching object of the exact type 't' or null.</summary>
    public static UnityEngine.Object First(Type t)
    {
        if (t == null) return null;

#if UNITY_2023_1_OR_NEWER
        // No non-generic FindFirstObjectByType(Type) available; do conservative scan.
        var all = All<UnityEngine.Object>(includeInactive: true);
        foreach (var o in all)
        {
            if (o != null && o.GetType() == t) return o;
        }
        return null;
#else
        return UnityEngine.Object.FindObjectOfType(t);
#endif
    }
}