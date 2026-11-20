using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    [Header("スライダーUI")]
    public Slider volumeSlider;
    public Slider sensitivitySlider;
    public Slider brightnessSlider;

    public const string KEY_VOLUME = "MasterVolume";
    public const string KEY_SENSITIVITY = "MouseSensitivity";
    public const string KEY_BRIGHTNESS = "Brightness";

    void Start()
    {
        float savedVol = PlayerPrefs.GetFloat(KEY_VOLUME, 1.0f);
        float savedSens = PlayerPrefs.GetFloat(KEY_SENSITIVITY, 2.0f);
        float savedBright = PlayerPrefs.GetFloat(KEY_BRIGHTNESS, 0.0f);

        if (volumeSlider != null)
        {
            volumeSlider.value = savedVol;
            SetVolume(savedVol);
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = savedSens;
            sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
        }

        if (brightnessSlider != null)
        {
            brightnessSlider.value = savedBright;
            brightnessSlider.onValueChanged.AddListener(SetBrightness);
        }
    }

    public void SetVolume(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(KEY_VOLUME, value);
        PlayerPrefs.Save();
    }

    public void SetSensitivity(float value)
    {
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, value);
        PlayerPrefs.Save();
    }

    public void SetBrightness(float value)
    {
        PlayerPrefs.SetFloat(KEY_BRIGHTNESS, value);
        PlayerPrefs.Save();
    }
}