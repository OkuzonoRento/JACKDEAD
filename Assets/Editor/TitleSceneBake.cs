using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// タイトルシーンのメニュー配置を、シーンのデータとして保存する（実行時に動かして見た目が変わらないようにする）。
/// ・MULTIPLAYER のボタンを「HOST GAME」と「JOIN GAME」の2つに分ける（クリックは Title3DSystem に登録）
/// ・項目とロゴを TitleMenuLayout の定数どおりに並べる
/// 未反映（MULTIPLAYER が残っている）のときは、スクリプト再読込のあとに自動で一度だけ行う。
/// 配置を調整したいときは TitleMenuLayout.cs の定数を変えて、メニュー JackDead/Title/メニュー配置をシーンに反映 を実行する。
/// </summary>
[InitializeOnLoad]
public static class TitleSceneBake
{
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    static TitleSceneBake()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Title/メニュー配置をシーンに反映")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(TitleScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[TitleSceneBake] TitleScene に未保存の変更があります。保存してから再実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);

        try
        {
            CasinoButtonEffect[] all = FindAll(scene);
            CasinoButtonEffect multi = Find(all, "MULTI");

            bool needsLocalize = LocalizeTexts(scene, false) > 0;
            if (multi == null && !needsLocalize && !force) return; // すでに反映済み

            if (multi != null)
            {
                SplitMulti(scene, multi);
                all = FindAll(scene);
            }

            TitleMenuLayout.Arrange(all);
            LocalizeTexts(scene, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TitleSceneBake] タイトルのメニュー配置をシーンに保存しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // 英語の元の文字 → 日本語。一致した文字に LocalizedText を付けて、シーンの文字を日本語にする（言語設定で英語に切り替わる）
    private static readonly System.Collections.Generic.Dictionary<string, string> Japanese =
        new System.Collections.Generic.Dictionary<string, string>
        {
            { "HOST GAME", "ルームを作る" },
            { "JOIN GAME", "ルームに参加" },
            { "SINGLE PLAY", "ソロプレイ" },
            { "SETTING", "設定" },
            { "SETTINGS", "設定" },
            { "QUIT", "ゲーム終了" },
            { "BACK", "戻る" },
            { "DEL", "削除" },
            { "HOVER OVER AN OPTION TO VIEW DETAILS", "項目にカーソルを合わせると、説明が表示されます" },
        };

    /// <summary>日本語化していない文字に LocalizedText を付ける。apply が false のときは、付ける必要がある数だけを返す</summary>
    private static int LocalizeTexts(Scene scene, bool apply)
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMPro.TextMeshProUGUI t in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (t.GetComponent<LocalizedText>() != null) continue;
                string key = t.text != null ? t.text.Trim() : "";
                if (!Japanese.TryGetValue(key, out string ja)) continue;
                count++;
                if (!apply) continue;

                LocalizedText lt = t.gameObject.AddComponent<LocalizedText>();
                lt.japanese = ja;
                lt.english = key;
                t.text = ja;
                EditorUtility.SetDirty(t);
                EditorUtility.SetDirty(lt);
            }
        }
        return count;
    }

    private static CasinoButtonEffect[] FindAll(Scene scene)
    {
        var list = new System.Collections.Generic.List<CasinoButtonEffect>();
        foreach (GameObject root in scene.GetRootGameObjects())
            list.AddRange(root.GetComponentsInChildren<CasinoButtonEffect>(true));
        return list.ToArray();
    }

    private static CasinoButtonEffect Find(CasinoButtonEffect[] all, string keyword)
    {
        foreach (CasinoButtonEffect e in all)
            if (e.name.ToUpperInvariant().Contains(keyword)) return e;
        return null;
    }

    private static void SplitMulti(Scene scene, CasinoButtonEffect multi)
    {
        Title3DSystem sys = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            sys = root.GetComponentInChildren<Title3DSystem>(true);
            if (sys != null) break;
        }
        if (sys == null)
        {
            Debug.LogWarning("[TitleSceneBake] Title3DSystem が見つかりません。");
            return;
        }

        GameObject joinGo = Object.Instantiate(multi.gameObject, multi.transform.parent);
        Undo.RegisterCreatedObjectUndo(joinGo, "Create JOIN button");
        CasinoButtonEffect host = multi;
        CasinoButtonEffect join = joinGo.GetComponent<CasinoButtonEffect>();
        host.name = "HOST";
        join.name = "JOIN";

        Setup(host, "HOST GAME", sys.OnClickMultiHost);
        Setup(join, "JOIN GAME", sys.OnClickMultiJoin);
    }

    private static void Setup(CasinoButtonEffect e, string label, UnityAction onClick)
    {
        if (e.buttonText != null)
        {
            e.buttonText.text = label;
            EditorUtility.SetDirty(e.buttonText);
        }

        Button b = e.GetComponent<Button>();
        if (b != null)
        {
            // 以前の登録（OnClickMultiMode）を外して、HOST / JOIN 用の処理だけにする
            while (b.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(b.onClick, 0);
            UnityEventTools.AddPersistentListener(b.onClick, onClick);
            EditorUtility.SetDirty(b);
        }
        EditorUtility.SetDirty(e);
    }
}
