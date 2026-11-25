using UnityEngine;

/// <summary>
/// 設定値の保存キーと、現在値の取得。
/// 設定画面そのものは MultiLobbyUI.OpenSettings（タイトル）と PauseMenu（ゲーム中）が、シーンに置いた部品として持つ。
/// 値は PlayerPrefs に保存する。
/// </summary>
public class SettingsManager : MonoBehaviour // 部品としては使わない（シーンに残った古い参照でエラーにならないよう MonoBehaviour のままにしてある）
{
    // PlayerPrefs のキー
    public const string KEY_VOLUME = "MasterVolume";      // 全体音量
    public const string KEY_BGM = "BGMVolume";
    public const string KEY_VOICE = "VoiceVolume";         // ボイスチャットの音量（0〜2。1 が標準）
    public const string KEY_MIC_MODE = "MicMode";          // マイクの使い方（0 = 自動で拾う, 1 = プッシュトゥトーク）
    public const string KEY_SE = "SEVolume";
    public const string KEY_SENSITIVITY = "MouseSensitivity";
    public const string KEY_BRIGHTNESS = "Brightness";
    public const string KEY_FRAME_RATE = "FrameRate";     // フレームレートの上限（30 / 60 / 120）
    public const int DefaultFrameRate = 60;
    public static readonly int[] FrameRates = { 30, 60, 120 };

    /// <summary>マウス感度の標準値。この値のとき、視点操作の速さは PlayerMovement の設定どおりになる</summary>
    public const float DefaultSensitivity = 1f;

    /// <summary>明るさの標準値（0.5 のとき補正なし）</summary>
    public const float DefaultBrightness = 0.5f;

    public static float MasterVolume => PlayerPrefs.GetFloat(KEY_VOLUME, 1f);
    public static float BgmVolume => PlayerPrefs.GetFloat(KEY_BGM, 1f);
    public static float SeVolume => PlayerPrefs.GetFloat(KEY_SE, 1f);
    public static float VoiceVolume => PlayerPrefs.GetFloat(KEY_VOICE, 1f);
    public static int FrameRate => PlayerPrefs.GetInt(KEY_FRAME_RATE, DefaultFrameRate);
    /// <summary>マイクの使い方。false = 声を自動で拾う、true = 押している間だけ話す（プッシュトゥトーク）</summary>
    public static bool PushToTalk => PlayerPrefs.GetInt(KEY_MIC_MODE, 0) == 1;
    public static float MouseSensitivity => PlayerPrefs.GetFloat(KEY_SENSITIVITY, DefaultSensitivity);
    public static float Brightness => PlayerPrefs.GetFloat(KEY_BRIGHTNESS, DefaultBrightness);
}
