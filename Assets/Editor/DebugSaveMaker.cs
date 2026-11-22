using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// デバッグ用のセーブデータを作る。レベル5から・レベル10から・レベル20からを、スロット1・2・3に書き込む（所持金は 10,000,000）。
/// タイトルのソロ／ホストのセーブ選択から、そのまま選んで始められる。借金の返済額は、そのレベルより前のノルマの合計にそろえる。
/// 注意: スロット1〜3の今のセーブデータは上書きされる。
/// </summary>
public static class DebugSaveMaker
{
    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    private const int DebugMoney = 10000000;

    private static readonly int[] Levels = { 5, 10, 20 };

    private static string SavePath(int slot)
    {
        return Path.Combine(Application.persistentDataPath, $"save_slot{slot}.json");
    }

    [MenuItem("JackDead/Debug/デバッグ用セーブを作成（Lv5・Lv10・Lv20 / スロット1〜3）")]
    private static void CreateSaves()
    {
        bool anyExists = false;
        for (int slot = 1; slot <= Levels.Length; slot++)
            if (File.Exists(SavePath(slot))) anyExists = true;

        if (anyExists && !EditorUtility.DisplayDialog("デバッグ用セーブを作成",
            "スロット1〜3の、今のセーブデータを上書きします。よろしいですか？\n（スロット1: レベル5 / スロット2: レベル10 / スロット3: レベル20、所持金 $10,000,000）",
            "上書きする", "キャンセル"))
            return;

        MapGenerator map = FindMapGenerator(out Scene openedScene, out bool openedHere);
        if (map == null)
        {
            Debug.LogError("[DebugSaveMaker] MapGenerator が見つかりません（SampleScene を確認してください）。");
            return;
        }

        try
        {
            int totalDebt = map.TotalDebt;
            for (int i = 0; i < Levels.Length; i++)
            {
                int level = Levels[i];
                int repaid = 0;
                for (int l = 1; l < level; l++) repaid += map.GetBaseQuota(l);

                GameSaveData data = new GameSaveData
                {
                    hasSaveData = true,
                    currentMoney = DebugMoney,
                    currentDay = level,
                    totalRepaid = repaid,
                    remainingDebt = Mathf.Max(0, totalDebt - repaid)
                };
                File.WriteAllText(SavePath(i + 1), JsonUtility.ToJson(data, true));
                Debug.Log($"[DebugSaveMaker] スロット{i + 1}: レベル{level} / 所持金 ${DebugMoney:N0} / 返済済み ${repaid:N0}");
            }
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(openedScene, true);
        }
        EditorUtility.DisplayDialog("デバッグ用セーブを作成", "作成しました。\nスロット1: レベル5 / スロット2: レベル10 / スロット3: レベル20\n所持金: $10,000,000", "OK");
    }

    [MenuItem("JackDead/Debug/セーブデータのフォルダを開く")]
    private static void OpenSaveFolder()
    {
        EditorUtility.RevealInFinder(Application.persistentDataPath);
    }

    /// <summary>ノルマの計算に、シーンの MapGenerator の設定を使う。開いていなければ、SampleScene を読み込み専用のように一時的に開く</summary>
    private static MapGenerator FindMapGenerator(out Scene scene, out bool openedHere)
    {
        scene = default;
        openedHere = false;

        MapGenerator found = Object.FindFirstObjectByType<MapGenerator>();
        if (found != null) return found;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null) return null;
        scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
        openedHere = true;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            found = root.GetComponentInChildren<MapGenerator>(true);
            if (found != null) return found;
        }
        return null;
    }
}