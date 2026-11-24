using UnityEngine;

/// <summary>
/// 他のプレイヤーが落とした／投げたアイテム（Box を含む）を、持ち主から届く位置へ追いかけさせる（マルチプレイ）。
/// ・持ち主の画面での動きがそのまま見えるよう、短い間隔で届く位置へ、物理の速さで寄せていく（体にぶつかる・跳ねる判定は、そのまま働く）
/// ・離れすぎたら、その位置へ移す
/// ・位置が届かなくなったら（止まった・受信できない）、普通の物理に戻る。止まったときの最後の位置は、GameplaySync の ItemSettle が決める
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RemoteItemFollower : MonoBehaviour
{
    private const float FollowTime = 0.08f;     // この時間で、届いた位置へ追いつく速さにする
    private const float TeleportDistance = 3f;  // これ以上離れたら、その位置へ移す
    private const float LostSeconds = 0.6f;     // これだけ届かなければ、追うのをやめる

    private Rigidbody body;
    private Vector3 targetPos;
    private Quaternion targetRot;
    private float lastReceived;
    private bool hasTarget;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public void SetTarget(Vector3 position, Quaternion rotation)
    {
        if (body == null) body = GetComponent<Rigidbody>();
        targetPos = position;
        targetRot = rotation;
        lastReceived = Time.time;
        if (!hasTarget)
        {
            hasTarget = true;
            body.isKinematic = false;
        }
    }

    private void FixedUpdate()
    {
        if (!hasTarget || body == null) return;

        if (Time.time - lastReceived > LostSeconds)
        {
            Destroy(this); // 届かなくなった：あとは普通の物理に任せる
            return;
        }

        Vector3 delta = targetPos - body.position;
        if (delta.magnitude > TeleportDistance)
        {
            body.position = targetPos;
            body.linearVelocity = Vector3.zero;
        }
        else
        {
            body.linearVelocity = delta / FollowTime;
        }

        body.MoveRotation(Quaternion.Slerp(body.rotation, targetRot, 0.5f));
        body.angularVelocity = Vector3.zero;
    }
}
