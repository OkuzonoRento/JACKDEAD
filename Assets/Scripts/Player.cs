
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    [Header("プレイヤーデータ")]
    [SerializeField] private PlayerDataAsset playerData;

    [Header("視点設定")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float mouseSensitivity = 2f;

    [Header("移動設定")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;

    [Header("スタミナ設定")]
    [SerializeField] private float staminaDrain = 20f;
    [SerializeField] private float staminaRecovery = 15f;

    [Header("しゃがみ設定")]
    [SerializeField] private float crouchHeight = 1.2f;
    [SerializeField] private float crouchCameraHeight = 0.45f;

    private CharacterController controller;
    private float verticalVelocity;
    private float cameraPitch;
    private bool jumpRequested;

    private float currentStamina;
    private float standingHeight;
    private Vector3 standingCenter;
    private float standingCameraHeight;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        standingHeight = controller.height;
        standingCenter = controller.center;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform != null)
        {
            standingCameraHeight = cameraTransform.localPosition.y;
        }

        currentStamina = playerData != null
            ? playerData.MaxStamina
            : 100f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpRequested = true;
        }

        Look();
        Move();
        HandleCursor();
    }

    private void HandleCursor()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Look()
    {
        if (cameraTransform == null)
        {
            return;
        }

        float mouseX =
            Input.GetAxis("Mouse X") * mouseSensitivity;

        float mouseY =
            Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -85f, 85f);

        cameraTransform.localRotation =
            Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void Move()
    {
        float normalSpeed = playerData != null
            ? playerData.MoveSpeed
            : 5f;

        float maxStamina = playerData != null
            ? playerData.MaxStamina
            : 100f;

        bool crouching = Input.GetKey(KeyCode.LeftControl);
        bool wantsSprint = Input.GetKey(KeyCode.LeftShift);

        bool sprinting = wantsSprint
            && !crouching
            && currentStamina > 0f;

        float speed = crouching
            ? crouchSpeed
            : sprinting ? sprintSpeed : normalSpeed;

        UpdateCrouch(crouching);

        if (sprinting)
        {
            currentStamina -= staminaDrain * Time.deltaTime;
        }
        else
        {
            currentStamina += staminaRecovery * Time.deltaTime;
        }

        currentStamina = Mathf.Clamp(
            currentStamina, 0f, maxStamina
        );

        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        Vector3 move =
            (transform.right * x + transform.forward * z).normalized;

        bool grounded = controller.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        // 接地中にSpaceキーを押すとジャンプ
        if (grounded && jumpRequested && !crouching)
        {
            verticalVelocity = Mathf.Sqrt(
                2f * jumpHeight * -gravity
            );
        }

        jumpRequested = false;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity =
            move * speed + Vector3.up * verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateCrouch(bool crouching)
    {
        if (crouching)
        {
            controller.height = crouchHeight;

            controller.center = new Vector3(
                standingCenter.x,
                crouchHeight / 2f,
                standingCenter.z
            );

            if (cameraTransform != null)
            {
                Vector3 pos = cameraTransform.localPosition;
                pos.y = crouchCameraHeight;
                cameraTransform.localPosition = pos;
            }
        }
        else
        {
            controller.height = standingHeight;
            controller.center = standingCenter;

            if (cameraTransform != null)
            {
                Vector3 pos = cameraTransform.localPosition;
                pos.y = standingCameraHeight;
                cameraTransform.localPosition = pos;
            }
        }
    }
}
