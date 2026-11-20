using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Box を投げて攻撃してくる敵（Enemy_01）。REPO の敵のように、普段はマップをうろつき、
/// プレイヤーを見つける（視界内、または近くで物音に気づく）と追いかけ、
/// 投げられる距離まで来たら Box（時限爆弾）を投げてくる。見失うと最後に見た場所を調べてから、また徘徊に戻る。
///
/// マルチプレイ：判断・移動・攻撃はホストだけが行い、位置と状態を全員へ配る。
/// 参加者側の敵は見た目だけ（同じ位置に出して、届いた位置へ滑らかに動かす）。
/// 投げた Box はホストが出して GameplaySync で全員に出す（プレイヤーが投げた物と同じ仕組み）。
/// </summary>
public class EnemyBrain : MonoBehaviour
{
    private enum AnimState { Idle = 0, Walk = 1, Run = 2, Throw = 3 }

    [Header("移動")]
    [SerializeField] private float patrolSpeed = 2.0f;
    [SerializeField] private float chaseSpeed = 4.5f;
    [SerializeField, Tooltip("向きを変える速さ（度/秒）")] private float turnSpeed = 300f;
    [SerializeField, Tooltip("徘徊の目的地を探す範囲（m）")] private float patrolRadius = 25f;
    [SerializeField] private Vector2 patrolWaitRange = new Vector2(1f, 3f);

    [Header("索敵")]
    [SerializeField, Tooltip("この距離まで見える")] private float sightRange = 22f;
    [SerializeField, Tooltip("視界の左右の広さ（度）。正面からこの角度まで見える")] private float sightHalfAngle = 70f;
    [SerializeField, Tooltip("この距離以内なら、視界の外・壁の向こうでも気づく")] private float hearRange = 6f;
    [SerializeField] private float eyeHeight = 1.6f;
    [SerializeField, Tooltip("見失ってから、追うのをやめるまでの時間（秒）")] private float loseSightTime = 6f;

    [Header("攻撃")]
    [SerializeField, Tooltip("これより近いと距離を取る")] private float attackMinRange = 5f;
    [SerializeField, Tooltip("これより遠いと近づく。この範囲の間で投げる")] private float attackMaxRange = 15f;
    [SerializeField] private float throwCooldown = 4f;
    [SerializeField, Tooltip("構えてから投げるまでの時間（秒）。この間にプレイヤーは動ける")] private float throwWindup = 0.7f;
    [SerializeField, Tooltip("Box を投げる速さ（m/s）")] private float throwSpeed = 16f;
    [SerializeField, Tooltip("10m先でどれだけ狙いがばらけるか（m）")] private float aimSpread = 0.8f;
    [SerializeField] private float handHeight = 1.7f;

