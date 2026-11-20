using System.Collections;
using UnityEngine;

public class SantaEnemyAI : MonoBehaviour
{
    [Header("索敵・視認距離の設定")]
    public float maxSightDistance = 15f; // 視認できる最大距離（これより離れると見えなくなる）
    public float loseSightTime = 1.0f;    // 視認できなくなってから追跡をやめるまでの秒数（0で即見失う）
    public float attackRange = 6f;        // 立ち止まって投げ始める距離
    [Range(0f, 360f)]
    public float viewAngle = 100f;        // 正面の視野角

    [Header("遮蔽物（壁）判定の設定")]
    public float eyeHeight = 1.3f;        // サンタの目の高さ
    public LayerMask obstacleMask = ~0;   // 遮蔽物として判定するレイヤー

    [Header("移動と攻撃の設定")]
    public float walkSpeed = 2.0f;        // 普段の歩く速度
    public float runSpeed = 5.0f;         // 追いかける時の走る速度（Root Motionオフ時用）
    public float attackCooldown = 2.5f;   // 次の投擲までの待ち時間（秒）

    [Header("プレゼント投擲の設定")]
    public GameObject presentPrefab;      // 飛ばすプレゼントのプレハブ
    public Transform rightHandPoint;      // 発射位置（サンタのRightHandボーン）
    public float releaseDelay = 0.45f;    // 投げモーション開始から手放すまでの秒数
    public float throwSpeed = 12f;        // 投げる強さ（横スピード）
    public float upwardArc = 3.5f;        // 山なりに飛ばす上向きの力

    private Animator animator;
    private CharacterController controller;
    private Transform player;
    private Transform playerRoot;
    private float lastAttackTime;
    private float wanderTimer;
    private Quaternion targetWanderRotation;
    private bool isChasing = false;
    private float timeSinceLastSawPlayer = 0f;

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerRoot = playerObj.transform;
            Camera childCam = playerObj.GetComponentInChildren<Camera>();
            player = (childCam != null) ? childCam.transform : playerObj.transform;
        }
        else if (Camera.main != null)
        {
            player = Camera.main.transform;
            playerRoot = player.root;
        }

        targetWanderRotation = transform.rotation;
    }

    void Update()
    {
        if (player == null || animator == null) return;

        Vector3 flatSelf = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatPlayer = new Vector3(player.position.x, 0f, player.position.z);
        float distance = Vector3.Distance(flatSelf, flatPlayer);

        // 最大視認距離以内 ＋ 遮蔽物なし の時だけ視認できる
        bool canSeePlayer = (distance <= maxSightDistance) && HasLineOfSight();

        if (!isChasing)
        {
            if (canSeePlayer && IsPlayerInAngle())
            {
                isChasing = true;
                timeSinceLastSawPlayer = 0f;
            }
        }
        else
        {
            if (canSeePlayer)
            {
                timeSinceLastSawPlayer = 0f;
            }
            else
            {
                timeSinceLastSawPlayer += Time.deltaTime;
            }

            // 視認距離の外に出るか壁に隠れて一定時間経ったら追跡終了
            if (timeSinceLastSawPlayer >= loseSightTime)
            {
                isChasing = false;
            }
        }

        if (isChasing)
        {
            if (distance <= attackRange && canSeePlayer)
            {
                // 攻撃範囲内：足を止めて投げる
                animator.SetBool("IsRunning", false);
                LookAtTarget(player.position);

                if (Time.time >= lastAttackTime + attackCooldown)
                {
                    animator.SetTrigger("Throw");
                    StartCoroutine(ThrowPresentAfterDelay());
                    lastAttackTime = Time.time;
                }
            }
            else
            {
                // 追跡中：走って追いかける
                animator.SetBool("IsRunning", true);
                LookAtTarget(player.position);
                MoveForward(runSpeed);
            }
        }
        else
        {
            // 未発見・見失った後：周囲をゆっくり歩く
            animator.SetBool("IsRunning", false);
            WanderAround();
        }
    }

    IEnumerator ThrowPresentAfterDelay()
    {
        yield return new WaitForSeconds(releaseDelay);

        if (presentPrefab == null || player == null) yield break;

        Vector3 spawnPos = (rightHandPoint != null)
            ? rightHandPoint.position
            : transform.position + Vector3.up * 1.3f + transform.right * 0.4f + transform.forward * 0.5f;

        GameObject present = Instantiate(presentPrefab, spawnPos, Quaternion.identity);

        Collider presentCol = present.GetComponent<Collider>();
        if (presentCol != null)
        {
            foreach (Collider selfCol in GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(presentCol, selfCol);
            }
        }

        Rigidbody rb = present.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 targetPos = (player.GetComponent<Camera>() != null)
                ? player.position - Vector3.up * 0.3f
                : player.position + Vector3.up * 0.9f;

            Vector3 dir = (targetPos - spawnPos).normalized;
            rb.linearVelocity = dir * throwSpeed + Vector3.up * upwardArc;
        }
    }

    bool IsPlayerInAngle()
    {
        Vector3 dirToPlayer = player.position - transform.position;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude < 0.001f) return true;

        float angle = Vector3.Angle(transform.forward, dirToPlayer.normalized);
        return angle <= viewAngle * 0.5f;
    }

    bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 target = (player.GetComponent<Camera>() != null)
            ? player.position
            : player.position + Vector3.up * 1.0f;

        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance <= 0.01f) return true;
        if (distance > maxSightDistance) return false;

        if (IsBlocked(origin, direction.normalized, distance)) return false;
        if (IsBlocked(target, -direction.normalized, distance)) return false;

        return true;
    }

    bool IsBlocked(Vector3 start, Vector3 dir, float dist)
    {
        RaycastHit[] hits = Physics.RaycastAll(start, dir, dist, obstacleMask, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            if (hit.transform == player || (playerRoot != null && (hit.transform == playerRoot || hit.transform.IsChildOf(playerRoot))))
                continue;

            return true;
        }
        return false;
    }

    void LookAtTarget(Vector3 targetPos)
    {
        Vector3 direction = targetPos - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 8f);
        }
    }

    void MoveForward(float speed)
    {
        if (animator.applyRootMotion) return;

        if (controller != null)
        {
            controller.SimpleMove(transform.forward * speed);
        }
        else
        {
            transform.position += transform.forward * speed * Time.deltaTime;
        }
    }

    void WanderAround()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
        {
            float randomAngle = Random.Range(0f, 360f);
            targetWanderRotation = Quaternion.Euler(0f, randomAngle, 0f);
            wanderTimer = Random.Range(3f, 5f);
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, targetWanderRotation, Time.deltaTime * 2f);
        MoveForward(walkSpeed);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;

        // 視認できる距離と視野角（黄色）
        Gizmos.color = Color.yellow;
        Vector3 leftDir = Quaternion.Euler(0, -viewAngle * 0.5f, 0) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0, viewAngle * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(origin, leftDir * maxSightDistance);
        Gizmos.DrawRay(origin, rightDir * maxSightDistance);
        Gizmos.DrawWireSphere(transform.position, maxSightDistance);

        // 攻撃範囲（赤色）
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}