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

    [Header("ダッシュ・注視時の視野角 (FOV) 演出")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float sprintFOV = 75f;
    [SerializeField] private float lockedFOV = 42f;
    [SerializeField] private float fovChangeSpeed = 8f;

    private CharacterController controller;
    private InputActionAsset playerInputActions;
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

    // --- スロット等の強制移動＆視点ロック用 ---
    private bool isControlLocked = false;
    private Vector3 lockStartPos;
    private Vector3 lockTargetStandPos;
    private Vector3 lockLookWorldPos;
    private float lockStartYaw;
    private float lockStartPitch;
    private float lockStartFOV;
    private float lockTransitionDuration = 0.4f;
    private float lockTransitionElapsed = 0f;

    public float CurrentStamina => currentStamina;
    public bool IsControlLocked => isControlLocked;

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

    /// <summary>
    /// プレイヤーの操作をロックし、指定した立ち位置へ滑らかに移動させながら視野角を狭めて注視点へ向ける
    /// </summary>
    public void LockControlAndMoveTo(Vector3 targetStandPos, Vector3 lookWorldPos, float transitionDuration = 0.4f)
    {
        isControlLocked = true;
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;

        lockStartPos = transform.position;
        lockTargetStandPos = new Vector3(targetStandPos.x, transform.position.y, targetStandPos.z);
        lockLookWorldPos = lookWorldPos;

        lockStartYaw = transform.eulerAngles.y;
        lockStartPitch = cameraPitch;
        lockStartFOV = targetCamera != null ? targetCamera.fieldOfView : normalFOV;

        lockTransitionDuration = Mathf.Max(0.05f, transitionDuration);
        lockTransitionElapsed = 0f;
    }

    /// <summary>
    /// プレイヤーの移動・視点操作ロックを解除する（FOVも通常へ自動で滑らかに戻ります）
    /// </summary>
    public void UnlockControl()
    {
        isControlLocked = false;
    }

    private void Update()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // --- ロック中（スロット稼働中）：正面位置へ移動 ＆ 視野角を狭めてリールへ固定 ---
        if (isControlLocked)
        {
            UpdateLockedMovementAndLook();
            return;
        }

        // --- 通常時の視点操作 (マウス) ---
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

        bool isCrouchingInput = crouchAction != null && crouchAction.IsPressed();
        bool isMoving = moveInput.sqrMagnitude > 0.01f;
        bool isIdling = !isMoving;

        // 5方向からの天井判定
        bool canStandUp = true;
        if (!isCrouchingInput)
        {
            Vector3 capsuleTop = transform.position + controller.center + Vector3.up * (controller.height * 0.5f);
            float neededHeight = standingHeight - controller.height;

            if (neededHeight > 0.01f)
            {
                float checkDistance = neededHeight + 0.15f;
                float radius = Mathf.Max(0.1f, controller.radius - 0.05f);

                bool hitCenter = Physics.Raycast(capsuleTop, Vector3.up, checkDistance);
                bool hitForward = Physics.Raycast(capsuleTop + transform.forward * radius, Vector3.up, checkDistance);
                bool hitBack = Physics.Raycast(capsuleTop - transform.forward * radius, Vector3.up, checkDistance);
                bool hitLeft = Physics.Raycast(capsuleTop - transform.right * radius, Vector3.up, checkDistance);
                bool hitRight = Physics.Raycast(capsuleTop + transform.right * radius, Vector3.up, checkDistance);

                if (hitCenter || hitForward || hitBack || hitLeft || hitRight)
                {
                    canStandUp = false;
                }
            }
        }

        bool isCrouching = isCrouchingInput || !canStandUp;

        if (isExhausted && currentStamina >= staminaThresholdToResume)
        {
            isExhausted = false;
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
            }
        }
        else
        {
            float recoveryRate = normalRecoveryRate;
            if (isCrouching)
            {
                recoveryRate = isMoving ? normalRecoveryRate : crouchRecoveryRate;
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

        // ★ ロック解除後はここから通常のFOV（normalFOV）またはダッシュFOVへ滑らかに戻る
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

    private void UpdateLockedMovementAndLook()
    {
        lockTransitionElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(lockTransitionElapsed / lockTransitionDuration);
        float smoothT = t * t * (3f - 2f * t);

        // 1. 立ち状態の目線高さへ滑らかに合わせる
        controller.height = Mathf.Lerp(controller.height, standingHeight, Time.deltaTime * heightChangeSpeed);
        if (cameraTransform != null)
        {
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, standingCameraY, Time.deltaTime * heightChangeSpeed);
            cameraTransform.localPosition = camPos;
        }

        // 2. ★ 視野角（FOV）を lockedFOV へ滑らかに狭める（ズームイン）
        if (targetCamera != null)
        {
            targetCamera.fieldOfView = Mathf.Lerp(lockStartFOV, lockedFOV, smoothT);
        }

        // 3. スタミナ回復
        currentStamina = Mathf.Clamp(currentStamina + idleRecoveryRate * Time.deltaTime, 0f, maxStamina);
        if (isExhausted && currentStamina >= staminaThresholdToResume)
        {
            isExhausted = false;
        }

        // 4. スロット正面の立ち位置へ滑らかに移動
        Vector3 desiredXZ = Vector3.Lerp(lockStartPos, lockTargetStandPos, smoothT);
        Vector3 moveDelta = new Vector3(
            desiredXZ.x - transform.position.x,
            velocity.y * Time.deltaTime,
            desiredXZ.z - transform.position.z
        );

        velocity.y += gravity * Time.deltaTime;
        controller.Move(moveDelta);

        // 5. カメラをスロット中央へ滑らかに回転
        if (cameraTransform != null)
        {
            Vector3 dirToTarget = lockLookWorldPos - cameraTransform.position;

            Vector3 flatDir = new Vector3(dirToTarget.x, 0f, dirToTarget.z);
            if (flatDir.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                float currentYaw = Mathf.LerpAngle(lockStartYaw, targetYaw, smoothT);
                transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
            }

            float horizontalDist = new Vector2(dirToTarget.x, dirToTarget.z).magnitude;
            float targetPitch = -Mathf.Atan2(dirToTarget.y, horizontalDist) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(targetPitch, upLookLimit, downLookLimit);

            cameraPitch = Mathf.LerpAngle(lockStartPitch, targetPitch, smoothT);
            cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }
}