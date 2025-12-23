using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CrosshairController
/// - Place this on a UI Image used as the crosshair (or on any manager and assign the Image).
/// - Changes the crosshair color from normalColor -> targetColor when the center ray hits an object
///   with tag == enemyTag. Smoothly interpolates color for a pleasant visual.
/// </summary>
[DisallowMultipleComponent]
public class CrosshairController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used for aiming. If null, Camera.main will be used.")]
    public Camera aimCamera;
    [Tooltip("UI Image used as the crosshair. If null and this component is on the Image GameObject, it will be fetched automatically.")]
    public Image crosshairImage;

    [Header("Detection")]
    [Tooltip("Tag used to identify enemies.")]
    public string enemyTag = "Enemy";
    [Tooltip("Layer mask for the raycast. Use ~0 to raycast against everything.")]
    public LayerMask raycastMask = ~0;
    [Tooltip("Max distance for the raycast.")]
    public float maxDistance = 200f;
    [Tooltip("Use SphereCast instead of Raycast (helps hit thin/skinned targets).")]
    public bool useSphereCast = false;
    [Tooltip("Radius used by SphereCast when enabled.")]
    public float sphereRadius = 0.15f;

    [Header("Visuals")]
    [Tooltip("Color when not pointing at an enemy.")]
    public Color normalColor = Color.white;
    [Tooltip("Color when pointing at an enemy.")]
    public Color targetColor = Color.green;
    [Tooltip("How fast the color transitions (higher = snappier).")]
    [Range(1f, 30f)] public float transitionSpeed = 12f;

    // internal
    Color _currentColor;
    Camera _cam;

    void Awake()
    {
        // fallback camera
        _cam = aimCamera != null ? aimCamera : Camera.main;
        if (_cam == null)
            Debug.LogWarning("[CrosshairController] No camera assigned and Camera.main is null.");

        // fallback image
        if (crosshairImage == null)
            crosshairImage = GetComponent<Image>();

        if (crosshairImage == null)
            Debug.LogWarning("[CrosshairController] No crosshair Image assigned and none found on the GameObject.");

        _currentColor = normalColor;
        if (crosshairImage != null)
            crosshairImage.color = _currentColor;
    }

    void Update()
    {
        if (crosshairImage == null) return;

        bool pointingAtEnemy = false;

        if (_cam != null)
        {
            // Ray from center of screen
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (useSphereCast)
            {
                if (Physics.SphereCast(ray, sphereRadius, out RaycastHit hit, maxDistance, raycastMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform != null && hit.transform.CompareTag(enemyTag))
                        pointingAtEnemy = true;
                }
            }
            else
            {
                if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, raycastMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform != null && hit.transform.CompareTag(enemyTag))
                        pointingAtEnemy = true;
                }
            }
        }

        Color target = pointingAtEnemy ? targetColor : normalColor;
        _currentColor = Color.Lerp(_currentColor, target, Mathf.Clamp01(Time.deltaTime * transitionSpeed));
        crosshairImage.color = _currentColor;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (crosshairImage == null)
            crosshairImage = GetComponent<Image>();
    }
#endif
}