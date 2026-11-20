using UnityEngine;

/// <summary>
/// 自分が落とした／投げたアイテムが止まったら、その位置と回転を全員へ配る（マルチプレイ）。
/// 他プレイヤーの画面では物理演算の結果が少しずれるので、最後にこの位置へ合わせて着地点を揃える。
/// GameplaySync.NotifyItemDropped が付ける。
/// </summary>
public class DroppedItemSettler : MonoBehaviour
{
    private const float RestSeconds = 0.4f;     // この時間、動かなければ静止とみなす
    private const float MaxWaitSeconds = 8f;    // 転がり続けても、この時間で打ち切って送る
    private const float LinearThresholdSqr = 0.0025f;
    private const float AngularThresholdSqr = 0.01f;

    private Rigidbody body;
    private float elapsed;
    private float restTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (body == null)
        {
            Destroy(this);
            return;
        }

        elapsed += Time.deltaTime;

        bool resting = body.linearVelocity.sqrMagnitude < LinearThresholdSqr
                    && body.angularVelocity.sqrMagnitude < AngularThresholdSqr;
        restTime = resting ? restTime + Time.deltaTime : 0f;

        if (restTime >= RestSeconds || elapsed >= MaxWaitSeconds)
        {
            Item item = GetComponent<Item>();
            GameplaySync.NotifyItemSettled(item, transform.position, transform.eulerAngles);
            Destroy(this);
        }
    }
}
