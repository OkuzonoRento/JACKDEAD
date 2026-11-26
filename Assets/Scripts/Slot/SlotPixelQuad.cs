using System.Collections.Generic;
using UnityEngine;

/// <summary>台の前に出すドット絵の板。大きさは m で指定する</summary>
public class SlotPixelQuad
{
    private static Mesh quadMesh;
    private static Shader shader;
    private static bool shaderMissing;
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

    public readonly Transform transform;
    private readonly Material material;

    private SlotPixelQuad(Transform transform, Material material)
    {
        this.transform = transform;
        this.material = material;
    }

    /// <summary>シェーダーが使えないときは null</summary>
    public static SlotPixelQuad Create(Transform parent, string name)
    {
        if (!EnsureShared()) return null;

        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = quadMesh;

        MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
        Material material = new Material(shader);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        obj.SetActive(false);
        return new SlotPixelQuad(obj.transform, material);
    }

    public bool Visible
    {
        get => transform != null && transform.gameObject.activeSelf;
        set { if (transform != null) transform.gameObject.SetActive(value); }
    }

    public Texture2D Texture { set => material.mainTexture = value; }

    public Color Color { set => material.SetColor("_Color", value); }

    /// <summary>重なる順（大きいほど手前に描く）</summary>
    public int Order { set => material.renderQueue = 3000 + value; }

    /// <summary>横幅を指定して、絵の縦横比のまま大きさを決める</summary>
    public void SetWidth(float width, float scale = 1f)
    {
        Texture picture = material.mainTexture;
        float ratio = picture != null ? picture.height / (float)picture.width : 1f;
        transform.localScale = new Vector3(width, width * ratio, 1f) * scale;
    }

    public void SetSize(float width, float height)
    {
        transform.localScale = new Vector3(width, height, 1f);
    }

    public void Destroy()
    {
        if (material != null) Object.Destroy(material);
        if (transform != null) Object.Destroy(transform.gameObject);
    }

    /// <summary>描いた絵をテクスチャにする。同じ名前なら使い回す</summary>
    public static Texture2D Cached(string key, System.Func<SlotPixelArt.Image> draw)
    {
        if (textures.TryGetValue(key, out Texture2D texture) && texture != null) return texture;
        texture = ToTexture(draw());
        textures[key] = texture;
        return texture;
    }

    public static Texture2D ToTexture(SlotPixelArt.Image image, Texture2D reuse = null)
    {
        Texture2D texture = reuse;
        if (texture == null || texture.width != image.width || texture.height != image.height)
        {
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        // 絵は左上が原点、テクスチャは左下が原点
        Color32[] pixels = new Color32[image.width * image.height];
        for (int y = 0; y < image.height; y++)
        {
            for (int x = 0; x < image.width; x++)
            {
                SlotPixelArt.Rgba p = image.pixels[y * image.width + x];
                pixels[(image.height - 1 - y) * image.width + x] = new Color32(p.r, p.g, p.b, p.a);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false);
        return texture;
    }

    private static bool EnsureShared()
    {
        if (shaderMissing) return false;
        if (shader == null)
        {
            shader = Resources.Load<Shader>("Shaders/SlotPixel");
            if (shader == null || !shader.isSupported)
            {
                shaderMissing = true;
                Debug.LogWarning("[スロット] 演出のシェーダー（Resources/Shaders/SlotPixel）が使えません。");
                return false;
            }
        }

        if (quadMesh == null)
        {
            quadMesh = new Mesh { name = "SlotPixelQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
            };
            quadMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quadMesh.RecalculateBounds();
        }
        return true;
    }
}
