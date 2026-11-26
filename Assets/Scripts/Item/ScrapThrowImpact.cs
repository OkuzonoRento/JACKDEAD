using System.Collections.Generic;
using UnityEngine;

/// <summary>投げたスクラップがぶつかったときの決まり（調整は PlayerController のインスペクター）</summary>
[System.Serializable]
public class ThrowImpactSettings
{
    [Header("金額の減り方（トラックの外でぶつけたとき）")]
    [Tooltip("この速さ（m/s）より弱くぶつかっても、何も起きない")]
    public float minImpactSpeed = 3f;
    [Tooltip("この速さ以上は、減り方が最大になる")]
    public float fullImpactSpeed = 11f;
    [Tooltip("弱くぶつけたときに減る割合（基準の価格に対して。壊れやすさを掛ける）")]
    [Range(0f, 1f)] public float minLossRate = 0.08f;
    [Tooltip("強くぶつけたときに減る割合")]
    [Range(0f, 1f)] public float maxLossRate = 0.3f;
    [Tooltip("続けて当たったときに、次に減るまでの間隔（秒）")]
    public float lossInterval = 0.25f;
    [Tooltip("プレイヤーや敵に当てたときも、金額を減らす")]
    public bool loseValueOnCharacterHit = true;

    [Header("プレイヤーに当てたとき")]
    [Tooltip("当たった人がスタンする時間（秒）")]
    public float playerStunSeconds = 0.6f;

    [Header("敵に当てたとき")]
    [Tooltip("敵がスタンする時間（秒）")]
    public float enemyStunSeconds = 1.5f;
    [Tooltip("敵へのダメージ = (基本 + 重さ × 重さあたり) × 速さの割合")]
    public float enemyBaseDamage = 4f;
    public float enemyDamagePerKg = 4f;
    public float enemyMaxDamage = 50f;
}

/// <summary>
/// 投げたスクラップ（爆弾以外）が何かにぶつかったときの処理。
/// ・まだ床や壁に当たっていない（直撃）なら、プレイヤーは少しスタンし、敵はスタンしてダメージを受ける
/// ・トラックの外でぶつかるたびに、金額が減る（0 になったら壊れる）
/// 投げた本人のPCだけに付ける（結果は GameplaySync が全員へ伝える）。物が止まったら外す。
/// </summary>
public class ScrapThrowImpact : MonoBehaviour
{
    private const float ThrowerGraceSeconds = 0.6f; // 投げた直後は、本人の体に当たっても何もしない
    private const float RestSeconds = 0.4f;
    private const float RestSpeedSqr = 0.01f;

    private ThrowImpactSettings settings;
    private PlayerController thrower;
    private Rigidbody body;
    private Item item;
    private float attachedTime;
    private float lastLossTime = -10f;
    private float restTime;
    private bool bounced; // 一度でも床や壁に当たったら、その後は誰もスタンさせない
    private readonly HashSet<int> hitTargets = new HashSet<int>();

    public static void Attach(GameObject thrownObject, PlayerController thrower, ThrowImpactSettings settings)
    {
        ScrapThrowImpact impact = thrownObject.GetComponent<ScrapThrowImpact>();
        if (impact == null) impact = thrownObject.AddComponent<ScrapThrowImpact>();
        impact.settings = settings;
        impact.thrower = thrower;
        impact.body = thrownObject.GetComponent<Rigidbody>();
        impact.item = thrownObject.GetComponent<Item>();
        impact.attachedTime = Time.time;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (settings == null || item == null || item.ItemData == null) return;

        float speed = collision.relativeVelocity.magnitude;
        bool hard = speed >= settings.minImpactSpeed;
        float strength = Mathf.InverseLerp(settings.minImpactSpeed, settings.fullImpactSpeed, speed);

        bool hitCharacter = false;
        Collider other = collision.collider;
        // スタンの時間は、アイテムに指定があれば（スタンガンなど）それを使う
        ItemDataAsset data = item.ItemData;
        float playerStun = data.PlayerStunSeconds > 0f ? data.PlayerStunSeconds : settings.playerStunSeconds;
        float enemyStun = data.EnemyStunSeconds > 0f ? data.EnemyStunSeconds : settings.enemyStunSeconds;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        EnemyBrain enemy = other.GetComponentInParent<EnemyBrain>();
        ulong remoteId = 0;
        bool hitRemote = player == null && enemy == null && RemotePlayerSync.Instance != null &&
                         RemotePlayerSync.Instance.TryGetClientId(other, out remoteId);

        if (player != null)
        {
            if (player == thrower && Time.time - attachedTime < ThrowerGraceSeconds) return;
            hitCharacter = true;
            if (hard && !bounced && hitTargets.Add(player.GetInstanceID())) player.Stun(playerStun);
        }
        else if (hitRemote)
        {
            hitCharacter = true;
            if (hard && !bounced && hitTargets.Add(other.GetInstanceID()))
                GameplaySync.NotifyPlayerStun(remoteId, playerStun);
        }
        else if (enemy != null)
        {
            hitCharacter = true;
            if (hard && !bounced && hitTargets.Add(enemy.GetInstanceID()))
            {
                float damage = Mathf.Min(settings.enemyMaxDamage,
                    (settings.enemyBaseDamage + item.ItemData.ItemWeight * settings.enemyDamagePerKg) * Mathf.Lerp(0.5f, 1f, strength));
                GameplaySync.HitEnemy(enemy, damage, enemyStun);
            }
        }
        else if (speed >= 1f)
        {
            bounced = true;
        }

        if (!hard || (hitCharacter && !settings.loseValueOnCharacterHit)) return;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        LoseValue(point, strength);
    }

    private void LoseValue(Vector3 point, float strength)
    {
        if (item.ItemData.IsBomb || item.ItemData.Fragility <= 0f) return;
        if (Time.time - lastLossTime < settings.lossInterval) return;
        if (IsInTruck(point)) return; // トラックの中では、傷まない

        int current = item.CurrentValue;
        if (current <= 0) return;

        float rate = Mathf.Lerp(settings.minLossRate, settings.maxLossRate, strength) * item.ItemData.Fragility;
        int loss = Mathf.Max(1, Mathf.RoundToInt(item.BaseValue * rate));
        int next = Mathf.Max(0, current - loss);

        lastLossTime = Time.time;
        GameplaySync.NotifyItemValue(item, next);
        item.ApplyDamagedValue(next);
    }

    private bool IsInTruck(Vector3 point)
    {
        DisplayTextManager truck = FindAnyObjectByType<DisplayTextManager>();
        return truck != null && (truck.IsInsideTruck(point) || truck.IsInsideTruck(transform.position));
    }

    private void Update()
    {
        bool resting = body == null || body.linearVelocity.sqrMagnitude < RestSpeedSqr;
        restTime = resting ? restTime + Time.deltaTime : 0f;
        if (restTime >= RestSeconds) Destroy(this);
    }
}
