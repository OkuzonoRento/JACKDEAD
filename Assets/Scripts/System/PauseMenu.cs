using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ゲーム中に Esc で開くメニュー。再開 / 設定（全体音量・BGM・SE・マウス感度・明るさ）/ タイトルへ戻る / ゲーム終了。
/// 画面の部品は、ゲームシーンの Hierarchy にある「PauseUI」に置いてある（作成: メニュー JackDead/Game/ポーズUIをシーンに作成）。
/// このスクリプトは、置いてある部品に、ボタンの動作・値の読み書き・ページの切り替えをつなぐだけで、部品を作ることはしない。
/// ・言語の設定はタイトルだけ（ここには無い）
/// ・設定の値と保存先はタイトルの設定画面と共通（SettingsManager のキー）
/// ・ソロプレイ中は Time.timeScale = 0 で一時停止する。マルチプレイ中は他プレイヤーが動き続けるので止めない
/// ・セーブはデータ作成時とステージクリア時のみ（GameSaveBridge）。ここでは保存しない
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private enum Page { Main, Settings, ConfirmTitle, ConfirmQuit }

    /// <summary>メニュー表示中。プレイヤーの移動・視点・インタラクトはこの間止める</summary>
    public static bool IsOpen { get; private set; }

    [Tooltip("ポーズ画面のルート（Canvas の子の PauseUI）。メニューを開くときだけ表示する")]
    public GameObject ui;

    private Page page = Page.Main;
    private ResultUIController resultUI;
    private Transform content;
    private bool bound;
    private bool warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        IsOpen = false;
    }

    private void OnDestroy()
    {
        if (IsOpen) SetOpen(false);
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        // ステージクリアの結果表示・次ステージへの遷移・ゲームオーバー演出の間は開かない（開いていたら閉じる）
        if (IsInTransition())
        {
            if (IsOpen) SetOpen(false);
            return;
        }

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            SetOpen(!IsOpen);
        }
    }

    private bool IsInTransition()
    {
        if (resultUI == null) resultUI = FindAnyObjectByType<ResultUIController>();
        return resultUI != null && (resultUI.IsResultShowing || resultUI.IsTransitioning);
    }

    private static bool IsMultiplayerActive()
    {
        return NetworkSession.Instance != null && NetworkSession.Instance.IsOnline;
    }

    private void SetOpen(bool open)
    {
        if (open && ui == null)
        {
            if (!warned) Debug.LogWarning("[PauseMenu] ポーズ画面（PauseUI）がシーンにありません。メニュー JackDead/Game/ポーズUIをシーンに作成 を実行してください。");
            warned = true;
            return;
        }

        IsOpen = open;
        page = Page.Main;

        if (open)
        {
            Bind();
            LoadSettingValues();
            ui.SetActive(true);
            Refresh();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!IsMultiplayerActive()) Time.timeScale = 0f;
        }
        else
        {
            PlayerPrefs.Save();
            if (ui != null) ui.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
        }
    }

    private void ReturnToTitle()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        if (NetworkSession.Instance != null) NetworkSession.Instance.LeaveRoom();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(NetworkSession.TitleSceneName);
    }

    private void QuitGame()
    {
        Time.timeScale = 1f;
        if (NetworkSession.Instance != null) NetworkSession.Instance.LeaveRoom();
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ───────── 部品の取得 ─────────

    private Transform P(string path)
    {
        Transform t = content.Find(path);
        if (t == null) Debug.LogWarning("[PauseMenu] 部品が見つかりません: " + path);
        return t;
    }

    private void Show(string path, bool on)
    {
        Transform t = P(path);
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    private void OnClick(string path, UnityEngine.Events.UnityAction action)
    {
        Transform t = P(path);
        if (t == null) return;
        Button b = t.GetComponent<Button>();
        if (b != null) b.onClick.AddListener(action);
    }

    private void SetPage(Page p)
    {
        page = p;
        Refresh();
    }

    // ───────── ボタンなどをつなぐ（最初に開いたとき一度だけ） ─────────

    private void Bind()
    {
        if (bound) return;
        bound = true;
        content = ui.transform.Find("Panel/Content");

        OnClick("Main/Resume", () => SetOpen(false));
        OnClick("Main/Settings", () => SetPage(Page.Settings));
        OnClick("Main/ToTitle", () => SetPage(Page.ConfirmTitle));
        OnClick("Main/Quit", () => SetPage(Page.ConfirmQuit));

        BindSlider("Settings/Row_Master", v => { PlayerPrefs.SetFloat(SettingsManager.KEY_VOLUME, v); GameSettingsRuntime.ApplyMasterVolume(v); }, false);
        BindSlider("Settings/Row_Bgm", v => PlayerPrefs.SetFloat(SettingsManager.KEY_BGM, v), false);
        BindSlider("Settings/Row_Se", v => PlayerPrefs.SetFloat(SettingsManager.KEY_SE, v), false);
        BindSlider("Settings/Row_Sensitivity", v => PlayerPrefs.SetFloat(SettingsManager.KEY_SENSITIVITY, v), true);
        BindSlider("Settings/Row_Brightness", v => { PlayerPrefs.SetFloat(SettingsManager.KEY_BRIGHTNESS, v); GameSettingsRuntime.ApplyBrightness(v); }, false);
        OnClick("Settings/Back", () => { PlayerPrefs.Save(); SetPage(Page.Main); });

        OnClick("ConfirmTitle/Confirm", ReturnToTitle);
        OnClick("ConfirmTitle/Cancel", () => SetPage(Page.Main));
        OnClick("ConfirmQuit/Confirm", QuitGame);
        OnClick("ConfirmQuit/Cancel", () => SetPage(Page.Main));
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

    // ───────── ページの切り替え ─────────

    private void Refresh()
    {
        Show("Main", page == Page.Main);
        Show("Settings", page == Page.Settings);
        Show("ConfirmTitle", page == Page.ConfirmTitle);
        Show("ConfirmQuit", page == Page.ConfirmQuit);

        bool multi = IsMultiplayerActive();
        Show("Main/MultiNote", multi);
        Show("ConfirmTitle/LeaveNote", multi);
    }
}
