using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 設定画面の「キー設定」タブの部品（タブのボタン・入力機器の行・操作ごとの行・初期設定に戻すボタン）を、
/// ゲームを実行する前から、シーンの Hierarchy に置いておく。実行時は KeyBindingsTab が、置いてある部品に動作をつなぐだけ。
/// 置き場所: タイトルシーンの LobbyUI の Settings と、ゲームシーンの PauseUI の Settings（どちらも同じ作り）。
/// ・まだ部品が無いシーンには、スクリプト再読込のあとに自動で一度だけ足す（すでにあるときは何もしない）
/// ・作り直したいときは、メニュー JackDead/キー設定UIをシーンに作成（今のものを置き換える）
/// ・LobbyUIBuilder / PauseUIBuilder でUIを作り直したときも、同じ部品が一緒に作られる（BuildSettings の最後に呼ばれる）
/// 操作の一覧（行の数と名前）は KeyBindings.Entries と同じ。操作を増やしたら、メニューから作り直す。
/// </summary>
[InitializeOnLoad]
public static class KeyBindingsUIBuilder
{
    private const float Left = LobbyUIBuilder.LeftMargin;
    private const float RowW = LobbyUIBuilder.RowWidth;
    private const float RowH = 50f;
    private const float RowSpacing = 6f;
    private const float DeviceRowTop = 196f;
    private const float ListTop = 270f;
    private const float ListHeight = 520f;

    static KeyBindingsUIBuilder()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/キー設定UIをシーンに作成")]
    private static void RunFromMenu() { Run(true); }

