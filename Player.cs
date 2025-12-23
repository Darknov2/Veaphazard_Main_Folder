using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 10f;
    public float gravity = -9.81f;
    public float jumpHeight = 2f;

    [Header("Camera Settings")]
    public Transform cameraTransform;
    public float lookSpeed = 2.0f;
    public float lookXLimit = 45.0f;

    [Header("Raycast Settings")]
    public float raycastDistance = 3f;
    public LayerMask raycastLayerMask = ~0;

    [Header("Interaction")]
    public bool interactionVerboseLogs = true;

    [Header("Physics Push")]
    public float pushForce = 5f;

    [Header("Build Mode")]
    public bool buildMode = false;

    // New: Throwing settings (press and hold R to charge)
    [Header("Throwing")]
    [Tooltip("Prefab to spawn when throwing. Assign the selected-inventory item's prefab here.")]
    public GameObject throwablePrefab;
    [Tooltip("Minimum launch speed when tapping R (m/s).")]
    public float minThrowForce = 4f;
    [Tooltip("Maximum launch speed when fully charged (m/s).")]
    public float maxThrowForce = 24f;
    [Tooltip("Time in seconds to reach max force when holding R.")]
    public float maxChargeTime = 1.5f;
    [Tooltip("Spawn offset in front of the camera (meters).")]
    public float throwSpawnDistance = 1.0f;
    [Tooltip("If true and the spawned object has no Rigidbody, a Rigidbody is automatically added.")]
    public bool autoAddRigidbody = true;

    // ---- Removed legacy terrain-edit fields and methods (use InputTerrainBrush instead) ----

    private CharacterController characterController;
    private Vector3 velocity;
    private bool isGrounded;
    private float rotationX = 0;

    // Charging state
    private bool isChargingThrow = false;
    private float chargeStartTime = 0f;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMovement();

        HandleDestroyObject();
        TryPushObjects();

        // Charging & throwing logic (press and hold R)
        if (Input.GetKeyDown(KeyCode.R))
        {
            // start charging
            isChargingThrow = true;
            chargeStartTime = Time.time;
        }

        if (isChargingThrow && Input.GetKeyUp(KeyCode.R))
        {
            // release and throw
            float chargeDuration = Time.time - chargeStartTime;
            float t = Mathf.Clamp01(chargeDuration / maxChargeTime);
            float launchSpeed = Mathf.Lerp(minThrowForce, maxThrowForce, t);
            ThrowItemFromCamera(launchSpeed);
            isChargingThrow = false;
        }

        // If input cancelled (e.g., build mode toggled) or other conditions, reset charging
        if (isChargingThrow && !Input.GetKey(KeyCode.R))
        {
            // safety: if key state inconsistent, stop charging
            isChargingThrow = false;
        }

        // NOTE: Terrain editing removed from Player; attach InputTerrainBrush to a manager or player GameObject instead.
    }

    private void HandleMovement()
    {
        isGrounded = characterController.isGrounded;
        if (isGrounded && velocity.y < 0) velocity.y = -2f;

        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : moveSpeed;

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");
        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        characterController.Move(move * currentSpeed * Time.deltaTime);

        if (Input.GetButtonDown("Jump") && isGrounded)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);

        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        transform.Rotate(Vector3.up * mouseX);
    }



    private void HandleDestroyObject()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            RaycastHit hit;
            if (Physics.Raycast(cameraTransform.position, cameraTransform.forward, out hit, raycastDistance, raycastLayerMask))
            {
                GameObject targetObject = hit.collider.gameObject;

                if (targetObject.CompareTag("Destroyable"))
                {
                    Transform parent = targetObject.transform.parent;
                    if (parent != null && parent.CompareTag("Destroyable"))
                    {
                        Debug.Log($"Destroying child: {targetObject.name} of parent: {parent.name}");
                        Destroy(targetObject);
                    }
                    else
                    {
                        Debug.Log($"Destroying object: {targetObject.name}");
                        Destroy(targetObject);
                    }
                }
                else if (targetObject.transform.parent != null && targetObject.transform.parent.CompareTag("Destroyable"))
                {
                    Debug.Log($"Destroying child: {targetObject.name} of parent: {targetObject.transform.parent.name}");
                    Destroy(targetObject);
                }
            }
        }
    }

    private void TryPushObjects()
    {
        if (characterController.velocity.magnitude < 0.01f) return;

        Vector3 moveDirection = new Vector3(characterController.velocity.x, 0, characterController.velocity.z).normalized;
        Vector3 castOrigin = transform.position + characterController.center + Vector3.down * (characterController.height / 2 - characterController.radius);
        float castDistance = characterController.radius + 0.2f;

        foreach (var hit in Physics.SphereCastAll(castOrigin, characterController.radius, moveDirection, castDistance))
        {
            Rigidbody rb = hit.collider.attachedRigidbody;
            if (rb != null && !rb.isKinematic && rb.gameObject != this.gameObject)
            {
                rb.AddForce(moveDirection * pushForce, ForceMode.VelocityChange);
            }
        }
    }

    // Instantiate the throwable prefab in front of camera and apply velocity along camera forward
    private void ThrowItemFromCamera(float launchSpeed)
    {
        if (throwablePrefab == null)
        {
            if (interactionVerboseLogs) Debug.LogWarning("PlayerController: throwablePrefab is not assigned. Assign a prefab in the inspector to enable throwing.");
            return;
        }

        Transform cam = cameraTransform != null ? cameraTransform : (Camera.main != null ? Camera.main.transform : null);
        Vector3 spawnPos;
        Vector3 forwardDir;
        if (cam != null)
        {
            spawnPos = cam.position + cam.forward * throwSpawnDistance;
            forwardDir = cam.forward;
        }
        else
        {
            // Fallback to player forward if camera missing
            spawnPos = transform.position + transform.forward * throwSpawnDistance + Vector3.up * 0.5f;
            forwardDir = transform.forward;
        }

        GameObject thrown = Instantiate(throwablePrefab, spawnPos, Quaternion.identity);
        if (thrown == null) return;

        Rigidbody rb = thrown.GetComponent<Rigidbody>();
        if (rb == null && autoAddRigidbody)
        {
            rb = thrown.AddComponent<Rigidbody>();
        }

        Vector3 launchVelocity = forwardDir.normalized * launchSpeed;
        // Add player's current horizontal velocity so thrown items inherit player motion slightly
        if (characterController != null)
            launchVelocity += new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);

        if (rb != null)
        {
            rb.velocity = launchVelocity;
        }
        else
        {
            // If no Rigidbody, try moving transform as a last resort (non-physical)
            thrown.transform.position += launchVelocity * Time.deltaTime;
        }
    }
}