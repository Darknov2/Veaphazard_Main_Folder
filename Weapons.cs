// (full file, updated inputTerrainBrush type and lookup)
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weapons
/// - Attach to your Player GameObject (same GameObject that has the PlayerController).
/// - Three categories: Hands (default), Melee, Firearm.
/// - Press 1 / 2 / 3 to switch categories. Use mouse wheel or Q/E to cycle models within the current category.
/// - Left click uses the currently equipped weapon:
///     * Firearm: raycast from camera forward (uses weapon.range)
///     * Melee: short spherecast / overlap to detect nearby target (uses weapon.range)
///     * Hands: behaves like a simple melee (can be configured)
/// - Each WeaponDefinition exposes:
///     * model (GameObject) - the prefab or scene GameObject used as the weapon model
///     * damage (float)
///     * range (float) - used for raycast / melee range
///     * allowTerrainInteraction (bool) - when true, Weapons will ENABLE the InputTerrainBrush component (if found)
///     * allowBuildSystem (bool) - when true, Weapons will ENABLE the ConstructionSystem component (if found)
/// </summary>
[DisallowMultipleComponent]
public class Weapons : MonoBehaviour
{
    public enum WeaponCategory { Hands = 0, Melee = 1, Firearm = 2 }

    [Serializable]
    public class WeaponDefinition
    {
        public string displayName = "Weapon";
        [Tooltip("Optional model prefab or GameObject to instantiate as the visible weapon.")]
        public GameObject model;

        [Tooltip("Damage dealt when hitting an enemy.")]
        public float damage = 10f;

        [Tooltip("Range used for raycasts (firearm) or melee reach (melee).")]
        public float range = 3f;

        [Tooltip("If true this weapon may interact with terrain (enables InputTerrainBrush if present).")]
        public bool allowTerrainInteraction = false;

        [Tooltip("If true this weapon allows using the build system (enables ConstructionSystem if present).")]
        public bool allowBuildSystem = false;
    }

    [Header("References")]
    [Tooltip("Camera used for aiming/raycasting. If null Camera.main will be used.")]
    public Transform cameraTransform;
    [Tooltip("Transform that will parent the active weapon model (local position/rotation preserved by prefab).")]
    public Transform weaponHolder;

    [Header("Optional Integration Targets")]
    [Tooltip("Optional reference to the ConstructionSystem to enable/disable when switching weapons. If null the script will try to find one at Awake.")]
    public ConstructionSystem constructionSystem;

    // CHANGED: use the explicit InputTerrainBrush type so inspector only accepts that component
    [Tooltip("Optional reference to the terrain input brush component (assign the InputTerrainBrush component here).")]
    public InputTerrainBrush inputTerrainBrush;

    [Header("Hands (default)")]
    public List<WeaponDefinition> hands = new List<WeaponDefinition>();

    [Header("Melee")]
    public List<WeaponDefinition> melee = new List<WeaponDefinition>();

    [Header("Firearm")]
    public List<WeaponDefinition> firearms = new List<WeaponDefinition>();

    [Header("Input / Cycling")]
    [Tooltip("Mouse wheel and Q/E keys cycle through available models in the current category.")]
    public bool enableScrollCycling = true;

    [Header("Misc")]
    [Tooltip("LayerMask used for weapon raycasts (firearm). Use ~0 to hit all layers.")]
    public LayerMask weaponRaycastLayerMask = ~0;
    [Tooltip("If true debug lines will be drawn for raycasts.")]
    public bool debugDraw = false;

    // public state
    public WeaponCategory currentCategory { get; private set; } = WeaponCategory.Hands;
    public int currentIndexInCategory { get; private set; } = 0;
    public WeaponDefinition CurrentWeapon => GetWeaponDefinition(currentCategory, currentIndexInCategory);

    // events
    public event Action<WeaponCategory, int, WeaponDefinition> OnWeaponChanged;

    // internals
    private GameObject _activeModelInstance;
    private Transform _cam;
    private Dictionary<WeaponCategory, List<WeaponDefinition>> _lists;
    private Dictionary<WeaponCategory, int> _indices = new Dictionary<WeaponCategory, int>()
    {
        { WeaponCategory.Hands, 0 },
        { WeaponCategory.Melee, 0 },
        { WeaponCategory.Firearm, 0 }
    };

