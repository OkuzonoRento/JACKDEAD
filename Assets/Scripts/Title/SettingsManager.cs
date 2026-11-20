using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("スライダーUI（4項目）")]
    public Slider bgmSlider;
    public Slider seSlider;
    public Slider sensitivitySlider;
    public Slider brightnessSlider;

    [Header("右端の数値表示テキスト")]
    public TextMeshProUGUI bgmValueText;
    public TextMeshProUGUI seValueText;
    public TextMeshProUGUI sensitivityValueText;
    public TextMeshProUGUI brightnessValueText;

    // PlayerPrefs のキー（ゲーム中の Esc メニュー PauseMenu や視点操作 PlayerMovement からも使う）
    public const string KEY_VOLUME = "MasterVolume";      // ゲーム中の全体音量（Esc メニュー用）
    public const string KEY_SENSITIVITY = "MouseSensitivity";
    public const string KEY_BRIGHTNESS = "Brightness";

    /// <summary>マウス感度の標準値。この値のとき、視点操作の速さは PlayerMovement の設定どおりになる</summary>
    public const float DefaultSensitivity = 1f;

    // 他のスクリプト（BGM再生・SE再生）から現在の音量を取得するためのプロパティ
    public static float BgmVolume => PlayerPrefs.GetFloat("BGMVolume", 1f);
    public static float SeVolume => PlayerPrefs.GetFloat("SEVolume", 1f);
    public static float MouseSensitivity => PlayerPrefs.GetFloat(KEY_SENSITIVITY, DefaultSensitivity);
    public static float Brightness => PlayerPrefs.GetFloat(KEY_BRIGHTNESS, 0.5f);

    void Start()
    {
        float bgm = BgmVolume;
        float se = SeVolume;
        float sens = MouseSensitivity;
        float bright = Brightness;

        if (bgmSlider != null)
        {
            bgmSlider.value = bgm;
            bgmSlider.onValueChanged.AddListener(SetBGMVolume);
            SetBGMVolume(bgm);
        }

        if (seSlider != null)
        {
            seSlider.value = se;
            seSlider.onValueChanged.AddListener(SetSEVolume);
            SetSEVolume(se);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = sens;
            sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
            SetSensitivity(sens);
        }

        if (brightnessSlider != null)
        {
            brightnessSlider.value = bright;
            brightnessSlider.onValueChanged.AddListener(SetBrightness);
            SetBrightness(bright);
        }
    }

    public void SetBGMVolume(float value)
    {
        PlayerPrefs.SetFloat("BGMVolume", value);
        if (bgmValueText != null)
            bgmValueText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    public void SetSEVolume(float value)
    {
        PlayerPrefs.SetFloat("SEVolume", value);
        if (seValueText != null)
            seValueText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    public void SetSensitivity(float value)
    {
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, value);
        if (sensitivityValueText != null)
            sensitivityValueText.text = $"{value:F1}";
    }

    public void SetBrightness(float value)
    {
        PlayerPrefs.SetFloat(KEY_BRIGHTNESS, value);
        if (brightnessValueText != null)
            brightnessValueText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }
}
