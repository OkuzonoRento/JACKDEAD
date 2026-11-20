using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("�X���C�_�[UI�i4���ځj")]
    public Slider bgmSlider;
    public Slider seSlider;

    [Header("スライダーUI")]
    public Slider volumeSlider;
    public Slider sensitivitySlider;
    public Slider brightnessSlider;

    [Header("�E�[�̐��l�\���e�L�X�g")]
    public TextMeshProUGUI bgmValueText;
    public TextMeshProUGUI seValueText;
    public TextMeshProUGUI sensitivityValueText;
    public TextMeshProUGUI brightnessValueText;

    // ���̃X�N���v�g�iBGM�Đ���SE�Đ��j���猻�݂̉��ʂ��擾���邽�߂̃v���p�e�B
    public static float BgmVolume => PlayerPrefs.GetFloat("BGMVolume", 1f);
    public static float SeVolume => PlayerPrefs.GetFloat("SEVolume", 1f);
    public static float MouseSensitivity => PlayerPrefs.GetFloat("MouseSensitivity", 1f);
    public static float Brightness => PlayerPrefs.GetFloat("Brightness", 0.5f);

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
        PlayerPrefs.SetFloat("MouseSensitivity", value);
        if (sensitivityValueText != null)
            sensitivityValueText.text = $"{value:F1}";
    }

    public void SetBrightness(float value)
    {
        PlayerPrefs.SetFloat("Brightness", value);
        if (brightnessValueText != null)
            brightnessValueText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }
}