using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// ゲーム内で使うフォントの TMP フォントアセットを Assets/Fonts に用意する。
/// ・WDXL Lubrifont JP N … 標準フォント（TMP Settings の既定）。日本語も出せるよう Dynamic（必要な字を実行時に生成）
/// ・DotGothic16 … 既存の JackDeadFonts（ゲーム内用・字数限定）に、足りない字を補う Dynamic 版を fallback として足す
/// Unity の起動・スクリプト再読込のたびに不足があれば自動で作る。メニュー JackDead/Fonts/フォントを再セットアップ からも実行できる。
/// </summary>
[InitializeOnLoad]
public static class FontSetup
{
    private const string Dir = "Assets/Fonts";
    private const string LubriTtf = Dir + "/WDXLLubrifontJPN-Regular.ttf";
    private const string LubriAsset = Dir + "/WDXLLubrifontJPN-Regular SDF.asset";
    private const string DotTtf = Dir + "/DotGothic16-Regular.ttf";
    private const string DotDynamicAsset = Dir + "/DotGothic16 Dynamic SDF.asset";
    private const string JackDeadAsset = Dir + "/JackDeadFonts.asset";

    static FontSetup()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Fonts/フォントを再セットアップ")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool verbose)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        TMP_FontAsset dot = EnsureDynamic(DotTtf, DotDynamicAsset);
        TMP_FontAsset lubri = EnsureDynamic(LubriTtf, LubriAsset);
        if (lubri == null)
        {
            if (verbose) Debug.LogWarning("[FontSetup] " + LubriTtf + " が見つかりません。");
            return;
        }

        bool changed = false;

        // Lubrifont の足りない字は DotGothic で補う
        changed |= AddFallback(lubri, dot);

        // 既存の JackDeadFonts（ゲーム内で使用中）にも補う字を足す
        TMP_FontAsset jack = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(JackDeadAsset);
        changed |= AddFallback(jack, dot);

        // TMP の既定フォントを Lubrifont にする（実行時に作られる文字や、タイトルの文字に使う）
        TMP_Settings settings = TMP_Settings.instance;
        if (settings != null && TMP_Settings.defaultFontAsset != lubri)
        {
            SerializedObject so = new SerializedObject(settings);
            SerializedProperty p = so.FindProperty("m_defaultFontAsset");
            if (p != null)
            {
                p.objectReferenceValue = lubri;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                changed = true;
            }
        }

        // タイトルシーンの文字を、シーンのデータとして Lubrifont に置き換える（実行時の差し替えではなく、保存済みの設定を直す）
        changed |= ReplaceFontsInScene(TitleScenePath, lubri, verbose);

        if (changed)
        {
            AssetDatabase.SaveAssets();
            if (verbose) Debug.Log("[FontSetup] フォントを設定しました。");
        }
        else if (verbose) Debug.Log("[FontSetup] すでに設定済みです。");
    }

    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    /// <summary>シーン内の TMP テキストのうち、古いフォント（Cinzel / LiberationSans）のものを main に置き換えて保存する</summary>
    private static bool ReplaceFontsInScene(string scenePath, TMP_FontAsset main, bool verbose)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return false;

        UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            // 保存していない変更があるときは、勝手に保存しない
            if (verbose) Debug.LogWarning("[FontSetup] TitleScene に未保存の変更があるため、フォントの置き換えを後回しにしました。保存してから再実行してください。");
            return false;
        }
        if (!wasLoaded)
            scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == null || t.font == main) continue;
                string n = t.font.name;
                if (!n.Contains("Cinzel") && !n.Contains("Liberation")) continue;
                t.font = main;
                t.fontSharedMaterial = main.material;
                EditorUtility.SetDirty(t);
                count++;
            }
        }

        if (count > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[FontSetup] TitleScene の文字 " + count + " 個を Lubrifont に置き換えて保存しました。");
        }
        if (!wasLoaded) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        return count > 0;
    }

    private static bool AddFallback(TMP_FontAsset target, TMP_FontAsset fallback)
    {
        if (target == null || fallback == null || target == fallback) return false;
        if (target.fallbackFontAssetTable == null) target.fallbackFontAssetTable = new List<TMP_FontAsset>();
        if (target.fallbackFontAssetTable.Contains(fallback)) return false;
        target.fallbackFontAssetTable.Add(fallback);
        EditorUtility.SetDirty(target);
        return true;
    }

    /// <summary>ttf から Dynamic の TMP フォントアセットを作る（すでにあればそれを返す）</summary>
    private static TMP_FontAsset EnsureDynamic(string ttfPath, string assetPath)
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null) return null;

        TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(
            font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (fa == null) return null;

        string baseName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        fa.name = baseName;
        AssetDatabase.CreateAsset(fa, assetPath);

        if (fa.material != null)
        {
            fa.material.name = baseName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
        }
        if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
        {
            fa.atlasTextures[0].name = baseName + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        }

        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath);
        Debug.Log("[FontSetup] TMP フォントアセットを作成しました: " + assetPath);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
    }
}
