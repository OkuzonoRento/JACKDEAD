using UnityEngine;

/// <summary>
/// 投げた／落とした物が何かにぶつかったとき、ぶつかった場所で音を出す（敵に聞こえる）。
/// ・物理を動かしている本人のPCだけに付ける（投げたプレイヤー。敵が投げた Box はホスト）
/// ・物が止まったら外す。止まっている物に人が触れても、音は出ない（歩きは無音のため）
/// </summary>
public class ItemImpactNoise : MonoBehaviour
{
    private const float MinImpactSpeed = 2f;   // これより弱くぶつかっても、音は出ない
    private const float NoiseInterval = 0.2f;  // 角が続けて当たるときに、音が重ならない間隔
    private const float RestSeconds = 0.4f;    // この時間、動かなければ止まったとみなす
    private const float RestSpeedSqr = 0.01f;

    private Rigidbody body;
    private Transform ignored;
    private float loudness;
    private float lastNoiseTime = -10f;
    private float restTime;

    /// <param name="thrownObject">投げた／落とした物</param>
    /// <param name="thrower">投げた本人の体にぶつかっても音を出さないときに指定する（敵が投げるとき）</param>
    public static void Attach(GameObject thrownObject, Transform thrower = null)
    {
        ItemImpactNoise noise = thrownObject.GetComponent<ItemImpactNoise>();
        if (noise == null) noise = thrownObject.AddComponent<ItemImpactNoise>();
        noise.body = thrownObject.GetComponent<Rigidbody>();
        noise.ignored = thrower;
        noise.loudness = thrownObject.GetComponent<BombBox>() != null ? EnemyNoise.BoxBounce : EnemyNoise.ItemImpact;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude < MinImpactSpeed) return;
        if (ignored != null && collision.transform.IsChildOf(ignored)) return;
        if (Time.time - lastNoiseTime < NoiseInterval) return;

        lastNoiseTime = Time.time;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        GameplaySync.NotifyNoise(point, loudness);
    }

    private void Update()
    {
        bool resting = body == null || body.linearVelocity.sqrMagnitude < RestSpeedSqr;
        restTime = resting ? restTime + Time.deltaTime : 0f;
        if (restTime >= RestSeconds) Destroy(this);
    }
}