    // ───────── シーンへの追加 ─────────

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        ProcessScene(LobbyUIBuilder.TitleScenePath, scene =>
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MultiLobbyUI lobby = root.GetComponentInChildren<MultiLobbyUI>(true);
                if (lobby != null) return lobby.transform.Find("Panel/Content/Settings");
            }
            return null;
        }, force);

        ProcessScene("Assets/Scenes/SampleScene.unity", scene =>
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PauseMenu pause = root.GetComponentInChildren<PauseMenu>(true);
                if (pause != null && pause.ui != null) return pause.ui.transform.Find("Panel/Content/Settings");
            }
            return null;
        }, force);
    }

    private static void ProcessScene(string scenePath, System.Func<Scene, Transform> findSettings, bool force)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[KeyBindingsUIBuilder] " + scenePath + " に未保存の変更があります。保存してから、もう一度実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        try
        {
            Transform settings = findSettings(scene);
            if (settings == null) return; // UI がまだ無い（LobbyUIBuilder / PauseUIBuilder が作るときに、一緒に作られる）

            if (!force && IsComplete(settings)) return;

            Remove(settings);
            Build(settings);
            LobbyUIBuilder.SetLayerRecursive(settings.gameObject, 5);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[KeyBindingsUIBuilder] キー設定の部品を " + System.IO.Path.GetFileName(scenePath) + " に置きました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>部品が揃っているか（タブ・パネル・全部の行）</summary>
    private static bool IsComplete(Transform settings)
    {
        if (settings.Find("Tab_General") == null || settings.Find("Tab_Controls") == null) return false;
        Transform content = settings.Find("KeyBindPanel/List/Viewport/Content");
        if (content == null || settings.Find("KeyBindPanel/Row_Device") == null || settings.Find("KeyBindPanel/Reset") == null) return false;

        foreach (KeyBindings.Entry e in KeyBindings.Entries)
            if (content.Find(RowName(e)) == null) return false;
        return true;
    }

    private static void Remove(Transform settings)
    {
        foreach (string name in new[] { "Tab_General", "Tab_Controls", "KeyBindPanel" })
        {
            Transform t = settings.Find(name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
    }

    internal static string RowName(KeyBindings.Entry e)
    {
        return "Row_" + e.action + (e.part ?? "");
    }

    // ───────── 部品を作る ─────────

    /// <summary>設定のグループ（Settings）に、タブのボタンと、キー設定のパネルを作る（LobbyUIBuilder.BuildSettings からも呼ばれる）</summary>
    internal static void Build(Transform g)
    {
        // タブのボタン（タイトルの右）。選んでいる方を白く、もう一方を暗く表示する色は、実行時に KeyBindingsTab が付ける
        MakeTab(g, "Tab_General", "一般", "GENERAL", 600f, 96f, 150f, 36f);
        MakeTab(g, "Tab_Controls", "キー設定", "KEY BINDINGS", 770f, 96f, 270f, 36f);

        // キー設定のパネル（「キー設定」タブのときだけ表示する。実行時に切り替える）
        GameObject panelGo = new GameObject("KeyBindPanel", typeof(RectTransform));
        panelGo.transform.SetParent(g, false);
        RectTransform panel = (RectTransform)panelGo.transform;
        LobbyUIBuilder.Stretch(panel);

        BuildDeviceRow(panel);
        BuildList(panel);

        LobbyUIBuilder.MakeText(panel, "PadNote", "", null, 28f, LobbyUIBuilder.TextDim, Left, ListTop + ListHeight + 6f, RowW, 40f);
        MakeTab(panel, "Reset", "初期設定に戻す", "RESET TO DEFAULT", Left, 860f, 560f, 36f);

        panelGo.SetActive(false);
    }

    /// <summary>文字だけのボタン（見えない Image でクリックを受ける）。中の文字は「Label」</summary>
    private static void MakeTab(Transform parent, string name, string ja, string en, float x, float y, float w, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        LobbyUIBuilder.TopLeft((RectTransform)go.transform, x, y, w, size * 1.3f);
        Image img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;

        TextMeshProUGUI t = LobbyUIBuilder.MakeText(go.transform, "Label", ja, en, size, LobbyUIBuilder.TextDim, 0f, 0f, w, size * 1.3f);
        LobbyUIBuilder.Stretch((RectTransform)t.transform);
    }

    /// <summary>一番上の「入力機器」の行。言語の設定と同じ形（ラベル / 値 / 「&lt;」「&gt;」）</summary>
    private static void BuildDeviceRow(Transform panel)
    {
        Image bg = LobbyUIBuilder.NewImage(panel, "Row_Device", LobbyUIBuilder.RowNormal);
        bg.raycastTarget = false;
        LobbyUIBuilder.TopLeft(bg.rectTransform, Left, DeviceRowTop, RowW, 56f);

        LobbyUIBuilder.MakeText(bg.transform, "Label", "入力機器", "INPUT DEVICE", 32f, LobbyUIBuilder.TextNormal, 20f, 0f, 340f, 56f);
        LobbyUIBuilder.MakeText(bg.transform, "Value", "キーボード・マウス", null, 32f, Color.white, 450f, 0f, 340f, 56f,
            FontStyles.Normal, TextAlignmentOptions.Midline);
        MakeArrow(bg.transform, "Prev", "<", 380f);
        MakeArrow(bg.transform, "Next", ">", 790f);
    }

    private static void MakeArrow(Transform parent, string name, string text, float x)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        LobbyUIBuilder.TopLeft((RectTransform)go.transform, x, 0f, 60f, 56f);
        Image img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);
        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;

        TextMeshProUGUI t = LobbyUIBuilder.MakeText(go.transform, "Label", text, null, 36f, Color.white, 0f, 0f, 60f, 56f,
            FontStyles.Normal, TextAlignmentOptions.Midline);
        LobbyUIBuilder.Stretch((RectTransform)t.transform);
    }

    /// <summary>操作ごとの行の一覧。多いので、スクロールできる枠（ScrollRect）に入れる。行は縦に自動で並ぶ（隠した行は詰まる）</summary>
    private static void BuildList(Transform panel)
    {
        GameObject scrollGo = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(panel, false);
        LobbyUIBuilder.TopLeft((RectTransform)scrollGo.transform, Left, ListTop, RowW + 20f, ListHeight);
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // 見えないが、ホイール・ドラッグは受ける

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        LobbyUIBuilder.Stretch((RectTransform)viewport.transform);

        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        RectTransform content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = RowSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 20, 0, 0); // 右の20は、行の幅を RowW にそろえる分

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        sr.viewport = (RectTransform)viewport.transform;
        sr.content = content;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;

        foreach (KeyBindings.Entry e in KeyBindings.Entries) BuildRow(content, e);
    }

    private static void BuildRow(Transform parent, KeyBindings.Entry e)
    {
        Image bg = LobbyUIBuilder.NewImage(parent, RowName(e), LobbyUIBuilder.RowNormal);
        bg.raycastTarget = false;
        LayoutElement le = bg.gameObject.AddComponent<LayoutElement>();
        le.minHeight = RowH;
        le.preferredHeight = RowH;

        LobbyUIBuilder.MakeText(bg.transform, "Label", e.ja, e.en, 32f, LobbyUIBuilder.TextNormal, 20f, 0f, 460f, RowH);

        // キー表示のボタン（押すと入力待ちになる）。中の文字は「Text」
        GameObject keyGo = new GameObject("Key", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        keyGo.transform.SetParent(bg.transform, false);
        LobbyUIBuilder.TopLeft((RectTransform)keyGo.transform, 520f, 5f, 320f, RowH - 10f);
        Image keyBg = keyGo.GetComponent<Image>();
        keyBg.color = new Color(1f, 1f, 1f, 0.14f);

        Button btn = keyGo.GetComponent<Button>();
        btn.targetGraphic = keyBg;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.55f, 0.55f, 1f);
        cb.pressedColor = new Color(1f, 0.3f, 0.3f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        LobbyUIBuilder.MakeText(keyGo.transform, "Text", "", null, 30f, Color.white, 0f, 0f, 320f, RowH - 10f,
            FontStyles.Normal, TextAlignmentOptions.Midline);
    }
}
