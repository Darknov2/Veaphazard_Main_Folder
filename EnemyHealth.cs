using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple per-enemy health component.
/// - Set Max Health in the Inspector per-enemy (each enemy instance gets its own value).
/// - Call ApplyDamage(amount) to deal damage, Heal(amount) to restore health.
/// - Invokes OnDeath when health reaches zero; default behaviour is Destroy(gameObject).
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Starting and maximum health for this enemy. Editable per-instance in the Inspector.")]
    public float maxHealth = 100f;

    [Tooltip("Current health (serialized so visible in Inspector at runtime).")]
    [SerializeField]
    private float currentHealth = -1f;

    [Header("Death")]
    [Tooltip("Invoked when health reaches zero or below.")]
    public UnityEvent OnDeath;

    private bool _isDead = false;

    void Reset()
    {
        maxHealth = 100f;
        currentHealth = maxHealth;
    }

    void Awake()
    {
        // initialize currentHealth to max if not set in inspector
        if (currentHealth <= 0f)
            currentHealth = maxHealth;
        else
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    /// <summary>
    /// Apply damage to this enemy. Returns true if the enemy died from this hit.
    /// </summary>
    public bool ApplyDamage(float amount)
    {
        if (_isDead) return false;
        if (amount <= 0f) return false;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Heal the enemy by amount (clamped to max).
    /// </summary>
    public void Heal(float amount)
    {
        if (_isDead) return;
        if (amount <= 0f) return;

        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
    }

    /// <summary>
    /// Set health directly (clamped).
    /// </summary>
    public void SetHealth(float value)
    {
        if (_isDead) return;
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        if (currentHealth <= 0f) Die();
    }

    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        try { OnDeath?.Invoke(); } catch { /* swallow handler errors */ }

        // Default behaviour: destroy this GameObject.
        // If you prefer custom handling (pooling, ragdoll), subscribe to OnDeath and remove this Destroy.
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // show health in scene view for convenience
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, $"HP: {currentHealth:F0}/{maxHealth:F0}");
    }
#endif
}