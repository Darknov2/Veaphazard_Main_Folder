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

    [Header("Throwing")]
    public GameObject throwablePrefab;           // prefab to instantiate when throwing
    public float throwForce = 12f;               // initial speed applied to thrown object
    public float throwSpawnDistance = 1.0f;      // spawn offset in front of camera
    public bool autoAddRigidbody = true;         // if spawned object has no Rigidbody, optionally add one

    // ---- Removed legacy terrain-edit fields and methods (use InputTerrainBrush instead) ----

    private CharacterController characterController;
    private Vector3 velocity;
    private bool isGrounded;
    private float rotationX = 0;

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

        if (Input.GetKeyDown(KeyCode.R))
            ThrowItemFromCamera();

        HandleDestroyObject();
        TryPushObjects();

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

    private void ThrowItemFromCamera()
    {
        // Check if throwablePrefab is assigned
        if (throwablePrefab == null)
        {
            Debug.LogWarning("[PlayerController] throwablePrefab is not assigned. Cannot throw.");
            return;
        }

        // Resolve camera transform (use cameraTransform field or Camera.main fallback)
        Transform activeCamera = cameraTransform;
        if (activeCamera == null && Camera.main != null)
            activeCamera = Camera.main.transform;

        Vector3 spawnPosition;
        Vector3 throwDirection;

        if (activeCamera != null)
        {
            // Use camera transform
            spawnPosition = activeCamera.position + activeCamera.forward * throwSpawnDistance;
            throwDirection = activeCamera.forward;
        }
        else
        {
            // Fallback to player forward and spawn slightly above player
            spawnPosition = transform.position + transform.forward * throwSpawnDistance + Vector3.up * 1.5f;
            throwDirection = transform.forward;
        }

        // Instantiate throwablePrefab at calculated position with identity rotation
        GameObject thrownObject = Instantiate(throwablePrefab, spawnPosition, Quaternion.identity);

        // Ensure the spawned object has a Rigidbody
        Rigidbody rb = thrownObject.GetComponent<Rigidbody>();
        if (rb == null && autoAddRigidbody)
        {
            rb = thrownObject.AddComponent<Rigidbody>();
        }

        // Compute launch velocity: camera forward * throwForce + player horizontal velocity
        Vector3 launchVelocity = throwDirection.normalized * throwForce;
        
        // Add player's horizontal velocity (xz components)
        Vector3 playerHorizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        launchVelocity += playerHorizontalVelocity;

        // Apply velocity to the Rigidbody
        if (rb != null)
        {
            rb.velocity = launchVelocity;
        }
        else
        {
            // Safe fallback if no Rigidbody exists: move the transform once
            thrownObject.transform.position += launchVelocity * Time.deltaTime;
        }
    }
}