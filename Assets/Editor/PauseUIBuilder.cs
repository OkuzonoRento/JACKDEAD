using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ゲーム中の Esc メニュー（ポーズ）の画面を、ゲームシーンの Hierarchy に実物として作って保存する（以前は OnGUI で毎フレーム描いていた）。
/// 見た目はタイトルのロビーUI（設定など）と同じ。言語の設定はタイトルだけなので、ここには置かない。
/// 置き場所: PlayerStatusUI の Canvas の子（Canvas は増やさない）。表示順は、子の Canvas の「Override Sorting」で最前面にする。
/// シーンに PauseMenu が無いときだけ、スクリプト再読込のあとに自動で一度行う。作り直すときは メニュー JackDead/Game/ポーズUIをシーンに作成。
/// </summary>
[InitializeOnLoad]
public static class PauseUIBuilder
{
    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";

    static PauseUIBuilder()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Game/ポーズUIをシーンに作成")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(GameScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[PauseUIBuilder] SampleScene に未保存の変更があります。保存してから、もう一度実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);

        try
        {
            PauseMenu existing = null;
            Canvas hud = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (existing == null) existing = root.GetComponentInChildren<PauseMenu>(true);
                if (hud == null)
                    foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                        if (c.name == "PlayerStatusUI") { hud = c; break; }
            }
            if (existing != null && !force) return;
            if (hud == null)
            {
                Debug.LogWarning("[PauseUIBuilder] PlayerStatusUI の Canvas が見つかりません。");
                return;
            }
            if (existing != null)
            {
                if (existing.ui != null) Object.DestroyImmediate(existing.ui);
                Object.DestroyImmediate(existing.gameObject);
            }

            // 1. Esc を受けるオブジェクト（UI の外に置く。HUD を隠しても Esc が効く）
            GameObject menuGo = new GameObject("PauseMenu");
            SceneManager.MoveGameObjectToScene(menuGo, scene);
            PauseMenu menu = menuGo.AddComponent<PauseMenu>();

            // 2. 画面（HUD の Canvas の子。最前面に出すため、子の Canvas で並び順を上書きする）
            GameObject ui = new GameObject("PauseUI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            ui.transform.SetParent(hud.transform, false);
            LobbyUIBuilder.Stretch((RectTransform)ui.transform);
            Canvas canvas = ui.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;

            Image dim = LobbyUIBuilder.NewImage(ui.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            LobbyUIBuilder.Stretch(dim.rectTransform);

            Image panel = LobbyUIBuilder.NewImage(ui.transform, "Panel", new Color(0.03f, 0.03f, 0.05f, 0.88f));
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = new Vector2(0f, 0f);
            pr.anchorMax = new Vector2(0f, 1f);
            pr.pivot = new Vector2(0f, 0.5f);
            pr.sizeDelta = new Vector2(LobbyUIBuilder.PanelWidth, 0f);
            pr.anchoredPosition = Vector2.zero;

            Image edge = LobbyUIBuilder.NewImage(panel.transform, "Edge", LobbyUIBuilder.Accent);
            RectTransform er = edge.rectTransform;
            er.anchorMin = new Vector2(1f, 0f);
            er.anchorMax = new Vector2(1f, 1f);
            er.pivot = new Vector2(1f, 0.5f);
            er.sizeDelta = new Vector2(4f, 0f);
            er.anchoredPosition = Vector2.zero;

            GameObject c2 = new GameObject("Content", typeof(RectTransform));
            c2.transform.SetParent(panel.transform, false);
            LobbyUIBuilder.Stretch((RectTransform)c2.transform);

            BuildMain(LobbyUIBuilder.Group(c2.transform, "Main"));
            LobbyUIBuilder.BuildSettings(LobbyUIBuilder.Group(c2.transform, "Settings"), false); // 言語はタイトルだけ
            BuildConfirm(LobbyUIBuilder.Group(c2.transform, "ConfirmTitle"), "タイトルへ戻る", "BACK TO TITLE", true);
            BuildConfirm(LobbyUIBuilder.Group(c2.transform, "ConfirmQuit"), "ゲームを終了", "QUIT GAME", false);

            LobbyUIBuilder.SetLayerRecursive(ui, 5);
            ui.SetActive(false); // 開くときに PauseMenu が表示する
            menu.ui = ui;

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PauseUIBuilder] ポーズ画面をゲームシーンに作成しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void BuildMain(Transform g)
    {
        LobbyUIBuilder.MakeText(g, "Title", "ポーズ", "PAUSE", 72f, LobbyUIBuilder.Accent, LobbyUIBuilder.LeftMargin, 70f, 900f, 90f, FontStyles.Bold);
        LobbyUIBuilder.MakeButton(g, "Resume", "再開", "RESUME", 250f);
        LobbyUIBuilder.MakeButton(g, "Settings", "設定", "SETTINGS", 322f);
        LobbyUIBuilder.MakeButton(g, "ToTitle", "タイトルへ戻る", "BACK TO TITLE", 394f);
        LobbyUIBuilder.MakeButton(g, "Quit", "ゲームを終了", "QUIT GAME", 466f);
        LobbyUIBuilder.MakeText(g, "MultiNote", "マルチプレイ中のため、ゲームは止まりません。", "THE GAME KEEPS RUNNING DURING MULTIPLAYER.",
            30f, LobbyUIBuilder.TextDim, LobbyUIBuilder.LeftMargin, 580f, 900f, 44f);
    }

    private static void BuildConfirm(Transform g, string titleJa, string titleEn, bool leaveRoomNote)
    {
        LobbyUIBuilder.MakeText(g, "Title", titleJa, titleEn, 72f, LobbyUIBuilder.Accent, LobbyUIBuilder.LeftMargin, 70f, 900f, 90f, FontStyles.Bold);
        LobbyUIBuilder.MakeText(g, "Message1", "セーブされるのはステージクリア時のみです。", "THE GAME IS SAVED ONLY WHEN YOU CLEAR A STAGE.",
            40f, Color.white, LobbyUIBuilder.LeftMargin, 240f, 900f, 56f);
        LobbyUIBuilder.MakeText(g, "Message2", "今のステージの進行は失われます。", "PROGRESS IN THE CURRENT STAGE WILL BE LOST.",
            40f, LobbyUIBuilder.ErrorColor, LobbyUIBuilder.LeftMargin, 304f, 900f, 56f);
        if (leaveRoomNote)
            LobbyUIBuilder.MakeText(g, "LeaveNote", "部屋からも退出します。", "YOU WILL ALSO LEAVE THE ROOM.",
                32f, LobbyUIBuilder.TextDim, LobbyUIBuilder.LeftMargin, 380f, 900f, 44f);
        LobbyUIBuilder.MakeButton(g, "Confirm", titleJa, titleEn, 520f, 46f, 600f);
        LobbyUIBuilder.MakeButton(g, "Cancel", "キャンセル", "CANCEL", 600f, 46f, 600f);
    }
}
