using System;
using UnityEngine;

/// <summary>
/// 敵が聞き取る「音」の通知。足音・投げた物がぶつかる・爆発・スロットなど、
/// 音が出る場所から Emit を呼ぶと、聞こえる範囲にいる敵（EnemyBrain）が音のした方へ調べに行く。
/// loudness は「どれだけ遠くまで聞こえるか」（m）。
/// </summary>
public static class EnemyNoise
{
    // 音の大きさ（聞こえる距離 m）の目安
    public const float Sprint = 16f;
    public const float Walk = 0f;     // 歩きは無音（敵は音では気づかない。0 の音は通知されない）
    public const float Crouch = 0f;   // しゃがみ歩きも無音
    public const float Jump = 9f;      // 踏み切る音
    /// <summary>着地の音（落ちる勢いが弱い → 強い）</summary>
    public const float LandMin = 11f;
    public const float LandMax = 24f;
    /// <summary>投げた／落とした物がぶつかる音（ぶつかった場所で出る）</summary>
    public const float ItemImpact = 10f;
    public const float BoxBounce = 9f;
    /// <summary>スロットを回している間、くり返し出る音</summary>
    public const float Slot = 30f;
    public const float Explosion = 30f;
    /// <summary>ボイスチャットの声（小声 → 大声。マイクに入る声の大きさで変わる）</summary>
    public const float VoiceMin = 8f;
    public const float VoiceMax = 20f;

    /// <summary>聞こえる距離の最大（m）。これより大きい値は切り詰める</summary>
    public const float MaxLoudness = 30f;

    public static event Action<Vector3, float> Heard;

    public static void Emit(Vector3 position, float loudness)
    {
        if (loudness <= 0f) return;
        Heard?.Invoke(position, Mathf.Min(loudness, MaxLoudness));
    }
}
