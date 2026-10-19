using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input Actions アセット")]
    [SerializeField] private InputActionAsset playerInputActions;

    [Header("移動パラメータ")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -9.81f * 2f;

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
    [SerializeField] private Camera targetCamera;         // 視野角を変更するカメラ
    [SerializeField] private float normalFOV = 60f;        // 通常時のFOV
    [SerializeField] private float sprintFOV = 75f;        // ダッシュ時のFOV
    [SerializeField] private float fovChangeSpeed = 8f;    // FOVが切り替わるスピード

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

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

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
        bool isSprinting = sprintAction != null && sprintAction.IsPressed() && !isCrouching && moveInput.y > 0; // 前進中のみダッシュ可能にする場合

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

        // ---ダッシュ時の視野角（FOV）変化 ---
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