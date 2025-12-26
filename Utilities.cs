using UnityEngine;

/// <summary>
/// Utility helpers for cross-version Unity compatibility.
/// Provides wrappers for deprecated FindObjectsOfType APIs that changed in Unity 2023.2+
/// </summary>
public static class Utilities
{
    /// <summary>
    /// Cross-version compatible FindObjectsOfType.
    /// Uses FindObjectsByType with FindObjectsSortMode.None on Unity 2023.2+,
    /// falls back to FindObjectsOfType on older versions.
    /// </summary>
    public static T[] FindObjectsOfTypeCompat<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_2_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        #pragma warning disable CS0618 // Type or member is obsolete
        return UnityEngine.Object.FindObjectsOfType<T>();
        #pragma warning restore CS0618
#endif
    }

    /// <summary>
    /// Cross-version compatible FindFirstObjectByType.
    /// Uses FindFirstObjectByType on Unity 2023.2+,
    /// falls back to FindObjectOfType on older versions.
    /// </summary>
    public static T FindFirstObjectByTypeCompat<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_2_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        #pragma warning disable CS0618 // Type or member is obsolete
        return UnityEngine.Object.FindObjectOfType<T>();
        #pragma warning restore CS0618
#endif
    }
}
