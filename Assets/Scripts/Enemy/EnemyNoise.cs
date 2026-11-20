using System;
using UnityEngine;

/// <summary>
/// 敵が聞き取る「音」の通知。足音・物を落とす／投げる・Box が跳ねる・爆発・スロットなど、
/// 音が出る場所から Emit を呼ぶと、聞こえる範囲にいる敵（EnemyBrain）が音のした方へ調べに行く。
/// loudness は「どれだけ遠くまで聞こえるか」（m）。
/// </summary>
public static class EnemyNoise
{
    // 音の大きさ（聞こえる距離 m）の目安
    public const float Sprint = 16f;
    public const float Walk = 7f;
    public const float Crouch = 2.5f;
    public const float ItemDrop = 10f;
    public const float BoxBounce = 9f;
    public const float Slot = 30f;
    public const float Explosion = 30f;

    /// <summary>聞こえる距離の最大（m）。これより大きい値は切り詰める</summary>
    public const float MaxLoudness = 30f;

    public static event Action<Vector3, float> Heard;

    public static void Emit(Vector3 position, float loudness)
    {
        if (loudness <= 0f) return;
        Heard?.Invoke(position, Mathf.Min(loudness, MaxLoudness));
    }
}
