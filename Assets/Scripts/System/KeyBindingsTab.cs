using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 設定画面の「一般 / キー設定」のタブと、キー設定（KeyBindings）の画面を動かす。
/// 画面の部品（タブのボタン・入力機器の行・操作ごとの行など）は、ゲームを実行する前からシーンに置いてある
/// （作成: エディタの KeyBindingsUIBuilder。メニュー JackDead/キー設定UIをシーンに作成）。
/// このスクリプトは、置いてある部品に、クリックの動作・表示する文字・表示の切り替えをつなぐだけで、部品を作ることはしない。
/// タイトルの設定（MultiLobbyUI）とゲーム中の設定（PauseMenu）の、どちらの「Settings」にも同じものが付く。
/// ・「一般」…今までの音量・感度などの設定　・「キー設定」…キーボード・マウス / コントローラーの割り当て
/// ・一番上の「入力機器」は、言語の設定と同じ「&lt; &gt;」で切り替える。接続している機器が1種類だけのときは、表示はするが切り替えられない
/// ・割り当てたい行のボタンを押して、割り当てたいキー（ボタン）を押す。Esc（コントローラーは Start）で取り消し、Backspace で解除
/// </summary>
public class KeyBindingsTab : MonoBehaviour
{
    private static readonly Color TextDim = new Color(0.55f, 0.55f, 0.58f, 1f);

    private class Row
    {
        public KeyBindings.Entry entry;
        public GameObject root;
        public TMP_Text keyText;
    }

    private Transform settings;
    private TMP_Text description;
    private string descriptionDefaultJa, descriptionDefaultEn;
    private readonly List<GameObject> generalObjects = new List<GameObject>();
    private readonly List<Row> rows = new List<Row>();

    private GameObject panel;
    private RectTransform listContent;
    private TMP_Text tabGeneral, tabControls, deviceValue, padNote;
    private Button devicePrev, deviceNext;
    private TMP_Text devicePrevText, deviceNextText;
    private KeyBindings.Scheme scheme = KeyBindings.Scheme.KeyboardMouse;
    private bool controlsTab;
    private bool built;

    /// <summary>設定画面（Settings のグループ）の部品に、動作をつなぐ。何度呼んでも1回だけ。部品が無いシーンでは何もしない</summary>
    public static void Attach(Transform settingsGroup)
    {
        if (settingsGroup == null || settingsGroup.GetComponent<KeyBindingsTab>() != null) return;

        if (settingsGroup.Find("KeyBindPanel") == null || settingsGroup.Find("Tab_General") == null)
        {
            Debug.LogWarning("[KeyBindingsTab] キー設定の部品がシーンにありません。メニュー JackDead/キー設定UIをシーンに作成 を実行してください。");
            return;
        }

        KeyBindingsTab tab = settingsGroup.gameObject.AddComponent<KeyBindingsTab>();
        tab.Bind(settingsGroup);
    }