    [Header("体")]
    [SerializeField, Tooltip("Box の爆発ダメージを受けない（Enemy_01 は無効。他の種類の敵は通常どおり受ける）")]
    private bool immuneToBombs = true;
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField] private float colliderRadius = 0.45f;
    [SerializeField] private float colliderHeight = 1.9f;

    [Header("投げ腕の動き（構えで腕を振りかぶり、投げで振り下ろす）")]
    [SerializeField, Tooltip("振りかぶったときの腕の角度（度）")] private float windupArmAngle = 140f;
    [SerializeField, Tooltip("投げ終わりに腕が前へ振り抜ける角度（度）")] private float followArmAngle = -70f;

    [Header("投げる Box")]
    public GameObject boxPrefab;
    public ItemDataAsset boxItemData;

    [Header("アニメーション（未設定の動きは再生しない）")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;
    public AnimationClip throwClip;

    public int NetId { get; private set; }

    private static readonly Dictionary<int, EnemyBrain> Registry = new Dictionary<int, EnemyBrain>();

    private const float PoseSendInterval = 0.1f;
    private const float AnimBlendSpeed = 8f;

    private bool isPuppet;
    private NavMeshAgent agent;
    private Animator animator;

    // AI の状態
    private bool noticed;
    private Vector3 lastKnownPos;
    private float loseTimer;
    private float cooldownTimer;
    private float windupTimer;      // > 0 のあいだ構えている
    private bool winding;
    private float patrolWaitTimer;
    private bool hasPatrolDest;
    private Vector3 visibleTargetPos;
    private bool targetVisible;
    private float poseTimer;

    // 見た目
    private AnimState animState = AnimState.Idle;
    private Vector3 puppetTargetPos;
    private float puppetTargetYaw;
    private bool puppetInitialized;

    // アニメーション再生
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] clipPlayables;
    private AnimationClip[] clipList;
    private float[] weights;

    private readonly List<ulong> remoteIds = new List<ulong>();
    private PlayerController localPlayer;
    private CharacterController localController;

    // ------------------------------------------------------------------

    private const int DeadState = 9; // 位置の通知で「倒された」を伝える

    private float health;

    /// <summary>爆発の範囲内にいる敵へダメージを与える（距離で弱まる）。判定するのはホスト（敵を動かしている側）だけ</summary>
    public static void DamageEnemiesInRadius(Vector3 center, float radius, float maxDamage)
    {
        List<EnemyBrain> targets = new List<EnemyBrain>(Registry.Values);
        foreach (EnemyBrain enemy in targets)
        {
            if (enemy == null || enemy.isPuppet || enemy.immuneToBombs) continue;

            Vector3 body = enemy.transform.position + Vector3.up * (enemy.colliderHeight * 0.5f);
            float dist = Vector3.Distance(center, body);
            if (dist > radius) continue;

            float falloff = 1f - Mathf.Clamp01(dist / radius);
            enemy.TakeBombDamage(maxDamage * Mathf.Lerp(0.25f, 1f, falloff));
        }
    }

    private void TakeBombDamage(float amount)
    {
        if (immuneToBombs || isPuppet) return;
        health -= amount;
        if (health > 0f) return;

        // 倒された：全員の画面から消す
        GameplaySync.NotifyEnemyPose(NetId, transform.position, transform.eulerAngles.y, DeadState);
        Destroy(gameObject);
    }

    public static void ApplyRemotePose(int netId, Vector3 position, float yaw, int state)
    {
        if (!Registry.TryGetValue(netId, out EnemyBrain enemy) || enemy == null || !enemy.isPuppet) return;

        if (state == DeadState)
        {
            Destroy(enemy.gameObject);
            return;
        }

        enemy.puppetTargetPos = position;
        enemy.puppetTargetYaw = yaw;
        enemy.animState = (AnimState)Mathf.Clamp(state, 0, 3);
        if (!enemy.puppetInitialized)
        {
            enemy.puppetInitialized = true;
            enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }
    }

    /// <summary>MapGenerator が出現させたあとに呼ぶ。全員同じ順で作られるので、同じ敵が同じIDになる</summary>
    public void Init(int netId)
    {
        NetId = netId;
        Registry[netId] = this;
    }

    private void Awake()
    {
        NetworkSession net = NetworkSession.Instance;
        isPuppet = net != null && net.IsOnline && !net.IsHost;

        health = maxHealth;

        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;

        // 当たり判定：プレイヤーや投げられた Box とぶつかる（参加者側の見た目だけの敵にも付ける）
        if (GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
            col.radius = colliderRadius;
            col.height = colliderHeight;
            col.center = new Vector3(0f, colliderHeight * 0.5f, 0f);
        }

        if (!isPuppet)
        {
            agent = GetComponent<NavMeshAgent>();
            if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
            agent.speed = patrolSpeed;
            agent.angularSpeed = 0f;       // 向きは自前で回す
            agent.updateRotation = false;
            agent.acceleration = 14f;
            agent.radius = 0.4f;
            agent.height = 1.9f;
            agent.stoppingDistance = 0.3f;
            agent.autoBraking = true;
        }
    }

    private void Start()
    {
        if (animator != null && animator.isHuman) armBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        BuildAnimationGraph();
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (Registry.TryGetValue(NetId, out EnemyBrain e) && e == this) Registry.Remove(NetId);
    }

    // ------------------------------------------------------------------
    // 更新
    // ------------------------------------------------------------------

    private void Update()
    {
        if (isPuppet)
        {
            UpdatePuppet();
        }
        else
        {
            UpdateAI();
            SendPose();
        }
        UpdateAnimation();
    }

    private void UpdatePuppet()
    {
        if (!puppetInitialized) return;
        float t = 1f - Mathf.Exp(-12f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, puppetTargetPos, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, puppetTargetYaw, 0f), t);
    }

    private void SendPose()
    {
        poseTimer += Time.unscaledDeltaTime;
        if (poseTimer < PoseSendInterval) return;
        poseTimer = 0f;
        GameplaySync.NotifyEnemyPose(NetId, transform.position, transform.eulerAngles.y, (int)animState);
    }

    // ------------------------------------------------------------------
    // AI（ホスト／ソロ）
    // ------------------------------------------------------------------

    private float stunTimer;

    /// <summary>Box が当たってスタンする（その場で動けなくなり、構えも中断される）。判定するのはホストだけ</summary>
    public void Stun(float seconds)
    {
        if (isPuppet) return;
        stunTimer = Mathf.Max(stunTimer, seconds);
        winding = false;
    }

    private void UpdateAI()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        float dt = Time.deltaTime;
        cooldownTimer -= dt;

        if (stunTimer > 0f)
        {
            stunTimer -= dt;
            agent.isStopped = true;
            winding = false;
            animState = AnimState.Idle;
            return;
        }

        FindVisibleTarget();

        if (targetVisible)
        {
            noticed = true;
            lastKnownPos = visibleTargetPos;
            loseTimer = 0f;
            UpdateEngage(dt);
        }
        else
        {
            CancelWindup();
            if (noticed)
            {
                UpdateInvestigate(dt);
            }
            else
            {
                UpdatePatrol(dt);
            }
        }

        UpdateFacing(dt);
        animState = ChooseAnimState();
    }

    private void UpdateEngage(float dt)
    {
        Vector3 toTarget = visibleTargetPos - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        // 構え中：その場で狙い、時間が来たら投げる
        if (winding)
        {
            agent.isStopped = true;
            windupTimer -= dt;
            if (windupTimer <= 0f)
            {
                ThrowBox();
                winding = false;
                cooldownTimer = throwCooldown;
            }
            return;
        }

        if (dist > attackMaxRange)
        {
            // 遠い：近づく
            agent.isStopped = false;
            agent.speed = chaseSpeed;
            agent.SetDestination(visibleTargetPos);
        }
        else if (dist < attackMinRange)
        {
            // 近すぎる：距離を取りながら投げる機会をうかがう
            Vector3 away = -toTarget.normalized * 4f;
            if (NavMesh.SamplePosition(transform.position + away, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                agent.SetDestination(hit.position);
            }
            else
            {
                agent.isStopped = true;
            }
            TryBeginWindup();
        }
        else
        {
            // ちょうどよい距離：立ち止まって狙う
            agent.isStopped = true;
            TryBeginWindup();
        }
    }

    private void TryBeginWindup()
    {
        if (winding || cooldownTimer > 0f) return;
        winding = true;
        windupTimer = throwWindup;
        agent.isStopped = true;
    }

    private void CancelWindup()
    {
        winding = false;
    }

    private void UpdateInvestigate(float dt)
    {
        loseTimer += dt;
        agent.isStopped = false;
        agent.speed = chaseSpeed;
        agent.SetDestination(lastKnownPos);

        if (loseTimer > loseSightTime)
        {
            noticed = false;
            hasPatrolDest = false;
            patrolWaitTimer = Random.Range(patrolWaitRange.x, patrolWaitRange.y);
        }
    }

    private void UpdatePatrol(float dt)
    {
        agent.speed = patrolSpeed;

        if (hasPatrolDest)
        {
            agent.isStopped = false;
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f)
            {
                hasPatrolDest = false;
                patrolWaitTimer = Random.Range(patrolWaitRange.x, patrolWaitRange.y);
            }
            return;
        }

        agent.isStopped = true;
        patrolWaitTimer -= dt;
        if (patrolWaitTimer > 0f) return;

        Vector3 randomPoint = transform.position + Random.insideUnitSphere * patrolRadius;
        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
            hasPatrolDest = true;
        }
        else
        {
            patrolWaitTimer = 1f;
        }
    }

    // 向きを、歩いているときは進行方向へ、狙っているときは相手へ向ける
    private void UpdateFacing(float dt)
    {
        Vector3 look = Vector3.zero;
        if (targetVisible && (winding || agent.isStopped))
        {
            look = visibleTargetPos - transform.position;
        }
        else if (agent.desiredVelocity.sqrMagnitude > 0.05f)
        {
            look = agent.desiredVelocity;
        }
        else if (targetVisible)
        {
            look = visibleTargetPos - transform.position;
        }

        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
    }

    private AnimState ChooseAnimState()
    {
        if (winding) return AnimState.Throw;

        float speed = agent.velocity.magnitude;
        if (speed < 0.15f) return AnimState.Idle;
        return speed > patrolSpeed + 0.7f ? AnimState.Run : AnimState.Walk;
    }

    // ------------------------------------------------------------------
    // 索敵
    // ------------------------------------------------------------------

    private void FindVisibleTarget()
    {
        targetVisible = false;
        float bestDist = float.MaxValue;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        // ローカル（ホスト自身）のプレイヤー
        if (localPlayer == null)
        {
            localPlayer = FindAnyObjectByType<PlayerController>();
            if (localPlayer != null) localController = localPlayer.GetComponent<CharacterController>();
        }
        if (localPlayer != null && !localPlayer.IsDown)
        {
            Vector3 chest = localController != null ? localController.bounds.center : localPlayer.transform.position;
            ConsiderTarget(eye, chest, ref bestDist);
        }

        // 他のプレイヤー（アバターの位置から）
        if (RemotePlayerSync.Instance != null)
        {
            RemotePlayerSync.Instance.GetAliveRemoteIds(remoteIds);
            foreach (ulong id in remoteIds)
            {
                if (!RemotePlayerSync.Instance.TryGetAvatarView(id, out Vector3 eyePos, out _, out _)) continue;
                ConsiderTarget(eye, eyePos - Vector3.up * 0.4f, ref bestDist);
            }
        }
    }

    private void ConsiderTarget(Vector3 eye, Vector3 chest, ref float bestDist)
    {
        Vector3 to = chest - eye;
        float dist = to.magnitude;
        if (dist >= bestDist || dist > sightRange) return;

        bool inHearing = dist <= hearRange;
        if (!inHearing)
        {
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (Vector3.Angle(transform.forward, flat) > sightHalfAngle) return;
        }

        // 壁などに遮られていないか（近くで気づく場合は、壁越しでも気づく）
        if (!inHearing)
        {
            int mask = ~((1 << LayerMask.NameToLayer("Items")) | (1 << 2));
            if (Physics.Linecast(eye, chest, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerController>() == null) return;
            }
        }

        bestDist = dist;
        targetVisible = true;
        visibleTargetPos = chest;
    }

    // ------------------------------------------------------------------
    // 攻撃
    // ------------------------------------------------------------------

    private void ThrowBox()
    {
        if (boxPrefab == null)
        {
            Debug.LogWarning("[ENEMY] boxPrefab が未設定のため Box を投げられません（JackDead/Setup Enemy を実行してください）。");
            return;
        }

        Vector3 origin = transform.position + Vector3.up * handHeight + transform.forward * 0.5f;

        // 狙いは遠いほどばらける
        Vector3 aim = visibleTargetPos;
        float dist = Vector3.Distance(origin, aim);
        aim += Random.insideUnitSphere * (aimSpread * dist / 10f);

        Vector3 velocity = ComputeThrowVelocity(origin, aim, throwSpeed);

        GameObject obj = Instantiate(boxPrefab, origin, Random.rotation);
        Item item = obj.GetComponent<Item>();
        if (item == null) item = obj.AddComponent<Item>();
        if (boxItemData != null) item.SetItemData(boxItemData);
        item.NetId = GameplaySync.AllocateDropId();

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 6f;

        BombBox bomb = obj.GetComponent<BombBox>();
        if (bomb != null) bomb.SetThrower(this);
        if (boxItemData != null)
        {
            GameplaySync.NotifyItemDropped(item, boxItemData, origin, velocity, bomb != null ? bomb.FuseTime : -1f);
        }
    }

    /// <summary>目標へ放物線で届く初速。時間は距離÷速さ（短すぎ・長すぎは抑える）</summary>
    private static Vector3 ComputeThrowVelocity(Vector3 from, Vector3 to, float speed)
    {
        Vector3 delta = to - from;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float t = Mathf.Clamp(flat.magnitude / Mathf.Max(1f, speed), 0.3f, 1.8f);
        float g = -Physics.gravity.y;

        Vector3 v = flat / t;
        v.y = (delta.y + 0.5f * g * t * t) / t;
        return v;
    }

    // ------------------------------------------------------------------
    // アニメーション（Animator のコントローラーを使わず、クリップを直接ブレンドする）
    // ------------------------------------------------------------------

    private void BuildAnimationGraph()
    {
        if (animator == null) return;
        clipList = new[] { idleClip, walkClip, runClip, throwClip };
        bool any = false;
        foreach (AnimationClip c in clipList) if (c != null) any = true;
        if (!any) return;

        graph = PlayableGraph.Create($"Enemy_{NetId}");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Anim", animator);

        mixer = AnimationMixerPlayable.Create(graph, clipList.Length);
        clipPlayables = new AnimationClipPlayable[clipList.Length];
        weights = new float[clipList.Length];
        for (int i = 0; i < clipList.Length; i++)
        {
            if (clipList[i] == null) continue;
            clipPlayables[i] = AnimationClipPlayable.Create(graph, clipList[i]);
            graph.Connect(clipPlayables[i], 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
        }
        output.SetSourcePlayable(mixer);
        weights[(int)AnimState.Idle] = 1f;
        graph.Play();
    }

    // ------------------------------------------------------------------
    // 投げる腕の動き（クリップを使わず、腕の骨を直接動かす）
    // 構え中：腕を後ろ上へ振りかぶる → 投げた瞬間：前へ振り抜く → 元の位置へ戻す
    // ------------------------------------------------------------------

    private Transform armBone;
    private float raise;          // 振りかぶりの進み具合 0〜1
    private float follow = -1f;   // 投げてからの経過（秒）。負なら投げていない
    private float armAngle;

    private const float FollowSwingTime = 0.2f;
    private const float FollowReturnTime = 0.5f;

    private void UpdateThrowArm(float dt)
    {
        if (animState == AnimState.Throw)
        {
            raise = Mathf.MoveTowards(raise, 1f, dt / Mathf.Max(0.1f, throwWindup));
            follow = -1f;
        }
        else if (raise > 0f && follow < 0f)
        {
            follow = 0f; // 構えが終わった＝投げた
        }

        float raised = windupArmAngle * Mathf.SmoothStep(0f, 1f, raise);
        if (follow < 0f)
        {
            armAngle = raised;
            return;
        }

        follow += dt;
        if (follow < FollowSwingTime)
        {
            armAngle = Mathf.Lerp(raised, followArmAngle, follow / FollowSwingTime);
        }
        else if (follow < FollowSwingTime + FollowReturnTime)
        {
            armAngle = Mathf.Lerp(followArmAngle, 0f, (follow - FollowSwingTime) / FollowReturnTime);
        }
        else
        {
            armAngle = 0f;
            raise = 0f;
            follow = -1f;
        }
    }

    // アニメーションの評価のあとに、腕の骨へ回転を上乗せする
    private void LateUpdate()
    {
        if (armBone == null || Mathf.Abs(armAngle) < 0.01f) return;
        armBone.rotation = Quaternion.AngleAxis(armAngle, transform.right) * armBone.rotation;
    }

    private void UpdateAnimation()
    {
        UpdateThrowArm(Time.deltaTime);

        if (!graph.IsValid()) return;

        // 構えのクリップが無いときは、体は待機のままにして腕だけ動かす
        int activeIndex = (int)animState;
        if (clipList[activeIndex] == null) activeIndex = (int)AnimState.Idle;

        float step = AnimBlendSpeed * Time.deltaTime;
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            if (clipList[i] == null) continue;
            float target = i == activeIndex ? 1f : 0f;
            weights[i] = Mathf.MoveTowards(weights[i], target, step);
            total += weights[i];
        }

        for (int i = 0; i < weights.Length; i++)
        {
            if (clipList[i] == null) continue;
            mixer.SetInputWeight(i, total > 0.0001f ? weights[i] / total : 0f);

            // 再生が終わりまで来たら先頭へ戻す（ループ）。構えのクリップは構えが始まるたびに頭から
            double length = clipList[i].length;
            double time = clipPlayables[i].GetTime();
            if (i == (int)AnimState.Throw)
            {
                if (animState != AnimState.Throw) clipPlayables[i].SetTime(0);
                else if (time > length) clipPlayables[i].SetTime(length);
            }
            else if (time > length && length > 0.01)
            {
                clipPlayables[i].SetTime(time % length);
            }
        }
    }
}