    void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        _cam = cameraTransform;

        // build dictionary for easy access
        _lists = new Dictionary<WeaponCategory, List<WeaponDefinition>>()
        {
            { WeaponCategory.Hands, hands },
            { WeaponCategory.Melee, melee },
            { WeaponCategory.Firearm, firearms }
        };

        // Ensure indices are in-range
        ClampAllIndices();

        // try to auto-find integration targets if not assigned
        if (constructionSystem == null)
            constructionSystem = SceneFind.First<ConstructionSystem>();

        // find InputTerrainBrush only if field not assigned in inspector
        if (inputTerrainBrush == null)
            inputTerrainBrush = SceneFind.First<InputTerrainBrush>();

        if (inputTerrainBrush == null)
        {
            Debug.LogWarning("Weapons: InputTerrainBrush not assigned or found. If you want terrain editing enabled by weapons, assign the InputTerrainBrush component in the inspector.");
        }

        // Spawn initial weapon model
        ApplyWeaponModel();

        // Ensure initial integration state matches the starting weapon
        UpdateInteractionFlags();
    }

    void OnEnable()
    {
        UpdateInteractionFlags();
    }

    void Update()
    {
        HandleCategorySwitchInput();
        HandleCycleInput();
        HandleFireInput();
    }

    private void HandleCategorySwitchInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchCategory(WeaponCategory.Hands);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchCategory(WeaponCategory.Melee);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchCategory(WeaponCategory.Firearm);
    }

    private void HandleCycleInput()
    {
        if (enableScrollCycling)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                int dir = scroll > 0f ? 1 : -1;
                CycleInCurrentCategory(dir);
            }
        }

        if (Input.GetKeyDown(KeyCode.Q)) CycleInCurrentCategory(-1);
        if (Input.GetKeyDown(KeyCode.E)) CycleInCurrentCategory(1);
    }

    private void HandleFireInput()
    {
        if (Input.GetMouseButtonDown(0))
            UseCurrentWeapon();
    }

    private void UseCurrentWeapon()
    {
        var w = CurrentWeapon;
        if (w == null) return;

        switch (currentCategory)
        {
            case WeaponCategory.Firearm:
                FireRay(w);
                break;
            case WeaponCategory.Melee:
            case WeaponCategory.Hands:
                PerformMelee(w);
                break;
        }
    }

    private void FireRay(WeaponDefinition w)
    {
        if (_cam == null)
        {
            Debug.LogWarning("Weapons: no camera assigned for firing.");
            return;
        }

        Vector3 origin = _cam.position;
        Vector3 dir = _cam.forward;

        if (debugDraw) Debug.DrawRay(origin, dir * w.range, Color.red, 1f);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, w.range, weaponRaycastLayerMask, QueryTriggerInteraction.Ignore))
        {
            TryDamageTarget(hit.collider.gameObject, w.damage);
        }
    }

    private void PerformMelee(WeaponDefinition w)
    {
        if (_cam == null)
        {
            Debug.LogWarning("Weapons: no camera assigned for melee.");
            return;
        }

        Vector3 origin = _cam.position;
        Vector3 dir = _cam.forward;
        float radius = Mathf.Max(0.25f, w.range * 0.25f);

        if (debugDraw)
        {
            Debug.DrawRay(origin, dir * w.range, Color.yellow, 0.5f);
            Vector3 end = origin + dir * w.range;
            Debug.DrawLine(end + Vector3.up * radius, end - Vector3.up * radius, Color.yellow, 0.5f);
            Debug.DrawLine(end + Vector3.right * radius, end - Vector3.right * radius, Color.yellow, 0.5f);
        }

        if (Physics.SphereCast(origin, radius, dir, out RaycastHit hit, w.range, weaponRaycastLayerMask, QueryTriggerInteraction.Ignore))
        {
            TryDamageTarget(hit.collider.gameObject, w.damage);
        }
        else
        {
            Collider[] cols = Physics.OverlapSphere(origin + dir * (w.range * 0.5f), radius, weaponRaycastLayerMask, QueryTriggerInteraction.Ignore);
            foreach (var c in cols)
            {
                TryDamageTarget(c.gameObject, w.damage);
                break;
            }
        }
    }

    private void TryDamageTarget(GameObject target, float damage)
    {
        if (target == null) return;

        if (!target.CompareTag("Enemy"))
        {
            if (debugDraw) Debug.Log($"Weapons: hit non-enemy '{target.name}'");
            return;
        }

        var health = target.GetComponent<EnemyHealth>() ?? target.GetComponentInParent<EnemyHealth>();
        if (health != null)
        {
            health.ApplyDamage(damage);
            if (debugDraw) Debug.Log($"Weapons: applied {damage} damage to '{target.name}'");
        }
        else
        {
            if (debugDraw) Debug.LogWarning($"Weapons: hit '{target.name}' but no EnemyHealth component found.");
        }
    }

    public void SwitchCategory(WeaponCategory cat)
    {
        if (currentCategory == cat) return;
        currentCategory = cat;
        ClampIndexForCategory(cat);
        currentIndexInCategory = _indices[cat];
        ApplyWeaponModel();
        UpdateInteractionFlags();
        OnWeaponChanged?.Invoke(cat, currentIndexInCategory, CurrentWeapon);
    }

    public void CycleInCurrentCategory(int direction)
    {
        var list = GetListForCategory(currentCategory);
        if (list == null || list.Count == 0) return;

        int idx = _indices[currentCategory];
        idx = (idx + direction) % list.Count;
        if (idx < 0) idx += list.Count;
        _indices[currentCategory] = idx;

        currentIndexInCategory = idx;
        ApplyWeaponModel();
        UpdateInteractionFlags();
        OnWeaponChanged?.Invoke(currentCategory, currentIndexInCategory, CurrentWeapon);
    }

    private void ApplyWeaponModel()
    {
        if (_activeModelInstance != null)
        {
            Destroy(_activeModelInstance);
            _activeModelInstance = null;
        }

        var w = CurrentWeapon;
        if (w == null || w.model == null || weaponHolder == null)
            return;

        _activeModelInstance = Instantiate(w.model, weaponHolder.position, weaponHolder.rotation, weaponHolder);
        _activeModelInstance.transform.localPosition = Vector3.zero;
        _activeModelInstance.transform.localRotation = Quaternion.identity;
        _activeModelInstance.transform.localScale = Vector3.one;
    }

    private List<WeaponDefinition> GetListForCategory(WeaponCategory cat)
    {
        if (_lists != null && _lists.ContainsKey(cat))
            return _lists[cat];

        switch (cat)
        {
            case WeaponCategory.Hands: return hands;
            case WeaponCategory.Melee: return melee;
            case WeaponCategory.Firearm: return firearms;
            default: return null;
        }
    }

    private WeaponDefinition GetWeaponDefinition(WeaponCategory cat, int idx)
    {
        var list = GetListForCategory(cat);
        if (list == null || list.Count == 0) return null;
        idx = Mathf.Clamp(idx, 0, list.Count - 1);
        return list[idx];
    }

    private void ClampAllIndices()
    {
        foreach (WeaponCategory c in Enum.GetValues(typeof(WeaponCategory)))
            ClampIndexForCategory(c);
        currentIndexInCategory = _indices[currentCategory];
    }

    private void ClampIndexForCategory(WeaponCategory c)
    {
        var list = GetListForCategory(c);
        if (list == null || list.Count == 0)
            _indices[c] = 0;
        else
            _indices[c] = Mathf.Clamp(_indices[c], 0, list.Count - 1);
    }

    private void UpdateInteractionFlags()
    {
        var w = CurrentWeapon;
        bool allowBuild = (w != null) && w.allowBuildSystem;
        bool allowTerrain = (w != null) && w.allowTerrainInteraction;

        if (constructionSystem != null)
            constructionSystem.enabled = allowBuild;

        if (inputTerrainBrush != null)
            inputTerrainBrush.enabled = allowTerrain;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDraw || cameraTransform == null) return;
        var w = CurrentWeapon;
        if (w == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(cameraTransform.position + cameraTransform.forward * w.range, 0.1f);
    }
}