    private void OnEnable()
    {
        Localization.LanguageChanged += OnLanguageChanged;
        InputSystem.onDeviceChange += OnDeviceChange;
        if (built) ShowTab(false); // 設定を開くたびに「一般」から
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    private void OnLanguageChanged()
    {
        if (built && controlsTab) RefreshDevices(true);
    }

    // ───────── 置いてある部品につなぐ ─────────

    private void Bind(Transform group)
    {
        settings = group;

        Transform descT = group.Find("Description");
        description = descT != null ? descT.GetComponent<TMP_Text>() : null;
        LocalizedText descLoc = description != null ? description.GetComponent<LocalizedText>() : null;
        if (descLoc != null) { descriptionDefaultJa = descLoc.japanese; descriptionDefaultEn = descLoc.english; }

        // 今まである部品（タイトル・説明・戻るボタン・このタブの部品以外）は「一般」タブの中身として、切り替えで隠す
        foreach (Transform child in group)
        {
            string n = child.name;
            if (n == "Title" || n == "Description" || n == "Back" || n == "Tab_General" || n == "Tab_Controls" || n == "KeyBindPanel") continue;
            generalObjects.Add(child.gameObject);
        }

        tabGeneral = ButtonLabel(group.Find("Tab_General"), () => ShowTab(false));
        tabControls = ButtonLabel(group.Find("Tab_Controls"), () => ShowTab(true));

        Transform panelT = group.Find("KeyBindPanel");
        panel = panelT.gameObject;

        Transform deviceRow = panelT.Find("Row_Device");
        deviceValue = deviceRow.Find("Value").GetComponent<TMP_Text>();
        devicePrevText = ButtonLabel(deviceRow.Find("Prev"), CycleDevice, out devicePrev);
        deviceNextText = ButtonLabel(deviceRow.Find("Next"), CycleDevice, out deviceNext);

        listContent = (RectTransform)panelT.Find("List/Viewport/Content");
        padNote = panelT.Find("PadNote").GetComponent<TMP_Text>();
        ButtonLabel(panelT.Find("Reset"), ResetCurrentScheme);

        // 操作ごとの行（名前で、KeyBindings.Entries と対応させる）
        foreach (KeyBindings.Entry entry in KeyBindings.Entries)
        {
            Transform rowT = listContent.Find("Row_" + entry.action + (entry.part ?? ""));
            if (rowT == null) { Debug.LogWarning("[KeyBindingsTab] 行がありません: " + entry.action + entry.part + "（キー設定UIをシーンに作り直してください）"); continue; }

            Row row = new Row { entry = entry, root = rowT.gameObject };
            Transform key = rowT.Find("Key");
            row.keyText = key.Find("Text").GetComponent<TMP_Text>();

            KeyBindings.Entry captured = entry;
            TMP_Text keyText = row.keyText;
            key.GetComponent<Button>().onClick.AddListener(() => BeginRebind(captured, keyText));

            // 行にカーソルを合わせると、説明が出る
            HoverNotifier hover = key.gameObject.AddComponent<HoverNotifier>();
            hover.onEnter = () => { if (!KeyBindings.Busy) SetDescription(captured.descJa, captured.descEn); };
            hover.onExit = () => { if (!KeyBindings.Busy) SetDescription(null, null); };

            rows.Add(row);
        }

        built = true;
        ShowTab(false);
    }

    private static TMP_Text ButtonLabel(Transform button, UnityEngine.Events.UnityAction onClick)
    {
        return ButtonLabel(button, onClick, out _);
    }

    private static TMP_Text ButtonLabel(Transform button, UnityEngine.Events.UnityAction onClick, out Button b)
    {
        b = button.GetComponent<Button>();
        b.onClick.AddListener(onClick);
        return button.Find("Label").GetComponent<TMP_Text>();
    }

    /// <summary>
    /// ポインターの出入りだけを受ける。EventTrigger は、ホイール（スクロール）の操作まで受けてしまい、
    /// 一覧のスクロールが効かなくなるので、必要な2つだけを受ける部品を使う
    /// </summary>
    private class HoverNotifier : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action onEnter, onExit;
        public void OnPointerEnter(PointerEventData eventData) { onEnter?.Invoke(); }
        public void OnPointerExit(PointerEventData eventData) { onExit?.Invoke(); }
    }

    // ───────── タブ・機器の切り替え ─────────

    private void ShowTab(bool controls)
    {
        controlsTab = controls;
        foreach (GameObject go in generalObjects) if (go != null) go.SetActive(!controls);
        if (panel != null) panel.SetActive(controls);

        SetTabColor(tabGeneral, !controls);
        SetTabColor(tabControls, controls);

        if (controls)
        {
            SetDescription(null, null);
        }
        else if (description != null)
        {
            // 「一般」に戻るときは、説明文を元の案内に戻す（そのあとは、各項目の説明が出る）
            LocalizedText lt = description.GetComponent<LocalizedText>();
            if (lt != null && !string.IsNullOrEmpty(descriptionDefaultJa)) lt.Set(descriptionDefaultJa, descriptionDefaultEn);
        }

        if (controls) RefreshDevices(false);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        // コントローラーを抜き差ししたら、選べる機器を更新する
        if (!(device is Gamepad)) return;
        if (built && controlsTab && !KeyBindings.Busy) RefreshDevices(true);
    }

    /// <summary>
    /// 設定できる機器は、いま接続しているものだけ。キーボード・マウスは常にあり、コントローラーは繋がっているときだけ増える。
    /// 「入力機器」の行は、1種類でも必ず表示する。2種類以上あるときだけ「&lt;」「&gt;」で切り替えられる（1種類のときは暗くして押せない）。
    /// 選んでいたコントローラーが抜かれたら、キーボードに戻す。keepScroll: 一覧のスクロール位置を保つ
    /// </summary>
    private void RefreshDevices(bool keepScroll)
    {
        bool padConnected = Gamepad.all.Count > 0;
        if (!padConnected && scheme == KeyBindings.Scheme.Gamepad) scheme = KeyBindings.Scheme.KeyboardMouse;

        deviceValue.text = scheme == KeyBindings.Scheme.KeyboardMouse
            ? Localization.T("キーボード・マウス", "KEYBOARD / MOUSE")
            : Localization.T("コントローラー", "CONTROLLER");

        devicePrev.interactable = deviceNext.interactable = padConnected;
        Color arrow = padConnected ? Color.white : new Color(1f, 1f, 1f, 0.15f);
        devicePrevText.color = deviceNextText.color = arrow;

        RefreshRows(keepScroll);
    }

