using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input Actions アセット")]
    [SerializeField] private InputActionAsset playerInputActions;

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
    [SerializeField] private float staminaDrainRate = 20f;     // ダッシュ時の1秒あたりの消費量

    [Header("スタミナ回復パラメータ")]
    [SerializeField] private float normalRecoveryRate = 8f;   // 通常時（歩き・移動中）の回復速度
    [SerializeField] private float idleRecoveryRate = 18f;     // 停止中（止まっているとき）の回復速度
    [SerializeField] private float crouchRecoveryRate = 30f;  // しゃがみ中の回復速度

    [Header("スタミナ枯渇ペナルティ設定")]
    [SerializeField] private float staminaThresholdToResume = 30f; // 再びダッシュ可能になるまでの回復閾値 (例: 30%)
    private bool isExhausted = false; // スタミナが完全に切れてバテた状態か

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
        playerInputActions?.FindActionMap("Player")?.Disable();
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

        // --- 視点操作 (マウス) ---
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

        // --- 移動入力 & 状態判定 ---
        if (moveAction != null)
        {
            moveInput = moveAction.ReadValue<Vector2>();
        }

        bool isCrouching = crouchAction != null && crouchAction.IsPressed();
        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        bool isIdling = !isMoving;

        // --- スタミナ枯渇（Exhausted）の判定更新 ---
        if (isExhausted)
        {
            // 一定割合（例: 30%）まで回復するまではダッシュ不可状態を維持
            if (currentStamina >= staminaThresholdToResume)
            {
                isExhausted = false;
            }
        }

        // ダッシュ可能条件：スタミナ切れを起こしておらず、スタミナが残っており、前進中で、しゃがんでいない
        bool canSprint = !isExhausted && currentStamina > 0f && moveInput.y > 0;
        bool isSprinting = sprintAction != null && sprintAction.IsPressed() && !isCrouching && canSprint;

        // --- スタミナの消費と状態別回復 ---
        if (isSprinting)
        {
            // インベントリの総重量に応じた消費倍率
            float totalWeight = playerController != null ? playerController.GetTotalInventoryWeight() : 0f;
            float weightMultiplier = 1.0f + (totalWeight * 0.1f);

            currentStamina -= staminaDrainRate * weightMultiplier * Time.deltaTime;

            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                isExhausted = true; // スタミナ完全枯渇フラグをON
                Debug.Log("[スタミナ] スタミナが完全に切れました！息切れ状態（一定まで回復するまでダッシュ不可）");
            }
        }
        else
        {
            // 非ダッシュ時の回復（状態によって速度を変更）
            float recoveryRate = normalRecoveryRate; // デフォルト（歩き・移動中）

            if (isCrouching)
            {
                // しゃがみ中でも「移動しているかどうか」で速度を分ける
                if (isMoving)
                {
                    recoveryRate = normalRecoveryRate; // しゃがみ歩き中は通常速度（歩き中と同じ）
                }
                else
                {
                    recoveryRate = crouchRecoveryRate; // 止まってしゃがんでいるときは最速回復
                }
            }
            else if (isIdling)
            {
                recoveryRate = idleRecoveryRate;     // 立ち止まっている時は通常回復
            }

            currentStamina = Mathf.Clamp(currentStamina + recoveryRate * Time.deltaTime, 0f, maxStamina);
        }

        float currentSpeed = walkSpeed;
        if (isSprinting) currentSpeed = sprintSpeed;
        else if (isCrouching) currentSpeed = crouchSpeed;

        // --- しゃがみに合わせたコライダーとカメラ高さのスムーズな変更 ---
        float targetControllerHeight = isCrouching ? crouchingHeight : standingHeight;
        float targetCameraY = isCrouching ? crouchingCameraY : standingCameraY;

        controller.height = Mathf.Lerp(controller.height, targetControllerHeight, Time.deltaTime * heightChangeSpeed);

        if (cameraTransform != null)
        {
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, targetCameraY, Time.deltaTime * heightChangeSpeed);
            cameraTransform.localPosition = camPos;
        }

        // --- ダッシュ時の視野角（FOV）変化 ---
        if (targetCamera != null)
        {
            float targetFOV = isSprinting ? sprintFOV : normalFOV;
            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFOV, Time.deltaTime * fovChangeSpeed);
        }

        // 移動方向の計算
        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        controller.Move(moveDir * currentSpeed * Time.deltaTime);

        // --- ジャンプ ---
        if (jumpAction != null && jumpAction.triggered && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}