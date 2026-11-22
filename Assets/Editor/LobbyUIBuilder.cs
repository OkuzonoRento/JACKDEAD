using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// タイトルシーンに「LobbyUI」（ソロ / マルチ / 設定の画面の部品）を、Hierarchy の実物として作って保存する。
/// 以前はコードで再生中に作っていた画面を、そのままの配置でシーンに置く。ボタンの動作や文字の書き換えは MultiLobbyUI がつなぐ。
/// シーンに LobbyUI が無いときだけ、スクリプト再読込のあとに自動で一度行う。
/// 作り直したいときは、メニュー JackDead/Title/ロビーUIをシーンに作成 を実行する（今の LobbyUI を置き換える）。
/// 配置の数値（x, y, 幅, 高さ）は、以前のコード版と同じ。
/// </summary>
[InitializeOnLoad]
public static class LobbyUIBuilder
{
    internal const string TitleScenePath = "Assets/Scenes/TitleScene.unity";

    internal static readonly Color TextNormal = new Color(0.8f, 0.8f, 0.8f, 1f);
    internal static readonly Color TextDim = new Color(0.55f, 0.55f, 0.58f, 1f);
    internal static readonly Color Accent = new Color(0.85f, 0.12f, 0.12f, 1f);
    internal static readonly Color ErrorColor = new Color(1f, 0.4f, 0.4f, 1f);
    internal static readonly Color RowNormal = new Color(1f, 1f, 1f, 0.06f);
    internal static readonly Color RowHover = new Color(1f, 1f, 1f, 0.16f);

    internal const float LeftMargin = 140f;
    internal const float PanelWidth = 1100f;
    internal const float RowWidth = 860f;
    internal const float RowHeight = 56f;

    private static Title3DSystem titleSystem;

