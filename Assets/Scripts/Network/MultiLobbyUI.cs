using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// タイトル画面の上に重ねて出す、ソロ / マルチ / 設定の画面（R.E.P.O. のように、暗い背景に左揃えの文字だけのメニュー）。
/// 画面の部品は、すべてタイトルシーンの Hierarchy の「LobbyUI」に置いてある（作成: メニュー JackDead/Title/ロビーUIをシーンに作成）。
/// このスクリプトは、置いてある部品に、ボタンの動作・文字の書き換え・表示の切り替えをつなぐだけで、部品を作ることはしない。
///
/// LobbyUI/Panel/Content の下に、画面ごとのグループがある:
///   Settings（設定）/ Solo（ソロのセーブ選択）/ DeleteConfirm（セーブ削除の確認）/ Multi（ルーム作成・参加・ルームの中）
/// 流れ：「ルームを作る」→ セーブデータ選択 → 部屋作成 / 「ルームに参加する」→ 招待コード入力
/// （セーブ選択が必要なのはホストのみ。参加者はホストのセーブで遊ぶ）
/// </summary>
public class MultiLobbyUI : MonoBehaviour
{
    public enum Step { Menu, SelectSave, JoinCode }
    private enum Mode { Multi, Solo, Settings }

    private static readonly Color TextNormal = new Color(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Color TextDim = new Color(0.55f, 0.55f, 0.58f, 1f);

    private Mode mode = Mode.Multi;
    private Step step = Step.Menu;
    private Step entryStep = Step.Menu; // 最初に開いたステップ（そこで「戻る」を押すとタイトルへ戻る）
    private string joinCode = "";
    private string playerName = "";
    private bool busy;
    private int pendingDelete;     // 0 以外: そのスロットの削除の確認を表示中
    private bool codeVisible;      // 招待コードを表示するか（最初は ****** で隠す）
    private float copiedUntil;
    private System.Action onClose;

    private bool bound;
    private Transform content;
    private string lastSignature = "";
    private Title3DSystem titleSystem;

    private TMP_InputField nameInput;
    private TMP_InputField codeInput;

    // ───────── 開く（外から呼ぶ） ─────────

    private static MultiLobbyUI Find()
    {
        MultiLobbyUI ui = FindFirstObjectByType<MultiLobbyUI>(FindObjectsInactive.Include);
        if (ui == null) Debug.LogWarning("[MultiLobbyUI] シーンに LobbyUI がありません。メニュー JackDead/Title/ロビーUIをシーンに作成 を実行してください。");
        return ui;
    }

    public static MultiLobbyUI Open(System.Action onClose) { return Open(Step.Menu, onClose); }

    /// <summary>マルチ。firstStep: Menu=選択 / SelectSave=ルームを作る / JoinCode=参加する。画面を出せなければ null</summary>
    public static MultiLobbyUI Open(Step firstStep, System.Action onClose)
    {
        MultiLobbyUI ui = Find();
        if (ui != null) ui.Begin(Mode.Multi, firstStep, onClose);
        return ui;
    }

    /// <summary>ソロプレイのセーブデータ選択</summary>
    public static MultiLobbyUI OpenSolo(System.Action onClose)
    {
        MultiLobbyUI ui = Find();
        if (ui != null) ui.Begin(Mode.Solo, Step.Menu, onClose);
        return ui;
    }

    /// <summary>設定画面</summary>
    public static MultiLobbyUI OpenSettings(System.Action onClose)
    {
        MultiLobbyUI ui = Find();
        if (ui != null) ui.Begin(Mode.Settings, Step.Menu, onClose);
        return ui;
    }

    private void Begin(Mode m, Step first, System.Action close)
    {
        // 開くたびに初期状態から始める（前回の入力・エラー表示は消す）
        mode = m;
        step = entryStep = first;
        onClose = close;
        joinCode = "";
        busy = false;
        pendingDelete = 0;
        codeVisible = false;
        copiedUntil = 0f;

        playerName = "";
        if (NetworkSession.Instance != null)
        {
            NetworkSession.Instance.ClearError();
            playerName = NetworkSession.Instance.LocalPlayerName;
        }

        gameObject.SetActive(true);
        Bind();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); // 入力欄を最初から選んだ状態にしない
        if (mode == Mode.Settings) LoadSettingValues();
        Refresh();
    }

    private void OnEnable() { Localization.LanguageChanged += OnLanguageChanged; }
    private void OnDisable() { Localization.LanguageChanged -= OnLanguageChanged; }
    private void OnLanguageChanged() { if (bound) Refresh(); }

