using UnityEngine;

/// <summary>
/// 吹き飛ばされたときの動き（R.E.P.O. のタンブル）。
/// 爆風などで吹き飛ばされると、体が物理で動くボールになって、跳ねて転がる。体は操作できないが、視点（カメラの向き）はマウスで自由に動かせる。
/// 勢いがなくなって止まったら、起き上がって（視点がゆっくり立ち直る）、操作が戻る。
/// PlayerMovement.Launch から呼ばれる（爆風・敵の攻撃など、吹き飛ばしはすべてここを通る）。
/// ・体は別の Rigidbody（球）。プレイヤー本体（CharacterController）は、この間その位置に追従させる
/// ・壁や床に強くぶつかると、画面が揺れる
/// </summary>
[DefaultExecutionOrder(-500)] // HitFeedback（0）より先に視点を決める。ぶつかったときの揺れは、その上に足される
public class PlayerTumble : MonoBehaviour
{
    [Header("吹き飛び")]
    [Tooltip("爆風などの吹き飛ばしの速さにかける倍率（大きいほど遠くまで飛ぶ）")] public float launchScale = 1.4f;
    [Tooltip("上向きの速さの最低値（m/s）。地面に擦らず、ふわっと浮く")] public float minUpSpeed = 3.5f;
    [Tooltip("回転の速さ（rad/s）。大きいほど、ぐるぐる回る")] public float spinSpeed = 9f;

    [Header("体（物理）")]
    public float radius = 0.4f;
    public float mass = 1f;
    [Range(0f, 1f), Tooltip("跳ねやすさ")] public float bounciness = 0.45f;
    [Range(0f, 1f), Tooltip("転がるときの摩擦")] public float friction = 0.6f;
    [Tooltip("空気抵抗")] public float linearDamping = 0.15f;
    [Tooltip("回転の減衰")] public float angularDamping = 0.5f;

    [Header("止まるまで・起き上がり")]
    [Tooltip("最低でもこの時間は転がる（秒）")] public float minTumbleSeconds = 0.9f;
    [Tooltip("最大でもこの時間で止める（秒）")] public float maxTumbleSeconds = 4.5f;
    [Tooltip("この速さ未満が続いたら、止まったとみなす（m/s）")] public float settleSpeed = 1.2f;
    [Tooltip("止まったとみなすまでの時間（秒）")] public float settleHold = 0.45f;
    [Tooltip("起き上がって、カメラの位置が立った高さへ戻るまでの時間（秒）")] public float standUpSeconds = 0.7f;

    [Header("ぶつかったときの画面の揺れ")]
    [Tooltip("この速さ以上の衝突で揺れる（m/s）")] public float impactMinSpeed = 4f;
    [Tooltip("この速さで、揺れが最大になる（m/s）")] public float impactMaxSpeed = 14f;

    private enum Phase { None, Tumble, StandUp }
    private Phase phase = Phase.None;

    private CharacterController cc;
    private PlayerMovement movement;
    private Camera cam;
    private Rigidbody body;
    private SphereCollider bodyCollider;

    private float timer;
    private float settleTimer;
    private float standTimer;
    private Vector3 normalCamLocalPos;
    private float normalPitch;
    private Vector3 standFromPos;
    private Quaternion standFromRot;
    private float lastImpactTime = -10f;

    public bool IsTumbling => phase != Phase.None;

    /// <summary>指定の速度で吹き飛ばして、転がり始める。すでに転がっている最中なら、その勢いを足す</summary>
    public void Begin(Vector3 launchVelocity)
    {
        if (phase == Phase.StandUp) FinishStandUp();

        Vector3 v = launchVelocity * launchScale;
        if (v.y < minUpSpeed) v.y = minUpSpeed;

        if (phase == Phase.Tumble)
        {
            body.linearVelocity += v;
            body.angularVelocity += Random.onUnitSphere * spinSpeed;
            timer = 0f;
            settleTimer = 0f;
            return;
        }

        cc = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        cam = Camera.main;
        if (cc == null || movement == null || cam == null) return;

        normalCamLocalPos = cam.transform.localPosition;
        normalPitch = NormalizeAngle(cam.transform.localEulerAngles.x);

        CreateBody(transform.TransformPoint(cc.center));
        body.linearVelocity = v;
        Debug.Log($"[タンブル] 開始 位置={body.position} 初速={v}");
        body.angularVelocity = Random.onUnitSphere * spinSpeed;

        movement.SetTumbling(true); // 操作できない（移動・視点・拾う）
        cc.enabled = false;         // プレイヤー本体は、体の位置に追従させる

        timer = 0f;
        settleTimer = 0f;
        phase = Phase.Tumble;
    }

    private void CreateBody(Vector3 center)
    {
        GameObject go = new GameObject("PlayerTumbleBody");
        go.transform.position = center;
        go.transform.rotation = Quaternion.identity;

        bodyCollider = go.AddComponent<SphereCollider>();
        bodyCollider.radius = radius;
        PhysicsMaterial mat = new PhysicsMaterial("TumbleBody")
        {
            bounciness = bounciness,
            dynamicFriction = friction,
            staticFriction = friction,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };
        bodyCollider.material = mat;

        body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.maxDepenetrationVelocity = 3f;
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        go.AddComponent<TumbleBodyProxy>().owner = this;
    }

    // 異常な加速（壁にめり込んだときの押し出しなど）を抑える
    private const float MaxBodySpeed = 20f;