    static LobbyUIBuilder()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("JackDead/Title/ロビーUIをシーンに作成")]
    private static void RunFromMenu() { Run(true); }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath) == null) return;

        Scene scene = EditorSceneManager.GetSceneByPath(TitleScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (wasLoaded && scene.isDirty)
        {
            if (force) Debug.LogWarning("[LobbyUIBuilder] TitleScene に未保存の変更があります。Ctrl+S で保存してから、もう一度実行してください。");
            return;
        }
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Additive);

        try
        {
            // すでにある LobbyUI を探す（Canvas の下にあるものは作成済み。単独の Canvas を持つ古い形は作り直す）
            MultiLobbyUI existing = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                existing = root.GetComponentInChildren<MultiLobbyUI>(true);
                if (existing != null) break;
            }
            // 古い形（単独の Canvas を持つ）や、新しい部品（回線の強さの表示）が足りない形のときは、作り直す
            bool oldForm = existing != null && (existing.GetComponent<Canvas>() != null
                || existing.transform.Find("Panel/Content/Multi/Room/Member1/Ping") == null);
            if (existing != null && !force && !oldForm) return;
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            titleSystem = null;
            Canvas mainCanvas = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (titleSystem == null) titleSystem = root.GetComponentInChildren<Title3DSystem>(true);
                if (mainCanvas == null)
                    foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
                        if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { mainCanvas = c; break; }
            }
            if (mainCanvas == null)
            {
                Debug.LogWarning("[LobbyUIBuilder] タイトルシーンに Canvas が見つかりません。");
                return;
            }

            // Canvas は、シーンの Canvas を1つだけ使う。その子として置く（FadeOverlay より手前にならないよう、その直前へ）
            GameObject ui = Build();
            ui.transform.SetParent(mainCanvas.transform, false);
            Transform fade = mainCanvas.transform.Find("FadeOverlay");
            if (fade != null) ui.transform.SetSiblingIndex(fade.GetSiblingIndex());
            else ui.transform.SetAsLastSibling();
            ui.SetActive(false); // 開くときに MultiLobbyUI が表示する
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[LobbyUIBuilder] ロビーUI（ソロ / マルチ / 設定）をタイトルシーンに作成しました。");
        }
        finally
        {
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ───────── 全体 ─────────

    internal static GameObject Build()
    {
        GameObject root = new GameObject("LobbyUI", typeof(RectTransform));
        Stretch((RectTransform)root.transform); // Canvas は、シーンの Canvas を使う（新しく作らない）
        root.AddComponent<MultiLobbyUI>();
        root.layer = 5;

        // 画面全体を少し暗くする（下のタイトルのクリックも止める）
        Image dim = NewImage(root.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
        Stretch(dim.rectTransform);

        // 左側の暗いパネル
        Image panel = NewImage(root.transform, "Panel", new Color(0.03f, 0.03f, 0.05f, 0.88f));
        RectTransform pr = panel.rectTransform;
        pr.anchorMin = new Vector2(0f, 0f);
        pr.anchorMax = new Vector2(0f, 1f);
        pr.pivot = new Vector2(0f, 0.5f);
        pr.sizeDelta = new Vector2(PanelWidth, 0f);
        pr.anchoredPosition = Vector2.zero;

        // パネルの右端の赤い線
        Image edge = NewImage(panel.transform, "Edge", Accent);
        RectTransform er = edge.rectTransform;
        er.anchorMin = new Vector2(1f, 0f);
        er.anchorMax = new Vector2(1f, 1f);
        er.pivot = new Vector2(1f, 0.5f);
        er.sizeDelta = new Vector2(4f, 0f);
        er.anchoredPosition = Vector2.zero;

        // 文字を置く領域（パネルの左上が原点）
        GameObject c = new GameObject("Content", typeof(RectTransform));
        c.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)c.transform);

        BuildSettings(Group(c.transform, "Settings"));
        BuildSolo(Group(c.transform, "Solo"));
        BuildDeleteConfirm(Group(c.transform, "DeleteConfirm"));
        BuildMulti(Group(c.transform, "Multi"));

        SetLayerRecursive(root, 5);
        return root;
    }

    internal static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    // ───────── 設定 ─────────

    internal static void BuildSettings(Transform g, bool withLanguage = true)
    {
        MakeText(g, "Title", "設定", "SETTINGS", 72f, Accent, LeftMargin, 70f, 900f, 90f, FontStyles.Bold);

        string hintJa = "項目にカーソルを合わせると、説明が表示されます";
        string hintEn = "HOVER OVER AN OPTION TO VIEW DETAILS";
        TextMeshProUGUI desc = MakeText(g, "Description", hintJa, hintEn, 30f, TextDim, LeftMargin, 800f, 900f, 44f);
        desc.enableAutoSizing = true; // 長い説明も、1行におさめる
        desc.fontSizeMax = 30f;
        desc.fontSizeMin = 16f;

        float y = 190f;
        Section(g, "Section_Sound", "サウンド", "SOUND", y);
        y += 52f;
        SliderRow(g, "Row_Master", y, "全体音量", "MASTER VOLUME", "ゲーム全体の音量を調整します。", "ADJUST THE OVERALL VOLUME OF EVERYTHING.", 0f, 1f, desc, hintJa, hintEn);
        y += 62f;
        SliderRow(g, "Row_Bgm", y, "BGM 音量", "BGM VOLUME", "音楽の音量を調整します。", "ADJUST THE MUSIC VOLUME.", 0f, 1f, desc, hintJa, hintEn);
        y += 62f;
        SliderRow(g, "Row_Se", y, "SE 音量", "SE VOLUME", "効果音の音量を調整します。", "ADJUST THE SOUND EFFECTS VOLUME.", 0f, 1f, desc, hintJa, hintEn);
        y += 62f + 10f;

        Section(g, "Section_Controls", "操作・表示", "CONTROLS", y);
        y += 52f;
        SliderRow(g, "Row_Sensitivity", y, "マウス感度", "MOUSE SENSITIVITY", "マウスで視点を動かす速さを調整します。", "ADJUST HOW FAST THE CAMERA TURNS WITH THE MOUSE.", 0.2f, 3f, desc, hintJa, hintEn);
        y += 62f;
        SliderRow(g, "Row_Brightness", y, "画面の明るさ", "BRIGHTNESS", "画面の明るさを調整します。真ん中が標準です。", "ADJUST THE SCREEN BRIGHTNESS. THE MIDDLE IS THE DEFAULT.", 0f, 1f, desc, hintJa, hintEn);
        y += 62f;

        // 言語（「<」「>」で切り替える）
        if (withLanguage)
        {
        GameObject row = Row(g, "Row_Language", y, "表示する言語を選びます（日本語 / 英語）。", "CHOOSE THE DISPLAY LANGUAGE: JAPANESE OR ENGLISH.", desc, hintJa, hintEn);
        MakeText(row.transform, "Label", "言語", "LANGUAGE", 32f, TextNormal, 20f, 0f, 340f, RowHeight);
        MakeText(row.transform, "Value", "日本語", null, 32f, Color.white, 450f, 0f, 220f, RowHeight, FontStyles.Normal, TextAlignmentOptions.Midline);
        MakeButton(row.transform, "Prev", "<", null, 0f, 36f, 60f, 380f, true);
        MakeButton(row.transform, "Next", ">", null, 0f, 36f, 60f, 690f, true);
        }

        MakeButton(g, "Back", "戻る", "BACK", 920f, 40f, 400f);
    }

    internal static void Section(Transform g, string name, string ja, string en, float y)
    {
        Image band = NewImage(g, name, new Color(Accent.r, Accent.g, Accent.b, 0.22f));
        band.raycastTarget = false;
        TopLeft(band.rectTransform, LeftMargin, y, RowWidth, 40f);
        Image bar = NewImage(band.transform, "Bar", Accent);
        bar.raycastTarget = false;
        TopLeft(bar.rectTransform, 0f, 0f, 8f, 40f);
        MakeText(band.transform, "Label", ja, en, 34f, new Color(1f, 0.55f, 0.55f, 1f), 24f, 0f, 600f, 40f, FontStyles.Bold);
    }

    internal static GameObject Row(Transform g, string name, float y, string descJa, string descEn, TextMeshProUGUI desc, string hintJa, string hintEn)
    {
        Image bg = NewImage(g, name, RowNormal);
        bg.raycastTarget = false;
        TopLeft(bg.rectTransform, LeftMargin, y, RowWidth, RowHeight);
        UiHover h = bg.gameObject.AddComponent<UiHover>();
        h.bg = bg;
        h.normal = RowNormal;
        h.hover = RowHover;
        h.descTarget = desc;
        h.descJa = descJa;
        h.descEn = descEn;
        h.defaultJa = hintJa;
        h.defaultEn = hintEn;
        return bg.gameObject;
    }

    internal static void SliderRow(Transform g, string name, float y, string ja, string en, string descJa, string descEn, float min, float max,
        TextMeshProUGUI desc, string hintJa, string hintEn)
    {
        GameObject row = Row(g, name, y, descJa, descEn, desc, hintJa, hintEn);
        MakeText(row.transform, "Label", ja, en, 32f, TextNormal, 20f, 0f, 340f, RowHeight);
        MakeText(row.transform, "Value", "100%", null, 30f, TextNormal, 730f, 0f, 110f, RowHeight, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        MakeSlider(row.transform, "Slider", 370f, 8f, 340f, 40f, min, max);
    }

    internal static void MakeSlider(Transform parent, string name, float x, float y, float w, float h, float min, float max)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        root.transform.SetParent(parent, false);
        TopLeft((RectTransform)root.transform, x, y, w, h);
        Image hit = root.GetComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f); // 見えないが、クリック・ドラッグは受ける

        Image track = NewImage(root.transform, "Track", new Color(1f, 1f, 1f, 0.25f));
        RectTransform tr = track.rectTransform;
        tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f);
        tr.offsetMin = new Vector2(0f, -3f); tr.offsetMax = new Vector2(0f, 3f);
        track.raycastTarget = false;

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        RectTransform fa = (RectTransform)fillArea.transform;
        fa.anchorMin = new Vector2(0f, 0.5f); fa.anchorMax = new Vector2(1f, 0.5f);
        fa.offsetMin = new Vector2(8f, -3f); fa.offsetMax = new Vector2(-8f, 3f);
        Image fill = NewImage(fillArea.transform, "Fill", Accent);
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.sizeDelta = Vector2.zero;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        RectTransform ha = (RectTransform)handleArea.transform;
        Stretch(ha);
        ha.offsetMin = new Vector2(8f, 0f); ha.offsetMax = new Vector2(-8f, 0f);
        Image handle = NewImage(handleArea.transform, "Handle", Color.white);
        handle.rectTransform.sizeDelta = new Vector2(16f, h * 0.7f);

        Slider s = root.GetComponent<Slider>();
        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = handle;
        s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.value = Mathf.Clamp(1f, min, max);
    }

    // ───────── ソロ ─────────

    internal static void BuildSolo(Transform g)
    {
        MakeText(g, "Title", "ソロプレイ", "SINGLE PLAY", 72f, Accent, LeftMargin, 70f, 900f, 90f, FontStyles.Bold);
        MakeText(g, "Subtitle", "セーブデータを選択", "SELECT SAVE DATA", 30f, TextDim, LeftMargin, 230f, 600f, 40f);

        float y = 290f;
        for (int i = 1; i <= 3; i++)
        {
            MakeButton(g, "Slot" + i, "スロット" + i + "   ニューゲーム", null, y, 40f, 760f);
            MakeButton(g, "Delete" + i, "削除", "DELETE", y, 32f, 200f, LeftMargin + 800f);
            y += 72f;
        }
        MakeButton(g, "Back", "戻る", "BACK", 920f, 40f, 400f);
    }

    internal static void BuildDeleteConfirm(Transform g)
    {
        MakeText(g, "Title", "警告", "WARNING", 72f, Accent, LeftMargin, 70f, 900f, 90f, FontStyles.Bold);
        MakeText(g, "Message", "スロット1のセーブデータを削除します。", null, 44f, Color.white, LeftMargin, 240f, 900f, 60f);
        MakeText(g, "Info", "", null, 34f, TextDim, LeftMargin, 306f, 900f, 50f);
        MakeText(g, "Caution", "この操作は元に戻せません。", "THIS CANNOT BE UNDONE.", 36f, ErrorColor, LeftMargin, 380f, 900f, 50f);
        MakeButton(g, "Delete", "削除する", "DELETE", 520f, 46f, 600f);
        MakeButton(g, "Cancel", "キャンセル", "CANCEL", 600f, 46f, 600f);
    }

    // ───────── マルチ ─────────

    internal static void BuildMulti(Transform g)
    {
        MakeText(g, "Title", "マルチプレイ", "MULTIPLAYER", 72f, Accent, LeftMargin, 70f, 900f, 90f, FontStyles.Bold);
        MakeText(g, "NoNet", "ネットワーク機能を初期化できませんでした。", "COULD NOT START NETWORKING.", 36f, ErrorColor, LeftMargin, 230f, 900f, 50f);

        // ルームに入る前（名前 → 作る / 参加）
        Transform off = Group(g, "Offline");
        MakeText(off, "NameLabel", "プレイヤー名", "PLAYER NAME", 30f, TextDim, LeftMargin, 230f, 400f, 40f);
        MakeInput(off, "NameInput", "名前を入力", "ENTER NAME", 16, LeftMargin, 274f, 520f, 64f);
        MakeText(off, "Connecting", "接続中...", "CONNECTING...", 44f, TextNormal, LeftMargin, 350f, 800f, 60f);

        Transform menu = Group(off, "Menu");
        MakeButton(menu, "Host", "ルームを作る（ホスト）", "HOST GAME", 350f);
        MakeButton(menu, "Join", "ルームに参加する", "JOIN GAME", 420f);

        Transform save = Group(off, "Save");
        MakeText(save, "Label", "セーブデータを選択", "SELECT SAVE DATA", 30f, TextDim, LeftMargin, 350f, 600f, 40f);
        float y = 404f;
        for (int i = 1; i <= 3; i++)
        {
            MakeButton(save, "Slot" + i, "スロット" + i + "   ニューゲーム", null, y, 40f);
            y += 64f;
        }

        Transform join = Group(off, "Join");
        MakeText(join, "Label", "招待コード", "INVITE CODE", 30f, TextDim, LeftMargin, 350f, 400f, 40f);
        MakeInput(join, "CodeInput", "ABC123", "ABC123", 6, LeftMargin, 394f, 520f, 64f);
        MakeButton(join, "Toggle", "表示", null, 402f, 30f, 160f, LeftMargin + 550f);
        MakeButton(join, "Paste", "貼り付け", "PASTE", 402f, 30f, 220f, LeftMargin + 720f);
        MakeButton(join, "JoinButton", "参加する", "JOIN", 920f, 40f, 400f, LeftMargin + 450f); // 「戻る」と同じ高さ

        // 部屋の中（招待コードとメンバー）
        Transform room = Group(g, "Room");
        MakeText(room, "CodeLabel", "招待コード", "INVITE CODE", 30f, TextDim, LeftMargin, 230f, 400f, 40f);
        MakeText(room, "Code", "******", null, 84f, Color.white, LeftMargin, 270f, 800f, 100f, FontStyles.Bold);
        MakeButton(room, "Toggle", "表示", null, 374f, 30f, 160f);
        MakeButton(room, "Copy", "コピー", "COPY", 374f, 30f, 200f, LeftMargin + 200f);
        MakeText(room, "Copied", "コピーしました", "COPIED", 28f, TextDim, LeftMargin + 430f, 378f, 400f, 40f);
        MakeText(room, "MembersLabel", "メンバー (1/6)", null, 30f, TextDim, LeftMargin, 444f, 600f, 40f);
        for (int i = 1; i <= 6; i++)
        {
            TextMeshProUGUI member = MakeText(room, "Member" + i, "★ プレイヤー" + i, null, 40f, TextNormal, LeftMargin, 492f + 54f * (i - 1), 800f, 52f);
            MakePing(member.transform); // 右側に、回線の強さ（電波のような4本の棒と、ミリ秒）
        }
        MakeButton(room, "Start", "ゲーム開始", "START GAME", 920f, 40f, 400f, LeftMargin + 450f); // 「戻る」と同じ高さ
        MakeText(room, "Waiting", "ホストの開始を待っています...", "WAITING FOR THE HOST TO START...", 36f, TextDim, LeftMargin, 780f, 900f, 50f);

        MakeText(g, "Error", "接続できませんでした。もう一度お試しください。", "COULD NOT CONNECT. PLEASE TRY AGAIN.", 30f, ErrorColor, LeftMargin, 840f, 900f, 50f);
        MakeButton(g, "Back", "戻る", "BACK", 920f, 40f, 400f);
    }

    // ───────── 部品（左上基準。y は下向きに増える。数値は以前のコード版と同じ） ─────────

    internal static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        // 画面ごとのグループは、最初は隠しておく（開くときに MultiLobbyUI が切り替える）
        if (parent.name == "Content") go.SetActive(false);
        return go.transform;
    }

    internal static Image NewImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    internal static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    internal static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, -y);
    }

    /// <summary>文字。en が null でない文字は、言語設定で日本語 / 英語に切り替わる（LocalizedText）。null のときは、実行時に MultiLobbyUI が書き換える</summary>
    internal static TextMeshProUGUI MakeText(Transform parent, string name, string ja, string en, float size, Color color, float x, float y, float w, float h,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = ja;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        TopLeft((RectTransform)go.transform, x, y, w, h);
        if (en != null)
        {
            LocalizedText lt = go.AddComponent<LocalizedText>();
            lt.japanese = ja;
            lt.english = en;
        }
        return t;
    }

    /// <summary>文字だけのメニュー項目。ホバーで右へスライドして赤くなる（タイトルのメニューと同じ動き）</summary>
    internal static GameObject MakeButton(Transform parent, string name, string ja, string en, float y, float size = 46f, float width = 860f, float x = LeftMargin,
        bool slideTextOnly = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f); // 見えないが、クリックは受ける
        TopLeft((RectTransform)go.transform, x, y, width, size * 1.3f);

        GameObject tgo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        tgo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)tgo.transform);
        TextMeshProUGUI t = tgo.GetComponent<TextMeshProUGUI>();
        t.text = ja;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        t.color = TextNormal;
        if (en != null)
        {
            LocalizedText lt = tgo.AddComponent<LocalizedText>();
            lt.japanese = ja;
            lt.english = en;
        }

        Button b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;

        CasinoButtonEffect fx = go.AddComponent<CasinoButtonEffect>();
        fx.buttonText = t;
        fx.titleSystem = titleSystem;
        fx.slideTextOnly = slideTextOnly;
        return go;
    }

    /// <summary>文字入力欄（暗い背景・左揃え）。入力中は点滅する太めの縦線と、ふちの赤い光（実行時につなぐ）が出る</summary>
    internal static void MakeInput(Transform parent, string name, string phJa, string phEn, int limit, float x, float y, float w, float h)
    {
        Image bg = NewImage(parent, name, new Color(1f, 1f, 1f, 0.08f));
        TopLeft(bg.rectTransform, x, y, w, h);
        TMP_InputField input = bg.gameObject.AddComponent<TMP_InputField>();

        GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(bg.transform, false);
        RectTransform ar = (RectTransform)area.transform;
        Stretch(ar);
        ar.offsetMin = new Vector2(16f, 4f);
        ar.offsetMax = new Vector2(-16f, -4f);

        TextMeshProUGUI ph = InputText("Placeholder", area.transform, phJa, TextDim, FontStyles.Italic);
        LocalizedText lt = ph.gameObject.AddComponent<LocalizedText>();
        lt.japanese = phJa;
        lt.english = phEn;
        TextMeshProUGUI txt = InputText("Text", area.transform, "", Color.white, FontStyles.Normal);
        ph.fontSize = txt.fontSize = h * 0.5f;

        input.textViewport = ar;
        input.textComponent = txt;
        input.placeholder = ph;
        input.characterLimit = limit;
        input.caretColor = Color.white;
        input.customCaretColor = true;
        input.caretWidth = 3;
        input.caretBlinkRate = 0.85f;
        input.onFocusSelectAll = false;
        input.richText = false;
        input.selectionColor = new Color(0.85f, 0.12f, 0.12f, 0.5f);
        input.asteriskChar = '*';

        Outline glow = bg.gameObject.AddComponent<Outline>();
        glow.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0f);
        glow.effectDistance = new Vector2(2f, 2f);
    }

    internal static TextMeshProUGUI InputText(string name, Transform parent, string text, Color color, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        return t;
    }

    /// <summary>回線の強さの表示（4本の棒と「42 ms」）。棒の色と本数は、実行時に MultiLobbyUI が通信時間に合わせて変える</summary>
    internal static void MakePing(Transform member)
    {
        GameObject go = new GameObject("Ping", typeof(RectTransform));
        go.transform.SetParent(member, false);
        TopLeft((RectTransform)go.transform, 600f, 0f, 200f, 52f);
        for (int b = 1; b <= 4; b++)
        {
            Image bar = NewImage(go.transform, "Bar" + b, new Color(1f, 1f, 1f, 0.18f));
            bar.raycastTarget = false;
            TopLeft(bar.rectTransform, (b - 1) * 14f, 46f - 9f * b, 10f, 9f * b);
        }
        MakeText(go.transform, "Text", "-- ms", null, 30f, TextNormal, 66f, 0f, 134f, 52f);
    }
}
