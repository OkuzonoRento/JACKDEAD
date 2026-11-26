using System.Collections.Generic;
using UnityEngine;

/// <summary>落ちているアイテムの光り方（調整は PlayerInteraction のインスペクター。再生中に変えてもすぐ反映される）</summary>
[System.Serializable]
public class ItemGlowSettings
{
    [Tooltip("オフにすると、光らせない")]
    public bool enabled = true;

    [Header("ふだん")]
    public Color color = new Color(1f, 0.82f, 0.42f);
    [Range(0f, 1f), Tooltip("面全体の明るさ")] public float surface = 0.1f;
    [Range(0f, 2f), Tooltip("ふちの明るさ")] public float edge = 0.5f;
    [Range(0f, 2f), Tooltip("ときどき流れる光の明るさ")] public float glint = 0.6f;
    [Min(0.5f), Tooltip("光が流れる間隔（秒）")] public float glintInterval = 2.5f;

    [Header("照準を合わせたとき")]
    public Color focusColor = new Color(1f, 0.97f, 0.85f);
    [Range(1f, 4f), Tooltip("ふだんより何倍明るくするか")] public float focusBoost = 2.2f;

    [Header("距離")]
    [Min(0f), Tooltip("この距離から薄くなる（m）")] public float fadeStartDistance = 10f;
    [Min(0f), Tooltip("この距離で見えなくなる（m）")] public float fadeEndDistance = 18f;
}

/// <summary>
/// 落ちているアイテムに、光を重ねて描く（暗い場所でも見つけやすくする）。Item が付ける。爆弾には付けない。
/// 照準が合っているアイテムは、さらに明るくする。
/// </summary>
public class ItemHighlight : MonoBehaviour
{
    private const string ShaderPath = "Shaders/ItemGlow"; // Resources の中の場所

    private static Material normalMaterial;
    private static Material focusMaterial;
    private static ItemHighlight focused;
    private static ItemGlowSettings settings = new ItemGlowSettings();
    private static bool shaderMissing;

    private readonly List<Renderer> targets = new List<Renderer>();

    public static void Attach(GameObject item)
    {
        if (item.GetComponent<ItemHighlight>() == null) item.AddComponent<ItemHighlight>();
    }

    /// <summary>照準が合っているアイテム（なければ null）</summary>
    public static void SetFocus(Item item)
    {
        ItemHighlight next = item != null ? item.GetComponent<ItemHighlight>() : null;
        if (next == focused) return;

        if (focused != null) focused.SetMaterial(normalMaterial);
        focused = next;
        if (focused != null) focused.SetMaterial(focusMaterial);
    }

    public static void Apply(ItemGlowSettings newSettings)
    {
        if (newSettings != null) settings = newSettings;
        if (!EnsureMaterials()) return;

        Write(normalMaterial, settings.color, 1f);
        Write(focusMaterial, settings.focusColor, settings.focusBoost);
    }

    private static void Write(Material material, Color color, float boost)
    {
        float on = settings.enabled ? boost : 0f;
        material.SetColor("_GlowColor", color);
        material.SetFloat("_Surface", settings.surface * on);
        material.SetFloat("_Rim", settings.edge * on);
        material.SetFloat("_Glint", settings.glint * on);
        material.SetFloat("_GlintInterval", settings.glintInterval);
        material.SetFloat("_FadeStart", settings.fadeStartDistance);
        material.SetFloat("_FadeEnd", Mathf.Max(settings.fadeEndDistance, settings.fadeStartDistance + 0.1f));
    }

    private static bool EnsureMaterials()
    {
        if (normalMaterial != null && focusMaterial != null) return true;
        if (shaderMissing) return false;

        Shader shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported)
        {
            shaderMissing = true;
            Debug.LogWarning("[アイテム] 光らせるシェーダー（Resources/Shaders/ItemGlow）が使えません。");
            return false;
        }

        normalMaterial = new Material(shader) { name = "ItemGlow" };
        focusMaterial = new Material(shader) { name = "ItemGlowFocus" };
        Write(normalMaterial, settings.color, 1f);
        Write(focusMaterial, settings.focusColor, settings.focusBoost);
        return true;
    }

    private void Start()
    {
        if (!EnsureMaterials()) return;

        // 元の材質はそのままに、光を描く材質を後ろへ足す
        foreach (MeshRenderer target in GetComponentsInChildren<MeshRenderer>())
        {
            if (target.GetComponent<TMPro.TMP_Text>() != null) continue;

            List<Material> materials = new List<Material>(target.sharedMaterials) { normalMaterial };
            target.sharedMaterials = materials.ToArray();
            targets.Add(target);
        }
        if (focused == this) SetMaterial(focusMaterial);
    }

    private void SetMaterial(Material material)
    {
        foreach (Renderer target in targets)
        {
            if (target == null) continue;
            Material[] materials = target.sharedMaterials;
            materials[materials.Length - 1] = material;
            target.sharedMaterials = materials;
        }
    }
}