    private void FixedUpdate()
    {
        if (phase != Phase.Tumble || body == null) return;

        Vector3 v = body.linearVelocity;
        if (v.magnitude > MaxBodySpeed) body.linearVelocity = v.normalized * MaxBodySpeed;
    }

    /// <summary>体の下に床があるか（空中かどうか）</summary>
    private bool IsBodyGrounded()
    {
        int itemsLayer = LayerMask.NameToLayer("Items");
        int mask = ~((1 << 2) | (itemsLayer >= 0 ? 1 << itemsLayer : 0));
        return Physics.Raycast(body.position, Vector3.down, radius + 0.3f, mask, QueryTriggerInteraction.Ignore);
    }

    private void Update()
    {
        if (phase != Phase.Tumble || body == null) return;

        float dt = Time.deltaTime;
        timer += dt;

        bool slow = body.linearVelocity.magnitude < settleSpeed && body.angularVelocity.magnitude < settleSpeed * 3f;
        settleTimer = slow ? settleTimer + dt : 0f;

        // 空中では解除しない（床に着いて、止まったときだけ起き上がる）
        bool grounded = IsBodyGrounded();
        if (!grounded) settleTimer = 0f;
        if (grounded && (timer >= maxTumbleSeconds || (timer >= minTumbleSeconds && settleTimer >= settleHold)))
            BeginStandUp();
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        if (phase == Phase.Tumble && body != null)
        {
            // プレイヤー本体を、体の位置へ。カメラは体の位置に付くが、向きはマウスで自由（体が回っても、視点は回らない）
            transform.position = body.position - Vector3.up * cc.center.y;
            cam.transform.SetPositionAndRotation(
                body.position + Vector3.up * 0.1f,
                transform.rotation * Quaternion.Euler(movement.CameraPitch, 0f, 0f));
            return;
        }

        if (phase == Phase.StandUp)
        {
            standTimer += Time.deltaTime;
            float k = Mathf.Clamp01(standTimer / Mathf.Max(0.05f, standUpSeconds));
            float s = k * k * (3f - 2f * k);

            Vector3 targetPos = transform.TransformPoint(normalCamLocalPos);
            Quaternion targetRot = transform.rotation * Quaternion.Euler(movement.CameraPitch, 0f, 0f);
            cam.transform.SetPositionAndRotation(
                Vector3.Lerp(standFromPos, targetPos, s),
                Quaternion.Slerp(standFromRot, targetRot, s));

            if (k >= 1f) FinishStandUp();
        }
    }

    /// <summary>止まった：地面の上に立たせて、視点をゆっくり立て直す</summary>
    private void BeginStandUp()
    {
        Vector3 center = body.position;
        Vector3 feet = center - Vector3.up * cc.center.y;
        int mask = ~((1 << 2) | (LayerMask.NameToLayer("Items") >= 0 ? 1 << LayerMask.NameToLayer("Items") : 0));
        if (Physics.Raycast(center + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 3f, mask, QueryTriggerInteraction.Ignore))
            feet = hit.point + Vector3.up * 0.05f;

        // 向きは、いまカメラが向いている方角（水平）にそろえる（視点はマウスで動かしていたので、そのまま）
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;

        standFromPos = cam.transform.position;
        standFromRot = cam.transform.rotation;

        DestroyBody();
        transform.position = feet;
        transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        cc.enabled = true;

        standTimer = 0f;
        phase = Phase.StandUp;
    }

    private void FinishStandUp()
    {
        phase = Phase.None;
        if (cam != null)
        {
            cam.transform.localPosition = normalCamLocalPos;
            cam.transform.localRotation = Quaternion.Euler(movement != null ? movement.CameraPitch : normalPitch, 0f, 0f);
        }
        if (movement != null)
        {
            movement.ResetMotion();
            movement.SetTumbling(false);
        }
    }

    private void DestroyBody()
    {
        if (body != null) Destroy(body.gameObject);
        body = null;
        bodyCollider = null;
    }

    private void OnDestroy()
    {
        DestroyBody();
    }

    /// <summary>体が壁や床にぶつかった（TumbleBodyProxy から）。強くぶつかると画面が揺れる</summary>
    public void OnBodyImpact(float relativeSpeed)
    {
        if (relativeSpeed < impactMinSpeed || Time.time - lastImpactTime < 0.12f) return;
        lastImpactTime = Time.time;
        float k = Mathf.Clamp01((relativeSpeed - impactMinSpeed) / Mathf.Max(0.1f, impactMaxSpeed - impactMinSpeed));
        HitFeedback hit = FindAnyObjectByType<HitFeedback>();
        if (hit != null) hit.Impact(k);

        // 吹き飛ばされて床や壁にぶつかる音も、敵に聞こえる
        if (body != null) GameplaySync.NotifyNoise(body.position, Mathf.Lerp(EnemyNoise.LandMin, EnemyNoise.LandMax, k));
    }

    private static float NormalizeAngle(float a)
    {
        while (a > 180f) a -= 360f;
        while (a < -180f) a += 360f;
        return a;
    }
}

/// <summary>転がる体の衝突を、PlayerTumble へ伝える</summary>
public class TumbleBodyProxy : MonoBehaviour
{
    public PlayerTumble owner;

    private void OnCollisionEnter(Collision collision)
    {
        if (owner != null) owner.OnBodyImpact(collision.relativeVelocity.magnitude);
    }
}
