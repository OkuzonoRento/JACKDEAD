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

    private Vector3 launchPosition;  // 吹き飛ぶ前に立っていた位置（立てる場所が見つからないときに戻る）
    private Vector3 clearCenter;     // 壁をすり抜けていないと分かっている、最後の体の位置
    private float stuckTimer;        // 空中で止まったまま（何かに挟まった）の時間
    private const float StuckSeconds = 1.5f;

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

        launchPosition = transform.position;
        clearCenter = transform.TransformPoint(cc.center);
        stuckTimer = 0f;
        CreateBody(clearCenter);
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

        // 体の中心が、前の位置との間にある壁や床を通り抜けていたら、通り抜ける前の位置へ戻す
        if (IsBlocked(clearCenter, body.position))
        {
            body.position = clearCenter;
            body.linearVelocity = -v * 0.2f;
        }
        else
        {
            clearCenter = body.position;
        }
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
        // ただし、床に着かないまま止まっている（壁や物に挟まった）ときは、抜け出させる。落下中は速いので、ここには入らない
        stuckTimer = !grounded && slow ? stuckTimer + dt : 0f;
        if (stuckTimer >= StuckSeconds ||
            (grounded && (timer >= maxTumbleSeconds || (timer >= minTumbleSeconds && settleTimer >= settleHold))))
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
        // 体の当たり判定は、立つ場所を調べる前に消す（消えるのは次のフレームなので、先に無効にする）
        bodyCollider.enabled = false;
        cc.height = movement.StandingHeight;
        Vector3 standAt = FindStandPosition(clearCenter);

        // 向きは、いまカメラが向いている方角（水平）にそろえる（視点はマウスで動かしていたので、そのまま）
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;

        standFromPos = cam.transform.position;
        standFromRot = cam.transform.rotation;

        DestroyBody();
        transform.position = standAt;
        transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        cc.enabled = true;

        standTimer = 0f;
        phase = Phase.StandUp;
    }

    // 立てる場所を探すときの、まわりへずらす距離（m）と向きの数
    private static readonly float[] SearchRadii = { 0f, 0.25f, 0.5f, 0.8f, 1.2f, 1.7f, 2.4f };
    private const int SearchDirections = 16;

    /// <summary>
    /// 体が止まった位置の近くで、壁や床にめり込まずに立てる場所を返す（返すのはプレイヤー本体の位置）。
    /// 壁の向こう側は選ばない。見つからなければ、吹き飛ぶ前の位置に戻る
    /// </summary>
    private Vector3 FindStandPosition(Vector3 from)
    {
        float half = Mathf.Max(cc.height * 0.5f, cc.radius);
        int mask = SolidMask();

        foreach (float distance in SearchRadii)
        {
            int count = distance <= 0f ? 1 : SearchDirections;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count;
                Vector3 probe = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (IsBlocked(from, probe)) continue;

                // 真下の床（急な面は除く）。足元の少し上に、本体の中心が来る
                if (!Physics.Raycast(probe + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.6f) continue;

                Vector3 position = hit.point + Vector3.up * (half + cc.skinWidth + 0.02f) - cc.center;
                if (IsFree(position) && !IsBlocked(from, position + cc.center)) return position;
            }
        }

        Debug.LogWarning($"[タンブル] 近くに立てる場所がないので、吹き飛ぶ前の位置へ戻します（止まった位置 {from}）。");
        if (!IsFree(launchPosition))
        {
            MapGenerator map = FindAnyObjectByType<MapGenerator>();
            if (map != null && map.HasPlayerSpawnPoint) return map.PlayerSpawnPoint;
        }
        return launchPosition;
    }

    /// <summary>この位置に本体を置いて、何にも重ならないか</summary>
    private bool IsFree(Vector3 position)
    {
        float half = Mathf.Max(cc.height * 0.5f, cc.radius);
        Vector3 middle = position + cc.center;
        float reach = half - cc.radius;
        Collider[] hits = Physics.OverlapCapsule(middle - Vector3.up * reach, middle + Vector3.up * reach,
            cc.radius, SolidMask(), QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (IsSolid(c)) return false;
        }
        return true;
    }

    /// <summary>2 点の間に壁や床があるか</summary>
    private static bool IsBlocked(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.001f) return false;

        foreach (RaycastHit hit in Physics.RaycastAll(from, delta / length, length, SolidMask(), QueryTriggerInteraction.Ignore))
        {
            if (IsSolid(hit.collider)) return true;
        }
        return false;
    }

    // 他のプレイヤー（Ignore Raycast レイヤー）と落ちているアイテムは、壁とは見なさない
    private static int SolidMask()
    {
        int items = LayerMask.NameToLayer("Items");
        return ~((1 << 2) | (items >= 0 ? 1 << items : 0));
    }

    /// <summary>動かない地形か（自分の体、敵、アイテムは除く）</summary>
    private static bool IsSolid(Collider c)
    {
        return c.GetComponentInParent<PlayerController>() == null
            && c.GetComponentInParent<TumbleBodyProxy>() == null
            && c.GetComponentInParent<EnemyBrain>() == null
            && c.GetComponentInParent<Item>() == null;
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

    /// <summary>
    /// 倒れた・復活したときに、転がりをその場でやめる（体を消して、カメラと当たり判定を元に戻す）。
    /// やめないと、マップの外へ落ち続ける体にプレイヤー本体が引っ張られ、次のステージで復活した直後に落下死する
    /// </summary>
    public void Cancel()
    {
        if (phase == Phase.None) return;

        DestroyBody();
        if (cc != null) cc.enabled = true;
        FinishStandUp();
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
