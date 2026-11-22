using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// タイトルシーンに残っている、使わなくなった古い UI（SaveSlotPanel / SettingsPanel と、設定の古いスクリプト）を取り除く。
/// 新しい画面は MultiLobbyUI が出すので不要。残っているときだけ、スクリプト再読込のあとに一度行う。
/// </summary>
[InitializeOnLoad]
public static class TitleSceneCleanup
{
    private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
    private static readonly string[] OldObjects = { "SaveSlotPanel", "SettingsPanel" };

    static TitleSceneCleanup()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Title/古いUIをシーンから削除")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(TitleScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[TitleSceneCleanup] TitleScene に未保存の変更があります。保存してから再実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);

        try
        {
            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (System.Array.IndexOf(OldObjects, t.name) < 0) continue;
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
                // 古い設定スクリプト（SettingsManager）は、もう部品としては使わないので外す
                foreach (SettingsManager sm in root.GetComponentsInChildren<SettingsManager>(true))
                {
                    Object.DestroyImmediate(sm);
                    removed++;
                }
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }

            if (removed == 0) return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TitleSceneCleanup] 古いUI／スクリプト " + removed + " 件をタイトルシーンから削除しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
