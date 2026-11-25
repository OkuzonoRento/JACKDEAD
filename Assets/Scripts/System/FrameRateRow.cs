using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 設定画面の「フレームレート」の行を動かす（30 / 60 / 120 を「&lt;」「&gt;」で切り替える）。
/// 行の部品は、エディタの KeyBindingsUIBuilder が、あらかじめシーンに置く（子の名前は Value / Prev / Next）。
/// タイトルの設定と、ゲーム中の設定の、どちらの行にも付く。
/// </summary>
public class FrameRateRow : MonoBehaviour
{
    private TMP_Text value;
    private bool bound;

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void Bind()
    {
        if (bound) return;
        bound = true;

        Transform v = transform.Find("Value");
        if (v != null) value = v.GetComponent<TMP_Text>();
        AddClick("Prev", -1);
        AddClick("Next", 1);
    }

    private void AddClick(string child, int step)
    {
        Transform t = transform.Find(child);
        Button b = t != null ? t.GetComponent<Button>() : null;
        if (b != null) b.onClick.AddListener(() => Change(step));
    }

    private void Change(int step)
    {
        int[] rates = SettingsManager.FrameRates;
        int index = System.Array.IndexOf(rates, SettingsManager.FrameRate);
        if (index < 0) index = System.Array.IndexOf(rates, SettingsManager.DefaultFrameRate);
        index = (index + step + rates.Length) % rates.Length;

        PlayerPrefs.SetInt(SettingsManager.KEY_FRAME_RATE, rates[index]);
        PlayerPrefs.Save();
        GameSettingsRuntime.ApplyFrameRate(rates[index]);
        Refresh();
    }

    private void Refresh()
    {
        if (value != null) value.text = SettingsManager.FrameRate + " fps";
    }
}
