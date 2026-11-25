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
    [SerializeField, Tooltip("視界の左右の広さ（度）。正面からこの角度まで見える（人間は片側およそ60度）")] private float sightHalfAngle = 60f;
    [SerializeField, Range(10f, 89f), Tooltip("視界の上下の広さ（度）。視線の向きから、上下この角度まで見える")] private float sightVerticalHalfAngle = 45f;
    [SerializeField, Range(-30f, 45f), Tooltip("視線を下へ向ける角度（度）")] private float sightDownTilt = 10f;
    [SerializeField, Tooltip("音のした場所を推測するときのずれ（音源までの距離に対する割合）。遠いほど正確に分からない")] private float noiseGuessError = 0.2f;
    [SerializeField, Tooltip("頭の骨が見つからないときの、目の高さ（m）")] private float eyeHeight = 1.6f;
    [SerializeField, Tooltip("物音を調べに歩いて向かう最大の時間（秒）。この時間で着けなければ、その場の周りを探す")] private float investigateMaxSeconds = 25f;

    [Header("気づくまで（REPO風。見えてから気づくまでに時間がかかる。しゃがむと気づかれにくい）")]
    [SerializeField, Tooltip("立っている相手に気づくまでの時間（秒）。x = すぐ気づく近さのすぐ外、y = 見える距離の端")] private Vector2 standingNoticeSeconds = new Vector2(0.9f, 2.8f);
    [SerializeField, Tooltip("しゃがんでいる相手に気づくまでの時間（秒）。x = 近く、y = 遠く")] private Vector2 crouchingNoticeSeconds = new Vector2(2f, 4.5f);
    [SerializeField, Tooltip("しゃがんでいる相手が見える距離（m）。これより遠いと、見えていても気づかない")] private float crouchSightRange = 11f;
    [SerializeField, Tooltip("立っている相手に、すぐ気づく近さ（m）")] private float instantNoticeRange = 3f;
    [SerializeField, Tooltip("しゃがんでいる相手に、すぐ気づく近さ（m）")] private float instantNoticeRangeCrouching = 1.5f;
    [SerializeField, Tooltip("見えなくなってから、気づきかけが消えるまでの時間（秒）")] private float awarenessFadeTime = 2f;
    [SerializeField, Range(0.05f, 0.95f), Tooltip("気づきかけがこの割合を超えると、立ち止まって相手の方を向く")] private float suspicionLevel = 0.35f;
    [SerializeField, Min(1f), Tooltip("頭・胴・腰のうち2か所だけ見えているとき、気づくまでの時間の倍率")] private float twoPartsTimeScale = 1.4f;
    [SerializeField, Min(1f), Tooltip("頭・胴・腰のうち1か所だけ見えているとき、気づくまでの時間の倍率")] private float onePartTimeScale = 2.2f;

    [Header("机や椅子の下に潜っている相手（しゃがんでいて、頭のすぐ上に物がある）")]
    [SerializeField, Tooltip("頭の上、この高さ（m）までに当たり判定があれば、潜っているとみなす")] private float coverCheckHeight = 0.7f;
    [SerializeField, Tooltip("潜っている相手が見える距離（m）")] private float hiddenSightRange = 6f;
    [SerializeField, Tooltip("潜っている相手に気づくまでの時間（秒）。x = 近く、y = 遠く")] private Vector2 hiddenNoticeSeconds = new Vector2(3.5f, 6f);
    [SerializeField, Tooltip("潜っている相手に、すぐ気づく近さ（m）")] private float instantNoticeRangeHidden = 0.8f;

    [Header("追跡の流れ")]
    [SerializeField, Tooltip("見失ってから、この時間（秒）以内に最後に見た場所へ着いたら、逃げた先を予測して走る")] private float chaseMemorySeconds = 3f;
    [SerializeField, Tooltip("逃げた先の予測で、最後に見た場所から進む距離（m）")] private float predictDistance = 9f;
    [SerializeField, Tooltip("予測した場所へ走る最大の時間（秒）")] private float predictMaxSeconds = 4f;
    [SerializeField, Tooltip("見失った場所の周りを、歩いて探す時間（秒）")] private float seekSeconds = 5f;
    [SerializeField, Tooltip("探し回る範囲（m）")] private float seekRadius = 6f;

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

    private const float PoseSendInterval = 0.05f;
    private const float AnimBlendSpeed = 8f;

    private bool isPuppet;
    private NavMeshAgent agent;
    private Animator animator;

    // AI の状態
    // 徘徊 →（物音）物音を調べる →（発見）追跡 → 逃げた先を予測して走る → 周りを歩いて探す → 徘徊
    private enum AiState { Roam, Investigate, Chase, Seek }

    private AiState state = AiState.Roam;
    private float stateTime;         // いまの状態になってからの時間（秒）
    private float lostTime;          // 相手が見えなくなってからの時間（秒）
    private ulong visibleTargetId;   // 見えている相手
    private ulong chaseTargetId;     // 追っている相手
    private Vector3 targetVelocity;  // 追っている相手の動き（見えている間に測る）
    private Vector3 trackedPos;
    private float trackTimer;
    private Vector3 chaseStartPos;   // 見失ったときに自分がいた場所
    private bool predicting;         // 逃げた先を予測して走っている最中
    private float predictTimer;
    private Vector3 seekPoint;
    private bool hasSeekPoint;
    private float seekPointTime;
    private const float VelocitySampleSeconds = 0.2f;
    private static readonly float[] PredictAngles = { 0f, -45f, 45f, -90f, 90f };
    private readonly HashSet<ulong> hiddenIds = new HashSet<ulong>(); // 机などの下に潜っているプレイヤー
    private Vector3 lastKnownPos;
    private float cooldownTimer;
    private float windupTimer;      // > 0 のあいだ構えている
    private bool winding;
    private float patrolWaitTimer;
    private bool hasPatrolDest;
    private Vector3 visibleTargetPos;
    private bool targetVisible;
    private float poseTimer;

    // プレイヤーごとの「気づきかけ」（0〜1。1 で気づく）。自分のPCのプレイヤーは LocalAwarenessKey
    private const ulong LocalAwarenessKey = ulong.MaxValue;
    private readonly Dictionary<ulong, float> awareness = new Dictionary<ulong, float>();
    private bool suspicious;          // 気づきかけている（まだ確信はない）
    private Vector3 suspectPos;
    private float suspectLevelNow;
    private PlayerMovement localMovement;

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

    /// <summary>
    /// 近くに、こちらに気づいて追ってきている敵がいるか（正気度の減りを速めるのに使う）。
    /// 敵を動かしているホストでは「気づいている」状態、参加者側の見た目だけの敵では「走る・投げる」動きで判断する
    /// </summary>
    public static bool IsChasedNear(Vector3 position, float radius)
    {
        foreach (EnemyBrain enemy in Registry.Values)
        {
            if (enemy == null) continue;
            if ((enemy.transform.position - position).sqrMagnitude > radius * radius) continue;

            bool chasing = enemy.isPuppet
                ? (enemy.animState == AnimState.Run || enemy.animState == AnimState.Throw)
                : (enemy.IsHunting && enemy.stunTimer <= 0f);
            if (chasing) return true;
        }
        return false;
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
        enemy.animState = (AnimState)Mathf.Clamp(state, 0, (int)AnimState.Throw);
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
        if (animator != null && animator.isHuman) headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        BuildAnimationGraph();
    }

    // ------------------------------------------------------------------
    // 聴覚：聞こえる範囲の音がしたら、音源の位置を推測して歩いて調べに行く
    // ------------------------------------------------------------------

    /// <summary>プレイヤーを追っている最中</summary>
    private bool IsHunting => state == AiState.Chase;

    private void OnEnable()
    {
        EnemyNoise.Heard += OnNoiseHeard;
    }

    private void OnDisable()
    {
        EnemyNoise.Heard -= OnNoiseHeard;
    }

    private void OnNoiseHeard(Vector3 source, float loudness)
    {
        // 判断するのはホスト（敵を動かしている側）だけ。すでにプレイヤーが見えているときは、そちらを優先
        if (isPuppet || agent == null || !agent.isOnNavMesh || targetVisible || stunTimer > 0f) return;

        float dist = Vector3.Distance(transform.position, source);

        // 壁を挟んでいても音は弱まらない（距離だけで聞こえるかどうかが決まる）
        if (dist > loudness) return;

        // 音源の正確な位置までは分からない：遠いほど、推測が大きくずれる
        Vector3 guess = source + Random.insideUnitSphere * (dist * noiseGuessError);
        guess.y = source.y;
        if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return;

        if (IsHunting) return; // プレイヤーを追っている最中は、そちらを優先

        // すでに別の音を調べているときは、より近い音を優先する
        if (state == AiState.Investigate
            && Vector3.Distance(transform.position, hit.position) > Vector3.Distance(transform.position, lastKnownPos)) return;

        lastKnownPos = hit.position;
        EnterState(AiState.Investigate);
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

        // 止まっていても、毎回送る（動きがなくても通信は変わらない。位置のずれを早く直せる）
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
        // スタンから起きたら、周りを探すところから
        if (IsHunting) EnterState(AiState.Seek);
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

        stateTime += dt;
        FindVisibleTarget();

        if (targetVisible)
        {
            TrackTarget(dt);
            lastKnownPos = visibleTargetPos;
            lostTime = 0f;
            if (state != AiState.Chase) EnterState(AiState.Chase); // 発見したら、すぐ追う
        }
        else
        {
            if (lostTime <= 0f) chaseStartPos = transform.position;
            lostTime += dt;
        }

        switch (state)
        {
            case AiState.Roam:
                // 気づきかけ：立ち止まらず、見えたあたりへ歩いて向かう
                if (suspicious) WalkToSuspect();
                else UpdatePatrol(dt);
                break;

            case AiState.Investigate:
                if (suspicious) lastKnownPos = suspectPos;
                MoveTo(lastKnownPos, patrolSpeed); // 走らず、歩いて調べに行く
                if (stateTime > investigateMaxSeconds || HasArrived()) EnterState(AiState.Seek);
                break;

            case AiState.Chase:
                UpdateChase(dt);
                break;

            case AiState.Seek:
                if (suspicious)
                {
                    // 探している最中の気づきかけ：見えたあたりへ歩いて向かう
                    seekPoint = suspectPos;
                    hasSeekPoint = true;
                    seekPointTime = 0f;
                }
                UpdateSeek(dt);
                break;
        }

        UpdateFacing(dt);
        animState = ChooseAnimState();
    }

    // 状態を切り替える
    private void EnterState(AiState next)
    {
        state = next;
        stateTime = 0f;
        winding = false;
        hasSeekPoint = false;
        hasPatrolDest = false;
        predicting = false;

        if (next == AiState.Roam)
        {
            patrolWaitTimer = Random.Range(patrolWaitRange.x, patrolWaitRange.y);
        }
    }

    private void MoveTo(Vector3 destination, float speed)
    {
        agent.isStopped = false;
        agent.speed = speed;
        agent.SetDestination(destination);
    }

    // 目的地に着いたか（目的地を出した直後は、まだ数えない）
    private bool HasArrived()
    {
        return stateTime > 0.4f && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
    }

    // 気づきかけた場所へ歩く。見えなくなっても、そこへ着くまでは歩き続ける（徘徊の目的地にする）
    private void WalkToSuspect()
    {
        if (!NavMesh.SamplePosition(suspectPos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return;
        MoveTo(hit.position, patrolSpeed);
        hasPatrolDest = true;
    }

    // 見えている相手の動き（向きと速さ）を覚えておく。見失ったあと、逃げた先を予測するのに使う
    private void TrackTarget(float dt)
    {
        if (visibleTargetId != chaseTargetId || lostTime > 0.3f)
        {
            // 相手が変わった／見失っていた直後は、測り直し
            chaseTargetId = visibleTargetId;
            targetVelocity = Vector3.zero;
            trackedPos = visibleTargetPos;
            trackTimer = 0f;
            return;
        }

        trackTimer += dt;
        if (trackTimer < VelocitySampleSeconds) return;

        Vector3 moved = visibleTargetPos - trackedPos;
        moved.y = 0f; // 見えている部位（頭・胴・腰）が変わっても、上下は数えない
        targetVelocity = Vector3.Lerp(targetVelocity, moved / trackTimer, 0.6f);
        trackedPos = visibleTargetPos;
        trackTimer = 0f;
    }

    private void UpdateChase(float dt)
    {
        if (targetVisible)
        {
            predicting = false;
            UpdateEngage(dt);
            return;
        }

        CancelWindup();

        if (!predicting)
        {
            // まず、最後に見た場所まで走る
            MoveTo(lastKnownPos, chaseSpeed);
            bool arrived = lostTime > 0.3f && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
            if (arrived && lostTime <= chaseMemorySeconds)
            {
                // 時間内に着いた：相手が逃げた先を予測して、そこへ走る
                lastKnownPos = PredictEscapePoint();
                predicting = true;
                predictTimer = 0f;
                MoveTo(lastKnownPos, chaseSpeed);
            }
            else if (lostTime > chaseMemorySeconds)
            {
                EnterState(AiState.Seek);
            }
            return;
        }

        predictTimer += dt;
        MoveTo(lastKnownPos, chaseSpeed);
        bool reached = predictTimer > 0.4f && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
        if (reached || predictTimer > predictMaxSeconds) EnterState(AiState.Seek);
    }

    // 逃げた先の予測：相手が最後に動いていた方向（止まっていたなら、自分が走ってきた方向の先）へ進んだ場所
    private Vector3 PredictEscapePoint()
    {
        Vector3 direction = targetVelocity.magnitude > 0.5f ? targetVelocity : lastKnownPos - chaseStartPos;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        direction.Normalize();

        if (!NavMesh.SamplePosition(lastKnownPos, out NavMeshHit from, 3f, NavMesh.AllAreas)) return lastKnownPos;

        // まっすぐが壁で行き止まりなら、左右へ振って、先まで進める向きを選ぶ（まっすぐに近いほど優先）
        Vector3 best = from.position;
        float bestScore = 0f;
        foreach (float angle in PredictAngles)
        {
            Vector3 end = from.position + Quaternion.Euler(0f, angle, 0f) * direction * predictDistance;
            Vector3 reach = NavMesh.Raycast(from.position, end, out NavMeshHit blocked, NavMesh.AllAreas) ? blocked.position : end;
            float score = Vector3.Distance(from.position, reach) * Mathf.Cos(angle * 0.5f * Mathf.Deg2Rad);
            if (score > bestScore)
            {
                bestScore = score;
                best = reach;
            }
        }
        return NavMesh.SamplePosition(best, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : from.position;
    }

    // 見失った場所の周りを、歩いてあちこち探す。見つからなければ、徘徊に戻る
    private void UpdateSeek(float dt)
    {
        seekPointTime += dt;
        bool arrived = hasSeekPoint && seekPointTime > 0.4f && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.4f;
        if (!hasSeekPoint || arrived || seekPointTime > 3f)
        {
            Vector3 random = lastKnownPos + Random.insideUnitSphere * seekRadius;
            random.y = lastKnownPos.y;
            if (NavMesh.SamplePosition(random, out NavMeshHit hit, seekRadius, NavMesh.AllAreas))
            {
                seekPoint = hit.position;
                hasSeekPoint = true;
                seekPointTime = 0f;
            }
        }

        if (hasSeekPoint) MoveTo(seekPoint, patrolSpeed);
        if (stateTime > seekSeconds) EnterState(AiState.Roam);
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
        if (!targetVisible && state != AiState.Chase && suspicious)
        {
            look = suspectPos - transform.position;
        }
        else if (targetVisible && (winding || agent.isStopped))
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
        suspicious = false;
        suspectLevelNow = 0f;
#if UNITY_EDITOR
        DebugRays.Clear();
#endif
        float bestDist = float.MaxValue;
        Vector3 eye = EyePosition();

        // ローカル（ホスト自身）のプレイヤー
        if (localPlayer == null)
        {
            localPlayer = FindAnyObjectByType<PlayerController>();
            if (localPlayer != null)
            {
                localController = localPlayer.GetComponent<CharacterController>();
                localMovement = localPlayer.GetComponent<PlayerMovement>();
            }
        }
        if (localPlayer != null)
        {
            if (localPlayer.IsDown)
            {
                awareness.Remove(LocalAwarenessKey);
                hiddenIds.Remove(LocalAwarenessKey);
            }
            else
            {
                // 頭・胴・腰の位置は、当たり判定の高さから出す（しゃがむと低くなる）
                Vector3 chest = localController != null ? localController.bounds.center : localPlayer.transform.position;
                float half = localController != null ? localController.bounds.extents.y : 1f;
                bool crouching = localMovement != null && localMovement.IsCrouching;
                ConsiderTarget(LocalAwarenessKey, eye,
                    chest + Vector3.up * Mathf.Max(0f, half - 0.15f), chest, chest - Vector3.up * (half * 0.5f),
                    crouching, ref bestDist);
            }
        }

        // 他のプレイヤー（アバターの位置から）
        if (RemotePlayerSync.Instance != null)
        {
            RemotePlayerSync.Instance.GetAliveRemoteIds(remoteIds);
            foreach (ulong id in remoteIds)
            {
                if (!RemotePlayerSync.Instance.TryGetAvatarView(id, out Vector3 eyePos, out _, out _)) continue;
                // 他のプレイヤーは、届いた目の位置から下へ、胴と腰を出す
                bool remoteCrouching = RemotePlayerSync.Instance.IsRemoteCrouching(id);
                ConsiderTarget(id, eye, eyePos,
                    eyePos - Vector3.up * (remoteCrouching ? 0.25f : 0.6f),
                    eyePos - Vector3.up * (remoteCrouching ? 0.5f : 1.1f),
                    remoteCrouching, ref bestDist);
            }
        }
    }

    private Transform headBone;

    /// <summary>目の位置。頭の骨があれば頭（動きに合わせて上下する）、なければ決まった高さ</summary>
    private Vector3 EyePosition()
    {
        return headBone != null ? headBone.position + Vector3.up * 0.08f : transform.position + Vector3.up * eyeHeight;
    }

    /// <summary>視線の向き。体の正面を、少し下へ傾けた向き</summary>
    private Quaternion ViewRotation()
    {
        return Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(sightDownTilt, 0f, 0f);
    }

    /// <summary>目から前へ広がるコーン（左右と上下で広さが違う）の中にあるか。距離と遮るものは別に調べる</summary>
    private bool IsInViewCone(Vector3 eye, Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(ViewRotation()) * (point - eye);
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
        float h = yaw / Mathf.Max(1f, sightHalfAngle);
        float v = pitch / Mathf.Max(1f, sightVerticalHalfAngle);
        return h * h + v * v <= 1f;
    }

    private static readonly RaycastHit[] SightHits = new RaycastHit[32];

    /// <summary>
    /// 目から相手の体までの間に、当たり判定（壁・床・天井・設置物・他の敵）がなければ true。
    /// 薄い板の当たり判定は、裏側から見ると光線が素通りしてしまうので、裏面も判定に含めて調べる。
    /// </summary>
    private bool HasLineOfSight(Vector3 eye, Vector3 target)
    {
        int itemsLayer = LayerMask.NameToLayer("Items");
        int mask = ~(1 << 2);
        if (itemsLayer >= 0) mask &= ~(1 << itemsLayer);

        Vector3 delta = target - eye;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;

        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(eye, delta / distance, SightHits, distance, mask, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;

        for (int i = 0; i < count; i++)
        {
            Collider col = SightHits[i].collider;
            if (col == null) continue;
            if (col.GetComponentInParent<EnemyBrain>() == this) continue;        // 自分の体は無視
            if (col.GetComponentInParent<PlayerController>() != null) continue;  // 見ようとしている本人
            return false;
        }
        return true;
    }

    // 見えている間は「気づきかけ」がたまり、1 になると気づく。見えない間は少しずつ消える。
    // 視線は、頭・胴・腰の3本。見えている本数が多いほど早く気づく（物陰から少しだけ出ていると、気づくのが遅い）。
    // しゃがんでいる相手は、見える距離が短く、気づくまでの時間も長い。
    // 気づいた直後（残りが多い間）は、しゃがんでも、すぐには見失わない
    private void ConsiderTarget(ulong id, Vector3 eye, Vector3 head, Vector3 chest, Vector3 waist, bool crouching, ref float bestDist)
    {
        awareness.TryGetValue(id, out float level);
        bool remembered = level > RememberLevel;

        // 机や椅子の下に潜っている（しゃがんでいて、頭のすぐ上に何かある）ときは、さらに気づかれにくい
        bool hidden = crouching && IsUnderCover(head);
        if (hidden) hiddenIds.Add(id); else hiddenIds.Remove(id);

        Vector3 to = chest - eye;
        float dist = to.magnitude;
        float range = sightRange;
        if (crouching && !remembered) range = hidden ? hiddenSightRange : crouchSightRange;
        float instant = !crouching ? instantNoticeRange : (hidden ? instantNoticeRangeHidden : instantNoticeRangeCrouching);

        // 頭から前へ広がるコーン状の視界の中に入っていて、当たり判定に遮られていない部位を数える
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        int visibleParts = 0;
        Vector3 seenPoint = chest; // 狙う場所（胴が見えれば胴、次に頭、腰の順）
        if (dist <= range)
        {
            bool seeWaist = IsInViewCone(eye, waist) && HasLineOfSight(eye, waist);
            bool seeHead = IsInViewCone(eye, head) && HasLineOfSight(eye, head);
            bool seeChest = IsInViewCone(eye, chest) && HasLineOfSight(eye, chest);
            if (seeWaist) { visibleParts++; seenPoint = waist; }
            if (seeHead) { visibleParts++; seenPoint = head; }
            if (seeChest) { visibleParts++; seenPoint = chest; }
#if UNITY_EDITOR
            DebugAddRay(eye, head, IsInViewCone(eye, head), seeHead);
            DebugAddRay(eye, chest, IsInViewCone(eye, chest), seeChest);
            DebugAddRay(eye, waist, IsInViewCone(eye, waist), seeWaist);
#endif
        }
#if UNITY_EDITOR
        else
        {
            DebugAddRay(eye, chest, false, false);
        }
#endif
        bool canSee = visibleParts > 0;

        if (!canSee)
        {
            level = Mathf.MoveTowards(level, 0f, Time.deltaTime / Mathf.Max(0.05f, awarenessFadeTime));
        }
        else if (remembered || flat.magnitude <= instant)
        {
            level = 1f;
        }
        else
        {
            // 遠いほど、また見えている部分が少ないほど、気づくまでに時間がかかる
            Vector2 seconds = !crouching ? standingNoticeSeconds : (hidden ? hiddenNoticeSeconds : crouchingNoticeSeconds);
            float time = Mathf.Lerp(seconds.x, seconds.y, Mathf.InverseLerp(instant, range, dist));
            if (visibleParts == 2) time *= twoPartsTimeScale;
            else if (visibleParts == 1) time *= onePartTimeScale;
            level = Mathf.Min(1f, level + Time.deltaTime / Mathf.Max(0.05f, time));
        }
        awareness[id] = level;

        if (!canSee) return;

        if (level >= 1f)
        {
            if (dist >= bestDist) return;
            bestDist = dist;
            targetVisible = true;
            visibleTargetId = id;
            visibleTargetPos = seenPoint;
        }
        else if (level >= suspicionLevel && level > suspectLevelNow)
        {
            suspicious = true;
            suspectLevelNow = level;
            suspectPos = seenPoint;
        }
    }

    private const float RememberLevel = 0.7f;

    private static readonly RaycastHit[] CoverHits = new RaycastHit[16];

    /// <summary>頭のすぐ上に当たり判定があるか（机や椅子などの下に潜っている）</summary>
    private bool IsUnderCover(Vector3 head)
    {
        int itemsLayer = LayerMask.NameToLayer("Items");
        int mask = ~(1 << 2);
        if (itemsLayer >= 0) mask &= ~(1 << itemsLayer);

        // 机の天板は薄いことがあるので、裏面も当たるようにする
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        int count = Physics.RaycastNonAlloc(head, Vector3.up, CoverHits, coverCheckHeight, mask, QueryTriggerInteraction.Ignore);
        Physics.queriesHitBackfaces = previous;

        for (int i = 0; i < count; i++)
        {
            Collider col = CoverHits[i].collider;
            if (col == null) continue;
            if (col.GetComponentInParent<EnemyBrain>() != null) continue;
            if (col.GetComponentInParent<PlayerController>() != null) continue;
            return true;
        }
        return false;
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------
    // デバッグ表示（DebugOverlay）用の情報。エディタだけで、ビルドには含まれない
    // ------------------------------------------------------------------

    public struct DebugRay
    {
        public Vector3 from, to;
        public bool inView; // 距離と角度の範囲内
        public bool clear;  // 遮られていない
    }

    /// <summary>机などの下に潜っていると判定しているプレイヤー</summary>
    public HashSet<ulong> DebugHidden => hiddenIds;

    /// <summary>直前の索敵で調べた視線</summary>
    public readonly List<DebugRay> DebugRays = new List<DebugRay>();

    public static IEnumerable<EnemyBrain> DebugAll => Registry.Values;
    public IReadOnlyDictionary<ulong, float> DebugAwareness => awareness;
    public static ulong DebugLocalKey => LocalAwarenessKey;

    public bool DebugIsPuppet => isPuppet;
    public Vector3 DebugEye => EyePosition();
    public Quaternion DebugViewRotation => ViewRotation();
    public float DebugSightVerticalHalfAngle => sightVerticalHalfAngle;
    public float DebugSightHalfAngle => sightHalfAngle;
    public float DebugSightRange => sightRange;
    public float DebugCrouchSightRange => crouchSightRange;
    public float DebugInstantRange => instantNoticeRange;
    public float DebugInstantRangeCrouching => instantNoticeRangeCrouching;
    public Vector3 DebugLastKnownPos => lastKnownPos;
    public bool DebugInvestigating => !isPuppet && state != AiState.Roam && !targetVisible;
    public NavMeshPath DebugPath => agent != null && agent.isOnNavMesh && agent.hasPath ? agent.path : null;

    public string DebugState
    {
        get
        {
            if (isPuppet) return "参加者側（見た目だけ）: " + animState;
            if (stunTimer > 0f) return $"スタン {stunTimer:0.0}s";
            if (winding) return $"構え {windupTimer:0.0}s";
            switch (state)
            {
                case AiState.Chase:
                    if (targetVisible) return "追跡（見えている）";
                    return predicting ? $"予測した場所へ走る（{predictTimer:0.0}s）" : $"最後に見た場所へ走る（見失い {lostTime:0.0}s）";
                case AiState.Investigate: return suspicious ? "物音を調べる（気づきかけ）" : "物音を調べる";
                case AiState.Seek: return $"歩いて探す（あと {Mathf.Max(0f, seekSeconds - stateTime):0.0}s）";
                default: return suspicious ? "気づきかけ（歩いて向かう）" : "徘徊";
            }
        }
    }

    private void DebugAddRay(Vector3 from, Vector3 to, bool inView, bool clear)
    {
        DebugRays.Add(new DebugRay { from = from, to = to, inView = inView, clear = clear });
    }
#endif

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
        ItemImpactNoise.Attach(obj, transform); // 飛んだ先でぶつかる音は、ほかの敵に聞こえる
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
        if (activeIndex >= clipList.Length || clipList[activeIndex] == null) activeIndex = (int)AnimState.Idle;

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
