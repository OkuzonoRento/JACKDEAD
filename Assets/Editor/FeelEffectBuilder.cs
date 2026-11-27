using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ハイのオーラと音の部品を、シーンの Hierarchy に実物として作って保存する。
/// ・ゲームシーン：PlayerStatusUI の Canvas の子「HighAura」（HUD の後ろ）と、ルートの「GameAudio」（効果音・曲の再生口）
/// ・タイトルシーン：ルートの「GameAudio」
/// シーンに無いとき、スクリプト再読込のあとに自動で一度行う。作り直すときは メニュー JackDead/Game/ハイのオーラと音をシーンに作成。
/// </summary>
[InitializeOnLoad]
public static class FeelEffectBuilder
{
    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    static FeelEffectBuilder()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Game/ハイのオーラと音をシーンに作成")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Build(GameScenePath, GameMusic.Mode.Game, force);
        Build(TitleScenePath, GameMusic.Mode.Title, force);
    }

    private static void Build(string path, GameMusic.Mode mode, bool force)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(path);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            Debug.LogWarning($"[FeelEffectBuilder] {scene.name} に未保存の変更があります。保存してから、メニュー JackDead/Game/ハイのオーラと音をシーンに作成 を実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            GameAudio audio = null;
            HighAura aura = null;
            Canvas hud = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (audio == null) audio = root.GetComponentInChildren<GameAudio>(true);
                if (aura == null) aura = root.GetComponentInChildren<HighAura>(true);
                if (hud == null)
                    foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                        if (c.name == "PlayerStatusUI") { hud = c; break; }
            }

            bool changed = false;
            if (audio == null || force)
            {
                if (audio != null) Object.DestroyImmediate(audio.gameObject);
                BuildAudio(scene, mode);
                changed = true;
            }
            if (mode == GameMusic.Mode.Game && (aura == null || force))
            {
                if (hud == null) Debug.LogWarning("[FeelEffectBuilder] PlayerStatusUI の Canvas が見つかりません。");
                else
                {
                    if (aura != null) Object.DestroyImmediate(aura.gameObject);
                    BuildAura(hud);
                    changed = true;
                }
            }
            if (!changed) return;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FeelEffectBuilder] {scene.name} に、音（GameAudio）{(mode == GameMusic.Mode.Game ? "とハイのオーラ（HighAura）" : "")}を作成しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BuildAudio(Scene scene, GameMusic.Mode mode)
    {
        GameObject root = new GameObject("GameAudio");
        SceneManager.MoveGameObjectToScene(root, scene);
        GameAudio audio = root.AddComponent<GameAudio>();
        GameMusic music = root.AddComponent<GameMusic>();
        music.mode = mode;

        Transform musicGroup = Child(root.transform, "Music");
        if (mode == GameMusic.Mode.Title)
        {
            music.titleSource = Source(musicGroup, "Music_Title", true, 0f);
        }
        else
        {
            music.ambienceSource = Source(musicGroup, "Music_Ambience", true, 0f);
            music.chaseSource = Source(musicGroup, "Music_Chase", true, 0f);
            music.heartbeatSource = Source(musicGroup, "Music_Heartbeat", true, 0f);
        }

        Transform flatGroup = Child(root.transform, "Effects2D");
        List<AudioSource> flat = new List<AudioSource>();
        for (int i = 1; i <= 6; i++) flat.Add(Source(flatGroup, $"Flat_{i:00}", false, 0f));
        audio.flatSources = flat.ToArray();

        Transform spatialGroup = Child(root.transform, "Effects3D");
        List<AudioSource> spatial = new List<AudioSource>();
        for (int i = 1; i <= 20; i++) spatial.Add(Source(spatialGroup, $"Spatial_{i:00}", false, 1f));
        audio.spatialSources = spatial.ToArray();

        EditorUtility.SetDirty(audio);
        EditorUtility.SetDirty(music);
    }

    private static Transform Child(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static AudioSource Source(Transform parent, string name, bool loop, float spatial)
    {
        AudioSource source = Child(parent, name).gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = spatial;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = 20f;
        source.dopplerLevel = 0f;
        return source;
    }

    private static void BuildAura(Canvas hud)
    {
        GameObject go = new GameObject("HighAura", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(hud.transform, false);
        go.transform.SetAsFirstSibling(); // HUD の文字やゲージより後ろ
        LobbyUIBuilder.Stretch((RectTransform)go.transform);
        LobbyUIBuilder.SetLayerRecursive(go, 5);

        RawImage image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false; // 出すときに、HighAura が有効にする

        HighAura aura = go.AddComponent<HighAura>();
        aura.image = image;
        EditorUtility.SetDirty(aura);
    }
}
