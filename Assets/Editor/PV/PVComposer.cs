using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// PV の1コマを作る：3D の画面（後処理つき）の上に、テロップ・暗転・閃光を重ねる。
/// 重ねる部品は、遠く離れた場所に置いた平行投影カメラ（1単位 = 1ピクセル）で描く。
/// フォントはメモリ上に作るので、プロジェクトのフォントアセットは書き換わらない。
/// </summary>
public class PVComposer : IDisposable
{
    private const int Layer = 31;
    private static readonly Vector3 Origin = new Vector3(0f, -5000f, 0f);

    private readonly RenderTexture sceneTarget;
    private readonly RenderTexture finalTarget;
    private readonly Texture2D frame;
    private readonly GameObject root;
    private readonly Camera camera;
    private readonly List<Object> owned = new List<Object>();
    private readonly TMP_FontAsset japanese;
    private readonly TMP_FontAsset latin;

    private readonly Material fadeMaterial;
    private readonly Material flashMaterial;
    private readonly Material shadeMaterial;
    private readonly Material lineMaterial;
    private readonly MeshRenderer fadeQuad;
    private readonly MeshRenderer flashQuad;
    private readonly MeshRenderer shadeQuad;
    private readonly MeshRenderer lineQuad;

    private readonly List<TextMeshPro> texts = new List<TextMeshPro>();
    private int used;

    public int LayerMaskBit => 1 << Layer;

    public PVComposer()
    {
        sceneTarget = new RenderTexture(PVRenderer.Width, PVRenderer.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        finalTarget = new RenderTexture(PVRenderer.Width, PVRenderer.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        sceneTarget.Create();
        finalTarget.Create();
        frame = new Texture2D(PVRenderer.Width, PVRenderer.Height, TextureFormat.RGBA32, false);

        root = new GameObject("PVComposer");
        root.transform.position = Origin;

        GameObject cameraObject = new GameObject("PVComposerCamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -100f);
        camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = PVRenderer.Height * 0.5f;
        camera.aspect = PVRenderer.Width / (float)PVRenderer.Height;
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 300f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << Layer;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        Material background = Own(new Material(Shader.Find("Unlit/Texture")));
        background.mainTexture = sceneTarget;
        Quad("Background", background, 0, PVRenderer.Width, PVRenderer.Height);

        Texture2D white = Own(Solid(2, 2, (x, y) => Color.white));
        Texture2D gradient = Own(Solid(2, 128, (x, y) => new Color(1f, 1f, 1f, Mathf.Pow(1f - y / 127f, 1.6f))));

        shadeMaterial = SpriteMaterial(gradient);
        shadeQuad = Quad("Shade", shadeMaterial, 5, PVRenderer.Width, 460f);
        shadeQuad.transform.localPosition = new Vector3(0f, -PVRenderer.Height * 0.5f + 230f, 0f);
        flashMaterial = SpriteMaterial(white);
        flashQuad = Quad("Flash", flashMaterial, 10, PVRenderer.Width, PVRenderer.Height);
        lineMaterial = SpriteMaterial(white);
        lineQuad = Quad("Line", lineMaterial, 35, 10f, 10f);
        fadeMaterial = SpriteMaterial(white);
        fadeQuad = Quad("Fade", fadeMaterial, 30, PVRenderer.Width, PVRenderer.Height);

        // 映画のような上下の黒帯
        Material bar = SpriteMaterial(white);
        bar.color = Color.black;
        Quad("BarTop", bar, 25, PVRenderer.Width, 70f).transform.localPosition = new Vector3(0f, PVRenderer.Height * 0.5f - 35f, 0f);
        Quad("BarBottom", bar, 25, PVRenderer.Width, 70f).transform.localPosition = new Vector3(0f, -PVRenderer.Height * 0.5f + 35f, 0f);

        japanese = MakeFont("Assets/Fonts/WDXLLubrifontJPN-Regular.ttf");
        latin = MakeFont("Assets/Fonts/Cinzel-VariableFont_wght.ttf");
        MakeNotice();
    }

    /// <summary>右下の黒帯に、ずっと出しておく注意書き</summary>
    private void MakeNotice()
    {
        GameObject go = new GameObject("PVNotice") { layer = Layer };
        go.transform.SetParent(root.transform, false);
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        t.font = japanese;
        t.fontSharedMaterial = japanese.material;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.alignment = TextAlignmentOptions.Right;
        t.rectTransform.pivot = new Vector2(1f, 0.5f);
        t.rectTransform.sizeDelta = new Vector2(1400f, 60f);
        t.rectTransform.anchoredPosition3D = new Vector3(PVRenderer.Width * 0.5f - 32f, -PVRenderer.Height * 0.5f + 35f, 0f);
        t.fontSize = 340f;
        t.color = new Color(0.9f, 0.9f, 0.9f, 0.9f);
        t.text = Notice;
        go.GetComponent<MeshRenderer>().sortingOrder = 60; // 暗転や閃光より手前
        t.ForceMeshUpdate(true, true);
    }

    private const string Notice = "※現在開発中のため、映像は実際のゲーム内容と異なる可能性があります。";

    private TMP_FontAsset MakeFont(string path)
    {
        Font source = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(path);
        if (source == null) throw new Exception("フォントがありません: " + path);
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 90, 12, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic);
        if (font == null) throw new Exception("フォントを作れません: " + path);
        Own(font);
        return font;
    }

    private T Own<T>(T o) where T : Object
    {
        owned.Add(o);
        return o;
    }

    private static Texture2D Solid(int w, int h, Func<int, int, Color> color)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) t.SetPixel(x, y, color(x, y));
        t.Apply();
        return t;
    }

