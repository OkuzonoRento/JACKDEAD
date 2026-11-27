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

    [Header("懐中電灯")]
    [Tooltip("未設定なら、カメラの子にあるライトを使う")]
    [SerializeField] private Light flashlight;
    [SerializeField, Range(0f, 2f), Tooltip("ライトに設定されている強さに掛ける倍率（小さいほど控えめ）")]
    private float flashlightIntensityScale = 0.7f;

    private float flashlightBaseIntensity = -1f;
    private float flashlightBaseRange, flashlightBaseAngle, flashlightBaseInnerAngle;

    /// <summary>
    /// ライトの明るさ・届く距離・広さを決める（手本は自分のライトの元の設定）。
    /// held が懐中電灯などなら、その分だけ明るくする。他プレイヤーのライトにも使う
    /// </summary>
    public void ApplyFlashlightSettings(Light target, ItemDataAsset held)
    {
        if (target == null || flashlightBaseIntensity < 0f) return;

        float intensity = held != null ? held.HeldLightIntensity : 1f;
        float range = held != null ? held.HeldLightRange : 1f;
        float angle = held != null ? held.HeldLightAngle : 1f;

        target.intensity = flashlightBaseIntensity * flashlightIntensityScale * intensity;
        target.range = flashlightBaseRange * range;
        target.spotAngle = Mathf.Min(150f, flashlightBaseAngle * angle);
        target.innerSpotAngle = Mathf.Min(target.spotAngle, flashlightBaseInnerAngle * angle);
    }

    /// <summary>画面に映している人（観戦中は相手）が、手に選んでいるアイテム</summary>
    private ItemDataAsset ViewedHeldItem()
    {
        long spectated = RemotePlayerSync.SpectatedClientId;
        if (spectated >= 0)
        {
            return RemotePlayerSync.Instance != null &&
                   RemotePlayerSync.Instance.TryGetStatus((ulong)spectated, out RemotePlayerSync.PlayerStatus status)
                ? GameplaySync.ResolveItemData(status.Held)
                : null;
        }

        if (playerController == null || playerController.Inventory == null) return null;
        int slot = playerController.SelectedSlotIndex;
        return slot >= 0 && slot < playerController.Inventory.Length ? playerController.Inventory[slot] : null;
    }

    /// <summary>自分の懐中電灯（他プレイヤーの画面に出すライトの、手本にもする）</summary>
    public Light Flashlight => flashlight;

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
    /// <summary>しゃがみ中か（マルチプレイで他プレイヤーに見せるために公開）</summary>
    public bool IsCrouching { get; private set; }

    private bool isSpectating = false;

    /// <summary>倒れて観戦中は、移動・視点操作を止める（カメラは SpectatorController が動かす）</summary>
    public void SetSpectating(bool spectating)
    {
        isSpectating = spectating;
        if (spectating)
        {
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            IsCrouching = false;
        }
    }

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
        wasAirborne = false;
        noLandNoiseUntil = Time.time + LandNoiseGrace;
    }

    // スタン（Box が当たったとき）：しばらく操作できない。しゃがみと同じ姿勢になり、描画の画角が狭まる
    private float stunTimer;
    private float footstepTimer;
    private const float StunFovScale = 0.65f; // スタン中の画角（通常の何倍まで狭めるか）

    /// <summary>スタン・吹き飛び中でも、視点（カメラ）だけは動かせる</summary>
    [Header("コントローラーの視点操作")]
    [SerializeField, Tooltip("右スティックを倒しきったときの視点の回る速さ（度/秒。マウス感度が標準のとき）")]
    private float gamepadLookSpeed = 170f;

    /// <summary>
    /// このフレームの視点の動き（度）。マウスは動いた量そのまま、コントローラーはスティックの倒し具合×速さ×時間。
    /// 設定画面の感度を反映する。使っているのがどちらかは、最後に操作した入力で決まる
    /// </summary>
    private Vector2 ReadLookDegrees()
    {
        Vector2 raw = lookAction.ReadValue<Vector2>();
        lookInput = raw;

        float sensScale = SettingsManager.MouseSensitivity / SettingsManager.DefaultSensitivity;

        InputControl active = lookAction.activeControl;
        if (active != null && active.device is Gamepad)
        {
            // スティックは「傾き」なので、時間を掛けて角度にする（フレームレートに左右されない）
            return raw * (gamepadLookSpeed * sensScale * Time.deltaTime);
        }
        return raw * (mouseSensitivity * sensScale);
    }

    private void HandleLookWhileStunned()
    {
        if (lookAction == null || cameraTransform == null) return;

        Vector2 lookDelta = ReadLookDegrees();
        transform.Rotate(Vector3.up * lookDelta.x);

        cameraPitch -= lookDelta.y;
        cameraPitch = Mathf.Clamp(cameraPitch, upLookLimit, downLookLimit);
    }

    /// <summary>スタン・吹き飛び中の姿勢：しゃがみと同じ高さ・カメラ位置にし、画角を狭める（他の人にもしゃがみとして見える）</summary>
    private void ApplyStunPose()
    {
        IsCrouching = true;
        float dt = Time.deltaTime;

        controller.height = Mathf.Lerp(controller.height, crouchingHeight, dt * heightChangeSpeed);

        if (cameraTransform != null)
        {
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, crouchingCameraY, dt * heightChangeSpeed);
            cameraTransform.localPosition = camPos;
            cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }

        if (targetCamera != null)
        {
            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, normalFOV * StunFovScale, dt * fovChangeSpeed);
        }
    }

    // 吹き飛ばし（爆発など）：空中にいる間は操作を受け付けない
    private float knockTimer;
    private float knockAge;
    private Vector3 knockVelocity;
    private const float KnockMaxSeconds = 2f;
    private const float KnockMinSeconds = 0.3f;
    private float knockRecoveryStun = 2.5f; // 着地してから動けるようになるまで（Launch で指定）

    /// <summary>スタン中、または吹き飛んでいる間は true（移動・視点・拾う操作ができない）</summary>
    public bool IsStunned => stunTimer > 0f || knockTimer > 0f || isTumbling;

    // 吹き飛ばされて転がっている間（PlayerTumble）。操作も、このスクリプトの移動も止める
    private bool isTumbling;

    /// <summary>視点の上下の角度（転がっている間も、マウスで自由に動かせる。PlayerTumble がカメラの向きに使う）</summary>
    public float CameraPitch => cameraPitch;

    public void SetTumbling(bool tumbling)
    {
        isTumbling = tumbling;
        if (tumbling)
        {
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            IsCrouching = false;
            velocity = Vector3.zero;
        }
    }

    /// <summary>起き上がったあと、視点の上下の角度をそろえる（左右の向きは、体の向きで決まる）</summary>
    public void ResetLook(float pitch)
    {
        cameraPitch = Mathf.Clamp(pitch, upLookLimit, downLookLimit);
    }

    /// <summary>指定の速度で吹き飛ばす（水平成分は空気抵抗で弱まり、地面に着くと終わる）</summary>
    /// <param name="recoveryStunSeconds">着地したあと、さらに動けない時間（秒）</param>
    public void Launch(Vector3 launchVelocity, float recoveryStunSeconds = 2.5f)
    {
        if (isControlLocked) return; // スロットで固定中は動かさない

        // 吹き飛ばされると、体が物理で転がる（R.E.P.O. のタンブル）。動きは PlayerTumble が行う
        PlayerTumble tumble = GetComponent<PlayerTumble>();
        if (tumble == null) tumble = gameObject.AddComponent<PlayerTumble>();
        tumble.Begin(launchVelocity);
        if (tumble.IsTumbling) return;

        knockRecoveryStun = recoveryStunSeconds;
        knockVelocity = new Vector3(launchVelocity.x, 0f, launchVelocity.z);
        velocity.y = launchVelocity.y;
        knockTimer = KnockMaxSeconds;
        knockAge = 0f;
        moveInput = Vector2.zero;
    }

    private void UpdateKnockedBack()
    {
        float dt = Time.deltaTime;
        knockAge += dt;
        moveInput = Vector2.zero;
        HandleLookWhileStunned();
        ApplyStunPose();

        // 水平の勢いは徐々に弱まる（地面についたら強く）
        float drag = controller.isGrounded ? 6f : 0.8f;
        knockVelocity *= Mathf.Exp(-drag * dt);

        velocity.y += gravity * dt;
        controller.Move((knockVelocity + Vector3.up * velocity.y) * dt);
        CheckLanding();

        knockTimer -= dt;
        if (knockAge > KnockMinSeconds && controller.isGrounded)
        {
            // 着地：少しふらつく
            knockTimer = 0f;
            velocity.y = -2f;
            stunTimer = Mathf.Max(stunTimer, knockRecoveryStun);
        }
    }

    /// <summary>落下・瞬間移動のあとに、落ちる勢いや吹き飛びを消す</summary>
    public void ResetMotion()
    {
        velocity = Vector3.zero;
        knockVelocity = Vector3.zero;
        knockTimer = 0f;
    }

    public void Stun(float seconds)
    {
        stunTimer = Mathf.Max(stunTimer, seconds);
        moveInput = Vector2.zero;
    }

    // スタン中の動き：入力は受け付けず、重力だけが効く。しゃがみの姿勢で、画角が狭まる
    private void UpdateStunned()
    {
        stunTimer -= Time.deltaTime;
        moveInput = Vector2.zero;
        HandleLookWhileStunned();
        ApplyStunPose();

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
        CheckLanding();
    }

    // しゃがみの状態が決まったあとで、懐中電灯を点けたり消したりする
    private void LateUpdate()
    {
        if (flashlight == null)
        {
            if (cameraTransform != null) flashlight = cameraTransform.GetComponentInChildren<Light>(true);
            if (flashlight == null) return;
        }
        if (flashlightBaseIntensity < 0f)
        {
            flashlightBaseIntensity = flashlight.intensity;
            flashlightBaseRange = flashlight.range;
            flashlightBaseAngle = flashlight.spotAngle;
            flashlightBaseInnerAngle = flashlight.innerSpotAngle;
        }
        ApplyFlashlightSettings(flashlight, ViewedHeldItem());

        bool on;
        if (isSpectating)
        {
            // 観戦中は、見ている相手のライトと同じ状態にする（同じ映像になる）
            on = true;
            long target = RemotePlayerSync.SpectatedClientId;
            if (target >= 0 && RemotePlayerSync.Instance != null &&
                RemotePlayerSync.Instance.TryGetAvatarLight((ulong)target, out bool targetOn)) on = targetOn;
        }
        else
        {
            // しゃがんでいる間は消す。スタン・吹き飛び中の「しゃがみと同じ姿勢」では消さない
            on = !(IsCrouching && !IsStunned);
        }
        if (flashlight.enabled != on) flashlight.enabled = on;
    }

    private void Update()
    {
        // ポーズメニュー表示中・観戦中は操作を受け付けない
        // 転がっている間も、視点はマウスで自由に動かせる（体だけが動く。カメラの位置と向きは PlayerTumble が決める）
        if (isTumbling && !PauseMenu.IsOpen && !isSpectating)
        {
            HandleLookWhileStunned();
            return;
        }

        if (PauseMenu.IsOpen || isSpectating || isTumbling) return;

        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        if (knockTimer > 0f && !isControlLocked)
        {
            UpdateKnockedBack();
            return;
        }

        if (stunTimer > 0f && !isControlLocked)
        {
            UpdateStunned();
            return;
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
            Vector2 lookDelta = ReadLookDegrees();
            float mouseX = lookDelta.x;
            float mouseY = lookDelta.y;

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
        IsCrouching = isCrouching;

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
            // 正気度が低いと、さらに消費が増える
            if (playerController != null) weightMultiplier *= playerController.StaminaDrainMultiplier;

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

        // 足音：敵が聞き取るのは走る音だけ（歩きとしゃがみ歩きは無音。大きさは EnemyNoise で決める）
        footstepTimer -= Time.deltaTime;
        if (footstepTimer <= 0f && isMoving && isGrounded)
        {
            footstepTimer = 0.35f;
            EnemyNoise.Emit(transform.position, isSprinting ? EnemyNoise.Sprint : (isCrouching ? EnemyNoise.Crouch : EnemyNoise.Walk));
        }

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
        Vector3 before = transform.position;
        controller.Move(moveDir * currentSpeed * Time.deltaTime);
        UpdateStepSound(transform.position - before, isGrounded && isMoving, isSprinting, isCrouching);

        if (jumpAction != null && jumpAction.triggered && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            GameplaySync.NotifyNoise(transform.position, EnemyNoise.Jump); // 踏み切る音（敵に聞こえる）
            GameAudio.Play(GameSound.Jump, 0.6f);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
        CheckLanding();
    }

    // ---- 自分の足音（耳で聞く音。敵に聞こえる大きさは EnemyNoise で別に決める）----
    private float stepTravelled = 1.2f; // 歩き出してすぐ、1歩目が鳴る

    private void UpdateStepSound(Vector3 moved, bool walking, bool sprinting, bool crouching)
    {
        if (!walking)
        {
            stepTravelled = 1.2f;
            return;
        }
        moved.y = 0f;
        stepTravelled += moved.magnitude; // 壁に当たって進めないときは、鳴らない
        float stride = sprinting ? 2.4f : (crouching ? 1.5f : 2f);
        if (stepTravelled < stride) return;
        stepTravelled = 0f;
        GameAudio.PlayStep(sprinting ? 0.7f : (crouching ? 0.15f : 0.42f));
    }

    // ---- 着地の音（高い所から落ちるほど大きく、遠くの敵まで聞こえる）----
    private const float LandNoiseMinSpeed = 5.5f;  // これより遅い着地（小さな段差）は無音。ジャンプの着地は約 7
    private const float LandNoiseMaxSpeed = 16f;   // この速さで、音が最大になる
    private const float LandNoiseGrace = 1.5f;     // ステージ開始などで出現した直後の着地は、数えない
    private bool wasAirborne;
    private float noLandNoiseUntil;

    /// <summary>移動（controller.Move）のあとに呼ぶ。空中から床に着いた瞬間に、落ちる速さに応じた音を出す</summary>
    private void CheckLanding()
    {
        if (!controller.isGrounded)
        {
            wasAirborne = true;
            return;
        }
        if (!wasAirborne) return;
        wasAirborne = false;

        float impact = -velocity.y;
        if (Time.time < noLandNoiseUntil || Time.timeSinceLevelLoad < LandNoiseGrace) return;
        if (impact >= 3f) GameAudio.Play(GameSound.Land, Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(3f, LandNoiseMaxSpeed, impact)));
        if (impact < LandNoiseMinSpeed) return;

        float k = Mathf.InverseLerp(LandNoiseMinSpeed, LandNoiseMaxSpeed, impact);
        GameplaySync.NotifyNoise(transform.position, Mathf.Lerp(EnemyNoise.LandMin, EnemyNoise.LandMax, k));
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