    /// <summary>次の機器へ（2種類のとき、押すたびに入れ替わる）</summary>
    private void CycleDevice()
    {
        if (KeyBindings.Busy || Gamepad.all.Count == 0) return;
        scheme = scheme == KeyBindings.Scheme.KeyboardMouse ? KeyBindings.Scheme.Gamepad : KeyBindings.Scheme.KeyboardMouse;
        RefreshDevices(false);
    }

    private static void SetTabColor(TMP_Text t, bool active)
    {
        if (t != null) t.color = active ? Color.white : TextDim;
    }

    private void ResetCurrentScheme()
    {
        if (KeyBindings.Busy) return;
        KeyBindings.ResetScheme(scheme);
        RefreshRows(true);
    }

    /// <summary>画面下の説明文を書き換える（null のときは元の案内に戻す）</summary>
    private void SetDescription(string ja, string en)
    {
        if (description == null) return;
        if (ja == null)
        {
            ja = "割り当てたい項目のボタンを押してから、キー（ボタン）を押してください。Esc（コントローラーは Start）で取り消し、Backspace で解除します。";
            en = "CLICK AN ACTION, THEN PRESS A KEY OR BUTTON. ESC (START ON A CONTROLLER) CANCELS, BACKSPACE CLEARS.";
        }
        LocalizedText lt = description.GetComponent<LocalizedText>();
        if (lt != null) lt.Set(ja, en);
        else description.text = Localization.T(ja, en);
    }

    // ───────── 行の表示 ─────────

    /// <summary>置いてある行のうち、選んでいる機器で変えられるものだけを表示し、今の割り当てを書き込む（行は作り直さない）</summary>
    private void RefreshRows(bool keepScroll)
    {
        foreach (Row row in rows)
        {
            bool available = KeyBindings.IsEntryAvailable(row.entry, scheme);
            if (row.root.activeSelf != available) row.root.SetActive(available);
            if (available) SetKeyText(row.keyText, row.entry);
        }

        if (!keepScroll && listContent != null) listContent.anchoredPosition = Vector2.zero;

        if (padNote != null)
        {
            bool pad = scheme == KeyBindings.Scheme.Gamepad;
            padNote.gameObject.SetActive(pad);
            if (pad)
            {
                string deviceName = Gamepad.current != null ? Gamepad.current.displayName : "";
                padNote.text = Localization.T("接続中：", "CONNECTED: ") + deviceName
                    + Localization.T("　移動：左スティック / 視点：右スティック（固定）", "   MOVE: LEFT STICK / LOOK: RIGHT STICK (FIXED)");
            }
        }
    }

    private void SetKeyText(TMP_Text text, KeyBindings.Entry entry)
    {
        string shown = KeyBindings.GetDisplay(entry, scheme);
        text.text = string.IsNullOrEmpty(shown) ? Localization.T("未設定", "UNBOUND") : shown;
        text.color = string.IsNullOrEmpty(shown) ? TextDim : Color.white;
    }

    private void BeginRebind(KeyBindings.Entry entry, TMP_Text keyText)
    {
        if (KeyBindings.Busy) return;

        keyText.text = Localization.T("キーを押してください…", "PRESS A KEY...");
        keyText.color = new Color(1f, 0.85f, 0.3f, 1f);
        SetDescription("割り当てたいキー（ボタン）を押してください。Esc（コントローラーは Start）で取り消し、Backspace で解除します。",
                       "PRESS THE KEY OR BUTTON TO ASSIGN. ESC (START ON A CONTROLLER) CANCELS, BACKSPACE CLEARS.");

        // 入力待ちのあいだ、ボタンの選択状態が残らないようにする
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        KeyBindings.StartRebind(entry, scheme, () =>
        {
            SetDescription(null, null);
            if (this != null && controlsTab) RefreshRows(true); // 入れ替えが起きた行もまとめて更新する（見ていた位置は保つ）
        });
    }
}
