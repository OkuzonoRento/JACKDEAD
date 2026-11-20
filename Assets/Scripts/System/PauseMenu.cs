using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// ゲーム中に Esc で開くメニュー（IMGUI）。再開 / 設定（音量・マウス感度）/ タイトルへ戻る / ゲーム終了。
/// ・ソロプレイ中は Time.timeScale = 0 で一時停止する。マルチプレイ中は他プレイヤーが動き続けるので止めない
/// ・セーブはデータ作成時とステージクリア時のみ（GameSaveBridge）。ここでは保存しない
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private enum Page { Main, Settings, ConfirmTitle, ConfirmQuit }

    /// <summary>メニュー表示中。プレイヤーの移動・視点・インタラクトはこの間止める</summary>
    public static bool IsOpen { get; private set; }

    private Page page = Page.Main;
    private ResultUIController resultUI;
    private float volume;
    private float sensitivity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        IsOpen = false;
        if (scene.name != NetworkSession.GameSceneName) return;
        new GameObject("PauseMenu").AddComponent<PauseMenu>();
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
        IsOpen = open;
        page = Page.Main;

        if (open)
        {
            volume = PlayerPrefs.GetFloat(SettingsManager.KEY_VOLUME, 1.0f);
            sensitivity = PlayerPrefs.GetFloat(SettingsManager.KEY_SENSITIVITY, SettingsManager.DefaultSensitivity);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!IsMultiplayerActive()) Time.timeScale = 0f;
        }
        else
        {
            PlayerPrefs.Save();
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

    private void OnGUI()
    {
        if (!IsOpen) return;

        const float w = 420f, h = 340f;
        Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(box, "PAUSE");

        GUILayout.BeginArea(new Rect(box.x + 24, box.y + 40, w - 48, h - 60));

        switch (page)
        {
            case Page.Main:
                if (GUILayout.Button("再開", GUILayout.Height(40))) SetOpen(false);
                GUILayout.Space(8);
                if (GUILayout.Button("設定", GUILayout.Height(40))) page = Page.Settings;
                GUILayout.Space(8);
                if (GUILayout.Button("タイトルへ戻る", GUILayout.Height(40))) page = Page.ConfirmTitle;
                GUILayout.Space(8);
                if (GUILayout.Button("ゲームを終了", GUILayout.Height(40))) page = Page.ConfirmQuit;
                if (IsMultiplayerActive())
                {
                    GUILayout.Space(8);
                    GUILayout.Label("マルチプレイ中のため、ゲームは止まりません。");
                }
                break;

            case Page.Settings:
                GUILayout.Label($"音量  {volume:0.00}");
                float newVol = GUILayout.HorizontalSlider(volume, 0f, 1f);
                if (!Mathf.Approximately(newVol, volume))
                {
                    volume = newVol;
                    AudioListener.volume = volume;
                    PlayerPrefs.SetFloat(SettingsManager.KEY_VOLUME, volume);
                }
                GUILayout.Space(14);
                GUILayout.Label($"マウス感度  {sensitivity:0.0}");
                float newSens = GUILayout.HorizontalSlider(sensitivity, 0.1f, 3f);
                if (!Mathf.Approximately(newSens, sensitivity))
                {
                    sensitivity = newSens;
                    PlayerPrefs.SetFloat(SettingsManager.KEY_SENSITIVITY, sensitivity);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("戻る", GUILayout.Height(34)))
                {
                    PlayerPrefs.Save();
                    page = Page.Main;
                }
                break;

            case Page.ConfirmTitle:
                GUILayout.Label("タイトルへ戻りますか？");
                GUILayout.Label("セーブされるのはステージクリア時のみです。\n今のステージの進行は失われます。");
                if (IsMultiplayerActive()) GUILayout.Label("部屋からも退出します。");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("タイトルへ戻る", GUILayout.Height(36))) ReturnToTitle();
                GUILayout.Space(6);
                if (GUILayout.Button("キャンセル", GUILayout.Height(34))) page = Page.Main;
                break;

            case Page.ConfirmQuit:
                GUILayout.Label("ゲームを終了しますか？");
                GUILayout.Label("セーブされるのはステージクリア時のみです。\n今のステージの進行は失われます。");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("ゲームを終了", GUILayout.Height(36))) QuitGame();
                GUILayout.Space(6);
                if (GUILayout.Button("キャンセル", GUILayout.Height(34))) page = Page.Main;
                break;
        }

        GUILayout.EndArea();
    }
}
