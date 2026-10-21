using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("プレイヤーコントローラー参照")]
    [SerializeField] private PlayerController playerController;

    [Header("移動パラメータ")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -9.81f * 2f;

    [Header("スタミナパラメータ")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaDrainRate = 20f;
    [SerializeField] private float normalRecoveryRate = 8f;
    [SerializeField] private float idleRecoveryRate = 18f;
    [SerializeField] private float crouchRecoveryRate = 30f;
    [SerializeField] private float staminaThresholdToResume = 30f;
    private bool isExhausted = false;

    private float currentStamina;

    [Header("視点操作パラメータ")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float upLookLimit = -80f;
    [SerializeField] private float downLookLimit = 80f;

    [Header("しゃがみ・高さ設定")]
    [SerializeField] private float standingHeight = 2.0f;
    [SerializeField] private float crouchingHeight = 1.0f;
    [SerializeField] private float standingCameraY = 0.6f;
    [SerializeField] private float crouchingCameraY = 0.0f;
    [SerializeField] private float heightChangeSpeed = 10f;

    [Header("ダッシュ時の視野角 (FOV) 演出")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float sprintFOV = 75f;
    [SerializeField] private float fovChangeSpeed = 8f;

    private CharacterController controller;
    private InputActionAsset playerInputActions; // PlayerControllerから取得
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private InputAction crouchAction;

    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 velocity;
    private bool isGrounded;

    private float cameraPitch = 0f;

    public float CurrentStamina => currentStamina;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerController == null) playerController = GetComponent<PlayerController>();

        currentStamina = maxStamina;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (targetCamera == null && cameraTransform != null)
        {
            targetCamera = cameraTransform.GetComponent<Camera>();
        }

        if (controller != null)
        {
            controller.height = standingHeight;
        }

        if (targetCamera != null)
        {
            normalFOV = targetCamera.fieldOfView;
        }
    }

    private void OnEnable()
    {
        // PlayerController経由でInputActionsを取得してセットアップ
        if (playerController != null)
        {
            playerInputActions = playerController.PlayerInputActionsAsset;
        }

        if (playerInputActions != null)
        {
            var map = playerInputActions.FindActionMap("Player");
            if (map != null)
            {
                moveAction = map.FindAction("Move");
                lookAction = map.FindAction("Look");
                jumpAction = map.FindAction("Jump");
                sprintAction = map.FindAction("Sprint");
                crouchAction = map.FindAction("Crouch");

                map.Enable();
            }
        }
    }

    private void OnDisable()
    {
        // アセット自体はPlayerControllerが管理するため、マップの無効化のみ行う
        if (playerInputActions != null)
        {
            playerInputActions.FindActionMap("Player")?.Disable();
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        if (lookAction != null && cameraTransform != null)
        {
            lookInput = lookAction.ReadValue<Vector2>();
            float mouseX = lookInput.x * mouseSensitivity;
            float mouseY = lookInput.y * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, upLookLimit, downLookLimit);
            cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }

        if (moveAction != null)
        {
            moveInput = moveAction.ReadValue<Vector2>();
        }

        bool isCrouching = crouchAction != null && crouchAction.IsPressed();
        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        bool isIdling = !isMoving;

        if (isExhausted)
        {
            if (currentStamina >= staminaThresholdToResume)
            {
                isExhausted = false;
            }
        }

        bool canSprint = !isExhausted && currentStamina > 0f && moveInput.y > 0;
        bool isSprinting = sprintAction != null && sprintAction.IsPressed() && !isCrouching && canSprint;

        if (isSprinting)
        {
            float totalWeight = playerController != null ? playerController.GetTotalInventoryWeight() : 0f;
            float weightMultiplier = 1.0f + (totalWeight * 0.1f);

            currentStamina -= staminaDrainRate * weightMultiplier * Time.deltaTime;

            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                isExhausted = true;
                Debug.Log("[スタミナ] スタミナが完全に切れました！息切れ状態");
            }
        }
        else
        {
            float recoveryRate = normalRecoveryRate;

            if (isCrouching)
            {
                if (isMoving) recoveryRate = normalRecoveryRate;
                else recoveryRate = crouchRecoveryRate;
            }
            else if (isIdling)
            {
                recoveryRate = idleRecoveryRate;
            }

            currentStamina = Mathf.Clamp(currentStamina + recoveryRate * Time.deltaTime, 0f, maxStamina);
        }

        float currentSpeed = walkSpeed;
        if (isSprinting) currentSpeed = sprintSpeed;
        else if (isCrouching) currentSpeed = crouchSpeed;

        float targetControllerHeight = isCrouching ? crouchingHeight : standingHeight;
        float targetCameraY = isCrouching ? crouchingCameraY : standingCameraY;

        controller.height = Mathf.Lerp(controller.height, targetControllerHeight, Time.deltaTime * heightChangeSpeed);

        if (cameraTransform != null)
        {
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, targetCameraY, Time.deltaTime * heightChangeSpeed);
            cameraTransform.localPosition = camPos;
        }

        if (targetCamera != null)
        {
            float targetFOV = isSprinting ? sprintFOV : normalFOV;
            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFOV, Time.deltaTime * fovChangeSpeed);
        }

        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        controller.Move(moveDir * currentSpeed * Time.deltaTime);

        if (jumpAction != null && jumpAction.triggered && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}