    private Material SpriteMaterial(Texture2D texture)
    {
        Material m = Own(new Material(Shader.Find("Sprites/Default")));
        m.mainTexture = texture;
        return m;
    }

    private MeshRenderer Quad(string name, Material material, int order, float w, float h)
    {
        Mesh mesh = Own(new Mesh());
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };

        GameObject go = new GameObject(name) { layer = Layer };
        go.transform.SetParent(root.transform, false);
        go.transform.localScale = new Vector3(w, h, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.sortingOrder = order;
        return r;
    }

    // ───────── 1コマごとの指定 ─────────

    /// <summary>重ねる部品をすべて消す（各コマの最初に呼ぶ）</summary>
    public void Clear()
    {
        foreach (TextMeshPro t in texts) t.gameObject.SetActive(false);
        used = 0;
        fadeQuad.enabled = false;
        flashQuad.enabled = false;
        shadeQuad.enabled = false;
        lineQuad.enabled = false;
    }

    public void Fade(float alpha) { Show(fadeQuad, fadeMaterial, new Color(0f, 0f, 0f, alpha)); }

    public void Flash(Color color, float alpha)
    {
        color.a = alpha;
        Show(flashQuad, flashMaterial, color);
    }

    public void Line(float y, float width, float height, Color color)
    {
        lineQuad.transform.localPosition = new Vector3(0f, y, 0f);
        lineQuad.transform.localScale = new Vector3(Mathf.Max(0.01f, width), height, 1f);
        Show(lineQuad, lineMaterial, color);
    }

    private static void Show(MeshRenderer quad, Material material, Color color)
    {
        if (color.a <= 0.001f) return;
        color.a = Mathf.Clamp01(color.a);
        material.color = color;
        quad.enabled = true;
    }

    /// <summary>画面下のテロップ。start からの経過で、字が広がりながら現れて、end の前に消える</summary>
    public void Caption(string text, float time, float start, float end)
    {
        if (time < start || time > end) return;
        float a = Mathf.Min(Mathf.Clamp01((time - start) / 0.35f), Mathf.Clamp01((end - time) / 0.3f));
        float k = (time - start) / Mathf.Max(0.1f, end - start);
        Show(shadeQuad, shadeMaterial, new Color(0f, 0f, 0f, 0.8f * a));
        Text(text, new Vector2(0f, -330f), 84f, new Color(0.96f, 0.93f, 0.86f, a), Mathf.Lerp(4f, 14f, k), false, 20);
    }

    /// <summary>文字を1つ出す。pos は画面中央からのピクセル、size は文字の高さ（ピクセル）の目安</summary>
    public void Text(string text, Vector2 pos, float size, Color color, float spacing = 0f, bool logo = false, int order = 40)
    {
        if (color.a <= 0.001f) return;
        TextMeshPro t = Next();
        t.font = logo ? latin : japanese;
        t.fontSharedMaterial = t.font.material;
        t.text = text;
        t.fontSize = size * 10f;
        t.characterSpacing = spacing;
        t.color = color;
        t.fontStyle = logo ? FontStyles.Bold : FontStyles.Normal;
        t.rectTransform.anchoredPosition3D = new Vector3(pos.x, pos.y, 0f);
        t.GetComponent<MeshRenderer>().sortingOrder = order;
        t.ForceMeshUpdate(true, true);
    }

    private TextMeshPro Next()
    {
        if (used >= texts.Count)
        {
            GameObject go = new GameObject("PVText") { layer = Layer };
            go.transform.SetParent(root.transform, false);
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(PVRenderer.Width, 400f);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            texts.Add(t);
        }
        TextMeshPro next = texts[used++];
        next.gameObject.SetActive(true);
        return next;
    }

    // ───────── 描画 ─────────

    public Texture2D Render(Camera sceneCamera)
    {
        Submit(sceneCamera, sceneTarget);
        Submit(camera, finalTarget);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = finalTarget;
        frame.ReadPixels(new Rect(0, 0, PVRenderer.Width, PVRenderer.Height), 0, 0);
        frame.Apply(false);
        RenderTexture.active = previous;
        return frame;
    }

    private static void Submit(Camera cam, RenderTexture target)
    {
        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(cam, request)) throw new Exception("この描画設定では、画面を書き出せません。");
        RenderPipeline.SubmitRenderRequest(cam, request);
    }

    public void Dispose()
    {
        if (root != null) Object.DestroyImmediate(root);
        foreach (Object o in owned)
            if (o != null) Object.DestroyImmediate(o);
        sceneTarget.Release();
        finalTarget.Release();
        Object.DestroyImmediate(sceneTarget);
        Object.DestroyImmediate(finalTarget);
        Object.DestroyImmediate(frame);
    }
}
