using UnityEngine;

/// <summary>
/// 実行中に作る図形（他のプレイヤーのカプセル、爆発の球など）に付ける材質。
/// CreatePrimitive が付ける標準の材質は、ビルドしたゲームでは描けず、マゼンタ（紫がかったピンク）になる。
/// そのため、Resources/Materials に置いた URP の材質を使う（Resources にあるので、必ずビルドに入る）。
/// </summary>
public static class RuntimeMaterials
{
    private static Material lit;
    private static Material unlit;

    /// <summary>周りの明かりを受ける材質を付けて、色を決める</summary>
    public static void ApplyLit(Renderer target, Color color)
    {
        Apply(target, ref lit, "Materials/RuntimeLit", color);
    }

    /// <summary>明かりに関係なく、その色で光って見える材質を付ける</summary>
    public static void ApplyUnlit(Renderer target, Color color)
    {
        Apply(target, ref unlit, "Materials/RuntimeUnlit", color);
    }

    private static void Apply(Renderer target, ref Material source, string path, Color color)
    {
        if (target == null) return;
        if (source == null) source = Resources.Load<Material>(path);
        if (source == null)
        {
            Debug.LogWarning($"[材質] Resources/{path} がありません。");
            return;
        }

        target.sharedMaterial = source;
        target.material.color = color; // この 1 個だけの複製に、色を付ける
    }
}
