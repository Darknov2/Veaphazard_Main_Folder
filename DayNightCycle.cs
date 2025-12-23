using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple Day / Night cycle for Unity.
/// - Attach this to an empty GameObject (or your scene manager). Assign a Directional Light to "sun".
/// - The script rotates the sun over time, changes its color & intensity, updates ambient lighting and fog color.
/// - Public parameters let you control day length (seconds), sunrise/sunset thresholds and curves/gradients.
/// - Works with built-in RP and most simple setups. For HDRP/URP you may want to drive Volume profiles instead.
/// </summary>
[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    [Header("Time")]
    [Tooltip("Length of a full day in seconds. Set to 0 to pause.")]
    public float dayLengthInSeconds = 120f;

    [Range(0f, 24f), Tooltip("Current time of day in hours (0..24). 0 = midnight, 12 = noon.")]
    public float timeOfDayHours = 8f;

    [Tooltip("Automatically advance time.")]
    public bool autoAdvance = true;

    [Header("Sun (Directional Light)")]
    [Tooltip("Directional light used as the sun.")]
    public Light sun;

    [Tooltip("Rotation offset (degrees) applied to the sun's transform before computing time rotation.")]
    public Vector3 sunRotationOffset = new Vector3(0f, 0f, 0f);

    [Header("Sun visual")]
    public Gradient sunColorOverDay = new Gradient()
    {
        colorKeys = new GradientColorKey[]
        {
            new GradientColorKey(new Color(0.05f,0.05f,0.25f), 0f), // midnight
            new GradientColorKey(new Color(1f,0.5f,0.3f), 0.23f),   // sunrise
            new GradientColorKey(Color.white, 0.5f),               // noon
            new GradientColorKey(new Color(1f,0.5f,0.3f), 0.77f),  // sunset
            new GradientColorKey(new Color(0.05f,0.05f,0.25f), 1f) // midnight
        },
        alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }
    };

    [Tooltip("Sun intensity curve across normalized day (0..1).")]
    public AnimationCurve sunIntensityOverDay = AnimationCurve.EaseInOut(0f, 0f, 0.5f, 1f);

    [Header("Ambient / Fog")]
    public Gradient ambientColorOverDay;
    public Gradient fogColorOverDay;
    [Range(0f, 1f)] public float ambientIntensityMultiplier = 1f;
    [Tooltip("Enable fog color changes.")]
    public bool updateFog = true;

    [Header("Sunrise / Sunset events")]
    public UnityEvent onSunrise;
    public UnityEvent onSunset;

    [Header("Debug")]
    [Tooltip("If true, logs sunrise/sunset events.")]
    public bool debugLogs = false;

    // internal
    private float normalizedTime; // 0..1
    private bool lastWasDay = true;
    private const float hoursInDay = 24f;

    void OnValidate()
    {
        // Provide sensible defaults for gradients if unset
        if (ambientColorOverDay == null || ambientColorOverDay.colorKeys.Length == 0)
        {
            ambientColorOverDay = new Gradient()
            {
                colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.02f,0.02f,0.05f), 0f),
                    new GradientColorKey(new Color(0.5f,0.5f,0.6f), 0.25f),
                    new GradientColorKey(new Color(1f,1f,1f), 0.5f),
                    new GradientColorKey(new Color(0.5f,0.4f,0.35f), 0.75f),
                    new GradientColorKey(new Color(0.02f,0.02f,0.05f), 1f)
                }
            };
        }
        if (fogColorOverDay == null || fogColorOverDay.colorKeys.Length == 0)
        {
            fogColorOverDay = ambientColorOverDay;
        }
    }

    void Reset()
    {
        // Try to automatically find a directional light in the scene
        if (sun == null)
        {
            var s = FindObjectOfType<Light>();
            if (s != null && s.type == LightType.Directional) sun = s;
        }
    }

    void Start()
    {
        if (!Application.isPlaying)
            return;
        UpdateCycle(0f);
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            float delta = Time.deltaTime;
            UpdateCycle(delta);
        }
        else
        {
            // In editor mode, reflect current time immediately
            UpdateCycle(0f);
        }
    }

    private void UpdateCycle(float deltaTime)
    {
        // Advance time
        if (autoAdvance && dayLengthInSeconds > 0f)
        {
            float hoursPerSecond = hoursInDay / Mathf.Max(0.0001f, dayLengthInSeconds);
            timeOfDayHours = (timeOfDayHours + hoursPerSecond * deltaTime) % hoursInDay;
        }

        normalizedTime = Mathf.Repeat(timeOfDayHours / hoursInDay, 1f); // 0..1

        // Sun rotation: map normalizedTime to angle (midnight=-90, sunrise=0 at horizon, noon=90 overhead, sunset=180 etc)
        // We'll set sun to rotate around X axis: 0 at horizon morning, 180 at horizon evening
        float sunAngle = normalizedTime * 360f - 90f; // -90 -> 270
        Quaternion rot = Quaternion.Euler(sunAngle, 0f, 0f) * Quaternion.Euler(sunRotationOffset);
        if (sun != null)
        {
            sun.transform.rotation = rot;

            // Sun color & intensity
            Color sunCol = sunColorOverDay.Evaluate(normalizedTime);
            sun.color = sunCol;

            float intensity = sunIntensityOverDay.Evaluate(normalizedTime);
            // Optionally scale intensity to something usable:
            sun.intensity = Mathf.Clamp01(intensity) * 1.5f; // base multiplier, tweak in inspector
        }

        // Ambient & fog
        Color ambientCol = ambientColorOverDay.Evaluate(normalizedTime);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambientCol * ambientIntensityMultiplier;

        if (updateFog)
        {
            try
            {
                RenderSettings.fogColor = fogColorOverDay.Evaluate(normalizedTime);
            }
            catch { /* ignore on pipelines that don't support fog */ }
        }

        // Sunrise / Sunset events detection
        bool isDay = IsDaytime();
        if (isDay && !lastWasDay)
        {
            lastWasDay = true;
            if (debugLogs) Debug.Log("[DayNightCycle] Sunrise triggered.");
            onSunrise?.Invoke();
        }
        else if (!isDay && lastWasDay)
        {
            lastWasDay = false;
            if (debugLogs) Debug.Log("[DayNightCycle] Sunset triggered.");
            onSunset?.Invoke();
        }
    }

    /// <summary>
    /// Consider daytime when sun is above horizon (simple test).
    /// </summary>
    public bool IsDaytime()
    {
        // sun is above horizon when its forward.y < 0 (directional light points 'down' as forward)
        if (sun != null)
        {
            return Vector3.Dot(sun.transform.forward, Vector3.up) < 0f;
        }
        // fallback: daytime between 6 and 18 hours
        return timeOfDayHours >= 6f && timeOfDayHours < 18f;
    }

    /// <summary>
    /// Set time instantly (hours 0..24)
    /// </summary>
    public void SetTimeOfDay(float hours)
    {
        timeOfDayHours = Mathf.Repeat(hours, 24f);
        UpdateCycle(0f);
    }

    /// <summary>
    /// Convenience to set a new day length (seconds).
    /// </summary>
    public void SetDayLength(float seconds)
    {
        dayLengthInSeconds = Mathf.Max(0f, seconds);
    }

    // Expose a simple inspector button for quickly jumping to sunrise / noon / sunset in editor
#if UNITY_EDITOR
    [ContextMenu("JumpToNoon")]
    private void JumpToNoon() { SetTimeOfDay(12f); UnityEditor.EditorUtility.SetDirty(this); }
    [ContextMenu("JumpToSunrise")]
    private void JumpToSunrise() { SetTimeOfDay(6f); UnityEditor.EditorUtility.SetDirty(this); }
    [ContextMenu("JumpToSunset")]
    private void JumpToSunset() { SetTimeOfDay(18f); UnityEditor.EditorUtility.SetDirty(this); }
#endif
}