    private void Update()
    {
        // 状態（ステップ・接続状況・メンバー・エラー）が変わったときだけ文字などを書き換える
        if (Signature() != lastSignature) Refresh();

        if (!KeyBindings.Busy && UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            Back();
    }

    private string Signature()
    {
        NetworkSession net = NetworkSession.Instance;
        StringBuilder sb = new StringBuilder();
        sb.Append(mode).Append('|').Append(step).Append('|').Append(busy).Append('|').Append(pendingDelete).Append('|').Append(codeVisible)
          .Append('|').Append(Time.unscaledTime < copiedUntil);
        if (net != null)
        {
            sb.Append('|').Append(net.State).Append('|').Append(net.RoomCode).Append('|').Append(net.LastError);
            sb.Append('|').Append(net.IsHost);
            foreach (LobbyMember m in net.Members) sb.Append('|').Append(m.name).Append(m.isHost).Append(m.pingMs / 5);
        }
        return sb.ToString();
    }

    // ───────── 操作 ─────────

    /// <summary>招待コードに使える文字（英数字）だけを、大文字・最大6文字にそろえる。貼り付けた文字の空白なども取り除く</summary>
    private static string Sanitize(string s)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (s ?? "").ToUpper())
            if (char.IsLetterOrDigit(c) && sb.Length < 6) sb.Append(c);
        return sb.ToString();
    }

    private void Back()
    {
        if (!gameObject.activeInHierarchy) return;
        if (pendingDelete != 0) { pendingDelete = 0; return; } // 削除の確認中は、取り消すだけ
        NetworkSession net = NetworkSession.Instance;
        bool offline = mode != Mode.Multi || net == null || net.State == NetworkSessionState.Offline;
        // 選択ステップ中は一つ前（ルーム作成/参加の選択）へ、最初のステップならタイトルへ
        if (offline && step != entryStep && !busy) step = entryStep;
        else Close();
    }

    private void Close()
    {
        if (mode == Mode.Settings) PlayerPrefs.Save();
        if (mode == Mode.Multi && NetworkSession.Instance != null)
        {
            NetworkSession.Instance.LeaveRoom();
            NetworkSession.Instance.ClearError();
        }
        gameObject.SetActive(false);
        onClose?.Invoke();
    }

    private async void CreateRoom(int slotNumber)
    {
        if (busy) return;
        busy = true;
        // ホストになる人だけがセーブデータを選ぶ
        SaveSystem.SelectAndLoadSlot(true, slotNumber);
        NetworkSession.Instance.LocalPlayerName = playerName;
        bool ok = await NetworkSession.Instance.CreateRoomAsync($"{playerName}の部屋");
        if (!ok) step = Step.Menu;
        busy = false;
    }

    private async void JoinRoom()
    {
        if (busy) return;
        busy = true;
        SaveSystem.IsMultiplayerMode = true;
        NetworkSession.Instance.LocalPlayerName = playerName;
        await NetworkSession.Instance.JoinRoomAsync(joinCode.Trim().ToUpper());
        busy = false;
    }

    private void StartSolo(int slot)
    {
        if (titleSystem == null) return;
        titleSystem.OnClickSelectSlot(slot); // セーブ読み込み → 画面の暗転 → ゲームシーンへ
        gameObject.SetActive(false);
    }

    // ───────── 部品の取得 ─────────

    private Transform P(string path)
    {
        Transform t = content.Find(path);
        if (t == null) Debug.LogWarning("[MultiLobbyUI] 部品が見つかりません: " + path);
        return t;
    }

    private void Show(string path, bool on)
    {
        Transform t = P(path);
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    private void SetText(string path, string text)
    {
        Transform t = P(path);
        if (t == null) return;
        TMP_Text tx = t.GetComponent<TMP_Text>();
        if (tx != null && tx.text != text) tx.text = text;
    }

    private void SetButtonLabel(string path, string text) { SetText(path + "/Label", text); }

    private void OnClick(string path, UnityEngine.Events.UnityAction action)
    {
        Transform t = P(path);
        if (t == null) return;
        Button b = t.GetComponent<Button>();
        if (b != null) b.onClick.AddListener(action);
    }

    private TMP_InputField Input(string path)
    {
        Transform t = P(path);
        return t != null ? t.GetComponent<TMP_InputField>() : null;
    }

    /// <summary>入力欄の、選択中を示す赤い光（ふちの Outline）をつなぐ</summary>
    private static void BindGlow(TMP_InputField input)
    {
        if (input == null) return;
        Outline glow = input.GetComponent<Outline>();
        if (glow == null) return;
        Color accent = glow.effectColor;
        input.onSelect.AddListener(_ => glow.effectColor = new Color(accent.r, accent.g, accent.b, 1f));
        input.onDeselect.AddListener(_ => glow.effectColor = new Color(accent.r, accent.g, accent.b, 0f));
    }

    // ───────── ボタンなどをつなぐ（最初に開いたとき一度だけ） ─────────

    private void Bind()
    {
        if (bound) return;
        bound = true;
        content = transform.Find("Panel/Content");
        titleSystem = FindAnyObjectByType<Title3DSystem>();

        // 設定
        OnClick("Settings/Back", Back);
        KeyBindingsTab.Attach(P("Settings"));
        BindSlider("Settings/Row_Master", v => { PlayerPrefs.SetFloat(SettingsManager.KEY_VOLUME, v); GameSettingsRuntime.ApplyMasterVolume(v); }, false);
        BindSlider("Settings/Row_Bgm", v => PlayerPrefs.SetFloat(SettingsManager.KEY_BGM, v), false);
        BindSlider("Settings/Row_Se", v => PlayerPrefs.SetFloat(SettingsManager.KEY_SE, v), false);
        BindSlider("Settings/Row_Sensitivity", v => PlayerPrefs.SetFloat(SettingsManager.KEY_SENSITIVITY, v), true);
        BindSlider("Settings/Row_Brightness", v => { PlayerPrefs.SetFloat(SettingsManager.KEY_BRIGHTNESS, v); GameSettingsRuntime.ApplyBrightness(v); }, false);
        UnityEngine.Events.UnityAction toggleLanguage = () => Localization.Current = Localization.IsEnglish ? GameLanguage.Japanese : GameLanguage.English;
        OnClick("Settings/Row_Language/Prev", toggleLanguage);
        OnClick("Settings/Row_Language/Next", toggleLanguage);

        // ソロ
        for (int i = 1; i <= 3; i++)
        {
            int slot = i;
            OnClick("Solo/Slot" + i, () => StartSolo(slot));
            OnClick("Solo/Delete" + i, () => pendingDelete = slot);
        }
        OnClick("Solo/Back", Back);

        // 削除の確認
        OnClick("DeleteConfirm/Delete", () => { SaveSystem.DeleteSlot(pendingDelete); pendingDelete = 0; });
        OnClick("DeleteConfirm/Cancel", () => pendingDelete = 0);

        // マルチ
        nameInput = Input("Multi/Offline/NameInput");
        if (nameInput != null)
        {
            nameInput.onValueChanged.AddListener(v => playerName = v);
            BindGlow(nameInput);
        }
        OnClick("Multi/Offline/Menu/Host", () => step = Step.SelectSave);
        OnClick("Multi/Offline/Menu/Join", () => step = Step.JoinCode);
        for (int i = 1; i <= 3; i++)
        {
            int slot = i;
            OnClick("Multi/Offline/Save/Slot" + i, () => CreateRoom(slot));
        }
        codeInput = Input("Multi/Offline/Join/CodeInput");
        if (codeInput != null)
        {
            codeInput.onValueChanged.AddListener(v =>
            {
                string up = Sanitize(v);
                joinCode = up;
                if (up != v) codeInput.SetTextWithoutNotify(up);
            });
            BindGlow(codeInput);
        }
        OnClick("Multi/Offline/Join/Toggle", () => codeVisible = !codeVisible);
        OnClick("Multi/Offline/Join/Paste", () => joinCode = Sanitize(GUIUtility.systemCopyBuffer));
        OnClick("Multi/Offline/Join/JoinButton", JoinRoom);

        OnClick("Multi/Room/Toggle", () => codeVisible = !codeVisible);
        OnClick("Multi/Room/Copy", () =>
        {
            if (NetworkSession.Instance == null) return;
            GUIUtility.systemCopyBuffer = NetworkSession.Instance.RoomCode;
            copiedUntil = Time.unscaledTime + 2f;
        });
        OnClick("Multi/Room/Start", () => { if (NetworkSession.Instance != null) NetworkSession.Instance.StartGame(); });
        OnClick("Multi/Back", Back);
    }

    private void BindSlider(string rowPath, System.Action<float> apply, bool oneDecimal)
    {
        Transform row = P(rowPath);
        if (row == null) return;
        Slider s = row.Find("Slider").GetComponent<Slider>();
        TMP_Text value = row.Find("Value").GetComponent<TMP_Text>();
        s.onValueChanged.AddListener(v =>
        {
            apply(v);
            value.text = oneDecimal ? v.ToString("F1") : Mathf.RoundToInt(v * 100f) + "%";
        });
    }

    private void SetSlider(string rowPath, float v, bool oneDecimal)
    {
        Transform row = P(rowPath);
        if (row == null) return;
        Slider s = row.Find("Slider").GetComponent<Slider>();
        s.SetValueWithoutNotify(Mathf.Clamp(v, s.minValue, s.maxValue));
        row.Find("Value").GetComponent<TMP_Text>().text = oneDecimal ? s.value.ToString("F1") : Mathf.RoundToInt(s.value * 100f) + "%";
    }

    private void LoadSettingValues()
    {
        SetSlider("Settings/Row_Master", SettingsManager.MasterVolume, false);
        SetSlider("Settings/Row_Bgm", SettingsManager.BgmVolume, false);
        SetSlider("Settings/Row_Se", SettingsManager.SeVolume, false);
        SetSlider("Settings/Row_Sensitivity", SettingsManager.MouseSensitivity, true);
        SetSlider("Settings/Row_Brightness", SettingsManager.Brightness, false);
    }

    // ───────── 画面の書き換え ─────────

    private static string SlotLabel(int slot)
    {
        GameSaveData data = SaveSystem.PeekSaveData(slot);
        return data != null && data.hasSaveData
            ? Localization.T($"スロット{slot}   {data.currentDay}日目  /  所持金 ${data.currentMoney:N0}",
                             $"SLOT {slot}   DAY {data.currentDay}  /  MONEY ${data.currentMoney:N0}")
            : Localization.T($"スロット{slot}   ニューゲーム", $"SLOT {slot}   NEW GAME");
    }

    private void Refresh()
    {
        if (content == null) return;
        lastSignature = Signature();

        Show("Settings", mode == Mode.Settings);
        Show("Solo", mode == Mode.Solo && pendingDelete == 0);
        Show("DeleteConfirm", mode == Mode.Solo && pendingDelete != 0);
        Show("Multi", mode == Mode.Multi);

        switch (mode)
        {
            case Mode.Settings: RefreshSettings(); break;
            case Mode.Solo: RefreshSolo(); break;
            case Mode.Multi: RefreshMulti(); break;
        }
    }

    private void RefreshSettings()
    {
        SetText("Settings/Row_Language/Value", Localization.IsEnglish ? "ENGLISH" : "日本語");
    }

    private void RefreshSolo()
    {
        if (pendingDelete != 0)
        {
            int slot = pendingDelete;
            GameSaveData data = SaveSystem.PeekSaveData(slot);
            SetText("DeleteConfirm/Message", Localization.T($"スロット{slot}のセーブデータを削除します。", $"DELETE THE SAVE DATA IN SLOT {slot}?"));
            SetText("DeleteConfirm/Info", data != null && data.hasSaveData
                ? Localization.T($"{data.currentDay}日目  /  所持金 ${data.currentMoney:N0}", $"DAY {data.currentDay}  /  MONEY ${data.currentMoney:N0}")
                : "");
            return;
        }

        for (int i = 1; i <= 3; i++)
        {
            GameSaveData data = SaveSystem.PeekSaveData(i);
            SetButtonLabel("Solo/Slot" + i, SlotLabel(i));
            Show("Solo/Delete" + i, data != null && data.hasSaveData);
        }
    }

    private static readonly Color PingOff = new Color(1f, 1f, 1f, 0.18f);

    /// <summary>回線の強さ。通信時間が短いほど棒が多く緑に、長いほど少なく赤になる。測れていないときは「--」</summary>
    private void SetPing(string path, int ms)
    {
        Transform ping = P(path);
        if (ping == null) return;

        int bars; Color color;
        if (ms < 0) { bars = 0; color = TextDim; }
        else if (ms <= 60) { bars = 4; color = new Color(0.35f, 0.85f, 0.4f, 1f); }
        else if (ms <= 120) { bars = 3; color = new Color(0.7f, 0.85f, 0.3f, 1f); }
        else if (ms <= 200) { bars = 2; color = new Color(0.95f, 0.65f, 0.2f, 1f); }
        else { bars = 1; color = new Color(0.9f, 0.25f, 0.2f, 1f); }

        for (int b = 1; b <= 4; b++)
        {
            Transform bar = ping.Find("Bar" + b);
            if (bar != null) bar.GetComponent<Image>().color = b <= bars ? color : PingOff;
        }
        TMP_Text text = ping.Find("Text").GetComponent<TMP_Text>();
        text.text = ms < 0 ? "-- ms" : ms + " ms";
        text.color = ms < 0 ? TextDim : color;
    }

    private void RefreshMulti()
    {
        NetworkSession net = NetworkSession.Instance;

        SetText("Multi/Title", entryStep == Step.JoinCode ? Localization.T("ルームに参加", "JOIN GAME")
                             : entryStep == Step.SelectSave ? Localization.T("ルームを作る", "HOST GAME")
                             : Localization.T("マルチプレイ", "MULTIPLAYER"));

        bool noNet = net == null;
        bool offline = !noNet && (net.State == NetworkSessionState.Offline || net.State == NetworkSessionState.Connecting);
        bool room = !noNet && !offline;
        Show("Multi/NoNet", noNet);
        Show("Multi/Offline", offline);
        Show("Multi/Room", room);

        if (offline)
        {
            bool locked = busy || net.State == NetworkSessionState.Connecting;

            // 部屋を作っている間・参加しようとしている間は、名前を変えられない
            if (nameInput != null)
            {
                if (nameInput.text != playerName) nameInput.SetTextWithoutNotify(playerName);
                nameInput.interactable = !locked;
                nameInput.textComponent.color = locked ? TextDim : Color.white;
            }

            Show("Multi/Offline/Connecting", locked);
            Show("Multi/Offline/Menu", !locked && step == Step.Menu);
            Show("Multi/Offline/Save", !locked && step == Step.SelectSave);
            Show("Multi/Offline/Join", !locked && step == Step.JoinCode);

            for (int i = 1; i <= 3; i++) SetButtonLabel("Multi/Offline/Save/Slot" + i, SlotLabel(i));

            if (codeInput != null)
            {
                TMP_InputField.ContentType wanted = codeVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                if (codeInput.contentType != wanted)
                {
                    codeInput.contentType = wanted;
                    codeInput.ForceLabelUpdate();
                }
                if (codeInput.text != joinCode) codeInput.SetTextWithoutNotify(joinCode);
            }
            SetButtonLabel("Multi/Offline/Join/Toggle", codeVisible ? Localization.T("隠す", "HIDE") : Localization.T("表示", "SHOW"));
        }

        if (room)
        {
            SetText("Multi/Room/Code", codeVisible ? net.RoomCode : new string('*', net.RoomCode.Length));
            SetButtonLabel("Multi/Room/Toggle", codeVisible ? Localization.T("隠す", "HIDE") : Localization.T("表示", "SHOW"));
            Show("Multi/Room/Copied", Time.unscaledTime < copiedUntil);

            SetText("Multi/Room/MembersLabel", Localization.T($"メンバー ({net.Members.Count}/{NetworkSession.MaxPlayers})", $"MEMBERS ({net.Members.Count}/{NetworkSession.MaxPlayers})"));
            for (int i = 0; i < NetworkSession.MaxPlayers; i++)
            {
                bool has = i < net.Members.Count;
                Show("Multi/Room/Member" + (i + 1), has);
                if (has) SetText("Multi/Room/Member" + (i + 1), (net.Members[i].isHost ? "★ " : "   ") + net.Members[i].name);
                if (has) SetPing("Multi/Room/Member" + (i + 1) + "/Ping", net.Members[i].pingMs);
            }

            Show("Multi/Room/Start", net.IsHost);
            Show("Multi/Room/Waiting", !net.IsHost);
            Transform waiting = P("Multi/Room/Waiting");
            if (waiting != null)
            {
                // メンバーの一覧の、すぐ下に出す
                RectTransform wr = (RectTransform)waiting;
                wr.anchoredPosition = new Vector2(wr.anchoredPosition.x, -(492f + 54f * net.Members.Count + 30f));
            }
        }

        Show("Multi/Error", net != null && !string.IsNullOrEmpty(net.LastError));
    }
}

/// <summary>マウスが行の範囲に入っている間、背景の色を変えて説明を出す（設定画面の行用）。
/// 上に重なるボタンやスライダーに乗っても行から外れた扱いにならないよう、イベントではなく位置で判定する</summary>
public class UiHover : MonoBehaviour
{
    public Image bg;
    public Color normal;
    public Color hover;
    public TMP_Text descTarget;
    public string descJa, descEn;
    public string defaultJa, defaultEn;

    private bool inside;

    void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        bool now = mouse != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, mouse.position.ReadValue(), null);
        if (now == inside) return;
        inside = now;
        Apply();
    }

    void OnEnable()
    {
        inside = false;
        Apply();
    }

    private void Apply()
    {
        if (bg != null) bg.color = inside ? hover : normal;
        if (descTarget != null) descTarget.text = inside ? Localization.T(descJa, descEn) : Localization.T(defaultJa, defaultEn);
    }
}
